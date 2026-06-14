# Powergrid – Design & Implementation Plan

## Overview

Powergrid is an in-game program accessible via the console. This document captures the design and serves as the planning artifact for implementing its features in LD59.

---

## Questions

### Core Gameplay

Powergrid is a game that takes place in a top down view of a directed graph of nodes. Nodes are rendered as circles. The player's goal is to get power from an anchor or root node, to the goal node. The player can get tokens by powering nodes, which reveals a token that the node 'holds'. These nodes aren't part of the grid, but they are found by activating nodes. The first phase of the game is discovering the graph. The game starts with only the root node being visible, and as the player powers nodes, they discover more nodes and tokens.

#### Goal: Arrange the grid such that power flows from a root/anchor node, to the victory node. There can be multiple root nodes.

The player's primary interaction with the system is dragging nodes on and off the graph. They have 3 temporary slots where they can hold nodes while moving others. A node can only be removed if there are no nodes that depend on its power to stay on the board.

#### Valid / Invalid moves
The player cannot make a move that would invalidate the state of the graph. The conditions of the graph are as follows.

1. For a node to be powered, there must be a path from a root node to that node. If the player tries to remove a piece that powers another piece, that move is considered invalid and is not allowed. If the player tries to place a token on a node that is not connected to a powered root, that move is also not allowed.
2. Powered lines cannot cross. If two lines physically cross eachother on the game board, only one crossing connection can be powered at a time
3. There are locks/keys in the game. A lock is applied across a connection and is related to a key node in the graph. Only when the key node is powered can power flow through a locked connection

Token types: For now there are two types of tokens which provide one and two power respectively.

#### Logic gates

The game contains logic gates such as AND and XOR. These gates take two inputs and function how you would expect them to.

### Level structure

Levels consist of multiple graphs. This is similar to panels in the witness. In order to progress to the next graph the previous one must be solved. If a panel becomes unsolved the next panels are deactivated. The player will need to be able to move a camera around 2d space to be able to see all of the cameras. Click to drag, mouse to zoom

### Power / Resource Model

The player has 3 slots at the bottom of the screen where they can temporarily store pieces that aren't required in the grid. They can use these pieces in future graphs within the same level.

### Progression & Events

13. **Does the game have levels or is it a single endless/sandbox session?**
The game has levels, we can load and list levels through the console. 
### UI & Presentation

16. **How was the UI laid out in the original jam game?** 
Top down view of the graph. The player can move a camera to see different puzzles. I'd like the camera to snap to center a puzzle when the player gets close to centering on it
17. **What information is always visible on screen vs. shown on demand?**
The player can always see the graph
18. **Does the player interact by clicking grid cells, dragging cables, clicking buttons, or some combination?**
The player interacts by dragging "power tokens" onto nodes in a graph
19. **Any sound or visual feedback that was important in the original?**
Don't worry about sound yet

### LD59 Integration

20. **How does the player open/close Powergrid?** It is a desktop program that can be manually closed by the player/user
21. **Does Powergrid run in a window inside the game's OS UI, or does it take over the whole screen?** It runs in a window
22. **Should Powergrid state persist while the window is closed, or reset each session?** Reset each session for now
23. **Does Powergrid interact with any other LD59 systems?** Not currently
24. **Are there any LD59-specific theming/aesthetic constraints?** The game applies a 1-bit dithering shader so any color will be rendered in 1 bit

### Scope

25. **Which features from the jam game are must-haves vs. nice-to-haves for this version?**
I would like to include all of the features from the game jam game
26. **Are there features you want to add that weren't in the jam game?**
Yes, but for now lets just focus on what is already there
27. **What is the target completion timeline?**
ASAP

---

## Architecture Notes (fill in during planning)

We should build components and entity descriptions for the different pieces of the game. Nodes, tokens, gates, connections

There will need to be a system for validating the graph which the game uses in deciding if a move is valid

- **Rendering approach:**

Tokens and nodes will be rendered as a circle (find this circle in the Content/images/powergrid folder)

The tokens should have a sprite font render a symbol in the center so the player knows how they function.

The gates will each have their own image in the Content/images/powergrid folder

Connections should be displayed as lines rendered. They should change color when powered/not powered. the direction should be visible. There should be arrows showing which direction the power can flow. It can be bidirectional for a pair of nodes

---

---

# Implementation Plan

> Authored after studying the original Unity source (`f:\Dev\Powergrid\Assets\Scripts`) and the LD59/Quartz UI framework. This is the build plan of record.

## Decisions (locked in)

1. **Levels are authored fresh in a new in-game editor.** The original levels exist only as Unity scenes/prefabs and cannot be imported. We rebuild content with the editor.
2. **Both token models are supported:** a fixed inventory of draggable power tokens (the jam-game model) *and* nodes that hold hidden tokens revealed when powered (the discovery model).
3. **Editor-first.** We build the shared data model + rendering, then the editor (so content can be authored), then play/validation, then multi-puzzle levels and polish.
4. **Levels are stored as Quartz Scene XML** (`SceneSerializer` / `Scene.SerializeToFile` / `Scene.FromFile`) — *not* a new JSON format. Game objects are modeled as `Entity` + custom `Component` types so they round-trip through the existing serializer. The current `PowergridUI` stub already wires `Scene` save/load and the console handler already lists `*.xml`.

## What ports from the original (and where it lives now)

The original logic is sound and ports almost directly. Key pieces from `f:\Dev\Powergrid\Assets\Scripts`:

| Original (Unity) | Role | New home |
|---|---|---|
| `PowerSlot` | Node: holds a token, reports power, drop target | `PowerNode` (engine) |
| `AndGate` / `XORGate` | Gate nodes overriding `GetPower`/`ConvertPower` | `PowerNode` with `NodeKind` enum |
| `LevelManager` | Per-puzzle graph: distribute power, validate moves, generate lines, detect crossings/locks, solved-state | `PuzzleGraph` (engine) |
| `ConnectionLineSegment` | Directed edge w/ competing (crossing) segments + locks | `Connection` (engine) |
| `Lock` / `Key` | Locked edge tied to a key node | `EdgeLock` + key-node reference |
| `DragDropController` / `DraggableObject` / `IDroppable` | Mouse drag of tokens onto nodes | `PowergridView` mouse handling |
| `CameraController` | Pan/zoom orthographic camera | `PowergridCamera` (2D pan/zoom + snap) |
| `LevelWire` + `OnLevelSolved`/`OnLevelUnSolved` | Witness-style panel activation chaining | `PowergridLevel` activation graph |
| `InventoryPlacement` | Screen-anchored token tray | Inventory bar UI |

**Core algorithms to preserve verbatim (they are subtle and correct):**
- `Distribute` — recursive power propagation with depth = token power.
- `CanAddPower` / `CanRemovePower` — move validity (no orphaned powered nodes; no illegal crossings).
- `CanAnchorFind` / `CanAnchorFindTwice` / `CanAnchorExactlyOnce` — reachability used by placement + AND/XOR gates.
- `Search` — depth-limited DFS respecting locks and active crossing conflicts.
- `HasOverlaps` — rejects placements that would force a powered line crossing.
- `DoLinesIntersect` — segment-intersection test driving both crossings and lock placement.

## Architecture

Three concerns, cleanly separated: **serialized data** (components on entities), **game logic** (a controller that runs graph algorithms over those components), and **presentation** (the view). This is the main cleanup vs. the jam game, where data, logic, and `MonoBehaviour`/rendering were entangled. It also mirrors the original's actual shape: `LevelManager` read `PowerSlot`s out of the scene, exactly as our controller reads components out of the `Scene`.

### 1. Serialized data layer — Entities + custom Components
Each game object is an `Entity` carrying one or more `Component`s (subclass `Quartz.Components.Component`; auto-discovered by reflection, auto-serialized). Entity `LocalPosition` holds the node's graph position.

- `PowerNodeComponent` — serialized props: `NodeKind { Normal, And, Xor }`, `IsAnchor`, `IsGoal`, `AnchorPower` (int), `HeldTokenPower` (int; 0 = none, for the discovery model). Outgoing connection targets (a list of node entity **names**) are emitted via `SerializeData()`/`DeserializeData()` since the auto-serializer can't do collections or references. **Runtime-only state** (`Removed`, placed-token power, `_powerFrom` set, `IsActive`) lives in **plain fields, not public properties**, so the auto-serializer ignores it (note: `[ComponentIgnore]` is *not* honored by `AutoSerialize`).
- `EdgeLockComponent` — serialized props: two segment points (`Vector2 PointA`, `Vector2 PointB`), key node **name**. `IsLocked` is runtime state derived from the key node.
- `PowergridLevelComponent` — one per level (on a config entity): holding-slot count, fixed inventory token powers, and the puzzle activation chain. Lists/maps go through `SerializeData()`.
- A **sub-puzzle** is an `Entity` (parent) tagged `"puzzle"` with its nodes/locks as child entities; or carries a `PuzzleComponent` holding its id + activation dependencies.
- Tokens/gates also get a `SpriteComponent` for their circle/gate art.

### 2. Logic layer — controller (reconstructed each session, not serialized)
- `PuzzleGraph` — built from one puzzle's node/lock components after load; ports `LevelManager`'s validation + distribution. Holds the resolved `Connection` list (directed `From`/`To`, cached `StartPos`/`EndPos`, `IsActive`, `CompetingConnections`, `Locks`). Exposes `IsSolved`, `CanAddPower`, `CanRemovePower`, `TryPlace`, `TryRemove`.
- `PowergridLevelController` — owns the `Scene`, runs the **post-load resolve pass** (turn node/lock **name** references into live component references — required because entity Guids are regenerated on load and not serialized), builds a `PuzzleGraph` per sub-puzzle, drives activation chaining (solve N → enable dependents; unsolve → disable downstream), and carries the shared inventory + 3 holding slots across puzzles.
- `Geometry` — `DoLinesIntersect`, line/point helpers (ported).

### 3. Presentation layer (XNA / Quartz)
- `PowergridUI : UIPanel` — the program window. Keeps the existing stub's `Scene` save/load wiring but **swaps the 3D `UI3DScene` for the 2D `PowergridView`**. Hosts the view, inventory bar, edit-mode toggle, status text. Registers in taskbar like `Minefield`/`Solitaire`.
- `PowergridView : UIElement` — scissor-clipped viewport (same pattern as `SolitaireContentPanel`: `spriteBatch.End()` → set `ScissorRectangle` → re-`Begin` with scissor rasterizer). Reads node positions/state from the `Scene`'s components via the controller. Owns the 2D camera, world↔screen transform, hit-testing, token drag/drop, and all drawing of nodes/edges/arrows/gates/locks/tokens.
- `PowergridCamera` — `Pan`, `Zoom`; `WorldToScreen`/`ScreenToWorld`; click-drag to pan, wheel to zoom; snap-to-center when near a puzzle's centroid.
- `InventoryBar : UIElement` — 3 holding slots + available tokens at the bottom of the window.
- `PowergridEditor` — edit-mode controller + palette overlay (spawn nodes/gates/tokens, connect directed edges, move, place locks, define/link sub-puzzles, save/load).

### Rendering notes (1-bit constraint)
- The game applies a **global 1-bit dithering post-process** (`OneBitDitheringPostProcessEffect`, wired in `Game1.cs`) — so we draw with normal colors and the whole frame is dithered. **Implication:** "powered vs unpowered" cannot rely on hue; it must differ in **brightness or pattern** (e.g. bright/solid powered line vs. dim/dashed unpowered) to survive 1-bit reduction. Same for node on/off states.
- Nodes/tokens use `Content/images/powergrid/circle-filled.png` and `circle-outlined.png`. Tokens render a symbol in the center via `Core.DefaultFont` (1 = single dot/“I”, 2 = “II”, etc.).
- Gates each get their own sprite in `Content/images/powergrid/` (to be added: `and.png`, `xor.png`).
- **Node roles must be visually distinct.** Anchor nodes draw an `anchor.png` marker (user-supplied, in `Content/images/powergrid/`) beside/over the node so it reads unmistakably as a power source. Goal/victory nodes get an equivalent distinct marker (sprite TBD). Markers are view-layer overlays keyed off `PowerNodeComponent.IsAnchor` / `IsGoal` — drawn *in addition to* the circle sprite so powered/unpowered state still reads through.
- Connections draw as rotated 1px lines (port the `DrawLine` helper from `PinballEngine`) with a direction **arrowhead**; bidirectional pairs draw arrows both ways.

## Level file format

**Quartz Scene XML** via the existing `SceneSerializer` / `Scene.SerializeToFile` / `Scene.FromFile`, stored under `Content/files/scenes/powergrid/<name>.xml` (already where the console handler looks — no change needed there). A level is one `Scene`; each game object is an `Entity` with our custom components. Sketch of the serialized shape:

```xml
<Scene>
  <Entities>
    <Entity Name="level">
      <Position><X>0</X><Y>0</Y></Position>
      <Component Type="PowergridLevelComponent">
        <Property Name="HoldingSlots" Value="3" Type="int" />
        <Data>inventory:1,1,2;activations:p0=>p1</Data>
      </Component>
    </Entity>

    <Entity Name="n0">
      <Position><X>0</X><Y>0</Y></Position>
      <Tag>puzzle:p0</Tag>
      <Component Type="PowerNodeComponent">
        <Property Name="NodeKind" Value="Normal" Type="enum" />
        <Property Name="IsAnchor" Value="true" Type="bool" />
        <Property Name="AnchorPower" Value="2" Type="int" />
        <Data>out:n1</Data>            <!-- outgoing connection target names -->
      </Component>
      <Component Type="SpriteComponent">
        <Property Name="Texture" Value="images/powergrid/circle-filled" Type="string" />
      </Component>
    </Entity>

    <Entity Name="n1"> ... PowerNodeComponent NodeKind="And", Data "out:n2" ... </Entity>
    <Entity Name="n2"> ... PowerNodeComponent IsGoal="true" HeldTokenPower="1" ... </Entity>

    <Entity Name="lock0">
      <Component Type="EdgeLockComponent">
        <Property Name="PointA" Value="1,1"  Type="Vector2" />
        <Property Name="PointB" Value="1,-1" Type="Vector2" />
        <Property Name="KeyNode" Value="n1"  Type="string" />
      </Component>
    </Entity>
  </Entities>
</Scene>
```

Notes:
- **References by name, resolved post-load.** `out:` targets and `KeyNode` are entity *names*; the controller resolves them after `Scene.FromFile` + `InitializeEntities()` (Guids change every load, so they can't be used). Crossing-conflict pairs (`CompetingConnections`) are computed at load via `DoLinesIntersect`, not stored.
- **Sub-puzzle grouping** via `Tag` (`puzzle:p0`) or a parent puzzle entity — TBD during Phase 2; tag is the simpler start.
- **Collections** (inventory, activation chain, outgoing targets) ride in `<Data>` through `SerializeData()`/`DeserializeData()`; only scalar props use auto-serialized `<Property>` elements.

## Build phases

- [x] **Phase 0 — Scaffolding.** Swapped the 3D `UI3DScene` for a 2D `PowergridView` in `PowergridUI.cs` (kept the `Scene` save/load wiring). Added the `PowerNodeComponent` skeleton; verified entities round-trip through the serializer headlessly. Build green.
- [x] **Phase 1 — Components + controller + rendering (shared).** `PowerNodeComponent`, `EdgeLockComponent`, `Connection`, `PuzzleGraph` (faithful `LevelManager` port + cleanups: single distribute loop, fixpoint for locks/gates, goal-powered solved condition), `PowergridLevelController` (post-load name resolution + puzzle grouping by tag), `Geometry`. Camera + world/screen transform. Renders nodes (filled=powered/outlined=unpowered), directed edges with arrowheads + powered styling, gate glyphs, anchor/goal markers (font fallback until sprites exist). Hand-authored `level-001.xml` + `/copy:` content entry. Engine verified headlessly (12/12 distribution/solved assertions). **Not yet eyeballed in the running GUI.**
- [~] **Phase 2 — Editor.** *2a done:* edit-mode toggle + toolbar (Select/Add/Connect/Delete + Save), node create/move/connect/delete, right-drag pan in edit mode, selection ring + connect rubber-band, and an inspector (cycle kind, toggle anchor/goal, adjust anchor power & held-token, delete). Live controller rebuild on every edit. Save via `Scene.SerializeToFile`. *2b remaining:* lock placement + key assignment, sub-puzzle tagging, activation links, and resolving the save-deploys-to-content-output path so edited levels reload cleanly next run. Not yet eyeballed in the GUI.
- [~] **Phase 3 — Play + validation.** *Done:* `PowergridLevelComponent` (fixed inventory + holding-slot count, serialized), inventory bar (holding slots + available tokens), token drag/drop between inventory/holding/nodes validated by `CanAddPower`/`CanRemovePower`, reject flash, placed-token rendering, solved banner. Engine already supports `Distribute`, crossing conflicts, locks/keys, AND/XOR. *Deferred:* node reveal-on-power (discovery → Phase 4). Play-layer validation verified headlessly (9/9); drag/drop UI not yet eyeballed.
- [x] **Phase 4 — Both token models + level flow.** Discovery (anchors/goals visible, powered nodes reveal their sticky frontier, node-held tokens granted into inventory). Witness-style activation chaining (a puzzle is active iff all prerequisite puzzles are active+solved; deactivates downstream when an upstream puzzle is unsolved) with an inactive "LOCKED" mask. Shared inventory/holding carries across puzzles in a level (single play state). Camera snap-to-puzzle when idle near a puzzle centroid. Discovery (9/9) and activation (10/10) verified headlessly; camera snap is visual-only.
- [ ] **Phase 5 — Polish.** Powered/unpowered visual distinction tuned for 1-bit, arrows, lock/key visuals, edit/play toggle UX, status messaging.

## Open questions / risks
- **Powered-state legibility under 1-bit dithering** — needs a brightness/pattern scheme, not color. Validate early (Phase 1) by viewing through the dither shader.

Yes, also note that the sprites in the powergrid folder are white filled in shapes, so you can render them with any color. I would recommend having the background being 50% gray so that we can use absolute black and absolute white for contrast. A powered line could be white and an unpowered one could be black.

- **Anchor power source** — original anchors derive power from a placed token; confirm whether anchors should be fixed sources (`anchorPower`) or also accept dragged tokens. Plan assumes configurable `anchorPower`, overridable in editor.

Anchors can be dragged onto / off of just like other nodes 

- **XOR/AND cost** — gate evaluation calls reachability searches every frame in the original; fine at jam scale, watch performance on large multi-puzzle levels (memoize per update if needed).

Yes this would be a good optimization

---

## Open Issues

- [ ] (Add as they come up)

### Editor

There should be an option in the powergrid ui to enter edit mode. In edit mode the user should be able to spawn nodes (including anchor/root and victory nodes) and tokens, connect nodes together (remember connections are directed), move nodes, place gates, as well as create different sub puzzles which are connected together in the game.


### Resources

The powergrid folder of this workstation contains a mostly working, but sloppy game jam implementation of this game. Use it as a starting point, but feel free to make architectural/logic/cleanliness improvements
