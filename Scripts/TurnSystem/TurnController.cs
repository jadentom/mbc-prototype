using Godot;
using System.Collections.Generic;

namespace MbcPrototype.TurnSystem;

/// <summary>
/// The turn flow (docs/parity M0.6, M1): whose turn it is, when a turn is
/// committed, when its events have resolved, when the round rolls over, and when
/// the match is over. It owns the rotation, the pending <see cref="TurnEvent"/>s
/// and the round counter.
///
/// **A turn is an energy budget** (docs/parity M1). One seat acts at a time, and
/// within its turn it may build as many units as its energy pays for; the turn
/// ends when the seat **skips** (which carries its unspent energy forward) or
/// when nothing is left to spend on. A **round** is one turn from every seat
/// still in the match; when the last one ends the round does too — energy is
/// paid out, and the order **rotates left**, so the seat that went first this
/// round goes last next round.
///
/// It decides nothing about *how* a seat acts. It announces that a turn has
/// started (<see cref="TurnStartedEventHandler"/>) and, after a shot resolves,
/// that the same seat may act again (<see cref="TurnResumedEventHandler"/>);
/// whoever listens takes the turn — player input, an AI combatant, or a recorded
/// command log being replayed. That separation is what lets a replay drive both
/// seats without the turn system knowing replays exist, and what lets a turn
/// hold several launches without the rotation knowing what a launch is.
/// </summary>
public partial class TurnController : Node
{
	/// <summary>
	/// Raised when a combatant's turn begins. Whoever listens takes the turn: this
	/// node has already made it the active combatant, and will not move on until
	/// the turn is ended (a skip, or nothing left to build) — a shot resolving
	/// only produces <see cref="TurnResumedEventHandler"/>.
	/// </summary>
	[Signal] public delegate void TurnStartedEventHandler(Combatant combatant);

	/// <summary>
	/// Raised when the events a turn registered have all resolved: the shot has
	/// landed, the damage is done, and the **same** seat may act again from its
	/// remaining energy. It is not a new turn.
	/// </summary>
	[Signal] public delegate void TurnResumedEventHandler(Combatant combatant);

	/// <summary>
	/// Raised when the first event of a turn is registered — control is locked from
	/// here until the turn's events all resolve. The HUD and the player's aiming
	/// state listen for this to cancel a charge in progress.
	/// </summary>
	[Signal] public delegate void TurnCommittedEventHandler();

	/// <summary>Raised when a combatant's turn is over, however it ended.</summary>
	[Signal] public delegate void TurnEndedEventHandler(Combatant combatant);

	/// <summary>
	/// Raised when every seat has had its turn and the round is ending, with the
	/// round that just finished. Income is paid out now — the match listens and
	/// pays it, because the economy is the match's and not the rotation's — and
	/// then the order rotates left and the next round begins.
	/// </summary>
	[Signal] public delegate void RoundEndedEventHandler(int round);

	/// <summary>Raised once the match is over, with the outcome for the human's team.</summary>
	[Signal] public delegate void MatchEndedEventHandler(bool playerWon);

	private readonly List<Combatant> _combatants = new List<Combatant>();
	private readonly List<TurnEvent> _pendingTurnEvents = new List<TurnEvent>();

	/// <summary>
	/// The seats in the order they act this round: every combatant that is still
	/// in the match, starting with the one whose turn to go first it is, and
	/// rotated left by one seat each round (docs/parity M1).
	/// </summary>
	private readonly List<Combatant> _roundOrder = new List<Combatant>();

	private int _roundCursor = -1;

	// A turn advance waiting for idle time (see QueueAdvance).
	private bool _advanceQueued;

	// A courtesy of the same kind: the "may act again" announcement is deferred
	// too (see QueueResume), because an event can resolve inside _ExitTree.
	private bool _resumeQueued;

	/// <summary>Every combatant in the rotation, in seat order.</summary>
	public IReadOnlyList<Combatant> Combatants => _combatants;

	/// <summary>The order the seats act in this round, for the HUD.</summary>
	public IReadOnlyList<Combatant> RoundOrder => _roundOrder;

	/// <summary>The combatant whose turn it currently is (null between turns).</summary>
	public Combatant ActiveCombatant { get; private set; }

	/// <summary>Current round number. Starts at 1 and advances when every seat has acted.</summary>
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
	/// of the first round takes its turn.
	/// </summary>
	public void Start(IReadOnlyList<Combatant> combatants)
	{
		_combatants.Clear();
		if (combatants != null) _combatants.AddRange(combatants);
		_pendingTurnEvents.Clear();
		_roundOrder.Clear();
		_resumeQueued = false;
		_advanceQueued = false;

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
		ActiveCombatant = null;
		_roundCursor = -1;

		BuildRoundOrder();
		if (_roundOrder.Count == 0)
		{
			EvaluateOutcome();
			return;
		}

		_roundCursor = 0;
		StartTurn(_roundOrder[0]);
	}

	/// <summary>
	/// Registers an event that must resolve before control returns to the seat
	/// that is acting. Registering the first event commits the turn and takes
	/// control away from the player; control returns — to the same seat — when the
	/// last pending event resolves.
	/// </summary>
	public void RegisterEvent(TurnEvent evt)
	{
		if (evt == null || evt.IsResolved || IsOver) return;

		if (!IsResolving)
		{
			// Committing the turn: lock player control immediately.
			IsResolving = true;
			IsPlayerTurn = false;
			EmitSignal(SignalName.TurnCommitted);

			GD.Print($"[Round {Round}] Turn committed — control locked.");
		}

		_pendingTurnEvents.Add(evt);
		evt.Resolved += () => OnEventResolved(evt);
		GD.Print($"[Round {Round}] Event started: {evt.Description}");
	}

	/// <summary>
	/// Ends the acting seat's turn: it skips whatever is left of it and its
	/// unspent energy is carried forward (docs/parity M1). Called by
	/// <see cref="MbcPrototype.Core.GameManager.ApplyCommand"/> when a
	/// <see cref="MbcPrototype.Simulation.CommandKind.Skip"/> command is applied,
	/// never directly by input — the log has to know the turn ended.
	///
	/// Does nothing while a shot is still resolving or after the match is over.
	/// </summary>
	public void EndTurn()
	{
		if (IsOver || IsResolving || ActiveCombatant == null) return;

		EndTurnNow();
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

	private void EndTurnNow()
	{
		IsPlayerTurn = false;
		Combatant ended = ActiveCombatant;
		ActiveCombatant = null;
		ended?.EndTurn();
		GD.Print($"[Round {Round}] {ended?.DisplayName ?? "?"}: turn over.");
		EmitSignal(SignalName.TurnEnded, ended);

		if (EvaluateOutcome()) return;

		QueueAdvance();
	}

	private void OnEventResolved(TurnEvent evt)
	{
		if (!_pendingTurnEvents.Remove(evt)) return;
		GD.Print($"[Round {Round}] Event resolved: {evt.Description}");

		if (_pendingTurnEvents.Count > 0 || !IsResolving) return;

		IsResolving = false;
		GD.Print($"[Round {Round}] Resolution complete.");

		if (EvaluateOutcome()) return;

		// The seat may have lost its chain during its own turn (its own blast,
		// most often): it is in no position to act again.
		if (ActiveCombatant == null || ActiveCombatant.IsDefeated)
		{
			EndTurnNow();
			return;
		}

		// The turn is not over: this is the budget model, so control returns to
		// the same seat with whatever energy it has left (docs/parity M1). The
		// announcement is deferred for the same reason the advance is: a
		// destruction event resolves inside _ExitTree, and a replay - which acts
		// the moment it is told to - would try to spawn a shot into the tree that
		// is still being freed.
		QueueResume();
	}

	/// <summary>
	/// Tells the acting seat it may act again, at idle time. See <see cref="QueueAdvance"/>
	/// for why the turn flow never calls back into the match from inside a
	/// node's teardown.
	/// </summary>
	private void QueueResume()
	{
		if (_resumeQueued) return;

		_resumeQueued = true;
		Callable.From(ResumeTurnDeferred).CallDeferred();
	}

	private void ResumeTurnDeferred()
	{
		_resumeQueued = false;

		// A new event may have been registered between the drain and this frame
		// (a cascade registering the next generation): then the turn is resolving
		// again, and its own drain will queue this.
		if (IsOver || IsResolving || ActiveCombatant == null) return;

		// The seat may have lost its chain in the meantime; it cannot act again.
		if (ActiveCombatant.IsDefeated)
		{
			EndTurnNow();
			return;
		}

		IsPlayerTurn = ActiveCombatant.IsPlayerControlled;
		GD.Print($"[Round {Round}] {ActiveCombatant.DisplayName}: may act again.");
		EmitSignal(SignalName.TurnResumed, ActiveCombatant);
	}

	/// <summary>
	/// Defers the rotation by a frame rather than advancing on the spot.
	///
	/// A turn can end from inside a node's <c>_ExitTree</c>: a structure's
	/// destruction event resolves there, and it is often the last event of the
	/// turn. The scene tree is locked while nodes are being removed, so anything
	/// the next turn spawns — an AI's shot, a replayed command, anything that
	/// adds a child — fails with "parent node is busy setting up children",
	/// leaving a turn whose event can never resolve. Deferring costs one frame
	/// and makes the rotation safe from wherever it was triggered.
	/// </summary>
	private void QueueAdvance()
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
	/// Begins <paramref name="combatant"/>'s turn and announces it. This node does
	/// not act for a seat: the listener does — input for the human, the combatant's
	/// own brain for an AI, a recorded command for a replay.
	/// </summary>
	private void StartTurn(Combatant combatant)
	{
		ActiveCombatant = combatant;
		if (combatant == null) return;

		IsPlayerTurn = combatant.IsPlayerControlled;
		GD.Print($"[Round {Round}] {combatant.DisplayName}: turn.");
		EmitSignal(SignalName.TurnStarted, combatant);
	}

	/// <summary>
	/// Moves to the next seat of this round, or ends the round when this was the
	/// last one.
	/// </summary>
	private void AdvanceTurn()
	{
		_roundCursor++;
		if (_roundCursor >= 0 && _roundCursor < _roundOrder.Count)
		{
			StartTurn(_roundOrder[_roundCursor]);
			return;
		}

		EndRound();
	}

	/// <summary>
	/// Every seat has had its turn: the round ends, the match pays out income
	/// (which it does on hearing <see cref="RoundEndedEventHandler"/>), the order
	/// rotates left, and the next round's first seat takes its turn.
	/// </summary>
	private void EndRound()
	{
		int finished = Round;
		GD.Print($"[Round {finished}] Every seat has had its turn.");

		// Income is the match's to pay; this is the announcement, not the payout.
		EmitSignal(SignalName.RoundEnded, finished);

		if (IsOver) return;

		Round++;
		BuildRoundOrder();
		_roundCursor = 0;

		if (_roundOrder.Count == 0)
		{
			EvaluateOutcome();
			return;
		}

		GD.Print($"[Round {Round}] First to act: {_roundOrder[0].DisplayName} (order rotates left each round).");
		StartTurn(_roundOrder[0]);
	}

	/// <summary>
	/// Rebuilds the order the seats act in for the round starting now: the seat
	/// after the one that went first last round goes first this round, and any
	/// combatant that has been defeated is dropped (docs/parity M1 — "player order
	/// rotates left every round").
	/// </summary>
	private void BuildRoundOrder()
	{
		_roundOrder.Clear();
		int count = _combatants.Count;
		if (count == 0) return;

		int first = (Round - 1) % count;
		for (int step = 0; step < count; step++)
		{
			Combatant combatant = _combatants[(first + step) % count];
			if (combatant == null || combatant.IsDefeated) continue;
			_roundOrder.Add(combatant);
		}
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
