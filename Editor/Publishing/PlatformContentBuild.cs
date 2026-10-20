using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VirtualVenues.WorldCreator;
using VirtualVenues.Editor.AssetPackPublisher;

namespace VirtualVenues.Editor.Publishing
{
    /// <summary>Target-specific build worker. Does not upload or mutate the working project's target.</summary>
    public static class PlatformContentBuild
    {
        [Serializable] public sealed class ContentEntry { public string assetPath, slotId, assetKey; }
        [Serializable] public sealed class Request
        {
            public string platform, kind, scene, streamingSettingsPath, outputDirectory, contentBaseUrl, releaseId;
            public string[] additionalScenes = Array.Empty<string>();
            public string[] avatars = Array.Empty<string>();
            public ContentEntry[] cosmetics = Array.Empty<ContentEntry>();
            public ContentEntry[] assets = Array.Empty<ContentEntry>();
        }
        [Serializable] public sealed class Result { public string result, platform, target, unityVersion, entryPoint, profile = "source-render-config", contentBaseUrl; }

        public static void RunBatch() => Build(ReadRequest());
        public static Request ReadRequest()
        {
            if (!File.Exists(".vv-disposable-project.json") || Directory.Exists(".plastic") || Directory.Exists(".git"))
            { throw new InvalidOperationException("Use a marked isolated project without SCM metadata."); }
            return JsonUtility.FromJson<Request>(File.ReadAllText(Environment.GetEnvironmentVariable("VV_PLATFORM_CONTENT_REQUEST")));
        }
        public static void Build(Request request)
        {
            if (request == null || (request.platform != "desktop-web" && request.platform != "ios" && request.platform != "linux-server"))
            { throw new ArgumentException("Select desktop-web, ios or a Linux server world."); }
            var target = request.platform == "ios" ? BuildTarget.iOS : request.platform == "linux-server" ? BuildTarget.StandaloneLinux64 : BuildTarget.WebGL;
            if (request.platform == "linux-server" && request.kind != "world") { throw new ArgumentException("Linux server export is only for worlds."); }
            if (EditorUserBuildSettings.activeBuildTarget != target || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
            { throw new InvalidOperationException("Start a ready worker with the matching target."); }
            string output = Path.GetFullPath(request.outputDirectory);
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            { throw new InvalidOperationException("Use a new empty output directory."); }
            Directory.CreateDirectory(output);
            string entry;
            if (request.kind == "world")
            {
                if (target == BuildTarget.StandaloneLinux64) { entry = BuildServerWorld(request, output); }
                else { BuildWorld(request, output, target); entry = "world.streaming.json"; }
            }
            else if (request.kind == "catalog" || request.kind == "asset-pack")
            {
                BuildCatalog(request, output, target);
                entry = "content_catalog.bin";
            }
            else { throw new ArgumentException("Content kind must be world, catalog or asset-pack."); }
            File.WriteAllText(Path.Combine(output, "platform-build.json"), JsonUtility.ToJson(new Result {
                result = "Succeeded", platform = request.platform, target = target.ToString(), unityVersion = Application.unityVersion, entryPoint = entry, contentBaseUrl = request.contentBaseUrl }, true));
        }
        private static string BuildServerWorld(Request request,string output)
        {
            string scene = request.scene;
            if (string.IsNullOrEmpty(scene) && !string.IsNullOrEmpty(request.streamingSettingsPath))
            { scene = JsonUtility.FromJson<StreamingWorldExporter.Settings>(File.ReadAllText(request.streamingSettingsPath)).sourceScene; }
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(scene)) { throw new ArgumentException("Select an imported server scene."); }
            string suffix = string.IsNullOrEmpty(request.releaseId) ? "local" : new string(request.releaseId.Replace("-", "").Take(8).ToArray());
            string name = ("world_ums_" + Path.GetFileNameWithoutExtension(scene) + "_" + suffix).ToLowerInvariant();
            var manifest = BuildPipeline.BuildAssetBundles(output,new[] { new AssetBundleBuild { assetBundleName = name,assetNames = new[] {scene} } },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,BuildTarget.StandaloneLinux64);
            if (!manifest || !File.Exists(Path.Combine(output,name))) { throw new InvalidOperationException("Server world export failed."); }
            return name;
        }
        private static void BuildWorld(Request request, string output, BuildTarget target)
        {
            if (!string.IsNullOrEmpty(request.streamingSettingsPath))
            {
                if ((request.additionalScenes?.Length ?? 0) > 0)
                { throw new ArgumentException("Partitioned settings cannot silently omit additional scenes."); }
                var settings = JsonUtility.FromJson<StreamingWorldExporter.Settings>(File.ReadAllText(request.streamingSettingsPath));
                settings.target = target;
                settings.outputDirectory = output;
                var report = StreamingWorldExporter.Export(settings);
                File.WriteAllText(Path.Combine(output, "streaming-export.json"), JsonUtility.ToJson(report, true));
            }
            else
            {
                string[] scenes = new[] { request.scene }.Concat(request.additionalScenes ?? Array.Empty<string>()).Distinct().ToArray();
                if (scenes.Any(scene => AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null))
                { throw new ArgumentException("Select imported main and additional scene assets."); }
                // Explicit build map preserves existing importer bundle names and excludes other publishers.
                var built = BuildPipeline.BuildAssetBundles(output, new[] { new AssetBundleBuild {
                    assetBundleName = "world_base", assetNames = scenes } },
                    BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
                if (!built) { throw new InvalidOperationException("World bundle build failed."); }
                string file = Path.Combine(output, "world_base");
                if (!BuildPipeline.GetCRCForAssetBundle(file, out uint crc)) { throw new InvalidOperationException("World CRC missing."); }
                var manifest = new WorldStreamingManifest { target = target.ToString(), baseBundle = "world_base", bundles = new[] {
                    new StreamingBundle { name = "world_base", file = "world_base", scene = request.scene,
                        additionalScenes = scenes.Skip(1).ToArray(),
                        bytes = new FileInfo(file).Length, crc = crc, hash = built.GetAssetBundleHash("world_base").ToString(),
                        dependencies = built.GetAllDependencies("world_base") } } };
                manifest.Validate(target.ToString());
                File.WriteAllText(Path.Combine(output, "world.streaming.json"), JsonUtility.ToJson(manifest, true));
            }
            StreamingWorldReleaseValidator.Validate(output, target.ToString());
        }
        private static UnityEngine.Object Asset(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (!asset) { throw new ArgumentException("Missing source asset: " + path); }
            return asset;
        }
        private static void BuildCatalog(Request request, string output, BuildTarget target)
        {
            if (string.IsNullOrEmpty(request.releaseId) || !Uri.TryCreate(request.contentBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            { throw new ArgumentException("Catalog needs a release identity and final HTTPS content base URL."); }
            AddressablesCatalogBuilder builder;
            if (request.kind == "catalog")
            {
                var avatarBuilder = new AvatarCatalogBuilder();
                avatarBuilder.SetBuildIdentity(request.releaseId + request.platform);
                avatarBuilder.SetRemoteLoadPath(request.contentBaseUrl);
                avatarBuilder.SetupAssetGroup(request.avatars.Select(p => Asset(p) as GameObject ?? throw new ArgumentException("Avatar must be a prefab.")).ToList(),
                    request.cosmetics.Select(c => new AvatarCatalogBuilder.CosmeticEntry(Asset(c.assetPath), c.slotId)).ToList());
                builder = avatarBuilder;
            }
            else
            {
                var pack = new AssetPackCatalogBuilder();
                pack.SetBuildIdentity(request.releaseId + request.platform);
                pack.SetRemoteLoadPath(request.contentBaseUrl);
                pack.SetupAssetGroup(request.assets.Select(a => new AssetPackCatalogBuilder.AssetPackEntry { Asset = Asset(a.assetPath), AssetKey = a.assetKey }).ToList());
                builder = pack;
            }
            // Separate output per platform; never clean the creator's existing ContentData exports.
            var settings = builder.GetOrCreateSettings();
            settings.profileSettings.SetValue(settings.activeProfileId, UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.kRemoteBuildPath, output);
            var result = builder.BuildForTarget(target);
            if (!result.Success) { throw new InvalidOperationException(result.Error); }
            foreach (string extension in new[] { ".bin", ".hash" })
            {
                string canonical = Path.Combine(output,"content_catalog" + extension);
                if (File.Exists(canonical)) { continue; }
                var candidates = Directory.GetFiles(output,"*" + extension);
                if (candidates.Length != 1) { throw new InvalidOperationException("Expected exactly one catalog " + extension + "."); }
                File.Move(candidates[0],canonical);
            }
            if (!File.Exists(Path.Combine(output, "content_catalog.bin")) || !File.Exists(Path.Combine(output, "content_catalog.hash")))
            { throw new InvalidOperationException("Addressables catalog .bin/.hash missing."); }
        }
    }
}
