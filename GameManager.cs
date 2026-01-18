using Godot;
using System;

public enum AmmoType { Node, Bomb }

public partial class GameManager : Node
{
	public static GameManager Instance;

	[ExportGroup("References")]
	[Export] public Camera3D MainCamera;
	[Export] public ProgressBar PowerBar;

	[ExportGroup("Aiming Settings")]
	[Export] public float RotationSpeed = 3.0f;
	[Export] public float UpwardBias = 5f;

	[ExportGroup("Power Settings")]
	[Export] public float MinLaunchForce = 4.0f;
	[Export] public float MaxLaunchForce = 20.0f;
	[Export] public float ChargeSpeed = 1.0f; // 1.0 = fills in 1 second

	[ExportGroup("Camera Settings")]
	[Export] public float CameraSmoothTime = 0.5f;
	
	[ExportGroup("UI References")]
	[Export] public TextureRect NodeIcon;
	[Export] public TextureRect BombIcon;
	[Export] public ColorRect SelectorBox;

	[ExportGroup("Ammo Prefabs")]
	[Export] public PackedScene ProjectileScene; // TODO: Change this to an interface; this is specifically a deploying node projectile
	[Export] public PackedScene BombScene;

	public BaseNode SelectedNode;
	
	private Vector3 _cameraOffset;
	private float _currentAimAngle = 0f;
	private MeshInstance3D _aimIndicator;
	private float _currentPowerPercent = 0f;
	private bool _isCharging = false;
	private bool _isChargingUp = true;
	private AmmoType _currentAmmo = AmmoType.Node;

	public override void _Ready()
	{
		Instance = this;
		
		// 1. Setup Camera Offset
		if (MainCamera != null)
		{
			var startBase = GetTree().Root.FindChild("BaseNode", true, false) as Node3D;
			if (startBase != null)
				_cameraOffset = MainCamera.GlobalPosition - startBase.GlobalPosition;
			else
				_cameraOffset = new Vector3(0, 15, 15); // Standard top-down angle
		}

		CreateAimIndicator();
		UpdateSelectedAmmo();
		if (PowerBar != null) PowerBar.Visible = false;
	}

	private void UpdateSelectedAmmo()
	{
		if (SelectorBox == null) return;

		// Determine which icon to highlight
		TextureRect targetIcon = (_currentAmmo == AmmoType.Node) ? NodeIcon : BombIcon;

		// Smoothly move the selector box to the icon's position
		Tween tween = GetTree().CreateTween();
		tween.SetTrans(Tween.TransitionType.Back);
		tween.SetEase(Tween.EaseType.Out);
		
		// We target the global_position of the icon
		tween.TweenProperty(SelectorBox, "global_position", targetIcon.GlobalPosition, 0.2f);
	}

	private void CreateAimIndicator()
	{
		_aimIndicator = new MeshInstance3D();
		
		// Create a long thin box that looks like a pointer
		var mesh = new BoxMesh { Size = new Vector3(0.2f, 0.2f, 2.0f) };
		_aimIndicator.Mesh = mesh;
		
		// Bright yellow material so it stands out
		var mat = new StandardMaterial3D { 
			AlbedoColor = new Color(1, 1, 0), 
			ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded 
		};
		_aimIndicator.MaterialOverride = mat;
		
		_aimIndicator.Visible = false;
		AddChild(_aimIndicator);
	}

	public void SelectNode(BaseNode node)
	{
		SelectedNode?.SetHighlight(false);
		SelectedNode = node;
		SelectedNode.SetHighlight(true);
		
		// Show indicator and move camera
		_aimIndicator.Visible = true;
		CenterCameraOn(node.GlobalPosition);

		// Cancel charging if we switch nodes
		_isCharging = false;
		_currentPowerPercent = 0f;
		if (PowerBar != null) PowerBar.Visible = false;
	}

	public override void _Process(double delta)
	{
		if (SelectedNode == null) return;

		HandleAiming((float)delta);
		HandleFiringLogic((float)delta);
	}

	private void HandleAiming(float delta)
	{
		float input = Input.GetAxis("aim_left", "aim_right");
		_currentAimAngle += input * RotationSpeed * delta;

		// Switch Ammo Type with Q or E (same for now because 1 ammo type)
		if (Input.IsActionJustPressed("switch_ammo_next") || Input.IsActionJustPressed("switch_ammo_previous"))
		{
			_currentAmmo = _currentAmmo == AmmoType.Node ? AmmoType.Bomb : AmmoType.Node;
			GD.Print("Switched to: " + _currentAmmo);
			UpdateSelectedAmmo();
		}

		_aimIndicator.GlobalPosition = SelectedNode.GlobalPosition + Vector3.Up * 1.0f;
		_aimIndicator.Rotation = new Vector3(0, _currentAimAngle, 0);
		_aimIndicator.GlobalPosition += _aimIndicator.GlobalTransform.Basis.Z * 1.5f;
	}

	private void HandleFiringLogic(float delta)
	{
		// 1. Start Charging
		if (Input.IsActionJustPressed("fire_shot"))
		{
			_isCharging = true;
			_currentPowerPercent = 0f;
			if (PowerBar != null) PowerBar.Visible = true;
		}

		// 2. While Holding
		if (_isCharging && Input.IsActionPressed("fire_shot"))
		{
			var effectiveChargeSpeed = _isChargingUp ? ChargeSpeed : -1f * ChargeSpeed;
			_currentPowerPercent += effectiveChargeSpeed * delta;
			_currentPowerPercent = Mathf.Clamp(_currentPowerPercent, 0f, 1f);
			if (_currentPowerPercent == 1f)
			{
				_isChargingUp = false;
			}
			else if (_currentPowerPercent == 0f)
			{
				_isChargingUp = true;
			}
			
			if (PowerBar != null) PowerBar.Value = _currentPowerPercent;

			// Optional: Scale the indicator arrow to show power visually in 3D
			_aimIndicator.Scale = new Vector3(1, 1, 1 + (_currentPowerPercent * 2f));
		}

		// 3. Release and Fire
		if (_isCharging && Input.IsActionJustReleased("fire_shot"))
		{
			_isChargingUp = true;
			float finalForce = Mathf.Lerp(MinLaunchForce, MaxLaunchForce, _currentPowerPercent);
			Fire(finalForce);
			
			// Reset
			_isCharging = false;
			_currentPowerPercent = 0f;
			if (PowerBar != null) PowerBar.Visible = false;
			_aimIndicator.Scale = Vector3.One;
		}
	}

	private void Fire(float force)
	{
		// Choose which scene to instantiate
		PackedScene sceneToSpawn = (_currentAmmo == AmmoType.Node) ? ProjectileScene : BombScene;
		
		var instance = sceneToSpawn.Instantiate<Node3D>();
		instance.GlobalPosition = SelectedNode.GlobalPosition + Vector3.Up * 2.0f;
		
		Vector3 launchDirection = _aimIndicator.GlobalTransform.Basis.Z.Normalized();
		Vector3 velocity = (launchDirection * force) + (Vector3.Up * UpwardBias);

		// TODO: Switch this to inheritance and use an interface
		if (instance is Projectile p)
		{
			p.Velocity = velocity;
			p.CreatorNode = SelectedNode;
		}
		else if (instance is Bomb b)
		{
			b.Velocity = velocity;
		}
		
		GetTree().Root.AddChild(instance);
	}

	private void CenterCameraOn(Vector3 targetPosition)
	{
		if (MainCamera == null) return;

		// Calculate destination based on ground position to keep height consistent
		Vector3 groundLevel = new Vector3(targetPosition.X, 0, targetPosition.Z);
		Vector3 destination = groundLevel + _cameraOffset;

		Tween tween = GetTree().CreateTween();
		tween.SetTrans(Tween.TransitionType.Expo); 
		tween.SetEase(Tween.EaseType.Out);
		
		tween.TweenProperty(MainCamera, "global_position", destination, CameraSmoothTime);
		
		// Optional: Ensure camera doesn't accidentally rotate
		tween.Parallel().TweenProperty(MainCamera, "global_rotation", MainCamera.GlobalRotation, CameraSmoothTime);
	}
}
