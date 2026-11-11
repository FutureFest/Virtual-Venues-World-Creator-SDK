# Water and boats: swimming, waves, wakes, jetskis

How water works in a VV world, what a creator (or an AI agent working in a creator project) can change, and what
only the app can change. Namespace: `VirtualVenues.WorldCreator`. This package installs at
`Packages/com.virtualvenues.sdk/`; its files are read-only in a creator project.

Menu: **GameObject > VirtualVenues > Swim Zone / New Jet Ski**. Add Component: **VirtualVenues > Water Waves**.

## The pieces

| Piece | What it is | You make it from |
|---|---|---|
| Water surface | Any mesh + a transparent water material. Looks only. | Your own mesh and shader |
| `TriggerZone` + `SwimZoneData` + trigger collider | "This volume is water." Players swim in it. **The top of the volume is the water line boats float on.** | SDK |
| Swim floor | A solid, invisible collider players stand on (a Swim Zone does not hold players up). | A BoxCollider on Unity's built-in **Water** layer |
| `WaterWaves` | Rolling swells + boat wakes on one water Renderer. Physics and shader read the same numbers. | SDK, on the water surface's Renderer |
| `Vehicle` (type Boat) + `Interactable` + trigger collider | A drivable boat. Data only: the app adds physics and networking; the TestPlayer imitates it. | SDK (`Resources/JetSki.prefab` is the example) |
| `Runtime/Shaders/VVWaterWaves.hlsl` | The shader side of `WaterWaves`: swell + wake height, slope, foam. | Include it in your water shader |

Only SDK scripts and Unity's own components ship with a world. You cannot add a script to change boat physics;
you change it through the fields below, the Rigidbody, colliders and events.

## Rules that bite

1. **Water line = top of the first trigger collider on the Swim Zone object.** `TriggerZone` binds the first
   trigger collider; extra trigger colliders on the same object (for example a thin "make players swim" slab) must
   come after it. Never add a second `SwimZoneData` with a higher top over the same water: boats take the highest.
2. **Swim floors go on the built-in Water layer (4).** Boats pass through Water-layer colliders. A floor on any
   other layer holds a boat up; with the floor at the water line the boat then slides on it like a sled (half
   speed, flat, hardly steers). Nothing else belongs on the Water layer if boats must hit it.
3. **A boat rests with its hull bottom 0.15 m under the water line** (`BoatFloat.Draft`). The hull bottom is the
   lowest point of the boat's meshes, so author the pivot wherever you like, but place boats near that height.
4. **Boats stop at everything solid that is not on the Water layer**: terrain, docks, other boats. To make a prop
   stop boats but not players (an invisible shore wall), use `CollisionScope` with only *Stops Vehicles* ticked. To
   let boats through a prop, untick it. Never set raw layer numbers.
5. **No Swim Zone under a boat = no water.** With no Swim Zone in the world at all the boat hangs still and logs a
   warning; outside a zone's footprint it falls like any body.
6. **`WaterWaves` sits on the water surface's Renderer**, and covers that Renderer's bounds. A water Renderer
   without it stays flat, even with the same material (pools next to a lake).
7. Thrust and steering only work while the keel is within 0.1 m of the water (no driving up beaches).

## Waves and wakes: `WaterWaves`

| Field | Default | Does |
|---|---|---|
| `_height` | 0.4 | Swell, crest to trough, metres. 0 = flat. |
| `_length` | 10 | Metres between crests. |
| `_speed` | 1.5 | How fast crests travel, m/s. |
| `_direction` | 45 | Compass degrees the swell travels: 0 = +Z, 90 = +X. |
| `_wakes` | on | Boats leave ripples (Play mode only). |
| `_wakeStrength` | 1 | How hard a hull pushes the water. 1 = about 0.2 m behind a jetski at 11 m/s. |
| `_wakeSpeed` | 3.5 | How fast ripples spread, m/s. Higher = wider V behind the boat. |
| `_wakeFade` | 0.5 | How quickly ripples die. Higher = shorter wakes. |
| `_wakeHeight` | 0.3 | Tallest a ripple may get, metres. Lower it when decks or floors sit close to the water. |

- Swells are two sine waves (a main one and a smaller cross swell). In the app they follow the network clock, so
  every player sees the same crests.
- Wakes are a ripple simulation on a grid over the Renderer's bounds: at most 256 cells on the longer side, so
  cell size = longer side / 256 (0.5 m at least). A 200 m lake gets 0.8 m cells; ripples shorter than two cells
  cannot exist. For crisper wakes on a big lake, use a smaller water Renderer where the boats are. The simulation
  sleeps when no boat moves.
- Every Boat `Vehicle` on the surface makes a wake by itself, on every player's game. Nothing to wire. Wakes are
  not synced between players; each game simulates them from the boats it sees.
- Ripples do not bounce off shores or islands; they pass under them and die at the grid edge.
- Boats ride both: the hull lifts and tilts with the surface and is pushed down its slope.
- From code (Editor tools, tests): `WaterWaves.Sample(worldPoint, out normal)` returns the height above the still
  line (swell + wake); `waves.Wake` is the `WaterWakeSim` while playing.

Set fields from code with `SerializedObject` (`FindProperty("_height").floatValue = ...`); the component is
`[ExecuteAlways]`, so the swell previews in the Scene view.

## Making the water show it: `VVWaterWaves.hlsl`

```hlsl
#include "Packages/com.virtualvenues.sdk/Runtime/Shaders/VVWaterWaves.hlsl"

float height; float2 slope;
VVWaterWaves(positionWS.xz, height, slope);   // metres above the still line, and (dh/dx, dh/dz): swell + wakes
float foam = VVWakeFoam(positionWS.xz);       // 0..1, left behind by boats
```

- Vertex: add `height` to the vertex's world Y (use the UNDISPLACED world xz). Fragment: tilt the normal by
  `slope` (world normal = `normalize(float3(-slope.x, 1, -slope.y))`) and add `foam` to your foam.
- Shader Graph: Custom Function node, File mode, this file, name `VVWaterWaves` (world position in; height and world
  normal out).
- The mesh needs vertices to bend: about one every 2 m. A Unity Plane scaled to a lake has 20 m cells and stays
  flat. Pad the mesh bounds by the wave height so it is not culled early.
- The values arrive per Renderer through a MaterialPropertyBlock (`_VVWaveA`, `_VVWaveB`, `_VVWakeTex`,
  `_VVWakeRect`, `_VVWakeCell`). Do not declare them as material properties and do not set them yourself.
- Web-safe: plain HLSL, one small RGHalf texture, no compute shaders, no keywords.

## Boats: `Vehicle` with `_vehicleType` = Boat

| Field | Default | Does |
|---|---|---|
| `_boatStats.enginePower` | 11 | Forward thrust, m/s². Top speed = `enginePower / drag.z`. |
| `_boatStats.turnPower` | 5 | Steering, rad/s². Top turn rate at full throttle = `turnPower / spin damping` (see below). |
| `_boatStats.buoyancy` | 10 | How stiffly the water holds the hull. Higher = less bob. Rest depth stays 0.15 m. |
| `_boatStats.drag` | (2, 3, 1) | Water drag sideways (x), up/down (y), forward (z). Higher x = more grip, less drift. |
| `_stats.mass` | 250 | Rigidbody mass: what it does to things it hits. Boat forces ignore mass. |
| `_stats.topSpeed` | 15 | NOT used to drive a boat. Set it to the real top speed so the **On Speed** event reaches 1. |
| `_seats` | none | Seat 0 drives. Empty = one driver seat at `_driverSeatOffset`. |
| `_cameraDistance`, `_cameraHeight` | 6, 2 | Driver camera. |
| `_events` | | Driver Enter/Exit, Throttle Start/Stop, Passenger Enter/Exit, **Enter/Exit Water**, **On Speed**. |

How it drives (fixed in the app and the TestPlayer, not editable from a world):

- Jet steering: full `turnPower` with the throttle held, up to 60% from speed alone (reached at 6 m/s), 20% standing
  still. Brake = reverse at half thrust.
- Drift: the hull grips sideways until it slides faster than 1.5 m/s, then lets go (down to 45% of `drag.x` at
  6 m/s) and grips again as the slide dies. Raise `drag.x` for a boat on rails, lower it for a looser one.
- Lean and bow lift come from the model: about 8 degrees bow up at full throttle, leaning into turns.
- Spin damping from the water is 2 per second. A **Rigidbody on the boat's root** is reused: set its Angular
  Damping above 2 for a calmer boat (2.5 = 115 deg/s top turn rate at `turnPower` 5), leave it lower for the
  default 143 deg/s. Its Collision Detection and mass constraints are kept too; Is Kinematic is the app's flag.
- The boat needs at least one solid (non-trigger) collider for its hull, on a child or the root. The object with
  the Interactable trigger keeps its own layer; hull colliders are moved to the app's Vehicle layer at load.

### Looks and sound (all world-side, no scripts)

- **Spray, foam trails:** built-in Particle Systems as children, **Rate over Distance** emission and
  **Emitter Velocity Mode = Transform** (other players' boats are moved by the network, not by their Rigidbody, so
  Rigidbody mode emits nothing for them). Simulation Space World. On a `WaterWaves` surface they are cleared when the boat teleports (no foam streak on respawn).
- **Engine and splash:** AudioSources driven by the `_events` (Driver Enter -> Play, Driver Exit -> Stop, On Speed ->
  `AudioSource.pitch` with a Speed Output Range like 0.6-1.8, Enter Water -> splash one-shot or particle burst).
- Put particle renderers in the boat's LODGroup so they cull with the hull.

## Testing without the app

- **TestPlayer (Play mode):** boats float, bob, lean, drift, collide and make wakes with the same code as the app
  (`BoatFloat`, `WaterWakeSim`). Not covered: other players, passengers, the app's Vehicle layer (a `CollisionScope`
  with *Stops Vehicles* off still stops a test boat).
- **Without Play mode** (Editor scripts, never during another agent's work in a shared Editor): build the test in
  `EditorSceneManager.NewPreviewScene()`; it has its own physics (`scene.GetPhysicsScene().Simulate(dt)`).
  `TestBoat.Attach(vehicle)` gives the boat the app's body, `boat.SetInput(accelerate, brake, turn)` then
  `boat.Step(zones)` once per physics step. `WaterWakeSim` is a plain class: `Disturb`, `Step`, `Sample`.
  `WaterWaves.Sample` is geometric (it checks Renderer bounds), so keep test objects away from real water.

## What needs the app

Changing the float model, steering, drift or wake physics means changing `BoatFloat` / `WaterWakeSim` in the SDK
and shipping a new app build (web client and server together). Ask for it; do not work around it in a world.
Worlds can only use an SDK version the live app also has.
