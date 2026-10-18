using Godot;
using System;
using System.Collections.Generic;
using MbcPrototype.Units;

namespace MbcPrototype.Presentation;

/// <summary>
/// The build menu: how a player chooses what to launch (docs/parity M1).
///
/// It is the HUD's answer to the energy budget. Because a turn is no longer one
/// shot, the player needs somewhere to browse the roster, see what they can pay
/// for, and keep building until the energy runs out — so this panel is the
/// primary affordance and the launch power meter sits under it while a unit is
/// being aimed. It has two states:
///
/// - **build** — the three pages (`1`, `3`, `7`) on top, the units of the page
///   below them as buttons (greyed out when they cost more than the player
///   holds), the energy readout and the turn clock in the header, and **End
///   turn** for a player who would rather carry energy forward than spend it;
/// - **launch** — the menu steps aside and shows which unit is about to be
///   fired, its cost, and the charge level, with **Cancel** to go back to the
///   menu without spending anything.
///
/// It is built at match start from the roster, like the minimap and the dev
/// overlay, so the units on screen and the units that can be built are the same
/// list — a unit added to the catalog gets a button on its level's page without
/// anyone editing this file, and a level nobody has a unit for gets no page.
///
/// It is a view: it owns no match state. Pressing a button only reports what
/// the player asked for, and the GameManager decides whether that is allowed
/// and what it means (it is also where the keys are read, so keyboard and mouse
/// go through one path).
/// </summary>
public partial class BuildMenu : Control
{
	/// <summary>Colour of the unit the keyboard cursor is on.</summary>
	private static readonly Color SelectedUnitColor = new Color(1f, 0.9f, 0.55f);

	/// <summary>Colour of the clock once the turn is nearly up.</summary>
	private static readonly Color UrgentClockColor = new Color(1f, 0.45f, 0.4f);

	private static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.08f, 0.92f);

	private static readonly Color PanelBorder = new Color(0.3f, 0.35f, 0.45f, 1f);

	private IReadOnlyList<UnitDefinition> _buildable = System.Array.Empty<UnitDefinition>();

	/// <summary>The pages this roster has: the distinct build levels, smallest first.</summary>
	private readonly List<int> _levels = new List<int>();

	/// <summary>The units on the page showing now, in roster order, and their buttons.</summary>
	private readonly List<UnitDefinition> _pageUnits = new List<UnitDefinition>();
	private readonly List<Button> _unitButtons = new List<Button>();
	private readonly List<Button> _pageButtons = new List<Button>();

	private int _page = 1;
	private int _cursor;
	private int _energy;
	private int _maxEnergy;
	private int _income;

	private int _shownClockSecond = int.MinValue;
	private bool _clockRunning;

	private PanelContainer _statusPanel;
	private PanelContainer _buildPanel;
	private PanelContainer _launchPanel;
	private Label _energyLabel;
	private Label _clockLabel;
	private Label _costHint;
	private Label _launchTitle;
	private Label _launchPower;
	private HBoxContainer _pageRow;
	private HBoxContainer _unitRow;

	/// <summary>A unit button was clicked (or asked for by the keyboard cursor): try to launch it.</summary>
	public event Action<UnitDefinition> UnitChosen;

	/// <summary>A page button was clicked: show that level's units.</summary>
	public event Action<int> PageChosen;

	/// <summary>End turn was clicked: the player would rather skip than spend (docs/parity M1).</summary>
	public event Action EndTurnRequested;

	/// <summary>Cancel was clicked while a unit was being aimed: back to the menu, nothing spent.</summary>
	public event Action CancelLaunchRequested;

	/// <summary>The level page showing now.</summary>
	public int Page => _page;

	/// <summary>Which unit on the page the keyboard cursor is on.</summary>
	public int Cursor => _pageUnits.Count == 0 ? 0 : ((_cursor % _pageUnits.Count) + _pageUnits.Count) % _pageUnits.Count;

	/// <summary>The unit the cursor is on, or null when this page has none.</summary>
	public UnitDefinition CursorUnit => _pageUnits.Count == 0 ? null : _pageUnits[Cursor];

	/// <summary>The page heading: "LEVEL 7 · 2 UNITS".</summary>
	public string PageTitle => $"LEVEL {_page} · {_pageUnits.Count} UNIT(S)";

	/// <summary>
	/// Builds the panel. Called once by the match, before the first turn; the
	/// roster arrives with every <see cref="ShowBuild"/> so a unit added to the
	/// catalog appears without rebuilding the panel.
	/// </summary>
	public void Build()
	{
		Name = "BuildMenu";
		SetAnchorsPreset(LayoutPreset.FullRect);
		// A full-rect Control would otherwise swallow every click meant for the
		// world; only the panels themselves take the mouse.
		MouseFilter = MouseFilterEnum.Ignore;

		// The energy/clock strip stays up through both states, so the clock and
		// the purse are readable while aiming too; it sits just above the scene's
		// power meter, which owns the bottom 20 pixels.
		_statusPanel = MakePanel(-360f, -58f, 360f, -26f);
		AddChild(_statusPanel);
		BuildStatusContents();

		_buildPanel = MakePanel(-360f, -250f, 360f, -62f);
		AddChild(_buildPanel);
		BuildPanelContents();

		_launchPanel = MakePanel(-300f, -212f, 300f, -62f);
		AddChild(_launchPanel);
		BuildLaunchContents();

		HidePanels();
	}

	/// <summary>Fills a page from the roster, selects a page, and shows the menu for a turn.</summary>
	/// <param name="buildable">The roster's buildable units (the catalog's own order).</param>
	/// <param name="energy">Energy the acting player holds.</param>
	/// <param name="maxEnergy">The cap, for the readout.</param>
	/// <param name="income">What the next round is expected to pay, for the readout.</param>
	/// <param name="preferredPage">The page the player was on, when it still exists.</param>
	public void ShowBuild(IReadOnlyList<UnitDefinition> buildable, int energy, int maxEnergy, int income, int preferredPage)
	{
		_buildable = buildable ?? System.Array.Empty<UnitDefinition>();

		_levels.Clear();
		foreach (UnitDefinition unit in _buildable)
		{
			if (unit == null || unit.Level <= 0 || _levels.Contains(unit.Level)) continue;
			_levels.Add(unit.Level);
		}
		_levels.Sort();

		_page = ChoosePage(preferredPage, energy);

		_statusPanel.Visible = true;
		_launchPanel.Visible = false;
		_buildPanel.Visible = true;

		RebuildPageButtons();
		RebuildUnitRow();
		SetEnergy(energy, maxEnergy, income);
		_cursor = FirstAffordableIndex();
		HighlightCursor();
	}

	/// <summary>
	/// Shows that a unit has been chosen and is being aimed: the menu gives way
	/// to the launch panel, and the power meter (outside this panel) takes over.
	/// </summary>
	public void ShowLaunch(UnitDefinition unit)
	{
		if (unit == null) return;

		_statusPanel.Visible = true;
		_buildPanel.Visible = false;
		_launchPanel.Visible = true;
		_launchTitle.Text = $"LAUNCHING  {unit.DisplayName}   ·   {unit.Level} energy";
		SetPower(0f);
	}

	/// <summary>Hides both states (the opponent's turn, a shot resolving, the match over).</summary>
	public void HidePanels()
	{
		if (_statusPanel != null) _statusPanel.Visible = false;
		if (_buildPanel != null) _buildPanel.Visible = false;
		if (_launchPanel != null) _launchPanel.Visible = false;
	}

	/// <summary>True while either state is on screen.</summary>
	public bool IsShowing => (_buildPanel?.Visible ?? false) || (_launchPanel?.Visible ?? false);

	/// <summary>Updates the energy line and greys out the units the player cannot pay for.</summary>
	public void SetEnergy(int energy, int maxEnergy, int income)
	{
		_energy = energy;
		_maxEnergy = maxEnergy;
		_income = income;

		if (_energyLabel != null)
		{
			_energyLabel.Text = $"Energy {energy} / {maxEnergy}     next round +{income}";
		}
		if (_costHint != null && _pageUnits.Count > 0)
		{
			_costHint.Text = $"{PageTitle}   ·   Q/E pick   ·   Enter build   ·   Space end turn";
		}

		for (int i = 0; i < _unitButtons.Count && i < _pageUnits.Count; i++)
		{
			int cost = _pageUnits[i].Level;
			_unitButtons[i].Disabled = cost <= 0 || energy < cost;
		}
	}

	/// <summary>Shows the turn clock, or blanks it when this match has none.</summary>
	/// <param name="seconds">Seconds left in the turn.</param>
	/// <param name="running">False for a match with no clock, or a clock not on this seat.</param>
	public void SetClock(float seconds, bool running)
	{
		if (_clockLabel == null) return;

		int whole = Mathf.FloorToInt(Mathf.Max(seconds, 0f));
		if (running == _clockRunning && whole == _shownClockSecond) return;

		_clockRunning = running;
		_shownClockSecond = whole;

		_clockLabel.Text = running ? $"{whole / 60}:{whole % 60:00}" : "";
		_clockLabel.Modulate = running && seconds <= 10f ? UrgentClockColor : Colors.White;
	}

	/// <summary>Shows the charge in the launch panel, so the power meter also reads as a number.</summary>
	public void SetPower(float percent)
	{
		if (_launchPower == null) return;

		_launchPower.Text = $"POWER  {Mathf.RoundToInt(Mathf.Clamp(percent, 0f, 1f) * 100f)}%";
	}

	/// <summary>Moves the keyboard cursor by <paramref name="step"/> units, wrapping at both ends.</summary>
	public void MoveCursor(int step)
	{
		if (_pageUnits.Count == 0) return;

		int count = _pageUnits.Count;
		_cursor = ((_cursor + step) % count + count) % count;
		HighlightCursor();
	}

	/// <summary>
	/// Switches to the page for <paramref name="level"/>, when the roster has one.
	/// Returns false when there is nothing to switch to, so the caller can say so.
	/// </summary>
	public bool SetPage(int level)
	{
		if (!_levels.Contains(level)) return false;

		_page = level;
		RebuildPageButtons();
		RebuildUnitRow();
		// The bindings are per page, so the disabled state has to follow the page.
		SetEnergy(_energy, _maxEnergy, _income);
		_cursor = FirstAffordableIndex();
		HighlightCursor();
		return true;
	}

	// ------------------------------------------------------------------
	// Layout
	// ------------------------------------------------------------------

	private PanelContainer MakePanel(float left, float top, float right, float bottom)
	{
		var panel = new PanelContainer
		{
			AnchorLeft = 0.5f,
			AnchorRight = 0.5f,
			AnchorTop = 1f,
			AnchorBottom = 1f,
			OffsetLeft = left,
			OffsetTop = top,
			OffsetRight = right,
			OffsetBottom = bottom,
			MouseFilter = MouseFilterEnum.Stop,
		};

		var style = new StyleBoxFlat
		{
			BgColor = PanelColor,
			BorderColor = PanelBorder,
			ContentMarginLeft = 10f,
			ContentMarginRight = 10f,
			ContentMarginTop = 8f,
			ContentMarginBottom = 8f,
		};
		style.SetBorderWidthAll(1);
		panel.AddThemeStyleboxOverride("panel", style);

		return panel;
	}

	/// <summary>The always-visible strip: energy and its next-round income on the left, the turn clock on the right.</summary>
	private void BuildStatusContents()
	{
		var header = new HBoxContainer();
		_statusPanel.AddChild(header);

		_energyLabel = new Label { Text = "Energy 0 / 0", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
		_energyLabel.AddThemeFontSizeOverride("font_size", 20);
		header.AddChild(_energyLabel);

		_clockLabel = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
		_clockLabel.AddThemeFontSizeOverride("font_size", 24);
		header.AddChild(_clockLabel);
	}

	private void BuildPanelContents()
	{
		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 6);
		_buildPanel.AddChild(column);

		// Pages: the levels the roster has a unit at (the original's 1 / 3 / 7).
		_pageRow = new HBoxContainer();
		_pageRow.AddThemeConstantOverride("separation", 6);
		column.AddChild(_pageRow);

		// The page's units.
		_unitRow = new HBoxContainer();
		_unitRow.AddThemeConstantOverride("separation", 6);
		column.AddChild(_unitRow);

		// The way out of a turn that is not going to be spent.
		var bottom = new HBoxContainer();
		bottom.AddThemeConstantOverride("separation", 10);
		column.AddChild(bottom);

		var endTurn = new Button { Text = "End turn  (Space)", CustomMinimumSize = new Vector2(160f, 0f) };
		endTurn.Pressed += () => EndTurnRequested?.Invoke();
		bottom.AddChild(endTurn);

		_costHint = new Label
		{
			Text = "",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Modulate = new Color(0.82f, 0.85f, 0.9f),
		};
		bottom.AddChild(_costHint);
	}

	private void BuildLaunchContents()
	{
		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 6);
		_launchPanel.AddChild(column);

		_launchTitle = new Label { Text = "LAUNCHING", HorizontalAlignment = HorizontalAlignment.Center };
		_launchTitle.AddThemeFontSizeOverride("font_size", 20);
		column.AddChild(_launchTitle);

		_launchPower = new Label { Text = "POWER  0%", HorizontalAlignment = HorizontalAlignment.Center };
		_launchPower.AddThemeFontSizeOverride("font_size", 18);
		column.AddChild(_launchPower);

		var hint = new Label
		{
			Text = "A/D aim   ·   hold W to charge, release to launch   ·   Esc cancels back to the menu",
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Modulate = new Color(0.82f, 0.85f, 0.9f),
		};
		column.AddChild(hint);

		var cancel = new Button { Text = "Cancel  (Esc)" };
		cancel.Pressed += () => CancelLaunchRequested?.Invoke();
		column.AddChild(cancel);
	}

	/// <summary>Rebuilds the page buttons, one per level the roster has a unit at.</summary>
	private void RebuildPageButtons()
	{
		foreach (Button button in _pageButtons)
		{
			_pageRow.RemoveChild(button);
			button.QueueFree();
		}
		_pageButtons.Clear();

		foreach (int level in _levels)
		{
			var button = new Button
			{
				Text = level.ToString(),
				ToggleMode = true,
				ButtonPressed = level == _page,
				CustomMinimumSize = new Vector2(56f, 32f),
				TooltipText = $"{level} energy units",
			};
			int captured = level;
			button.Pressed += () => PageChosen?.Invoke(captured);
			_pageRow.AddChild(button);
			_pageButtons.Add(button);
		}
	}

	/// <summary>Rebuilds the unit buttons for the page showing.</summary>
	private void RebuildUnitRow()
	{
		foreach (Button button in _unitButtons)
		{
			_unitRow.RemoveChild(button);
			button.QueueFree();
		}
		_unitButtons.Clear();
		_pageUnits.Clear();

		foreach (UnitDefinition unit in _buildable)
		{
			if (unit == null || unit.Level != _page) continue;

			int index = _pageUnits.Count;
			_pageUnits.Add(unit);

			var button = new Button
			{
				Text = $"{unit.DisplayName}  ·  {unit.Level}",
				Icon = unit.Icon,
				ExpandIcon = false,
				CustomMinimumSize = new Vector2(150f, 56f),
				TooltipText = $"{unit.DisplayName}: {unit.Level} energy"
					+ (unit.HitPoints > 0 ? $", {unit.HitPoints} hit points" : ""),
				MouseFilter = MouseFilterEnum.Stop,
			};
			button.Pressed += () =>
			{
				if (index < _pageUnits.Count) UnitChosen?.Invoke(_pageUnits[index]);
			};
			_unitRow.AddChild(button);
			_unitButtons.Add(button);
		}
	}

	/// <summary>Marks the unit under the keyboard cursor, so a keyboard player can see what Enter will build.</summary>
	private void HighlightCursor()
	{
		for (int i = 0; i < _unitButtons.Count; i++)
		{
			_unitButtons[i].Modulate = i == Cursor ? SelectedUnitColor : Colors.White;
		}
	}

	/// <summary>
	/// Picks the page to open: the one the player was on when it still has
	/// something they can pay for, otherwise the cheapest page that does.
	/// </summary>
	private int ChoosePage(int preferredPage, int energy)
	{
		if (_levels.Count == 0) return preferredPage;

		if (_levels.Contains(preferredPage) && PageHasAffordable(preferredPage, energy)) return preferredPage;

		foreach (int level in _levels)
		{
			if (PageHasAffordable(level, energy)) return level;
		}

		return _levels.Contains(preferredPage) ? preferredPage : _levels[0];
	}

	/// <summary>True when the page holds a unit this energy can pay for.</summary>
	private bool PageHasAffordable(int level, int energy)
	{
		foreach (UnitDefinition unit in _buildable)
		{
			if (unit != null && unit.Level == level && energy >= level) return true;
		}
		return false;
	}

	/// <summary>The first unit on the page the player can pay for, or 0 when none is.</summary>
	private int FirstAffordableIndex()
	{
		for (int i = 0; i < _pageUnits.Count; i++)
		{
			if (_energy >= _pageUnits[i].Level) return i;
		}
		return 0;
	}
}
