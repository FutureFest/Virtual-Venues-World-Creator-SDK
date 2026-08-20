using System.Collections.Generic;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace VirtualVenues.Editor.Publishing
{
    /// <summary>
    /// Avatar-pack catalog builder: the original avatar Addressables config (settings asset
    /// "VVSDKAddressableSettings", group "VVSDKAvatars", profile "VVSDKProfile") expressed as a subclass
    /// of the shared <see cref="AddressablesCatalogBuilder"/>.
    ///
    /// THE THREE-PARTY CONTRACT (see .claude/docs/avatar-pipeline/README.md):
    ///   Addressables address     == {slotId}_{assetId}
    ///   backend asset.game_id    == assetId
    ///   backend asset_category.game_id == the avatar's slotId
    /// Avatars are the exception: they are addressed BARE, because AvatarContainer.ChangeAvatar passes the
    /// raw key and only ChangeCostume builds a "Costume_" prefix itself.
    /// </summary>
    public sealed class AvatarCatalogBuilder : AddressablesCatalogBuilder
    {
        protected override string SettingsAssetName => "VVSDKAddressableSettings";
        protected override string GroupName => "VVSDKAvatars";
        protected override string ProfileName => "VVSDKProfile";

        /// <summary>A cosmetic prefab and the avatar slot it is published into.</summary>
        public readonly struct CosmeticEntry
        {
            public readonly GameObject Prefab;
            public readonly string SlotId;

            public CosmeticEntry(GameObject prefab, string slotId)
            {
                Prefab = prefab;
                SlotId = slotId;
            }
        }

        /// <summary>
        /// Splits a cosmetic prefab name into the Addressables address and the assetId the backend stores.
        /// A prefab already named "Hat_Halo" for slot "Hat" keeps that address and yields assetId "Halo";
        /// a prefab named "Halo" becomes address "Hat_Halo" with the same assetId. Without this, the two
        /// spellings produce either an un-loadable address or a double-prefixed "Hat_Hat_Halo".
        ///
        /// This is the ONE place the rule lives — the address and the published gameId must agree, and two
        /// copies of it would drift.
        /// </summary>
        public static void ResolveCosmeticAddress(string prefabName, string slotId, out string address, out string assetId)
        {
            if (string.IsNullOrEmpty(slotId))
            {
                // No slot declared: publish verbatim, matching pre-slot behaviour.
                address = prefabName;
                assetId = prefabName;
                return;
            }

            string prefix = slotId + "_";
            bool alreadyPrefixed = prefabName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase);

            assetId = alreadyPrefixed ? prefabName.Substring(prefix.Length) : prefabName;
            address = alreadyPrefixed ? prefabName : prefix + prefabName;
        }

        /// <summary>Sets up the avatar group with the provided avatar + cosmetic prefabs.</summary>
        public void SetupAssetGroup(List<GameObject> avatarPrefabs, List<CosmeticEntry> cosmetics)
        {
            AddressableAssetGroup group = GetOrCreateClearedGroup();

            if (avatarPrefabs != null)
            {
                foreach (var prefab in avatarPrefabs)
                {
                    if (prefab == null) { continue; }
                    // Avatars stay BARE — AvatarContainer.ChangeAvatar loads the raw key.
                    AddEntry(group, prefab, prefab.name, "Avatar_" + prefab.name);
                }
            }
            if (cosmetics != null)
            {
                foreach (var cosmetic in cosmetics)
                {
                    if (cosmetic.Prefab == null) { continue; }
                    ResolveCosmeticAddress(cosmetic.Prefab.name, cosmetic.SlotId, out string address, out _);
                    AddEntry(group, cosmetic.Prefab, address, "Cosmetic_" + cosmetic.Prefab.name);
                }
            }

            SaveGroup();
        }
    }
}
