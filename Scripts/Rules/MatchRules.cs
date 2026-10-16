using Godot;

namespace MbcPrototype.Rules;

/// <summary>
/// The match's global rules: every gameplay number that is not a property of one
/// unit and not a property of the map (docs/parity §7). One resource per rule
/// set, so a balance change is an edit and not a rebuild (§2).
///
/// Today it holds the blast falloff curve, which is what the hit-point model
/// needed (§M0.2). The rest of §7's global rows — the turn clock, the energy cap
/// and income, the launch ballistics, the cord rules — move in here as the
/// milestones that introduce them land (M0.7 gathers what exists; M1 to M3 add
/// the rest).
///
/// The field defaults are fallbacks for a match that runs with no resources
/// assigned at all (§2), never the source of truth: the resource is.
/// </summary>
[GlobalClass]
public partial class MatchRules : Resource
{
	/// <summary>
	/// Share of a weapon's damage dealt in the outer band of its blast — the
	/// donut between <see cref="MbcPrototype.Units.UnitDefinition.DirectHitRadius"/>
	/// and its <see cref="MbcPrototype.Units.UnitDefinition.OuterRadius"/>.
	///
	/// The manual publishes no falloff curve, so this two-band model is our first
	/// guess at one (docs/parity §7): full damage inside the inner radius,
	/// <see cref="BlastOuterBandFraction"/> of it out to the outer radius, nothing
	/// past it. Rounding is downward — a near miss is never worth a direct hit —
	/// so a Bomb (3) splashes 1.
	/// </summary>
	[Export(PropertyHint.Range, "0,1")] public float BlastOuterBandFraction = 0.5f;

	/// <summary>
	/// Damage <paramref name="damage"/> deals in the outer band, rounded down
	/// and never below zero. Derived from the weapon's own damage rather than
	/// stored per unit (§2: a derived value is a function, not a second
	/// hand-maintained number).
	/// </summary>
	public int OuterBandDamage(int damage)
	{
		if (damage <= 0) return 0;
		return Mathf.FloorToInt(damage * Mathf.Clamp(BlastOuterBandFraction, 0f, 1f));
	}
}
