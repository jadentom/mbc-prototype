using Godot;

namespace MbcPrototype.TurnSystem;

/// <summary>
/// One participant in the turn rotation. The GameManager only ever talks to
/// this interface, so a new enemy, a second AI opponent, or an entirely
/// different kind of opponent (something that is not built out of a node
/// chain) only has to derive from Combatant and be added to the rotation.
///
/// Concrete combatants decide how they act: <see cref="NodeChainCombatant"/>
/// covers anything built from a chain of nodes, which today is both the player
/// and the enemy.
/// </summary>
public abstract partial class Combatant : Node
{
	/// <summary>Name used in logs and in the turn label.</summary>
	[Export] public string DisplayName = "Combatant";

	/// <summary>
	/// Combatants on the same team never target each other and never fight:
	/// the match ends as soon as one team has no combatant left that can act.
	/// </summary>
	[Export] public int Team = 0;

	/// <summary>Color of this combatant's nodes on the minimap.</summary>
	[Export] public Color MinimapColor = new Color(0, 0, 0, 0.9f);

	/// <summary>True for the human-controlled combatant.</summary>
	public abstract bool IsPlayerControlled { get; }

	/// <summary>
	/// True when this combatant can no longer act at all. Checked before every
	/// turn and after every resolution — a defeated combatant is skipped by the
	/// rotation and loses the match for its team.
	/// </summary>
	public abstract bool IsDefeated { get; }

	/// <summary>
	/// Called by the GameManager when this combatant's turn begins. A
	/// player-controlled combatant just waits for input; an AI combatant
	/// starts acting here (and must eventually register a TurnEvent, or call
	/// GameManager.EndIdleTurn() if it cannot act).
	/// </summary>
	public abstract void BeginTurn();

	/// <summary>
	/// Called once this combatant's turn is over — either because its events
	/// resolved or because the match ended. Used by AI combatants to abandon
	/// whatever they were doing.
	/// </summary>
	public virtual void EndTurn() { }
}
