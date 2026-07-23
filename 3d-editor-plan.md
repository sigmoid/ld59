# 3D Editor — Plan

> **Status:** IMPLEMENTED (2026-07-22). All features in section 2 are built; see section 4 for
> what shipped and known simplifications/limitations per feature.

---

## 1. Current state (already built)

The in-game editor foundation exists in the walking sim (`ld59/UI/UI3DScene.cs` +
`ld59/WalkingSim/`). What works today:

- **Editor mode** — `F2` toggles it; forces free-fly camera.
- **Camera** — right-drag to look, WASD to fly (left-click stays free for the editor).
- **Selection** — left-click picks the mesh entity under the cursor. GPU color-picking: each
  entity is drawn to an offscreen ID buffer in a unique color (its index packed across R/G/B =
  24-bit, ~16M ids), the pixel under the cursor is read back and mapped to the entity. Occlusion-
  correct (real depth), pixel-exact. Only entities with renderable geometry draw into it — so
  lights/spawns aren't pickable this way yet (that's what the light-billboard-into-id-buffer
  feature solves).
- **Inspector** — reflection-driven panel; edits `Position` + any component's serializable
  props (incl. `Mesh3DComponent.NoCollide`, light Range/Intensity/Color). Commits on Enter.
- **Navmesh** — `B` bakes in-process from live scene geometry (DotRecast), writes the OBJ,
  swaps it into the walker. `N` toggles a translucent + wireframe overlay.
- **Delete** — `Delete` removes the selected entity.

Reusable engine systems the editor leans on:

- `Quartz.UI` widgets (`Window`, `Button`, `TextInput`, `Slider`, `Label`, `ScrollArea`, layouts).
- Reflection (de)serialization: `ComponentSerializer.AutoSerialize`, `EntityFactory._componentTypes`
  (registry of every `Component` type), `Entity.Serialize()`.
- `Scene` add/remove/query; `SceneNavBaker` (in-process bake); `NavMeshDebugRenderer` (overlay).

Known gaps in the current build: only mesh entities are selectable (lights aren't pickable);
inspector doesn't scroll; no save-to-XML yet; no undo; no gizmos; no model browser/placement.

---

## 2. Features (YOU fill this in)

* Entity inspector (already partially implemented)
  * should be able to see and edit public properties of components attached to an entity
  * include entity name
  * should be able to add and remove components from individual entities
* Editor gizmos 
  * move/scale/rotate (should be able to use traditional qwer buttons to select from these)
  * light gizmo billboarded plane to show where lights are, these should be selectable as well
  * This should be integrated into the way we pick meshes, we render a sphere or circle into the id buffer for selection
* Content browser
  * Browse prefabs
  * browse models
* Prefab system - this already mostly exists
  * You can specify entities with text, there should be a library of prefabs that are defined in text files with components and params
* Scene panel (hierarchy view) - displays the objects in the scene in a list view
* Ability to start game from the point of the camera - will spawn the player at the camera position
* Navmesh window
  * button to bake a new navmesh
* Navmesh obstacle component
  * this is a new component that will be added to any object that should be considered as part of the navmesh bake. any object without this component should be ignored.
---

## 3. Open questions / decisions

> Things to settle before or during planning (I'll propose defaults; you confirm).

- Save target & format: scene XML in the Content source dir? (matches current pipeline) 
Yes, use the XML format.
- Undo model: command stack vs. snapshot?
Command stack is what I'm used to
- Selection for non-mesh entities (lights, spawn points): hierarchy list, or in-world gizmo icons?
There will be in world gizmos and also these objects will show up in the hierarchy list
- Placement reference: raycast onto navmesh/ground, camera-focus point, or fixed distance?
This is tough because building raycasting would involve a big feature addition, I think fixed distance is fine for now. We will use the gizmos to move objects around absolutely and we can already select meshes individually.
- Does this stay in-game (F2 overlay) or become a separate editor build/mode?
Stays as part of the game

---

## 4. Implementation Plan

Everything lives in the game (`F2` overlay). New editor code goes under `ld59/UI/Editor/`.
Scenes save as XML to the Content **source** dir. Mutations route through a command stack (undo).

### 4.0 Foundations (build first — most features depend on these)

**F1. Command stack (undo/redo)**
- `IEditorCommand { void Do(); void Undo(); string Label; }` + `EditorHistory` (undo/redo stacks,
  `Ctrl+Z` / `Ctrl+Y`).
- Concrete commands added as features need them: `SetPropertyCommand`, `TransformCommand`,
  `AddEntityCommand`, `DeleteEntityCommand`, `AddComponentCommand`, `RemoveComponentCommand`.
- Every mutation (inspector edit, gizmo drag, delete, spawn) goes through this — retrofit the
  existing delete + inspector edits onto it.
- New: `Editor/EditorHistory.cs`, `Editor/Commands/*.cs`. Reuse: none.

**F2. `EditorSelection` (extract + generalize)**
- Pull the current selection state out of `UI3DScene` into `Editor/EditorSelection.cs`: holds the
  selected `Entity`, exposes `OnChanged`. Fed from three sources — viewport mesh pick (exists),
  light-billboard pick (Phase 5), hierarchy click (Phase 1).
- Reuse: existing `PickSelection` / `SetSelected` (move highlight logic here).

**F3. Scene save/load to XML**
- Serialize with the existing `Entity.Serialize()` → `<Scene><Entities>…`; `Ctrl+S` + toolbar button.
- Resolve the Content **source** dir at runtime: search upward from `AppContext.BaseDirectory`
  for `ld59/Content/files/scenes` (bin is the fallback). Store once in `EditorState`.
- Load already exists (`Scene.FromFile`). Also (re)write the navmesh OBJ next to it (already wired).
- New: `Editor/SceneWriter.cs`. **Risk:** source-path discovery — validate on a real run; if it
  can't find source, fall back to runtime content dir + warn.

### 4.1 Scene hierarchy panel  *(quick win — unlocks non-mesh selection immediately)*
- `Editor/EditorHierarchyPanel.cs`: a `Window` + `ScrollArea` listing **every** scene entity
  (mesh, light, spawn) as clickable `Button` rows; click → `EditorSelection`. Highlight the
  selected row. Right-side or left-side dock.
- Needs a public entity enumerator on `Scene` (add `IReadOnlyList<Entity> Entities`).
- Reuse: `ScrollArea`, `Button`. Delete key already works on the selection.

### 4.2 Inspector upgrade
- Editable **entity Name** (`TextInput` at top) + wrap rows in a `ScrollArea` (fixes overflow).
- **Add/remove components**: an "Add Component" button → dropdown/list built from
  `EntityFactory`'s component-type registry (expose it); each component header gets a small "✕"
  remove button. Both via `AddComponentCommand` / `RemoveComponentCommand`.
- Route field edits through `SetPropertyCommand` (undo).
- Reuse/extend: existing `EditorInspector.cs`, `ScrollArea`. **Change needed:** make
  `EntityFactory._componentTypes` queryable (a public `IEnumerable<string> KnownComponentTypes`).

### 4.3 NavMeshObstacle component + navmesh window
- New `NavMeshObstacleComponent` (empty serializable marker) in `WalkingSim/`.
- **Invert the baker's gather rule**: `SceneNavBaker` collects geometry **only** from entities
  that have `NavMeshObstacleComponent` (today it takes all `Mesh3D` minus `NoCollide`). Deprecate
  `NoCollide`. Add a one-shot "mark all meshes as obstacles" helper for migrating existing scenes.
- `Editor/EditorNavmeshPanel.cs`: **Bake** button (calls existing bake), shows src→nav tri counts,
  overlay toggle. `B` stays as the shortcut.
- Reuse: `SceneNavBaker`, `NavMeshDebugRenderer`. Small, high-value; do early.

### 4.4 Transform gizmos  *(the big one)*
- `Editor/Gizmos/TransformGizmo.cs`: draws handles at the selected entity —
  translate (3 axis arrows), rotate (3 rings), scale (3 axis boxes). **QWER** picks mode
  (Q select / W move / E rotate / R scale).
- **Picking = same ID buffer**: render gizmo handles into the ID buffer in a **reserved id range**
  (e.g. `0xFFFF00 | axisIndex`) so a click hit-tests handles before entities; a handle hit starts
  a drag. (This is your "render a shape into the id buffer" approach, extended to handles.)
- **Drag math (no raycast — fixed-distance decision):** translate by projecting the mouse delta
  onto the handle's axis in screen space; rotate by mouse delta around the ring; scale by drag
  distance. Absolute manipulation. Commit one `TransformCommand` on mouse-up (undo).
- New: `Gizmos/TransformGizmo.cs`, `Gizmos/GizmoRenderer.cs`. Reuse: ID-buffer path, `EditorHistory`.
- **Risk:** screen-space axis-projection drag is the fiddliest part — ship **translate first**,
  then rotate/scale. Rotate/scale can iterate without blocking anything else.

### 4.5 Light (billboard) gizmos + non-mesh viewport picking
- `Editor/Gizmos/BillboardGizmoRenderer.cs`: a camera-facing quad/icon at each light (and spawn)
  entity, drawn in the main pass **and** into the ID buffer under that entity's id → lights become
  click-selectable in the viewport (not just the hierarchy).
- Reuse: ID buffer, `PointLightComponent`/`DirectionalLightComponent` positions, `EncodeId`.

### 4.6 Prefab library + content browser + placement
- **Prefabs**: a `Content/files/prefabs/*.xml` folder, each an entity definition (components +
  params). Loading already works via `EntityFactory` (`PrefabPath` / `FromContentFile`).
- `Editor/EditorContentBrowser.cs`: two tabs — **Prefabs** (list the prefab files) and **Models**
  (list built models under `Content/models`). Click → spawn.
- **Placement (fixed distance):** new entity is dropped a fixed distance in front of the camera
  (`CameraPosition + forward * d`), then moved with gizmos. Models spawn as a `Mesh3D` entity;
  prefabs via `EntityFactory.FromContentFile`. Via `AddEntityCommand` (undo).
- Reuse: `EntityFactory`, `Scene.AddEntity`, existing prefab system.

### 4.7 Start game from camera
- Toolbar button / hotkey: set the walker spawn to the current camera position (snapped to the
  nearest navmesh point), update/create the `PlayerStart` entity, exit editor, enter Walk.
- Reuse: `WalkController.Spawn`/`Rebind`, `NavMesh.FindTriangle` + a nearest-triangle fallback,
  existing `F2` mode toggle.

### New systems / components summary

| New file / type | Feature | Notes |
|---|---|---|
| `Editor/EditorHistory.cs` + `Commands/*` | undo/redo | foundation |
| `Editor/EditorSelection.cs` | selection | extract from UI3DScene |
| `Editor/SceneWriter.cs` | save | `Entity.Serialize` → XML, source dir |
| `Editor/EditorHierarchyPanel.cs` | 4.1 | list all entities |
| `Editor/EditorContentBrowser.cs` | 4.6 | prefabs + models tabs |
| `Editor/EditorNavmeshPanel.cs` | 4.3 | bake button + counts |
| `Editor/Gizmos/TransformGizmo.cs` + `GizmoRenderer.cs` | 4.4 | QWER, id-buffer handles |
| `Editor/Gizmos/BillboardGizmoRenderer.cs` | 4.5 | light/spawn icons + picking |
| `WalkingSim/NavMeshObstacleComponent.cs` | 4.3 | marker; baker gathers only these |
| Extend `EditorInspector.cs` | 4.2 | name, scroll, add/remove component |

### Engine changes required (small, in `quartz`)
- `Scene`: public `Entities` enumerator (hierarchy + save).
- `EntityFactory`: expose the component-type registry (`KnownComponentTypes`) for "Add Component".
- Confirm `Scene` rebuilds `SceneLightData` from components each frame (live light edits) — verify.

### Suggested build order
1. **Foundations** — F1 command stack, F2 selection extract, F3 save/load.
2. **4.1 Hierarchy** — instant non-mesh selection + navigation.
3. **4.2 Inspector upgrade** — name, scroll, add/remove components (retrofit onto commands).
4. **4.3 NavMeshObstacle + navmesh panel** — small, fixes the "what's in the navmesh" model.
5. **4.4 Gizmos (translate → rotate → scale)** — the heavy lift; ships incrementally.
6. **4.5 Light billboards** — completes viewport picking of non-mesh entities.
7. **4.6 Content browser + placement** then **4.7 start-from-camera**.

### Top risks
- **Gizmo drag without raycast** (4.4) — screen-space projection is the trickiest math; de-risk by
  shipping translate first.
- **Source-dir path discovery** (F3) — runtime can only see the bin content dir by default; needs
  an upward search or a configured path. Validate early.
- **NavMeshObstacle migration** (4.3) — inverting the gather rule silently empties navmeshes of
  scenes authored under the old rule until meshes are tagged; provide the bulk "tag all" action.
- **255→24-bit already done**, but the gizmo-handle id range must not collide with entity ids
  (reserve the top of the 24-bit space).
