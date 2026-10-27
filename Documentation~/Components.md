# World components

Every component a world can use, for creators and for AI agents adding them from C#. Namespace
`VirtualVenues.WorldCreator` unless noted. Prefabs live in `Packages/com.virtualvenues.sdk/Runtime/Resources/`
(load with `Resources.Load<GameObject>("<Name>")`). Menus are under **GameObject > VirtualVenues**.

## How the SDK works

Most components are **markers**: they hold settings and register themselves, and the VirtualVenues player app
(UPC in the browser, UMS on the server) adds the real behaviour and networking when the world loads. A marker
does nothing in your project except what the SDK test player simulates (column **Test player**).
**Self-contained** components run on their own, in the editor and in the app.

Only scripts from this SDK and from Unity packages survive into the app. Any other script on a world object
arrives as a missing script. New SDK components also need a matching app build — check `CHANGELOG.md`.

Lighting, sky and fog come from the main scene's own Lighting settings. The one world-settings component is
`WorldControlSettings` (below).

## Stage, screens, sound, lights

| Component | Add it | Key fields | Needs | Kind | Test player |
|---|---|---|---|---|---|
| `Stage` | **New Stage** (prefab `Stage`: stage + screen + booths) | `_stageIndex` int, `_activateOnStart` (off = dark until someone enters a `StageTrigger`) | — | marker | no |
| `Screen` | prefab `Screen` | `_stageIndex` (a parent `Stage` wins), `_screenMesh` Renderer, `_defaultTexture`, `_source` Artist/Visuals | parent Stage or index | marker | no |
| `Artist` | prefab `Artist` | `_stageIndex`, `_artistMesh`, `_pivot`, `_speakerSource` | parent Stage or index | marker | no |
| `Speaker` | add component | `_stageIndex`, `_source` Artist/Visuals, `_channel` Mono/StereoLeft/StereoRight | `AudioSource` | marker | no |
| `StageTrigger` | **Stage Trigger** (20×10×20 box) | `_stage` (auto: nearest), `_activateOnInitialize` | `Collider` (trigger) | marker | no |
| `Fixture` | **Fixtures/Moving Head Fixture**; PAR and Strobe via the Stage's "+" scene gizmo; prefabs `Moving Head Fixture`, `PAR Fixture`, `Strobe Fixture` | `_stage`, `_type` PAR/MovingHead/Strobe, `_mirrorPanRotation` | under a Stage | marker (DMX) | no |
| `RoomBoundary` | add component | `stageIndex`, `volume` Collider, `privacyBarrier` GameObject | a trigger volume | self-contained | no listener offline |
| `AmbientAudio` | add component (**Ambient Audio**) | AudioSource min/max distance | `AudioSource` | self-contained | silent offline |
| `AudioChannel` | add component | `_channel` id: `background-music`, `ambient-audio`, `sfx` or a custom id | `AudioSource` here or below | marker | no (app mixer) |
| `AudioChannelSet` | add component, one per world | `_channels`: id, display name, parent (one of the three above), default volume; max 8 | — | marker | no |

`RoomAmbientAudio` is the old name of `AmbientAudio`: it still works in published worlds but is hidden from Add
Component. Use `AmbientAudio` in new worlds.

**Audio channels.** Players set a volume per channel in the app. Every world has Master, Stream, Voice Chat,
Background Music, Ambient Audio, Sound Effects and Interface. Untagged AudioSources go to Ambient Audio (under an
`AmbientAudio`) or Sound Effects (everything else, rides and vehicles too); `Speaker` sources always follow Stream.
Background Music has no automatic rule: tag music with an `AudioChannel` set to `background-music`. Add your own
channels with `AudioChannelSet`. The app only sets each source's mixer group; your volume, pitch and mute stay yours.
Old ids from SDK 0.10.13–0.10.14 still work: `rides` = `sfx`, `ambient-music` = `background-music`,
`ambient-environment` = `ambient-audio`.

Stage parts find their stage index from a parent `Stage` first, then their own `_stageIndex`. Visuals screens
show black until the app's visuals stream is live. Shader `Custom/UnlitChromaKeyURP` keys out the artist video.

## Controls

| Component / asset | Add it | Key fields | Kind | Test player |
|---|---|---|---|---|
| `ControlStyle` (ScriptableObject) | **Create > Virtual Venues > Control Style**, or duplicate a built-in from `Runtime/Resources/ControlStyles` | `_id` (stable, lowercase), `_displayName`; **Look** `activation` HoldButton/Locked, `holdButtons` Left/Right, `sensitivity`, `invertY`, `topClamp`/`bottomClamp`; **Camera** `zoomMax`, `defaultZoom`, `shoulderSide`, `fieldOfView`, `allowFirstPerson`; **Facing** `mode` FaceMoveDirection/FaceCamera/FaceCameraWhileHeld, `heldButton`, `turnSmoothTime` | data | Look + Facing yes; Camera framing app only |
| `WorldControlSettings` | add component (one per world) | `_defaultStyle` ControlStyle, `_allowedStyles` (empty = built-ins + default), `_allowPlayerChoice` | marker | yes |

Built-ins: `classic` (hold a mouse button to look, face where you walk — the default), `shooter` (click to lock
the cursor, always face the camera, sideways keys strafe, Ctrl frees the cursor) and `mmo` (left-drag orbits,
right-drag turns the character, both buttons run). The active style is `ControlStyles.Active`: the player's pick
(when the world allows it) > the world's default > Classic. Styles are data only, so a custom one ships inside
your world. Venue launch settings for zoom (`-zoomMax`, `-defaultZoomEval`) override a style's zoom.

## Spawning

| Component | Add it | Key fields | Kind | Test player |
|---|---|---|---|---|
| `SpawnPoint` | **Spawn Point** (prefab `SpawnPoint`) | `_isDefault`, `_team` | marker | yes (random pick) |

Every world needs at least one. Nothing checks this at publish.

## Interaction, seats, vehicles

| Component | Add it | Key fields | Needs | Kind | Test player |
|---|---|---|---|---|---|
| `Interactable` | add component | `_interactionDisplayText` ("Press {0} to interact"), `_interactionCollider`, `_highlightRenderers`, `_onLocalInteract` UnityEvent | trigger collider | marker | yes (E key) |
| `Seat` | **New Seat** (prefab `Seat`) | `_seatPoint` (default: self) | `Interactable` + trigger collider | marker | yes |
| `Vehicle` | **New Go Kart** / **New Jet Ski** | `_vehicleType` Car/Boat, `_seats` (seat 0 drives), `_wheels`, `_stats` (topSpeed, acceleration, steer, grip, mass), `_boatStats`, `_events` (see Vehicle events) | `Interactable` + trigger collider; Boat needs a Swim Zone or water | marker | yes (boats float on the Swim Zone and collide like the app; cars use an arcade drive with no collisions; look-around, driver/throttle/speed/water events) |
| `TriggerEvent` | **New Trigger Event** | `_onLocalPlayerEnter`, `_onEveryone` UnityEvents, `_cooldown`, `_once` | trigger BoxCollider | marker | yes |
| `AdminEvent` | add component, or **New Admin Event** | `_eventName` ("fireworks", case-insensitive), `_onEveryone` UnityEvent, `_cooldown` (10 s), `_description` | — | marker | **Fire now** button in Play mode |
| `CustomControls` | add component, or **New Custom Controls Zone** | `_showWhen` (WhileSeated / AfterInteract / WhileInRange / WhileInZone), `_controls[]` (label, icon, cooldown 0.2 s, `onPressed` + `onEveryone` UnityEvents) | a seat, an `Interactable`, or a trigger collider, depending on `_showWhen` | marker | yes (on-screen buttons) |
| `BrowserBridgeInteractableData` | add next to `Interactable` | `_browserBridgePayloadJson` (sent to the web page) | `Interactable` | marker | no |

`Seat` and `Vehicle` add their `Interactable` in `Reset()`, which runs from the menu or Inspector but **not** from
`AddComponent` in code. Use the prefab, or add the `Interactable` and trigger collider yourself.

### Vehicle events (engine audio, spray, lights)

A `Vehicle`'s **Events** foldout runs on every player's game, so everyone hears and sees them. Cars and boats
alike.

- **Driver Enter / Exit**: e.g. an engine-start one-shot; an engine loop is Driver Enter → `AudioSource.Play`
  (Loop on) and Driver Exit → `AudioSource.Stop`.
- **Throttle Start / Stop**: the driver starts / stops pressing forward (rev sound, wake spray).
- **Passenger Enter / Exit**: someone takes / leaves a seat other than seat 0.
- **Enter / Exit Water**: boats only, when the hull lands in or lifts out of the water (splash).
- **On Speed**: speed as a fraction of `_stats.topSpeed`, mapped into **Speed Output Range**. Pick
  `AudioSource.pitch` from the *dynamic float* list with a range like 0.8–2 for an engine that revs with speed.
  Boats don't use `topSpeed` to drive, so set it to the boat's real top speed for On Speed to reach 1.
- Players who join while someone drives get Driver Enter (and Throttle Start if held) so loops start; one-shot
  events (passenger, water) don't replay.
- TestPlayer: Driver, Throttle, On Speed and (boats) Enter / Exit Water fire; Passenger doesn't (one player).

The driver can look around: the camera turns with the mouse by the world's control style (hold a mouse button in
Classic/MMO, always in Shooter), stays where you leave it, and swings back behind the vehicle once you drive
forward. `_cameraDistance` / `_cameraHeight` set where it rests.

### Admin events (cues fired from chat)

An `AdminEvent` is a named cue for a show moment: fireworks, a light change, a curtain drop. An admin types
`/event fireworks` in chat and every player in the world runs **On Everyone** at the same time; `/events` lists the
names (with descriptions) in the current world, visible only to the admin.

- Wire **On Everyone** to plain targets such as `Animator.SetTrigger` or `ParticleSystem.Play`.
- Names are case-insensitive. Several markers with the same name all fire (e.g. one per launch site).
- The cooldown is enforced by the server, per name; markers sharing a name use the longest one.
- One-shot: players who join after it fired don't replay it. Leave the end state of a cue in a shape that is fine
  to miss.
- Only admins can fire them; the server checks, not the player's app.
- Test in Play mode with the **Fire now** button in the Inspector (runs every marker with that name, locally).

### Custom controls (on-screen buttons)

`CustomControls` shows a row of tap/click buttons while the local player is in a context. The newest shown set is
on screen; when it hides, the previous one comes back. `Reset()` picks `_showWhen` from what is next to it: under a
`Seat`/`Vehicle` → WhileSeated, next to an `Interactable` → AfterInteract, otherwise → WhileInZone (adds a trigger box).

- **WhileSeated**: put it on a vehicle's seat Transform (per seat), on a `Seat`, or on the `Vehicle` root (covers
  every seat without its own, including the default driver seat).
- **AfterInteract**: press the Interactable's key to open, again (or walk away) to close. **WhileInRange**: while highlighted.
- **WhileInZone**: while the player is inside the trigger collider.
- `onPressed` runs only for the presser; `onEveryone` runs for everyone (server-checked: seated on it, or within 10 m).
- `Vehicle.Push(m/s)` and `Vehicle.Turn(deg/s)` nudge a vehicle. **Wire them into `onEveryone`**: they only act where
  the vehicle's physics runs (the driver's client, or the server when nobody drives).

Canoe: `Vehicle` (Boat), seat 0 has `CustomControls` with *Switch side* (Everyone → paddle Animator trigger),
*Paddle forward* (Everyone → `Vehicle.Push(2)`), *Paddle back* (Everyone → `Vehicle.Push(-2)`). Steering stays on
WASD/joystick. Not authorable in the web world editor yet.

## Zones

A zone = a GameObject with a trigger collider + `TriggerZone` (`_zoneName`) + one zone-data component.

| Zone data | Add it | Key fields | Test player |
|---|---|---|---|
| `SwimZoneData` | **Swim Zone** | — (players swim inside) | no |
| `BounceZoneData` | **Bounce Pad** | `_direction` Transform (empty = up), `_force` 20, `_onBounce` | no |
| `PushZoneData` | **Push Zone** | `_direction`, `_initialForce` 5, `_force` 1 | no |
| `RespawnZoneData` | **Respawn Zone** | `_respawnPoint` (empty = a spawn point), `_affectsPlayers`, `_affectsVehicles` | no |
| `VoiceZoneData` | add to a **Trigger Zone** | `_conversationalDistance` 5, `_audibleDistance` 32 | no |
| `AreaZoneData` | add to a **Trigger Zone** | `_areaId`, `_prompt`, `_targetRoomId` | no |

**Waves:** add `WaterWaves` (**VirtualVenues/Water Waves**) to the water surface's Renderer over a Swim Zone:
`_height` (crest to trough, 0.4 m), `_length` 10 m, `_speed` 1.5 m/s, `_direction` 45° (0 = +Z, 90 = +X). Boats ride
them in the app and the test player. The water shows them only if its shader includes
`Runtime/Shaders/VVWaterWaves.hlsl` (Shader Graph Custom Function `VVWaterWaves`: world position in, height and
world normal out; add the height to the vertex y) and the mesh has about one vertex every 2 m. A water Renderer
without `WaterWaves` stays flat, so pools can share the lake's material.

## Things that move

| Component | Add it | Key fields | Kind | Test player |
|---|---|---|---|---|
| `RotatingObject` | add component | `rotationAxis` (deg/s), `networkSynced` | self-contained | yes (can't ride it) |
| `AnimatedObject` | **New Animated Object** | `_mode` Move/Rotate/Scale, `_from`, `_to`, `_duration`, `_easing`, `_carryPlayers` | self-contained | yes (carry) |
| `KeepUpright` | add component on a child of a spinner | `_carryPlayers` | self-contained | yes (carry) |
| `SplineMover` | **New Spline Ride** | `_spline`, `_duration`, `_loopMode` Loop/PingPong/Once, `_alignToSpline`, `_carryPlayers` | self-contained | yes (carry) |
| `SplineTrack`, `SplineRide`, `TrackSupportBlocker` | **New Roller Coaster**, **Support Blocker** | see `Rides.md` | self-contained | yes |
| `RideSounds` | add component on a cart (next to `SplineMover`) | `_speedLayers[]` (AudioSource, min/max speed m/s, fade, pitch at min/max, max volume), `_lift`, `_booster`, `_brake`, `_stationStop`, `_stationLaunch` AudioSources | self-contained | yes |
| `Pathway`, `PathwayPreset` (kit) | **New Pathway** | see `Paths.md` | self-contained | yes |
| `PhysicsObject` | **New Physics Ball** | `_mass`, `_respawnBelowY`, `_respawnAfterIdle`, `_pushableByPlayers` | marker (server physics) | no |
| `Billboard` | add component | `_upOverride`, `_lookOffset`, `_flipZ` | self-contained | yes |

Turn off **Occlusion Static** on anything that moves.

### Ride sounds (coaster carts)

`RideSounds` makes a cart sound like it moves: looping layers fade in and out and rise in pitch with the cart's
speed, a chain loop plays on Lift sections, and one-shots play on entering a Booster or Brake and when the cart
stops and launches at a station. Everyone hears the same thing at the same moment from the shared ride clock;
late joiners get no one-shots. Put it on the lead car (or each car you want heard) with 3D AudioSources on the
cart. Details and API in `Rides.md`.

## Collision

| Component | Add it | Key fields | Kind | Test player |
|---|---|---|---|---|
| `CollisionScope` | **Add Component > VirtualVenues > Collision Scope** | `_stopsPlayers` (default on), `_stopsCamera`, `_stopsVehicles` | marker | players + camera (see below) |

- Tick what the object stops; anything unticked passes through. **Players**: players bump into it and stand on it.
  **Camera**: the camera stops at it instead of going through. **Vehicles**: vehicles bump into it and drive on it.
  Examples: invisible wall or rail = Players; keep the camera out of a rock = Camera; kart-only barrier = Vehicles;
  road players and karts use but the camera ignores = Players + Vehicles. All three = a normal collider. None = it
  stops no players, camera or vehicles (clicks and raycasts still hit it).
- Covers every solid (non-trigger) collider on the object and its children. A `CollisionScope` further down owns its
  own children. A solid collider on the same GameObject as a trigger is skipped (red in the Scene view) — move it
  to its own child. Gizmo: blue = Players, yellow = Camera, green = Vehicles, mixed when several are ticked.
- Don't set layers yourself; your project needs no extra layers. When the world loads the app sets each collider's
  layer and its **Include / Exclude Layers** (Vehicle). Don't author those two lists yourself on scoped colliders.
- **Static colliders only.** The boxes cover players, the camera and vehicles; other physics bodies (balls) follow the
  layer the app picks, and a Rigidbody on a scoped object can fall through the floor.
- Test player: colliders that stop the camera move to Default in Play mode, the rest to built-in layer 1
  (TransparentFX, still solid and counted as ground), and the test character ignores colliders that don't stop
  players. Inside such a box the character counts as grounded (it sinks slowly instead of falling); a collider
  disabled and re-enabled in Play mode is solid again for the test character. **Vehicles is app only.**

## NFT minting (namespace `VirtualVenues.Plugins.Nmkr`)

| Component | Add it | Key fields |
|---|---|---|
| `NmkrPluginSettings` | **Plugins/NMKR/Add NMKR Plugin** (one per scene) | filled at build from the `NmkrPluginConfig` asset — leave empty |
| `NmkrMintInteractable` | **Plugins/NMKR/Add Mint Interactable** | `_projectUid`, `_nftCount`, `_dropTitle`, `_dropDescription`, `_dropImage` |

Settings window: **VirtualVenues > Plugins > NMKR Settings**. Full guide:
`Editor/Plugins/Nmkr/nmkr-getting-started.md`. Not simulated by the test player.

## Not world props

Published with the **Avatar Publisher**, not placed in worlds: `Avatar` (namespace `VirtualVenues`), `Mount`,
`PetFollower`, and the action assets `DanceAction` / `VfxAction` / `SprayAction`
(**Assets > Create > Virtual Venues > Actions**).

## Testing

`Runtime/Prefabs/TestPlayerSpawner.prefab` — drop it in the scene and press Play. It spawns a third-person
player at a random `SpawnPoint` and simulates what the **Test player** columns say. Remove it before publishing.

## Configure(...) — setting markers from code

Markers expose `Configure` so code can set them without `SerializedObject`:
`Stage.Configure(int stageIndex, bool activateOnStart = true)`, `Screen.Configure(int stageIndex, string source)`,
`Speaker.Configure(int stageIndex, string speakerSource, int speakerChannel)`,
`SpawnPoint.Configure(bool isDefault, string team)`, `TriggerZone.Configure(string zoneName)`,
`BounceZoneData.Configure(Transform direction, float force)`,
`PushZoneData.Configure(Transform direction, float initialForce, float force)`,
`RespawnZoneData.Configure(Transform respawnPoint, bool affectsVehicles, bool affectsPlayers)`,
`VoiceZoneData.Configure(float conversationalDistance, float audibleDistance)`,
`AnimatedObject.Configure(AnimationMode mode, Vector3 from, Vector3 to, float duration, bool easing, bool carryPlayers)`,
`SplineRide.Configure(bool useGravity, float cruiseSpeed, float friction, float drag)`.

`Configure` changes are not saved by Unity on their own: call `EditorUtility.SetDirty(component)` and save the
scene. For anything else, use `SerializedObject` (field names are in the tables above).
