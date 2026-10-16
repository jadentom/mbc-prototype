using Godot;
using System.Collections.Generic;
using MbcPrototype.Core;
using MbcPrototype.Simulation;
using MbcPrototype.TurnSystem;
using MbcPrototype.Units;

namespace MbcPrototype.Testing;

/// <summary>
/// The headless match harness (docs/parity §6): plays a real match with nobody at
/// the keyboard, records its command log, and prints a hash of the state it ended
/// in. Run it twice and the hashes must match; replay a recorded log and the hash
/// must match again. That is the regression suite for every rule the game has —
/// "change a damage value, replay, compare" (§2) — and the reason the command log
/// exists at all (M0.5).
///
/// It is written in C# rather than as a throwaway GDScript probe on purpose: the
/// harness has to read the simulation's own state (combatants, structures, the
/// log), and those are C# types.
///
/// Usage (`--` separates Godot's own arguments from the harness's):
///
/// <code>
/// godot --headless --path . Scenes/harness.tscn -- --turns=4 --seed=12345 --record=/tmp/run.json
/// godot --headless --path . Scenes/harness.tscn -- --replay=/tmp/run.json --expect=&lt;hash&gt;
/// </code>
///
/// Options: <c>--turns</c> (rounds to play, default 4, and a cap when replaying),
/// <c>--seed</c> (default 12345), <c>--record</c> (write the log here),
/// <c>--replay</c> (play this log), <c>--expect</c> (fail unless the final hash is
/// this one), <c>--quiet</c>.
///
/// A replay stops when its log runs out — the log is the whole of what happened —
/// so replaying a log and recording the same match end at the same place, and the
/// two hashes are comparable.
///
/// The harness is the *player's* brain: it fires from the structure nearest the
/// enemy, aiming at it, alternating through the roster — a policy that reads the
/// state, which is itself a function of the log, so the match it plays is
/// reproducible.
/// </summary>
public partial class MatchHarness : Node
{
	private const string MatchScenePath = "res://Scenes/main_scene.tscn";

	private int _turns = 4;
	private int _seed = 12345;
	private string _recordPath;
	private string _replayPath;
	private string _expectedHash;
	private bool _quiet;

	private GameManager _match;
	private bool _finished;
	private int _shots;

	// Stall detection: a match that stops making progress — a turn event that never
	// resolves, an ammo round that never lands — must fail the test rather than hang
	// it, and say what it was waiting for.
	private double _sinceProgress;
	private string _lastProgress = "";
	private const double StallSeconds = 30.0;

	// A replay ends when its log does: the log is the whole of what happened, and
	// running on past it would compare a match against something longer than itself.
	private bool _wasReplaying;

	public override void _Ready()
	{
		if (!ParseArguments()) return;

		var matchScene = GD.Load<PackedScene>(MatchScenePath);
		if (matchScene == null)
		{
			Fail($"{MatchScenePath} did not load.");
			return;
		}

		CommandLog replayLog = null;
		if (!string.IsNullOrEmpty(_replayPath))
		{
			replayLog = CommandLog.Load(_replayPath, out string error);
			if (replayLog == null)
			{
				Fail($"cannot replay '{_replayPath}': {error}.");
				return;
			}
			_seed = replayLog.Seed;
		}

		// Instantiated before entering the tree so the seed is in place when the
		// match builds itself: the generator, the log and (later) the map all read
		// it during the match's own _Ready.
		Node world = matchScene.Instantiate();
		_match = world.GetNodeOrNull<GameManager>("GameManager");
		if (_match == null)
		{
			Fail($"{MatchScenePath} has no GameManager node.");
			return;
		}
		_match.MatchSeed = _seed;

		AddChild(world);

		// Before the first turn starts (the match defers it by a frame).
		if (replayLog != null)
		{
			_match.StartReplay(replayLog);
			_wasReplaying = _match.IsReplaying;
		}

		if (!_quiet) GD.Print($"[Harness] seed {_seed}, map '{_match.Map?.Id}', playing {_turns} round(s)"
			+ (replayLog != null ? $", replaying {replayLog.Count} command(s)" : ", recording"));
	}

	public override void _Process(double delta)
	{
		if (_finished || _match == null || GameManager.Instance == null) return;

		if (CheckForStall(delta)) return;

		// Stop between turns, while nothing is resolving: a cascade in flight is a
		// state two runs would not catch at the same moment.
		bool replayEnded = _wasReplaying && !_match.IsReplaying;
		_wasReplaying = _match.IsReplaying;
		if (_match.IsGameOver || ((replayEnded || _match.CurrentTurn > _turns) && !_match.IsResolvingTurn))
		{
			Finish();
			return;
		}

		// The log drives both seats during a replay; nothing to do here.
		if (_match.IsReplaying) return;

		if (_match.IsPlayerTurn && !_match.IsResolvingTurn && _match.ActiveCombatant != null)
		{
			SubmitPlayerAction();
		}
	}

	/// <summary>
	/// Fails the run when the match stops moving: the same round, the same command
	/// count and the same structure count for <see cref="StallSeconds"/> of
	/// simulated time means something is waiting for an event that will never come —
	/// and a hung test is worse than a failing one.
	/// </summary>
	private bool CheckForStall(double delta)
	{
		// Progress is what the *match* did, not what the engine did: a stalled match
		// keeps taking physics steps forever.
		string progress = $"{_match.CurrentTurn}/{_match.Commands.Count}/{_match.LiveStructureCount}";
		if (progress != _lastProgress)
		{
			_lastProgress = progress;
			_sinceProgress = 0.0;
			return false;
		}

		_sinceProgress += delta;
		if (_sinceProgress < StallSeconds) return false;

		GD.PrintErr($"[Harness] stalled at turn {_match.CurrentTurn} for {_sinceProgress:0} s of simulated time "
			+ $"({_match.SimulationTicks} physics steps; resolving: {_match.IsResolvingTurn}, "
			+ $"replaying: {_match.IsReplaying}). Still in the world:");
		DumpSpawnedNodes();
		Fail("the match stopped making progress.");
		return true;
	}

	private static string Format(Vector3 v)
	{
		return $"({v.X:0.##}, {v.Y:0.##}, {v.Z:0.##})";
	}

	/// <summary>Prints everything the match has spawned that is still in the tree.</summary>
	private void DumpSpawnedNodes()
	{
		Node container = _match.SpawnContainer;
		if (container == null) return;

		foreach (Node child in container.GetChildren())
		{
			string motion = child is CharacterBody3D body
				? $" position {Format(body.GlobalPosition)} velocity {Format(body.Velocity)}"
				: "";
			bool alive = child is not Structure structure || !structure.IsDestroyed;
			GD.PrintErr($"  - {child.Name} ({child.GetType().Name}){motion}{(alive ? "" : " [destroyed]")}");
		}
	}

	/// <summary>
	/// The harness's policy for the human's seat: fire the next unit in the roster
	/// from the structure closest to the enemy, aimed at it, at a fixed charge. A
	/// fixed policy is the point — the match has to be the same match twice.
	/// </summary>
	private void SubmitPlayerAction()
	{
		Combatant seat = _match.ActiveCombatant;
		IReadOnlyList<UnitDefinition> buildable = _match.Catalog?.Buildable;
		if (buildable == null || buildable.Count == 0)
		{
			_match.EndIdleTurn();
			return;
		}

		Structure origin = NearestOwnStructure(seat, out Structure target);
		if (origin == null || target == null)
		{
			_match.EndIdleTurn();
			return;
		}

		Vector3 delta = target.GlobalPosition - origin.GlobalPosition;
		float aim = Mathf.Atan2(delta.X, delta.Z);
		UnitDefinition unit = buildable[_shots % buildable.Count];
		_shots++;

		_match.SubmitCommand(MatchCommand.Launch(seat.Seat, unit.Id, origin.Id, aim, PlayerPower));
	}

	/// <summary>The charge the harness's player fires at. Short of full: a flat arc that lands.</summary>
	private const float PlayerPower = 0.6f;

	/// <summary>Own structure closest to the enemy, and the enemy structure it is closest to.</summary>
	private Structure NearestOwnStructure(Combatant seat, out Structure target)
	{
		target = null;
		Structure origin = null;
		float best = float.MaxValue;

		// The enemy first: the seat's own structures are then judged against it.
		Structure enemy = null;
		float nearestEnemy = float.MaxValue;
		foreach (Combatant combatant in _match.Combatants)
		{
			if (combatant == seat || combatant.Team == seat.Team) continue;
			if (combatant is not NodeChainCombatant chain) continue;

			foreach (Structure structure in chain.Structures)
			{
				if (!IsLive(structure)) continue;
				float distance = structure.GlobalPosition.DistanceSquaredTo(seat is NodeChainCombatant own && own.RootStructure != null
					? own.RootStructure.GlobalPosition
					: Vector3.Zero);
				if (distance < nearestEnemy)
				{
					nearestEnemy = distance;
					enemy = structure;
				}
			}
		}
		if (enemy == null) return null;
		target = enemy;

		if (seat is not NodeChainCombatant ours) return null;
		foreach (Structure structure in ours.Structures)
		{
			if (!IsLive(structure)) continue;
			float distance = structure.GlobalPosition.DistanceSquaredTo(enemy.GlobalPosition);
			if (distance < best)
			{
				best = distance;
				origin = structure;
			}
		}
		return origin;
	}

	private static bool IsLive(Structure structure)
	{
		return structure != null && GodotObject.IsInstanceValid(structure) && !structure.IsDestroyed;
	}

	private void Finish()
	{
		_finished = true;

		string hash = _match.StateHash();
		if (!_quiet)
		{
			GD.Print($"[Harness] ended at turn {_match.CurrentTurn}"
				+ (_match.IsGameOver ? " (match over)" : "")
				+ $", {_match.Commands.Count} command(s) logged, {_match.Combatants.Count} combatant(s)");
			GD.Print($"[Harness] STATE HASH {hash}");
		}
		else
		{
			GD.Print(hash);
		}

		if (!string.IsNullOrEmpty(_recordPath) && !SaveLog(_recordPath)) return;

		if (!string.IsNullOrEmpty(_expectedHash) && !string.Equals(_expectedHash, hash, System.StringComparison.OrdinalIgnoreCase))
		{
			Fail($"expected state hash {_expectedHash}, got {hash}.");
			return;
		}

		GetTree().Quit(0);
	}

	private bool SaveLog(string path)
	{
		string directory = path.GetBaseDir();
		if (!string.IsNullOrEmpty(directory)) DirAccess.MakeDirRecursiveAbsolute(directory);

		if (_match.Commands.Save(path) != Error.Ok) return false;

		if (!_quiet) GD.Print($"[Harness] log written to {path} ({_match.Commands.Describe()})");
		return true;
	}

	private bool ParseArguments()
	{
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			string[] parts = argument.Split('=', 2);
			string name = parts[0].TrimStart('-');
			string value = parts.Length > 1 ? parts[1] : "";

			switch (name)
			{
				case "turns": _turns = Mathf.Max(1, value.ToInt()); break;
				case "seed": _seed = value.ToInt(); break;
				case "record": _recordPath = value; break;
				case "replay": _replayPath = value; break;
				case "expect": _expectedHash = value; break;
				case "quiet": _quiet = true; break;
				default:
					Fail($"unknown option '{argument}'. Options: --turns= --seed= --record= --replay= --expect= --quiet.");
					return false;
			}
		}
		return true;
	}

	private void Fail(string message)
	{
		GD.PrintErr($"[Harness] {message}");
		_finished = true;
		if (IsInsideTree()) GetTree().Quit(1);
	}
}
