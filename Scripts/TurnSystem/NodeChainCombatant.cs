using Godot;
using System.Collections.Generic;
using MbcPrototype.Core;
using MbcPrototype.Simulation;
using MbcPrototype.Units;

namespace MbcPrototype.TurnSystem;

/// <summary>
/// A combatant made of a chain of <see cref="Structure"/>s, exactly like the
/// player: it owns the structures it launched, every newly deployed one hangs
/// from the structure that fired it by a <see cref="Cord"/>, and it is defeated
/// when its chain is wiped out.
///
/// Ownership is inherited: a projectile fired by one of this combatant's
/// structures deploys a structure that belongs to the same combatant (see
/// Projectile.Deploy), which is what keeps several chains apart on the same
/// field.
/// </summary>
public abstract partial class NodeChainCombatant : Combatant
{
	/// <summary>Scene used for this combatant's starting root structure.</summary>
	[Export] public PackedScene StructureScene = GD.Load<PackedScene>("res://Scenes/structure.tscn");

	/// <summary>
	/// The roster entry this combatant's chain starts from — the Hub, today —
	/// used for a root this combatant spawns itself (and as the fallback for one
	/// authored in a scene without a definition of its own). Set by the match
	/// before the root is created; docs/parity M0.4 takes it from the map's start
	/// positions instead.
	/// </summary>
	[Export] public UnitDefinition RootUnit;

	/// <summary>
	/// Where this combatant's spawned structures are parented: the match's
	/// structure container, set by the setup before a root is spawned (docs/parity
	/// M0.6 — the combatant is told where the match lives rather than asking a
	/// singleton).
	/// </summary>
	public Node3D StructureContainer { get; set; }

	/// <summary>The structure the whole chain hangs from; null until the root is in place.</summary>
	public Structure RootStructure { get; private set; }

	private readonly List<Structure> _structures = new List<Structure>();

	/// <summary>Every structure of this combatant's chain, live or not yet freed.</summary>
	public IReadOnlyList<Structure> Structures => _structures;

	/// <summary>
	/// The structure this combatant acts from by default: the top of the chain,
	/// i.e. the surviving structure with no live cord above it. Also used as the
	/// defeat check — destroying a root cascades down its cords to everything the
	/// root anchors, so no root means no chain.
	/// </summary>
	public virtual Structure ActionStructure => FindRootStructure();

	/// <summary>True once this combatant's chain has no root left to act from.</summary>
	public override bool IsDefeated => FindRootStructure() == null;

	/// <summary>
	/// Spawns this combatant's starting root structure at <paramref name="position"/>
	/// and returns it. Ownership and the unit definition are set before the
	/// structure enters the tree so its _Ready registers it with this chain and
	/// gives it the right hit points.
	/// </summary>
	public Structure SpawnRoot(Vector3 position)
	{
		if (StructureScene == null)
		{
			GD.PushError($"{GetType().Name} '{Name}': no StructureScene assigned, cannot spawn a root.");
			return null;
		}

		Structure root = StructureScene.Instantiate<Structure>();
		root.OwnerChain = this;
		root.Definition = RootUnit;
		root.Position = position;
		RootStructure = root;

		// Parented under the GameManager's structure container (see
		// GameManager.StructureContainer) instead of the tree root, so a combatant
		// can spawn its root during scene setup. Falls back to the tree root.
		Node parent = StructureContainer ?? GameManager.Instance?.StructureContainer;
		if (parent == null) parent = GetTree().Root;
		parent.AddChild(root);
		return root;
	}

	public void RegisterStructure(Structure structure)
	{
		if (structure != null && !_structures.Contains(structure))
		{
			_structures.Add(structure);
		}
	}

	public void UnregisterStructure(Structure structure)
	{
		_structures.Remove(structure);
	}

	/// <summary>
	/// Returns the chain's root: the surviving structure with no live cord above
	/// it. Returns null when nothing of this chain is left.
	///
	/// The cord is what decides this, not the structure's parent reference: the
	/// link is the cord (docs/parity M0.3), so a structure whose cord has gone —
	/// or whose parent has been destroyed out from under it — is a root again.
	/// That is what makes "no root left" mean "no chain left" while a destruction
	/// cascade is still running.
	/// </summary>
	public Structure FindRootStructure()
	{
		foreach (Structure structure in _structures)
		{
			if (structure == null || !IsInstanceValid(structure) || structure.IsDestroyed) continue;

			Cord cord = structure.ParentCord;
			bool hangsFromSomething = cord != null && IsInstanceValid(cord) && cord.IsIntact;
			if (!hangsFromSomething)
			{
				return structure;
			}
		}
		return null;
	}

	/// <summary>
	/// Launches one unit from <paramref name="origin"/>, by way of the command log:
	/// an AI's action is recorded exactly like the player's, so a match can be
	/// replayed from the log alone (docs/parity M0.5). The command is applied
	/// immediately, and registers the TurnEvent that keeps the turn open until the
	/// shot resolves.
	/// </summary>
	protected void Launch(UnitDefinition unit, Structure origin, float aimAngleRadians, float powerPercent)
	{
		GameManager gm = GameManager.Instance;
		if (gm == null || unit == null || origin == null) return;

		gm.SubmitCommand(MatchCommand.Launch(Seat, unit.Id, origin.Id, aimAngleRadians, powerPercent));
	}
}
