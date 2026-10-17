using Godot;
using System.Collections.Generic;
using MbcPrototype.Enemies;
using MbcPrototype.Rules;
using MbcPrototype.Units;
using MbcPrototype.World;

namespace MbcPrototype.Testing;

/// <summary>
/// The developer overlay: a panel that edits the match's live numbers and writes
/// them back into the resources they came from (docs/parity M0.7, §2 rule 3).
///
/// It exists because a balance change has to take seconds. Without it the loop is
/// "edit, rebuild, play for ten minutes, hope" — pick a damage value, find a
/// target, fire, watch — and a loop that slow stops being run (that is §8's
/// "iteration is slow, so it stops"). With it the loop is: F1, pick
/// <c>bomb.Damage</c>, hold Right, close, fire. Save writes the resource, so the
/// change is still there next run; Reload throws the live edits away and re-reads
/// the file, which is what makes editing the resource *in the editor* at the same
/// time work too.
///
/// It shows whatever the match's resources export — see
/// <see cref="TuningSurface"/>, which is the same surface the headless harness
/// drives with <c>--tune</c>. The overlay is a view over it, not a second list of
/// numbers: nothing here knows what a Hub is.
///
/// F1 toggles it, and while it is open it owns the keyboard (see
/// <see cref="MbcPrototype.Core.GameManager._Process"/>, which stops reading aim
/// and charge input). The keys are raw on purpose — a dev tool should not
/// consume the game's input map, and a designer's muscle memory for A/D aiming
/// keeps working the moment the panel closes.
/// </summary>
public partial class DevOverlay : CanvasLayer
{
	/// <summary>The action that opens and closes the panel (F1 in project.godot).</summary>
	public const string ToggleAction = "dev_overlay";

	/// <summary>Width of the panel, in pixels.</summary>
	private const int PanelWidth = 460;

	/// <summary>Colour of the selected row, so the eye can follow the arrows.</summary>
	private static readonly Color SelectedColor = new Color(1f, 0.85f, 0.3f);
	private static readonly Color RowColor = new Color(0.85f, 0.87f, 0.9f);

	private TuningSurface _surface;
	private int _selected;
	private PanelContainer _panel;
	private VBoxContainer _rows;
	private ScrollContainer _scroll;
	private Label _status;

	/// <summary>True while the panel is open and taking the keyboard.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>Every number this match can have changed under it.</summary>
	public TuningSurface Surface => _surface;

	/// <summary>
	/// Builds the surface from the match's own resources and the panel that shows
	/// it. Called by <see cref="MbcPrototype.Core.GameManager"/> once the match
	/// exists, so the resources listed are the ones the simulation is running on
	/// rather than the ones the scene points at.
	/// </summary>
	public void Build(UnitCatalog catalog, MatchRules rules, AiProfile profile, MapDefinition map)
	{
		_surface = TuningSurface.For(catalog, rules, profile, map);

		BuildPanel();

		GD.Print($"[Dev] Tuning surface: {_surface.Entries.Count} number(s) — press F1. "
			+ "Arrows pick and change, Enter writes them back to the resources, R re-reads them.");
	}

	/// <summary>
	/// The whole keyboard path. It runs in <c>_Input</c>, before the game's
	/// <c>_UnhandledInput</c>, and consumes what it uses — but that is not enough
	/// on its own: aiming reads the Input singleton, which always reports the raw
	/// key state, so GameManager also stops aiming while this panel is open.
	/// </summary>
	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed(ToggleAction))
		{
			Toggle();
			GetViewport()?.SetInputAsHandled();
			return;
		}

		if (!IsOpen || @event is not InputEventKey key || !key.Pressed) return;

		// Shift steps ten times as far and Ctrl ten times as fine, which is the
		// difference between a 0..1 fraction and a gravity of 9.8.
		double factor = key.ShiftPressed ? 10.0 : (key.CtrlPressed ? 0.1 : 1.0);

		bool handled = true;
		switch (key.Keycode)
		{
			case Key.Down: Move(1); break;
			case Key.Up: Move(-1); break;
			case Key.Right: Nudge(+1, factor); break;
			case Key.Left: Nudge(-1, factor); break;
			case Key.Enter: case Key.KpEnter: Save(); break;
			case Key.R: Reload(); break;
			case Key.Escape: SetOpen(false); break;
			default: handled = false; break;
		}

		if (handled) GetViewport()?.SetInputAsHandled();
	}

	/// <summary>Opens or closes the panel.</summary>
	public void Toggle()
	{
		SetOpen(!IsOpen);
	}

	/// <summary>
	/// Shows or hides the panel. Opening leaves the selection where it was, so
	/// closing the panel to watch a shot and reopening it does not lose the
	/// designer's place in a list of thirty numbers.
	/// </summary>
	public void SetOpen(bool open)
	{
		IsOpen = open;
		if (_panel != null) _panel.Visible = open;
		if (open) Refresh();
	}

	/// <summary>Moves the selection by <paramref name="step"/> rows, wrapping at both ends.</summary>
	private void Move(int step)
	{
		if (_surface == null || _surface.Entries.Count == 0) return;

		int count = _surface.Entries.Count;
		_selected = ((_selected + step) % count + count) % count;
		Refresh();
	}

	/// <summary>
	/// Changes the selected number by <paramref name="direction"/> times its own
	/// step. The change lands on the resource immediately — the simulation reads
	/// these resources every time it fires — and only reaches the file on Save.
	/// </summary>
	private void Nudge(int direction, double factor)
	{
		TuningEntry entry = Selected;
		if (entry == null) return;

		entry.Value = entry.Read() + (direction * entry.Step * factor);

		// The value is clamped to the property's own range hint, so a row that is
		// already at its bound does not move — say so rather than looking broken.
		_status.Text = $"{entry.Describe()}   (live — Enter to save)";
		Refresh();
	}

	/// <summary>The entry the arrows are pointed at, or null before the surface is built.</summary>
	private TuningEntry Selected
	{
		get
		{
			if (_surface == null || _surface.Entries.Count == 0) return null;
			_selected = Mathf.Clamp(_selected, 0, _surface.Entries.Count - 1);
			return _surface.Entries[_selected];
		}
	}

	/// <summary>Writes every edited number back into its resource file (docs/parity §2 rule 3).</summary>
	private void Save()
	{
		if (_surface == null) return;

		bool ok = _surface.Save();
		_status.Text = ok
			? $"saved {_surface.Entries.Count} number(s) to the resources"
			: "save FAILED — see the log";
		if (ok) GD.Print($"[Dev] Saved {_surface.Entries.Count} number(s) back to their resources.");
	}

	/// <summary>Re-reads every resource from its file, discarding the live edits.</summary>
	private void Reload()
	{
		if (_surface == null) return;

		bool ok = _surface.Reload();
		_status.Text = ok ? "re-read every resource from disk" : "reload FAILED — see the log";
		if (ok) GD.Print("[Dev] Re-read every resource from disk.");
		Refresh();
	}

	/// <summary>Rebuilds every row's text: the selection moves, and so do the values.</summary>
	private void Refresh()
	{
		if (_surface == null || _rows == null) return;

		for (int i = 0; i < _rows.GetChildCount() && i < _surface.Entries.Count; i++)
		{
			if (_rows.GetChild(i) is not Label label) continue;

			TuningEntry entry = _surface.Entries[i];
			bool selected = i == _selected;

			// The target is padded rather than the property, so a column of values
			// lines up down the panel and a number that moves is easy to see move.
			label.Text = $"{(selected ? ">" : " ")} {entry.Label,-28} {entry.Read(),8:0.###}";
			label.Modulate = selected ? SelectedColor : RowColor;
		}

		if (Selected != null) _scroll?.EnsureControlVisible(_rows.GetChild(_selected) as Control);
	}

	/// <summary>
	/// Builds the panel in code. It is not authored in main_scene.tscn because it
	/// is a development tool with no reason to exist in a shipped scene, and
	/// because its rows come from whatever the resources export.
	/// </summary>
	private void BuildPanel()
	{
		_panel = new PanelContainer
		{
			Name = "DevPanel",
			Visible = false,
			// Anchored to the left edge, full height: the bottom-right is the
			// minimap's and the top-right is the turn label's.
			AnchorBottom = 1f,
			OffsetLeft = 8f,
			OffsetTop = 8f,
			OffsetRight = 8f + PanelWidth,
			OffsetBottom = -8f,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		_panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.06f, 0.08f, 0.92f),
			BorderColor = new Color(0.3f, 0.35f, 0.45f, 1f),
			ContentMarginLeft = 8f,
			ContentMarginRight = 8f,
			ContentMarginTop = 6f,
			ContentMarginBottom = 6f,
		});
		AddChild(_panel);

		var column = new VBoxContainer();
		_panel.AddChild(column);

		column.AddChild(new Label { Text = "TUNING  (F1 closes)" });
		column.AddChild(new Label
		{
			Text = "up/down pick · left/right change (Shift x10, Ctrl /10)\nEnter save to resource · R re-read from disk",
		});

		_scroll = new ScrollContainer
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		column.AddChild(_scroll);

		_rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_scroll.AddChild(_rows);

		foreach (TuningEntry entry in _surface.Entries)
		{
			_rows.AddChild(new Label { Text = entry.Label, Modulate = RowColor });
		}

		_status = new Label { Text = $"{_surface.Entries.Count} number(s) from the match's resources" };
		column.AddChild(_status);

		Refresh();
	}
}
