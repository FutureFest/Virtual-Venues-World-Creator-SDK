using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VirtualVenues.WorldCreator;
using Object = UnityEngine.Object;

namespace VirtualVenues.Editor
{
    /// <summary>Explicit, build-only static-visual export; normal world publishing is unchanged.</summary>
    public static class StreamingWorldExporter
    {
        [Serializable] public sealed class Settings
        {
            public BuildTarget target = BuildTarget.WebGL;
            public string sourceScene;
            public string outputDirectory;
            public string legacyWorldUrl;
            public bool includeInactiveStaticVisuals;
            public bool detachStreamedCollisionMeshes;
            public string[] visualRootPaths = Array.Empty<string>();
            public string[] excludedRendererNamePrefixes = Array.Empty<string>();
            public StreamingZone[] zones = Array.Empty<StreamingZone>();
        }
        [Serializable] public sealed class Report
        {
            public string sourceScene;
            public string sourceSha256;
            public int baseRenderers;
            public int movedRenderers;
            public int retainedColliders;
            public int retainedBehaviours;
            public int extractedMeshes;
            public int detachedCollisionMeshes;
            public AssetOwnership[] meshOwnership;
            public string[] baseDependencies;
            public string[] warnings;
            public ZoneReport[] zones;
            public WorldStreamingManifest manifest;
        }
        [Serializable] public sealed class ZoneReport
        {
            public string id;
            public int renderers;
            public int vertices;
        }

        [Serializable] public sealed class AssetOwnership
        {
            public string asset;
            public string[] scenes;
            public long streamBytes;
            public bool basePinned;
        }

        [MenuItem("VirtualVenues/Worlds/Export streaming static visuals (pilot)")]
        private static void ExportFromSettings()
        {
            var path = EditorUtility.OpenFilePanel("Streaming world settings JSON", "", "json");
            if (string.IsNullOrEmpty(path)) { return; }
            Export(JsonUtility.FromJson<Settings>(File.ReadAllText(path)));
        }

        public static string ExportJson(string settingsPath) => JsonUtility.ToJson(
            Export(JsonUtility.FromJson<Settings>(File.ReadAllText(settingsPath))), true);

        public static Report Export(Settings settings)
        {
            if (settings == null || (settings.target != BuildTarget.WebGL && settings.target != BuildTarget.iOS) || (settings.target == BuildTarget.iOS && EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS))
                throw new ArgumentException("Select the matching supported streaming export target before exporting.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Export requires a ready editor outside Play mode.");
            if (settings == null || string.IsNullOrEmpty(settings.sourceScene) ||
                !settings.sourceScene.StartsWith("Assets/", StringComparison.Ordinal) ||
                !settings.sourceScene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(settings.sourceScene) || settings.visualRootPaths == null || settings.visualRootPaths.Length == 0 ||
                settings.zones == null || settings.zones.Length == 0 || settings.excludedRendererNamePrefixes == null)
                throw new ArgumentException("Provide a saved scene, visual roots, disjoint zones and an output directory.");
            if (string.IsNullOrEmpty(settings.outputDirectory)) throw new ArgumentException("Missing output directory.");
            var output = Path.GetFullPath(settings.outputDirectory);
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new ArgumentException("Use a new, empty output directory; an existing release must never be overwritten.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes before exporting.");
            if (LightmapSettings.lightmaps.Length != 0)
                throw new InvalidOperationException("Pilot export requires a scene without baked lightmaps.");

            var sourceHash = HashFile(settings.sourceScene);
            var originalActive = SceneManager.GetActiveScene();
            string temp = "Assets/__VVStreamingExport_" + Guid.NewGuid().ToString("N");
            var scenes = new List<Scene>();
            var manifest = new WorldStreamingManifest { baseBundle = "world_base", legacyWorldUrl = settings.legacyWorldUrl,
                zones = settings.zones, mobileZoneBudget = 1, desktopZoneBudget = 3 };
            // Validate authoring bounds and IDs before creating any scenes/assets.
            manifest.bundles = new[] { DummyBundle("world_base", temp + "/base.unity") }.Concat(
                settings.zones.Select(z => z == null ? null : DummyBundle(z.bundle, temp + "/" + z.id + ".unity"))).ToArray();
            manifest.Validate();
            var report = new Report { sourceScene = settings.sourceScene, sourceSha256 = sourceHash };
            try
            {
                Directory.CreateDirectory(temp + "/meshes");
                Directory.CreateDirectory(temp + "/materials");
                File.Copy(settings.sourceScene, temp + "/base.unity");
                AssetDatabase.Refresh();
                var baseScene = EditorSceneManager.OpenScene(temp + "/base.unity", OpenSceneMode.Additive);
                scenes.Add(baseScene);
                SceneManager.SetActiveScene(baseScene);
                foreach (var root in baseScene.GetRootGameObjects())
                    if (PrefabUtility.IsPartOfPrefabInstance(root))
                        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                // Nested instances whose outer scene root is not itself a prefab.
                foreach (var t in All<Transform>(baseScene).ToArray())
                    if (PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject))
                        PrefabUtility.UnpackPrefabInstance(t.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                var protectedComponents = new HashSet<Object>();
                var controlledTransforms = new HashSet<Transform>();
                foreach (var c in All<Component>(baseScene).Where(c => c != null))
                {
                    var p = new SerializedObject(c).GetIterator();
                    while (p.Next(true))
                    {
                        if (p.propertyType == SerializedPropertyType.ObjectReference &&
                            (p.objectReferenceValue is Renderer || p.objectReferenceValue is MeshFilter))
                            protectedComponents.Add(p.objectReferenceValue);
                        // A sibling controller/UnityEvent can move or enable a visual's parent
                        // without referencing its renderer. Keep that entire controlled branch.
                        if (p.propertyType != SerializedPropertyType.ObjectReference || p.propertyPath == "m_GameObject" ||
                            !(c is MonoBehaviour || c is Animator || c is Animation)) { continue; }
                        var target = p.objectReferenceValue as Transform;
                        if (p.objectReferenceValue is GameObject go) target = go.transform;
                        if (target != null) controlledTransforms.Add(target);
                    }
                }
                var colliderSignature = Colliders(baseScene);
                var behaviourSignature = Behaviours(baseScene);
                report.retainedColliders = All<Collider>(baseScene).Count();
                report.retainedBehaviours = All<MonoBehaviour>(baseScene).Count();
                foreach (var c in All<Component>(baseScene).Where(c => c != null && c.GetType().Name == "SpawnPoint"))
                    if (settings.zones.Any(z => z.blockUntilReady && z.Bounds.Contains(c.transform.position)))
                        throw new InvalidOperationException("A blocked streaming room contains a spawn point: " + HierarchyPath(c.transform));

                var zoneScenes = new Dictionary<string, Scene>();
                foreach (var z in settings.zones)
                {
                    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    scenes.Add(scene); // Track immediately, including failures during scene saving.
                    EditorSceneManager.SaveScene(scene, temp + "/" + z.id + ".unity");
                    zoneScenes.Add(z.id, scene);
                }
                var transforms = settings.zones.ToDictionary(z => z.id, z => new Dictionary<Transform, Transform>());
                var rows = settings.zones.ToDictionary(z => z.id, z => new ZoneReport { id = z.id });
                var movedMeshes = new HashSet<UnityEngine.Mesh>();
                foreach (var renderer in All<MeshRenderer>(baseScene).ToArray())
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    var path = HierarchyPath(renderer.transform);
                    if ((!settings.includeInactiveStaticVisuals && (!renderer.enabled || !renderer.gameObject.activeInHierarchy)) || filter == null || filter.sharedMesh == null ||
                        protectedComponents.Contains(renderer) || protectedComponents.Contains(filter) ||
                        renderer.probeAnchor != null || renderer.lightProbeProxyVolumeOverride != null || renderer.additionalVertexStreams != null ||
                        !settings.visualRootPaths.Any(root => path == root || path.StartsWith(root + "/", StringComparison.Ordinal)) ||
                        settings.excludedRendererNamePrefixes.Any(prefix => renderer.name.StartsWith(prefix, StringComparison.Ordinal)) ||
                        HasDynamicAncestor(renderer.transform, controlledTransforms)) { continue; }
                    // Cross-boundary visual objects stay in the base; no triangle cutting or material changes.
                    var bounds = settings.includeInactiveStaticVisuals ? StaticMeshBounds(renderer.transform, filter.sharedMesh) : renderer.bounds;
                    var zones = settings.zones.Where(z => z.Bounds.Contains(bounds.min) && z.Bounds.Contains(bounds.max)).ToArray();
                    if (zones.Length != 1) { continue; }
                    var zone = zones[0];
                    var copy = CopyTransform(renderer.transform, zoneScenes[zone.id], transforms[zone.id]);
                    var newFilter = copy.gameObject.AddComponent<MeshFilter>(); EditorUtility.CopySerialized(filter, newFilter);
                    var newRenderer = copy.gameObject.AddComponent<MeshRenderer>(); EditorUtility.CopySerialized(renderer, newRenderer);
                    if (!SameMatrix(renderer.localToWorldMatrix, newRenderer.localToWorldMatrix))
                        throw new InvalidOperationException("Visual transform changed: " + path);
                    rows[zone.id].renderers++; rows[zone.id].vertices += filter.sharedMesh.vertexCount;
                    movedMeshes.Add(filter.sharedMesh);
                    Object.DestroyImmediate(renderer); Object.DestroyImmediate(filter);
                }
                if (rows.Values.Any(z => z.renderers == 0)) throw new InvalidOperationException("Every configured zone must contain at least one eligible visual. Check bounds/roots.");

                // Extract imported mesh sub-assets so a room cannot drag an entire FBX into its bundle.
                // Optionally detach streamed visuals from base collision using exact position/index copies.
                // Collider geometry is independently checked; default pilot ownership is preserved.
                var meshes = new Dictionary<UnityEngine.Mesh, UnityEngine.Mesh>();
                var materials = new Dictionary<Material, Material>();
                var collisionMeshes = new Dictionary<UnityEngine.Mesh, UnityEngine.Mesh>();
                var collisionGeometry = CollisionGeometry(baseScene);
                foreach (var scene in scenes)
                {
                    foreach (var filter in All<MeshFilter>(scene)) filter.sharedMesh = ExtractMesh(filter.sharedMesh, temp, meshes);
                    foreach (var collider in All<MeshCollider>(scene))
                        collider.sharedMesh = settings.detachStreamedCollisionMeshes && movedMeshes.Contains(collider.sharedMesh)
                            ? ExtractCollisionMesh(collider.sharedMesh, temp, collisionMeshes) : ExtractMesh(collider.sharedMesh, temp, meshes);
                    foreach (var renderer in All<Renderer>(scene))
                        renderer.sharedMaterials = renderer.sharedMaterials.Select(m => ExtractMaterial(m, temp, materials)).ToArray();
                }
                if (colliderSignature != Colliders(baseScene) || behaviourSignature != Behaviours(baseScene) ||
                    collisionGeometry != CollisionGeometry(baseScene))
                    throw new InvalidOperationException("Collision or gameplay component signatures changed.");
                EditorSceneManager.SaveScene(baseScene, temp + "/base.unity");
                foreach (var z in settings.zones) EditorSceneManager.SaveScene(zoneScenes[z.id], temp + "/" + z.id + ".unity");
                AssetDatabase.SaveAssets();
                report.extractedMeshes = meshes.Count;
                report.detachedCollisionMeshes = collisionMeshes.Count;
                report.meshOwnership = MeshOwnership(scenes, baseScene);
                report.baseRenderers = All<Renderer>(baseScene).Count();
                report.movedRenderers = rows.Values.Sum(z => z.renderers);
                report.zones = settings.zones.Select(z => rows[z.id]).ToArray();

                var builds = new List<AssetBundleBuild> { new AssetBundleBuild { assetBundleName = "world_base", assetNames = new[] { baseScene.path } } };
                builds.AddRange(settings.zones.Select(z => new AssetBundleBuild { assetBundleName = z.bundle, assetNames = new[] { zoneScenes[z.id].path } }));
                var owners = new Dictionary<string, List<int>>(StringComparer.Ordinal);
                for (int i = 0; i < builds.Count; i++)
                    foreach (var dependency in AssetDatabase.GetDependencies(builds[i].assetNames, true))
                    {
                        var type = AssetDatabase.GetMainAssetTypeAtPath(dependency);
                        if (type == null || type == typeof(MonoScript) || type == typeof(SceneAsset) || AssetDatabase.IsValidFolder(dependency) ||
                            dependency.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) { continue; }
                        if (!owners.TryGetValue(dependency, out var list)) owners.Add(dependency, list = new List<int>());
                        list.Add(i);
                    }
                // Each shared group is scoped to its actual set of consumer scenes. A single global
                // shared bundle would otherwise pin room-only assets through the base scene.
                int group = 0;
                foreach (var shared in owners.Where(p => p.Value.Count > 1).GroupBy(p => string.Join("_", p.Value)).OrderBy(g => g.Key, StringComparer.Ordinal))
                    builds.Add(new AssetBundleBuild { assetBundleName = "shared_" + group++, assetNames = shared.Select(p => p.Key).OrderBy(p => p, StringComparer.Ordinal).ToArray() });
                Directory.CreateDirectory(output);
                var built = BuildPipeline.BuildAssetBundles(output, builds.ToArray(), BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, settings.target);
                if (built == null) throw new InvalidOperationException("Streaming world bundle build failed.");
                manifest.bundles = built.GetAllAssetBundles().OrderBy(n => n, StringComparer.Ordinal).Select(name =>
                {
                    var file = Path.Combine(output, name);
                    if (!BuildPipeline.GetCRCForAssetBundle(file, out var crc)) throw new InvalidOperationException("Missing bundle CRC: " + name);
                    var scene = builds.First(b => b.assetBundleName == name).assetNames.SingleOrDefault(p => p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase));
                    return new StreamingBundle { name = name, file = name, bytes = new FileInfo(file).Length, crc = crc,
                        hash = built.GetAssetBundleHash(name).ToString(), dependencies = built.GetAllDependencies(name), scene = scene };
                }).ToArray();
                manifest.target = settings.target == BuildTarget.iOS ? "iOS" : "WebGL";
                if (settings.target != BuildTarget.WebGL && settings.target != BuildTarget.iOS)
                    throw new ArgumentException("Unsupported streaming export target.");
                manifest.Validate(manifest.target); report.manifest = manifest;
                report.baseDependencies = manifest.FindBundle(manifest.baseBundle).dependencies;
                report.warnings = new[] { "Static visuals only: collisions, gameplay, screens and animated objects remain in the base.",
                    "Bundle bytes and vertex counts do not measure browser/GPU memory. Base/shared dependencies remain resident.",
                    "Generated entrance colliders and the loading overlay are pilot UX; validate every route before production.",
                    "The normal publisher does not upload/register this multi-file format yet. Use a compatible UPC and client-only override." };
                if (sourceHash != HashFile(settings.sourceScene)) throw new InvalidOperationException("Authored source scene changed during export.");
                File.WriteAllText(Path.Combine(output, "world.streaming.json"), JsonUtility.ToJson(manifest, true));
                File.WriteAllText(Path.Combine(output, "export-report.json"), JsonUtility.ToJson(report, true));
                Debug.Log("Streaming export: " + report.movedRenderers + " visual renderers in " + settings.zones.Length + " zones. " + output);
                return report;
            }
            finally
            {
                if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
                foreach (var scene in scenes.AsEnumerable().Reverse()) if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.DeleteAsset(temp);
            }
        }

        private static StreamingBundle DummyBundle(string name, string scene) => new StreamingBundle { name = name, file = name, scene = scene, bytes = 1, hash = new string('0', 32) };
        private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true));
        private static bool HasDynamicAncestor(Transform transform, ISet<Transform> controlled)
        {
            for (var t = transform; t != null; t = t.parent)
                if (controlled.Contains(t) || t.GetComponents<MonoBehaviour>().Length != 0 || t.GetComponent<Animator>() != null || t.GetComponent<Animation>() != null) return true;
            return false;
        }
        private static string HierarchyPath(Transform t) => t.parent == null ? t.name : HierarchyPath(t.parent) + "/" + t.name;
        private static Transform CopyTransform(Transform source, Scene scene, Dictionary<Transform, Transform> copies)
        {
            if (copies.TryGetValue(source, out var copy)) return copy;
            var go = new GameObject(source.name) { layer = source.gameObject.layer }; SceneManager.MoveGameObjectToScene(go, scene);
            copy = go.transform;
            if (source.parent != null) copy.SetParent(CopyTransform(source.parent, scene, copies), false);
            copy.localPosition = source.localPosition; copy.localRotation = source.localRotation; copy.localScale = source.localScale;
            go.SetActive(source.gameObject.activeSelf); copies.Add(source, copy); return copy;
        }
        private static bool SameMatrix(Matrix4x4 a, Matrix4x4 b)
        { for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > 0.0001f) return false; return true; }
        private static UnityEngine.Mesh ExtractMesh(UnityEngine.Mesh source, string temp, Dictionary<UnityEngine.Mesh, UnityEngine.Mesh> copies)
        {
            if (source == null) return null;
            if (copies.TryGetValue(source, out var copy)) return copy;
            copy = Object.Instantiate(source);
            // Mesh snapshots work for non-readable imported meshes in the editor. Verify all vertex
            // streams and indices; collider geometry and renderer attributes must be byte-identical.
            using (var a = UnityEngine.Mesh.AcquireReadOnlyMeshData(source))
            using (var b = UnityEngine.Mesh.AcquireReadOnlyMeshData(copy))
            {
                if (a[0].vertexCount != b[0].vertexCount || a[0].vertexBufferCount != b[0].vertexBufferCount ||
                    !a[0].GetIndexData<byte>().SequenceEqual(b[0].GetIndexData<byte>())) throw new InvalidOperationException("Mesh extraction changed indices.");
                for (int i = 0; i < a[0].vertexBufferCount; i++)
                    if (!a[0].GetVertexData<byte>(i).SequenceEqual(b[0].GetVertexData<byte>(i))) throw new InvalidOperationException("Mesh extraction changed vertices.");
            }
            AssetDatabase.CreateAsset(copy, temp + "/meshes/mesh_" + copies.Count + ".asset"); copies.Add(source, copy); return copy;
        }
        // Disabled renderers can report empty bounds. Mesh bounds remain valid without enabling them.
        private static Bounds StaticMeshBounds(Transform transform, UnityEngine.Mesh mesh)
        {
            var local = mesh.bounds;
            var world = new Bounds(transform.TransformPoint(local.min), Vector3.zero);
            for (int i = 0; i < 8; i++)
                world.Encapsulate(transform.TransformPoint(new Vector3(
                    (i & 1) == 0 ? local.min.x : local.max.x,
                    (i & 2) == 0 ? local.min.y : local.max.y,
                    (i & 4) == 0 ? local.min.z : local.max.z)));
            // Conservative rounding margin for rotated or negatively scaled mesh bounds.
            world.Expand(Mathf.Max(0.0001f, world.extents.magnitude * 0.000001f));
            return world;
        }
        private static UnityEngine.Mesh ExtractCollisionMesh(UnityEngine.Mesh source, string temp,
            Dictionary<UnityEngine.Mesh, UnityEngine.Mesh> copies)
        {
            if (source == null) { return null; }
            if (copies.TryGetValue(source, out var existing)) { return existing; }
            var copy = new UnityEngine.Mesh { name = source.name + "_Collision", indexFormat = source.indexFormat };
            try
            {
                using (var data = UnityEngine.Mesh.AcquireReadOnlyMeshData(source))
                using (var vertices = new NativeArray<Vector3>(source.vertexCount, Allocator.Temp))
                {
                    data[0].GetVertices(vertices);
                    copy.vertices = vertices.ToArray();
                    copy.subMeshCount = source.subMeshCount;
                    for (int i = 0; i < source.subMeshCount; i++)
                    {
                        var sub = data[0].GetSubMesh(i);
                        using (var indices = new NativeArray<int>(sub.indexCount, Allocator.Temp))
                        {
                            data[0].GetIndices(indices, i, true);
                            copy.SetIndices(indices.ToArray(), sub.topology, i, false);
                        }
                    }
                    for (int channel = 0; channel < 2; channel++)
                        if (source.HasVertexAttribute((UnityEngine.Rendering.VertexAttribute)((int)UnityEngine.Rendering.VertexAttribute.TexCoord0 + channel)))
                            using (var uv = new NativeArray<Vector2>(source.vertexCount, Allocator.Temp))
                            { data[0].GetUVs(channel, uv); copy.SetUVs(channel, uv); }
                    copy.bounds = source.bounds;
                }
                if (MeshGeometry(source) != MeshGeometry(copy))
                    throw new InvalidOperationException("Collision extraction changed positions or topology.");
                AssetDatabase.CreateAsset(copy, temp + "/meshes/collision_" + copies.Count + ".asset");
                copies.Add(source, copy);
                return copy;
            }
            catch { Object.DestroyImmediate(copy); throw; }
        }
        private static string MeshGeometry(UnityEngine.Mesh mesh)
        {
            if (mesh == null) { return "null"; }
            using (var bytes = new MemoryStream())
            using (var writer = new BinaryWriter(bytes))
            using (var data = UnityEngine.Mesh.AcquireReadOnlyMeshData(mesh))
            using (var vertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp))
            {
                data[0].GetVertices(vertices); writer.Write(mesh.vertexCount); writer.Write(mesh.subMeshCount);
                foreach (var v in vertices) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    var sub = data[0].GetSubMesh(i); writer.Write((int)sub.topology); writer.Write(sub.indexCount);
                    using (var indices = new NativeArray<int>(sub.indexCount, Allocator.Temp))
                    { data[0].GetIndices(indices, i, true); foreach (var index in indices) writer.Write(index); }
                }
                // Physics raycast texture coordinates must survive collision detachment too.
                for (int channel = 0; channel < 2; channel++)
                {
                    bool present = mesh.HasVertexAttribute((UnityEngine.Rendering.VertexAttribute)((int)UnityEngine.Rendering.VertexAttribute.TexCoord0 + channel));
                    writer.Write(present);
                    if (present) using (var uv = new NativeArray<Vector2>(mesh.vertexCount, Allocator.Temp))
                    { data[0].GetUVs(channel, uv); foreach (var value in uv) { writer.Write(value.x); writer.Write(value.y); } }
                }
                writer.Flush();
                using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes.ToArray()));
            }
        }
        private static string CollisionGeometry(Scene scene) => string.Join("\n", All<MeshCollider>(scene).Select(c =>
            HierarchyPath(c.transform) + "|" + MeshGeometry(c.sharedMesh)));
        private static AssetOwnership[] MeshOwnership(IEnumerable<Scene> scenes, Scene baseScene)
        {
            var owners = new Dictionary<UnityEngine.Mesh, HashSet<string>>();
            foreach (var scene in scenes)
            {
                var meshes = All<MeshFilter>(scene).Select(f => f.sharedMesh)
                    .Concat(All<MeshCollider>(scene).Select(c => c.sharedMesh))
                    .Concat(All<SkinnedMeshRenderer>(scene).Select(r => r.sharedMesh)).Where(m => m != null).Distinct();
                foreach (var mesh in meshes)
                {
                    if (!owners.TryGetValue(mesh, out var set)) owners.Add(mesh, set = new HashSet<string>());
                    set.Add(scene.path);
                }
            }
            return owners.Select(pair =>
            {
                long bytes = 0;
                using (var data = UnityEngine.Mesh.AcquireReadOnlyMeshData(pair.Key))
                {
                    for (int i = 0; i < data[0].vertexBufferCount; i++) bytes += data[0].GetVertexData<byte>(i).Length;
                    bytes += data[0].GetIndexData<byte>().Length;
                }
                return new AssetOwnership { asset = AssetDatabase.GetAssetPath(pair.Key),
                    scenes = pair.Value.OrderBy(p => p, StringComparer.Ordinal).ToArray(), streamBytes = bytes,
                    basePinned = pair.Value.Contains(baseScene.path) };
            }).OrderByDescending(row => row.streamBytes).ToArray();
        }
        private static Material ExtractMaterial(Material source, string temp, Dictionary<Material, Material> copies)
        {
            if (source == null || !AssetDatabase.IsSubAsset(source)) return source;
            if (!copies.TryGetValue(source, out var copy))
            { copy = Object.Instantiate(source); AssetDatabase.CreateAsset(copy, temp + "/materials/material_" + copies.Count + ".mat"); copies.Add(source, copy); }
            return copy;
        }
        private static string Behaviours(Scene scene) => string.Join("\n", All<MonoBehaviour>(scene).Select(c => c == null ? "MISSING" :
            HierarchyPath(c.transform) + "|" + c.GetType().FullName + "|" + EditorJsonUtility.ToJson(c)));
        private static string Colliders(Scene scene) => string.Join("\n", All<Collider>(scene).Select(c => HierarchyPath(c.transform) + "|" +
            c.transform.localToWorldMatrix + "|" + Regex.Replace(EditorJsonUtility.ToJson(c), "\"m_Mesh\":\\s*\\{[^}]*\\}", "\"m_Mesh\":null")));
        private static string HashFile(string path)
        { using (var s = File.OpenRead(path)) using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
    }
}
