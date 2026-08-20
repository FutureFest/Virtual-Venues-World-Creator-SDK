using System;
using System.Collections.Generic;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using VirtualVenues.Editor.Publishing;

namespace VirtualVenues.Editor.AvatarPublisher
{
    /// <summary>
    /// Static avatar-facing facade for building the avatar Addressables catalog. The shared build engine now
    /// lives in <see cref="AddressablesCatalogBuilder"/> (so asset packs can reuse it via a sibling subclass);
    /// this preserves the original public API used by AvatarPublisherUI and delegates to a single
    /// <see cref="AvatarCatalogBuilder"/> instance. Behaviour is unchanged for the avatar path.
    /// </summary>
    public static class AddressablesBuildManager
    {
        internal const string BUCKET_URL = AddressablesCatalogBuilder.BucketUrl;

        private static readonly AvatarCatalogBuilder _impl = new AvatarCatalogBuilder();

        public static AddressableAssetSettings GetOrCreateSettings() => _impl.GetOrCreateSettings();

        public static void SetRemoteLoadPath(string contentBaseUrl) => _impl.SetRemoteLoadPath(contentBaseUrl);

        public static string BuildContentBaseUrl(string bucketUrl, string userId, string catalogId, string versionId)
            => AddressablesCatalogBuilder.BuildContentBaseUrl(bucketUrl, userId, catalogId, versionId);

        public static string GenerateVersionId() => AddressablesCatalogBuilder.GenerateVersionId();

        public static void SetBuildIdentity(string versionId) => _impl.SetBuildIdentity(versionId);

        public static void SetupAssetGroup(List<GameObject> avatarPrefabs, List<AvatarCatalogBuilder.CosmeticEntry> cosmetics)
            => _impl.SetupAssetGroup(avatarPrefabs, cosmetics);

        public static AddressablesCatalogBuilder.BuildResult BuildForWebGPU(
            IProgress<(float progress, string message)> progress = null) => _impl.BuildForWebGPU(progress);

        public static string GetBuildOutputPath() => _impl.GetBuildOutputPath();

        public static List<AddressablesCatalogBuilder.BuildOutputFile> GetBuildOutputFiles() => _impl.GetBuildOutputFiles();

        public static void ClearSettingsCache() => _impl.ClearSettingsCache();
    }
}
