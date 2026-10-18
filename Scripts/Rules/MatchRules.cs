using Godot;

namespace MbcPrototype.Rules;

/// <summary>
/// The match's global rules: every gameplay number that is not a property of one
/// unit and not a property of the map (docs/parity §7). One resource per rule
/// set, so a balance change is an edit and not a rebuild (§2).
///
/// It holds the blast falloff curve the hit-point model needed (§M0.2) and the
/// launch ballistics (§M0.7). The rest of §7's global rows — the turn clock, the
/// energy cap and income, the cord rules — move in here as the milestones that
/// introduce them land (M1 to M3).
///
/// **How a number is read.** The exports are the tuning surface — what a designer
/// (or the dev overlay, §M0.7) edits — and they hold an *unset* sentinel until
/// their resource says otherwise. The <c>Effective…</c> members are what the
/// simulation reads: the resource's value, or a degenerate fallback so the
/// project still runs with no assets at all (§2). <see cref="Validate"/> reports
/// every unset value at match start, so an unset number is visible rather than
/// silently replaced by a fallback.
///
/// There are deliberately no real balance numbers in the code: a number that
/// lived here as well as in the resource would be in two places (§2), which is
/// why the fallbacks are "make it run" values and not a second opinion.
/// </summary>
[GlobalClass]
public partial class MatchRules : Resource
{
	// ------------------------------------------------------------------
	// Blast
	// ------------------------------------------------------------------

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
	///
	/// Negative means "not set" (see the class remarks).
	/// </summary>
	[Export(PropertyHint.Range, "-1,1")] public float BlastOuterBandFraction = -1f;

	// ------------------------------------------------------------------
	// Launch ballistics (§7)
	// ------------------------------------------------------------------

	/// <summary>
	/// Gravity that pulls a shot down, in world units per second squared.
	///
	/// A **guess** (docs/parity §7): the original publishes no physics, so 9.8 is
	/// Earth's and everything about the arcs is provisional. Every kind of ammo
	/// reads it from here rather than carrying its own copy, so a heavier-feeling
	/// world is one edit.
	/// </summary>
	[Export] public float Gravity = 0f;

	/// <summary>
	/// Launch force at zero charge, in world units per second. Unset (zero) means
	/// the value belongs to the resource, not to a default in code (§2).
	/// </summary>
	[Export] public float MinLaunchForce = 0f;

	/// <summary>
	/// Launch force at full charge. With <see cref="MinLaunchForce"/> it is the
	/// reach of a launcher, scaled by that launcher's
	/// <see cref="MbcPrototype.Units.UnitDefinition.LaunchForceScale"/> — so how far
	/// a shot goes is a number a designer can find, not one buried in a launcher.
	/// **A guess** (§7): the manual publishes no ranges.
	/// </summary>
	[Export] public float MaxLaunchForce = 0f;

	/// <summary>
	/// Upward speed every shot gets on top of its aim, so a launch arcs instead of
	/// skidding along the ground. **A guess** (§7).
	/// </summary>
	[Export] public float UpwardBias = 0f;

	/// <summary>
	/// Height above its launcher's origin that a shot starts at, in world units.
	/// Enough to clear the structure it came from; a guess, and the kind of number
	/// that moves the moment arcs are tuned.
	/// </summary>
	[Export] public float MuzzleHeight = 0f;

	// ------------------------------------------------------------------
	// Aiming and charging: the feel of firing, which is gameplay (§2)
	// ------------------------------------------------------------------

	/// <summary>
	/// How fast the player's aim swings, in radians per second. A guess, and the
	/// number to move when aiming feels twitchy or sluggish.
	/// </summary>
	[Export] public float AimRotationSpeed = 0f;

	/// <summary>
	/// How much of the power meter fills per second while the fire key is held
	/// (1.0 = a full charge takes one second, and the meter then runs back down at
	/// the same rate). A guess: it is how long a player has to place a shot, so it
	/// is balance and not presentation.
	/// </summary>
	[Export] public float ChargeSpeed = 0f;

	// ------------------------------------------------------------------
	// Energy, rounds and the turn clock (§7, M1)
	//
	// The energy economy is the match's, not a unit's: what a unit costs is its
	// own Level, and how fast energy comes back is here. Negative means "not
	// set" (see the class remarks); 0 is a real value for the incomes and for
	// the clock ("this match pays no main-Hub income", "this match has no
	// clock"), which is why those sentinels are negative rather than zero.
	// ------------------------------------------------------------------

	/// <summary>
	/// Most energy a player may hold. The manual's 35; a number that will move
	/// when the economy is tuned. Negative means "not set".
	/// </summary>
	[Export] public int MaxStoredEnergy = -1;

	/// <summary>
	/// Energy each player starts the match with, before any income. **A guess**
	/// (docs/parity §7): the manual publishes income but not the opening purse.
	/// Negative means "not set".
	/// </summary>
	[Export] public int StartingEnergy = -1;

	/// <summary>
	/// Energy the chain's **main Hub** pays its owner at the end of every round
	/// (the manual's 7). Only the root structure pays it: a Hub built outward
	/// builds but does not generate, which is the original's rule
	/// ([units.md](../../docs/parity/units.md)). Negative means "not set".
	/// </summary>
	[Export] public int MainHubIncome = -1;

	/// <summary>
	/// Energy each Harvester standing on an energy pool pays at the end of a
	/// round (the manual's 3). Negative means "not set".
	/// </summary>
	[Export] public int HarvesterIncomeOnPool = -1;

	/// <summary>
	/// Energy each Harvester standing on ordinary ground pays at the end of a
	/// round (the manual's 1). Negative means "not set".
	/// </summary>
	[Export] public int HarvesterIncomeOffPool = -1;

	/// <summary>
	/// Seconds a player has for one turn before the turn is ended for them, or
	/// zero for a match with no clock. The manual's default is 60 s. **A guess**
	/// about how the clock is enforced, not about the number: the original's
	/// clock is a per-move limit, and this milestone ends the turn when it runs
	/// out rather than forfeiting anything else.
	///
	/// Negative means "not set"; unlike the incomes, zero here is meaningful
	/// (a match played without a clock), so the sentinel has to be negative.
	/// </summary>
	[Export(PropertyHint.Range, "-1,600,1")] public float TurnClockSeconds = -1f;

	// ------------------------------------------------------------------
	// Fallbacks for a match with no content at all (§2)
	//
	// These exist so that a match whose rules resource was never assigned still
	// launches shots instead of dropping them at the launcher's feet or hanging
	// the turn system forever. They are not a second set of balance numbers and
	// no resource repeats them: Validate() has already reported the missing
	// resource by the time one of these is used.
	// ------------------------------------------------------------------

	/// <summary>Gravity used when nothing is set: a shot that falls.</summary>
	public const float FallbackGravity = 9.8f;

	/// <summary>Force range used when nothing is set: enough to leave the launcher.</summary>
	public const float FallbackMinLaunchForce = 2f;

	/// <summary>Force range used when nothing is set (see <see cref="FallbackMinLaunchForce"/>).</summary>
	public const float FallbackMaxLaunchForce = 20f;

	/// <summary>Upward speed used when nothing is set, so a shot arcs rather than skims.</summary>
	public const float FallbackUpwardBias = 5f;

	/// <summary>Muzzle height used when nothing is set: above the structure it fires from.</summary>
	public const float FallbackMuzzleHeight = 2f;

	/// <summary>Aim speed used when nothing is set.</summary>
	public const float FallbackAimRotationSpeed = 3f;

	/// <summary>Charge speed used when nothing is set (a full charge in one second).</summary>
	public const float FallbackChargeSpeed = 1f;

	/// <summary>Energy cap used when nothing is set: the manual's maximum.</summary>
	public const int FallbackMaxStoredEnergy = 35;

	/// <summary>Starting energy used when nothing is set: one level-7 build.</summary>
	public const int FallbackStartingEnergy = 7;

	/// <summary>Main-Hub income used when nothing is set.</summary>
	public const int FallbackMainHubIncome = 7;

	/// <summary>Harvester income on a pool used when nothing is set.</summary>
	public const int FallbackHarvesterIncomeOnPool = 3;

	/// <summary>Harvester income off a pool used when nothing is set.</summary>
	public const int FallbackHarvesterIncomeOffPool = 1;

	/// <summary>Turn clock used when nothing is set: the manual's 60 s move.</summary>
	public const float FallbackTurnClockSeconds = 60f;

	/// <summary>
	/// The rules a match runs on when no resource was assigned at all: nothing is
	/// set, and every <c>Effective…</c> member below answers with its fallback.
	/// </summary>
	public static readonly MatchRules Fallback = new MatchRules();

	/// <summary>Gravity a shot falls at, as the simulation reads it.</summary>
	public float EffectiveGravity => Gravity > 0f ? Gravity : FallbackGravity;

	/// <summary>Upward speed a shot is given on top of its aim, as the simulation reads it.</summary>
	public float EffectiveUpwardBias => UpwardBias > 0f ? UpwardBias : FallbackUpwardBias;

	/// <summary>Height a shot starts at above its launcher, as the simulation reads it.</summary>
	public float EffectiveMuzzleHeight => MuzzleHeight > 0f ? MuzzleHeight : FallbackMuzzleHeight;

	/// <summary>Aim speed, as the simulation reads it.</summary>
	public float EffectiveAimRotationSpeed => AimRotationSpeed > 0f ? AimRotationSpeed : FallbackAimRotationSpeed;

	/// <summary>Charge speed, as the simulation reads it.</summary>
	public float EffectiveChargeSpeed => ChargeSpeed > 0f ? ChargeSpeed : FallbackChargeSpeed;

	/// <summary>Energy cap, as the simulation reads it.</summary>
	public int EffectiveMaxStoredEnergy => MaxStoredEnergy >= 0 ? MaxStoredEnergy : FallbackMaxStoredEnergy;

	/// <summary>Starting energy, as the simulation reads it.</summary>
	public int EffectiveStartingEnergy => StartingEnergy >= 0 ? StartingEnergy : FallbackStartingEnergy;

	/// <summary>Main-Hub income, as the simulation reads it.</summary>
	public int EffectiveMainHubIncome => MainHubIncome >= 0 ? MainHubIncome : FallbackMainHubIncome;

	/// <summary>Harvester income on a pool, as the simulation reads it.</summary>
	public int EffectiveHarvesterIncomeOnPool => HarvesterIncomeOnPool >= 0
		? HarvesterIncomeOnPool
		: FallbackHarvesterIncomeOnPool;

	/// <summary>Harvester income off a pool, as the simulation reads it.</summary>
	public int EffectiveHarvesterIncomeOffPool => HarvesterIncomeOffPool >= 0
		? HarvesterIncomeOffPool
		: FallbackHarvesterIncomeOffPool;

	/// <summary>
	/// How long a player's turn lasts, in seconds, as the simulation reads it.
	/// Zero means the match has no clock.
	/// </summary>
	public float EffectiveTurnClockSeconds => TurnClockSeconds >= 0f ? TurnClockSeconds : FallbackTurnClockSeconds;

	/// <summary>True when this match runs with a per-turn clock at all.</summary>
	public bool HasTurnClock => EffectiveTurnClockSeconds > 0f;

	/// <summary>
	/// Reports a rule set that has not been filled in, so a missing number is
	/// visible at match start rather than as a blast that quietly does nothing or a
	/// shot that will not move.
	/// </summary>
	public void Validate()
	{
		if (BlastOuterBandFraction < 0f)
		{
			GD.PushError("MatchRules: no blast falloff curve set (BlastOuterBandFraction is unset) — nothing takes damage from a near miss.");
		}
		if (Gravity <= 0f)
		{
			GD.PushError("MatchRules: no gravity set — shots will not fall.");
		}
		if (MaxLaunchForce <= 0f || MaxLaunchForce <= MinLaunchForce)
		{
			GD.PushError($"MatchRules: launch force range {MinLaunchForce}..{MaxLaunchForce} is not a range — shots cannot be charged.");
		}
		if (UpwardBias <= 0f)
		{
			GD.PushError("MatchRules: no upward bias set — shots will not arc.");
		}
		if (MuzzleHeight <= 0f)
		{
			GD.PushError("MatchRules: no muzzle height set — shots will start inside the structure that fired them.");
		}
		if (AimRotationSpeed <= 0f)
		{
			GD.PushError("MatchRules: no aim rotation speed set — the player cannot aim.");
		}
		if (ChargeSpeed <= 0f)
		{
			GD.PushError("MatchRules: no charge speed set — the power meter cannot fill.");
		}
		if (MaxStoredEnergy < 0)
		{
			GD.PushError("MatchRules: no maximum stored energy set — the energy economy has no cap.");
		}
		if (StartingEnergy < 0)
		{
			GD.PushError("MatchRules: no starting energy set — neither seat can build in the first round.");
		}
		if (MaxStoredEnergy >= 0 && StartingEnergy > MaxStoredEnergy)
		{
			GD.PushError($"MatchRules: starting energy {StartingEnergy} exceeds the {MaxStoredEnergy} cap.");
		}
		if (MainHubIncome < 0)
		{
			GD.PushError("MatchRules: no main-Hub income set — a chain never earns anything.");
		}
		if (HarvesterIncomeOnPool < 0 || HarvesterIncomeOffPool < 0)
		{
			GD.PushError("MatchRules: Harvester income is unset — a Harvester earns nothing either way.");
		}
		if (TurnClockSeconds < 0f)
		{
			GD.PushError("MatchRules: the turn clock is unset — set seconds, or 0 for a match with no clock.");
		}
	}

	/// <summary>
	/// The launch force a shot is given: the charge level interpolated across the
	/// force range, times how far the launcher throws relative to the match's reach
	/// (<see cref="MbcPrototype.Units.UnitDefinition.LaunchForceScale"/>).
	///
	/// A derived value and therefore a function, not a stored number (§2): every
	/// consumer that wants a reach — the launcher now, the AI's range estimate and
	/// M3's height→range curve later — asks here rather than repeating the lerp.
	/// </summary>
	/// <param name="powerPercent">Charge level, 0..1 (values outside are clamped).</param>
	/// <param name="reachScale">The launcher's reach multiplier; zero or less means "the match's own reach".</param>
	public float LaunchForce(float powerPercent, float reachScale)
	{
		float min = MinLaunchForce;
		float max = MaxLaunchForce;
		if (max <= min)
		{
			// No usable range in the resource: fall back to one, so a match with no
			// rules still shoots (see the fallbacks above).
			min = FallbackMinLaunchForce;
			max = FallbackMaxLaunchForce;
		}

		float scale = reachScale > 0f ? reachScale : 1f;
		return Mathf.Lerp(min, max, Mathf.Clamp(powerPercent, 0f, 1f)) * scale;
	}

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
