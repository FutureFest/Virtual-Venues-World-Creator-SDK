# Rides: tracks, supports, tunnels

How the SDK's spline rides fit together. This page is for creators, and for AI agents that build or edit rides
from C# or through the Unity CLI in a creator project (this package installs at `Packages/com.virtualvenues.sdk/`).
Namespace: `VirtualVenues.WorldCreator`.

Example scene: `Runtime/Examples/RollerCoaster/RollerCoasterExample.unity`. It has a lift, a drop, banked turns,
generated supports, and a guest tunnel under the lift hill that the supports stand on.
Menu: **GameObject > VirtualVenues > New Roller Coaster / Support Blocker**.

## Components

| Component | Lives on | Owns |
|---|---|---|
| `SplineContainer` (Unity Splines) | the track object | The track shape. Knot roll = banking. |
| `SplineTrack` | the track object | The generated mesh: rails, ties, **support posts**. |
| `SplineRide` | the track object | Ride physics (gravity, Lift/Booster/Brake/Station sections, track events). |
| `SplineMover` | each cart | Moves the cart along the container. |
| `TrackSupportBlocker` | any object with a `Collider` | Whether support posts stop on that collider. |

## The support-post rule

`SplineTrack` with **Show Supports** on drops one post every **Support Spacing** metres, straight down in the
track object's local space. Each post stops at the **first** of these below it:

1. A **solid** (non-trigger) collider: ground, terrain, a tunnel roof, a building.
2. A **trigger** that has a `TrackSupportBlocker` in `Block` mode (an invisible "no posts below here" box).
3. **Ground Height** (local Y). Posts that hit nothing stop here.

Ignored on the way down:
- Colliders under the track object itself.
- Colliders under any `SplineMover` (carts).
- Plain triggers without a blocker.
- Any collider whose `TrackSupportBlocker` is set to `LetSupportsThrough` (fences, lamps).

Posts shorter than **Min Height** are skipped (the track sits on the ground, e.g. at the station).

**Tunnels:** give the tunnel's roof a collider. Posts from track above stand on the roof instead of poking into
the tunnel. Nothing else is needed. Add a `TrackSupportBlocker` only if the roof is a trigger or has no mesh.

## When the mesh rebuilds

The track mesh is `HideFlags.DontSave`: it is never saved in the scene, and is rebuilt:
- in `OnEnable`, and again in `Start` at runtime (so colliders from the rest of the world exist first);
- when a knot of this track's spline changes (editor);
- when any Transform or Collider is changed through the editor (Undo-recorded edits), or the hierarchy changes;
- when a `TrackSupportBlocker` is enabled, disabled or changed;
- when you call `Rebuild()` / `SplineTrack.RebuildAll()`.

**From a script, call `SplineTrack.RebuildAll()` after you create, move or delete colliders.** Direct
`transform.position = …` from code is not Undo-recorded, so the editor hook doesn't see it.
Supports re-cast `Physics.SyncTransforms()` first, so a just-moved collider is hit at its new spot.

## API

```csharp
// SplineTrack
void Rebuild();
static void RebuildAll();
void ConfigureParts(bool leftRail, Material leftMat, bool rightRail, Material rightMat, bool ties, Material tieMat);
void ConfigureSupports(bool show, Material material, float spacing, float radius, float minHeight, float groundHeight);
bool StopsSupports(Collider c);   // the rule above, for tools that want to preview it
Mesh TrackMesh { get; }           // last submesh = supports when they use their own material
bool ShowSupports { get; }  float SupportSpacing { get; }  float GroundHeight { get; }

// TrackSupportBlocker (requires a Collider)
enum BlockMode { Block, LetSupportsThrough }
BlockMode Mode { get; }
void SetMode(BlockMode mode);                                                   // rebuilds all tracks
static TrackSupportBlocker Create(Vector3 position, Transform parent = null);    // editor only: invisible trigger box
```

## Recipe: a coaster over a tunnel, from an editor script

```csharp
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using VirtualVenues.WorldCreator;

var go = new GameObject("Coaster", typeof(SplineContainer));
UnityEngine.Splines.Spline spline = go.GetComponent<SplineContainer>().Spline;   // fully qualified, see Traps
spline.Clear();
spline.Add(new BezierKnot(new float3(0, 2, 0)), TangentMode.AutoSmooth);
spline.Add(new BezierKnot(new float3(0, 12, 20)), TangentMode.AutoSmooth);
spline.Add(new BezierKnot(new float3(0, 12, 40)), TangentMode.AutoSmooth);
var track = go.AddComponent<SplineTrack>();              // adds MeshFilter + MeshRenderer
go.GetComponent<MeshRenderer>().sharedMaterial = railMaterial;

// Tunnel under the high part: a solid roof is all it takes.
var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);  // comes with a BoxCollider
roof.transform.position = new Vector3(0, 4, 30);
roof.transform.localScale = new Vector3(6, 0.5f, 8);

track.ConfigureSupports(true, postMaterial, 6f, 0.15f, 1.5f, 0f);   // rebuilds; posts near z=30 stop at y=4.25
```

Keep posts out of a walkway without a visible object: make a trigger `BoxCollider` over the path and add
`TrackSupportBlocker` (or call `TrackSupportBlocker.Create(pos)` in the editor), then `SplineTrack.RebuildAll()`.

## Traps

- In a `FutureFest.*` namespace a bare `Spline` resolves to `FutureFest.Spline`. Write `UnityEngine.Splines.Spline`.
- Non-uniform scale on the track object makes carts drift off the rails. Keep the track at scale 1.
- In Prefab Mode there is no scene physics, so posts reach Ground Height. That's expected; they re-cast in the scene.
- Coaster carts need **Keep Upright** off on their `SplineMover`, or they stay level on sloped rails.
- Ride timing (`SplineRide`) is baked in world space on enable. Don't move or rotate a ride at runtime.
