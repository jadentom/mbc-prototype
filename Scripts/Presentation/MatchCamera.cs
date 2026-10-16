using Godot;
using System.Collections.Generic;
using System.Linq;
using MbcPrototype.Core;
using MbcPrototype.World;

namespace MbcPrototype.Presentation;

/// <summary>
/// The match's view of the world (docs/parity M0.6): the camera that follows the
/// action, the edge panning the player drives, and the minimap — widget,
/// coordinate mapping, structure markers, pool markers and connection lines.
///
/// It is presentation and nothing else. It reads the world it is given
/// (<see cref="World"/>, for the bounds and the pools) and the structures it is
/// handed each frame (<see cref="Refresh"/>), and it changes neither: nothing here
/// can move a structure, launch a unit or end a turn, which is the point of taking
/// it out of GameManager.
/// </summary>
public partial class MatchCamera : Node
{
	/// <summary>The camera this drives. Assigned by the match when it builds its parts.</summary>
	public Camera3D Camera;

	/// <summary>The scene's UI layer, which the minimap widget is parented under.</summary>
	public CanvasLayer UiLayer;

	/// <summary>The world this camera shows: the map's bounds and its pools.</summary>
	public MatchWorld World;

	[ExportGroup("Camera Settings")]
	/// <summary>Seconds a centering tween takes.</summary>
	[Export] public float CameraSmoothTime = 0.5f;

	[ExportGroup("Camera Panning")]
	/// <summary>Speed of edge panning, in world units per second.</summary>
	[Export] public float PanSpeed = 60.0f;
	/// <summary>How close to the window's edge the cursor has to be to pan, in pixels.</summary>
	[Export] public int PanEdgeMargin = 48;
	/// <summary>
	/// Furthest the camera may pan, in world XZ. Camera-only: the map's own bounds
	/// are what M3's wrapping world will clamp to.
	/// </summary>
	[Export] public Vector2 PanBoundsMin = new Vector2(-5000, -5000);
	[Export] public Vector2 PanBoundsMax = new Vector2(5000, 5000);

	[ExportGroup("Minimap")]
	// On-screen minimap side as a fraction of the window's shorter side
	// (e.g. 0.25 -> 162px at 648px window height, 270px at 1080p).
	[Export(PropertyHint.Range, "0.05, 0.5")]
	public float MinimapScreenFraction = 0.25f;
	[Export] public Color MinimapViewRectColor = new Color(1, 1, 1, 0.4f);
	// Structure dots are drawn this many minimap pixels square, centred on the
	// structure. Big enough to pick out at a glance: the whole map is 475 world
	// units across a widget a quarter of the window's shorter side.
	[Export] public Vector2 MinimapMarkerSize = new Vector2(6, 6);
	[Export] public Color MinimapStructureColor = new Color(0, 0, 0, 0.9f);
	[Export] public Color MinimapEnemyStructureColor = new Color(0.9f, 0.1f, 0.1f, 0.95f);
	[Export] public Color MinimapSelectedStructureColor = new Color(1, 1, 0, 1);
	[Export] public Color MinimapLineColor = new Color(0, 0, 0, 0.7f);
	// Chain cables on the minimap, in minimap pixels. Matched to the dots: a
	// thinner line between 6-pixel markers reads as a gap rather than as a chain.
	[Export] public float MinimapLineWidth = 2.0f;
	/// <summary>
	/// Colour the minimap draws pools in: the pool's lit colour, and constant.
	/// The minimap is a map, not a window — a field of 60-odd squares pulsing out
	/// of phase would be noise where the player wants to read positions.
	/// </summary>
	[Export] public Color MinimapPoolColor = PurplePool.LitColor;
	/// <summary>
	/// Smallest a pool marker may be drawn on the minimap, in minimap pixels. A
	/// pool is 6 world units across a 475-unit map, which is barely one pixel on
	/// the widget: without a floor the markers could not be seen at all.
	/// </summary>
	[Export] public float MinimapPoolMarkerSize = 3.0f;

	// The camera's offset from whatever it is looking at: taken from its authored
	// position measured from the seat the match opens on, so the opening view is
	// the framed one whatever the map says about where that seat is.
	private Vector3 _offset;

	// In-flight camera centering tween, killed the moment the player pans.
	private Tween _cameraTween;

	// Minimap widgets (created at runtime in CreateMinimap).
	private SubViewport _minimapViewport;
	private Camera3D _minimapCamera;
	private TextureRect _minimapRect;
	private ColorRect _minimapFrame;
	private ColorRect _minimapViewIndicator;
	private Vector2I _lastMinimapPixelSize = Vector2I.Zero;

	// Structure markers on the minimap, reconciled every frame.
	private readonly Dictionary<Structure, ColorRect> _minimapMarkers = new Dictionary<Structure, ColorRect>();

	// Pool markers on the minimap, one per pool in field order. Flat squares in
	// MinimapPoolColor: the pools' own 3D geometry is on PurplePool.WorldRenderLayer
	// and excluded from the minimap camera, so what shows here does not pulse.
	private readonly List<ColorRect> _minimapPoolMarkers = new List<ColorRect>();
	private Node2D _minimapPoolLayer;

	// Connection lines on the minimap (child structure -> line to its parent),
	// reconciled every frame. Drawn under the markers via a dedicated layer.
	private readonly Dictionary<Structure, Line2D> _minimapLines = new Dictionary<Structure, Line2D>();
	private Node2D _minimapLineLayer;

	// True while the OS cursor is inside the game window. Edge panning is
	// disabled otherwise so a stale edge position never keeps panning.
	private bool _cursorInWindow = true;

	/// <summary>
	/// Builds the minimap and settles the camera's offset from what it looks at.
	/// Called by the match once <see cref="Camera"/>, <see cref="UiLayer"/> and
	/// <see cref="World"/> are set.
	/// </summary>
	public void Build()
	{
		if (Camera != null)
		{
			Vector3 openingSeat = World != null ? World.SeatPosition(0) : Vector3.Zero;
			_offset = Camera.GlobalPosition - new Vector3(openingSeat.X, 0f, openingSeat.Z);
		}

		CreateMinimap();

		// Track whether the OS cursor is inside the game window so edge panning
		// never triggers from a stale position after it leaves, and keep the
		// minimap sized to the window.
		Window window = GetWindow();
		window.MouseEntered += () => _cursorInWindow = true;
		window.MouseExited += () => _cursorInWindow = false;
		window.SizeChanged += UpdateMinimapLayout;
	}

	/// <summary>
	/// The camera's per-frame work: the minimap reconciled against the world it is
	/// given. Panning is <see cref="Pan"/>, driven by the match only while the
	/// player has control.
	/// </summary>
	/// <param name="structures">Every live structure in the match, for the markers and the lines.</param>
	/// <param name="selected">The player's selection, which the minimap highlights.</param>
	public void Refresh(IReadOnlyList<Structure> structures, Structure selected)
	{
		UpdateMinimapViewIndicator();
		UpdateMinimapPoolMarkers();
		UpdateMinimapMarkers(structures, selected);
		UpdateMinimapLines(structures);
	}

	/// <summary>Pans the camera when the cursor sits near a window edge.</summary>
	public void Pan(float delta)
	{
		HandleCameraPanning(delta);
	}

	/// <summary>
	/// The region the minimap shows: the map's bounds, through
	/// <see cref="MatchWorld"/> (docs/parity M0.4). Every world↔minimap mapping
	/// goes through here, so changing a map's size rescales the whole minimap.
	/// </summary>
	public Rect2 WorldRect => World != null ? World.Bounds : FallbackRect;

	/// <summary>Side of the square region the minimap shows: the map's extent.</summary>
	public float Extent => World != null ? World.Extent : MatchWorld.FallbackExtent;

	private static Rect2 FallbackRect => new Rect2(
		Vector2.One * (-MatchWorld.FallbackExtent * 0.5f), Vector2.One * MatchWorld.FallbackExtent);


	/// <summary>
	/// Builds the RTS-style minimap: a SubViewport with a top-down camera
	/// rendered into a TextureRect in the bottom-right corner, plus a
	/// translucent rectangle showing the main camera's current view.
	/// Clicking the minimap centers the main camera on that spot.
	///
	/// The minimap currently shows a fixed-size subset of the (effectively
	/// infinite) ground plane — see WorldRect for the single
	/// seam to swap in a real map-sized region later.
	/// </summary>
	private void CreateMinimap()
	{
		if (Camera == null) return;
		var uiLayer = UiLayer;
		if (uiLayer == null) return;


		// 1. Off-screen SubViewport that re-renders the shared 3D world top-down.
		// Its resolution is matched to the on-screen widget by UpdateMinimapLayout().
		_minimapViewport = new SubViewport
		{
			Name = "MinimapViewport",
			Size = new Vector2I(256, 256),
			TransparentBg = false,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			GuiDisableInput = true,
			PhysicsObjectPicking = false,
			OwnWorld3D = false, // Render the same world the player sees.
		};

		_minimapCamera = new Camera3D
		{
			Name = "MinimapCamera",
			Projection = Camera3D.ProjectionType.Orthogonal,
			Size = Extent,
			Position = new Vector3(WorldRect.GetCenter().X, 2000, WorldRect.GetCenter().Y),
			RotationDegrees = new Vector3(-90, 0, 0),
			Near = 1.0f,
			Far = 4000.0f,
		};
		_minimapViewport.AddChild(_minimapCamera);
		_minimapCamera.Current = true; // Make it the viewport's active camera.

		// The pools' 3D geometry is left out of the minimap: their pulse would be
		// noise on a map, and a 6-unit pool is barely a pixel here anyway. Flat
		// markers in a steady colour are drawn instead (UpdateMinimapPoolMarkers).
		_minimapCamera.CullMask &= ~PurplePool.WorldRenderLayer;
		AddChild(_minimapViewport);

		// 2. A thin frame behind the minimap so it stands out on the ground.
		_minimapFrame = new ColorRect
		{
			Name = "MinimapFrame",
			Color = new Color(0, 0, 0, 0.6f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_minimapFrame.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		uiLayer.AddChild(_minimapFrame);

		// 3. The on-screen minimap widget (bottom-right corner).
		_minimapRect = new TextureRect
		{
			Name = "Minimap",
			Texture = _minimapViewport.GetTexture(),
			ClipContents = true,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		_minimapRect.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		uiLayer.AddChild(_minimapRect);

		// 4. Translucent rectangle showing where the main camera is looking.
		_minimapViewIndicator = new ColorRect
		{
			Name = "ViewIndicator",
			Color = MinimapViewRectColor,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_minimapRect.AddChild(_minimapViewIndicator);

		// 5. Layer for the pool markers, added before the layers below so pools sit
		// underneath the cables and the node dots: a node standing in a pool has to
		// stay readable.
		_minimapPoolLayer = new Node2D { Name = "PoolMarkers" };
		_minimapRect.AddChild(_minimapPoolLayer);

		// 6. Layer for parent->child connection lines. Added before any
		// marker dots exist, so lines always draw underneath the dots.
		_minimapLineLayer = new Node2D { Name = "ConnectionLines" };
		_minimapRect.AddChild(_minimapLineLayer);

		// 7. Clicking the minimap moves the camera to that spot.
		_minimapRect.GuiInput += OnMinimapGuiInput;

		// Size the widget (and viewport) for the current window.
		UpdateMinimapLayout();
	}

	/// <summary>
	/// Sizes the minimap to the game window: its side is
	/// MinimapScreenFraction of the window's shorter side. The SubViewport
	/// resolution is matched to the widget so the render stays crisp, and
	/// the frame hugs the widget. Runs on window resize; every minimap
	/// mapping reads _minimapRect.Size, so this is the only place that
	/// needs to know the pixel layout.
	/// </summary>
	private void UpdateMinimapLayout()
	{
		if (_minimapRect == null || _minimapViewport == null || _minimapFrame == null) return;

		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		float side = Mathf.Max(1f, Mathf.Min(viewportSize.X, viewportSize.Y) * MinimapScreenFraction);
		Vector2I pixelSize = new Vector2I(Mathf.RoundToInt(side), Mathf.RoundToInt(side));
		if (pixelSize == _lastMinimapPixelSize) return;
		_lastMinimapPixelSize = pixelSize;

		_minimapViewport.Size = pixelSize;

		_minimapRect.OffsetLeft = -pixelSize.X - 8;
		_minimapRect.OffsetTop = -pixelSize.Y - 8;
		_minimapRect.OffsetRight = -8;
		_minimapRect.OffsetBottom = -8;

		_minimapFrame.OffsetLeft = -pixelSize.X - 10;
		_minimapFrame.OffsetTop = -pixelSize.Y - 10;
		_minimapFrame.OffsetRight = -6;
		_minimapFrame.OffsetBottom = -6;
	}

	private void OnMinimapGuiInput(InputEvent @event)
	{
		if (_minimapRect == null) return;
		if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
		{
			Vector2 local = _minimapRect.GetLocalMousePosition();
			Vector2 size = _minimapRect.Size;
			if (size.X <= 0f || size.Y <= 0f) return;

			// Map the click to a world position on the ground plane.
			float nx = Mathf.Clamp(local.X / size.X, 0f, 1f);
			float ny = Mathf.Clamp(local.Y / size.Y, 0f, 1f);
			Rect2 worldRect = WorldRect;
			Vector3 target = new Vector3(
				worldRect.Position.X + nx * worldRect.Size.X,
				0f,
				worldRect.Position.Y + ny * worldRect.Size.Y);
			CenterOn(target);
		}
	}

	/// <summary>
	/// Tweens the camera to look at a world position, keeping its tilt, killing any
	/// centering already in flight so the two never fight.
	/// </summary>
	public void CenterOn(Vector3 targetPosition)
	{
		if (Camera == null) return;

		// Stop any in-flight panning/centering tween first.
		if (_cameraTween != null && _cameraTween.IsValid())
		{
			_cameraTween.Kill();
		}

		// Calculate destination based on ground position to keep height consistent
		Vector3 groundLevel = new Vector3(targetPosition.X, 0, targetPosition.Z);
		Vector3 destination = groundLevel + _offset;

		_cameraTween = GetTree().CreateTween();
		_cameraTween.SetTrans(Tween.TransitionType.Expo); 
		_cameraTween.SetEase(Tween.EaseType.Out);
		
		_cameraTween.TweenProperty(Camera, "global_position", destination, CameraSmoothTime);
		
		// Optional: Ensure camera doesn't accidentally rotate
		_cameraTween.Parallel().TweenProperty(Camera, "global_rotation", Camera.GlobalRotation, CameraSmoothTime);
	}

	/// <summary>Converts a world-space XZ position to minimap pixel coordinates.</summary>
	private Vector2 WorldToMinimap(Vector2 worldXZ)
	{
		if (_minimapRect == null) return Vector2.Zero;
		Rect2 worldRect = WorldRect;
		Vector2 normalized = new Vector2(
			(worldXZ.X - worldRect.Position.X) / worldRect.Size.X,
			(worldXZ.Y - worldRect.Position.Y) / worldRect.Size.Y);
		return new Vector2(normalized.X * _minimapRect.Size.X, normalized.Y * _minimapRect.Size.Y);
	}

	/// <summary>
	/// Positions and sizes the minimap's view indicator to match the main
	/// camera's ground footprint.
	/// </summary>
	private void UpdateMinimapViewIndicator()
	{
		if (_minimapRect == null || _minimapViewIndicator == null || Camera == null) return;

		// Ray from the main camera through the screen center down to the ground.
		Vector3 camPos = Camera.GlobalPosition;
		Vector3 forward = -Camera.GlobalTransform.Basis.Z;
		if (Mathf.Abs(forward.Y) < 0.001f) return;
		float t = -camPos.Y / forward.Y;
		if (t < 0f) return; // Camera not looking at the ground plane.
		Vector3 groundLookAt = camPos + forward * t;

		// Project the camera's screen-space footprint onto the ground plane.
		Vector3 up = Camera.GlobalTransform.Basis.Y;
		Vector3 right = Camera.GlobalTransform.Basis.X;
		Vector2 upGround = new Vector2(up.X, up.Z);
		Vector2 rightGround = new Vector2(right.X, right.Z);
		if (upGround.LengthSquared() < 0.001f || rightGround.LengthSquared() < 0.001f) return;

		float aspect = GetViewportAspect();
		float halfHeightWorld = (Camera.Size * 0.5f) / upGround.Length();
		float halfWidthWorld = (Camera.Size * 0.5f * aspect) / rightGround.Length();

		Vector2 center = WorldToMinimap(new Vector2(groundLookAt.X, groundLookAt.Z));
		Rect2 worldRect = WorldRect;
		Vector2 halfMinimap = new Vector2(
			halfWidthWorld / worldRect.Size.X * _minimapRect.Size.X,
			halfHeightWorld / worldRect.Size.Y * _minimapRect.Size.Y);

		_minimapViewIndicator.Position = center - halfMinimap;
		_minimapViewIndicator.Size = halfMinimap * 2f;
	}

	private float GetViewportAspect()
	{
		Vector2 size = GetViewport().GetVisibleRect().Size;
		return size.Y > 0f ? size.X / size.Y : 1f;
	}

	/// <summary>
	/// Reconciles the minimap's structure markers: creates a dot for every live
	/// structure (black) and highlights the selected one (yellow), and removes
	/// markers for structures that have been destroyed. Dots are plain ColorRects
	/// clipped to the minimap, so anything outside the shown region vanishes.
	/// </summary>
	private void UpdateMinimapMarkers(IReadOnlyList<Structure> structures, Structure selected)
	{
		if (_minimapRect == null) return;

		foreach (Structure node in structures)
		{
			if (node == null || !IsInstanceValid(node) || node.IsDestroyed) continue;

			if (!_minimapMarkers.TryGetValue(node, out ColorRect marker) || !IsInstanceValid(marker))
			{
				marker = new ColorRect
				{
					Size = MinimapMarkerSize,
					Color = MinimapStructureColor,
					MouseFilter = Control.MouseFilterEnum.Ignore,
				};
				_minimapRect.AddChild(marker);
				_minimapMarkers[node] = marker;
			}

			marker.Color = StructureMinimapColor(node, selected);
			Vector2 pos = WorldToMinimap(new Vector2(node.GlobalPosition.X, node.GlobalPosition.Z));
			marker.Position = pos - marker.Size * 0.5f;
		}

		// Drop markers whose node has been destroyed, freed, or left the
		// registry since last frame.
		List<Structure> dead = null;
		foreach (KeyValuePair<Structure, ColorRect> pair in _minimapMarkers)
		{
			if (!IsInstanceValid(pair.Key) || pair.Key.IsDestroyed || !structures.Contains(pair.Key))
			{
				pair.Value.QueueFree();
				if (dead == null) dead = new List<Structure>();
				dead.Add(pair.Key);
			}
		}
		if (dead != null)
		{
			foreach (Structure key in dead)
			{
				_minimapMarkers.Remove(key);
			}
		}
	}

	/// <summary>
	/// Reconciles the minimap's pool markers: one flat square per pool, in
	/// <see cref="MinimapPoolColor"/> and never pulsing.
	///
	/// The pools' own geometry is excluded from the minimap camera (see
	/// CreateMinimap), so these markers are the only pools the minimap shows — a
	/// steady version of the same purple, which is what a map wants. They are
	/// reconciled every frame like the node markers, which is also what keeps them
	/// right when the window (and so the minimap) is resized.
	/// </summary>
	private void UpdateMinimapPoolMarkers()
	{
		PoolField field = World?.Pools;
		if (_minimapRect == null || _minimapPoolLayer == null || field == null) return;
		IReadOnlyList<PurplePool> pools = field.Pools;
		if (pools.Count == 0) return;

		// World units to minimap pixels. The widget and the world rect are both
		// square, so one factor covers X and Z.
		float scale = _minimapRect.Size.X / Mathf.Max(0.01f, WorldRect.Size.X);

		for (int i = 0; i < pools.Count; i++)
		{
			if (_minimapPoolMarkers.Count <= i)
			{
				var marker = new ColorRect
				{
					Name = $"PoolMarker{i}",
					Color = MinimapPoolColor,
					MouseFilter = Control.MouseFilterEnum.Ignore,
				};
				_minimapPoolLayer.AddChild(marker);
				_minimapPoolMarkers.Add(marker);
			}

			ColorRect dot = _minimapPoolMarkers[i];
			// A pool's true size on the minimap, floored so it can be seen at all.
			float side = Mathf.Max(pools[i].Side * scale, MinimapPoolMarkerSize);
			dot.Size = new Vector2(side, side);
			Vector2 pos = WorldToMinimap(new Vector2(pools[i].GlobalPosition.X, pools[i].GlobalPosition.Z));
			dot.Position = pos - dot.Size * 0.5f;
		}
	}

	/// <summary>
	/// Reconciles the minimap's connection lines: a plain line for every
	/// structure with a live parent, drawn between the two dots (no
	/// directionality). Lines live in their own layer under the markers and
	/// are clipped to the minimap like everything else.
	/// </summary>
	private void UpdateMinimapLines(IReadOnlyList<Structure> structures)
	{
		if (_minimapLineLayer == null) return;

		foreach (Structure node in structures)
		{
			if (node == null || !IsInstanceValid(node) || node.IsDestroyed) continue;
			Structure parent = node.ParentStructure;
			if (parent == null || !IsInstanceValid(parent) || parent.IsDestroyed) continue;

			if (!_minimapLines.TryGetValue(node, out Line2D line) || !IsInstanceValid(line))
			{
				line = new Line2D
				{
					Width = MinimapLineWidth,
					DefaultColor = MinimapLineColor,
				};
				_minimapLineLayer.AddChild(line);
				_minimapLines[node] = line;
			}

			line.Points = new[]
			{
				WorldToMinimap(new Vector2(parent.GlobalPosition.X, parent.GlobalPosition.Z)),
				WorldToMinimap(new Vector2(node.GlobalPosition.X, node.GlobalPosition.Z)),
			};
		}

		// Drop lines whose structure (or its parent) has died or left the chain.
		List<Structure> dead = null;
		foreach (KeyValuePair<Structure, Line2D> pair in _minimapLines)
		{
			Structure node = pair.Key;
			Structure parent = node?.ParentStructure;
			if (!IsInstanceValid(node) || node.IsDestroyed || !structures.Contains(node)
				|| parent == null || !IsInstanceValid(parent) || parent.IsDestroyed)
			{
				pair.Value.QueueFree();
				if (dead == null) dead = new List<Structure>();
				dead.Add(node);
			}
		}
		if (dead != null)
		{
			foreach (Structure key in dead)
			{
				_minimapLines.Remove(key);
			}
		}
	}

	/// <summary>
	/// Pans the camera when the mouse cursor is near a screen edge, in
	/// classic RTS style. Speed ramps up the deeper the cursor sits inside
	/// the edge margin. Never fights a centering tween — panning wins.
	/// </summary>
	private void HandleCameraPanning(float delta)
	{
		if (Camera == null) return;
		// Never pan from a stale position: the cursor must actually be inside
		// the game window and the window focused.
		if (!_cursorInWindow) return;
		if (!GetViewport().GetWindow().HasFocus()) return;
		// Don't pan while the cursor is over UI (minimap, ammo bar, labels).
		if (GetViewport().GuiGetHoveredControl() != null) return;

		Vector2 mouse = GetViewport().GetMousePosition();
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		if (viewportSize.X <= 0f || viewportSize.Y <= 0f) return;

		float margin = PanEdgeMargin;
		Vector2 panDir = Vector2.Zero;
		float edgeDepth = 0f;

		if (mouse.X <= margin)
		{
			panDir.X = -1f;
			edgeDepth = Mathf.Max(edgeDepth, Mathf.Clamp((margin - mouse.X) / margin, 0f, 1f));
		}
		else if (mouse.X >= viewportSize.X - margin)
		{
			panDir.X = 1f;
			edgeDepth = Mathf.Max(edgeDepth, Mathf.Clamp((mouse.X - (viewportSize.X - margin)) / margin, 0f, 1f));
		}

		if (mouse.Y <= margin)
		{
			panDir.Y = -1f;
			edgeDepth = Mathf.Max(edgeDepth, Mathf.Clamp((margin - mouse.Y) / margin, 0f, 1f));
		}
		else if (mouse.Y >= viewportSize.Y - margin)
		{
			panDir.Y = 1f;
			edgeDepth = Mathf.Max(edgeDepth, Mathf.Clamp((mouse.Y - (viewportSize.Y - margin)) / margin, 0f, 1f));
		}

		if (panDir == Vector2.Zero) return;

		// Take over from any in-flight centering tween.
		if (_cameraTween != null && _cameraTween.IsValid())
		{
			_cameraTween.Kill();
		}

		// Convert the screen-space pan direction to a ground-plane direction.
		Vector3 move = Camera.GlobalTransform.Basis.X * panDir.X
					 + Camera.GlobalTransform.Basis.Y * (-panDir.Y);
		move.Y = 0f;
		if (move.LengthSquared() < 0.0001f) return;
		move = move.Normalized();

		Vector3 newPos = Camera.GlobalPosition + move * (PanSpeed * edgeDepth * delta);
		newPos.X = Mathf.Clamp(newPos.X, Mathf.Min(PanBoundsMin.X, PanBoundsMax.X), Mathf.Max(PanBoundsMin.X, PanBoundsMax.X));
		newPos.Z = Mathf.Clamp(newPos.Z, Mathf.Min(PanBoundsMin.Y, PanBoundsMax.Y), Mathf.Max(PanBoundsMin.Y, PanBoundsMax.Y));
		Camera.GlobalPosition = newPos;
	}

	/// <summary>Minimap color for a node: the selection stands out, otherwise nodes use their owner's color.</summary>
	private Color StructureMinimapColor(Structure node, Structure selected)
	{
		if (node == selected) return MinimapSelectedStructureColor;
		if (node.OwnerChain != null && IsInstanceValid(node.OwnerChain)) return node.OwnerChain.MinimapColor;
		return MinimapStructureColor;
	}

}
