using Godot;
using System.Collections.Generic;

namespace MbcPrototype.Units;

/// <summary>
/// The roster a match draws from: the one list of units the HUD, the launcher
/// and (later) a scenario all read, so a unit that exists is a unit that can be
/// built and drawn, and adding a thirteenth unit is a new
/// <see cref="UnitDefinition"/> added here rather than a code change
/// (docs/parity M0.1, M4).
///
/// A resource rather than a directory scan or a code list on purpose: it is
/// content, it is the same on every machine, and the editor validates it
/// (see <see cref="Validate"/>).
/// </summary>
[GlobalClass]
public partial class UnitCatalog : Resource
{
	/// <summary>Every unit in the roster, in the order the HUD shows them.</summary>
	[Export] public Godot.Collections.Array<UnitDefinition> Units = new Godot.Collections.Array<UnitDefinition>();

	/// <summary>How many units the roster holds.</summary>
	public int Count => Units?.Count ?? 0;

	/// <summary>
	/// The units a player or the AI may actually build, in catalog order:
	/// everything with <see cref="UnitDefinition.CanBeBuilt"/> set. The build
	/// menu and the AI's plan both come from here, so "what can be built" has
	/// one home, and it is the units' own resources.
	///
	/// The list is rebuilt on every read (it is a handful of units) so a live
	/// change to a unit's build permission — the overlay and <c>--tune</c> can
	/// switch a unit out of the roster mid-match — is obeyed immediately. It is
	/// a fresh list each time rather than a cached one, so a caller can hold on
	/// to it without a later read changing what it is looking at.
	/// </summary>
	public IReadOnlyList<UnitDefinition> Buildable
	{
		get
		{
			var buildable = new List<UnitDefinition>();
			if (Units != null)
			{
				foreach (UnitDefinition unit in Units)
				{
					if (unit != null && unit.CanBeBuilt) buildable.Add(unit);
				}
			}
			return buildable;
		}
	}

	/// <summary>The unit at <paramref name="index"/>, or null when out of range.</summary>
	public UnitDefinition Get(int index)
	{
		return Units != null && index >= 0 && index < Units.Count ? Units[index] : null;
	}

	/// <summary>
	/// The unit with the given <see cref="UnitDefinition.Id"/> (null when the
	/// roster has no such unit). Ids are how code names a unit it needs by role —
	/// the Hub every chain starts from, for instance.
	/// </summary>
	public UnitDefinition Find(string id)
	{
		if (string.IsNullOrEmpty(id) || Units == null) return null;

		foreach (UnitDefinition unit in Units)
		{
			if (unit != null && unit.Id == id) return unit;
		}
		return null;
	}

	/// <summary>
	/// Reports the roster's own data mistakes — a missing id, two units sharing
	/// one id, a unit with nothing to launch — so a content error surfaces at
	/// match start instead of as a missing projectile later (§2: a number or a
	/// name that exists in two places is a bug).
	/// </summary>
	public void Validate()
	{
		if (Units == null || Units.Count == 0)
		{
			GD.PushError("UnitCatalog: the roster is empty — there is nothing to build or launch.");
			return;
		}

		var seenIds = new HashSet<string>();
		for (int i = 0; i < Units.Count; i++)
		{
			UnitDefinition unit = Units[i];
			if (unit == null)
			{
				GD.PushError($"UnitCatalog: entry {i} is empty.");
				continue;
			}

			if (string.IsNullOrWhiteSpace(unit.Id)) GD.PushError($"UnitCatalog: entry {i} ('{unit.DisplayName}') has no id.");
			else if (!seenIds.Add(unit.Id)) GD.PushError($"UnitCatalog: '{unit.Id}' appears more than once.");

			if (unit.ProjectileScene == null) GD.PushError($"UnitCatalog: '{unit.Id}' has no projectile scene, so it cannot be launched.");
			if (unit.HitPoints < 0) GD.PushError($"UnitCatalog: '{unit.Id}' has negative hit points.");
			if (unit.Level <= 0) GD.PushError($"UnitCatalog: '{unit.Id}' has no level (its energy cost) — set one in the unit's resource.");
			if (unit.HasBlast && unit.Damage <= 0) GD.PushWarning($"UnitCatalog: '{unit.Id}' has blast radii but no damage.");
			if (unit.Damage > 0 && !unit.HasBlast) GD.PushWarning($"UnitCatalog: '{unit.Id}' damages only what it strikes (no blast radii).");
		}
	}

	/// <summary>The roster's ids, for the match-start log line.</summary>
	public string DescribeIds()
	{
		if (Units == null || Units.Count == 0) return "none";

		var ids = new List<string>();
		foreach (UnitDefinition unit in Units) ids.Add(unit == null ? "?" : unit.Id);
		return string.Join(", ", ids);
	}
}
