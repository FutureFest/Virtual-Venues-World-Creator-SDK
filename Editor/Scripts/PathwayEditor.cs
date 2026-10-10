using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Splines;

namespace VirtualVenues.WorldCreator.Editor
{
    /// <summary>
    /// Pathway inspector: edit the path shape, reshuffle, bake / unbake the props, and load or save a path kit.
    /// </summary>
    [CustomEditor(typeof(Pathway))]
    public class PathwayEditor : UnityEditor.Editor
    {
        private PathwayPreset _kit = null;

        // Terrain stamp settings, kept for the editor session.
        private static bool _showTerrain = false;
        private static PathwayTerrain.Options _terrain = new PathwayTerrain.Options
        {
            Flatten = true, Paint = true, ClearGrass = true, ClearTrees = true, Falloff = 1.5f,
        };

        public override void OnInspectorGUI()
        {
            var path = (Pathway)target;
            ShapeButton();
            EditorGUILayout.Space();
            DrawDefaultInspector();
            EditorGUILayout.Space();
            Actions(path);
            EditorGUILayout.Space();
            Kits(path);
            EditorGUILayout.Space();
            TerrainStamp(path);
            TerrainDetailWarning(path);
        }

        // Unity draws far-off terrain simplified, up to Pixel Error pixels off, which can poke through a path strip
        // that sits a few centimetres above the real ground.
        private static void TerrainDetailWarning(Pathway path)
        {
            if (!path.ShowGround || !path.SnapToGround) { return; }
            var rough = new List<Terrain>();
            foreach (Terrain terrain in Terrain.activeTerrains) { if (terrain.heightmapPixelError > 2f) { rough.Add(terrain); } }
            if (rough.Count == 0) { return; }
            EditorGUILayout.HelpBox("Terrain shows through the path strip in the distance? The Terrain's Pixel Error is high, so it is drawn " +
                "simplified far away. Set it to 1 (a bit more terrain detail), or press Apply To Terrain to flatten the ground under the path.",
                MessageType.Warning);
            if (GUILayout.Button("Set Terrain Pixel Error To 1"))
            {
                foreach (Terrain terrain in rough)
                {
                    Undo.RecordObject(terrain, "Terrain Pixel Error");
                    terrain.heightmapPixelError = 1f;
                }
            }
        }

        private static void TerrainStamp(Pathway path)
        {
            _showTerrain = EditorGUILayout.Foldout(_showTerrain, "Unity Terrain", true);
            if (!_showTerrain) { return; }
            EditorGUILayout.HelpBox("Stamps the path into the Unity Terrains under it. This changes the Terrain Data itself " +
                "(Undo works), so run it again after reshaping the path.", MessageType.Info);
            _terrain.Flatten = EditorGUILayout.Toggle(new GUIContent("Flatten", "Level the ground to the path's height."), _terrain.Flatten);
            _terrain.Paint = EditorGUILayout.Toggle(new GUIContent("Paint", "Paint the Terrain Layer below along the path."), _terrain.Paint);
            using (new EditorGUI.DisabledScope(!_terrain.Paint))
            {
                _terrain.Layer = (TerrainLayer)EditorGUILayout.ObjectField(new GUIContent("Terrain Layer", "e.g. a gravel or dirt Terrain Layer."),
                    _terrain.Layer, typeof(TerrainLayer), false);
            }
            _terrain.ClearGrass = EditorGUILayout.Toggle(new GUIContent("Clear Grass", "Remove terrain grass and detail meshes on the path."), _terrain.ClearGrass);
            _terrain.ClearTrees = EditorGUILayout.Toggle(new GUIContent("Clear Trees", "Remove terrain trees on the path."), _terrain.ClearTrees);
            _terrain.Falloff = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Edge Falloff", "Metres past each edge over which flatten and paint fade out."), _terrain.Falloff));
            if (GUILayout.Button("Apply To Terrain"))
            {
                int count = PathwayTerrain.Apply(path, _terrain);
                if (count == 0) { EditorUtility.DisplayDialog("Apply To Terrain", "No active Unity Terrain is under this path.", "OK"); }
            }
        }

        private void OnSceneGUI()
        {
            SplineTrackEditor.DrawKnotMarkers(((Pathway)target).GetComponent<SplineContainer>());
        }

        private static void ShapeButton()
        {
            bool editing = ToolManager.activeContextType == typeof(SplineToolContext);
            if (GUILayout.Button(new GUIContent(editing ? "Stop Editing Path Shape" : "Edit Path Shape",
                "Show move / bend handles on the path points in the Scene view. Or click a numbered point in the Scene view.")))
            {
                if (editing) { ToolManager.SetActiveContext<GameObjectToolContext>(); }
                else
                {
                    ToolManager.SetActiveContext<SplineToolContext>();
                    Tools.current = Tool.Move;
                }
                SceneView.RepaintAll();
            }
            EditorGUILayout.HelpBox(editing
                ? "Click a path point, then drag it (W), or drag its small tangent dots to bend the path. " +
                  "Draw Splines (Scene view tool bar) adds points. The ground and props follow as you drag."
                : "Click a numbered path point in the Scene view (or this button) to reshape the path.",
                MessageType.Info);
        }

        private static void Actions(Pathway path)
        {
            if (path.IsBaked)
            {
                EditorGUILayout.HelpBox("Baked: the props are normal objects under \"" + Pathway.BakedRootName + "\". Edit or delete them " +
                    "by hand. Reshaping the path moves the ground strip only. Unbake to go back to automatic props (hand edits are lost).",
                    MessageType.Warning);
                if (GUILayout.Button(new GUIContent("Unbake", "Delete the baked props and let the path place them again."))) { Unbake(path); }
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Rebuild", "Place everything again, e.g. after moving the terrain or floor under the path.")))
                {
                    path.Rebuild();
                }
                if (GUILayout.Button(new GUIContent("Reshuffle", "Pick a new random seed: different props, nudges, turns and sizes.")))
                {
                    Undo.RecordObject(path, "Reshuffle Pathway");
                    var so = new SerializedObject(path);
                    so.FindProperty("_seed").intValue = Random.Range(1, int.MaxValue);
                    so.ApplyModifiedProperties();
                    path.Rebuild();
                }
            }
            if (GUILayout.Button(new GUIContent("Bake", "Turn the props into normal saved objects. They stop rebuilding when the world " +
                "loads (faster load, and you can hand-edit them), but no longer follow the path.")))
            {
                Bake(path);
            }
            JunctionHelp(path);
        }

        private static void JunctionHelp(Pathway path)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Junctions: every spline on this object is a path. Where path points are linked, the paths meet: " +
                "the strips join in a patch and fences stop at the joining path's edge.\n" +
                "• Branch: Draw Splines (Scene view tool bar), start the click on an existing point.\n" +
                "• Or put two points on top of each other and press Link Touching Points.\n" +
                $"Junctions now: {path.JunctionPositions().Count}", MessageType.None);
            if (GUILayout.Button(new GUIContent("Link Touching Points", "Join path points from different splines that are within half a metre of each other.")))
            {
                int linked = LinkTouchingPoints(path, 0.5f);
                if (linked == 0) { EditorUtility.DisplayDialog("Link Touching Points", "No unlinked points of different splines are within half a metre of each other.", "OK"); }
            }
        }

        /// <summary>Links knots of different splines closer than <paramref name="distance"/> metres and moves them together. Returns how many links it made.</summary>
        public static int LinkTouchingPoints(Pathway path, float distance)
        {
            SplineContainer container = path.GetComponent<SplineContainer>();
            Undo.RecordObject(container, "Link Path Points");
            KnotLinkCollection links = container.KnotLinkCollection;
            int made = 0;
            for (int s1 = 0; s1 < container.Splines.Count; s1++)
            {
                for (int k1 = 0; k1 < container.Splines[s1].Count; k1++)
                {
                    for (int s2 = s1 + 1; s2 < container.Splines.Count; s2++)
                    {
                        for (int k2 = 0; k2 < container.Splines[s2].Count; k2++)
                        {
                            var a = new SplineKnotIndex(s1, k1);
                            var b = new SplineKnotIndex(s2, k2);
                            BezierKnot knotA = container.Splines[s1][k1];
                            BezierKnot knotB = container.Splines[s2][k2];
                            if (Vector3.Distance(knotA.Position, knotB.Position) > distance) { continue; }
                            bool already = false;
                            foreach (SplineKnotIndex other in links.GetKnotLinks(a)) { already |= other.Equals(b); }
                            if (already) { continue; }
                            knotB.Position = knotA.Position;
                            container.Splines[s2][k2] = knotB;
                            links.Link(a, b);
                            made++;
                        }
                    }
                }
            }
            if (made > 0)
            {
                EditorUtility.SetDirty(container);
                path.Rebuild();
            }
            return made;
        }

        private void Kits(Pathway path)
        {
            EditorGUILayout.LabelField("Path Kit", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _kit = (PathwayPreset)EditorGUILayout.ObjectField(_kit, typeof(PathwayPreset), false);
                using (new EditorGUI.DisabledScope(_kit == null))
                {
                    if (GUILayout.Button(new GUIContent("Load", "Replace this path's ground and rules with the kit's. Keeps the shape and seed."), GUILayout.Width(60f)))
                    {
                        Undo.RecordObject(path, "Load Path Kit");
                        path.ApplyPreset(_kit);
                        EditorUtility.SetDirty(path);
                    }
                }
            }
            if (GUILayout.Button(new GUIContent("Save As Kit...", "Save this path's ground and rules as a kit to reuse on other paths.")))
            {
                SaveKit(path);
            }
        }

        private static void SaveKit(Pathway path)
        {
            string file = EditorUtility.SaveFilePanelInProject("Save Path Kit", path.name + " Kit", "asset", "Where to save the path kit.");
            if (string.IsNullOrEmpty(file)) { return; }
            var kit = CreateInstance<PathwayPreset>();
            kit.Set(path.ShowGround, path.Width, path.TextureLength, path.GroundHeight, path.GroundMaterial, path.Rules);
            AssetDatabase.CreateAsset(kit, file);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(kit);
        }

        /// <summary>Replaces the generated props with saved copies (prefab links kept) and stops rebuilding them.</summary>
        public static void Bake(Pathway path)
        {
            List<PathwayPlacement> placements = path.ComputePlacements();
            Undo.RecordObject(path, "Bake Pathway");
            path.SetBaked(true);
            var root = new GameObject(Pathway.BakedRootName);
            Undo.RegisterCreatedObjectUndo(root, "Bake Pathway");
            root.transform.SetParent(path.transform, false);
            foreach (PathwayPlacement p in placements)
            {
                GameObject instance = PrefabUtility.IsPartOfPrefabAsset(p.Prefab)
                    ? (GameObject)PrefabUtility.InstantiatePrefab(p.Prefab, root.transform)
                    : Object.Instantiate(p.Prefab, root.transform);
                instance.transform.SetLocalPositionAndRotation(p.Position, p.Rotation);
                instance.transform.localScale = p.Scale;
            }
            EditorUtility.SetDirty(path);
        }

        /// <summary>Deletes the baked props and goes back to automatic ones.</summary>
        public static void Unbake(Pathway path)
        {
            Undo.RecordObject(path, "Unbake Pathway");
            Transform baked;
            while ((baked = path.transform.Find(Pathway.BakedRootName)) != null) { Undo.DestroyObjectImmediate(baked.gameObject); }
            path.SetBaked(false);
            EditorUtility.SetDirty(path);
        }

        [MenuItem("GameObject/VirtualVenues/New Pathway", isValidateFunction: false, priority: 0)]
        private static void CreatePathway(MenuCommand command)
        {
            var go = new GameObject("Pathway", typeof(SplineContainer));
            Spline spline = go.GetComponent<SplineContainer>().Spline;
            spline.Clear();
            spline.Add(new BezierKnot(new Vector3(0f, 0f, 0f)), TangentMode.AutoSmooth);
            spline.Add(new BezierKnot(new Vector3(0f, 0f, 10f)), TangentMode.AutoSmooth);
            spline.Add(new BezierKnot(new Vector3(6f, 0f, 18f)), TangentMode.AutoSmooth);
            go.AddComponent<Pathway>();
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "New Pathway");
            Selection.activeGameObject = go;
        }
    }
}
