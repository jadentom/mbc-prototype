using Godot;
using MbcPrototype.Rules;

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
///
/// It also holds the combatant's **energy**: the budget a turn is spent from
/// (docs/parity M1). The amount that comes back each round is the chain's
/// business — a <see cref="NodeChainCombatant"/> earns it from its Hub and
/// Harvesters — but the purse, the cap and what a build takes out of it are the
/// same for every kind of seat, so they live here.
/// </summary>
public abstract partial class Combatant : Node
{
	/// <summary>Name used in logs and in the turn label.</summary>
	[Export] public string DisplayName = "Combatant";

	/// <summary>
	/// This combatant's seat in the rotation — its position, not its team: two
	/// combatants on one team hold two seats. The command log names the seat that
	/// acted, so a replay can tell whose turn a recorded action belonged to
	/// (docs/parity M0.5).
	/// </summary>
	[Export] public int Seat = -1;

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
	/// The rules this combatant's energy economy runs on (docs/parity §7): the
	/// cap and the starting purse come from here, so changing either is an edit
	/// in one resource and not a rebuild. Handed in by
	/// <see cref="MbcPrototype.Core.MatchSetup"/>, which is where the match's
	/// rules are resolved.
	/// </summary>
	public MatchRules Rules { get; set; } = MatchRules.Fallback;

	/// <summary>
	/// Energy this combatant may still spend. It starts at the rules' starting
	/// purse, every build takes its unit's <see cref="MbcPrototype.Units.UnitDefinition.Level"/>
	/// out of it, and the end of a round adds the income (capped, so unspent
	/// energy carries up to the cap).
	/// </summary>
	public int Energy { get; private set; }

	/// <summary>Energy this combatant spent during the round in progress (for the round ledger).</summary>
	public int EnergySpentThisRound { get; private set; }

	/// <summary>Most energy this combatant may hold.</summary>
	public int MaxEnergy => Rules?.EffectiveMaxStoredEnergy ?? MatchRules.FallbackMaxStoredEnergy;

	/// <summary>
	/// Seeds the purse at the rules' starting energy. Called once by the match
	/// setup, before the first turn: it is the one energy change that is not
	/// income and not a build.
	/// </summary>
	public void BeginMatch()
	{
		Energy = Mathf.Clamp(Rules?.EffectiveStartingEnergy ?? 0, 0, MaxEnergy);
		EnergySpentThisRound = 0;
	}

	/// <summary>True when this combatant can pay <paramref name="cost"/> right now.</summary>
	public bool CanAfford(int cost)
	{
		return cost > 0 && Energy >= cost;
	}

	/// <summary>
	/// Takes the cost of a build out of the purse. Returns false and changes
	/// nothing when it cannot be paid, so a caller can try another unit instead
	/// — the turn is only over when nothing is affordable.
	/// </summary>
	public bool SpendEnergy(int cost)
	{
		if (!CanAfford(cost)) return false;

		Energy -= cost;
		EnergySpentThisRound += cost;
		return true;
	}

	/// <summary>
	/// Pays income at the end of a round, up to the cap. Returns how much of it
	/// was actually banked: energy already at the cap is lost, which is what
	/// makes the cap mean something.
	/// </summary>
	public int GrantIncome(int income)
	{
		if (income <= 0) return 0;

		int room = Mathf.Max(0, MaxEnergy - Energy);
		int granted = Mathf.Min(income, room);
		Energy += granted;
		return granted;
	}

	/// <summary>Starts a fresh spending tally for the round that is beginning.</summary>
	public void ResetRoundSpending()
	{
		EnergySpentThisRound = 0;
	}

	/// <summary>
	/// Called by the GameManager when this combatant's turn begins. A
	/// player-controlled combatant just waits for input; an AI combatant
	/// starts acting here (and must eventually submit a build, or skip the
	/// rest of the turn, or both).
	/// </summary>
	public abstract void BeginTurn();

	/// <summary>
	/// Called once this combatant's turn is over — either because it spent out
	/// or skipped, or because the match ended. Used by AI combatants to abandon
	/// whatever they were doing.
	/// </summary>
	public virtual void EndTurn() { }
}
