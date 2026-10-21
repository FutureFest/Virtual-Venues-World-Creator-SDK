using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Auth0;
using UnityEditor;
using UnityEngine;
using VirtualVenues.Plugins.Nmkr.Editor;

namespace WorldPublisher
{
    public enum WorldPublishState { Idle, Running, Succeeded, Failed }

    [Serializable]
    public class WorldPublishStatus
    {
        public WorldPublishState state;
        public string message;
        public float progress; // 0..1
        public string worldId;
        public string worldName;
        public string umsPath;
        public string upcPath;

        public override string ToString() =>
            $"{state} {progress:P0} {message}" +
            (string.IsNullOrEmpty(worldId) ? "" : $" worldId={worldId}") +
            (string.IsNullOrEmpty(upcPath) ? "" : $" ums={umsPath} upc={upcPath}");
    }

    /// <summary>
    /// Builds a world (UMS Linux + UPC WebGL scene bundles) and optionally uploads it, with no window.
    /// Each platform switch reloads the domain, so state lives in SessionState and the job re-hooks itself
    /// after every reload. Callers start it and poll <see cref="Status"/> (or listen to <see cref="Changed"/>).
    /// </summary>
    [InitializeOnLoad]
    public static class WorldPublishJob
    {
        public const string OutputFolder = "Assets/WorldMapAssetBundles";
        private const string JOB_KEY = "VV.WorldPublishJob.Job";
        private const string STATUS_KEY = "VV.WorldPublishJob.Status";
        private const string VERSION_KEY = "WorldMapVersion_"; // EditorPrefs key the window used; keeps numbering continuous
        private const double RELOAD_TIMEOUT_SECONDS = 120;
        private const int STEP_UPLOADING = 6;
        private const int STEP_MOBILE = 7;

        [Serializable]
        private class Job
        {
            public List<string> scenes = new List<string>(); // main scene first
            public string worldName;
            public bool upload;
            public bool includeMobile;
            public string mobileOutput, iosReleaseId;
            public int step;
            public string bundleBase;
            public string umsPath;
            public string upcPath;
            public string thumbnailPath; // JPEG in Temp/, optional
            public BuildTarget originalTarget;
            public BuildTargetGroup originalGroup;
            public bool awaitingReload;
            public double switchTime;
        }

        public static event Action<WorldPublishStatus> Changed;

        public static WorldPublishStatus Status
        {
            get
            {
                string json = SessionState.GetString(STATUS_KEY, "");
                return string.IsNullOrEmpty(json) ? new WorldPublishStatus() : JsonUtility.FromJson<WorldPublishStatus>(json);
            }
        }

        public static bool IsRunning => Status.state == WorldPublishState.Running;

        static WorldPublishJob()
        {
            if (!IsRunning) { return; }
            Job job = LoadJob();
            if (job == null) { Fail("Publish state was lost."); return; }
            if (job.step == STEP_UPLOADING || job.step == STEP_MOBILE)
            {
                Cleanup(job);
                Fail("Upload was interrupted by a script reload. Publish again.");
                return;
            }
            // A reload after a platform switch is what the build step waits for.
            job.awaitingReload = false;
            SaveJob(job);
            RestoreToken();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        /// <summary>
        /// Starts a publish. Throws if a precondition fails (bad scene, compile errors, NMKR config, not signed in).
        /// upload:false builds both bundles and stops — no sign-in needed.
        /// thumbnailPath: optional JPEG (see <see cref="WorldThumbnail"/>) uploaded with the world; the job deletes it when done.
        /// </summary>
        public static void Start(string mainScenePath, IList<string> additionalScenePaths, string worldName, bool upload = true, bool includeMobile = false, string thumbnailPath = null)
        {
            if (IsRunning || VirtualVenues.Editor.Publishing.PlatformPublisherProcess.IsRunning || (VirtualVenues.Editor.Publishing.PlatformPublisherProcess.ExternalBuildRunning?.Invoke() ?? false))
            { throw new InvalidOperationException("Wait for the current build or publishing operation to finish."); }
            if (string.IsNullOrEmpty(mainScenePath) || !mainScenePath.EndsWith(".unity") || AssetDatabase.LoadAssetAtPath<SceneAsset>(mainScenePath) == null)
            {
                throw new ArgumentException($"Not a scene asset: {mainScenePath}");
            }
            if (EditorUtility.scriptCompilationFailed) { throw new InvalidOperationException("Scripts have compile errors. Fix the Console errors before publishing."); }

            worldName = worldName?.Trim();
            if (upload && string.IsNullOrEmpty(worldName)) { throw new ArgumentException("A world name is required to upload."); }
            if (worldName != null && worldName.Length > 100) { throw new ArgumentException("World name must be 100 characters or less."); }

            var scenes = new List<string> { mainScenePath };
            foreach (string path in additionalScenePaths ?? Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".unity") && !scenes.Contains(path)) { scenes.Add(path); }
            }

            foreach (string scenePath in scenes)
            {
                WorldNmkrValidator.Result nmkr = WorldNmkrValidator.Validate(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath));
                if (!nmkr.Ok)
                {
                    throw new InvalidOperationException(
                        $"NMKR issues in {Path.GetFileNameWithoutExtension(scenePath)} must be fixed before publishing:\n- " + string.Join("\n- ", nmkr.Errors));
                }
            }

            if (upload)
            {
                RestoreToken();
                if (!WorldPublisherApi.IsTokenValid)
                {
                    throw new InvalidOperationException("Not signed in or the session expired. Sign in once through VirtualVenues > World Publisher.");
                }
            }

            var job = new Job
            {
                scenes = scenes,
                worldName = worldName,
                upload = upload,
                includeMobile = includeMobile,
                thumbnailPath = thumbnailPath,
                bundleBase = GenerateVersionedBundleName(mainScenePath),
                originalTarget = EditorUserBuildSettings.activeBuildTarget,
                originalGroup = EditorUserBuildSettings.selectedBuildTargetGroup,
            };
            SaveJob(job);
            SetStatus(new WorldPublishStatus { state = WorldPublishState.Running, message = "Initializing publishing...", worldName = worldName });
            Debug.Log($"[WorldPublishJob] Started {job.bundleBase} (upload={upload}); original target {job.originalGroup}/{job.originalTarget}");

            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        // Each platform is a switch step then a build step: the build waits (across ticks and the domain reload the
        // switch causes) until scripts are compiled for that platform.
        private static void Tick()
        {
            Job job = LoadJob();
            if (job == null || !IsRunning) { EditorApplication.update -= Tick; return; }

            try
            {
                switch (job.step)
                {
                    case 0:
                        if (job.includeMobile && string.IsNullOrEmpty(job.iosReleaseId))
                        { StartMobile(job); return; }
                        Progress(0.2f, "Switching to Linux platform...");
                        SwitchTo(job, BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64);
                        job.step++;
                        break;
                    case 1:
                        if (!ReadyToBuild(job, BuildTarget.StandaloneLinux64)) { return; }
                        Progress(0.25f, "Building UMS asset bundle for Linux...");
                        job.umsPath = BuildBundle(job, BuildTarget.StandaloneLinux64, "ums", "UMS");
                        job.step++;
                        break;
                    case 2:
                        Progress(0.4f, "Switching to WebGL platform...");
                        SwitchTo(job, BuildTargetGroup.WebGL, BuildTarget.WebGL);
                        job.step++;
                        break;
                    case 3:
                        if (!ReadyToBuild(job, BuildTarget.WebGL)) { return; }
                        Progress(0.45f, "Building UPC asset bundle for WebGL...");
                        job.upcPath = BuildBundle(job, BuildTarget.WebGL, "upc", "UPC");
                        job.step++;
                        break;
                    case 4:
                        Progress(0.5f, "Refreshing AssetDatabase...");
                        AssetDatabase.Refresh();
                        job.step++;
                        break;
                    case 5:
                        EditorApplication.update -= Tick;
                        if (!job.upload)
                        {
                            Cleanup(job);
                            Succeed(job, null, "Build complete (not uploaded).");
                            return;
                        }
                        job.step = STEP_UPLOADING;
                        SaveJob(job);
                        Progress(0.6f, "Starting upload to cloud...");
                        _ = UploadAsync(job);
                        return;
                }
                SaveJob(job);
            }
            catch (Exception ex)
            {
                EditorApplication.update -= Tick;
                Cleanup(job);
                Fail($"Publishing failed at step {job.step}:\n\n{ex.Message}");
            }
        }

        [Serializable] private class MobileRequest
        {
            public string kind = "world", scene, assetId, version;
            public string[] additionalScenes;
        }
        [Serializable] private class MobileResult { public string releaseId; public bool published; }
        private static void StartMobile(Job job)
        {
            string id = Guid.NewGuid().ToString();
            string folder = Path.GetFullPath(Path.Combine("Builds", "WorldPublishing", id));
            Directory.CreateDirectory(folder);
            string request = Path.Combine(folder, "world-request.json");
            File.WriteAllText(request, JsonUtility.ToJson(new MobileRequest {
                scene = job.scenes[0], additionalScenes = job.scenes.Skip(1).ToArray(), assetId = id, version = id }, true));
            job.mobileOutput = Path.Combine(folder, "mobile");
            job.step = STEP_MOBILE;
            SaveJob(job);
            EditorApplication.update -= Tick;
            Progress(0.05f, "Building mobile world in an isolated iOS worker...");
            VirtualVenues.Editor.Publishing.PlatformPublisherProcess.Changed += OnMobileChanged;
            try
            {
                var q = new Func<string, string>(VirtualVenues.Editor.Publishing.PlatformPublisherProcess.Quote);
                string args = "world-mobile --source " + q(Path.GetFullPath(".")) + " --sdk-root " + q(VirtualVenues.Editor.Publishing.PlatformPublisherProcess.SDKRoot)
                    + " --job " + q(request) + " --output " + q(job.mobileOutput) + " --unity " + q(EditorApplication.applicationPath);
                if (job.upload) { args += " --publish --api " + q(WorldPublisherApi.BaseUrl); }
                VirtualVenues.Editor.Publishing.PlatformPublisherProcess.StartForWorldJob(args, job.upload);
            }
            catch { VirtualVenues.Editor.Publishing.PlatformPublisherProcess.Changed -= OnMobileChanged; throw; }
        }
        private static void OnMobileChanged()
        {
            if (VirtualVenues.Editor.Publishing.PlatformPublisherProcess.IsRunning) { return; }
            VirtualVenues.Editor.Publishing.PlatformPublisherProcess.Changed -= OnMobileChanged;
            Job job = LoadJob();
            if (job == null || job.step != STEP_MOBILE) { return; }
            try
            {
                if (!VirtualVenues.Editor.Publishing.PlatformPublisherProcess.Status.StartsWith("Completed.", StringComparison.Ordinal))
                { throw new InvalidOperationException(VirtualVenues.Editor.Publishing.PlatformPublisherProcess.Status); }
                var result = JsonUtility.FromJson<MobileResult>(File.ReadAllText(Path.Combine(job.mobileOutput, "world-result.json")));
                if (string.IsNullOrEmpty(result.releaseId) || result.published != job.upload)
                { throw new InvalidOperationException("Mobile world build/publication was incomplete."); }
                job.iosReleaseId = result.releaseId;
                job.step = 0;
                SaveJob(job);
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Cleanup(job); Fail("Mobile publishing failed: " + error.Message); }
        }

        private static void SwitchTo(Job job, BuildTargetGroup group, BuildTarget target)
        {
            if (EditorUserBuildSettings.activeBuildTarget == target) { return; }

            Debug.Log($"[WorldPublishJob] Switching build target to {target}...");
            // Saved before switching: the static ctor clears it after the reload.
            job.awaitingReload = true;
            job.switchTime = EditorApplication.timeSinceStartup;
            SaveJob(job);
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
            {
                job.awaitingReload = false;
                throw new Exception(
                    $"Failed to switch build target to {target}.\n\n" +
                    "Install its Build Support module via Unity Hub -> Installs -> Add Modules.");
            }
        }

        // Building before the recompile + domain reload finishes makes the player serialization layout differ from the
        // editor's (fields inside #if UNITY_WEBGL etc.) and the bundle build fails.
        private static bool ReadyToBuild(Job job, BuildTarget target)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { return false; }
            if (EditorUtility.scriptCompilationFailed) { throw new Exception($"Scripts failed to compile for {target}. Fix the Console errors and publish again."); }
            if (job.awaitingReload)
            {
                // ponytail: assumes every target switch recompiles + reloads; the timeout covers a switch that doesn't.
                if (EditorApplication.timeSinceStartup - job.switchTime < RELOAD_TIMEOUT_SECONDS) { return false; }
                Debug.LogWarning($"No script reload seen {RELOAD_TIMEOUT_SECONDS}s after switching to {target}; building anyway.");
                job.awaitingReload = false;
            }
            if (EditorUserBuildSettings.activeBuildTarget != target) { throw new Exception($"Active build target is {EditorUserBuildSettings.activeBuildTarget}, expected {target}."); }
            return true;
        }

        // Builds the world bundle for one platform and returns its file path.
        private static string BuildBundle(Job job, BuildTarget target, string prefix, string subfolder)
        {
            string bundleName = $"world_{prefix}_{job.bundleBase}";
            AssignBundleName(job.scenes, bundleName);

            string outputFolder = Path.Combine(OutputFolder, subfolder);
            Directory.CreateDirectory(outputFolder);

            Debug.Log($"[WorldPublishJob] Building asset bundles for {target}...");
            var errors = new List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception) { errors.Add(message); }
            };
            AssetBundleManifest manifest;
            Application.logMessageReceived += capture;
            try
            {
                manifest = BuildPipeline.BuildAssetBundles(outputFolder, BuildAssetBundleOptions.None, target);
            }
            finally
            {
                Application.logMessageReceived -= capture;
            }

            if (manifest == null)
            {
                throw new Exception($"{subfolder} bundle build failed for {target}.\n\n{DescribeBuildErrors(errors, target)}");
            }

            string fileName = bundleName.ToLower();
            string filePath = Path.Combine(outputFolder, fileName);
            if (!File.Exists(filePath))
            {
                string[] createdBundles = Directory.GetFiles(outputFolder, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => !f.EndsWith(".manifest") && !f.EndsWith(".meta"))
                    .Select(Path.GetFileName)
                    .ToArray();

                throw new FileNotFoundException(
                    $"{subfolder} bundle build completed but expected file was not created.\n\n" +
                    $"Expected file: {filePath}\n\n" +
                    $"Bundles found in directory:\n{string.Join("\n", createdBundles)}\n\n" +
                    "This may indicate:\n" +
                    "- Scene is empty (no content to bundle)\n" +
                    "- File naming mismatch\n" +
                    "- Build completed with warnings that prevented bundle creation"
                );
            }

            Debug.Log($"[WorldPublishJob] {subfolder} bundle created: {fileName} ({new FileInfo(filePath).Length / 1024} KB)");
            return filePath;
        }

        private static string DescribeBuildErrors(List<string> errors, BuildTarget target)
        {
            if (errors.Any(e => e.Contains("script class layout is incompatible") || e.Contains("has an extra field")))
            {
                return $"The editor's scripts were not compiled for {target} (script class layout mismatch).\n" +
                       "Publish again. If it repeats, look for [SerializeField] fields inside platform #if blocks.";
            }
            if (errors.Count > 0) { return $"First error:\n{errors[0]}\n\nSee the Console for the full log."; }
            return "No error was logged. Make sure the scene has content, then check the Console.";
        }

        // Every published scene shares one bundle. The loader makes the main scene active by matching its file name
        // against the bundle name ("world_upc_<main scene>_<yyMMdd>_<vv>"), so the naming convention is load-bearing.
        private static void AssignBundleName(List<string> scenes, string bundleName)
        {
            foreach (string path in scenes)
            {
                AssetImporter importer = AssetImporter.GetAtPath(path);
                if (importer == null)
                {
                    throw new Exception($"Failed to get AssetImporter for scene: {path}. The scene may not be properly imported.");
                }
                importer.assetBundleName = bundleName;
            }
        }

        // BuildAssetBundles builds every named asset in the project, so names must not outlive a publish.
        private static void ClearBundleNames(List<string> scenes)
        {
            foreach (string path in scenes)
            {
                AssetImporter importer = AssetImporter.GetAtPath(path);
                if (importer != null && !string.IsNullOrEmpty(importer.assetBundleName)) { importer.assetBundleName = string.Empty; }
            }
            AssetDatabase.RemoveUnusedAssetBundleNames();
        }

        private static async Task UploadAsync(Job job)
        {
            try
            {
                if (!File.Exists(job.umsPath)) { throw new FileNotFoundException($"UMS bundle not found: {job.umsPath}"); }
                if (!File.Exists(job.upcPath)) { throw new FileNotFoundException($"UPC bundle not found: {job.upcPath}"); }

                byte[] umsData = await Task.Run(() => File.ReadAllBytes(job.umsPath));
                byte[] upcData = await Task.Run(() => File.ReadAllBytes(job.upcPath));
                byte[] thumbnail = File.Exists(job.thumbnailPath) ? await Task.Run(() => File.ReadAllBytes(job.thumbnailPath)) : null;

                var progress = new Progress<string>(message => Progress(0.6f + UploadProgress(message) * 0.4f, message));
                World world = await WorldPublisherApi.UploadWorldAsync(
                    Path.GetFileName(job.umsPath), Path.GetFileName(job.upcPath), umsData, upcData, job.worldName, progress, job.iosReleaseId, thumbnail);

                Cleanup(job);
                Succeed(job, world, "Publishing Complete!");
            }
            catch (Exception ex)
            {
                Cleanup(job);
                Fail($"Upload failed: {ex.Message}");
            }
        }

        private static float UploadProgress(string message)
        {
            if (message.Contains("Requesting upload URLs")) { return 0.1f; }
            if (message.Contains("Uploading UMS")) { return 0.3f; }
            if (message.Contains("Uploading UPC")) { return 0.6f; }
            if (message.Contains("Uploading thumbnail")) { return 0.7f; }
            if (message.Contains("Confirming upload")) { return 0.8f; }
            if (message.Contains("Fetching world info")) { return 0.9f; }
            return 1.0f;
        }

        private static void Cleanup(Job job)
        {
            SessionState.EraseString(JOB_KEY);
            ClearBundleNames(job.scenes);
            if (File.Exists(job.thumbnailPath)) { File.Delete(job.thumbnailPath); }
            if (EditorUserBuildSettings.activeBuildTarget != job.originalTarget)
            {
                Debug.Log($"[WorldPublishJob] Restoring original build target: {job.originalGroup}/{job.originalTarget}");
                EditorUserBuildSettings.SwitchActiveBuildTarget(job.originalGroup, job.originalTarget);
            }
        }

        private static void Succeed(Job job, World world, string message)
        {
            var status = new WorldPublishStatus
            {
                state = WorldPublishState.Succeeded,
                progress = 1f,
                message = message,
                worldId = world?.worldId,
                worldName = world?.worldName ?? job.worldName,
                umsPath = job.umsPath,
                upcPath = job.upcPath,
            };
            Debug.Log($"[WorldPublishJob] SUCCEEDED {status}");
            SetStatus(status);
        }

        private static void Fail(string message)
        {
            SessionState.EraseString(JOB_KEY);
            Debug.LogError($"[WorldPublishJob] FAILED: {message}");
            SetStatus(new WorldPublishStatus { state = WorldPublishState.Failed, message = message });
        }

        private static void Progress(float value, string message)
        {
            WorldPublishStatus status = Status;
            status.progress = value;
            status.message = message;
            SetStatus(status);
        }

        private static void SetStatus(WorldPublishStatus status)
        {
            SessionState.SetString(STATUS_KEY, JsonUtility.ToJson(status));
            Changed?.Invoke(status);
        }

        private static Job LoadJob()
        {
            string json = SessionState.GetString(JOB_KEY, "");
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Job>(json);
        }

        private static void SaveJob(Job job) => SessionState.SetString(JOB_KEY, JsonUtility.ToJson(job));

        // EditorAuthToken is a plain static, so every domain reload wipes it; refill it from the cached sign-in.
        private static void RestoreToken()
        {
            if (WorldPublisherApi.IsTokenValid) { return; }
            if (AuthManager.Instance.Credentials.TryGetCachedCredentials(out var cached))
            {
                WorldPublisherApi.SetAccessToken(cached.AccessToken, cached.ExpiresAt);
            }
        }

        private static string GenerateVersionedBundleName(string assetPath)
        {
            string sceneName = Path.GetFileNameWithoutExtension(assetPath);
            string date = DateTime.Now.ToString("yyMMdd");

            string versionKey = VERSION_KEY + sceneName;
            string dateKey = versionKey + "_date";

            int newVersion = EditorPrefs.GetString(dateKey, "") != date ? 0 : EditorPrefs.GetInt(versionKey, -1) + 1;
            if (newVersion > 99) { newVersion = 0; }

            EditorPrefs.SetInt(versionKey, newVersion);
            EditorPrefs.SetString(dateKey, date);
            return $"{sceneName}_{date}_{newVersion:D2}";
        }
    }
}
