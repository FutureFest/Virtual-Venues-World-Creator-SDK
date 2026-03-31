using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace VirtualVenues.Editor.AvatarPublisher
{
    /// <summary>
    /// Manages isolated Addressables configuration and building for Avatar Publisher.
    /// Creates SDK-specific settings that won't conflict with user's existing Addressables setup.
    /// </summary>
    public static class AddressablesBuildManager
    {
        private const string SETTINGS_FOLDER = "Assets/VirtualVenues/Addressables";
        private const string SETTINGS_ASSET_NAME = "VVSDKAddressableSettings";
        private const string GROUP_NAME = "VVSDKAvatars";
        private const string PROFILE_NAME = "VVSDKProfile";

        private const string REMOTE_BUILD_PATH = "ContentData";
        internal const string BUCKET_URL = "https://ff-desktop-clients.s3.amazonaws.com";

        private static AddressableAssetSettings _settings;

        /// <summary>
        /// Gets or creates the isolated AddressableAssetSettings for VVSDK.
        /// </summary>
        public static AddressableAssetSettings GetOrCreateSettings()
        {
            if (_settings != null) { return _settings; }

            string settingsPath = $"{SETTINGS_FOLDER}/{SETTINGS_ASSET_NAME}.asset";

            // Try to load existing settings
            _settings = AssetDatabase.LoadAssetAtPath<AddressableAssetSettings>(settingsPath);
            if (_settings != null) { return _settings; }

            // Create directory if it doesn't exist
            if (!Directory.Exists(SETTINGS_FOLDER))
            {
                Directory.CreateDirectory(SETTINGS_FOLDER);
                AssetDatabase.Refresh();
            }

            // Create new settings
            _settings = AddressableAssetSettings.Create(SETTINGS_FOLDER, SETTINGS_ASSET_NAME, false, true);

            // Configure settings
            _settings.BuildRemoteCatalog = true;
            _settings.RemoteCatalogBuildPath.SetVariableByName(_settings, AddressableAssetSettings.kRemoteBuildPath);
            _settings.RemoteCatalogLoadPath.SetVariableByName(_settings, AddressableAssetSettings.kRemoteLoadPath);

            // Create or get profile
            SetupProfile(_settings);

            EditorUtility.SetDirty(_settings);
            AssetDatabase.SaveAssets();

            return _settings;
        }

        private static void SetupProfile(AddressableAssetSettings settings)
        {
            string profileId = settings.profileSettings.GetProfileId(PROFILE_NAME);

            if (string.IsNullOrEmpty(profileId))
            {
                profileId = settings.profileSettings.AddProfile(PROFILE_NAME, settings.activeProfileId);
            }

            // Set profile variables
            settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kRemoteBuildPath, REMOTE_BUILD_PATH);
            settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kRemoteLoadPath, BUCKET_URL);

            settings.activeProfileId = profileId;
        }

        /// <summary>
        /// Sets the remote load path to the given content base URL.
        /// </summary>
        public static void SetRemoteLoadPath(string contentBaseUrl)
        {
            var settings = GetOrCreateSettings();
            settings.profileSettings.SetValue(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath, contentBaseUrl);
            EditorUtility.SetDirty(settings);
        }

        /// <summary>
        /// Builds the content base URL from user-scoped catalog path components.
        /// </summary>
        public static string BuildContentBaseUrl(string bucketUrl, string userId, string catalogId, string versionId)
        {
            return $"{bucketUrl}/users/{userId}/catalogs/{catalogId}/versions/{versionId}";
        }

        /// <summary>
        /// Generates a unique version ID (UUID v4) for a new catalog version.
        /// </summary>
        public static string GenerateVersionId()
        {
            return Guid.NewGuid().ToString();
        }

        /// <summary>
        /// Sets up the asset group with the provided avatar and cosmetic prefabs.
        /// </summary>
        public static void SetupAssetGroup(List<GameObject> avatarPrefabs, List<GameObject> cosmeticPrefabs)
        {
            var settings = GetOrCreateSettings();

            // Find or create the group
            var group = settings.FindGroup(GROUP_NAME);
            if (group == null)
            {
                group = settings.CreateGroup(GROUP_NAME, false, false, false, null,
                    typeof(ContentUpdateGroupSchema),
                    typeof(BundledAssetGroupSchema));
            }

            // Configure group schema
            var bundleSchema = group.GetSchema<BundledAssetGroupSchema>();
            if (bundleSchema == null)
            {
                bundleSchema = group.AddSchema<BundledAssetGroupSchema>();
            }

            bundleSchema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            bundleSchema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
            bundleSchema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
            bundleSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            bundleSchema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.AppendHash;

            // Clear existing entries from group
            var existingEntries = group.entries.ToList();
            foreach (var entry in existingEntries)
            {
                settings.RemoveAssetEntry(entry.guid);
            }

            // Add avatar prefabs
            foreach (var prefab in avatarPrefabs)
            {
                if (prefab == null) { continue; }
                AddPrefabToGroup(settings, group, prefab, "Avatar");
            }

            // Add cosmetic prefabs
            foreach (var prefab in cosmeticPrefabs)
            {
                if (prefab == null) { continue; }
                AddPrefabToGroup(settings, group, prefab, "Cosmetic");
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static void AddPrefabToGroup(AddressableAssetSettings settings, AddressableAssetGroup group, GameObject prefab, string labelPrefix)
        {
            string assetPath = AssetDatabase.GetAssetPath(prefab);
            string guid = AssetDatabase.AssetPathToGUID(assetPath);

            if (string.IsNullOrEmpty(guid)) { return; }

            var entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = prefab.name;

            // Add label for type identification
            string label = $"{labelPrefix}_{prefab.name}";
            entry.labels.Add(label);
        }

        /// <summary>
        /// Builds Addressables for WebGPU target.
        /// </summary>
        public static BuildResult BuildForWebGPU(IProgress<(float progress, string message)> progress = null)
        {
            var settings = GetOrCreateSettings();

            progress?.Report((0.1f, "Preparing build..."));

            // Store current active settings
            var previousSettings = AddressableAssetSettingsDefaultObject.Settings;

            try
            {
                // Set our settings as active temporarily
                AddressableAssetSettingsDefaultObject.Settings = settings;

                progress?.Report((0.2f, "Cleaning build cache..."));

                // Clean previous build
                AddressableAssetSettings.CleanPlayerContent(settings.ActivePlayerDataBuilder);

                progress?.Report((0.3f, "Building Addressables for WebGPU..."));

                // Build
                AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

                if (!string.IsNullOrEmpty(result.Error))
                {
                    Debug.LogError($"Addressables build failed: {result.Error}");
                    return new BuildResult
                    {
                        Success = false,
                        Error = result.Error,
                        OutputPath = null
                    };
                }

                progress?.Report((0.9f, "Build complete!"));

                string outputPath = GetBuildOutputPath();

                return new BuildResult
                {
                    Success = true,
                    Error = null,
                    OutputPath = outputPath,
                    BundleCount = result.LocationCount
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"Addressables build exception: {ex.Message}");
                return new BuildResult
                {
                    Success = false,
                    Error = ex.Message,
                    OutputPath = null
                };
            }
            finally
            {
                // Restore previous settings
                AddressableAssetSettingsDefaultObject.Settings = previousSettings;
            }
        }

        /// <summary>
        /// Gets the path to the build output folder.
        /// </summary>
        public static string GetBuildOutputPath()
        {
            var settings = GetOrCreateSettings();
            string buildPath = settings.profileSettings.GetValueByName(settings.activeProfileId, AddressableAssetSettings.kRemoteBuildPath);

            // Resolve path relative to project
            if (!Path.IsPathRooted(buildPath))
            {
                buildPath = Path.Combine(Directory.GetCurrentDirectory(), buildPath);
            }

            return buildPath;
        }

        /// <summary>
        /// Scans the build output folder and returns detected files.
        /// </summary>
        public static List<BuildOutputFile> GetBuildOutputFiles()
        {
            var files = new List<BuildOutputFile>();
            string outputPath = GetBuildOutputPath();

            if (!Directory.Exists(outputPath)) { return files; }

            foreach (var filePath in Directory.GetFiles(outputPath, "*", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(filePath);
                string extension = Path.GetExtension(filePath).ToLower();

                // Skip meta and manifest files
                if (extension == ".meta" || extension == ".manifest") { continue; }

                var fileInfo = new FileInfo(filePath);
                bool isCatalog = fileName.EndsWith(".bin") || fileName.EndsWith(".hash");
                bool isBundle = extension == ".bundle";

                files.Add(new BuildOutputFile
                {
                    FileName = fileName,
                    FilePath = filePath,
                    FileSize = fileInfo.Length,
                    IsCatalog = isCatalog,
                    IsBundle = isBundle
                });
            }

            return files;
        }

        /// <summary>
        /// Clears any cached settings reference.
        /// </summary>
        public static void ClearSettingsCache()
        {
            _settings = null;
        }

        public class BuildResult
        {
            public bool Success;
            public string Error;
            public string OutputPath;
            public int BundleCount;
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
