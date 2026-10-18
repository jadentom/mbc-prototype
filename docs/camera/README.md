# Camera

Camera system for **MbcPrototype** — centering, screen-edge panning, and the
runtime-built minimap. All of it lives in
[`Scripts/Presentation/MatchCamera.cs`](../Scripts/Presentation/MatchCamera.cs),
the match's view (docs/parity M0.6); `GameManager` hands it the main camera, the
UI layer, the world and the structures each frame, and it changes nothing about
the match.

## Main camera

- Orthographic (`projection=1`), `size=30`, ~45° tilt, looking down toward -Z.
- Its serialized `Transform3D` in `Scenes/main_scene.tscn` is easy to misread —
  verify against the runtime basis if it matters (confirmed: forward =
  `(0, -0.7071, -0.7071)`, screen-up projects to -Z on the ground).
- `_cameraOffset` is the **stand-off between the camera body and the ground point
  at the middle of the screen**, read off the camera's own transform in
  `Build()` (`TryGetGroundLookAt`, fallback `(0, 15, 15)`). It is *not* measured
  from the camera's position to the target: with the scene's ~45° tilt the camera
  hangs `(0, 10, 10)` *behind* the spot it centres, so measuring from the body put
  every centred view 50 world units — about a full screen width — to the right of
  its target, on node clicks and minimap clicks alike. `Build()` then puts the
  camera on `seat0 + offset`, so the match opens framed on the seat the map says.

## Centering

- `CenterOn(target)` tweens `MainCamera` to `target.XZ + _cameraOffset`
  (Expo-out ease over `CameraSmoothTime` seconds) and pins `global_rotation`
  so the camera never accidentally rotates. `CenterOnImmediate()` is the same
  destination with no tween, for the opening view.
- The tween is stored in `_cameraTween` and **killed whenever the player pans**,
  so panning never fights centering. `SelectStructure()`, minimap clicks and
  `FollowActingCombatant()` all route through `CenterOn()`.

## Edge panning

`HandleCameraPanning(delta)` pans in classic RTS style when the cursor is
within `PanEdgeMargin` px of a screen edge, with speed ramping on edge depth
(`PanSpeed`). Guards (all must pass or panning is skipped):

- the window must have focus;
- the OS cursor must be inside the window — tracked via
  `Window.MouseEntered/MouseExited` in `_Ready`, which prevents a stale edge
  position from panning once the cursor has left;
- no Control may be hovered (`GuiGetHoveredControl() == null`), so panning
  never fights the minimap or ammo bar.

The resulting position is clamped to `PanBoundsMin`/`PanBoundsMax`.

## Minimap

- Built **at runtime** by `CreateMinimap()` — a square `SubViewport` that
  re-renders the shared world (`OwnWorld3D=false`) through a straight-down
  orthographic camera (`Current=true` **after** `AddChild`), displayed in a
  `TextureRect` in the bottom-right corner. It is *not* stored in the scene.
- Shows the **map's bounds** — the square region `MapDefinition.Bounds`
  describes, centered on the map's centre (the development map is 475×475).
  Before M0.4 this was a `MinimapWorldSize`-square region centered on a
  `MinimapCenter` of its own, guessed at because the ground had no bounds.
- The widget scales with the game window: its side is
  `MinimapScreenFraction` (0.25) of the window's shorter side, and
  `UpdateMinimapLayout()` (called from `_Ready` and on `Window.SizeChanged`)
  matches the SubViewport resolution to the widget so it stays crisp.

Features:

- translucent white rectangle = the main camera's view footprint
  (`UpdateMinimapViewIndicator()`, which centres on the same ground point
  centring does — one answer, `TryGetGroundLookAt`, for both);
- flat pool markers = one per purple pool, in a steady colour that never pulses
  (`UpdateMinimapPoolMarkers()`, drawn under the cables and the dots). The pools'
  *3D* geometry is on `PurplePool.WorldRenderLayer`, which this camera has cleared
  from its `cull_mask`: a field of pools pulsing out of phase is noise on a map.
  Marker size is the pool's true size on the map, floored at
  `MinimapPoolMarkerSize` (3 px) so it can be seen at all;
- black dots = nodes, yellow dot = selected node (`UpdateMinimapMarkers()`,
  reconciled against `_allNodes` every frame), sized by `MinimapMarkerSize`
  (6×6 px — the map is 475 world units across a widget a quarter of the window's
  shorter side, so a small dot cannot be found at a glance);
- plain lines connecting each child dot to its parent dot
  (`UpdateMinimapLines()`, drawn under the dots on a dedicated `Line2D` layer
  — no directionality), `MinimapLineWidth` wide (2 px, matched to the dots);
- left-click centers the camera on that world spot (`OnMinimapGuiInput`).

### Scaling seam

Every world↔minimap mapping goes through `GetMinimapWorldRect()`, and both
that method and the top-down camera `Size` in `CreateMinimap()` now read
`GameManager.Map` (`MapDefinition.Bounds` and `MapDefinition.Extent`) — this was
the seam, and docs/parity M0.4 is what went through it. Changing a map's size
rescales the whole minimap from those two spots; nothing else knows the size.

## Invariants & gotchas

- **Pan vs tween**: panning kills the in-flight centering tween; never let the
  two fight.
- **The offset is a stand-off from the ground look-at point, not from the camera
  body.** A tilted orthographic camera looks at the ground at
  `position + forward × (−position.Y / forward.Y)`, which is a screen's worth of Z
  in front of where it hangs. Anything that wants "centre the view on X" (a node
  click, a minimap click, the view indicator, the opening frame) has to be in
  those terms — the two were mixed once and every centred view landed a screen to
  the right.
- **Cursor-inside-window**: edge panning only runs while the OS cursor is
  inside the window — keep that invariant when touching the camera.
- `SubViewport` minimap: `GuiDisableInput=true` and
  `PhysicsObjectPicking=false` so clicks on it never leak into 3D picking;
  handle clicks on the `TextureRect` via `GuiInput`.
- Code-built UI (the minimap) uses `SetAnchorsPreset` + explicit offsets; a
  Control under a `CanvasLayer` anchors to the viewport, not a parent Control.
- Minimap markers are plain `ColorRect`s clipped by `ClipContents=true`.
