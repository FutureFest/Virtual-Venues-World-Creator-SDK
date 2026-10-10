using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace VirtualVenues.WorldCreator.Editor
{
    /// <summary>
    /// Stamps a <see cref="Pathway"/> into the Unity Terrains under it: flattens the ground to the path, paints a
    /// Terrain Layer along it, and clears grass and trees off it. Changes the Terrain Data itself (one Undo step),
    /// so it runs from a button, not live.
    /// </summary>
    public static class PathwayTerrain
    {
        public struct Options
        {
            public bool Flatten;
            public bool Paint;
            public TerrainLayer Layer;
            public bool ClearGrass;
            public bool ClearTrees;
            /// <summary>Metres past each edge of the path over which the stamp fades out.</summary>
            public float Falloff;
        }

        /// <summary>Applies the path to every active Terrain it crosses. Returns how many terrains changed.</summary>
        public static int Apply(Pathway path, Options options)
        {
            // One centre line per spline: every path in the network gets stamped.
            var line = new List<List<Vector3>>();
            SplineContainer container = path.GetComponent<SplineContainer>();
            if (container == null) { return 0; }
            foreach (Spline spline in container.Splines)
            {
                List<Vector3> one = WorldLine(path, spline);
                if (one.Count >= 2) { line.Add(one); }
            }
            if (line.Count == 0) { return 0; }
            float half = path.Width * 0.5f;
            float reach = half + Mathf.Max(0f, options.Falloff);

            Bounds pathBounds = new Bounds(line[0][0], Vector3.zero);
            foreach (List<Vector3> one in line) { foreach (Vector3 p in one) { pathBounds.Encapsulate(p); } }
            pathBounds.Expand(new Vector3(reach * 2f, 0f, reach * 2f));

            int changed = 0;
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                TerrainData data = terrain.terrainData;
                if (data == null) { continue; }
                Vector3 origin = terrain.transform.position;
                var terrainBounds = new Bounds(origin + data.size * 0.5f, data.size);
                if (pathBounds.min.x > terrainBounds.max.x || pathBounds.max.x < terrainBounds.min.x ||
                    pathBounds.min.z > terrainBounds.max.z || pathBounds.max.z < terrainBounds.min.z) { continue; }

                var undo = new List<Object> { data };
                undo.AddRange(data.alphamapTextures);
                Undo.RegisterCompleteObjectUndo(undo.ToArray(), "Apply Path To Terrain");

                if (options.Flatten) { Flatten(terrain, line, half, reach, pathBounds); }
                if (options.Paint && options.Layer != null) { Paint(terrain, options.Layer, line, half, reach, pathBounds); }
                if (options.ClearGrass) { ClearGrass(terrain, line, half, pathBounds); }
                if (options.ClearTrees) { ClearTrees(terrain, line, half); }
                EditorUtility.SetDirty(data);
                changed++;
            }
            // The strip and props snap to the new ground.
            path.Rebuild();
            return changed;
        }

        // One path's centre line in world space, about every half metre.
        private static List<Vector3> WorldLine(Pathway path, Spline spline)
        {
            var line = new List<Vector3>();
            if (spline == null || spline.Count < 2) { return line; }
            float length = spline.GetLength();
            int segments = Mathf.Max(1, Mathf.CeilToInt(length / 0.5f));
            for (int i = 0; i <= segments; i++)
            {
                line.Add(path.transform.TransformPoint((Vector3)spline.EvaluatePosition((float)i / segments)));
            }
            if (!path.SnapToGround) { return line; }

            // Snapped paths follow the ground, so flatten to the ground's own height along the middle (levelled across
            // the path, lightly smoothed along it) instead of pulling the terrain to wherever the spline floats.
            var ground = new float[line.Count];
            for (int i = 0; i < line.Count; i++) { ground[i] = GroundHeight(line[i]); }
            for (int i = 0; i < line.Count; i++)
            {
                float sum = 0f;
                int n = 0;
                for (int k = Mathf.Max(0, i - 2); k <= Mathf.Min(line.Count - 1, i + 2); k++) { sum += ground[k]; n++; }
                line[i] = new Vector3(line[i].x, sum / n, line[i].z);
            }
            return line;
        }

        private static float GroundHeight(Vector3 world)
        {
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                Vector3 local = world - terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (local.x >= 0f && local.z >= 0f && local.x <= size.x && local.z <= size.z)
                {
                    return terrain.SampleHeight(world) + terrain.transform.position.y;
                }
            }
            return world.y;
        }

        // Stamp strength at a world XZ point: 1 on any of the paths, fading to 0 at `reach`. Height = the nearest path's height there.
        public static float Weight(List<List<Vector3>> lines, Vector3 point, float half, float reach, out float height)
        {
            float best = float.MaxValue;
            height = 0f;
            var p = new Vector2(point.x, point.z);
            foreach (List<Vector3> line in lines)
            {
                for (int i = 0; i < line.Count - 1; i++)
                {
                    var a = new Vector2(line[i].x, line[i].z);
                    var b = new Vector2(line[i + 1].x, line[i + 1].z);
                    Vector2 ab = b - a;
                    float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                    float d = (a + ab * t - p).sqrMagnitude;
                    if (d >= best) { continue; }
                    best = d;
                    height = Mathf.Lerp(line[i].y, line[i + 1].y, t);
                }
            }
            float distance = Mathf.Sqrt(best);
            if (distance <= half) { return 1f; }
            if (distance >= reach) { return 0f; }
            return Mathf.SmoothStep(1f, 0f, (distance - half) / (reach - half));
        }

        // Grid cells of a `resolution`-sample map over the terrain that the path's bounds touch.
        private static RectInt Cells(Terrain terrain, Bounds bounds, int resolution, bool corners)
        {
            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            int last = corners ? resolution - 1 : resolution;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((bounds.min.x - origin.x) / size.x * last), 0, resolution - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((bounds.max.x - origin.x) / size.x * last), 0, resolution - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((bounds.min.z - origin.z) / size.z * last), 0, resolution - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((bounds.max.z - origin.z) / size.z * last), 0, resolution - 1);
            return new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
        }

        private static Vector3 CellWorld(Terrain terrain, int x, int z, int resolution, bool corners)
        {
            Vector3 size = terrain.terrainData.size;
            float last = corners ? resolution - 1 : resolution;
            float offset = corners ? 0f : 0.5f;
            return terrain.transform.position + new Vector3((x + offset) / last * size.x, 0f, (z + offset) / last * size.z);
        }

        private static void Flatten(Terrain terrain, List<List<Vector3>> line, float half, float reach, Bounds bounds)
        {
            TerrainData data = terrain.terrainData;
            int res = data.heightmapResolution;
            RectInt r = Cells(terrain, bounds, res, true);
            float[,] heights = data.GetHeights(r.x, r.y, r.width, r.height);
            float baseY = terrain.transform.position.y;
            for (int z = 0; z < r.height; z++)
            {
                for (int x = 0; x < r.width; x++)
                {
                    float w = Weight(line, CellWorld(terrain, r.x + x, r.y + z, res, true), half, reach, out float y);
                    if (w <= 0f) { continue; }
                    float target = Mathf.Clamp01((y - baseY) / data.size.y);
                    heights[z, x] = Mathf.Lerp(heights[z, x], target, w);
                }
            }
            data.SetHeights(r.x, r.y, heights);
        }

        private static void Paint(Terrain terrain, TerrainLayer layer, List<List<Vector3>> line, float half, float reach, Bounds bounds)
        {
            TerrainData data = terrain.terrainData;
            var layers = new List<TerrainLayer>(data.terrainLayers);
            int index = layers.IndexOf(layer);
            if (index < 0)
            {
                // ponytail: on a terrain with no layers yet, ours becomes the only layer and covers it all.
                layers.Add(layer);
                data.terrainLayers = layers.ToArray();
                index = layers.Count - 1;
            }
            int res = data.alphamapResolution;
            RectInt r = Cells(terrain, bounds, res, false);
            float[,,] maps = data.GetAlphamaps(r.x, r.y, r.width, r.height);
            int count = maps.GetLength(2);
            for (int z = 0; z < r.height; z++)
            {
                for (int x = 0; x < r.width; x++)
                {
                    float w = Weight(line, CellWorld(terrain, r.x + x, r.y + z, res, false), half, reach, out _);
                    if (w <= 0f) { continue; }
                    float old = maps[z, x, index];
                    float now = Mathf.Max(old, w);
                    // Scale the other layers down so all weights still add up to 1.
                    float rest = 1f - old;
                    float keep = rest > 1e-5f ? (1f - now) / rest : 0f;
                    for (int l = 0; l < count; l++) { maps[z, x, l] = l == index ? now : maps[z, x, l] * keep; }
                }
            }
            data.SetAlphamaps(r.x, r.y, maps);
        }

        private static void ClearGrass(Terrain terrain, List<List<Vector3>> line, float half, Bounds bounds)
        {
            TerrainData data = terrain.terrainData;
            int res = data.detailResolution;
            if (res <= 0 || data.detailPrototypes.Length == 0) { return; }
            RectInt r = Cells(terrain, bounds, res, false);
            for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
            {
                int[,] details = data.GetDetailLayer(r.x, r.y, r.width, r.height, layer);
                for (int z = 0; z < r.height; z++)
                {
                    for (int x = 0; x < r.width; x++)
                    {
                        if (Weight(line, CellWorld(terrain, r.x + x, r.y + z, res, false), half, half, out _) > 0f) { details[z, x] = 0; }
                    }
                }
                data.SetDetailLayer(r.x, r.y, layer, details);
            }
        }

        private static void ClearTrees(Terrain terrain, List<List<Vector3>> line, float half)
        {
            TerrainData data = terrain.terrainData;
            var kept = new List<TreeInstance>(data.treeInstances.Length);
            foreach (TreeInstance tree in data.treeInstances)
            {
                Vector3 world = terrain.transform.position + Vector3.Scale(tree.position, data.size);
                if (Weight(line, world, half, half, out _) <= 0f) { kept.Add(tree); }
            }
            if (kept.Count != data.treeInstances.Length) { data.treeInstances = kept.ToArray(); }
        }
    }
}
