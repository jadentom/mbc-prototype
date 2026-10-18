using Godot;
using System.Collections.Generic;
using MbcPrototype.Enemies;
using MbcPrototype.Rules;
using MbcPrototype.Simulation;
using MbcPrototype.TurnSystem;
using MbcPrototype.Units;
using MbcPrototype.World;

namespace MbcPrototype.Core;

/// <summary>
/// Builds a match and holds the content it was built from (docs/parity M0.6): the
/// roster, the rules, the seed, the match's random generator, the command log, and
/// the combatants taking turns.
///
/// It is also where "how many combatants" stops being a constant. One combatant is
/// created **per seat the map defines** — seat 0 is the human's, the rest are AI —
/// so a map with three seats is a three-way match without touching this code, and
/// `1v1` is what today's map happens to say rather than something the match assumes
/// (docs/parity §1, *Built for expansion*).
/// </summary>
public partial class MatchSetup : Node
{
	[ExportGroup("Seat colours")]
	/// <summary>Minimap colour of the human's structures.</summary>
	[Export] public Color HumanSeatColor = new Color(0, 0, 0, 0.9f);
	/// <summary>Minimap colour of every other seat's structures, so the two sides can be told apart.</summary>
	[Export] public Color OpponentSeatColor = new Color(0.9f, 0.1f, 0.1f, 0.95f);

	private readonly List<Combatant> _combatants = new List<Combatant>();

	/// <summary>The roster this match draws from (null when the match has none).</summary>
	public UnitCatalog Catalog { get; private set; }

	/// <summary>The match's global rules (never null once built: a fallback stands in).</summary>
	public MatchRules Rules { get; private set; }

	/// <summary>
	/// How this match's AI seats play (docs/parity §7). One profile for every AI
	/// combatant; M6 is where a match might hand out one each.
	/// </summary>
	public AiProfile Profile { get; private set; }

	/// <summary>The seed the match runs on.</summary>
	public int Seed { get; private set; }

	/// <summary>The match's own random generator, seeded from <see cref="Seed"/>.</summary>
	public RandomNumberGenerator Random { get; private set; }

	/// <summary>The ordered log of everything this match is told to do.</summary>
	public CommandLog Commands { get; private set; }

	/// <summary>Every combatant in the match, in seat order.</summary>
	public IReadOnlyList<Combatant> Combatants => _combatants;

	/// <summary>The human's combatant: the first seat, which is player-controlled.</summary>
	public Combatant HumanCombatant => _combatants.Count > 0 ? _combatants[0] : null;

	/// <summary>
	/// Resolves the match's content and builds its combatants.
	///
	/// The fallbacks here exist so the project still runs with no assets at all —
	/// the rules fall back to their code defaults — while a missing roster is
	/// reported loudly rather than papered over with a hardcoded one, because a
	/// roster in code is exactly what M0.1 removed (§2).
	/// </summary>
	/// <param name="catalog">The roster, or null.</param>
	/// <param name="rules">The match rules, or null.</param>
	/// <param name="profile">How this match's AI seats play, or null.</param>
	/// <param name="matchSeed">The seed to run on; zero means "pick one and say so".</param>
	/// <param name="world">The world, for the seats and the starting structure.</param>
	/// <param name="structureContainer">Where spawned structures are parented.</param>
	public void Build(UnitCatalog catalog, MatchRules rules, AiProfile profile, int matchSeed, MatchWorld world, Node3D structureContainer)
	{
		Catalog = catalog;
		if (Catalog == null)
		{
			GD.PushError("MatchSetup: no UnitCatalog assigned — there is no roster to build or launch from this match.");
		}
		else
		{
			Catalog.Validate();
			GD.Print($"[Match] Unit catalog: {Catalog.Count} unit(s) — {Catalog.DescribeIds()}.");
		}

		Rules = rules;
		if (Rules == null)
		{
			GD.PushWarning("MatchSetup: no MatchRules assigned — using the code's fallback rules.");
			Rules = new MatchRules();
		}
		Rules.Validate();

		Profile = profile;
		if (Profile == null)
		{
			GD.PushWarning("MatchSetup: no AiProfile assigned — using the code's fallback, which is an enemy with no aim.");
			Profile = AiProfile.Fallback;
		}
		Profile.Validate();

		Seed = ResolveSeed(matchSeed);
		Commands = new CommandLog
		{
			Seed = Seed,
			MapId = world?.Map?.Id ?? "",
		};
		Random = new RandomNumberGenerator { Seed = (ulong)Seed };

		GD.Print($"[Match] Seed {Seed} on map '{Commands.MapId}'{(matchSeed != 0 ? "" : " (chosen)")}.");

		CreateCombatants(world, structureContainer);
	}

	/// <summary>
	/// Settles the seed: a match nobody can reproduce is a match nobody can test, so
	/// a chosen seed is printed and written into the command log.
	/// </summary>
	private static int ResolveSeed(int matchSeed)
	{
		if (matchSeed != 0) return matchSeed;

		int chosen = (int)(GD.Randi() & 0x7fffffff);
		return chosen != 0 ? chosen : 1;
	}

	/// <summary>
	/// Creates one combatant per seat the map defines and gives each its starting
	/// structure: seat 0 is the human's, every other seat is an AI on a team of its
	/// own. Today's map has two seats, which is exactly the 1v1 the first slice
	/// commits to — but the count comes from the map, not from this method.
	/// </summary>
	private void CreateCombatants(MatchWorld world, Node3D structureContainer)
	{
		int seats = Mathf.Max(1, world?.SeatCount ?? 1);

		// The structure every chain starts from, taken from the roster rather than
		// named in code. It is the Hub today; M6's setup screen is where a player
		// could choose otherwise.
		UnitDefinition startingUnit = Catalog?.Find("hub");
		if (startingUnit == null)
		{
			GD.PushWarning("MatchSetup: the catalog has no 'hub' unit — the starting chains will spawn without a definition.");
		}

		for (int seat = 0; seat < seats; seat++)
		{
			Combatant combatant = seat == 0
				? new PlayerCombatant
				{
					Name = "PlayerCombatant",
					DisplayName = "You",
					Seat = seat,
					Team = seat,
					MinimapColor = HumanSeatColor,
					RootUnit = startingUnit,
				}
				: new EnemyCombatant
				{
					Name = $"EnemyCombatant{seat}",
					DisplayName = seat == 1 ? "Enemy" : $"Enemy {seat}",
					Seat = seat,
					Team = seat,
					MinimapColor = OpponentSeatColor,
					RootUnit = startingUnit,
					// The AI's numbers are the profile's, handed straight to the
					// combatant that plays by them (docs/parity §7): they are not
					// copied onto this node first, which is how the same four
					// numbers used to exist in three places (§2).
					Profile = Profile,
				};

			// The purse and its cap are the match's rules (docs/parity M1): the
			// combatant reads them from here rather than carrying numbers of its
			// own, so changing the economy is a resource edit.
			combatant.Rules = Rules;
			combatant.BeginMatch();

			if (combatant is NodeChainCombatant chain) chain.StructureContainer = structureContainer;

			AddChild(combatant);
			_combatants.Add(combatant);

			Structure root = (combatant as NodeChainCombatant)?.SpawnRoot(world?.SeatPosition(seat) ?? Vector3.Zero);
			GD.Print($"[Match] Seat {seat} ({combatant.DisplayName}) starts at {root?.GlobalPosition} with {combatant.Energy} energy.");
		}

		GD.Print($"[Match] {_combatants.Count} combatant(s) ready on {seats} seat(s).");
	}

	/// <summary>
	/// Pays every seat its income for the round that just ended (docs/parity M1,
	/// §7): the main Hub's, plus what each Harvester earns where it stands.
	///
	/// Called by the match when the turn controller announces the end of a round —
	/// the rotation does not know what a Harvester is, and the economy does not
	/// know whose turn it is. The rules are read here, every round, so a number
	/// changed under a running match (the dev overlay, <c>--tune</c>) is felt at
	/// the next payout rather than at the next build.
	/// </summary>
	/// <param name="world">The world the pools are on; null means bare ground.</param>
	public void GrantRoundIncome(MatchWorld world)
	{
		foreach (Combatant combatant in _combatants)
		{
			if (combatant == null || combatant.IsDefeated) continue;

			var breakdown = new List<string>();
			int income = combatant is NodeChainCombatant chain
				? chain.ComputeIncome(Rules, world?.Pools, breakdown)
				: 0;
			int spent = combatant.EnergySpentThisRound;
			int granted = combatant.GrantIncome(income);
			combatant.ResetRoundSpending();

			string sources = breakdown.Count == 0 ? "nothing" : string.Join(", ", breakdown);
			GD.Print($"[Energy] {combatant.DisplayName}: spent {spent}, income {income} ({sources}) "
				+ $"granted {granted} → {combatant.Energy}/{combatant.MaxEnergy}.");
		}
	}
}
