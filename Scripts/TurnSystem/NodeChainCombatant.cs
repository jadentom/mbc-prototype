using Godot;
using System.Collections.Generic;
using MbcPrototype.Combat;
using MbcPrototype.Core;

namespace MbcPrototype.TurnSystem;

/// <summary>
/// A combatant made of a chain of <see cref="BaseNode"/>s, exactly like the
/// player: it owns the nodes it launched, links every newly deployed node to
/// the one that fired it, and is defeated when its chain is wiped out.
///
/// Ownership is inherited: a projectile fired by one of this combatant's nodes
/// deploys a node that belongs to the same combatant (see Projectile.Deploy),
/// which is what keeps several chains apart on the same field.
/// </summary>
public abstract partial class NodeChainCombatant : Combatant
{
	/// <summary>Scene used for this combatant's starting root node.</summary>
	[Export] public PackedScene NodeScene = GD.Load<PackedScene>("res://Scenes/base_node.tscn");

	/// <summary>The node the whole chain hangs from; null until the root is spawned.</summary>
	public BaseNode RootNode { get; private set; }

	private readonly List<BaseNode> _nodes = new List<BaseNode>();

	/// <summary>Every node of this combatant's chain, live or not yet freed.</summary>
	public IReadOnlyList<BaseNode> Nodes => _nodes;

	/// <summary>
	/// The node this combatant acts from by default: the top of the chain, i.e.
	/// the surviving node with no valid parent. Also used as the defeat check —
	/// destroying a root cascades through every child, so no root means no chain.
	/// </summary>
	public virtual BaseNode ActionNode => FindHighestRemainingNode();

	/// <summary>True once this combatant's chain has no root left to act from.</summary>
	public override bool IsDefeated => FindHighestRemainingNode() == null;

	/// <summary>
	/// Adopts an already existing node (the player's starting BaseNode, which
	/// is placed in the scene) as this combatant's root.
	/// </summary>
	public void AttachRoot(BaseNode root)
	{
		if (root == null) return;

		RootNode = root;
		root.OwnerChain = this;
		// BaseNode._Ready registers itself too; RegisterNode is idempotent.
		RegisterNode(root);
	}

	/// <summary>
	/// Spawns this combatant's starting root node at <paramref name="position"/>
	/// and returns it. Ownership is set before the node enters the tree so its
	/// _Ready registers it with this chain.
	/// </summary>
	public BaseNode SpawnRoot(Vector3 position)
	{
		if (NodeScene == null)
		{
			GD.PushError($"{GetType().Name} '{Name}': no NodeScene assigned, cannot spawn a root.");
			return null;
		}

		BaseNode root = NodeScene.Instantiate<BaseNode>();
		root.OwnerChain = this;
		root.Position = position;
		RootNode = root;

		// Parented under the GameManager's node container (see
		// GameManager.NodeContainer) instead of the tree root, so a combatant can
		// spawn its root during scene setup. Falls back to the tree root.
		Node parent = GameManager.Instance?.NodeContainer;
		if (parent == null) parent = GetTree().Root;
		parent.AddChild(root);
		return root;
	}

	public void RegisterNode(BaseNode node)
	{
		if (node != null && !_nodes.Contains(node))
		{
			_nodes.Add(node);
		}
	}

	public void UnregisterNode(BaseNode node)
	{
		_nodes.Remove(node);
	}

	/// <summary>
	/// Returns the topmost surviving node of the chain — the root, i.e. a node
	/// with no valid parent. Returns null when no nodes remain.
	/// </summary>
	public BaseNode FindHighestRemainingNode()
	{
		foreach (BaseNode node in _nodes)
		{
			if (node == null || !IsInstanceValid(node) || node.IsDestroyed) continue;

			bool hasValidParent = node.ParentBase != null
				&& IsInstanceValid(node.ParentBase)
				&& !node.ParentBase.IsDestroyed;
			if (!hasValidParent)
			{
				return node;
			}
		}
		return null;
	}

	/// <summary>
	/// Launches one piece of ammo from <paramref name="origin"/>. Goes through
	/// the exact code path the player's shots use, and registers the TurnEvent
	/// that keeps the turn open until the shot resolves.
	/// </summary>
	protected void Launch(AmmoType ammo, BaseNode origin, float aimAngleRadians, float powerPercent)
	{
		AmmoLauncher.Launch(ammo, origin, aimAngleRadians, powerPercent, $"{DisplayName} {ammo}");
	}
}
