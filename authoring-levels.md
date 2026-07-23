# Authoring a Walking-Sim Level

A start-to-finish guide for building a level in Blender and playing it in the game. No hand-written
XML — you model in Blender, mark objects with custom properties, run one script, and rebuild.

If something looks wrong in-game, jump to [Troubleshooting](#troubleshooting) — the common
failures each have a one-line cause.

---

## How it works (30-second version)

You build the level in Blender. One exporter script turns it into everything the game needs:

```
your_level.blend
   │   tools/blender_export_walksim.py
   ▼
 models/<mesh>.fbx            visual geometry (one per unique mesh)
 files/scenes/<name>.xml       the scene (meshes, interactables, lights, spawn)
 files/scenes/<name>_asset.xml scene metadata (walk mode, navmesh path, ambient)
 files/scenes/<name>_navmesh.obj  the walkable surface (baked by Recast)
 files/root/<name>.scene3d     the file you open in-game
   + Content.mgcb patched automatically
```

Then you `dotnet build` and open `<name>.scene3d` from the in-game File Explorer.

The **player is a point that walks on the navmesh**; the visual meshes are what you see. The
navmesh is generated automatically from your walkable geometry — you never model it by hand.

---

## One-time setup

1. **Blender** 3.6+ (4.x fine).
2. Open the exporter in Blender's **Scripting** workspace: `tools/blender_export_walksim.py`.
3. Edit the config block at the top:

   ```python
   GAME_CONTENT_DIR  = r"F:\Dev\LD59\ld59\Content"   # your Content folder
   REPO_DIR          = r"F:\Dev\LD59"                # repo root (holds NavMeshBaker/)
   OUTPUT_SCENE_NAME = "my_level"                    # names all the output files
   AMBIENT           = "60,60,70"                    # base light r,g,b (0-255)
   RUN_BAKER         = True                          # auto-bake the navmesh via dotnet
   ```

   `RUN_BAKER=True` needs `dotnet` on your PATH. If it can't run, the script prints the exact
   bake command to run yourself — nothing breaks.

---

## Step 1 — Model the level

Model normally, keeping three rules:

- **Work in meters.** 1 Blender unit = 1 meter in-game. A doorway ~2 m tall, a room ~4–6 m. The
  player is ~1.8 m tall and walks at 3 m/s. If your level is a different scale, either resize it
  in Blender (`S`) or adjust the navmesh agent settings later (see [Tuning](#tuning)).
- **Floors face up.** Walkable surfaces need their normals pointing up (Blender's default). If a
  floor was flipped, select it and `Shift+N` (Recalculate Normals). A floor with a downward
  normal bakes as non-walkable.
- **Walls need to be solid-ish.** Give walls some thickness; paper-thin geometry can occasionally
  be walked through. The navmesh insets ~0.35 m from walls automatically (that's the player's
  radius), so you don't leave gaps yourself.

What counts as geometry:
- **Ground / walls / stairs / ramps** — anything you want to walk on or bump into. All of it is
  included in the navmesh by default.
- **Props you can't walk on** (a floating sign, a small object) — add custom property
  `no_collide = 1` so it renders but isn't part of the walkable surface.

> Overlapping surfaces (a tunnel under a hill) work, but the gap must be at least the player
> height (~1.8 m) or the lower path won't be walkable.

---

## Step 2 — Mark interactable objects

Select an object, open **Object Properties → Custom Properties → New**, and add:

| Property | Type | Meaning |
|---|---|---|
| `interact_action` | string | Marks the object interactable and sets the effect. Start with `show-text`. |
| `interact_prompt` | string | The crosshair hint, e.g. `read the tablet` (default: `interact`). |
| `interact_message` | string | For `show-text`, the text shown in the popup. |
| `interact_target` | string | What the action acts on (a file path, glyph id, entity name) — used by non-text effects. |

Example (a readable tablet):

```
interact_action  = show-text
interact_prompt  = read the tablet
interact_message = The glyphs speak of a buried road.
```

In-game: walk within ~2.5 m, look at it (it highlights and shows the prompt), press **E**.

Only `show-text` does something today. To add more effects (reveal a file, play a sound, learn a
glyph…), see [Interaction effects](#interaction-effects) — you author the `interact_action`
now, and wiring the effect is a small code change later.

---

## Step 3 — Place lights

Add Blender lights normally. Two types are exported:

- **Sun** (directional) → the main scene light. Angle it in Blender; the direction is read from
  its orientation.
- **Point** → a local light with falloff.

Optional custom properties on a light tune it:

| Property | On | Default |
|---|---|---|
| `intensity` | any light | sun `1.0`, point `2.0` |
| `range` | point only | `30` (meters of reach) |
| `shadows` | sun only | `1` (on) |

Lighting notes:
- The shader adds all lights then clamps, so **too many bright lights wash out to white**. One
  sun plus low ambient is a good, readable default. If it looks flat/white, lower `intensity` or
  the `AMBIENT` value.
- `AMBIENT` (in the exporter config) is the base fill everywhere — it sets how dark the shadows
  are. `30,30,30` = moody, `120,120,130` = soft/overcast.

---

## Step 4 — Place the spawn

Add an **Empty** (or any object) named `PlayerStart`, positioned where the player should begin —
on the ground, roughly eye level is fine (the game snaps you to the floor). Alternatively put
`player_start = 1` as a custom property on any object.

If there's no spawn, the game drops you on the first navmesh triangle (usually fine, but you won't
control where).

---

## Step 5 — Export

With your level open in Blender:

1. Make sure everything you want exported is **visible** (hidden objects are skipped).
2. In the Scripting workspace, run the script (**Alt+P** or the ▶ button).
3. Watch the console — it logs each mesh, interactable, light, the navmesh, and the bake.

The script writes all the files listed at the top and patches `Content.mgcb`. If the baker didn't
auto-run, copy the printed `dotnet run … NavMeshBaker …` command and run it once.

---

## Step 6 — Rebuild the game

```
cd F:\Dev\LD59\ld59
dotnet build
```

This compiles the new FBX models and copies the scene files into the game.

---

## Step 7 — Play it

1. Launch the game, open the **File Explorer**, and open `<name>.scene3d`.
2. Controls:

   | Input | Action |
   |---|---|
   | Click in the window | Capture the mouse (look around) |
   | Mouse | Look |
   | W / A / S / D | Walk |
   | E | Interact with the highlighted object |
   | Tab | Release the mouse |

   (Escape quits the game — use **Tab** to free the cursor.)

---

## First export: the marker-cube check

The one thing that can silently go wrong is the visual mesh and the navmesh not lining up (an axis
or scale mismatch). Catch it in 30 seconds on your first export:

1. In Blender, drop a small cube sitting **on the ground** somewhere obvious.
2. Export, rebuild, and walk over to it in-game.
3. Your feet should meet the ground where the cube sits, and the cube should rest **on** the floor
   — not float above it or sink into it.

If it lines up, your coordinate setup is correct for every future export. If the player floats or
sinks relative to the world, that's the axis/scale issue — tell the toolmaker; it's a one-time
fix in the exporter's conversion.

---

## Tuning

### Lighting
Everything except the sun direction is data you can change without re-exporting geometry:
- Edit lights and ambient in `files/scenes/<name>.xml` / `<name>_asset.xml`.
- **Fast loop:** the game re-reads these when you open the window. Edit the deployed copies under
  `ld59/bin/Debug/net9.0-windows/Content/files/scenes/…`, then close and reopen the `.scene3d` —
  no rebuild. Copy your final values back into `ld59/Content/files/scenes/…` and rebuild once to
  make them permanent.

### Navmesh (walkability)
If parts you wanted to walk on are missing, or the player fits through gaps they shouldn't,
re-bake with different agent settings. From the repo root:

```
cd NavMeshBaker
dotnet run -- ../ld59/Content/files/scenes/<name>_navmesh_src.obj \
              ../ld59/Content/files/scenes/<name>_navmesh.obj \
              --slope 60 --step 0.4 --validate
```

Then `dotnet build` the game again. The knobs (all meters/degrees):

| Flag | Meaning | Default |
|---|---|---|
| `--radius` | player radius / how far the walkable area insets from walls | 0.35 |
| `--height` | player height / minimum headroom for overlapping paths | 1.8 |
| `--step` | tallest step the player can climb (raise for tall stairs) | 0.3 |
| `--slope` | steepest walkable slope in degrees (raise to walk steeper hills) | 45 |
| `--cell` | voxel size; smaller = more accurate navmesh, slower bake | 0.15 |
| `--scale` | multiply the input size (e.g. `4` for a 4×-bigger level) | 1 |
| `--validate` | print the result's connectivity + confirm it's walkable | — |

The exporter uses these defaults; the config block in the script lets you set them for auto-bakes.

---

## Interaction effects

Interactions are **data-driven**. Your `interact_action` string flows from Blender into the
game and is routed by `InteractionDispatcher.Dispatch` in
`ld59/WalkingSim/InteractionDispatcher.cs`. Today only `show-text` is wired.

Adding an effect is one `case` in that file. The file itself lists the ready-to-wire menu and the
exact game systems each one calls, for example:

| `interact_action` | What it would do | Status |
|---|---|---|
| `show-text` | popup with `interact_message` | **working** |
| `play-sound` | play a sound clip (`interact_target` → clip) | easy to add |
| `reveal-file` | make a desktop file appear (`interact_target` = file path) | easy to add |
| `open-file` | open a desktop file/app in a window | easy to add |
| `unlock-info` | grant a deduction clue | easy to add |
| `learn-glyph` | teach a word/symbol into a vocabulary | needs a new system |
| `toggle` | show/hide another object (`interact_target` = its name) | needs a new system |

You can author objects with any of these `interact_action` values now; the ones marked "needs a
new system" simply do nothing until that system is built.

---

## Powergrid puzzles in the world

An object can display a powergrid puzzle on its surface; the player presses **E** to open a
focused solving view, and **which valid solution** they chose drives environment changes.

### Author the puzzle
Powergrid levels are separate scene files under `Content/files/scenes/powergrid/` (built with the
existing powergrid tools). A puzzle can have several valid solutions — that's what makes
"different solution → different outcome" possible.

### Turn an object into a puzzle panel (Blender custom properties)

| Property | Meaning |
|---|---|
| `puzzle_level` | the powergrid scene, e.g. `files/scenes/powergrid/level-001.xml`. Presence of this makes the object a puzzle. |
| `puzzle_outcomes` | which-solution → effect rules (DSL below). |
| `puzzle_size` | panel width in meters (default 2.0). |
| `puzzle_yoffset` | panel centre height above the object (default 1.0). |
| `puzzle_yaw` | panel facing, game degrees (0 = +Z). Omit to use the object's own rotation. |

The object's mesh is the visual frame (a monitor, a pedestal…); the puzzle renders on a
**world-mounted panel** in front of it — a fixed part of the level, not a billboard. It faces
`puzzle_yaw` (or the object's rotation), so orient the object toward where the player approaches.
The object is automatically interactable — no `interact_action` needed.

### The outcomes DSL

Rules are separated by `|`. Each is `constraints => action:target:message`, where constraints are
`node=rune` pairs joined by `&` (empty = matches any solved state). The **first** matching rule
fires. `node` is the powergrid node's entity name; `rune` is the placed symbol's name.

```
Gate=Sun&Lever=Moon => reveal:Bridge | Gate=Moon => reveal:Pit | => show-text::You solved it.
```

- Solve with Gate=Sun and Lever=Moon → the `Bridge` entity is revealed.
- Solve with Gate=Moon → the `Pit` is revealed.
- Any other valid solution → a "You solved it." popup.

`action:target` reuses the interaction effects (`reveal` / `hide` / `toggle` an entity by name,
`show-text`, and anything you add to `InteractionDispatcher`).

### Revealing things

Give the object you want to appear later the custom property `start_hidden = 1` (it exports as
`Visible="false"`). A `reveal:<name>` outcome makes it appear; `hide` / `toggle` also work.

> **Navmesh caveat:** the navmesh is baked once, up front. If you reveal geometry the player
> should be able to **walk on** (a bridge), that geometry must already be in the navmesh — it's
> only *visually* hidden until revealed. Don't expect revealing an object to add new walkable
> ground at runtime.

### Controls in the puzzle view
Press **E** on the panel to open it (the cursor frees automatically). Solve with the mouse as in
the desktop powergrid app. Press **Tab** to leave without solving. Solving fires the outcome and
returns you to the world.

## What gets generated (reference)

For `OUTPUT_SCENE_NAME = "my_level"`:

| File | Purpose |
|---|---|
| `Content/models/<mesh>.fbx` | visual geometry (one per unique Blender mesh) |
| `Content/files/scenes/my_level.xml` | the scene: meshes, interactables, lights, spawn |
| `Content/files/scenes/my_level_asset.xml` | walk mode, navmesh path, ambient |
| `Content/files/scenes/my_level_navmesh.obj` | the baked walkable surface (game loads this) |
| `Content/files/scenes/my_level_navmesh_src.obj` | walkable geometry fed to the baker (not shipped) |
| `Content/files/root/my_level.scene3d` | the entry you open in the File Explorer |
| `Content.mgcb` | patched with the new models + copied files |

Re-exporting the same `OUTPUT_SCENE_NAME` overwrites these and skips `Content.mgcb` entries that
already exist.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| **Blank white window** | The level failed to load (a missing file/model, bad path). | Check the game console log; confirm `dotnet build` ran after the export and the `.scene3d` / asset paths match. |
| **Player floats above / sinks into the ground** | Visual/navmesh axis or scale mismatch. | Do the [marker-cube check](#first-export-the-marker-cube-check); if off, it's a one-time exporter conversion fix. |
| **Can't walk on a surface you modeled** | Its normal faces down, or it's steeper than `--slope`. | `Shift+N` to recalc normals in Blender; or re-bake with a higher `--slope`. |
| **Everything is pure white / flat** | Lights over-driving past the clamp. | Lower light `intensity` and/or `AMBIENT`. |
| **A tunnel under something isn't walkable** | Headroom below the max is less than player height. | Raise the gap to ≥ ~1.8 m, or lower `--height` and re-bake. |
| **Object is huge or tiny** | Level isn't in meters. | Resize in Blender, or note the player is fixed ~1.8 m tall. |
| **Interactable shows no prompt** | Too far (>2.5 m), or missing `interact_action`. | Get closer; confirm the custom property is set. |
| **Stairs act like a wall** | Step rise exceeds `--step`. | Re-bake with a larger `--step` (e.g. `0.4`). |
| **Baker didn't run during export** | `dotnet` not on Blender's PATH. | Copy the printed `dotnet run … NavMeshBaker …` command and run it in a terminal. |

---

## See also
- `walking-sim.md` — the design and internals of the walking sim (navmesh model, ID-buffer
  picking, the baker).
- `tools/blender_export_walksim.py` — the exporter (top-of-file docstring documents every option).
- `ld59/WalkingSim/InteractionDispatcher.cs` — where interaction effects are added.
