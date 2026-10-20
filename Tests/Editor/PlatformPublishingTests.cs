using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Diagnostics;
using UnityEngine.TestTools;
using UnityEngine;
using UnityEngine.UIElements;
using NUnit.Framework;
using UnityEditor;
using VirtualVenues.Editor.Publishing;

namespace VirtualVenues.Tests
{
    public class PlatformPublishingTests
    {
        [UnityTest] public IEnumerator SDKWorldValidationFinishesAndPreservesTarget()
        {
            string release = Environment.GetEnvironmentVariable("VV_SDK_WORLD_TEST_RELEASE");
            if (string.IsNullOrEmpty(release)) { Assert.Ignore("Set VV_SDK_WORLD_TEST_RELEASE to an exported world release."); }
            Assert.That(File.Exists(PlatformPublisherProcess.ContentToolPath), Is.True);
            var original = EditorUserBuildSettings.activeBuildTarget;
            PlatformPublisherProcess.Start(PlatformPublisherProcess.ContentToolPath,
                "publish --dry-run --require-kind world --release " + PlatformPublisherProcess.Quote(release) + " --api https://api.test");
            Assert.Throws<InvalidOperationException>(() => WorldPublisher.WorldPublishJob.Start("missing.unity", null, "probe", false));
            Assert.Throws<InvalidOperationException>(() => PlatformPublisherProcess.Start(PlatformPublisherProcess.ContentToolPath, "--help"));
            var timer = Stopwatch.StartNew();
            while (PlatformPublisherProcess.IsRunning && timer.Elapsed.TotalSeconds < 30) { yield return null; }
            Assert.That(PlatformPublisherProcess.IsRunning, Is.False);
            Assert.That(PlatformPublisherProcess.Status, Does.StartWith("Completed."));
            Assert.That(PlatformPublisherProcess.Status, Does.Contain("no network requests made"));
            Assert.That(EditorUserBuildSettings.activeBuildTarget, Is.EqualTo(original));
        }

        [Test] public void WorldPublisherLayoutLoadsFromTheInstalledSDK()
        {
            var paths = AssetDatabase.FindAssets("t:VisualTreeAsset WorldPublisherUI");
            Assert.That(paths, Is.Not.Empty);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetDatabase.GUIDToAssetPath(paths[0]));
            var root = tree.CloneTree();
            Assert.That(root.Q<Toggle>("include-mobile"), Is.Not.Null);
            Assert.That(root.Q<Toggle>("include-mobile").value, Is.False);
            Assert.That(root.Q<DropdownField>("build-format"), Is.Null);
            Assert.That(root.Q("platform-publish-section"), Is.Null);
            Assert.That(root.Q("additional-scenes-section"), Is.Not.Null);
            Assert.That(root.Q<Button>("publish-button"), Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(PlatformPublisherProcess.SDKRoot, "package.json")), Is.True);
        }
        [Test] public void PreviouslyPublishedManifestsRemainCompatible()
        {
            var manifest = new VirtualVenues.WorldCreator.WorldStreamingManifest {
                target = "iOS", baseBundle = "world_base",
                bundles = new[] { new VirtualVenues.WorldCreator.StreamingBundle {
                    name = "world_base", file = "world_base", scene = "Assets/Main.unity", bytes = 10,
                    hash = new string('a', 32), additionalScenes = null } } };
            string json = JsonUtility.ToJson(manifest).Replace("\"additionalScenes\":[],", "").Replace(",\"additionalScenes\":[]", "").Replace(",\"additionalScenes\":null", "");
            // Missing/null additional scenes are the existing remotely published schema.
            JsonUtility.FromJson<VirtualVenues.WorldCreator.WorldStreamingManifest>(json).Validate("iOS");
            manifest.Validate("iOS");
        }
        [Test] public void MobileWorldIncludesAdditionalScenesAndKeepsMainScene()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS) { Assert.Ignore("Requires a ready iOS worker."); }
            if (!File.Exists(".vv-disposable-project.json")) { Assert.Ignore("Requires a disposable worker."); }
            string id = Guid.NewGuid().ToString("N");
            string folder = "Assets/PublisherProbe_" + id;
            Directory.CreateDirectory(folder);
            string main = folder + "/Main.unity", extra = folder + "/Extra.unity";
            string output = Path.Combine(Path.GetTempPath(), "vv-publisher-" + id, "ios");
            var original = EditorUserBuildSettings.activeBuildTarget;
            var setup = UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (string path in new[] { main, extra })
                {
                    var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
                    new GameObject(Path.GetFileNameWithoutExtension(path));
                    Assert.That(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, path), Is.True);
                }
                AssetDatabase.Refresh();
                PlatformContentBuild.Build(new PlatformContentBuild.Request { kind = "world", platform = "ios", scene = main,
                    additionalScenes = new[] { extra }, outputDirectory = output });
                var manifest = JsonUtility.FromJson<VirtualVenues.WorldCreator.WorldStreamingManifest>(File.ReadAllText(Path.Combine(output, "world.streaming.json")));
                manifest.Validate("iOS");
                Assert.That(manifest.FindBundle(manifest.baseBundle).scene, Is.EqualTo(main));
                Assert.That(manifest.FindBundle(manifest.baseBundle).additionalScenes, Is.EqualTo(new[] { extra }));
                var bundle = AssetBundle.LoadFromFile(Path.Combine(output, "world_base"));
                try { Assert.That(bundle.GetAllScenePaths().Length, Is.EqualTo(2)); }
                finally { bundle.Unload(true); }
                Assert.That(EditorUserBuildSettings.activeBuildTarget, Is.EqualTo(original));
            }
            finally
            {
                if (setup.Any(item => item.isLoaded && item.isActive))
                { UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(setup); }
                else { UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene); }
                AssetDatabase.DeleteAsset(folder);
                if (Directory.Exists(Path.GetDirectoryName(output))) { Directory.Delete(Path.GetDirectoryName(output), true); }
            }
        }
        [Test] public void UnsupportedCatalogTargetDoesNotSwitchTheEditor()
        {
            var original = EditorUserBuildSettings.activeBuildTarget;
            Assert.Throws<ArgumentException>(() => new AvatarCatalogBuilder().BuildForTarget(BuildTarget.Android));
            Assert.That(EditorUserBuildSettings.activeBuildTarget, Is.EqualTo(original));
        }
        [Test] public void WrongWorkerTargetDoesNotSwitchTheEditor()
        {
            var original = EditorUserBuildSettings.activeBuildTarget;
            var other = original == BuildTarget.iOS ? BuildTarget.WebGL : BuildTarget.iOS;
            Assert.Throws<InvalidOperationException>(() => new AvatarCatalogBuilder().BuildForTarget(other));
            Assert.That(EditorUserBuildSettings.activeBuildTarget, Is.EqualTo(original));
        }
        [Test] public void CosmeticAddressesRemainTheSameAcrossTargetBuilds()
        {
            AvatarCatalogBuilder.ResolveCosmeticAddress("Halo", "Hat", out var address, out var gameId);
            Assert.That(address, Is.EqualTo("Hat_Halo"));
            Assert.That(gameId, Is.EqualTo("Halo"));
            AvatarCatalogBuilder.ResolveCosmeticAddress("Hat_Halo", "Hat", out var prefixed, out var existingId);
            Assert.That(prefixed, Is.EqualTo(address));
            Assert.That(existingId, Is.EqualTo(gameId));
        }
    }
}
