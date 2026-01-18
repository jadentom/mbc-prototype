using Godot;
using System.Collections.Generic;

public partial class BaseNode : StaticBody3D
{
	public BaseNode ParentBase;
	public List<BaseNode> Children = new List<BaseNode>();
	
	private MeshInstance3D _highlight;

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
			GameManager.Instance.SelectNode(this);
		}
	}

	public void SetHighlight(bool state) => _highlight.Visible = state;
}
