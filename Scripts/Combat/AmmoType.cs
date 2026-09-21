namespace MbcPrototype.Combat;

/// <summary>
/// The kinds of ammo a combatant can launch. Every combatant (player or AI)
/// uses the same ammo types through the same launch path — see
/// <see cref="AmmoLauncher"/>.
/// </summary>
public enum AmmoType
{
	/// <summary>A projectile that lands and deploys a new node into the chain.</summary>
	Node,

	/// <summary>A bomb that damages the first node it hits, then explodes.</summary>
	Bomb,
}
