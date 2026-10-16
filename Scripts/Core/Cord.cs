using Godot;

namespace MbcPrototype.Core;

/// <summary>
/// The link in the network: the geometry between two <see cref="Structure"/>s —
/// the one it hangs from and the one it anchors — plus the cable drawn between
/// them.
///
/// Before M0.3 a structure *was* its link. <c>BaseNode</c> held hit points, the
/// parent pointer, the list of children and the cable all at once, which is why
/// "the chain" could only ever be an ad-hoc parent pointer: there was no object
/// to hang the network's rules on. A cord is that object. Build legality, cord
/// nodes, the destruction cascade, lineage lights and Power Spike travel are all
/// cord operations, and they now have somewhere to live.
///
/// A cord is parented under the structure that hangs from it, so it lives and
/// dies with that structure: cut a structure down and its cord goes with it.
/// Its geometry is two endpoints and a length today; the cord nodes that sit at
/// intervals along it, and everything M2 does with them, come with M2.
/// </summary>
public partial class Cord : Node3D
{
	/// <summary>
	/// Cable thickness, in world units. Ten centimetres against a structure two
	/// units across: thick enough to read as a link, thin enough not to hide the
	/// ground marker of a blast underneath it.
	/// </summary>
	private const float CableThickness = 0.1f;

	/// <summary>The structure this cord hangs from — nearer the network's origin.</summary>
	public Structure ParentStructure { get; private set; }

	/// <summary>The structure this cord anchors — the one built along it.</summary>
	public Structure ChildStructure { get; private set; }

	/// <summary>
	/// Distance between the two structures, in world units. Zero until the cable
	/// is built (see <see cref="BuildCable"/>), because the endpoints only have
	/// positions once both are in the tree.
	/// </summary>
	public float Length { get; private set; }

	/// <summary>True while both ends are still standing, so the cord is still a link.</summary>
	public bool IsIntact => IsLive(ParentStructure) && IsLive(ChildStructure);

	/// <summary>Where the cord starts, at the structure it hangs from.</summary>
	public Vector3 ParentPoint => IsLive(ParentStructure) ? ParentStructure.GlobalPosition : GlobalPosition;

	/// <summary>Where the cord ends, at the structure it anchors.</summary>
	public Vector3 ChildPoint => IsLive(ChildStructure) ? ChildStructure.GlobalPosition : GlobalPosition;

	/// <summary>
	/// Connects two structures with a cord and returns it: the child is parented
	/// the cord, the parent is told it anchors one, and the cable is built.
	///
	/// Both structures must be in the tree (the cable is measured between their
	/// world positions), which is why this runs from the child's
	/// <see cref="Structure._Ready"/> and not from the shot that deployed it.
	/// </summary>
	/// <param name="parent">The structure the new one was launched from.</param>
	/// <param name="child">The structure that hangs from it.</param>
	public static Cord Connect(Structure parent, Structure child)
	{
		if (!IsLive(parent) || child == null) return null;

		var cord = new Cord
		{
			Name = $"Cord_{child.Name}",
			ParentStructure = parent,
			ChildStructure = child,
		};

		// Under the child: the cord's lifetime is the link's lifetime, and a
		// structure cut down takes the cord that anchors it with it.
		child.AddChild(cord);
		cord.BuildCable();
		parent.AddCord(cord);
		return cord;
	}

	public override void _ExitTree()
	{
		// The link is gone, so the structure it hung from no longer anchors it.
		// The parent may already be gone itself during a scene teardown, and the
		// cascade clears its list anyway; both are why this is guarded.
		if (IsLive(ParentStructure))
		{
			ParentStructure.RemoveCord(this);
		}
		base._ExitTree();
	}

	/// <summary>
	/// Builds the cable that draws this cord: a box running from the child to the
	/// parent, subdivided along its length so the cable shader's traffic has
	/// something to travel over.
	/// </summary>
	private void BuildCable()
	{
		if (!IsLive(ParentStructure) || !IsLive(ChildStructure)) return;

		Length = ChildPoint.DistanceTo(ParentPoint);

		var mesh = new BoxMesh
		{
			Size = new Vector3(CableThickness, CableThickness, Length),
			SubdivideDepth = (int)Mathf.Max(10, Length * 2),
		};
		var cable = new MeshInstance3D { Mesh = mesh };

		var material = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://Shaders/CableShader.gdshader"),
		};
		// Without the length in the shader the cable's flow is drawn "middle-out"
		// once the box is subdivided.
		material.SetShaderParameter("cable_length", Length);
		cable.MaterialOverride = material;

		AddChild(cable);

		// The box's +Z axis runs from this cord's origin (the child structure)
		// toward the parent, so it is centred half a length along that axis.
		cable.Position = Vector3.Zero;
		cable.LookAt(ParentPoint);
		cable.Position = -cable.Transform.Basis.Z * (Length / 2.0f);
	}

	private static bool IsLive(Structure structure)
	{
		return structure != null && GodotObject.IsInstanceValid(structure) && !structure.IsDestroyed;
	}
}
