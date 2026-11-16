using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VirtualVenues.WorldCreator;
using Screen = VirtualVenues.WorldCreator.Screen;
using Stage = VirtualVenues.WorldCreator.Stage;

namespace VirtualVenues.Tests
{
    /// <summary>
    /// The Stage markers' editor tools: the Inspector's stage Refresh (the parent Stage wins, else the nearest Stage),
    /// the Speaker's audio range, and the VirtualVenues logo as every Stage component's script icon.
    /// </summary>
    public class StageMarkerToolsTests
    {
        // Far from anything a test scene may already hold, so "nearest" only sees this test's Stages.
        private static readonly Vector3 Far = new Vector3(10000f, 0f, 10000f);

        private GameObject _stageA;
        private GameObject _stageB;

        [SetUp] public void Setup()
        {
            _stageA = NewStage("Refresh Stage A", Far, 7);
            _stageB = NewStage("Refresh Stage B", Far + new Vector3(100f, 0f, 0f), 8);
        }

        [TearDown] public void Teardown()
        {
            Object.DestroyImmediate(_stageA);
            Object.DestroyImmediate(_stageB);
        }

        private static GameObject NewStage(string name, Vector3 position, int index)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            // Through the serialized field, not Configure, so the runtime Stage list is left alone.
            var so = new SerializedObject(go.AddComponent<Stage>());
            so.FindProperty("_stageIndex").intValue = index;
            so.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        private static Component AddMarker(GameObject go, string kind)
        {
            switch (kind)
            {
                case "Screen": return go.AddComponent<Screen>();
                case "Artist": return go.AddComponent<Artist>();
                default: go.AddComponent<AudioSource>(); return go.AddComponent<Speaker>();
            }
        }

        private static void Assign(Component marker, Stage stage)
        {
            var so = new SerializedObject(marker);
            so.FindProperty("_stage").objectReferenceValue = stage;
            so.FindProperty("_stageIndex").intValue = stage.StageIndex;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Object AssignedStage(Component marker) => new SerializedObject(marker).FindProperty("_stage").objectReferenceValue;
        private static int SerializedIndex(Component marker) => new SerializedObject(marker).FindProperty("_stageIndex").intValue;

        private static int StageIndexOf(Component marker)
        {
            if (marker is Screen screen) { return screen.StageIndex; }
            if (marker is Artist artist) { return artist.StageIndex; }
            return ((Speaker)marker).StageIndex;
        }

        // The owner's report: a stage speaker moved next to another Stage adopts that Stage on Refresh, as one undo step.
        [TestCase("Speaker")]
        [TestCase("Screen")]
        [TestCase("Artist")]
        public void RefreshAdoptsTheNearestStage(string kind)
        {
            var go = new GameObject("Refresh marker");
            try
            {
                Component marker = AddMarker(go, kind);
                Assign(marker, _stageA.GetComponent<Stage>());
                go.transform.position = _stageB.transform.position + new Vector3(2f, 0f, 0f);

                Undo.IncrementCurrentGroup();
                StagePickerGUI.RefreshStage(new Object[] { marker });
                Assert.That(AssignedStage(marker), Is.SameAs(_stageB.GetComponent<Stage>()));
                Assert.That(SerializedIndex(marker), Is.EqualTo(8));
                Assert.That(StageIndexOf(marker), Is.EqualTo(8));

                Undo.PerformUndo();
                Assert.That(AssignedStage(marker), Is.SameAs(_stageA.GetComponent<Stage>()));
                Assert.That(SerializedIndex(marker), Is.EqualTo(7));
            }
            finally { Object.DestroyImmediate(go); }
        }

        // A Stage in the parents wins over a nearer one, and replaces a Stage assigned before the object was moved under it.
        [TestCase("Speaker")]
        [TestCase("Screen")]
        [TestCase("Artist")]
        public void RefreshPrefersTheParentStage(string kind)
        {
            var go = new GameObject("Refresh child marker");
            go.transform.SetParent(_stageA.transform, false);
            Component marker = AddMarker(go, kind);
            Assign(marker, _stageB.GetComponent<Stage>());
            go.transform.position = _stageB.transform.position;

            StagePickerGUI.RefreshStage(new Object[] { marker });
            Assert.That(AssignedStage(marker), Is.SameAs(_stageA.GetComponent<Stage>()));
            Assert.That(StageIndexOf(marker), Is.EqualTo(7));
            Assert.That(Stage.FindFor(marker), Is.SameAs(_stageA.GetComponent<Stage>()));
        }

        // Several selected markers each take their own Stage.
        [Test] public void RefreshResolvesEachSelectedMarker()
        {
            var nearA = new GameObject("Near A");
            var nearB = new GameObject("Near B");
            try
            {
                Component a = AddMarker(nearA, "Speaker");
                Component b = AddMarker(nearB, "Screen");
                nearA.transform.position = _stageA.transform.position;
                nearB.transform.position = _stageB.transform.position;
                StagePickerGUI.RefreshStage(new Object[] { a, b });
                Assert.That(AssignedStage(a), Is.SameAs(_stageA.GetComponent<Stage>()));
                Assert.That(AssignedStage(b), Is.SameAs(_stageB.GetComponent<Stage>()));
            }
            finally
            {
                Object.DestroyImmediate(nearA);
                Object.DestroyImmediate(nearB);
            }
        }

        // On a prefab instance Refresh records a prefab override.
        [Test] public void RefreshOnAPrefabInstanceIsAnOverride()
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>("Screen"));
            try
            {
                go.transform.position = _stageB.transform.position;
                Screen screen = go.GetComponentInChildren<Screen>(true);
                StagePickerGUI.RefreshStage(new Object[] { screen });
                Assert.That(AssignedStage(screen), Is.SameAs(_stageB.GetComponent<Stage>()));
                bool overridden = false;
                foreach (PropertyModification modification in PrefabUtility.GetPropertyModifications(screen) ?? new PropertyModification[0])
                {
                    if (modification.propertyPath == "_stage") { overridden = true; }
                }
                Assert.That(overridden, Is.True);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // Only Stages in the marker's own scene count; with none, the marker keeps the Stage it has.
        [Test] public void RefreshWithNoStageInTheSceneKeepsTheAssignment()
        {
            UnityEngine.SceneManagement.Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("Lonely speaker");
                EditorSceneManager.MoveGameObjectToScene(go, preview);
                Component marker = AddMarker(go, "Speaker");
                Assign(marker, _stageA.GetComponent<Stage>());
                Assert.That(Stage.FindFor(marker), Is.Null);
                StagePickerGUI.RefreshStage(new Object[] { marker });
                Assert.That(AssignedStage(marker), Is.SameAs(_stageA.GetComponent<Stage>()));
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        // An inactive Stage is not adopted.
        [Test] public void FindForSkipsInactiveStages()
        {
            var go = new GameObject("Near inactive B");
            try
            {
                go.transform.position = _stageB.transform.position;
                _stageB.SetActive(false);
                Assert.That(Stage.FindFor(go.transform), Is.SameAs(_stageA.GetComponent<Stage>()));
            }
            finally
            {
                _stageB.SetActive(true);
                Object.DestroyImmediate(go);
            }
        }

        // Stage Trigger and Fixture look their Stage up the same way: the parent Stage first, else the nearest.
        [Test] public void StageTriggerAndFixtureTakeTheParentStageThenTheNearest()
        {
            var trigger = new GameObject("Refresh trigger");
            var fixture = new GameObject("Refresh fixture");
            try
            {
                trigger.AddComponent<BoxCollider>();
                StageTrigger stageTrigger = trigger.AddComponent<StageTrigger>();
                Fixture stageFixture = fixture.AddComponent<Fixture>();

                trigger.transform.position = _stageB.transform.position;
                fixture.transform.position = _stageB.transform.position;
                stageTrigger.EDITOR_RESET();
                stageFixture.EDITOR_RESET();
                Assert.That(stageTrigger.StageIndex, Is.EqualTo(8));
                Assert.That(stageFixture.StageIndex, Is.EqualTo(8));

                trigger.transform.SetParent(_stageA.transform, true);
                fixture.transform.SetParent(_stageA.transform, true);
                stageTrigger.EDITOR_RESET();
                stageFixture.EDITOR_RESET();
                Assert.That(stageTrigger.StageIndex, Is.EqualTo(7));
                Assert.That(stageFixture.StageIndex, Is.EqualTo(7));
            }
            finally
            {
                Object.DestroyImmediate(trigger);
                Object.DestroyImmediate(fixture);
            }
        }

        // ---- Speaker audio range ----

        [Test] public void AudioRangeIsTheAudioSourceMinAndMaxDistance()
        {
            var go = new GameObject("Audio range speaker", typeof(AudioSource));
            try
            {
                go.AddComponent<Speaker>();
                AudioSource source = go.GetComponent<AudioSource>();
                source.minDistance = 4f;
                source.maxDistance = 30f;
                SpeakerEditor.GetAudioRange(source, out float inner, out float outer);
                Assert.That(inner, Is.EqualTo(4f));
                Assert.That(outer, Is.EqualTo(30f));

                source.maxDistance = 2f; // Unity clamps max to at least min; the preview never draws outer inside inner either
                SpeakerEditor.GetAudioRange(source, out inner, out outer);
                Assert.That(outer, Is.GreaterThanOrEqualTo(inner));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test] public void ShowAudioRangeIsAnEditorPreference()
        {
            bool had = EditorPrefs.HasKey(SpeakerEditor.ShowAudioRangePrefKey);
            bool before = SpeakerEditor.ShowAudioRange;
            try
            {
                SpeakerEditor.ShowAudioRange = true;
                Assert.That(EditorPrefs.GetBool(SpeakerEditor.ShowAudioRangePrefKey), Is.True);
                SpeakerEditor.ShowAudioRange = false;
                Assert.That(SpeakerEditor.ShowAudioRange, Is.False);
                Assert.That(SpeakerEditor.ShowAudioRangeLabel, Is.EqualTo("Show audio range"));
                Assert.That(SpeakerEditor.FullVolumeLabel, Is.EqualTo("Full volume"));
                Assert.That(SpeakerEditor.SilentBeyondLabel, Is.EqualTo("Silent beyond"));
            }
            finally
            {
                if (had) { SpeakerEditor.ShowAudioRange = before; }
                else { EditorPrefs.DeleteKey(SpeakerEditor.ShowAudioRangePrefKey); }
            }
        }

        // ---- Script icons ----

        private const string LogoGuid = "1580eff59dda18b4c8eeff61e5753e38"; // Editor/Images/vv_logo_small.png

        // Stage, Stage Trigger, Screen, Artist, Speaker, Fixture, Ambient Audio (and its old name), Room Boundary.
        [TestCase("a104a4109ac4bc54c9418b51ce6cd7d9", typeof(Stage))]
        [TestCase("ca35c816719f74b46a78436b6398832e", typeof(StageTrigger))]
        [TestCase("5a01ee41d39b02445bd1e4bfe458a17d", typeof(Screen))]
        [TestCase("76a9fabfd984ec94e9bdef38b9913cf1", typeof(Artist))]
        [TestCase("88da85e377bfe4341bcd9321c72b3fbd", typeof(Speaker))]
        [TestCase("efd89fb62c01b7949a33f825dd173abd", typeof(Fixture))]
        [TestCase("d4655ad76c64e9b4b8608011359c55d2", typeof(AmbientAudio))]
        [TestCase("e5bf1431afae4f9c9a62b80f612a0d74", null)] // RoomAmbientAudio (obsolete)
        [TestCase("86b4621b67e84dc3a2b51093c92155a7", typeof(RoomBoundary))]
        public void StageComponentsUseTheLogoAsTheirScriptIcon(string scriptGuid, System.Type type)
        {
            string path = AssetDatabase.GUIDToAssetPath(scriptGuid);
            Assert.That(path, Is.Not.Empty);
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            Assert.That(script, Is.Not.Null, path);
            if (type != null) { Assert.That(script.GetClass(), Is.EqualTo(type), path); }
            string meta = File.ReadAllText(Path.GetFullPath(path + ".meta")); // GetFullPath also resolves Packages/ paths
            StringAssert.Contains("icon: {fileID: 2800000, guid: " + LogoGuid + ", type: 3}", meta, path);
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(LogoGuid)), Is.Not.Null, "the logo texture");
        }
    }
}
