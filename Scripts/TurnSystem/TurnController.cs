using Godot;
using System.Collections.Generic;

namespace MbcPrototype.TurnSystem;

/// <summary>
/// The turn flow (docs/parity M0.6): whose turn it is, when a turn is committed,
/// when its events have resolved, when the round rolls over, and when the match is
/// over. It owns the rotation, the pending <see cref="TurnEvent"/>s and the round
/// counter.
///
/// It decides nothing about *how* a seat acts. It announces that a turn has
/// started (<see cref="TurnStartedEventHandler"/>) and whoever listens takes the
/// turn — player input, an AI combatant, or a recorded command log being replayed.
/// That separation is what lets a replay drive both seats without the turn system
/// knowing replays exist.
/// </summary>
public partial class TurnController : Node
{
	/// <summary>
	/// Raised when a combatant's turn begins. Whoever listens takes the turn: this
	/// node has already made it the active combatant and cleared the player-control
	/// flag, and will not advance again until that turn's events resolve.
	/// </summary>
	[Signal] public delegate void TurnStartedEventHandler(Combatant combatant);

	/// <summary>
	/// Raised when the first event of a turn commits it — control is locked from
	/// here until the turn's events all resolve. The HUD and the player's aiming
	/// state listen for this to cancel a charge in progress.
	/// </summary>
	[Signal] public delegate void TurnCommittedEventHandler();

	/// <summary>Raised once the match is over, with the outcome for the human's team.</summary>
	[Signal] public delegate void MatchEndedEventHandler(bool playerWon);

	private readonly List<Combatant> _combatants = new List<Combatant>();
	private readonly List<TurnEvent> _pendingTurnEvents = new List<TurnEvent>();
	private int _activeIndex = -1;

	// A turn advance waiting for idle time (see QueueTurnAdvance).
	private bool _advanceQueued;

	/// <summary>Every combatant in the rotation, in turn order.</summary>
	public IReadOnlyList<Combatant> Combatants => _combatants;

	/// <summary>The combatant whose turn it currently is (null before the match starts).</summary>
	public Combatant ActiveCombatant { get; private set; }

	/// <summary>Current round number. Starts at 1 and advances when the rotation wraps.</summary>
	public int Round { get; private set; } = 1;

	/// <summary>True while a turn's events are still resolving, so control is locked.</summary>
	public bool IsResolving { get; private set; }

	/// <summary>True while the human may aim, charge, select structures and fire.</summary>
	public bool IsPlayerTurn { get; set; }

	/// <summary>True once the match has been won or lost; the scene reloads on input.</summary>
	public bool IsOver { get; private set; }

	/// <summary>
	/// The team the human plays on, taken from the player-controlled combatant when
	/// the match starts: victory is "only this team is left", defeat is "it is gone"
	/// (docs/parity §1 — the check stays team-aware even though today every team is
	/// one combatant).
	/// </summary>
	public int PlayerTeam { get; private set; } = -1;

	/// <summary>
	/// Starts the rotation: the match hands over its combatants and the first seat
	/// takes its turn.
	/// </summary>
	public void Start(IReadOnlyList<Combatant> combatants)
	{
		_combatants.Clear();
		if (combatants != null) _combatants.AddRange(combatants);

		PlayerTeam = -1;
		foreach (Combatant combatant in _combatants)
		{
			if (combatant != null && combatant.IsPlayerControlled)
			{
				PlayerTeam = combatant.Team;
				break;
			}
		}

		Round = 1;
		IsOver = false;
		IsResolving = false;
		IsPlayerTurn = false;
		_activeIndex = -1;

		StartFirstTurn();
	}

	/// <summary>
	/// Registers an event that must resolve before the current turn ends.
	/// Registering the first event commits the turn and takes control away from the
	/// player; control returns when the last pending event resolves.
	/// </summary>
	public void RegisterEvent(TurnEvent evt)
	{
		if (evt == null || evt.IsResolved) return;

		if (!IsResolving)
		{
			// Committing the turn: lock player control immediately.
			IsResolving = true;
			IsPlayerTurn = false;
			EmitSignal(SignalName.TurnCommitted);

			GD.Print($"[Turn {Round}] Turn committed — control locked.");
		}

		_pendingTurnEvents.Add(evt);
		evt.Resolved += () => OnEventResolved(evt);
		GD.Print($"[Turn {Round}] Event started: {evt.Description}");
	}

	/// <summary>
	/// Ends the active turn for a combatant that spent its whole turn without
	/// registering a single TurnEvent (for example a shot that could not be
	/// launched). Without this the rotation would wait forever for events that are
	/// never coming. Does nothing while events are still resolving.
	/// </summary>
	public void EndIdleTurn()
	{
		if (IsResolving || IsOver) return;
		CompleteTurn();
	}

	/// <summary>
	/// Ends the match: the rotation stops and <see cref="MatchEndedEventHandler"/>
	/// tells the match to show the outcome. Defeat and victory are different states,
	/// not different mechanics.
	/// </summary>
	public void EndMatch(bool playerWon)
	{
		if (IsOver) return;

		IsOver = true;
		IsPlayerTurn = false;
		IsResolving = false;
		ActiveCombatant = null;

		EmitSignal(SignalName.MatchEnded, playerWon);
	}

	private void OnEventResolved(TurnEvent evt)
	{
		if (!_pendingTurnEvents.Remove(evt)) return;
		GD.Print($"[Turn {Round}] Event resolved: {evt.Description}");

		if (_pendingTurnEvents.Count == 0 && IsResolving)
		{
			CompleteTurn();
		}
	}

	/// <summary>
	/// Every pending event has finished: the acting combatant's turn is over. The
	/// match is checked for a winner, and otherwise the turn passes to the next
	/// combatant that can still act.
	/// </summary>
	private void CompleteTurn()
	{
		IsResolving = false;
		ActiveCombatant?.EndTurn();

		GD.Print($"[Turn {Round}] Resolution complete.");

		// A chain whose root was destroyed cascades through every child, so a
		// combatant with no root left loses the match for its team.
		if (EvaluateOutcome()) return;

		QueueTurnAdvance();
	}

	/// <summary>
	/// Advances the rotation at idle time rather than on the spot.
	///
	/// A turn can end from inside a node's <c>_ExitTree</c>: a structure's
	/// destruction event resolves there, and it is often the last event of the turn.
	/// The scene tree is locked while nodes are being removed, so anything the next
	/// turn spawns — an AI's shot, a replayed command, anything that adds a child —
	/// fails with "parent node is busy setting up children", leaving a turn whose
	/// event can never resolve. Deferring costs one frame and makes the rotation safe
	/// from wherever it was triggered.
	/// </summary>
	private void QueueTurnAdvance()
	{
		if (_advanceQueued) return;

		_advanceQueued = true;
		Callable.From(AdvanceTurnDeferred).CallDeferred();
	}

	private void AdvanceTurnDeferred()
	{
		_advanceQueued = false;
		if (IsOver) return;
		AdvanceTurn();
	}

	/// <summary>
	/// Hands the first turn of the match to the first combatant in the rotation.
	/// </summary>
	private void StartFirstTurn()
	{
		if (_combatants.Count == 0) return;

		_activeIndex = 0;
		Round = 1;
		StartTurn(_combatants[_activeIndex]);
	}

	/// <summary>
	/// Begins <paramref name="combatant"/>'s turn and announces it. This node does
	/// not act for a seat: the listener does — input for the human, the combatant's
	/// own brain for an AI, a recorded command for a replay.
	/// </summary>
	private void StartTurn(Combatant combatant)
	{
		ActiveCombatant = combatant;
		if (combatant == null) return;

		IsPlayerTurn = combatant.IsPlayerControlled;
		EmitSignal(SignalName.TurnStarted, combatant);
	}

	/// <summary>
	/// Passes the turn to the next combatant that can still act, skipping defeated
	/// ones, and counts a round each time the rotation wraps around.
	/// </summary>
	private void AdvanceTurn()
	{
		int count = _combatants.Count;
		if (count == 0) return;

		bool roundCounted = false;
		for (int step = 1; step <= count; step++)
		{
			int next = (_activeIndex + step) % count;

			// Wrapped past the end of the rotation: one full round has passed.
			if (next <= _activeIndex && !roundCounted)
			{
				Round++;
				roundCounted = true;
			}

			if (_combatants[next].IsDefeated) continue;

			_activeIndex = next;
			StartTurn(_combatants[next]);
			return;
		}

		// Every combatant is defeated; EvaluateOutcome() should have ended the match
		// before this could happen.
	}

	/// <summary>
	/// Ends the match once a whole team has no combatant left that can act: defeat
	/// when the player's team is gone, victory when every other team is (destroying
	/// a chain's root takes its whole chain with it). Returns true when the match
	/// ended.
	/// </summary>
	private bool EvaluateOutcome()
	{
		if (_combatants.Count == 0 || PlayerTeam < 0) return false;

		bool playerAlive = false;
		bool othersAlive = false;

		foreach (Combatant combatant in _combatants)
		{
			if (combatant.IsDefeated) continue;
			if (combatant.Team == PlayerTeam) playerAlive = true;
			else othersAlive = true;
		}

		if (playerAlive && othersAlive) return false;

		EndMatch(playerAlive && !othersAlive);
		return true;
	}
}
