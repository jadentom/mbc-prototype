using MbcPrototype.TurnSystem;

namespace MbcPrototype.Core;

/// <summary>
/// The human-controlled chain: aiming, charging and firing are driven by input
/// in the GameManager, so this combatant only marks the rotation slot it owns.
/// Its root is the BaseNode already placed in the scene (see
/// GameManager.CreateCombatants()).
///
/// Keeping the player on the same <see cref="NodeChainCombatant"/> base as the
/// enemy is what makes the two "exactly the same": same nodes, same ammo, same
/// launch path, same destruction cascade, same defeat rule.
/// </summary>
public partial class PlayerCombatant : NodeChainCombatant
{
	public override bool IsPlayerControlled => true;

	/// <summary>Nothing to start: the GameManager re-selects a node and waits for input.</summary>
	public override void BeginTurn() { }
}
