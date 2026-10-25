# Changelog

All notable changes to `com.virtualvenues.sdk`.

## [Unreleased]

### Fixed
- **World Publisher no longer leaks 30+ GB per mobile publish**: the isolated iOS worker
  (`Builds/WorldPublishing/<id>/mobile/worker`, a full project copy with its own Library) is deleted after a
  successful iOS build. A failed build keeps it for debugging. The small `release/` folder (logs, receipt) stays.

## [0.10.13] - 2026-10-24

Needs the matching FutureFestXR player update for RideSounds and audio channels (new components). Ship UPC and
UMS together.

### Added
- **Audio channels**: `AudioChannel` sends the AudioSources on an object and its children to a channel players
  can turn up or down on its own (web overlay mixer widget or in-game menu). Defaults in every world: Master,
  Stream, Voice Chat, Ambient Music, Ambient Environment, Sound Effects, Rides and Vehicles, Interface
  (ids in `AudioChannels`). `AudioChannelSet` adds up to 8 custom channels, each under one of the four
  assignable defaults. Inspector dropdown and warnings. Needs a matching app build (new components).
- **TestPlayer mouse-wheel zoom**, same rule as the live player: wheel forward zooms in, distance = zoom x the
  control style's **Zoom Max**, starting at its **Default Zoom**. All the way in hides the character (first person)
  when the style allows it. Works on foot and seated, not while driving. Shoulder side and FOV are still live-only.
  The SDK runtime assembly now references Cinemachine (already a package dependency).
- **RideSounds**: speed-driven ride sounds on a `SplineMover` cart, like the old FutureFest coaster cart. Speed
  layers (looping AudioSources with a m/s band, edge fade, pitch at each end, max volume) set their volume and
  pitch every frame; a Lift loop plays only on Lift sections; one-shots on entering a Booster or Brake and when
  the cart stops and launches at a station. Runs from the shared ride clock on every player's game, no network
  traffic; no one-shots on a resync (late join, clock switch). Play-mode inspector readout of speed, section and
  each layer. Replaces per-train ride copies with pitch and volume track events.
  - `SplineMover` now reports `Speed` (m/s), `Section` (the `TrackSection` it is in), `Resynced` and `Ride`.
  - Needs a matching app build (new component).

### Fixed
- **TestPlayer can look around while seated** (ride seats, benches, coaster and Ferris seats): sitting used to switch
  off the whole third-person controller, camera included, so the view stayed fixed to the seat. Now only walking,
  jumping and gravity stop; the camera turns by the control style as on foot, like the live player. Driving a
  vehicle still uses the driver look-around.

## [0.10.12] - 2026-10-24

Editor only: no player or server update needed.

### Added
- **Avatar Publisher: Actions, Pets and Mounts sections.** Dance / VFX / Spray action assets, pets and mounts each
  get their own list under Cosmetics, with their category shown instead of a slot dropdown. Drop a prefab on Pets
  or Mounts and it publishes as `Pet_<name>` / `Mount_<name>`. Publishing and the catalog format are unchanged.

### Changed
- **Avatar Publisher: New and Existing keep separate lists.** Choosing **New** opens an empty staging area at
  version 1.0.0; switching back to **Existing** brings that catalog's lists and version back untouched. After a New
  catalog publishes, its lists move to Existing and New starts empty again. Lists saved before this version stay
  under Existing.
- **Avatar Publisher: long lists scroll.** Avatar Prefabs, Cosmetics, Actions, Pets and Mounts each scroll inside
  their own box, and the per-avatar slot summary is folded into one collapsed "Avatar slots" row.

### Fixed
- **Avatar Publisher upload**: large catalogs no longer fail with `403 Request has expired`. Upload links last 15
  minutes, so the publisher now gets new links for the files still waiting (WebGL and iOS).

## [0.10.11] - 2026-10-24

0.10.10 was never published; its Collision Scope ships here.

Needs the matching FutureFestXR player and server update. Ship UPC and UMS together: vehicles sync new state, so
an old client on a new server (or the reverse) loses vehicle sync.

### Added
- **Admin Event** (**Add Component > VirtualVenues > Admin Event**, or **GameObject > VirtualVenues > New Admin
  Event**): a named cue (e.g. `fireworks`) an admin fires for everyone in the world by typing `/event fireworks`
  in chat; `/events` lists the cues in the world. Wire **On Everyone** to `Animator.SetTrigger`,
  `ParticleSystem.Play` and the like. Names are case-insensitive, markers sharing a name fire together, the
  cooldown (default 10 s) is enforced by the server, and players who join later don't replay it. **Fire now** in
  the Inspector tests it in Play mode. See `Documentation~/Components.md` > Interaction.
  - Needs the matching FutureFestXR player and server update.
- **Vehicle events**: a `Vehicle`'s new **Events** foldout (Driver Enter/Exit, Throttle Start/Stop, Passenger
  Enter/Exit, Enter/Exit Water for boats, On Speed with a Speed Output Range) runs on every player's game — hook
  engine start, engine loop, rev and spray audio/VFX. Players who join mid-drive get Driver Enter / Throttle Start
  so loops start. The TestPlayer fires Driver, Throttle and On Speed. See `Documentation~/Components.md` >
  Vehicle events.
- **Vehicle look-around**: the driver camera turns with the mouse by the control style, stays put, and swings
  back behind the vehicle once you drive forward (also in the TestPlayer). `ControlStyleMath.StepVehicleOrbit`
  holds the shared rule.
  - Needs the matching FutureFestXR player update.
- **Collision Scope** (**Add Component > VirtualVenues > Collision Scope**): tick what an object's solid colliders
  stop — **Players**, **Camera**, **Vehicles**, any mix (e.g. invisible walls the camera passes through, a rock that
  keeps the camera out while players walk through, kart-only barriers). Covers children; coloured gizmo. No layer
  setup in your project. The TestPlayer follows Players and Camera in Play mode. See `Documentation~/Components.md`
  > Collision.
  - Needs the matching FutureFestXR player update.
- `AddressablesBuildManager.BuildForTarget(target, progress)`: build the avatar catalog for one platform from code.

### Fixed
- **TestPlayer camera stuck at the spawn point**: the TestPlayer's camera rig is Cinemachine, but the SDK did not
  depend on it, so in projects without Cinemachine its brain and follow camera were missing scripts. The SDK now
  depends on `com.unity.cinemachine` 3.1.2 (Unity 6.6+ uses its built-in 6.6.0 instead).
- **Pathway hang**: the Inspector's **+** on an empty rule list made a zeroed rule (Spacing 0) that froze the
  editor. Such a rule now gets real defaults, and Spacing, size and corner angle are clamped when read too.
- **World Publisher**: the thumbnail capture is saved per main scene under `Library/VVWorldThumbnails/`, so it
  survives reloads and restarts and every publish reuses it until you capture again. The chosen main scene no
  longer clears when publishing switches platform.

## [0.10.9] - 2026-10-21

- World Publisher thumbnails: capture from the Scene view or pick a custom image; a 1280x720 JPEG is uploaded with the world and shown while the world loads. Skipped automatically on backends without thumbnail support.
- `WorldPublishJob.Start` takes an optional `thumbnailPath`; new `WorldPublisher.WorldThumbnail` helpers.

## [0.10.8] - 2026-10-19

- Add optional native iOS world companions to World Publisher with isolated worker builds, additional scene support, manifest validation and SDK-contained publishing tools.
- Preserve the existing desktop/server world workflow and the 0.10.7 avatar publishing features.

## [0.10.7] - 2026-10-09

- Avatar Publisher **Also export iOS**: builds the catalog a second time for iOS under the same version
  (`versions/{vid}/iOS/`); skipped on backends without iOS support. Fixed the recorded build target.

## [0.10.6] - 2026-10-09

### Added
- **Control styles**: `ControlStyle` assets describe how the camera turns, how it frames the player and which
  way the character faces. Built-ins Classic (default, today's controls), Shooter (Fortnite-style strafe +
  locked cursor) and MMO (WoW-style right-drag turn). `WorldControlSettings` sets a world's default, which
  styles players may switch to, or forces one. The TestPlayer follows the active style (look + facing).
  `ControlStyleMath` holds the shared rules. See `Documentation~/Components.md` > Controls.
  - Needs a matching app build (new components).
- **Custom Controls**: on-screen buttons while seated, after an interact, in range or in a zone (see
  `Documentation~/Components.md` > Custom controls). Vehicle Push / Turn.
- **Pathway** (**GameObject > VirtualVenues > New Pathway**): draw a spline, get a finished path — a ground strip
  plus props from your own prefabs, laid out by rules. See `Documentation~/Paths.md`.
  - Rules: Every / Segments (fences stretch to meet on curves) / Start / End / Corners; lanes Center, Left,
    Right, Both Sides, Alternating; offset, spacing, insets, jitter, chance, facing, random turn and size,
    keep-clear radius. Each rule has its own random stream, so editing one never reshuffles another.
  - Snaps the strip and props onto the ground below. Rebuilds live as you drag the spline and on world load;
    **Bake** saves the props as normal objects that no longer rebuild.
  - **Path Kits** (`PathwayPreset`): save and load a ground + rules set.
  - **Unity Terrain** stamp: flatten, paint a Terrain Layer, clear grass and trees along the path (Undo works).
  - **Junctions**: every spline in the container is a path; linked points are junctions. Strips meet in a patch,
    fences stop at the joining path and close into corners, and a new **Junctions** slot places corner or centre
    props. **Link Touching Points** links points sitting on top of each other.
  - `SnapIgnoreRoot`: colliders under it are never ground (the web World Editor sets it to the placed object).
  - Pathway props handed over as inactive or hidden prototypes are switched on.
  - Needs a matching app build (new component).
- Streaming-world pilot export / validate tools and per-entry avatar bundles.

## [0.10.5] - 2026-10-05

### Added
- **Docs for AI agents**: `AGENTS.md` plus `Documentation~/Components.md` (every world component),
  `Documentation~/Building-a-World.md` (build a world from code via the `unity` CLI) and
  `Documentation~/Publishing.md`.
- **`WorldPublisher.WorldPublishJob`**: build (and optionally upload) a world from code —
  `WorldPublishJob.Start(mainScene, additionalScenes, worldName, upload)`, then poll `WorldPublishJob.Status`.
  Survives the script reloads of the platform switches, re-reads the cached sign-in after each one, and
  `upload: false` builds both bundles with no sign-in. The World Publisher window now runs on it.

### Fixed
- REQUIREMENTS.md pointed at the wrong `TestPlayerSpawner` path.

## [0.10.4] - 2026-09-30

### Added
- Example scene `Runtime/Examples/RollerCoaster/RollerCoasterExample.unity`: lift hill, drop, banked turns and a
  camelback on a `SplineTrack`, with supports, a 3-car train parked at the station, and a spawn point. The ride
  uses `SplineRide`: station stop, lift chain, booster, brake run, and a "Drop" event that fires a particle burst.
- **SplineRide** (add next to a `SplineContainer`): ride physics for the carts on that track.
  - Gravity: carts speed up downhill and slow down uphill (friction and drag tunable; turn it off for boats/trams).
  - **Track sections** in metres along the track: **Lift** (chain speed uphill), **Booster** (pushes up to a speed),
    **Brake** (slows to a speed) and **Station** (stops, waits, launches). Drawn in colour on the track in the Scene
    view; drag their handles along the track.
  - **Track events**: points that fire when the train passes — an Animator trigger and/or a UnityEvent
    (play a sound, burst particles, open a gate).
  - The lap is simulated once on load and carts read it off the shared clock, so every player sees the train in
    the same place with no network traffic. Carts can't be pushed and don't roll back.
  - The inspector shows lap time, top speed, and warnings (train stalls, hits a station too fast).
- `SplineTrack` **Parts**: turn the left rail, right rail and ties on or off, and give each its own material
  (empty = the Mesh Renderer's material, so existing tracks look the same).
- `SplineTrack` **Supports**: generated posts from the track down to a ground height (spacing, thickness, minimum
  height, own material; off by default). The example's hand-placed supports are replaced by them.
  - Posts stop on the first solid collider below them, so a tunnel or building with a collider keeps posts out
    (no setup). Posts re-cast live in the editor when colliders move.
  - **Track Support Blocker** (GameObject > VirtualVenues > Support Blocker, or the track inspector's
    **Create Support Blocker** button): makes a trigger stop posts (an invisible no-post zone), or with
    **Let Supports Through** lets posts ignore a collider (fences, lamps). Orange/green box in the Scene view.
  - Track on its side or upside down (**Arch Tilt**, default 90°) gets an **arch** instead of a post: two legs
    **Arch Width** apart (default 5 m), a crossbeam above the track and a hanger down to it — nothing runs through
    the rails.
  - `SplineTrack.RebuildAll()` and `StopsSupports(Collider)` for scripts; guide in `Documentation~/Rides.md`.
- Track points show as numbered dots in the Scene view when a track is selected; click one to move, rotate or
  bend it with Unity's spline handles.
- Example scene: a second, gentle **platform coaster** — open decks players stand on (Carry Players), a 10 s
  boarding stop, booster and brakes, with its own boarding platform and spawn point.
- `SplineTrack` / `SplineRide` inspectors: **Edit Track Shape** button opens Unity's spline tools — move and rotate
  track points (roll = banking) and drag tangent handles to bend the curve. Rails and ride timing update live.
- Tooltips on every track, ride and cart setting; section rows are labelled by type ("Lift (22–55 m)").
- `SplineMover`: **Gap Metres** (trailing cars ride a fixed distance behind the lead car — give every car of a
  train the same Start Offset) and **Fire Events** (on for the lead car only). Duration is ignored on a
  `SplineRide` track. The New Roller Coaster prefab now has a station, lift and brakes.
- Test player (`TestPlayerInteractor`): offline stand-in for the player's interactions — nearest-Interactable
  prompt + E, Seats (with a Sitting animation), Trigger Events, riding carry-platform movers and a simple Vehicle
  drive — so creators can try rides in their own project.

## [0.10.3] - 2026-09-29

### Added — Rides
- **SplineTrack:** add it next to a `SplineContainer` to get coaster rails + ties along the spline (gauge, rail
  radius, tie spacing, height offset for carts whose wheels sit below their pivot). The mesh is rebuilt on load (never saved) and updates live while you drag knots.
- GameObject > VirtualVenues > **New Roller Coaster**: a banked, hilly track with 3 carts that sit on the rails.
- Coasters: turn **Keep Upright** off on the carts' `SplineMover`, or they stay level and float off sloped rails.

## [0.10.2] - 2026-09-27

### Fixed — World Publisher
- Waits for scripts to recompile (and the domain reload to finish) after each platform switch before building that
  platform's bundle. Fixes "script class layout is incompatible between the editor and the player" in projects with
  serialized fields inside platform `#if` blocks (e.g. PixelCrushers), which failed when publishing from WebGL.
- Publishing resumes across the domain reload; scene list is frozen for the whole publish.
- Build failures now show the real Console error (layout mismatch / compile error) instead of "Build Support is not
  installed". Blocks publishing while scripts have compile errors. No modal error dialog in batch mode.

## [0.10.1] - 2026-09-27

### Added
- **Hotbar actions:** Create > Virtual Venues > Actions > **Dance** (Animator Override Controller), **VFX** (prefab
  with a ParticleSystem on its root + emit amount) and **Spray** (decal material using `Shader Graphs/Decal`), each
  with a hotbar icon. Publish them with the Avatar Publisher; they pick their own Dance / VFX / Spray category.

### Changed — Avatar Publisher
- Action assets are validated on drop and before publish (missing controller / prefab / ParticleSystem, non-Decal
  spray material), and use their icon sprite as the row thumbnail.

Needs the matching FutureFestXR player update to play SDK-authored actions.

## [0.10.0] - 2026-09-27

### Changed
- **Respawn Zone:** new **Affects Players** option (default on). Turn it off for a vehicle-only kill zone: riders are
  dismounted and the vehicle goes home, but players on foot are not respawned.
- **`KeepUpright`:** new **Carry Players** option (default on), so ferris-wheel pods carry the players standing in them.
- `RotatingObject`, `SplineMover` and `AnimatedObject` now run before the player's platform probe (execution order
  -50; `KeepUpright` -40), so riders no longer slide sideways on moving platforms.

Needs the matching FutureFestXR player and server update, which also keeps riders glued to moving platforms as seen
by other players.

### Changed — Avatar Publisher
- **Costumes** are listed under Avatar Prefabs with a Kind dropdown (Avatar / Costume, defaulting by the
  `Costume_` name) and publish as cosmetics in the `Costume` slot.
- Legacy `PetSO` / `MountData` assets publish into their own Pet / Mount category; SDK `Mount` / `PetFollower`
  marker prefabs are still accepted in those slots.
- Row icons: failed bakes retry, icons no longer go blank after a scene open or publish build, and custom icons
  are kept even after their asset is removed.
- Saved lists and custom icons are stored per project, so two projects on one machine no longer overwrite each other.

## [0.9.26] - 2026-09-27

### Added — world features ported from the old FutureFest SDK

Each is a data-only marker; the FutureFestXR player adds the behaviour and networking. All have an example
prefab under GameObject > VirtualVenues.

- **Trigger zones:** new zone types **Bounce**, **Push** and **Respawn** (`BounceZoneData`, `PushZoneData`,
  `RespawnZoneData`), and **Swim** now has `SwimZoneData`. Respawn can also send vehicles home.
- **`SplineMover`:** moves an object along a Unity spline (Loop / PingPong / Once) at constant speed, in sync
  for every player; can carry players standing on it. Needs `com.unity.splines`.
- **`KeepUpright`:** keeps an object level under a spinning parent (e.g. ferris wheel pods).
- **`AnimatedObject`:** ping-pongs position, rotation or scale between two values; can carry players.
- **`TriggerEvent`:** UnityEvents for the player who walks in and for everyone, with a cooldown / once option.
- **`Vehicle`:** new **Boat** type with boat stats. Floats on Crest water, or on the top of a Swim Zone.
- **`PhysicsObject`:** a server-simulated prop players can push; respawns when it falls or sits idle.

Needs the matching FutureFestXR player and server update (ship UPC and UMS together).

## [0.9.25] - 2026-09-27

### Added — publish additive scenes with a world

The World Publisher has an **Additional scenes** list under the scene picker. Every listed scene is built into
the same world bundle as the main scene (UMS and UPC), so a world can be split into a main scene plus
additively-loaded scenes (e.g. booths, art galleries).

- The main scene stays the **active** scene at runtime (its lighting, skybox and fog win). The player finds it
  by matching its file name against the bundle name `world_upc_<main scene>_<yyMMdd>_<vv>`; this needs the
  matching FutureFestXR player update (`WorldContentLoader`). Older players still load every scene, but may
  make the last-loaded one active.
- The list is remembered per main scene.
- Bundle names are now cleared from the published scenes after every publish (success or failure). Before,
  a scene kept its name and was rebuilt into later, unrelated `BuildAssetBundles` runs.

## [0.9.22] - 2026-09-17

### Added — load a published catalog into the publish lists

Pick a catalog in the Avatar Publisher's **Existing** dropdown (or press **Load** on a catalog card, or
click Existing with empty lists) and the Avatar Prefabs / Cosmetics lists fill with that catalog's latest
version, each item rebound to its local asset by the GUID captured at publish, with the published slot
carried over. The next Publish is then a new version built from exactly what you see.

- Items whose GUID has no valid asset in this project stay as **placeholder rows** with a rebind field
  and an X. Publish skips them (with a console warning) rather than blocking.
- **Replace after confirm**: a non-empty list edited since the last load or publish asks before it is
  replaced. "Keep" leaves the list, and the status line says which catalog it will publish to.
- A status line under the dropdown shows what is loaded; the loaded catalog's card is highlighted.

### Added — row icons

Every Avatar / Cosmetic row shows a baked icon: a prefab render, a material on a preview sphere, or the
texture itself, baked one per editor tick and cached. Click the icon for an inline custom Texture2D
field, drop a texture onto it, or right-click to reset to the auto icon. Custom icons persist with the
list.

Icons are re-baked at publish and uploaded beside the bundles as `thumb_<assetGuid>.png`, with
`iconObject` in the catalog metadata. Until the backend accepts `.png` objects, the publisher detects
the rejection and republishes without icons, so publishing never fails on them.

### Changed

- The Material (preview sphere) and Texture (readable blit) bake paths moved from the Asset Pack
  Publisher window into `AssetThumbnailBaker` (`BakeMaterial`, `ToReadableSquare`, `BakeAsset`). No
  behaviour change for asset packs.

## [0.9.21] - 2026-09-04

### Added — Material and Texture cosmetics

The Avatar Publisher's **Cosmetics** list now takes a **Material** or a **Texture2D** as well as a
prefab. Drop them in like prefabs (multi-select works); each row's asset field is typed to whatever the
chosen slot expects, and a row auto-picks the first slot on the avatar that can take that asset.

- A **Material** publishes into a **Material** slot — the existing kind, which swaps
  `materialIndex` on the slot's renderers. Nothing on the avatar side changes for this.
- A **Texture2D** publishes into the new **Texture** slot kind (`AvatarSlotKind.Texture`), which sets a
  shader property (`Texture Property`, default `_BaseMap`) on `materialIndex` of the slot's renderers.
  Skin tones, decals, face paint. The texture is applied to the renderer's own material instance, so
  avatars that share a material do not retexture each other.
- Publishing is **blocked** with a reason when a cosmetic's type does not fit its slot's kind (a prefab
  into a Material slot, a Material into a Texture slot, and so on). A slot no avatar in the publish
  declares is still only a warning, as before.
- A material or texture that lives **inside a model file** is rejected — extract it to its own asset
  first. It shares the model's GUID, so it could neither be restored nor addressed on its own.

The slot inspector shows `Texture Property` and a Texture2D `Fallback` picker for Texture slots, and the
validator treats Material and Texture slots alike (no pivot warning; "no renderers" warning).

### Changed — publisher windows

Layout pass across the Avatar, World and Asset Pack Publisher windows: the SDK version sits in the brand
header, the published Avatars / Worlds / Packs list is its own card with a count in its title, the
publisher form reads as one flat surface with dividers instead of nested cards, and "Signed in as …"
replaces the greeting.

## [0.9.20] - 2026-08-31

### Fixed — a slot pivot can no longer delete the avatar

Equipping a cosmetic starts by destroying **every child of the slot's pivot**. A pivot pointed at the
avatar root — or at a bone inside the rig — therefore wiped the skeleton and the body on the first
equip. There was no exception and no log: the character simply vanished, and the same scripts worked
fine in a project where the pivots happened to be authored correctly. Diagnosing one case took a day.

The runtime now gives such a slot a container transform of its own (`__slot_<slotId>`, created under
the authored pivot) and equips into that, so clearing only ever destroys that one slot's garment. A
slot gets a container when its pivot:

- **is the avatar itself**, or
- is a **rig node** — clearing it would destroy bones a SkinnedMeshRenderer is using. The body mesh is
  usually a *sibling* of the armature, so nothing renderable lives under the pivot and the mistake is
  invisible by eye, or
- is **shared with another slot**, so equipping into either would evict the other, or
- is **missing entirely** — the cosmetic used to load and then silently attach to nothing. It now
  attaches at the avatar's origin. For a Rigged Mesh slot that is a complete fix (the garment is rebound
  onto the skeleton); for a Mesh slot the item sits at the avatar's feet until a pivot is assigned.

A healthy dedicated pivot is left exactly as authored, so an avatar published wearing something still
has that item replaced rather than doubled. Material slots are untouched — they ignore `itemPivot`.

**This heals every already-published avatar with no republish.** The authoring rule is unchanged and
still worth following: give every Mesh / Rigged Mesh slot its **own empty transform**, placed **outside
the rig** (a `Slots` object beside the armature, one empty child per slot).


## [0.9.19] - 2026-08-26

### Changed — BREAKING (`VirtualVenues.AvatarSlotKind`)

The slot kinds are renamed to describe what a creator is choosing between — does this piece bend
with the body, or just sit on it — rather than the mechanism used to attach it.

| Old | New | Inspector label |
|---|---|---|
| `AvatarSlotKind.Attachment` | `AvatarSlotKind.Mesh` | **Mesh** |
| `AvatarSlotKind.Material` | *(unchanged)* | **Material** |
| `AvatarSlotKind.SkinnedMesh` | `AvatarSlotKind.RiggedMesh` | **Rigged Mesh** |

Behaviour is unchanged. This is a rename only.

**No data migration needed.** The enum's underlying values are unchanged — and are now pinned
explicitly (`Mesh = 0`, `Material = 1`, `RiggedMesh = 2`) so they cannot drift in a future
reorder — so **existing avatar prefabs and already-published avatar bundles keep their slot
kinds**. Nothing serializes the member *name*, on disk or on the wire.

Only **source** breaks: an editor script referencing `AvatarSlotKind.Attachment` or
`AvatarSlotKind.SkinnedMesh` no longer compiles. Rename the reference. If you configure slots in
the Inspector, there is nothing to do.

### Changed (`Avatar` inspector)

- A slot now shows **only the fields that slot type actually uses**. Previously every slot showed
  every field, and the ones the runtime ignores were silently discarded on load — a SkinnedMesh
  slot offered `Renderers` and `Material Index`, a Material slot offered `Item Pivot`.

  | Slot Type | Fields shown |
  |---|---|
  | Mesh | Item Pivot, Fallback |
  | Material | Renderers, Material Index, Fallback |
  | Rigged Mesh | Item Pivot, Hide When Equipped, Fallback |

- The `Kind` field is labelled **Slot Type**. The serialized field name is unchanged.
- **`Fallback`** is now typed to the slot: a Material slot offers only Materials, the other two only
  prefabs. It previously accepted any Object, and a wrong pick became a silent `null` at runtime.

### Added

- **Camera Look At Point Offset** is drawn in the Scene view while the Avatar is selected — a dotted
  line from the root to a labelled marker at the point the player camera frames. It updates live, so
  the offset can be dialled in by eye instead of by trial build.

## [0.9.18] - 2026-08-21

### Changed — BREAKING (`VirtualVenues.Avatar`)

The `Fallback Content` / `Loading Content` block was named after FutureBot, the original built-in
avatar, back when slots were a fixed enum (`BodyPaint`, `Face`, `Hat`, …). Slots are creator-named
now, so a per-slot-name default no longer makes sense: on an avatar with a `Shirt` slot, a failed
Shirt cosmetic was falling back to a material called **Face**.

The two component-level material fallbacks are merged into one generic **Fallback Material**, and the
inspector block is now **Fallback Material / Fallback Item / Loading Material / Loading Item**.

Precedence is unchanged: the **per-slot `Fallback` always wins**; these are the last resort.

**Removed properties** — a script referencing any of these will no longer compile:

| Removed | Use instead |
|---|---|
| `Avatar.FallbackFace` | `Avatar.FallbackMaterial` |
| `Avatar.FallbackGameObject` | `Avatar.FallbackItem` |
| `Avatar.FallbackBodyPaint` | `Avatar.FallbackMaterial` — see the data note below |

No `[Obsolete]` forwarders were left for these. `FallbackBodyPaint` has no backing field any more and
could only ever return null; a compile error is better than a silent null at runtime.

### Data migration

`FormerlySerializedAs` remaps the serialized fields automatically, so **existing avatar prefabs and
already-published avatar bundles keep their values** — `_fallbackFace` → `_fallbackMaterial`,
`_fallbackGameObject` → `_fallbackItem`. Nothing to do by hand.

Two things do NOT survive the upgrade:

1. **`_fallbackBodyPaint` is dropped.** If an avatar has a bodypaint-style Material slot with **no
   per-slot `Fallback` of its own**, that slot now falls back to the single Fallback Material — which
   may be a face material. Fix: set the slot's own `Fallback`, which takes priority anyway. Avatars
   whose bodypaint slot already carries a per-slot `Fallback` are unaffected.
2. **Prefab-variant overrides of these fields are lost.** `FormerlySerializedAs` does not remap
   `m_Modifications` property paths, so a *variant* that overrode `_fallbackFace` or
   `_fallbackGameObject` silently reverts to its base prefab's value. Re-assign the field on the
   variant after upgrading. Non-variant prefabs are unaffected.

### Fixed

- A Material slot whose cosmetic fails to load with no fallback configured (slot-level or
  avatar-level) now **keeps the material it is already wearing** instead of being assigned `null`,
  which rendered as missing-material magenta in a build. It logs a warning naming the slot.
  This also removes a blank flash during loading on avatars with no **Loading Material** set.

### Removed

- The Avatar validator no longer emits the Info-level notice on every `SkinnedMesh` slot about
  authoring the garment against this avatar's rig. The requirement is unchanged and is still
  documented in the slot's `kind` tooltip; the panel just doesn't repeat it per slot.
