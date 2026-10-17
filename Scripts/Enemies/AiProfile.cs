using Godot;

namespace MbcPrototype.Enemies;

/// <summary>
/// How an AI combatant plays, as a <see cref="Resource"/> (docs/parity §7, *AI*):
/// every weight its planner uses lives here, so tuning the enemy is an edit to a
/// file and not a rebuild (§2).
///
/// It exists because the same four numbers used to be exported three times — on
/// <see cref="MbcPrototype.Core.GameManager"/>, on
/// <see cref="MbcPrototype.Core.MatchSetup"/> and on
/// <see cref="EnemyCombatant"/> — and injected from each into the next. That is
/// the duplication §2 calls a bug waiting to happen: sooner or later the three
/// disagree. One resource, read by the combatant itself, is one home.
///
/// M6 replaces this AI with a real planner; the profile is where its aggression,
/// expansion and target-preference weights will land, which is why it is a
/// resource of its own rather than more fields on the rules.
///
/// Field defaults are unset sentinels, not real numbers: a field Godot never
/// writes (because it equals the script default) must not be a value that means
/// something (§2, and see <see cref="MbcPrototype.Rules.MatchRules"/>'s convention).
/// </summary>
[GlobalClass]
public partial class AiProfile : Resource
{
	/// <summary>
	/// Seconds the AI spends aiming (with the power meter visibly ramping) before
	/// it releases the shot. Longer is a more watchable turn; zero fires at once.
	///
	/// Negative means "not set" and <see cref="Validate"/> reports it.
	/// </summary>
	[Export] public float ThinkTime = -1f;

	/// <summary>
	/// Half-angle of the AI's random aim cone around the direction to its target,
	/// in degrees. 0 aims dead straight and 180 fires in any direction at all —
	/// this one number is how good the enemy's aim is.
	/// </summary>
	[Export(PropertyHint.Range, "-1,180")] public float AimSpreadDegrees = -1f;

	/// <summary>Lowest launch power the AI will pick, as a fraction of the force range.</summary>
	[Export(PropertyHint.Range, "-1,1")] public float MinPowerPercent = -1f;

	/// <summary>Highest launch power the AI will pick, as a fraction of the force range.</summary>
	[Export(PropertyHint.Range, "-1,1")] public float MaxPowerPercent = -1f;

	/// <summary>True once every number above has been given a value.</summary>
	public bool IsSet => ThinkTime >= 0f
		&& AimSpreadDegrees >= 0f
		&& MinPowerPercent >= 0f
		&& MaxPowerPercent >= 0f;

	/// <summary>
	/// The profile a combatant plays by when no profile was assigned at all: it
	/// thinks for no time, sprays in any direction and fires anywhere across the
	/// force range.
	///
	/// Deliberately *degenerate* rather than good. These are not balance numbers
	/// and no resource states them (§2: one home per number) — they exist so that
	/// a match whose content was never wired up still has an enemy that takes its
	/// turn instead of one that cannot pick a shot, and
	/// <see cref="Validate"/> has already reported the missing profile.
	/// </summary>
	public static readonly AiProfile Fallback = new AiProfile
	{
		ThinkTime = 0f,
		AimSpreadDegrees = 180f,
		MinPowerPercent = 0f,
		MaxPowerPercent = 1f,
	};

	/// <summary>
	/// Reports a profile that has not been filled in, so an enemy with no aim or
	/// no planning is visible at match start rather than as a combatant that
	/// behaves oddly for reasons nobody can find.
	/// </summary>
	public void Validate()
	{
		if (ThinkTime < 0f)
		{
			GD.PushError("AiProfile: no think time set — the AI's turn has no aiming pause.");
		}
		if (AimSpreadDegrees < 0f)
		{
			GD.PushError("AiProfile: no aim spread set — the AI's aim cone is undefined.");
		}
		if (MinPowerPercent < 0f || MaxPowerPercent < 0f)
		{
			GD.PushError("AiProfile: launch power range is unset — the AI has no power to fire at.");
		}
		else if (MaxPowerPercent < MinPowerPercent)
		{
			GD.PushError($"AiProfile: launch power range {MinPowerPercent}..{MaxPowerPercent} is not a range.");
		}
	}
}
