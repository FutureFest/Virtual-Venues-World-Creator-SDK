using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace VirtualVenues.Editor.Publishing
{
    /// <summary>
    /// Shared base for building an isolated Addressables remote catalog for a VV SDK publisher (avatars,
    /// asset packs, ...). Holds all the common machinery — isolated AddressableAssetSettings, profile,
    /// remote build/load paths, the WebGL-target catalog build (with global-default + build-target
    /// snapshot/restore), and output scanning — and leaves the per-publisher specifics (which settings
    /// asset + group + profile to use, and how prefabs are added/labelled) to subclasses.
    ///
    /// Each subclass uses its OWN settings asset + group so publishers never build each other's content.
    /// Editor-only. Extracted from the original avatar-only AddressablesBuildManager (behaviour-preserving
    /// for the avatar path, which now delegates here via <see cref="AvatarCatalogBuilder"/>).
    /// </summary>
    public abstract class AddressablesCatalogBuilder
    {
        protected const string SettingsFolder = "Assets/VirtualVenues/Addressables";
        protected const string RemoteBuildPath = "ContentData";
        public const string BucketUrl = "https://ff-desktop-clients.s3.amazonaws.com";

        /// <summary>Name of the isolated AddressableAssetSettings asset this publisher owns.</summary>
        protected abstract string SettingsAssetName { get; }

        /// <summary>Name of the Addressables group this publisher's assets live in.</summary>
        protected abstract string GroupName { get; }

        /// <summary>Name of the Addressables profile this publisher uses.</summary>
        protected abstract string ProfileName { get; }

        private AddressableAssetSettings _settings;

        // Sanitized per-publish versionId baked into the next build's bundle identity (null = legacy naming).
        private string _buildIdentity;

        /// <summary>
        /// Opt-in: makes the next build's bundles globally unique by recreating the group (fresh group
        /// GUID — the seed of the internal CAB serialized-file names via
        /// BuildScriptPackedMode.CalculateGroupHash) under a versionId-suffixed name, and giving the
        /// shared monoscript/built-in bundles per-publish Custom names. Without this, every publish from
        /// one project shares the same internal bundle ids, so Unity refuses to load a second pack/version
        /// in the same session ("another AssetBundle with the same files is already loaded").
        /// When never called, every code path behaves exactly as before (legacy fixed-name group).
        /// </summary>
        public void SetBuildIdentity(string versionId)
        {
            if (string.IsNullOrEmpty(versionId)) { throw new ArgumentException("versionId is required.", nameof(versionId)); }

            // Keep only [a-z0-9]: a UUID becomes 32 hex chars, safe in group names, bundle file names,
            // the filesystem, and S3 object names alike.
            string sanitized = new string(versionId.ToLowerInvariant()
                .Where(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z')).ToArray());
            if (sanitized.Length == 0) { throw new ArgumentException($"versionId '{versionId}' contains no usable characters.", nameof(versionId)); }

            _buildIdentity = sanitized;
        }

        // ---- settings / profile ---------------------------------------------------------------- //

        /// <summary>Gets or creates this publisher's isolated AddressableAssetSettings.</summary>
        public AddressableAssetSettings GetOrCreateSettings()
        {
            if (_settings != null) { return _settings; }

            string settingsPath = $"{SettingsFolder}/{SettingsAssetName}.asset";
            _settings = AssetDatabase.LoadAssetAtPath<AddressableAssetSettings>(settingsPath);
            if (_settings != null) { return _settings; }

            if (!Directory.Exists(SettingsFolder))
            {
                Directory.CreateDirectory(SettingsFolder);
                AssetDatabase.Refresh();
            }

            _settings = AddressableAssetSettings.Create(SettingsFolder, SettingsAssetName, false, true);
            _settings.BuildRemoteCatalog = true;
            _settings.RemoteCatalogBuildPath.SetVariableByName(_settings, AddressableAssetSettings.kRemoteBuildPath);
            _settings.RemoteCatalogLoadPath.SetVariableByName(_settings, AddressableAssetSettings.kRemoteLoadPath);

            SetupProfile(_settings);

            EditorUtility.SetDirty(_settings);
            AssetDatabase.SaveAssets();
            return _settings;
        }

        private void SetupProfile(AddressableAssetSettings settings)
        {
            string profileId = settings.profileSettings.GetProfileId(ProfileName);
            if (string.IsNullOrEmpty(profileId))
            {
                profileId = settings.profileSettings.AddProfile(ProfileName, settings.activeProfileId);
            }
            settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kRemoteBuildPath, RemoteBuildPath);
            settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kRemoteLoadPath, BucketUrl);
            settings.activeProfileId = profileId;
        }

        /// <summary>Sets the remote load path baked into the catalog to the given content base URL.</summary>
        public void SetRemoteLoadPath(string contentBaseUrl)
        {
            var settings = GetOrCreateSettings();
            settings.profileSettings.SetValue(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath, contentBaseUrl);
            EditorUtility.SetDirty(settings);
        }

        /// <summary>Clears the cached settings reference (re-fetched on next use).</summary>
        public void ClearSettingsCache()
        {
            _settings = null;
        }

        // ---- group helpers (used by subclasses' SetupAssetGroup) -------------------------------- //

        /// <summary>Get or create this publisher's group, configured for a remote LZ4 bundle, and clear its entries.</summary>
        protected AddressableAssetGroup GetOrCreateClearedGroup()
        {
            var settings = GetOrCreateSettings();

            AddressableAssetGroup group;
            if (string.IsNullOrEmpty(_buildIdentity))
            {
                // Legacy path (no SetBuildIdentity): reuse the fixed-name group exactly as before.
                group = settings.FindGroup(GroupName);
                if (group == null)
                {
                    group = settings.CreateGroup(GroupName, false, false, false, null,
                        typeof(ContentUpdateGroupSchema),
                        typeof(BundledAssetGroupSchema));
                }
            }
            else
            {
                // Per-publish identity: remove ALL of this publisher's groups (the pre-fix fixed-name
                // group and any "<GroupName>_<oldVersionId>" left by an aborted publish), then create a
                // fresh one. CreateGroup generates a NEW group GUID, which is what the SBP bundle id —
                // and therefore the internal CAB serialized-file names Unity de-dupes on — derives from;
                // the versionId suffix additionally lands in the output bundle file name for traceability.
                RemovePublisherGroups(settings);
                group = settings.CreateGroup($"{GroupName}_{_buildIdentity}", false, false, false, null,
                    typeof(ContentUpdateGroupSchema),
                    typeof(BundledAssetGroupSchema));
                // The build reads DefaultGroup's schema, and the removal above can leave that reference
                // stale; set it explicitly so Addressables' self-heal never creates a stray local group.
                settings.DefaultGroup = group;
                ApplyUniqueSharedBundleNames(settings);
            }

            var bundleSchema = group.GetSchema<BundledAssetGroupSchema>() ?? group.AddSchema<BundledAssetGroupSchema>();
            bundleSchema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            bundleSchema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
            bundleSchema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
            bundleSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            bundleSchema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.AppendHash;

            foreach (var entry in group.entries.ToList())
            {
                settings.RemoveAssetEntry(entry.guid);
            }
            return group;
        }

        /// <summary>Add one prefab to the group with the given Addressables address + optional label.</summary>
        protected void AddEntry(AddressableAssetGroup group, GameObject prefab, string address, string label)
        {
            AddEntry(group, (UnityEngine.Object)prefab, address, label);
        }

        /// <summary>
        /// Add any project asset (prefab, Texture2D, Cubemap, ...) to the group with the given
        /// Addressables address + optional label. Addressables entries are GUID-based, so the asset
        /// type only matters to the loader (<c>LoadAssetAsync&lt;T&gt;</c>), not to the group.
        /// </summary>
        protected void AddEntry(AddressableAssetGroup group, UnityEngine.Object asset, string address, string label)
        {
            if (asset == null) { return; }
            var settings = GetOrCreateSettings();
            string assetPath = AssetDatabase.GetAssetPath(asset);
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) { return; }

            var entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = address;
            if (!string.IsNullOrEmpty(label)) { entry.labels.Add(label); }
        }

        /// <summary>Persist group edits.</summary>
        protected void SaveGroup()
        {
            EditorUtility.SetDirty(GetOrCreateSettings());
            AssetDatabase.SaveAssets();
            // The save imports any group asset created above, which is when the Addressables
            // postprocessor adopts it into the GLOBAL DEFAULT settings — strip it right back out.
            if (RemoveOwnGroupsFromGlobalDefault() > 0) { AssetDatabase.SaveAssets(); }
        }

        /// <summary>
        /// Removes this publisher's groups (and null/dead references) from the GLOBAL DEFAULT settings'
        /// group list; returns how many were removed (caller persists). Unity's Addressables asset
        /// postprocessor (AddressableAssetSettings.OnPostprocessAllAssets) "adopts" every newly imported
        /// group asset into the default settings — it assumes a single-settings project — so each group
        /// created here leaks a foreign reference into the default, which NREs the next player build
        /// ("Failed to build Addressables content, content not included in Player Build"). Strips only
        /// groups owned by THIS publisher's isolated settings plus null entries — never a consumer's own
        /// groups. The postprocessor can also replay queued imports after a delayed registration, so the
        /// player-build-side self-heals (UweBuilder / Build Uploader) stay in place as the backstop.
        /// </summary>
        private int RemoveOwnGroupsFromGlobalDefault()
        {
            if (_settings == null) { return 0; }
            // SettingsExists guard: never force-create a default settings in projects that have none.
            if (!AddressableAssetSettingsDefaultObject.SettingsExists) { return 0; }

            AddressableAssetSettings def = AddressableAssetSettingsDefaultObject.Settings;
            if (def == null || def == _settings || def.groups == null) { return 0; }

            int removed = def.groups.RemoveAll(g => g == null || g.Settings == _settings);
            if (removed > 0)
            {
                EditorUtility.SetDirty(def);
                Debug.Log(
                    $"[{GetType().Name}] Removed {removed} group reference(s) the Addressables asset " +
                    "postprocessor adopted into the project default settings (publisher groups are isolated).");
            }
            return removed;
        }

        /// <summary>
        /// Remove every group this publisher owns. The build includes ALL groups in the settings
        /// (BuildScriptBase.ProcessAllGroups), so a stale group surviving here would leak its entries
        /// into the catalog — verify removal and abort the publish rather than build with one present.
        /// </summary>
        private void RemovePublisherGroups(AddressableAssetSettings settings)
        {
            foreach (var staleGroup in FindPublisherGroups(settings)) { settings.RemoveGroup(staleGroup); }
            if (FindPublisherGroups(settings).Count > 0)
            {
                throw new InvalidOperationException(
                    $"Could not remove the previous '{GroupName}' Addressables group(s); aborting the build to avoid stale content.");
            }
        }

        // Matches the pre-fix fixed-name group and any "<GroupName>_<versionId>" from a prior publish.
        // Each publisher owns its own settings asset (created with createDefaultGroups: false), so these
        // are the only groups that can exist here.
        private List<AddressableAssetGroup> FindPublisherGroups(AddressableAssetSettings settings)
        {
            return settings.groups
                .Where(g => g != null && (g.Name == GroupName || g.Name.StartsWith(GroupName + "_", StringComparison.Ordinal)))
                .ToList();
        }

        /// <summary>
        /// Give the build's shared bundles (monoscripts + Unity built-in/shaders) a per-publish Custom
        /// name. Their default ProjectName-hash names are identical for every publish from one project
        /// AND match the player build's own shared bundles — the other half of the
        /// "same files already loaded" collision.
        /// </summary>
        private void ApplyUniqueSharedBundleNames(AddressableAssetSettings settings)
        {
            string sharedName = $"{GroupName.ToLowerInvariant()}_{_buildIdentity}";
            settings.MonoScriptBundleNaming = MonoScriptBundleNaming.Custom;
            settings.MonoScriptBundleCustomNaming = sharedName; // never empty — an empty prefix suppresses the monoscript bundle
            settings.BuiltInBundleNaming = BuiltInBundleNaming.Custom;
            settings.BuiltInBundleCustomNaming = sharedName;
            EditorUtility.SetDirty(settings); // plain auto-setters above don't dirty the asset themselves
        }

        // ---- build ----------------------------------------------------------------------------- //

        /// <summary>
        /// Builds this publisher's Addressables catalog for the WebGL/WebGPU target. Switches the active
        /// build target to WebGL if needed (restored afterwards), temporarily makes the isolated settings
        /// the global default (restored / torn down afterwards), cleans, and builds. The caller is expected
        /// to have called <see cref="SetRemoteLoadPath"/> with the final content base URL first so bundle
        /// URLs bake correctly.
        /// </summary>
        public BuildResult BuildForWebGPU(IProgress<(float progress, string message)> progress = null)
        {
            BuildTarget originalTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup originalGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            bool switchedTarget = false;

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                progress?.Report((0.05f, "Switching to WebGL platform..."));
                bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
                if (!switched)
                {
                    return new BuildResult
                    {
                        Success = false,
                        Error = "Failed to switch the active build target to WebGL. " +
                                "Ensure Web Build Support is installed (Unity Hub > Add Modules), then try again.",
                        OutputPath = null
                    };
                }
                switchedTarget = true;
            }

            var settings = GetOrCreateSettings();
            progress?.Report((0.1f, "Preparing build..."));

            try
            {
                progress?.Report((0.15f, "Cleaning previous build output..."));
                CleanOutputFolder();

                progress?.Report((0.2f, "Cleaning build cache..."));
                AddressableAssetSettings.CleanPlayerContent(settings.ActivePlayerDataBuilder);

                progress?.Report((0.3f, "Building Addressables for WebGPU..."));

                // Build THIS publisher's isolated settings DIRECTLY, WITHOUT ever swapping the project-wide
                // AddressableAssetSettingsDefaultObject.Settings.
                //
                // The static AddressableAssetSettings.BuildPlayerContent() builds the GLOBAL DEFAULT settings,
                // which is why the old code temporarily swapped the default in and back out. That swap was the
                // root of two real bugs: (1) it polluted the project default — a publisher group that leaked
                // into the default's group list broke EVERY player build (UPC/UMS/UWE all auto-build the
                // default, and a foreign group there NREs the content build), and (2) it crashed on fresh
                // projects that had no default at all. Driving the active data builder with an input bound to
                // OUR settings reaches the exact same build path BuildPlayerContentImpl uses, minus the swap.
                // BuildScriptPackedMode reads input.AddressableSettings and never the global default, so the
                // publisher build is fully isolated: it can never touch the project default, its groups, or
                // any player build.
                var input = new AddressablesDataBuilderInput(settings);
                AddressablesPlayerBuildResult result =
                    settings.ActivePlayerDataBuilder.BuildData<AddressablesPlayerBuildResult>(input);

                // Match BuildPlayerContentImpl, which Refreshes after building so the freshly-written
                // ContentData files are visible to the subsequent GetBuildOutputFiles/copy reads.
                AssetDatabase.Refresh();

                if (result == null)
                {
                    return new BuildResult { Success = false, Error = "Addressables build returned no result.", OutputPath = null };
                }
                if (!string.IsNullOrEmpty(result.Error))
                {
                    Debug.LogError($"Addressables build failed: {result.Error}");
                    return new BuildResult { Success = false, Error = result.Error, OutputPath = null };
                }

                progress?.Report((0.9f, "Build complete!"));
                return new BuildResult
                {
                    Success = true,
                    Error = null,
                    OutputPath = GetBuildOutputPath(),
                    BundleCount = result.LocationCount,
                    BundleFilePaths = result.AssetBundleBuildResults != null
                        ? result.AssetBundleBuildResults.Select(r => Path.GetFullPath(r.FilePath)).ToList()
                        : new List<string>()
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"Addressables build exception: {ex.Message}");
                return new BuildResult { Success = false, Error = ex.Message, OutputPath = null };
            }
            finally
            {
                // The Refresh in the build path can import this publish's group asset, triggering the
                // postprocessor adoption (see RemoveOwnGroupsFromGlobalDefault) — strip before leaving so
                // a publish can never break the next player build.
                try
                {
                    if (RemoveOwnGroupsFromGlobalDefault() > 0) { AssetDatabase.SaveAssets(); }
                }
                catch (Exception cleanupEx)
                {
                    Debug.LogError($"Failed to clean leaked publisher groups from the default Addressables settings: {cleanupEx.Message}");
                }

                // Only the active build target is touched now (no global-default swap to restore).
                try
                {
                    if (switchedTarget && EditorUserBuildSettings.activeBuildTarget != originalTarget)
                    {
                        EditorUserBuildSettings.SwitchActiveBuildTarget(originalGroup, originalTarget);
                    }
                }
                catch (Exception targetEx)
                {
                    Debug.LogError($"Failed to restore build target to {originalTarget}: {targetEx.Message}");
                }
            }
        }

        /// <summary>
        /// Delete stale build outputs (top-level .bundle/.bin/.hash/.json) from the shared output
        /// folder before building. With per-publish-unique bundle names, a stale file surviving here
        /// would be scanned by <see cref="GetBuildOutputFiles"/> and uploaded under the wrong
        /// pack/catalog version — so a failed delete ABORTS the build instead of being best-effort.
        /// </summary>
        private void CleanOutputFolder()
        {
            string outputPath = GetBuildOutputPath();
            if (string.IsNullOrEmpty(outputPath) || !Directory.Exists(outputPath)) { return; }

            foreach (string file in Directory.GetFiles(outputPath, "*", SearchOption.TopDirectoryOnly))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                string name = Path.GetFileName(file);
                if (ext != ".bundle" && !name.EndsWith(".bin") && !name.EndsWith(".hash") && ext != ".json") { continue; }

                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    throw new IOException(
                        $"Could not delete stale build output '{file}' — close anything locking it and retry.", ex);
                }
            }
        }

        /// <summary>Resolves the absolute build output folder (the profile's remote build path).</summary>
        public string GetBuildOutputPath()
        {
            var settings = GetOrCreateSettings();
            string buildPath = settings.profileSettings.GetValueByName(settings.activeProfileId, AddressableAssetSettings.kRemoteBuildPath);
            if (!Path.IsPathRooted(buildPath))
            {
                buildPath = Path.Combine(Directory.GetCurrentDirectory(), buildPath);
            }
            return buildPath;
        }

        /// <summary>Scans the build output folder and returns the catalog + bundle files.</summary>
        public List<BuildOutputFile> GetBuildOutputFiles()
        {
            var files = new List<BuildOutputFile>();
            string outputPath = GetBuildOutputPath();
            if (!Directory.Exists(outputPath)) { return files; }

            foreach (var filePath in Directory.GetFiles(outputPath, "*", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(filePath);
                string extension = Path.GetExtension(filePath).ToLower();
                if (extension == ".meta" || extension == ".manifest") { continue; }

                var fileInfo = new FileInfo(filePath);
                files.Add(new BuildOutputFile
                {
                    FileName = fileName,
                    FilePath = filePath,
                    FileSize = fileInfo.Length,
                    IsCatalog = fileName.EndsWith(".bin") || fileName.EndsWith(".hash"),
                    IsBundle = extension == ".bundle"
                });
            }
            return files;
        }

        // ---- static helpers -------------------------------------------------------------------- //

        /// <summary>Builds the user-scoped content base URL for a catalog version.</summary>
        public static string BuildContentBaseUrl(string bucketUrl, string userId, string catalogId, string versionId)
        {
            string safeUserId = Uri.EscapeDataString(userId);
            return $"{bucketUrl}/users/{safeUserId}/catalogs/{catalogId}/versions/{versionId}";
        }

        /// <summary>Generates a unique version id (UUID v4).</summary>
        public static string GenerateVersionId()
        {
            return Guid.NewGuid().ToString();
        }

        // ---- result DTOs ----------------------------------------------------------------------- //

        public class BuildResult
        {
            public bool Success;
            public string Error;
            public string OutputPath;
            public int BundleCount;

            /// <summary>Absolute paths of the bundles THIS build produced — upload exactly these, nothing stale.</summary>
            public List<string> BundleFilePaths;
        }

        public class BuildOutputFile
        {
            public string FileName;
            public string FilePath;
            public long FileSize;
            public bool IsCatalog;
            public bool IsBundle;
        }
    }
}
