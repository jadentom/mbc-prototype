using Godot;
using System.Collections.Generic;
using MbcPrototype.Core;
using MbcPrototype.Rules;
using MbcPrototype.Simulation;
using MbcPrototype.TurnSystem;
using MbcPrototype.Units;
using MbcPrototype.World;

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
/// this one), <c>--tune=&lt;target&gt;.&lt;Property&gt;=&lt;value&gt;</c> (change one of the
/// match's numbers before it starts; repeatable), <c>--save-tuning</c> (write the
/// tuned numbers back into their resources), <c>--aim=enemy|pools</c> (aim the
/// player's builds at the enemy, or at the nearest energy pool in reach, which
/// exercises the Harvester's on-pool income — docs/parity M1), <c>--quiet</c>.
///
/// <c>--tune</c> and <c>--save-tuning</c> go through
/// <see cref="TuningSurface"/> — the same surface the developer overlay edits
/// (docs/parity M0.7) — so "change a damage value and replay the match" is a
/// command rather than an afternoon:
///
/// <code>
/// godot --headless --fixed-fps 60 --path . Scenes/harness.tscn -- --turns=10 --seed=7 \
///     --replay=/tmp/m.json --tune=bomb.Damage=6
/// </code>
///
/// A different state hash is the proof the change reached the simulation.
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

	/// <summary>Where the harness's builds are aimed: at the enemy, or at the nearest pool in reach.</summary>
	private string _aimMode = "enemy";
	private string _recordPath;
	private string _replayPath;
	private string _expectedHash;
	private bool _quiet;

	// "target.Property=value" arguments, applied through the tuning surface before
	// the match starts (docs/parity M0.7).
	private readonly List<string> _tunes = new List<string>();
	private bool _saveTuning;

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

		// Before the scene enters the tree: the match reads its resources while it
		// builds itself, so a tuned number has to be on the resource by then.
		if (!ApplyTuning()) return;

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
	/// The harness's policy for the human's seat: build the first unit in the
	/// roster the energy can pay for (docs/parity M1), from the launcher nearest
	/// the enemy, or skip the rest of the turn when nothing is affordable. A fixed
	/// policy is the point — the match has to be the same match twice.
	/// </summary>
	private void SubmitPlayerAction()
	{
		Combatant seat = _match.ActiveCombatant;
		IReadOnlyList<UnitDefinition> buildable = _match.Catalog?.Buildable;
		if (seat == null || buildable == null || buildable.Count == 0)
		{
			_match.SkipTurn();
			return;
		}

		Structure origin = NearestOwnLauncher(seat, out Structure target);
		UnitDefinition unit = ChooseAffordable(seat, buildable);
		if (origin == null || unit == null)
		{
			_match.SkipTurn();
			return;
		}

		float aim;
		float power = PlayerPower;
		if (_aimMode == "pools" && TryAimAtPool(origin, out float poolAim, out float poolPower))
		{
			aim = poolAim;
			power = poolPower;
		}
		else if (target != null)
		{
			Vector3 delta = target.GlobalPosition - origin.GlobalPosition;
			aim = Mathf.Atan2(delta.X, delta.Z);
		}
		else
		{
			_match.SkipTurn();
			return;
		}

		_match.SubmitCommand(MatchCommand.Launch(seat.Seat, unit.Id, origin.Id, aim, power));
	}

	/// <summary>The charge the harness's player fires at when it is aiming at the enemy.</summary>
	private const float PlayerPower = 0.6f;

	/// <summary>
	/// The first buildable unit the seat can pay for, starting where the last build
	/// left off and wrapping — the same order the AI works through, so a run covers
	/// the roster.
	/// </summary>
	private UnitDefinition ChooseAffordable(Combatant seat, IReadOnlyList<UnitDefinition> buildable)
	{
		for (int step = 0; step < buildable.Count; step++)
		{
			UnitDefinition unit = buildable[(_shots + step) % buildable.Count];
			if (unit == null || !seat.CanAfford(unit.Level)) continue;

			_shots = (_shots + step + 1) % buildable.Count;
			return unit;
		}
		return null;
	}

	/// <summary>Own launcher closest to the enemy, and the enemy structure it is closest to.</summary>
	private Structure NearestOwnLauncher(Combatant seat, out Structure target)
	{
		target = null;
		Structure origin = null;
		float best = float.MaxValue;

		// The enemy first: the seat's own launchers are then judged against it.
		Structure enemy = null;
		float nearestEnemy = float.MaxValue;
		foreach (Combatant combatant in _match.Combatants)
		{
			if (combatant == seat || combatant.Team == seat.Team) continue;
			if (combatant is not NodeChainCombatant chain) continue;

			foreach (Structure structure in chain.Structures)
			{
				if (!IsLive(structure)) continue;
				float distance = structure.GlobalPosition.DistanceSquaredTo(
					seat is NodeChainCombatant own && own.RootStructure != null
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
			if (structure.Definition == null || !structure.Definition.IsLauncher) continue;

			float distance = structure.GlobalPosition.DistanceSquaredTo(enemy.GlobalPosition);
			if (distance < best)
			{
				best = distance;
				origin = structure;
			}
		}
		return origin;
	}

	/// <summary>
	/// Aims a build so that it lands on the nearest energy pool the launcher can
	/// reach — the one place in the harness that has to invert the ballistics, and
	/// only so that a headless run can exercise the Harvester's on-pool income
	/// (docs/parity M1). It reads every number off the match's rules and the
	/// launcher's own definition, so a tuned arc still lands where it is aimed.
	///
	/// The flight is the simulation's own: a horizontal launch force with a fixed
	/// upward bias, falling under the rules' gravity from the launcher's muzzle
	/// height. Returns false when no pool is within reach.
	/// </summary>
	private bool TryAimAtPool(Structure origin, out float aim, out float power)
	{
		aim = 0f;
		power = 0f;

		PoolField pools = _match.Pools;
		MatchRules rules = _match.ActiveRules;
		if (pools == null || rules == null || origin == null) return false;

		float gravity = rules.EffectiveGravity;
		float bias = rules.EffectiveUpwardBias;
		float muzzle = rules.EffectiveMuzzleHeight;
		if (gravity <= 0f || bias <= 0f) return false;

		// Time in the air: the muzzle height plus the upward bias, under gravity.
		float flightTime = (bias + Mathf.Sqrt((bias * bias) + (2f * gravity * muzzle))) / gravity;
		if (flightTime <= 0f) return false;

		float baseMin = rules.MinLaunchForce;
		float baseMax = rules.MaxLaunchForce;
		if (baseMax <= baseMin)
		{
			baseMin = MatchRules.FallbackMinLaunchForce;
			baseMax = MatchRules.FallbackMaxLaunchForce;
		}
		float reachScale = origin.Definition != null && origin.Definition.LaunchForceScale > 0f
			? origin.Definition.LaunchForceScale
			: 1f;
		float minRange = baseMin * reachScale * flightTime;
		float maxRange = baseMax * reachScale * flightTime;

		var from = new Vector2(origin.GlobalPosition.X, origin.GlobalPosition.Z);
		PurplePool nearest = null;
		float nearestDistance = float.MaxValue;
		foreach (PurplePool pool in pools.Pools)
		{
			var at = new Vector2(pool.GlobalPosition.X, pool.GlobalPosition.Z);
			float distance = from.DistanceTo(at);
			if (distance < minRange || distance > maxRange) continue;
			if (distance < nearestDistance)
			{
				nearestDistance = distance;
				nearest = pool;
			}
		}
		if (nearest == null) return false;

		var delta = new Vector2(nearest.GlobalPosition.X - origin.GlobalPosition.X,
			nearest.GlobalPosition.Z - origin.GlobalPosition.Z);
		aim = Mathf.Atan2(delta.X, delta.Y);
		power = Mathf.Clamp(((nearestDistance / flightTime) / reachScale - baseMin) / (baseMax - baseMin), 0f, 1f);
		return true;
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

	/// <summary>
	/// Applies every <c>--tune=target.Property=value</c> to the match's resources
	/// before the match builds itself, through the same
	/// <see cref="TuningSurface"/> the developer overlay drives (docs/parity M0.7).
	///
	/// A number nobody can find is reported with the list of ones that exist, so a
	/// typo costs a run and not a hunt. With <c>--save-tuning</c> the change is
	/// written back to the resource as well, which is the overlay's Enter key from
	/// a script — and how the write-back is testable without a window.
	/// </summary>
	private bool ApplyTuning()
	{
		if (_tunes.Count == 0) return true;

		TuningSurface surface = TuningSurface.For(_match.Catalog, _match.Rules, _match.Profile, _match.Map);
		if (surface.Entries.Count == 0)
		{
			Fail("--tune was given but the match has no tunable resources at all.");
			return false;
		}

		foreach (string tune in _tunes)
		{
			int separator = tune.LastIndexOf('=');
			if (separator <= 0)
			{
				Fail($"--tune={tune} is not target.Property=value.");
				return false;
			}

			string label = tune.Substring(0, separator);
			string text = tune.Substring(separator + 1);

			TuningEntry entry = surface.Find(label);
			if (entry == null)
			{
				Fail($"--tune={tune}: no number called '{label}'. Known numbers: {surface.DescribeLabels()}.");
				return false;
			}

			if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
			{
				Fail($"--tune={tune}: '{text}' is not a number.");
				return false;
			}

			double before = entry.Read();
			entry.Value = value;
			if (!_quiet) GD.Print($"[Harness] tuned {entry.Label}: {before:0.###} -> {entry.Read():0.###}");
		}

		if (_saveTuning)
		{
			if (!surface.Save())
			{
				Fail("--save-tuning could not write the resources back.");
				return false;
			}
			if (!_quiet) GD.Print($"[Harness] wrote {_tunes.Count} tuned number(s) back into the resources.");
		}

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
				case "tune": _tunes.Add(value); break;
				case "aim": _aimMode = value; break;
				case "save-tuning": _saveTuning = true; break;
				case "quiet": _quiet = true; break;
				default:
					Fail($"unknown option '{argument}'. Options: --turns= --seed= --record= --replay= --expect= --tune= --save-tuning --aim= --quiet.");
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
