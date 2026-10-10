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
| `RoomAmbientAudio` | add component | AudioSource min/max distance | `AudioSource` | self-contained | silent offline |

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
| `Vehicle` | **New Go Kart** / **New Jet Ski** | `_vehicleType` Car/Boat, `_seats` (seat 0 drives), `_wheels`, `_stats` (topSpeed, acceleration, steer, grip, mass), `_boatStats` | `Interactable` + trigger collider; Boat needs a Swim Zone or water | marker | yes (arcade drive) |
| `TriggerEvent` | **New Trigger Event** | `_onLocalPlayerEnter`, `_onEveryone` UnityEvents, `_cooldown`, `_once` | trigger BoxCollider | marker | yes |
| `CustomControls` | add component, or **New Custom Controls Zone** | `_showWhen` (WhileSeated / AfterInteract / WhileInRange / WhileInZone), `_controls[]` (label, icon, cooldown 0.2 s, `onPressed` + `onEveryone` UnityEvents) | a seat, an `Interactable`, or a trigger collider, depending on `_showWhen` | marker | yes (on-screen buttons) |
| `BrowserBridgeInteractableData` | add next to `Interactable` | `_browserBridgePayloadJson` (sent to the web page) | `Interactable` | marker | no |

`Seat` and `Vehicle` add their `Interactable` in `Reset()`, which runs from the menu or Inspector but **not** from
`AddComponent` in code. Use the prefab, or add the `Interactable` and trigger collider yourself.

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

## Things that move

| Component | Add it | Key fields | Kind | Test player |
|---|---|---|---|---|
| `RotatingObject` | add component | `rotationAxis` (deg/s), `networkSynced` | self-contained | yes (can't ride it) |
| `AnimatedObject` | **New Animated Object** | `_mode` Move/Rotate/Scale, `_from`, `_to`, `_duration`, `_easing`, `_carryPlayers` | self-contained | yes (carry) |
| `KeepUpright` | add component on a child of a spinner | `_carryPlayers` | self-contained | yes (carry) |
| `SplineMover` | **New Spline Ride** | `_spline`, `_duration`, `_loopMode` Loop/PingPong/Once, `_alignToSpline`, `_carryPlayers` | self-contained | yes (carry) |
| `SplineTrack`, `SplineRide`, `TrackSupportBlocker` | **New Roller Coaster**, **Support Blocker** | see `Rides.md` | self-contained | yes |
| `Pathway`, `PathwayPreset` (kit) | **New Pathway** | see `Paths.md` | self-contained | yes |
| `PhysicsObject` | **New Physics Ball** | `_mass`, `_respawnBelowY`, `_respawnAfterIdle`, `_pushableByPlayers` | marker (server physics) | no |
| `Billboard` | add component | `_upOverride`, `_lookOffset`, `_flipZ` | self-contained | yes |

Turn off **Occlusion Static** on anything that moves.

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
