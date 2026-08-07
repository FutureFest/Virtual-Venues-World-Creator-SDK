using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.Editor.AssetPackPublisher
{
    /// <summary>
    /// Durable, project-local definition of an asset pack — the SOURCE OF TRUTH the
    /// <see cref="AssetPackPublisherUI"/> edits and re-publishes. Lives as an .asset the creator saves in
    /// their project (created via the publisher's "New Pack…" button), so it survives closing/reopening
    /// Unity: the <see cref="Item.prefab"/> fields are direct GameObject references that Unity serializes
    /// natively, which is what lets a pack be re-opened and updated without re-adding every asset by hand.
    ///
    /// This is creator CONTENT (like a prefab), not SDK tooling — it belongs in the creator's project.
    /// The Addressables group built at publish time is a throwaway artifact (rebuilt + wiped each publish
    /// by the bundle-identity fix), so it is deliberately NOT the place pack membership is remembered.
    ///
    /// Editor-only (lives in com.virtualvenues.sdk.editor). Never loaded at runtime / in a player build.
    /// </summary>
    public class AssetPackDefinition : ScriptableObject
    {
        // ---- backend identity (written on publish) --------------------------------------------- //

        /// <summary>ff-api asset-pack id. Empty until the first publish; reused on every later publish so
        /// the marketplace pack keeps its identity (an UPDATE, not a new pack).</summary>
        public string packId;

        /// <summary>versionId of the most recent publish (each publish mints a fresh one).</summary>
        public string lastVersionId;

        /// <summary>contentBaseUrl of the most recent publish (informational).</summary>
        public string lastContentBaseUrl;

        /// <summary>ISO-8601 UTC timestamp of the most recent publish (informational).</summary>
        public string lastPublishedUtc;

        // ---- pack metadata (mirrors the publisher UI fields 1:1) ------------------------------- //

        public string packName;
        public string version = "1.0.0";
        public int price;

        /// <summary>Comma-separated tags (same CSV format as the publisher's Tags field).</summary>
        public string tags;

        /// <summary>Comma-separated categories (same CSV format as the publisher's Categories field).</summary>
        public string categories;

        // ---- assets ---------------------------------------------------------------------------- //

        public List<Item> items = new List<Item>();

        /// <summary>One asset in the pack. Mirrors a publisher Row + the published <c>AssetMeta</c>.</summary>
        [Serializable]
        public class Item
        {
            /// <summary>Direct prefab reference — Unity persists this across sessions (the whole point).
            /// Null for skybox items, which reference <see cref="texture"/> instead.</summary>
            public GameObject prefab;

            /// <summary>
            /// Skybox items only (kind == "skybox"): the sky TEXTURE — an equirect/panoramic Texture2D
            /// or a Cubemap. Published as the asset itself (no prefab); the runtime wraps it in a
            /// shipped skybox template material. Null for every other kind.
            /// </summary>
            public Texture texture;

            /// <summary>
            /// Material items only (kind == "material"): the published Material asset. Offered in the
            /// World Editor's Materials rail and assignable into a renderer slot; READ-ONLY there, because
            /// its shader variants froze at publish time. Null for every other kind.
            /// </summary>
            public Material material;

            /// <summary>"{Type}_{Id}" content key (exactly one underscore).</summary>
            public string assetKey;

            public string displayName;
            public string category;

            /// <summary>Stored as the kind STRING (not a dropdown index) so it survives Kinds[] reordering.</summary>
            public string kind = "prop";

            /// <summary>Icon source: "Auto" (render the prefab) or "Custom" (use <see cref="customIconGuid"/>).
            /// String (not an index) for forward-compat, same as <see cref="kind"/>.</summary>
            public string iconSource = "Auto";

            /// <summary>GUID of the creator-supplied Texture2D used when <see cref="iconSource"/> is "Custom"
            /// (empty otherwise). A GUID — not a direct reference — so it survives the texture moving in-project.</summary>
            public string customIconGuid;

            /// <summary>thumbnailUrl from the most recent publish (informational; re-baked each publish).</summary>
            public string thumbnailUrl;

            /// <summary>
            /// When true, this prefab's child GameObjects are published as EXPOSED nodes in the pack's
            /// composite manifest (pack_manifest.json), so dropping the asset in the World Editor explodes
            /// it into real, individually-editable child objects (Unity-style hierarchy). On (default) =
            /// explode-on-drop; off = the asset stays one monolithic object. The pack still shows ONE grid
            /// tile either way.
            /// </summary>
            public bool exposeChildren = true;
        }
    }
}
