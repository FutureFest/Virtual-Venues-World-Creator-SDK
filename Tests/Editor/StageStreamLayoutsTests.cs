using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualVenues.WorldCreator;
using Screen = VirtualVenues.WorldCreator.Screen;

namespace VirtualVenues.Tests
{
    public class StageStreamLayoutsTests
    {
        private static readonly Vector2 Half = new Vector2(1f, 0.5f);
        private static readonly Vector2 TopOffset = new Vector2(0f, -0.5f);
        private const string StackedTextureGuid = "cd334ff6349015940bb12bc0c54e3f54"; // screen-calibrate-uv-test-virtualvenues-vv-combined.jpg

        // Edit mode never calls Awake on these markers; the app does, before Configure.
        private static void RunAwake(MonoBehaviour marker)
        {
            marker.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(marker, null);
        }

        private static void SetLayoutField(MonoBehaviour marker, StreamLayout streamLayout)
        {
            var so = new SerializedObject(marker);
            so.FindProperty("_streamLayout").intValue = (int)streamLayout;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetDefaultTexture(MonoBehaviour marker, Texture2D texture)
        {
            var so = new SerializedObject(marker);
            so.FindProperty("_defaultTexture").objectReferenceValue = texture;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static MonoBehaviour Marker(GameObject go, bool artist)
        {
            return artist ? (MonoBehaviour)go.GetComponentInChildren<Artist>(true) : go.GetComponentInChildren<Screen>(true);
        }

        private static Renderer Mesh(MonoBehaviour marker) => marker is Artist artist ? artist.Renderer : ((Screen)marker).Renderer;
        private static Texture2D DefaultTexture(MonoBehaviour marker) => marker is Artist artist ? artist.DefaultTexture : ((Screen)marker).DefaultTexture;
        private static StreamLayout LayoutOf(MonoBehaviour marker) => marker is Artist artist ? artist.StreamLayout : ((Screen)marker).StreamLayout;
        private static Vector2 Tiling(MonoBehaviour marker) => marker is Artist artist ? artist.TextureTiling : ((Screen)marker).TextureTiling;
        private static Vector2 Offset(MonoBehaviour marker) => marker is Artist artist ? artist.TextureOffset : ((Screen)marker).TextureOffset;

        [Test] public void CodeDefaultsAreTheWholeFrameOrTheStackedHalf()
        {
            Assert.That((int)StreamLayout.Stacked, Is.EqualTo(0), "0 must stay Stacked: published worlds have no value");
            Assert.That(System.Enum.GetValues(typeof(StreamLayout)).Length, Is.EqualTo(2));
            foreach (bool artist in new[] { false, true })
            {
                StageStreamLayouts.GetDefaultTransform(StreamLayout.Whole, artist, out Vector2 t, out Vector2 o);
                Assert.That(t, Is.EqualTo(Vector2.one)); Assert.That(o, Is.EqualTo(Vector2.zero));
                StageStreamLayouts.GetDefaultTransform(StreamLayout.Stacked, artist, out t, out o);
                Assert.That(t, Is.EqualTo(Half)); Assert.That(o, Is.EqualTo(artist ? TopOffset : Vector2.zero));
            }
        }

        [Test] public void ParseAndFormatRoundTrip()
        {
            foreach (StreamLayout streamLayout in System.Enum.GetValues(typeof(StreamLayout)))
            {
                Assert.That(StageStreamLayouts.Parse(StageStreamLayouts.Format(streamLayout)), Is.EqualTo(streamLayout));
            }
            Assert.That(StageStreamLayouts.Format(StreamLayout.Stacked), Is.EqualTo(""));
            Assert.That(StageStreamLayouts.Format(StreamLayout.Whole), Is.EqualTo("whole"));
            Assert.That(StageStreamLayouts.Parse("WHOLE"), Is.EqualTo(StreamLayout.Whole));
            foreach (string stacked in new[] { null, "", "top", "Bottom", "left", "1", "2", "stacked" })
            {
                Assert.That(StageStreamLayouts.Parse(stacked), Is.EqualTo(StreamLayout.Stacked), stacked ?? "null");
            }
        }

        [Test] public void ComponentAddedFromCodeIsStackedWithTheCodeDefaults()
        {
            var go = new GameObject("StreamLayoutNoMesh");
            try
            {
                var screen = go.AddComponent<Screen>();
                var artist = go.AddComponent<Artist>();
                RunAwake(screen); RunAwake(artist);
                Assert.That(screen.StreamLayout, Is.EqualTo(StreamLayout.Stacked));
                Assert.That(artist.StreamLayout, Is.EqualTo(StreamLayout.Stacked));
                Assert.That(screen.TextureTiling, Is.EqualTo(Half)); Assert.That(screen.TextureOffset, Is.EqualTo(Vector2.zero));
                Assert.That(artist.TextureTiling, Is.EqualTo(Half)); Assert.That(artist.TextureOffset, Is.EqualTo(TopOffset));
            }
            finally { Object.DestroyImmediate(go); }
        }

        // The SDK prefabs place new markers as Whole video with the Whole material and no Default Texture: the
        // material carries the placeholder picture.
        [TestCase("Screen", false)]
        [TestCase("Artist", true)]
        [TestCase("Stage", false)]
        [TestCase("Stage", true)]
        public void PrefabsSerializeWholeWithTheWholeMaterialAndNoDefaultTexture(string prefabName, bool artist)
        {
            MonoBehaviour marker = Marker(Resources.Load<GameObject>(prefabName), artist);
            Assert.That(marker, Is.Not.Null);
            Material whole = StageStreamLayoutAssets.GetMaterial(artist, StreamLayout.Whole);
            Material stacked = StageStreamLayoutAssets.GetMaterial(artist, StreamLayout.Stacked);
            Assert.That(whole, Is.Not.Null); Assert.That(stacked, Is.Not.Null);

            Assert.That(LayoutOf(marker), Is.EqualTo(StreamLayout.Whole));
            Assert.That(Mesh(marker).sharedMaterial, Is.SameAs(whole));
            Assert.That(DefaultTexture(marker), Is.Null, "the placeholder comes from the material");

            // Same shader as the stacked material (and the same keying for the artist), the whole picture at 1x1.
            Assert.That(whole.shader, Is.SameAs(stacked.shader));
            Assert.That(whole.shader.name, Is.Not.EqualTo("Hidden/InternalErrorShader"));
            Texture wholeTexture = whole.GetTexture("_BaseMap");
            Assert.That(wholeTexture, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(wholeTexture), Does.EndWith(artist ? "artist-calibrate-uv-test-virtualvenues.jpg" : "screen-calibrate-uv-test.jpg"));
            Assert.That(whole.GetTextureScale("_BaseMap"), Is.EqualTo(Vector2.one));
            Assert.That(whole.GetTextureOffset("_BaseMap"), Is.EqualTo(Vector2.zero));
            if (artist)
            {
                foreach (string key in new[] { "_Similarity", "_Smoothness", "_LumaThreshold", "_LumaSmooth", "_Despill", "_EdgePower" })
                {
                    Assert.That(whole.GetFloat(key), Is.EqualTo(stacked.GetFloat(key)), key);
                }
                Assert.That(whole.GetColor("_KeyColor"), Is.EqualTo(stacked.GetColor("_KeyColor")));
                Assert.That(wholeTexture.width, Is.EqualTo(wholeTexture.height * 2), "the artist half of the stacked picture");
            }
            Assert.That(stacked.GetTexture("_BaseMap"), Is.Not.Null, "the stacked placeholder picture");
            Assert.That(stacked.GetTextureScale("_BaseMap"), Is.EqualTo(Half), "the stacked materials are unchanged");
            Assert.That(stacked.GetTextureOffset("_BaseMap"), Is.EqualTo(artist ? TopOffset : Vector2.zero));
        }

        // With a mesh material, the marker reproduces that material's tiling/offset whatever its Stream Layout, a
        // creator's custom values included, and a stream layout change after Awake does not override it.
        [TestCase("Screen", StreamLayout.Whole)]
        [TestCase("Screen", StreamLayout.Stacked)]
        [TestCase("Artist", StreamLayout.Whole)]
        [TestCase("Artist", StreamLayout.Stacked)]
        public void MeshMaterialTilingIsUsedForEitherLayout(string prefabName, StreamLayout streamLayout)
        {
            LogAssert.ignoreFailingMessages = true; // renderer.material in edit mode warns about leaking a material
            var go = Object.Instantiate(Resources.Load<GameObject>(prefabName));
            MonoBehaviour marker = Marker(go, prefabName == "Artist");
            Renderer mesh = Mesh(marker);
            Material material = null;
            var tiling = new Vector2(2f, 0.25f);
            var offset = new Vector2(0.1f, 0.3f);
            try
            {
                material = new Material(mesh.sharedMaterial);
                material.SetTextureScale("_BaseMap", tiling);
                material.SetTextureOffset("_BaseMap", offset);
                mesh.sharedMaterial = material;
                SetLayoutField(marker, streamLayout);
                RunAwake(marker);

                Assert.That(Tiling(marker), Is.EqualTo(tiling));
                Assert.That(Offset(marker), Is.EqualTo(offset));
                SetLayoutField(marker, streamLayout == StreamLayout.Whole ? StreamLayout.Stacked : StreamLayout.Whole);
                Assert.That(Tiling(marker), Is.EqualTo(tiling));
                Assert.That(Offset(marker), Is.EqualTo(offset));
            }
            finally
            {
                // Awake's renderer.material made an instance of our copy; destroy both, never the SDK material.
                Material instance = mesh != null ? mesh.sharedMaterial : null;
                Object.DestroyImmediate(go);
                if (instance != null && instance != material && !EditorUtility.IsPersistent(instance)) { Object.DestroyImmediate(instance); }
                if (material != null) { Object.DestroyImmediate(material); }
            }
        }

        // A layout marker has no mesh material and gets its stream layout from Configure after Awake: the code default of
        // the stream layout it ends up with.
        [Test] public void LayoutConfigureSetsTheStreamLayoutAfterAddComponent()
        {
            var go = new GameObject("StreamLayoutConfigure");
            try
            {
                var screen = go.AddComponent<Screen>();
                var artist = go.AddComponent<Artist>();
                RunAwake(screen); RunAwake(artist);
                screen.Configure(1, "Visuals", "whole");
                artist.Configure(1, Vector3.zero, "Artist", "Whole");
                Assert.That(screen.StreamLayout, Is.EqualTo(StreamLayout.Whole));
                Assert.That(screen.TextureTiling, Is.EqualTo(Vector2.one)); Assert.That(screen.TextureOffset, Is.EqualTo(Vector2.zero));
                Assert.That(screen.StreamIndex, Is.EqualTo(1)); Assert.That(screen.StageIndex, Is.EqualTo(1));
                Assert.That(artist.StreamLayout, Is.EqualTo(StreamLayout.Whole));
                Assert.That(artist.TextureTiling, Is.EqualTo(Vector2.one)); Assert.That(artist.TextureOffset, Is.EqualTo(Vector2.zero));

                screen.Configure(1, "Visuals", "");
                artist.Configure(1, Vector3.zero, "Artist", "bottom");
                Assert.That(screen.StreamLayout, Is.EqualTo(StreamLayout.Stacked));
                Assert.That(screen.TextureTiling, Is.EqualTo(Half)); Assert.That(screen.TextureOffset, Is.EqualTo(Vector2.zero));
                Assert.That(artist.StreamLayout, Is.EqualTo(StreamLayout.Stacked));
                Assert.That(artist.TextureTiling, Is.EqualTo(Half)); Assert.That(artist.TextureOffset, Is.EqualTo(TopOffset));

                var legacy = new GameObject("StreamLayoutLegacyConfigure").AddComponent<Artist>();
                try
                {
                    legacy.Configure(0, Vector3.zero, "Artist");
                    Assert.That(legacy.StreamLayout, Is.EqualTo(StreamLayout.Stacked));
                    Assert.That(legacy.TextureTiling, Is.EqualTo(Half)); Assert.That(legacy.TextureOffset, Is.EqualTo(TopOffset));
                }
                finally { Object.DestroyImmediate(legacy.gameObject); }
            }
            finally { Object.DestroyImmediate(go); }
        }

        // The inspector swap on a prefab instance: stream layout and mesh material change together, record as prefab overrides
        // (the Artist mesh is a hidden child) and undo as one step. The Default Texture is never touched.
        [TestCase("Screen", false)]
        [TestCase("Artist", true)]
        [TestCase("Stage", true)]
        public void SetStreamLayoutSwapsOnlyTheMaterialAsAnUndoablePrefabOverride(string prefabName, bool artist)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>(prefabName));
            try
            {
                MonoBehaviour marker = Marker(go, artist);
                Renderer mesh = Mesh(marker);
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                StageStreamLayoutAssets.SetStreamLayout(marker, StreamLayout.Stacked);
                Undo.CollapseUndoOperations(group);

                Assert.That(LayoutOf(marker), Is.EqualTo(StreamLayout.Stacked));
                Assert.That(mesh.sharedMaterial, Is.SameAs(StageStreamLayoutAssets.GetMaterial(artist, StreamLayout.Stacked)));
                Assert.That(DefaultTexture(marker), Is.Null);
                Assert.That(StageStreamLayoutAssets.HasCustomMaterial(marker), Is.False);
                Assert.That(HasOverride(marker, "_streamLayout"), Is.True);
                Assert.That(HasOverride(marker, "_defaultTexture"), Is.False);
                Assert.That(HasOverride(mesh, "m_Materials.Array.data[0]"), Is.True);

                Undo.PerformUndo();
                Assert.That(LayoutOf(marker), Is.EqualTo(StreamLayout.Whole));
                Assert.That(mesh.sharedMaterial, Is.SameAs(StageStreamLayoutAssets.GetMaterial(artist, StreamLayout.Whole)));

                // A Default Texture set on the marker (even an SDK test picture) stays as it is either way.
                var stackedPicture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(StackedTextureGuid));
                Assert.That(stackedPicture, Is.Not.Null);
                SetDefaultTexture(marker, stackedPicture);
                StageStreamLayoutAssets.SetStreamLayout(marker, StreamLayout.Whole);
                Assert.That(mesh.sharedMaterial, Is.SameAs(StageStreamLayoutAssets.GetMaterial(artist, StreamLayout.Whole)));
                Assert.That(DefaultTexture(marker), Is.SameAs(stackedPicture));
                StageStreamLayoutAssets.SetStreamLayout(marker, StreamLayout.Stacked);
                Assert.That(mesh.sharedMaterial, Is.SameAs(StageStreamLayoutAssets.GetMaterial(artist, StreamLayout.Stacked)));
                Assert.That(DefaultTexture(marker), Is.SameAs(stackedPicture));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SetStreamLayoutLeavesACustomMaterialAndTextureAlone(bool artist)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>(artist ? "Artist" : "Screen"));
            var custom = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var customTexture = new Texture2D(4, 4);
            try
            {
                MonoBehaviour marker = Marker(go, artist);
                Mesh(marker).sharedMaterial = custom;
                SetDefaultTexture(marker, customTexture);

                Assert.That(StageStreamLayoutAssets.HasCustomMaterial(marker), Is.True);
                StageStreamLayoutAssets.SetStreamLayout(marker, StreamLayout.Stacked);
                Assert.That(LayoutOf(marker), Is.EqualTo(StreamLayout.Stacked));
                Assert.That(Mesh(marker).sharedMaterial, Is.SameAs(custom));
                Assert.That(DefaultTexture(marker), Is.SameAs(customTexture));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(custom);
                Object.DestroyImmediate(customTexture);
            }
        }

        // The overrides are written to disk and come back on reload. A prefab variant saved from the instance stores
        // them exactly as a scene does (PrefabInstance m_Modifications), without touching the open scenes.
        [TestCase("Artist", true)]
        [TestCase("Screen", false)]
        public void SwapOverridesSurviveSaveAndReload(string prefabName, bool artist)
        {
            string path = "Assets/__StageStreamLayoutOverrideTest" + prefabName + ".prefab";
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>(prefabName));
            try
            {
                StageStreamLayoutAssets.SetStreamLayout(Marker(go, artist), StreamLayout.Stacked);
                PrefabUtility.SaveAsPrefabAsset(go, path, out bool saved);
                Assert.That(saved, Is.True);
                Object.DestroyImmediate(go);
                go = null;

                string yaml = System.IO.File.ReadAllText(path);
                Assert.That(yaml, Does.Contain("propertyPath: 'm_Materials.Array.data[0]'"));
                Assert.That(yaml, Does.Contain("propertyPath: _streamLayout"));
                Assert.That(yaml, Does.Not.Contain("propertyPath: _defaultTexture"));

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                MonoBehaviour marker = Marker(AssetDatabase.LoadAssetAtPath<GameObject>(path), artist);
                Assert.That(LayoutOf(marker), Is.EqualTo(StreamLayout.Stacked));
                Assert.That(Mesh(marker).sharedMaterial, Is.SameAs(StageStreamLayoutAssets.GetMaterial(artist, StreamLayout.Stacked)));
                Assert.That(DefaultTexture(marker), Is.Null);
            }
            finally
            {
                if (go != null) { Object.DestroyImmediate(go); }
                AssetDatabase.DeleteAsset(path);
            }
        }

        // Scenes and prefabs saved before the rename store the field as _videoArea, as a value and as a prefab
        // instance override. FormerlySerializedAs("_videoArea") must read both back.
        [TestCase("Artist", true)]
        [TestCase("Screen", false)]
        public void TheOldVideoAreaKeyStillLoads(string prefabName, bool artist)
        {
            string sourcePath = AssetDatabase.GetAssetPath(Resources.Load<GameObject>(prefabName));
            string valuePath = "Assets/__StageStreamLayoutLegacyValue" + prefabName + ".prefab";
            string overridePath = "Assets/__StageStreamLayoutLegacyOverride" + prefabName + ".prefab";
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>(prefabName));
            try
            {
                // A plain value: the SDK prefab (Whole = 1, the code default is Stacked = 0) with the old key.
                string yaml = System.IO.File.ReadAllText(sourcePath);
                Assert.That(yaml, Does.Contain("_streamLayout: 1"));
                System.IO.File.WriteAllText(valuePath, yaml.Replace("_streamLayout: 1", "_videoArea: 1"));
                AssetDatabase.ImportAsset(valuePath, ImportAssetOptions.ForceUpdate);
                Assert.That(LayoutOf(Marker(AssetDatabase.LoadAssetAtPath<GameObject>(valuePath), artist)), Is.EqualTo(StreamLayout.Whole));

                // An override on a prefab instance (how a creator's scene stores a Stacked marker), with the old path.
                StageStreamLayoutAssets.SetStreamLayout(Marker(go, artist), StreamLayout.Stacked);
                PrefabUtility.SaveAsPrefabAsset(go, overridePath, out bool saved);
                Assert.That(saved, Is.True);
                string variant = System.IO.File.ReadAllText(overridePath);
                Assert.That(variant, Does.Contain("propertyPath: _streamLayout"));
                System.IO.File.WriteAllText(overridePath, variant.Replace("propertyPath: _streamLayout", "propertyPath: _videoArea"));
                AssetDatabase.ImportAsset(overridePath, ImportAssetOptions.ForceUpdate);
                Assert.That(LayoutOf(Marker(AssetDatabase.LoadAssetAtPath<GameObject>(overridePath), artist)), Is.EqualTo(StreamLayout.Stacked));
            }
            finally
            {
                Object.DestroyImmediate(go);
                AssetDatabase.DeleteAsset(valuePath);
                AssetDatabase.DeleteAsset(overridePath);
            }
        }

        // ---- Screen "Shows" (ScreenContent): Visuals or Artist (green screen) ----

        private static Material ScreenMaterial(ScreenContent content, StreamLayout streamLayout)
        {
            return StageStreamLayoutAssets.GetMaterial(content == ScreenContent.Artist, streamLayout);
        }

        [Test] public void ScreenShowsVisualsByDefault()
        {
            Assert.That((int)ScreenContent.Visuals, Is.EqualTo(0), "0 must stay Visuals: published worlds have no value");
            Assert.That(System.Enum.GetValues(typeof(ScreenContent)).Length, Is.EqualTo(2));
            var go = new GameObject("ScreenContentDefault");
            try { Assert.That(go.AddComponent<Screen>().Content, Is.EqualTo(ScreenContent.Visuals)); }
            finally { Object.DestroyImmediate(go); }
            foreach (string prefabName in new[] { "Screen", "Stage" })
            {
                Assert.That(((Screen)Marker(Resources.Load<GameObject>(prefabName), false)).Content, Is.EqualTo(ScreenContent.Visuals), prefabName);
            }
        }

        [Test] public void ScreenContentParseAndFormatRoundTrip()
        {
            foreach (ScreenContent content in System.Enum.GetValues(typeof(ScreenContent)))
            {
                Assert.That(ScreenContents.Parse(ScreenContents.Format(content)), Is.EqualTo(content));
            }
            Assert.That(ScreenContents.Format(ScreenContent.Visuals), Is.EqualTo(""));
            Assert.That(ScreenContents.Format(ScreenContent.Artist), Is.EqualTo("artist"));
            Assert.That(ScreenContents.Parse("Artist"), Is.EqualTo(ScreenContent.Artist));
            foreach (string visuals in new[] { null, "", "visuals", "Visuals", "1", "whole", "keyed" })
            {
                Assert.That(ScreenContents.Parse(visuals), Is.EqualTo(ScreenContent.Visuals), visuals ?? "null");
            }
        }

        // Shows picks the pair (Artist = the chroma key materials), Stream Layout the one in it.
        [TestCase(ScreenContent.Visuals, StreamLayout.Whole, "Screen-Calibrate-UV-Whole.mat")]
        [TestCase(ScreenContent.Visuals, StreamLayout.Stacked, "Screen-Calibrate-UV.mat")]
        [TestCase(ScreenContent.Artist, StreamLayout.Whole, "Artist-Calibrate-UV-Whole.mat")]
        [TestCase(ScreenContent.Artist, StreamLayout.Stacked, "Artist-Calibrate-UV.mat")]
        public void ScreenShowsAndStreamLayoutPickTheMaterial(ScreenContent content, StreamLayout streamLayout, string materialFile)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>("Screen"));
            try
            {
                var screen = (Screen)Marker(go, false);
                StageStreamLayoutAssets.SetScreenContent(screen, content);
                StageStreamLayoutAssets.SetStreamLayout(screen, streamLayout);
                Assert.That(screen.Content, Is.EqualTo(content));
                Assert.That(screen.StreamLayout, Is.EqualTo(streamLayout));
                Assert.That(AssetDatabase.GetAssetPath(screen.Renderer.sharedMaterial), Does.EndWith("/" + materialFile));
                Assert.That(screen.Renderer.sharedMaterial, Is.SameAs(ScreenMaterial(content, streamLayout)));
                Assert.That(StageStreamLayoutAssets.HasCustomMaterial(screen), Is.False);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // Changing either dropdown alone swaps to the material of both, from every combination.
        [Test] public void ChangingShowsOrStreamLayoutSwapsToTheMaterialOfBoth()
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>("Screen"));
            try
            {
                var screen = (Screen)Marker(go, false);
                foreach (ScreenContent content in new[] { ScreenContent.Artist, ScreenContent.Visuals, ScreenContent.Artist })
                {
                    foreach (StreamLayout streamLayout in new[] { StreamLayout.Stacked, StreamLayout.Whole })
                    {
                        StageStreamLayoutAssets.SetScreenContent(screen, content);
                        Assert.That(screen.Renderer.sharedMaterial, Is.SameAs(ScreenMaterial(content, screen.StreamLayout)));
                        StageStreamLayoutAssets.SetStreamLayout(screen, streamLayout);
                        Assert.That(screen.Renderer.sharedMaterial, Is.SameAs(ScreenMaterial(content, streamLayout)));
                    }
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        // On a prefab instance (the Screen prefab, and the screen inside the Stage prefab): Shows and the mesh material change
        // together, record as prefab overrides and undo as one step. The Default Texture is never touched.
        [TestCase("Screen")]
        [TestCase("Stage")]
        public void SetScreenContentIsAnUndoablePrefabOverride(string prefabName)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>(prefabName));
            try
            {
                var screen = (Screen)Marker(go, false);
                Renderer mesh = screen.Renderer;
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                StageStreamLayoutAssets.SetScreenContent(screen, ScreenContent.Artist);
                Undo.CollapseUndoOperations(group);

                Assert.That(screen.Content, Is.EqualTo(ScreenContent.Artist));
                Assert.That(mesh.sharedMaterial, Is.SameAs(ScreenMaterial(ScreenContent.Artist, StreamLayout.Whole)));
                Assert.That(screen.DefaultTexture, Is.Null);
                Assert.That(HasOverride(screen, "_content"), Is.True);
                Assert.That(HasOverride(screen, "_defaultTexture"), Is.False);
                Assert.That(HasOverride(mesh, "m_Materials.Array.data[0]"), Is.True);

                Undo.PerformUndo();
                Assert.That(screen.Content, Is.EqualTo(ScreenContent.Visuals));
                Assert.That(mesh.sharedMaterial, Is.SameAs(ScreenMaterial(ScreenContent.Visuals, StreamLayout.Whole)));
            }
            finally { Object.DestroyImmediate(go); }
        }

        // A creator's own material stays (Shows still changes); no material at all takes the SDK one. An Artist has no Shows.
        [Test] public void SetScreenContentLeavesACustomMaterialAlone()
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>("Screen"));
            var artistGo = (GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>("Artist"));
            var custom = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                var screen = (Screen)Marker(go, false);
                screen.Renderer.sharedMaterial = custom;
                StageStreamLayoutAssets.SetScreenContent(screen, ScreenContent.Artist);
                Assert.That(screen.Content, Is.EqualTo(ScreenContent.Artist));
                Assert.That(screen.Renderer.sharedMaterial, Is.SameAs(custom));
                StageStreamLayoutAssets.SetStreamLayout(screen, StreamLayout.Stacked);
                Assert.That(screen.Renderer.sharedMaterial, Is.SameAs(custom));

                screen.Renderer.sharedMaterial = null;
                StageStreamLayoutAssets.SetScreenContent(screen, ScreenContent.Artist);
                Assert.That(screen.Renderer.sharedMaterial, Is.SameAs(ScreenMaterial(ScreenContent.Artist, StreamLayout.Stacked)));

                var artist = (Artist)Marker(artistGo, true);
                Material artistMaterial = artist.Renderer.sharedMaterial;
                StageStreamLayoutAssets.SetScreenContent(artist, ScreenContent.Visuals);
                Assert.That(artist.Renderer.sharedMaterial, Is.SameAs(artistMaterial));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(artistGo);
                Object.DestroyImmediate(custom);
            }
        }

        // A layout screen has no mesh material: Shows Artist + Stacked is the top half, like an Artist; Whole is the full frame.
        [Test] public void ArtistScreenWithoutAMaterialShowsTheTopHalf()
        {
            var go = new GameObject("ScreenContentNoMesh");
            try
            {
                var screen = go.AddComponent<Screen>();
                RunAwake(screen);
                screen.Configure(1, "Artist", "", "artist");
                Assert.That(screen.Content, Is.EqualTo(ScreenContent.Artist));
                Assert.That(screen.StreamLayout, Is.EqualTo(StreamLayout.Stacked));
                Assert.That(screen.TextureTiling, Is.EqualTo(Half)); Assert.That(screen.TextureOffset, Is.EqualTo(TopOffset));
                Assert.That(screen.StreamIndex, Is.EqualTo(0)); Assert.That(screen.StageIndex, Is.EqualTo(1));

                screen.Configure(1, "Artist", "whole", "Artist");
                Assert.That(screen.TextureTiling, Is.EqualTo(Vector2.one)); Assert.That(screen.TextureOffset, Is.EqualTo(Vector2.zero));

                // The older overloads leave Shows as it is.
                screen.Configure(1, "Artist", "");
                Assert.That(screen.Content, Is.EqualTo(ScreenContent.Artist));
                Assert.That(screen.TextureOffset, Is.EqualTo(TopOffset));

                screen.Configure(1, "Visuals", "", "");
                Assert.That(screen.Content, Is.EqualTo(ScreenContent.Visuals));
                Assert.That(screen.TextureTiling, Is.EqualTo(Half)); Assert.That(screen.TextureOffset, Is.EqualTo(Vector2.zero));
            }
            finally { Object.DestroyImmediate(go); }
        }

        // The Inspector warns when one object has both a Screen and an Artist; no SDK prefab does.
        [Test] public void ScreenAndArtistOnOneObjectIsFlagged()
        {
            var go = new GameObject("ScreenAndArtist");
            try
            {
                var screen = go.AddComponent<Screen>();
                Assert.That(StagePickerGUI.HasScreenAndArtist(screen), Is.False);
                var artist = go.AddComponent<Artist>();
                Assert.That(StagePickerGUI.HasScreenAndArtist(screen), Is.True);
                Assert.That(StagePickerGUI.HasScreenAndArtist(artist), Is.True);
                Assert.That(StagePickerGUI.HasScreenAndArtist(null), Is.False);
            }
            finally { Object.DestroyImmediate(go); }
            foreach (string prefabName in new[] { "Screen", "Artist", "Stage" })
            {
                GameObject prefab = Resources.Load<GameObject>(prefabName);
                foreach (Screen screen in prefab.GetComponentsInChildren<Screen>(true)) { Assert.That(StagePickerGUI.HasScreenAndArtist(screen), Is.False, prefabName); }
                foreach (Artist artist in prefab.GetComponentsInChildren<Artist>(true)) { Assert.That(StagePickerGUI.HasScreenAndArtist(artist), Is.False, prefabName); }
            }
        }

        private static bool HasOverride(Object target, string propertyPath)
        {
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(target);
            foreach (PropertyModification modification in PrefabUtility.GetPropertyModifications(target) ?? new PropertyModification[0])
            {
                if (modification.target == source && modification.propertyPath == propertyPath) { return true; }
            }
            return false;
        }
    }
}
