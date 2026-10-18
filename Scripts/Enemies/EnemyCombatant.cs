using Godot;
using System.Collections.Generic;
using MbcPrototype.Core;
using MbcPrototype.TurnSystem;
using MbcPrototype.Units;

namespace MbcPrototype.Enemies;

/// <summary>
/// A node-chain combatant driven by a simple AI. It is the player's mirror
/// image: the same nodes, the same roster, the same launch path, the same energy
/// budget and the same defeat rule — the only difference is that its aim and
/// power come from <see cref="ChooseAimAngle"/> and a random roll instead of
/// from input.
///
/// Every number it plays by lives in its <see cref="Profile"/> (docs/parity §7),
/// so there is no enemy tuning in this file or anywhere above it. It also plays by
/// the same turn rule as the player: **a turn is one launch** (docs/parity M1), so
/// it plans one build, releases it after the profile's think time, and its turn is
/// over when the shot resolves — the next turn belongs to the next seat. It skips
/// (forfeiting the rest of the round) when nothing in the roster is affordable.
/// It works through the catalog's buildable units in order; there is deliberately
/// no judgement in it, because docs/parity M6 is where the real planner lands.
/// </summary>
public partial class EnemyCombatant : NodeChainCombatant
{
	/// <summary>
	/// The AI's numbers: how long it thinks, how widely it sprays, and the power
	/// range it picks from. Handed in at match setup (docs/parity §7 — one home
	/// per number), and the fallbacks below stand in only for a combatant built
	/// outside a match.
	/// </summary>
	public AiProfile Profile { get; set; } = AiProfile.Fallback;

	public override bool IsPlayerControlled => false;

	/// <summary>
	/// The enemy acts from the launcher closest to its target, so its chain
	/// creeps toward the player instead of piling up around its own root. A
	/// Harvester standing in the chain is not a launcher, so it is never chosen.
	/// </summary>
	public override Structure ActionStructure
	{
		get
		{
			Structure target = PickTarget(RootPosition);
			return FindLauncherNearestTo(target?.GlobalPosition ?? RootPosition) ?? base.ActionStructure;
		}
	}

	// The action planned at the start of the turn, released when the timer runs out.
	private bool _acting;
	private float _thinkTimer;
	private UnitDefinition _plannedUnit;
	private float _plannedAimAngle;
	private float _plannedPowerPercent;

	/// <summary>Where in the catalog's buildable list the next plan picks from.</summary>
	private int _nextUnitIndex;

	/// <summary>
	/// Plans this turn's build — one launch, which is all a turn is (docs/parity
	/// M1) — or skips, when nothing in the roster is affordable ("spent out"
	/// forfeits the rest of the round).
	/// </summary>
	public override void BeginTurn()
	{
		Structure origin = ActionStructure;
		if (origin == null)
		{
			// Nothing to act from: skip instead of stalling the rotation.
			SubmitSkip();
			return;
		}

		IReadOnlyList<UnitDefinition> buildable = GameManager.Instance?.Catalog?.Buildable;
		if (buildable == null || buildable.Count == 0)
		{
			GD.PushError("EnemyCombatant: no buildable units in the match's catalog — nothing to launch.");
			SubmitSkip();
			return;
		}

		UnitDefinition planned = ChooseAffordableUnit(buildable);
		if (planned == null)
		{
			// Every unit in the roster costs more than the energy left: the purse
			// is spent out, so the enemy passes for the rest of the round and the
			// energy carries forward.
			GD.Print($"[Enemy] {DisplayName}: {Energy} energy buys nothing — skipping the rest of the round.");
			SubmitSkip();
			return;
		}

		_acting = true;
		_thinkTimer = Mathf.Max(Profile?.ThinkTime ?? 0f, 0f);

		_plannedUnit = planned;
		_plannedAimAngle = ChooseAimAngle(origin);
		_plannedPowerPercent = Random.RandfRange(Profile?.MinPowerPercent ?? 0f, Profile?.MaxPowerPercent ?? 1f);

		Preview(0f);
		GD.Print($"[Enemy] Planning {_plannedUnit.DisplayName} ({_plannedUnit.Level} energy, {Energy} left): "
			+ $"{Mathf.RadToDeg(_plannedAimAngle):0}°, power {_plannedPowerPercent:0.00}");
	}

	public override void EndTurn()
	{
		_acting = false;
	}

	public override void _Process(double delta)
	{
		if (!_acting) return;

		Structure origin = ActionStructure;
		if (origin == null)
		{
			// Lost the node it was aiming from: skip rather than stall.
			_acting = false;
			SubmitSkip();
			return;
		}

		_thinkTimer -= (float)delta;

		// Ramp the visible power up to the planned value while "thinking", so an
		// AI turn reads exactly like a player charge.
		float thinkTime = Profile?.ThinkTime ?? 0f;
		float progress = thinkTime > 0f ? Mathf.Clamp(1f - (_thinkTimer / thinkTime), 0f, 1f) : 1f;
		Preview(_plannedPowerPercent * progress);

		if (_thinkTimer > 0f) return;

		_acting = false;
		Launch(_plannedUnit, origin, _plannedAimAngle, _plannedPowerPercent);

		// If nothing could be launched (no ammo scene, dead origin) no TurnEvent
		// was registered and the rotation would wait forever — skip the turn. When
		// a shot *did* go out, the turn is over the moment it resolves: a turn is
		// one launch (docs/parity M1).
		if (!(GameManager.Instance?.IsResolvingTurn ?? false)) SubmitSkip();
	}

	/// <summary>
	/// The first unit in the roster the energy can pay for, starting where the last
	/// plan left off and wrapping. Null when the energy buys nothing at all.
	/// </summary>
	private UnitDefinition ChooseAffordableUnit(IReadOnlyList<UnitDefinition> buildable)
	{
		if (buildable.Count == 0) return null;

		for (int step = 0; step < buildable.Count; step++)
		{
			int index = (_nextUnitIndex + step) % buildable.Count;
			UnitDefinition unit = buildable[index];
			if (unit == null || !CanAfford(unit.Level)) continue;

			_nextUnitIndex = (index + 1) % buildable.Count;
			return unit;
		}
		return null;
	}

	/// <summary>
	/// Picks a random aim direction: the direction to the target, opened up by a
	/// random angle within the profile's <see cref="AiProfile.AimSpreadDegrees"/>.
	/// With a small spread the AI stays dangerous; at 180° it is pure random.
	/// </summary>
	private float ChooseAimAngle(Structure origin)
	{
		Structure target = PickTarget(RootPosition);
		if (origin == null || target == null)
		{
			return Random.RandfRange(-Mathf.Pi, Mathf.Pi);
		}

		Vector3 delta = target.GlobalPosition - origin.GlobalPosition;
		Vector3 flat = new Vector3(delta.X, 0f, delta.Z);
		if (flat.LengthSquared() < 0.0001f)
		{
			return Random.RandfRange(-Mathf.Pi, Mathf.Pi);
		}

		// The aim indicator points along +Z rotated by the aim angle, so the angle
		// facing `flat` is atan2(x, z) — the inverse of AmmoLauncher's mapping and
		// the same convention the player's aiming uses.
		float toTarget = Mathf.Atan2(flat.X, flat.Z);
		float spread = Profile?.AimSpreadDegrees ?? 180f;
		return toTarget + Mathf.DegToRad(Random.RandfRange(-spread, spread));
	}

	/// <summary>The ground position used to measure "closest" when picking targets.</summary>
	private Vector3 RootPosition
	{
		get
		{
			return (RootStructure != null && IsInstanceValid(RootStructure) && !RootStructure.IsDestroyed)
				? RootStructure.GlobalPosition
				: Vector3.Zero;
		}
	}

	/// <summary>
	/// Nearest live node of a hostile combatant — anything on another team, which
	/// covers the player and, with more enemies on the field, their allies too.
	/// Returns null when there is nothing left to shoot at.
	/// </summary>
	private Structure PickTarget(Vector3 from)
	{
		GameManager gm = GameManager.Instance;
		if (gm == null) return null;

		Structure best = null;
		float bestDistance = float.MaxValue;

		foreach (Combatant combatant in gm.Combatants)
		{
			if (combatant == this || combatant.Team == Team) continue;
			if (combatant is not NodeChainCombatant chain) continue;

			foreach (Structure node in chain.Structures)
			{
				if (node == null || !IsInstanceValid(node) || node.IsDestroyed) continue;

				float distance = from.DistanceSquaredTo(node.GlobalPosition);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = node;
				}
			}
		}

		return best;
	}

	/// <summary>
	/// The match's seeded generator: every random choice this AI makes comes from
	/// it, never from the global one, so the same seed and the same command log
	/// give the same match (docs/parity M0.5).
	/// </summary>
	private static RandomNumberGenerator Random => GameManager.Instance?.Random ?? FallbackRandom;

	/// <summary>Used only outside a match (a combatant in a test scene of its own).</summary>
	private static readonly RandomNumberGenerator FallbackRandom = new RandomNumberGenerator();

	/// <summary>Shows the shared aim arrow (and power meter) for the planned shot.</summary>
	private void Preview(float powerPercent)
	{
		GameManager.Instance?.ShowAimPreview(ActionStructure, _plannedAimAngle, powerPercent, true);
	}
}
