namespace MbcPrototype.Combat;

/// <summary>
/// The behaviour behind a unit's
/// <see cref="MbcPrototype.Units.UnitDefinition.ProjectileScene"/>: the root
/// script of an ammo scene implements this, and that is how
/// <see cref="AmmoLauncher"/> configures it without knowing what it is.
///
/// It replaces the chain of <c>if (instance is Projectile) … else if (instance
/// is Bomb)</c> the launcher used to run. A new unit is a definition plus a
/// scene whose script implements this interface — no new branch, and no enum to
/// extend.
/// </summary>
public interface ILaunchedAmmo
{
	/// <summary>
	/// Configures this ammo for flight. Called exactly once by
	/// <see cref="AmmoLauncher.Launch"/>, after the instance is created and
	/// positioned but *before* it is added to the tree — an ammo scene that
	/// needs its transform in <c>_Ready</c> (a cable measured to a parent, a
	/// spawned child placed at the impact point) can rely on that order.
	/// </summary>
	void Initialize(AmmoLaunch launch);
}
