using Godot;
using System.Collections.Generic;

namespace MbcPrototype.Simulation;

/// <summary>
/// The ordered record of everything a match was told to do: the match's *input*,
/// as opposed to its state (docs/parity M0.5).
///
/// Every action — the human's and the AI's alike — is appended here in the order
/// it was applied, and the simulation is only ever changed by applying a command.
/// A match is therefore a function of (map, seed, this log): replay the log and
/// the same match happens again, which is what makes replays (M7), regression
/// tests (§6) and "change a number, replay, compare" (§2) possible at all.
///
/// The log knows how to write itself out and read itself back, and nothing else:
/// applying a command is the simulation's job
/// (<see cref="MbcPrototype.Core.GameManager.ApplyCommand"/>), so a log can be
/// read, inspected and compared without a match running.
/// </summary>
public sealed class CommandLog
{
	/// <summary>
	/// Format version, written into every log. A reader refuses a version it does
	/// not know rather than guessing at the shape of the file.
	/// </summary>
	public const int FormatVersion = 1;

	private readonly List<MatchCommand> _commands = new List<MatchCommand>();

	/// <summary>The seed the match was given: with the map, the other half of its input.</summary>
	public int Seed { get; set; }

	/// <summary>The id of the map the commands were played on.</summary>
	public string MapId { get; set; } = "";

	/// <summary>
	/// Whether <see cref="Record"/> still appends. Off while replaying, so
	/// playing a log back never edits the log it is reading.
	/// </summary>
	public bool IsRecording { get; set; } = true;

	/// <summary>The commands, in the order they were applied.</summary>
	public IReadOnlyList<MatchCommand> Commands => _commands;

	/// <summary>How many commands the log holds.</summary>
	public int Count => _commands.Count;

	/// <summary>The command applied at <paramref name="index"/>.</summary>
	public MatchCommand this[int index] => _commands[index];

	/// <summary>
	/// Appends a command, if this log is recording. The caller has already
	/// applied it: the log is a record of what happened, not a queue of what
	/// should.
	/// </summary>
	public void Record(MatchCommand command)
	{
		if (IsRecording) _commands.Add(command);
	}

	/// <summary>Stops recording, and remembers why: this log is now being replayed.</summary>
	public void StopRecording()
	{
		IsRecording = false;
	}

	/// <summary>One line describing the log, for logs and test output.</summary>
	public string Describe()
	{
		return $"{Count} command(s), seed {Seed}, map '{MapId}'";
	}

	// ------------------------------------------------------------------
	// Serialization
	// ------------------------------------------------------------------

	/// <summary>This log as a serializable dictionary.</summary>
	public Godot.Collections.Dictionary ToDictionary()
	{
		var commands = new Godot.Collections.Array<Godot.Collections.Dictionary>();
		foreach (MatchCommand command in _commands) commands.Add(command.ToDictionary());

		return new Godot.Collections.Dictionary
		{
			{ "version", FormatVersion },
			{ "seed", Seed },
			{ "map", MapId },
			{ "commands", commands },
		};
	}

	/// <summary>Reads a log back, explaining what is wrong with one it cannot read.</summary>
	public static bool TryFromDictionary(Godot.Collections.Dictionary source, out CommandLog log, out string error)
	{
		log = null;
		error = null;

		if (source == null)
		{
			error = "the log is empty";
			return false;
		}

		int version = source.ContainsKey("version") ? source["version"].AsInt32() : 0;
		if (version != FormatVersion)
		{
			error = $"command log version {version}, this build writes and reads version {FormatVersion}";
			return false;
		}

		var result = new CommandLog
		{
			Seed = source.ContainsKey("seed") ? source["seed"].AsInt32() : 0,
			MapId = source.ContainsKey("map") ? source["map"].AsString() : "",
		};

		if (source.ContainsKey("commands") && source["commands"].VariantType == Variant.Type.Array)
		{
			foreach (Variant entry in source["commands"].AsGodotArray())
			{
				if (entry.VariantType != Variant.Type.Dictionary)
				{
					error = "a command in the log is not a dictionary";
					log = null;
					return false;
				}
				if (!MatchCommand.TryFromDictionary(entry.AsGodotDictionary(), out MatchCommand command, out error))
				{
					log = null;
					return false;
				}
				result._commands.Add(command);
			}
		}

		log = result;
		return true;
	}

	/// <summary>The whole log as JSON text — the form it is stored in.</summary>
	public string ToJson()
	{
		return Json.Stringify(ToDictionary(), "  ");
	}

	/// <summary>Reads a log from JSON text.</summary>
	public static bool TryFromJson(string json, out CommandLog log, out string error)
	{
		log = null;
		error = null;

		Variant parsed = Json.ParseString(json);
		if (parsed.VariantType != Variant.Type.Dictionary)
		{
			error = "the file is not a command log (its root is not a JSON object)";
			return false;
		}
		return TryFromDictionary(parsed.AsGodotDictionary(), out log, out error);
	}

	/// <summary>
	/// Writes the log to <paramref name="path"/> (`user://` or absolute, as
	/// Godot's FileAccess understands it).
	/// </summary>
	public Error Save(string path)
	{
		using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		if (file == null)
		{
			Error openError = FileAccess.GetOpenError();
			GD.PushError($"CommandLog: cannot write '{path}': {openError}.");
			return openError;
		}

		file.StoreString(ToJson());
		return Error.Ok;
	}

	/// <summary>Reads a log from <paramref name="path"/>, or returns null and says why.</summary>
	public static CommandLog Load(string path, out string error)
	{
		error = null;

		if (!FileAccess.FileExists(path))
		{
			error = $"there is no file at '{path}'";
			return null;
		}

		using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			error = $"cannot read '{path}': {FileAccess.GetOpenError()}";
			return null;
		}

		if (!TryFromJson(file.GetAsText(), out CommandLog log, out error)) return null;
		return log;
	}
}
