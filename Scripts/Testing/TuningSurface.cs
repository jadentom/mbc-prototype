using Godot;
using System.Collections.Generic;
using MbcPrototype.Enemies;
using MbcPrototype.Rules;
using MbcPrototype.Units;
using MbcPrototype.World;

namespace MbcPrototype.Testing;

/// <summary>
/// One tunable number: which resource it lives on, what it is called, and what it
/// is worth right now.
///
/// It is a *view* of a resource's exported property, not a copy of it: reading
/// <see cref="Value"/> goes back to the resource, and writing it changes the
/// resource every consumer already holds a reference to — which is what makes a
/// live edit (docs/parity M0.7) felt by the next shot instead of the next build.
/// </summary>
public sealed class TuningEntry
{
	/// <summary>The resource the number lives on — the one home for it (§2).</summary>
	public Resource Home { get; init; }

	/// <summary>Short label of that resource, for the overlay and for <c>--tune</c>.</summary>
	public string Target { get; init; }

	/// <summary>The exported property's name, as Godot knows it.</summary>
	public string Property { get; init; }

	/// <summary>The Godot type of the value, which decides how it is edited and parsed.</summary>
	public Variant.Type Type { get; init; }

	/// <summary>Smallest step an edit makes; from the property's range hint when it has one.</summary>
	public double Step { get; init; }

	/// <summary>Lower bound of the range hint, or <see cref="double.NegativeInfinity"/> when unbounded.</summary>
	public double Min { get; init; } = double.NegativeInfinity;

	/// <summary>Upper bound of the range hint, or <see cref="double.PositiveInfinity"/> when unbounded.</summary>
	public double Max { get; init; } = double.PositiveInfinity;

	/// <summary>The number as the resource holds it now.</summary>
	public double Value
	{
		get => Read();
		set => Write(value);
	}

	/// <summary>"hub.Damage" — how a designer (and <c>--tune</c>) names this entry.</summary>
	public string Label => $"{Target}.{Property}";

	/// <summary>The current value, as the line the overlay shows.</summary>
	public string Describe()
	{
		return Type == Variant.Type.Bool
			? $"{Property} = {(Read() != 0 ? "on" : "off")}"
			: $"{Property} = {Read():0.###}";
	}

	/// <summary>Reads the value straight off the resource, so nothing here can go stale.</summary>
	public double Read()
	{
		Variant value = Home.Get(Property);
		return Type == Variant.Type.Bool ? (value.AsBool() ? 1 : 0) : value.AsDouble();
	}

	/// <summary>Writes the value onto the resource, clamped to the property's own range.</summary>
	public void Write(double value)
	{
		double clamped = System.Math.Clamp(value, Min, Max);

		if (Type == Variant.Type.Bool)
		{
			// A bool property in the inspector is a checkbox; as a number it is
			// on or off, so anything but zero turns it on.
			Home.Set(Property, clamped != 0);
			return;
		}
		if (Type == Variant.Type.Int)
		{
			Home.Set(Property, (int)System.Math.Round(clamped));
			return;
		}
		Home.Set(Property, (float)clamped);
	}
}

/// <summary>
/// Every number a designer can move in a running match, gathered from the
/// resources the match is actually using (docs/parity §7 and M0.7).
///
/// It is built by walking each resource's **exported** properties, so a number
/// added to <see cref="MatchRules"/>, to a unit's
/// <see cref="UnitDefinition"/>, to the <see cref="MapDefinition"/> or to the
/// <see cref="AiProfile"/> appears in the overlay and in <c>--tune</c> without
/// anyone adding it here. That is the point: the tuning surface is the resources
/// themselves (§2), and this class is only a reader and a writer of them.
///
/// Both the developer overlay and the headless harness drive this one class, so
/// the thing a designer fiddles with on screen and the thing a regression run
/// replays are the same code path.
/// </summary>
public sealed class TuningSurface
{
	private readonly List<TuningEntry> _entries = new List<TuningEntry>();
	private readonly List<Resource> _homes = new List<Resource>();

	/// <summary>Everything the surface can edit, in the order it was built.</summary>
	public IReadOnlyList<TuningEntry> Entries => _entries;

	/// <summary>
	/// Gathers the tunables of a match's content: the rules, every unit in the
	/// roster, the map, and the AI's profile. Anything null is skipped, so a match
	/// missing a resource simply has fewer rows.
	/// </summary>
	public static TuningSurface For(UnitCatalog catalog, MatchRules rules, AiProfile profile, MapDefinition map)
	{
		var surface = new TuningSurface();

		surface.Add("rules", rules);
		surface.Add("ai", profile);
		surface.Add("map", map);

		if (catalog != null)
		{
			foreach (UnitDefinition unit in catalog.Units)
			{
				surface.Add(string.IsNullOrEmpty(unit?.Id) ? "unit" : unit.Id, unit);
			}
		}

		return surface;
	}

	/// <summary>
	/// Adds every numeric or boolean property <paramref name="resource"/> exports.
	/// Strings, references (an icon, an ammo scene) and collections are left out:
	/// they are content, not balance, and §7's table is numbers.
	/// </summary>
	public void Add(string target, Resource resource)
	{
		if (resource == null) return;

		if (!_homes.Contains(resource)) _homes.Add(resource);

		foreach (Godot.Collections.Dictionary property in resource.GetPropertyList())
		{
			string name = property["name"].AsString();
			var usage = (PropertyUsageFlags)(int)property["usage"];

			// Only what the resource's own script exports. A resource also reports
			// its base class's properties — "resource_local_to_scene" is a bool any
			// Resource has, and listing it would put a checkbox that does nothing
			// for balance at the top of the panel (and, because it is saved like
			// any other, write it into the .tres). The inspector's group and
			// category headers come through the same list.
			if ((usage & PropertyUsageFlags.ScriptVariable) == 0) continue;

			var type = (Variant.Type)(int)property["type"];
			if (type != Variant.Type.Float && type != Variant.Type.Int && type != Variant.Type.Bool) continue;

			ParseRange(property, type, out double min, out double max, out double step);

			_entries.Add(new TuningEntry
			{
				Home = resource,
				Target = target,
				Property = name,
				Type = type,
				Min = min,
				Max = max,
				Step = step,
			});
		}
	}

	/// <summary>Finds one entry by its <c>target.property</c> label (case-insensitive).</summary>
	public TuningEntry Find(string label)
	{
		foreach (TuningEntry entry in _entries)
		{
			if (string.Equals(entry.Label, label, System.StringComparison.OrdinalIgnoreCase)) return entry;
		}
		return null;
	}

	/// <summary>Every label the surface knows, for an error message that says what was expected.</summary>
	public string DescribeLabels()
	{
		var labels = new List<string>();
		foreach (TuningEntry entry in _entries)
		{
			labels.Add(entry.Label);
		}
		return string.Join(", ", labels);
	}

	/// <summary>
	/// Writes the edited values back into the resource files they came from, so a
	/// change made in a running match survives the match (docs/parity §2 rule 3,
	/// M0.7). Returns false if any file could not be written, having printed which
	/// one and why.
	/// </summary>
	public bool Save()
	{
		bool saved = true;

		foreach (Resource home in _homes)
		{
			if (string.IsNullOrEmpty(home.ResourcePath))
			{
				GD.PushError($"TuningSurface: '{home.ResourceName}' was not loaded from a file, so there is nowhere to save it.");
				saved = false;
				continue;
			}

			Error error = ResourceSaver.Save(home, home.ResourcePath);
			if (error != Error.Ok)
			{
				GD.PushError($"TuningSurface: could not save {home.ResourcePath} ({error}).");
				saved = false;
			}
		}

		return saved;
	}

	/// <summary>
	/// Throws away the live edits by re-reading every resource from its file. This
	/// is rule 3's other half (§2): a number is changeable without a restart, in
	/// both directions — including one edited in the editor while the match runs.
	/// </summary>
	public bool Reload()
	{
		bool reloaded = true;

		foreach (Resource home in _homes)
		{
			if (string.IsNullOrEmpty(home.ResourcePath)) continue;

			// CacheMode.Ignore, not the default: the default hands back the
			// instance that is already loaded — which is the one holding the live
			// edits — so re-reading a file would look like it worked and change
			// nothing. Ignore reads the file without disturbing the cache, and the
			// values are copied across below.
			Resource fresh = ResourceLoader.Load<Resource>(home.ResourcePath, null, ResourceLoader.CacheMode.Ignore);
			if (fresh == null)
			{
				GD.PushError($"TuningSurface: could not re-read {home.ResourcePath}.");
				reloaded = false;
				continue;
			}

			// Copied across rather than swapped: the match holds references to the
			// *instance* it built with, so replacing the object here would leave
			// the simulation reading the old numbers.
			foreach (TuningEntry entry in _entries)
			{
				if (entry.Home != home) continue;
				home.Set(entry.Property, fresh.Get(entry.Property));
			}
		}

		return reloaded;
	}

	/// <summary>
	/// Reads a property's range hint, so an edit steps by the amount the resource
	/// itself declares and never wanders outside it. A property with no hint gets
	/// a step derived from its current magnitude — a designer stepping 0.001 at a
	/// time through a gravity of 9.8 learns nothing.
	/// </summary>
	private static void ParseRange(Godot.Collections.Dictionary property, Variant.Type type, out double min, out double max, out double step)
	{
		min = double.NegativeInfinity;
		max = double.PositiveInfinity;

		var hint = (PropertyHint)(int)property["hint"];
		string hintString = property["hint_string"].AsString();

		if (hint == PropertyHint.Range && !string.IsNullOrEmpty(hintString))
		{
			string[] parts = hintString.Split(',');
			if (parts.Length >= 2
				&& double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double low)
				&& double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double high))
			{
				min = low;
				max = high;

				if (parts.Length >= 3
					&& double.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double declared)
					&& declared > 0)
				{
					step = declared;
					return;
				}
			}
		}

		if (type == Variant.Type.Int) step = 1;
		else if (type == Variant.Type.Bool) step = 1;
		else step = 0.1;
	}
}
