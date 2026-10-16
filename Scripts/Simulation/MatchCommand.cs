using Godot;

namespace MbcPrototype.Simulation;

/// <summary>
/// The kinds of action a match can be given, and therefore the kinds a command
/// log can hold.
///
/// One today: firing a unit <em>is</em> building in this game, so a launch is the
/// whole of a turn's input. M1 adds the actions an energy budget brings (a build
/// menu, skipping a round, ending a turn), and each one is a new value here plus
/// a case in <see cref="MbcPrototype.Core.GameManager.ApplyCommand"/> — never a
/// new code path around the log.
/// </summary>
public enum CommandKind
{
	/// <summary>Fire a unit from one of the acting seat's structures.</summary>
	Launch = 0,
}

/// <summary>
/// One action, as data: everything needed to reproduce it, without the input
/// device, the AI that chose it, or the frame it happened on (docs/parity M0.5).
///
/// A command names things by **id** — the seat that acted, the unit's id from the
/// roster, the id of the structure the shot came from — and never by object
/// reference, so a command survives being written to a file and read back in
/// another run, or in another process. That is what makes a replay a replay
/// rather than a recording.
///
/// Deliberately a plain struct: a command is a value, not a node, and it has no
/// behaviour beyond describing itself.
/// </summary>
public readonly struct MatchCommand
{
	/// <summary>What kind of action this is.</summary>
	public CommandKind Kind { get; }

	/// <summary>
	/// The seat that acted — its position in the rotation, not its team (a team
	/// may hold several seats once a match can have more than one combatant
	/// each).
	/// </summary>
	public int Seat { get; }

	/// <summary>The unit fired, by <see cref="MbcPrototype.Units.UnitDefinition.Id"/>.</summary>
	public string UnitId { get; }

	/// <summary>The structure the action was taken from, by <see cref="MbcPrototype.Core.Structure.Id"/>.</summary>
	public int OriginStructureId { get; }

	/// <summary>Aim angle, in the aim-indicator convention (see AmmoLauncher).</summary>
	public float AimAngleRadians { get; }

	/// <summary>Charge level, 0..1, mapped onto the launch force range.</summary>
	public float PowerPercent { get; }

	private MatchCommand(CommandKind kind, int seat, string unitId, int originStructureId, float aimAngleRadians, float powerPercent)
	{
		Kind = kind;
		Seat = seat;
		UnitId = unitId ?? "";
		OriginStructureId = originStructureId;
		AimAngleRadians = aimAngleRadians;
		PowerPercent = powerPercent;
	}

	/// <summary>A shot: fire <paramref name="unitId"/> from <paramref name="originStructureId"/>.</summary>
	public static MatchCommand Launch(int seat, string unitId, int originStructureId, float aimAngleRadians, float powerPercent)
	{
		return new MatchCommand(CommandKind.Launch, seat, unitId, originStructureId, aimAngleRadians, powerPercent);
	}

	/// <summary>This command as a serializable dictionary.</summary>
	public Godot.Collections.Dictionary ToDictionary()
	{
		return new Godot.Collections.Dictionary
		{
			{ "kind", (int)Kind },
			{ "seat", Seat },
			{ "unit", UnitId },
			{ "origin", OriginStructureId },
			{ "aim", AimAngleRadians },
			{ "power", PowerPercent },
		};
	}

	/// <summary>
	/// Reads a command back. Returns false and explains why when the dictionary
	/// is not a command this build understands — a log from a newer version, or a
	/// hand-edited file, must fail loudly rather than replay as something else.
	/// </summary>
	public static bool TryFromDictionary(Godot.Collections.Dictionary source, out MatchCommand command, out string error)
	{
		command = default;
		error = null;

		if (source == null)
		{
			error = "the command is empty";
			return false;
		}

		if (!source.ContainsKey("kind") || !source.ContainsKey("seat")
			|| !source.ContainsKey("unit") || !source.ContainsKey("origin"))
		{
			error = "a command needs a kind, a seat, a unit and an origin structure";
			return false;
		}

		int kind = source["kind"].AsInt32();
		if (!System.Enum.IsDefined(typeof(CommandKind), kind))
		{
			error = $"unknown command kind {kind}";
			return false;
		}

		command = new MatchCommand(
			(CommandKind)kind,
			source["seat"].AsInt32(),
			source["unit"].AsString(),
			source["origin"].AsInt32(),
			(float)source["aim"].AsDouble(),
			(float)source["power"].AsDouble());
		return true;
	}

	/// <summary>One line describing this command, for logs and test output.</summary>
	public string Describe()
	{
		return $"{Kind} by seat {Seat}: {UnitId} from structure {OriginStructureId} "
			+ $"at {Mathf.RadToDeg(AimAngleRadians):0.#}°, power {PowerPercent:0.###}";
	}

	public override string ToString() => Describe();
}
