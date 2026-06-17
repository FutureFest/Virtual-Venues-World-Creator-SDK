using System.Collections.Generic;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace VirtualVenues.Editor.Publishing
{
    /// <summary>
    /// Avatar-pack catalog builder: the original avatar Addressables config (settings asset
    /// "VVSDKAddressableSettings", group "VVSDKAvatars", profile "VVSDKProfile") expressed as a subclass
    /// of the shared <see cref="AddressablesCatalogBuilder"/>. Behaviour matches the original
    /// AddressablesBuildManager exactly (address = prefab name, label = "Avatar_"/"Cosmetic_" + name).
    /// </summary>
    public sealed class AvatarCatalogBuilder : AddressablesCatalogBuilder
    {
        protected override string SettingsAssetName => "VVSDKAddressableSettings";
        protected override string GroupName => "VVSDKAvatars";
        protected override string ProfileName => "VVSDKProfile";

        /// <summary>Sets up the avatar group with the provided avatar + cosmetic prefabs.</summary>
        public void SetupAssetGroup(List<GameObject> avatarPrefabs, List<GameObject> cosmeticPrefabs)
        {
            AddressableAssetGroup group = GetOrCreateClearedGroup();

            if (avatarPrefabs != null)
            {
                foreach (var prefab in avatarPrefabs)
                {
                    if (prefab == null) { continue; }
                    AddEntry(group, prefab, prefab.name, "Avatar_" + prefab.name);
                }
            }
            if (cosmeticPrefabs != null)
            {
                foreach (var prefab in cosmeticPrefabs)
                {
                    if (prefab == null) { continue; }
                    AddEntry(group, prefab, prefab.name, "Cosmetic_" + prefab.name);
                }
            }

            SaveGroup();
        }
    }
}
