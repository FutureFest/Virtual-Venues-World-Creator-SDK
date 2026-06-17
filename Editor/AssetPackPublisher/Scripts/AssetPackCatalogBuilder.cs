using System.Collections.Generic;
using UnityEngine;
using VirtualVenues.Editor.Publishing;

namespace VirtualVenues.Editor.AssetPackPublisher
{
    /// <summary>
    /// Asset-pack catalog builder: a subclass of the shared <see cref="AddressablesCatalogBuilder"/> that
    /// uses its OWN isolated settings ("VVSDKAssetPackSettings") + group ("VVSDKAssetPacks") so it never
    /// builds the avatar group's content (and vice-versa). Each prefab's Addressables address AND label is
    /// set to its assetKey ("Type_Id") so the runtime <c>ContentCache.RequestContent(assetKey)</c> resolves
    /// it after <c>MultiCatalogLoader</c> loads the catalog.
    /// </summary>
    public sealed class AssetPackCatalogBuilder : AddressablesCatalogBuilder
    {
        protected override string SettingsAssetName => "VVSDKAssetPackSettings";
        protected override string GroupName => "VVSDKAssetPacks";
        protected override string ProfileName => "VVSDKAssetPackProfile";

        /// <summary>Set up the asset-pack group with (prefab, assetKey) pairs; address+label = assetKey.</summary>
        public void SetupAssetGroup(IList<AssetPackEntry> assets)
        {
            var group = GetOrCreateClearedGroup();
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    if (asset == null || asset.Prefab == null || string.IsNullOrEmpty(asset.AssetKey)) { continue; }
                    AddEntry(group, asset.Prefab, asset.AssetKey, asset.AssetKey);
                }
            }
            SaveGroup();
        }

        /// <summary>One asset to include in the pack: the source prefab + its "Type_Id" assetKey.</summary>
        public sealed class AssetPackEntry
        {
            public GameObject Prefab;
            public string AssetKey;
        }
    }
}
