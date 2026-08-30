using Godot;
using System;
using System.Collections.Generic;
using MbcPrototype.Combat;
using MbcPrototype.TurnSystem;

namespace MbcPrototype.Core;

public enum AmmoType { Node, Bomb }

public partial class GameManager : Node
{
	public static GameManager Instance;

	[ExportGroup("References")]
	[Export] public Camera3D MainCamera;
	[Export] public ProgressBar PowerBar;

	[ExportGroup("Aiming Settings")]
	[Export] public float RotationSpeed = 3.0f;
	[Export] public float UpwardBias = 5f;

	[ExportGroup("Power Settings")]
	[Export] public float MinLaunchForce = 4.0f;
	[Export] public float MaxLaunchForce = 20.0f;
	[Export] public float ChargeSpeed = 1.0f; // 1.0 = fills in 1 second

	[ExportGroup("Camera Settings")]
	[Export] public float CameraSmoothTime = 0.5f;

	[ExportGroup("Camera Panning")]
	[Export] public float PanSpeed = 60.0f;
	[Export] public int PanEdgeMargin = 48;
	[Export] public Vector2 PanBoundsMin = new Vector2(-5000, -5000);
	[Export] public Vector2 PanBoundsMax = new Vector2(5000, 5000);

	[ExportGroup("Minimap")]
	[Export] public Vector2 MinimapCenter = new Vector2(0, 0);
	// World side length shown. 1500^2 -> 475^2 cuts the covered area ~10x
	// (2250000 vs 225625) while keeping the on-screen widget small.
	[Export] public float MinimapWorldSize = 475.0f;
	// On-screen minimap side as a fraction of the window's shorter side
	// (e.g. 0.25 -> 162px at 648px window height, 270px at 1080p).
	[Export(PropertyHint.Range, "0.05, 0.5")]
	public float MinimapScreenFraction = 0.25f;
	[Export] public Color MinimapViewRectColor = new Color(1, 1, 1, 0.4f);
	[Export] public Vector2 MinimapMarkerSize = new Vector2(3, 3);
	[Export] public Color MinimapNodeColor = new Color(0, 0, 0, 0.9f);
	[Export] public Color MinimapSelectedNodeColor = new Color(1, 1, 0, 1);
	[Export] public Color MinimapLineColor = new Color(0, 0, 0, 0.7f);
	[Export] public float MinimapLineWidth = 1.0f;

	[ExportGroup("UI References")]
	[Export] public TextureRect NodeIcon;
	[Export] public TextureRect BombIcon;
	[Export] public ColorRect SelectorBox;
	[Export] public Label TurnLabel;
	[Export] public Label DefeatLabel;

	[ExportGroup("Ammo Prefabs")]
	[Export] public PackedScene ProjectileScene; // TODO: Change this to an interface; this is specifically a deploying node projectile
	[Export] public PackedScene BombScene;

	public BaseNode SelectedNode;

	/// <summary>Current player turn number. Starts at 1.</summary>
	public int CurrentTurn { get; private set; } = 1;

	/// <summary>True while the player may aim / charge / select nodes / fire.</summary>
	public bool IsPlayerTurn { get; private set; } = true;

	/// <summary>
	/// True while a turn's events are still resolving (control is locked).
	/// A turn is committed the moment its first TurnEvent is registered.
	/// </summary>
	public bool IsResolvingTurn { get; private set; } = false;

	private Vector3 _cameraOffset;
	private float _currentAimAngle = 0f;
	private MeshInstance3D _aimIndicator;
	private float _currentPowerPercent = 0f;
	private bool _isCharging = false;
	private bool _isChargingUp = true;
	private AmmoType _currentAmmo = AmmoType.Node;

	// In-flight camera centering tween, killed the moment the player pans.
	private Tween _cameraTween;

	// Minimap widgets (created at runtime in CreateMinimap).
	private SubViewport _minimapViewport;
	private Camera3D _minimapCamera;
	private TextureRect _minimapRect;
	private ColorRect _minimapFrame;
	private ColorRect _minimapViewIndicator;
	private Vector2I _lastMinimapPixelSize = Vector2I.Zero;

	// Node markers on the minimap (BaseNode -> dot), reconciled every frame.
	private readonly Dictionary<BaseNode, ColorRect> _minimapMarkers = new Dictionary<BaseNode, ColorRect>();

	// Connection lines on the minimap (child node -> line to its parent),
	// reconciled every frame. Drawn under the markers via a dedicated layer.
	private readonly Dictionary<BaseNode, Line2D> _minimapLines = new Dictionary<BaseNode, Line2D>();
	private Node2D _minimapLineLayer;

	// True while the OS cursor is inside the game window. Edge panning is
	// disabled otherwise so a stale edge position never keeps panning.
	private bool _mouseInsideWindow = true;

	// Every BaseNode currently in the game, for locating surviving chain roots.
	private readonly List<BaseNode> _allNodes = new List<BaseNode>();

	// TurnEvents that must finish before the current turn's resolution completes.
	private readonly List<TurnEvent> _pendingTurnEvents = new List<TurnEvent>();

	public override void _Ready()
	{
		Instance = this;
		
		// 1. Setup Camera Offset
		if (MainCamera != null)
		{
			var startBase = GetTree().Root.FindChild("BaseNode", true, false) as Node3D;
			if (startBase != null)
				_cameraOffset = MainCamera.GlobalPosition - startBase.GlobalPosition;
			else
				_cameraOffset = new Vector3(0, 15, 15); // Standard top-down angle
		}

		CreateAimIndicator();
		CreateMinimap();
		UpdateSelectedAmmo();
		if (PowerBar != null) PowerBar.Visible = false;
		if (DefeatLabel != null) DefeatLabel.Visible = false;
		UpdateTurnLabel();

		// Track whether the OS cursor is inside the game window so edge
		// panning never triggers from a stale position after it leaves.
		Window window = GetWindow();
		window.MouseEntered += () => _mouseInsideWindow = true;
		window.MouseExited += () => _mouseInsideWindow = false;
		window.SizeChanged += UpdateMinimapLayout; // Keep the minimap sized to the window.
	}

	private void UpdateSelectedAmmo()
	{
		if (SelectorBox == null) return;

		// Determine which icon to highlight
		TextureRect targetIcon = (_currentAmmo == AmmoType.Node) ? NodeIcon : BombIcon;

		// Smoothly move the selector box to the icon's position
		Tween tween = GetTree().CreateTween();
		tween.SetTrans(Tween.TransitionType.Back);
		tween.SetEase(Tween.EaseType.Out);
		
		// We target the global_position of the icon
		tween.TweenProperty(SelectorBox, "global_position", targetIcon.GlobalPosition, 0.2f);
	}

	private void CreateAimIndicator()
	{
		_aimIndicator = new MeshInstance3D();
		
		// Create a long thin box that looks like a pointer
		var mesh = new BoxMesh { Size = new Vector3(0.2f, 0.2f, 2.0f) };
		_aimIndicator.Mesh = mesh;
		
		// Bright yellow material so it stands out
		var mat = new StandardMaterial3D { 
			AlbedoColor = new Color(1, 1, 0), 
			ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded 
		};
		_aimIndicator.MaterialOverride = mat;
		
		_aimIndicator.Visible = false;
		AddChild(_aimIndicator);
	}

	public void SelectNode(BaseNode node)
	{
		// No selecting while the turn's events are still resolving.
		if (!IsPlayerTurn) return;
		if (node == null || !IsInstanceValid(node) || node.IsDestroyed) return;

		// The previous selection may have been destroyed; only touch it
		// while it is still a valid instance.
		if (SelectedNode != null && IsInstanceValid(SelectedNode))
		{
			SelectedNode.SetHighlight(false);
		}
		SelectedNode = node;
		SelectedNode.SetHighlight(true);
		
		// Show indicator and move camera
		_aimIndicator.Visible = true;
		CenterCameraOn(node.GlobalPosition);

		// Cancel charging if we switch nodes
		_isCharging = false;
		_currentPowerPercent = 0f;
		if (PowerBar != null) PowerBar.Visible = false;
	}

	public override void _Process(double delta)
	{
		HandleCameraPanning((float)delta);
		UpdateMinimapViewIndicator();
		UpdateMinimapMarkers();
		UpdateMinimapLines();

		// Safety net: never keep control pointed at a destroyed node.
		// If the selected node dies, control reverts to the highest
		// remaining node in the chain (or ends the game in defeat if none
		// remain).
		if (SelectedNode != null && (!IsInstanceValid(SelectedNode) || SelectedNode.IsDestroyed))
		{
			if (!IsResolvingTurn)
			{
				RevertControlToHighestNode();
			}
			// During resolution the selection is re-established in CompleteTurn().
			return;
		}

		if (!IsPlayerTurn || SelectedNode == null) return;

		HandleAiming((float)delta);
		HandleFiringLogic((float)delta);
	}

	private void HandleAiming(float delta)
	{
		float input = Input.GetAxis("aim_left", "aim_right");
		_currentAimAngle += input * RotationSpeed * delta;

		// Switch Ammo Type with Q or E
		if (Input.IsActionJustPressed("switch_ammo_next") || Input.IsActionJustPressed("switch_ammo_previous"))
		{
			_currentAmmo = _currentAmmo == AmmoType.Node ? AmmoType.Bomb : AmmoType.Node;
			GD.Print("Switched to: " + _currentAmmo);
			UpdateSelectedAmmo();
		}

		_aimIndicator.GlobalPosition = SelectedNode.GlobalPosition + Vector3.Up * 1.0f;
		_aimIndicator.Rotation = new Vector3(0, _currentAimAngle, 0);
		_aimIndicator.GlobalPosition += _aimIndicator.GlobalTransform.Basis.Z * 1.5f;
	}

	private void HandleFiringLogic(float delta)
	{
		// 1. Start Charging
		if (Input.IsActionJustPressed("fire_shot"))
		{
			_isCharging = true;
			_currentPowerPercent = 0f;
			if (PowerBar != null) PowerBar.Visible = true;
		}

		// 2. While Holding
		if (_isCharging && Input.IsActionPressed("fire_shot"))
		{
			var effectiveChargeSpeed = _isChargingUp ? ChargeSpeed : -1f * ChargeSpeed;
			_currentPowerPercent += effectiveChargeSpeed * delta;
			_currentPowerPercent = Mathf.Clamp(_currentPowerPercent, 0f, 1f);
			if (_currentPowerPercent == 1f)
			{
				_isChargingUp = false;
			}
			else if (_currentPowerPercent == 0f)
			{
				_isChargingUp = true;
			}
			
			if (PowerBar != null) PowerBar.Value = _currentPowerPercent;

			// Optional: Scale the indicator arrow to show power visually in 3D
			_aimIndicator.Scale = new Vector3(1, 1, 1 + (_currentPowerPercent * 2f));
		}

		// 3. Release and Fire
		if (_isCharging && Input.IsActionJustReleased("fire_shot"))
		{
			_isChargingUp = true;
			float finalForce = Mathf.Lerp(MinLaunchForce, MaxLaunchForce, _currentPowerPercent);
			Fire(finalForce);
			
			// Reset
			_isCharging = false;
			_currentPowerPercent = 0f;
			if (PowerBar != null) PowerBar.Visible = false;
			_aimIndicator.Scale = Vector3.One;
		}
	}

	private void Fire(float force)
	{
		// Choose which scene to instantiate
		PackedScene sceneToSpawn = (_currentAmmo == AmmoType.Node) ? ProjectileScene : BombScene;
		
		var instance = sceneToSpawn.Instantiate<Node3D>();
		instance.GlobalPosition = SelectedNode.GlobalPosition + Vector3.Up * 2.0f;
		
		Vector3 launchDirection = _aimIndicator.GlobalTransform.Basis.Z.Normalized();
		Vector3 velocity = (launchDirection * force) + (Vector3.Up * UpwardBias);

		// The projectile (and everything it triggers — landing, damage,
		// destruction cascades, etc.) must fully resolve before control
		// returns to the player.
		TurnEvent turnEvent = new TurnEvent(_currentAmmo == AmmoType.Node ? "Node projectile" : "Bomb");

		// TODO: Switch this to inheritance and use an interface
		if (instance is Projectile p)
		{
			p.Velocity = velocity;
			p.CreatorNode = SelectedNode;
			p.TurnEvent = turnEvent;
		}
		else if (instance is Bomb b)
		{
			b.Velocity = velocity;
			b.TurnEvent = turnEvent;
		}
		
		GetTree().Root.AddChild(instance);

		// Committing the turn: locks control until every event resolves.
		RegisterTurnEvent(turnEvent);
	}

	// ------------------------------------------------------------------
	// Turn system
	// ------------------------------------------------------------------

	/// <summary>
	/// Registers an event that must resolve before the current turn ends.
	/// Registering the first event commits the turn and takes control away
	/// from the player; control returns when the last pending event resolves.
	/// </summary>
	public void RegisterTurnEvent(TurnEvent evt)
	{
		if (evt == null || evt.IsResolved) return;

		if (!IsResolvingTurn)
		{
			// Committing the turn: lock player control immediately.
			IsResolvingTurn = true;
			IsPlayerTurn = false;

			// Cancel any in-flight charging state.
			_isCharging = false;
			_currentPowerPercent = 0f;
			if (PowerBar != null) PowerBar.Visible = false;
			if (_aimIndicator != null) _aimIndicator.Visible = false;

			GD.Print($"[Turn {CurrentTurn}] Turn committed — control locked.");
		}

		_pendingTurnEvents.Add(evt);
		evt.Resolved += () => OnTurnEventResolved(evt);
		GD.Print($"[Turn {CurrentTurn}] Event started: {evt.Description}");
	}

	private void OnTurnEventResolved(TurnEvent evt)
	{
		if (!_pendingTurnEvents.Remove(evt)) return;
		GD.Print($"[Turn {CurrentTurn}] Event resolved: {evt.Description}");

		if (_pendingTurnEvents.Count == 0 && IsResolvingTurn)
		{
			CompleteTurn();
		}
	}

	/// <summary>
	/// Every pending event has finished: advance the turn counter and hand
	/// control back to the player. Control stays on the last selected node
	/// when it survived the turn; if it was destroyed it reverts to the
	/// highest remaining node in the chain, or the game ends in defeat if
	/// no nodes remain.
	/// </summary>
	private void CompleteTurn()
	{
		IsResolvingTurn = false;

		BaseNode target;

		// Note: a destroyed node may still be a "valid" instance at this
		// point (QueueFree removes it at the end of the frame), so check
		// the IsDestroyed flag rather than IsInstanceValid alone.
		bool selectionSurvived = SelectedNode != null
			&& IsInstanceValid(SelectedNode)
			&& !SelectedNode.IsDestroyed;

		if (selectionSurvived)
		{
			target = SelectedNode;
		}
		else
		{
			target = FindHighestRemainingNode();
			if (target == null)
			{
				Defeat();
				return;
			}
		}

		CurrentTurn++;
		UpdateTurnLabel();

		GD.Print($"[Turn {CurrentTurn}] Resolution complete — player turn begins.");
		IsPlayerTurn = true;
		SelectNode(target);
	}

	private void Defeat()
	{
		IsPlayerTurn = false;
		SelectedNode = null;
		if (_aimIndicator != null) _aimIndicator.Visible = false;
		if (DefeatLabel != null) DefeatLabel.Visible = true;
		GD.Print("DEFEAT: no nodes remain in the chain.");
	}

	/// <summary>
	/// Hands control back to the topmost surviving node of the chain.
	/// Called after destruction so control never gets stuck on a dead node.
	/// </summary>
	private void RevertControlToHighestNode()
	{
		BaseNode highest = FindHighestRemainingNode();
		if (highest == null)
		{
			Defeat();
			return;
		}
		SelectNode(highest);
	}

	/// <summary>
	/// Returns the topmost surviving node of the chain — the root, i.e. a
	/// node with no valid parent. Returns null when no nodes remain.
	/// </summary>
	private BaseNode FindHighestRemainingNode()
	{
		foreach (BaseNode node in _allNodes)
		{
			if (node == null || !IsInstanceValid(node) || node.IsDestroyed) continue;

			bool hasValidParent = node.ParentBase != null
				&& IsInstanceValid(node.ParentBase)
				&& !node.ParentBase.IsDestroyed;
			if (!hasValidParent)
			{
				return node;
			}
		}
		return null;
	}

	// ------------------------------------------------------------------
	// Node registry
	// ------------------------------------------------------------------

	public void RegisterNode(BaseNode node)
	{
		if (node != null && !_allNodes.Contains(node))
		{
			_allNodes.Add(node);
		}
	}

	public void UnregisterNode(BaseNode node)
	{
		_allNodes.Remove(node);
	}

	private void UpdateTurnLabel()
	{
		if (TurnLabel != null)
		{
			TurnLabel.Text = "Turn: " + CurrentTurn;
		}
	}

	private void CenterCameraOn(Vector3 targetPosition)
	{
		if (MainCamera == null) return;

		// Stop any in-flight panning/centering tween first.
		if (_cameraTween != null && _cameraTween.IsValid())
		{
			_cameraTween.Kill();
		}

		// Calculate destination based on ground position to keep height consistent
		Vector3 groundLevel = new Vector3(targetPosition.X, 0, targetPosition.Z);
		Vector3 destination = groundLevel + _cameraOffset;

		_cameraTween = GetTree().CreateTween();
		_cameraTween.SetTrans(Tween.TransitionType.Expo); 
		_cameraTween.SetEase(Tween.EaseType.Out);
		
		_cameraTween.TweenProperty(MainCamera, "global_position", destination, CameraSmoothTime);
		
		// Optional: Ensure camera doesn't accidentally rotate
		_cameraTween.Parallel().TweenProperty(MainCamera, "global_rotation", MainCamera.GlobalRotation, CameraSmoothTime);
	}

	// ------------------------------------------------------------------
	// Camera: minimap & screen-edge panning
	// ------------------------------------------------------------------

	/// <summary>
	/// Builds the RTS-style minimap: a SubViewport with a top-down camera
	/// rendered into a TextureRect in the bottom-right corner, plus a
	/// translucent rectangle showing the main camera's current view.
	/// Clicking the minimap centers the main camera on that spot.
	///
	/// The minimap currently shows a fixed-size subset of the (effectively
	/// infinite) ground plane — see GetMinimapWorldRect() for the single
	/// seam to swap in a real map-sized region later.
	/// </summary>
	private void CreateMinimap()
	{
		if (MainCamera == null) return;
		var uiLayer = GetNodeOrNull<CanvasLayer>("../UI");
		if (uiLayer == null) return;

		MinimapWorldSize = Mathf.Max(MinimapWorldSize, 1f);

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
			Size = MinimapWorldSize,
			Position = new Vector3(MinimapCenter.X, 2000, MinimapCenter.Y),
			RotationDegrees = new Vector3(-90, 0, 0),
			Near = 1.0f,
			Far = 4000.0f,
		};
		_minimapViewport.AddChild(_minimapCamera);
		_minimapCamera.Current = true; // Make it the viewport's active camera.
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

		// 5. Layer for parent->child connection lines. Added before any
		// marker dots exist, so lines always draw underneath the dots.
		_minimapLineLayer = new Node2D { Name = "ConnectionLines" };
		_minimapRect.AddChild(_minimapLineLayer);

		// 6. Clicking the minimap moves the camera to that spot.
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
			Rect2 worldRect = GetMinimapWorldRect();
			Vector3 target = new Vector3(
				worldRect.Position.X + nx * worldRect.Size.X,
				0f,
				worldRect.Position.Y + ny * worldRect.Size.Y);
			CenterCameraOn(target);
		}
	}

	/// <summary>
	/// The world-space XZ rectangle the minimap displays.
	///
	/// Currently a fixed-size subset of the (effectively infinite) ground
	/// plane: MinimapWorldSize wide/tall centered on MinimapCenter. When a
	/// real map with defined bounds exists, replace the body of this method
	/// with a rect derived from the map size (e.g. from PanBoundsMin/Max) so
	/// the minimap automatically scales to the whole map. The top-down camera
	/// size in CreateMinimap() must then follow the same rect.
	/// </summary>
	private Rect2 GetMinimapWorldRect()
	{
		float half = MinimapWorldSize * 0.5f;
		return new Rect2(MinimapCenter.X - half, MinimapCenter.Y - half, MinimapWorldSize, MinimapWorldSize);
	}

	/// <summary>Converts a world-space XZ position to minimap pixel coordinates.</summary>
	private Vector2 WorldToMinimap(Vector2 worldXZ)
	{
		if (_minimapRect == null) return Vector2.Zero;
		Rect2 worldRect = GetMinimapWorldRect();
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
		if (_minimapRect == null || _minimapViewIndicator == null || MainCamera == null) return;

		// Ray from the main camera through the screen center down to the ground.
		Vector3 camPos = MainCamera.GlobalPosition;
		Vector3 forward = -MainCamera.GlobalTransform.Basis.Z;
		if (Mathf.Abs(forward.Y) < 0.001f) return;
		float t = -camPos.Y / forward.Y;
		if (t < 0f) return; // Camera not looking at the ground plane.
		Vector3 groundLookAt = camPos + forward * t;

		// Project the camera's screen-space footprint onto the ground plane.
		Vector3 up = MainCamera.GlobalTransform.Basis.Y;
		Vector3 right = MainCamera.GlobalTransform.Basis.X;
		Vector2 upGround = new Vector2(up.X, up.Z);
		Vector2 rightGround = new Vector2(right.X, right.Z);
		if (upGround.LengthSquared() < 0.001f || rightGround.LengthSquared() < 0.001f) return;

		float aspect = GetViewportAspect();
		float halfHeightWorld = (MainCamera.Size * 0.5f) / upGround.Length();
		float halfWidthWorld = (MainCamera.Size * 0.5f * aspect) / rightGround.Length();

		Vector2 center = WorldToMinimap(new Vector2(groundLookAt.X, groundLookAt.Z));
		Rect2 worldRect = GetMinimapWorldRect();
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
	/// Reconciles the minimap's node markers: creates a dot for every live
	/// node (black) and highlights the selected node (yellow), and removes
	/// markers for nodes that have been destroyed. Dots are plain ColorRects
	/// clipped to the minimap, so anything outside the shown region vanishes.
	/// </summary>
	private void UpdateMinimapMarkers()
	{
		if (_minimapRect == null) return;

		foreach (BaseNode node in _allNodes)
		{
			if (node == null || !IsInstanceValid(node) || node.IsDestroyed) continue;

			if (!_minimapMarkers.TryGetValue(node, out ColorRect marker) || !IsInstanceValid(marker))
			{
				marker = new ColorRect
				{
					Size = MinimapMarkerSize,
					Color = MinimapNodeColor,
					MouseFilter = Control.MouseFilterEnum.Ignore,
				};
				_minimapRect.AddChild(marker);
				_minimapMarkers[node] = marker;
			}

			marker.Color = (node == SelectedNode) ? MinimapSelectedNodeColor : MinimapNodeColor;
			Vector2 pos = WorldToMinimap(new Vector2(node.GlobalPosition.X, node.GlobalPosition.Z));
			marker.Position = pos - marker.Size * 0.5f;
		}

		// Drop markers whose node has been destroyed, freed, or left the
		// registry since last frame.
		List<BaseNode> dead = null;
		foreach (KeyValuePair<BaseNode, ColorRect> pair in _minimapMarkers)
		{
			if (!IsInstanceValid(pair.Key) || pair.Key.IsDestroyed || !_allNodes.Contains(pair.Key))
			{
				pair.Value.QueueFree();
				if (dead == null) dead = new List<BaseNode>();
				dead.Add(pair.Key);
			}
		}
		if (dead != null)
		{
			foreach (BaseNode key in dead)
			{
				_minimapMarkers.Remove(key);
			}
		}
	}

	/// <summary>
	/// Reconciles the minimap's connection lines: a plain line for every
	/// child node with a live parent, drawn between the two dots (no
	/// directionality). Lines live in their own layer under the markers and
	/// are clipped to the minimap like everything else.
	/// </summary>
	private void UpdateMinimapLines()
	{
		if (_minimapLineLayer == null) return;

		foreach (BaseNode node in _allNodes)
		{
			if (node == null || !IsInstanceValid(node) || node.IsDestroyed) continue;
			BaseNode parent = node.ParentBase;
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

		// Drop lines whose node (or its parent) has died or left the chain.
		List<BaseNode> dead = null;
		foreach (KeyValuePair<BaseNode, Line2D> pair in _minimapLines)
		{
			BaseNode node = pair.Key;
			BaseNode parent = node?.ParentBase;
			if (!IsInstanceValid(node) || node.IsDestroyed || !_allNodes.Contains(node)
				|| parent == null || !IsInstanceValid(parent) || parent.IsDestroyed)
			{
				pair.Value.QueueFree();
				if (dead == null) dead = new List<BaseNode>();
				dead.Add(node);
			}
		}
		if (dead != null)
		{
			foreach (BaseNode key in dead)
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
		if (MainCamera == null) return;
		// Never pan from a stale position: the cursor must actually be inside
		// the game window and the window focused.
		if (!_mouseInsideWindow) return;
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
		Vector3 move = MainCamera.GlobalTransform.Basis.X * panDir.X
					 + MainCamera.GlobalTransform.Basis.Y * (-panDir.Y);
		move.Y = 0f;
		if (move.LengthSquared() < 0.0001f) return;
		move = move.Normalized();

		Vector3 newPos = MainCamera.GlobalPosition + move * (PanSpeed * edgeDepth * delta);
		newPos.X = Mathf.Clamp(newPos.X, Mathf.Min(PanBoundsMin.X, PanBoundsMax.X), Mathf.Max(PanBoundsMin.X, PanBoundsMax.X));
		newPos.Z = Mathf.Clamp(newPos.Z, Mathf.Min(PanBoundsMin.Y, PanBoundsMax.Y), Mathf.Max(PanBoundsMin.Y, PanBoundsMax.Y));
		MainCamera.GlobalPosition = newPos;
	}
}
