using System;

namespace VirtualVenues.Editor.AssetPackPublisher
{
    // -------------------------------------------------------------------------------------------------
    // Self-contained copies of the ff-api contract POCOs the publisher needs.
    //
    // These intentionally duplicate the matching types in com.virtualvenues.layout.runtime
    // (VirtualVenues.Layout). The publisher ships inside com.virtualvenues.sdk for world creators,
    // who do NOT have the layout.runtime assembly (it drags in FutureFest core/MP + Mirror, none of
    // which exist in a creator project). Keeping these here lets the SDK editor assembly compile
    // standalone with no reference to layout.runtime.
    //
    // All are JsonUtility-safe POCOs and mirror the frozen ff-api contract
    // (docs/uwe/openapi.yaml). Keep field names/types in sync if that contract changes.
    // -------------------------------------------------------------------------------------------------

    /// <summary>3-component vector. JsonUtility-safe POCO (NOT UnityEngine.Vector3).</summary>
    [Serializable]
    public class Vec3
    {
        public float x;
        public float y;
        public float z;
    }

    /// <summary>Axis-aligned bounds of an asset, in its local space.</summary>
    [Serializable]
    public class BoundsData
    {
        public Vec3 center;
        public Vec3 size;
    }

    /// <summary>
    /// Shared asset/package contract used by the publisher and ff-api.
    ///
    /// KEY CONVENTION (grounded in FutureFest.ContentManagement.ContentId, which does
    /// fullContentId.Split("_") and uses [0]=type and [1]=id):
    /// assetKey == "{Type}_{Id}" — EXACTLY ONE underscore, and the Id token MUST NOT
    /// contain underscores (e.g. "Prop_SpeakerStack" OK; "Prop_Speaker_Stack" mis-parses).
    /// </summary>
    [Serializable]
    public class AssetMeta
    {
        /// <summary>"{Type}_{Id}" — single underscore, Id has no underscores.</summary>
        public string assetKey;

        public string displayName;
        public string category;

        /// <summary>Matches LayoutInstance.kind vocabulary where applicable.</summary>
        public string kind;

        public string thumbnailUrl;
        public BoundsData bounds;
    }

    /// <summary>Optional engine fingerprint persisted at publish (used to refuse mismatched builds).</summary>
    [Serializable]
    public class PackageMetadata
    {
        public string unityVersion;
        public string bundleVersion;
        public string buildTarget;
    }

    /// <summary>A package in the customer's Library (GET/POST /users/me/library). Maps 1:1 to a CatalogRef.</summary>
    [Serializable]
    public class LibraryPackage
    {
        public string packageId;
        public string catalogId;
        public string versionId;
        public string contentBaseUrl;
        public string name;
        public string version;
        public PackageMetadata metadata; // optional — backend omits when empty (may be null)
    }

    // NOTE: UploadUrl is intentionally NOT defined here — the publisher already owns its own
    // UploadUrl DTO in AssetPackPublisherApi.cs (it was never a VirtualVenues.Layout type).
}
