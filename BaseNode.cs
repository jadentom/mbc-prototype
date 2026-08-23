using Godot;
using System.Collections.Generic;

public partial class BaseNode : StaticBody3D
{
	[Export] public PackedScene ExplosionScene;

	public BaseNode ParentBase;
	public List<BaseNode> Children = new List<BaseNode>();

	private MeshInstance3D _highlight;
	private ProgressBar _healthBar;
	private SubViewport _viewport;

	/// <summary>
	/// True once this node has been marked for destruction. Remains true until
	/// the node actually leaves the tree (QueueFree defers the removal to the
	/// end of the frame), so callers can reliably tell a dying node from a
	/// living one even before it is freed.
	/// </summary>
	public bool IsDestroyed { get; private set; } = false;

	/// <summary>
	/// Resolved once this node has actually left the tree after destruction.
	/// Keeps the turn resolution running until the whole destruction cascade
	/// (this node and every descendant) has finished.
	/// </summary>
	private TurnEvent _destructionEvent;

	public int Health = 3;
	private int _maxHealth = 3;

	public override void _Ready()
	{
		_highlight = GetNode<MeshInstance3D>("HighlightRing");
		_highlight.Visible = false;

		// In 3D, we use _InputEvent for mouse clicks on objects
		InputEvent += OnInput;

		// If we have a parent, create the visual connection
		if (ParentBase != null)
		{
			CreateCable();
		}

		// Health Bar
		var healthBarScene = GD.Load<PackedScene>("res://health_bar.tscn");
		var healthBarInstance = healthBarScene.Instantiate<Control>();

		_viewport = new SubViewport();
		_viewport.Size = new Vector2I(80, 10);
		_viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		_viewport.TransparentBg = true;
		_viewport.AddChild(healthBarInstance);
		AddChild(_viewport);

		var sprite = new Sprite3D();
		sprite.Texture = _viewport.GetTexture();
		sprite.Position = new Vector3(0, 2, 0);
		sprite.Scale = new Vector3(3, 3, 1);
		sprite.Centered = true;
		sprite.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
		AddChild(sprite);

		_healthBar = healthBarInstance.GetNode<ProgressBar>("ProgressBar");
		UpdateHealthBar();

		GameManager.Instance?.RegisterNode(this);
	}

	public override void _ExitTree()
	{
		// Resolve the destruction event only once this node is truly out of
		// the tree, so the turn doesn't end while the cascade is still running.
		if (_destructionEvent != null)
		{
			_destructionEvent.MarkResolved();
			_destructionEvent = null;
		}

		GameManager.Instance?.UnregisterNode(this);
		base._ExitTree();
	}

	private void CreateCable()
	{
		if (ParentBase == null) return;

		MeshInstance3D cable = new MeshInstance3D();
		BoxMesh mesh = new BoxMesh();

		float distance = GlobalPosition.DistanceTo(ParentBase.GlobalPosition);
		mesh.Size = new Vector3(0.1f, 0.1f, distance);
		mesh.SubdivideDepth = (int)Mathf.Max(10, distance * 2);
		cable.Mesh = mesh;

		ShaderMaterial mat = new ShaderMaterial();
		mat.Shader = GD.Load<Shader>("res://CableShader.gdshader");

		// This is the critical line that fixes the "middle-out" issue
		mat.SetShaderParameter("cable_length", distance);

		cable.MaterialOverride = mat;
		AddChild(cable);

		// Position and Orientation
		cable.Position = Vector3.Zero;
		cable.LookAt(ParentBase.GlobalPosition);

		// Move the box so its start sits at the child node
		cable.Position = -cable.Transform.Basis.Z * (distance / 2.0f);
	}

	private void OnInput(Node camera, InputEvent @event, Vector3 position, Vector3 normal, long shapeIdx)
	{
		if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
		{
			GameManager.Instance?.SelectNode(this);
		}
	}

	public void SetHighlight(bool state) => _highlight.Visible = state;

	public void TakeDamage(int amount)
	{
		Health -= amount;
		UpdateHealthBar();
		if (Health <= 0)
		{
			Destroy();
		}
	}

	public async void Destroy()
	{
		if (IsDestroyed) return;
		IsDestroyed = true;

		// This node's destruction (and the cascade it triggers) must resolve
		// before the current turn can end. Resolved in _ExitTree, once the
		// node has actually left the tree.
		_destructionEvent = new TurnEvent("Destroy " + Name);
		GameManager.Instance?.RegisterTurnEvent(_destructionEvent);

		// Instantiate explosion immediately
		if (ExplosionScene != null)
		{
			var explosion = ExplosionScene.Instantiate<Node3D>();
			GetTree().Root.AddChild(explosion);
			explosion.GlobalPosition = this.GlobalPosition;
		}

		// Delay
		await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);

		// Propagate destruction to children
		foreach (BaseNode child in Children)
		{
			if (IsInstanceValid(child)) // Check if child is still valid before destroying
			{
				child.Destroy(); // Recursively destroy children
			}
		}
		Children.Clear(); // Clear the list after triggering destruction

		// Actual removal (the destruction event resolves in _ExitTree)
		QueueFree();
	}

	private void UpdateHealthBar()
	{
		if (_healthBar != null)
		{
			_healthBar.Value = (float)Health / _maxHealth * 100;
		}
	}
}
