# Pathways

`Pathway` (namespace `VirtualVenues.WorldCreator`) turns a spline into a finished path: a ground strip plus props
(lamp posts, fences, plants, benches) laid out by rules. Add it with **GameObject > VirtualVenues > New Pathway**,
or `AddComponent<Pathway>()` on an object (it adds the `SplineContainer`, `MeshFilter` and `MeshRenderer` it needs).

It is **self-contained**: only the spline, the rules and the seed are saved. The strip and props are rebuilt when
the world loads (in the editor and in the app), and live while you drag the spline. **Bake** turns the props into
normal saved objects that stop rebuilding.

## Main fields

| Field | Meaning |
|---|---|
| `_seed` | Every random choice comes from it. Same seed = same path. **Reshuffle** picks a new one. |
| `_snapToGround` | Drop the strip and props onto the colliders under the path (terrain, floors). Off = on the spline. |
| `_showGround`, `_width`, `_groundMaterial`, `_textureLength`, `_groundHeight` | The ground strip. Use a URP/Lit material. |
| `_junctionRadius` | Where paths meet, the strips stop this far from the middle and a patch fills it. 0 = automatic. |
| `_rules` | List of `PathwayRule`, one per layer of props. Earlier rules win keep-clear clashes. |

## Rules (`PathwayRule`)

| Field | Meaning |
|---|---|
| `Props` | Prefabs to pick from, each with a weight. Use script-free prefabs (or SDK / Unity scripts only). |
| `Slot` | `Every` (one per Spacing), `Segments` (pieces end to end, stretched to meet on curves: fences, walls, hedges), `Start`, `End`, `Corners` (path points turning by `CornerAngle` or more), `Junctions` (one per corner where paths meet; Center lane = one in the middle). |
| `Lane` | `Center`, `Left`, `Right`, `BothSides`, `Alternating`. |
| `Offset` | Metres beyond the path edge (negative = in from the edge). Center lane: metres right of the middle. |
| `Spacing` | Metres between props, or one piece's length for `Segments`. |
| `StartInset`, `EndInset` | Metres left empty at each end. |
| `Jitter`, `Chance` | Nudge along the path (fraction of Spacing); chance each placement gets a prop. |
| `Facing`, `RandomTurn` | `AlongPath`, `TowardPath`, `AwayFromPath`, `Random`; extra random turn in degrees. |
| `MinScale`, `MaxScale` | Random uniform size. |
| `Clearance` | Keep-clear radius: skip a prop that lands this close to an earlier one that also has a radius. |
| `FollowGround` | Snap this rule's props to the ground (when the path snaps). |

Each rule draws its own random numbers, so editing one rule never reshuffles another.

## From code

```csharp
var go = new GameObject("Park Path", typeof(SplineContainer));
var spline = go.GetComponent<SplineContainer>().Spline;
spline.Clear();
spline.Add(new BezierKnot(new float3(0, 0, 0)), TangentMode.AutoSmooth);
spline.Add(new BezierKnot(new float3(0, 0, 20)), TangentMode.AutoSmooth);
spline.Add(new BezierKnot(new float3(10, 0, 30)), TangentMode.AutoSmooth);
var path = go.AddComponent<Pathway>();
path.Configure(seed: 1, snapToGround: true, showGround: true, width: 3f, groundMaterial: gravel, rules: new[]
{
    new PathwayRule("Lamps", PathwaySlot.Every, PathwayLane.Alternating, 8f, lampPrefab),
    new PathwayRule("Fence", PathwaySlot.Segments, PathwayLane.BothSides, 2f, fencePrefab) { Offset = 1f },
    new PathwayRule("Plants", PathwaySlot.Every, PathwayLane.BothSides, 1.5f, bushPrefab, flowerPrefab)
        { Offset = 2f, Jitter = 0.6f, Chance = 0.7f, RandomTurn = 180f, MinScale = 0.7f, MaxScale = 1.3f, Clearance = 0.4f },
});
```

`path.ComputePlacements()` lists every prop without creating anything (for checks and tests).

## Junctions (path networks)

Every spline in the Pathway's `SplineContainer` is a path. Where knots of different splines are **linked**, the
paths meet in a junction:

- The strips stop short of the junction and a patch fills the middle.
- Every prop stays off the other paths. A side-lane prop also stays out of the other paths' matching lane, so two
  fences close into a corner instead of crossing. A fence running straight past a branch keeps going on the far side.
- `Start` / `End` skip path ends joined to a junction, and `Corners` skips linked points.
- `Junctions` rules put props on each corner (lamps, bollards), or one in the middle on the Center lane (fountain).

Make one: in the Scene view use **Draw Splines** and start the click on an existing path point (that links it), or
put two points on top of each other and press **Link Touching Points**. From code:

```csharp
var container = go.GetComponent<SplineContainer>();
Spline branch = container.AddSpline();
branch.Add(new BezierKnot(container.Spline[1].Position), TangentMode.AutoSmooth);   // starts on main point 1
branch.Add(new BezierKnot(new float3(15, 0, 5)), TangentMode.AutoSmooth);
container.KnotLinkCollection.Link(new SplineKnotIndex(0, 1), new SplineKnotIndex(1, 0));
path.Rebuild();
```

Paths cross only where they have a linked point; separate Pathway objects never know about each other.

## Kits

A **Path Kit** (`PathwayPreset`, **Create > VirtualVenues > Pathway Kit**) stores the ground and rules. **Save As
Kit...** in the Pathway Inspector saves one; **Load** copies a kit onto a path (keeps its shape and seed).
From code: `path.ApplyPreset(kit)`.

## Unity Terrain

The Inspector's **Unity Terrain** foldout stamps the path into the active Terrains under it: **Flatten** (levels the
ground across the path; snapped paths keep following the hills), **Paint** a Terrain Layer, **Clear Grass**,
**Clear Trees**, with an **Edge Falloff**. It edits the Terrain Data (one Undo step), so run it again after
reshaping the path. Every path in the network is stamped. Code: `VirtualVenues.WorldCreator.Editor.PathwayTerrain.Apply(path, options)`.

Terrain poking through the strip in the distance means the Terrain's **Pixel Error** is high (it is drawn simplified
far away). The Inspector offers **Set Terrain Pixel Error To 1**; Apply To Terrain also fixes it under the path.

## Bake

**Bake** saves the props as normal children under `Pathway Props (Baked)` (prefab links kept) and stops
rebuilding them: faster world load, and you can hand-edit them, but they no longer follow the spline. The ground
strip stays live. **Unbake** deletes them (hand edits are lost) and goes back to automatic props.
Code: `VirtualVenues.WorldCreator.Editor.PathwayEditor.Bake(path)` / `Unbake(path)`.

## Traps

- Moving the terrain or floor under a path doesn't re-snap it: press **Rebuild**.
- Generated props are never saved (`HideFlags.DontSave`); don't parent your own objects under `Pathway Props`.
- Paths in different Pathway objects don't see each other: put paths that meet in one Pathway.
- The web World Editor has the same component ("Pathway" in Add Component): **Edit Path** to click points onto
  the ground, drag them, branch from a middle point, drop a point onto another path's point to join them.
  Props are dropped in from the asset panel. Bake, Path Kits and the Unity Terrain stamp are Unity-only.
