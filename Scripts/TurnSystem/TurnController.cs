using Godot;
using System.Collections.Generic;

namespace MbcPrototype.TurnSystem;

/// <summary>
/// The turn flow (docs/parity M1): whose turn it is, when a turn is
/// committed, when its events have resolved, when the round rolls over, and when
/// the match is over. It owns the rotation, the pending <see cref="TurnEvent"/>s
/// and the two counters the HUD reads — the round and the turn inside it.
///
/// **A round and a turn are different things.** A **turn** is exactly one
/// launch: the seat picks a unit, fires it, its event resolves, and the turn is
/// over. A **round** is the seats taking turns in order — the human, then the
/// enemy, then the human again — until every seat has either spent out or
/// skipped; at the round's end the seats are paid their income and the round
/// advances, with the order **rotated left** so the seat that went first goes
/// last next round (docs/parity M1).
///
/// A seat that **skips** forfeits the rest of the round: the energy it did not
/// spend carries forward, but it takes no further turns until the next round.
/// That is the same thing as being spent out — a purse that cannot pay for any
/// unit in the roster skips itself — so a round always ends when every seat is
/// out of turns, never on a timer.
///
/// It decides nothing about *how* a seat acts. It announces that a turn has
/// started (<see cref="TurnStartedEventHandler"/>) and lets whoever listens take
/// it — player input, an AI combatant, or a recorded command log being replayed.
/// That separation is what lets a replay drive both seats without the turn system
/// knowing replays exist, and what keeps a turn one launch without the rotation
/// knowing what a launch is.
/// </summary>
public partial class TurnController : Node
{
	/// <summary>
	/// Raised when a combatant's turn begins: it is the active combatant, and the
	/// turn lasts until one launch of its resolves or it skips. Whoever listens
	/// takes the turn — this node will not move on by itself.
	/// </summary>
	[Signal] public delegate void TurnStartedEventHandler(Combatant combatant);

	/// <summary>
	/// Raised when a turn's launch is registered — control is locked from here
	/// until its events all resolve. The HUD and the player's aiming state listen
	/// for this to cancel a charge in progress.
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
	/// The seats that still owe a turn this round, in the order they will take
	/// them: the front is the seat acting now (or next), and a seat that finishes a
	/// launch goes to the back. A seat that skips, or is defeated, leaves it — which
	/// is how a round ends: when nothing is left in the queue. Rotating the order at
	/// the round's start is what makes the seat that went first go last next round.
	/// </summary>
	private readonly List<Combatant> _roundOrder = new List<Combatant>();

	// A turn advance waiting for idle time (see QueueAdvance).
	private bool _advanceQueued;

	/// <summary>Every combatant in the rotation, in seat order.</summary>
	public IReadOnlyList<Combatant> Combatants => _combatants;

	/// <summary>
	/// The seats still owed a turn this round, in order, the acting one first. For
	/// the HUD's order display and for anyone asking "who is left to act".
	/// </summary>
	public IReadOnlyList<Combatant> RoundOrder => _roundOrder;

	/// <summary>The combatant whose turn it currently is (null between turns).</summary>
	public Combatant ActiveCombatant { get; private set; }

	/// <summary>
	/// The round being played: one turn per seat until every seat is out of turns.
	/// Energy is paid out at its end (see <see cref="RoundEndedEventHandler"/>), and
	/// it is the number the HUD shows next to the turn.
	/// </summary>
	public int Round { get; private set; } = 1;

	/// <summary>
	/// Which turn of this round is being played, counting from 1. Turns alternate
	/// inside a round, so this is what tells the player how far through the round
	/// they are — <see cref="Round"/> alone cannot, because a round holds as many
	/// turns as the seats' energy pays for.
	/// </summary>
	public int TurnNumber { get; private set; }

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
		TurnNumber = 0;
		IsOver = false;
		IsResolving = false;
		IsPlayerTurn = false;
		ActiveCombatant = null;

		BuildRoundOrder();
		if (_roundOrder.Count == 0)
		{
			EvaluateOutcome();
			return;
		}

		StartTurn(_roundOrder[0]);
	}

	/// <summary>
	/// Registers the event a turn's launch creates. Registering the first one
	/// commits the turn and takes control away from the player; when the last
	/// pending event resolves the turn is over and the next seat takes it.
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

			GD.Print($"[Round {Round} · Turn {TurnNumber}] Committed — control locked.");
		}

		_pendingTurnEvents.Add(evt);
		evt.Resolved += () => OnEventResolved(evt);
		GD.Print($"[Round {Round} · Turn {TurnNumber}] Event started: {evt.Description}");
	}

	/// <summary>
	/// Ends the acting seat's turn by **skipping**: it forfeits the rest of the
	/// round and its unspent energy carries forward (docs/parity M1). Called by
	/// <see cref="MbcPrototype.Core.GameManager.ApplyCommand"/> when a
	/// <see cref="MbcPrototype.Simulation.CommandKind.Skip"/> command is applied,
	/// never directly by input — the log has to know the turn ended.
	///
	/// Does nothing while a shot is still resolving or after the match is over.
	/// </summary>
	public void EndTurn()
	{
		if (IsOver || IsResolving || ActiveCombatant == null) return;

		EndTurnNow(forfeitsRound: true);
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

	/// <summary>
	/// Ends the acting seat's turn. A seat that launched leaves the front of the
	/// round's order for the back — it is not done for the round, it simply takes
	/// its next turn after everyone else's. A seat that skipped, or that has been
	/// defeated, leaves the order for good, and the round is over when nobody is
	/// left in it.
	/// </summary>
	private void EndTurnNow(bool forfeitsRound)
	{
		IsPlayerTurn = false;
		Combatant ended = ActiveCombatant;
		ActiveCombatant = null;
		ended?.EndTurn();

		_roundOrder.Remove(ended);
		bool finished = forfeitsRound || ended == null || ended.IsDefeated;
		if (!finished) _roundOrder.Add(ended);

		GD.Print($"[Round {Round} · Turn {TurnNumber}] {ended?.DisplayName ?? "?"}: turn over"
			+ (finished ? " — done for this round." : "."));
		EmitSignal(SignalName.TurnEnded, ended);

		if (EvaluateOutcome()) return;

		QueueAdvance();
	}

	private void OnEventResolved(TurnEvent evt)
	{
		if (!_pendingTurnEvents.Remove(evt)) return;
		GD.Print($"[Round {Round} · Turn {TurnNumber}] Event resolved: {evt.Description}");

		if (_pendingTurnEvents.Count > 0 || !IsResolving) return;

		IsResolving = false;
		GD.Print($"[Round {Round} · Turn {TurnNumber}] Resolution complete.");

		if (EvaluateOutcome()) return;

		// A turn is one launch: the shot has landed and done its damage, so this
		// seat has had its turn. It keeps whatever energy is left for its next one,
		// and the next seat acts now.
		EndTurnNow(forfeitsRound: false);
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
	/// Gives the turn to the seat at the front of the round's order, dropping
	/// anything that can no longer act, and ends the round when nothing is left.
	/// </summary>
	private void AdvanceTurn()
	{
		for (int i = 0; i < _roundOrder.Count; i++)
		{
			if (_roundOrder[i] != null && !_roundOrder[i].IsDefeated) continue;
			_roundOrder.RemoveAt(i);
			i--;
		}

		if (_roundOrder.Count == 0)
		{
			EndRound();
			return;
		}

		StartTurn(_roundOrder[0]);
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

		TurnNumber++;
		IsPlayerTurn = combatant.IsPlayerControlled;
		GD.Print($"[Round {Round} · Turn {TurnNumber}] {combatant.DisplayName}: turn.");
		EmitSignal(SignalName.TurnStarted, combatant);
	}

	/// <summary>
	/// Every seat has had its turn: the round ends, the match pays out income
	/// (which it does on hearing <see cref="RoundEndedEventHandler"/>), the order
	/// rotates left, and the next round's first seat takes its turn.
	/// </summary>
	private void EndRound()
	{
		int finished = Round;
		GD.Print($"[Round {finished}] Every seat is out of turns — the round ends.");

		// Income is the match's to pay; this is the announcement, not the payout.
		EmitSignal(SignalName.RoundEnded, finished);

		if (IsOver) return;

		Round++;
		TurnNumber = 0;
		BuildRoundOrder();

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
