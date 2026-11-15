using System;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Which part of the video frame a Screen or Artist shows. Whole shows the full frame: a movie, or a full-frame
    /// artist or visuals stream. Stacked is one video carrying two pictures, the artist on the top half and the
    /// visuals on the bottom half, so one stream feeds both in sync: an Artist shows the top half, a Screen the
    /// bottom half. Stacked is 0 because markers saved before this setting existed (published worlds, layouts
    /// without the key) must keep playing stacked.
    /// </summary>
    public enum StreamLayout
    {
        [InspectorName("Stacked")] Stacked = 0,
        [InspectorName("Whole video")] Whole = 1
    }

    /// <summary>
    /// The code default tiling/offset for each StreamLayout, and its layout string: "whole" = Whole, anything else = Stacked.
    /// A Screen or Artist whose mesh has a material uses that material's own tiling/offset for either stream layout (in the
    /// SDK the stream layout picks the material); the code default is only for markers with no mesh material (layout-created).
    /// </summary>
    public static class StageStreamLayouts
    {
        /// <summary>
        /// Whole: the full frame, (1, 1) / (0, 0). Stacked: the top half for an Artist, (1, 0.5) / (0, -0.5), the bottom
        /// half for a Screen, (1, 0.5) / (0, 0). -0.5 rather than 0.5 so AVPro's vertical-flip path needs no texture wrap.
        /// </summary>
        public static void GetDefaultTransform(StreamLayout streamLayout, bool artist, out Vector2 tiling, out Vector2 offset)
        {
            if (streamLayout == StreamLayout.Whole)
            {
                tiling = Vector2.one;
                offset = Vector2.zero;
                return;
            }
            tiling = new Vector2(1f, 0.5f);
            offset = artist ? new Vector2(0f, -0.5f) : Vector2.zero;
        }

        /// <summary>"whole" is Whole. "", null and anything else (older "top"/"bottom" values too) are Stacked.</summary>
        public static StreamLayout Parse(string value)
        {
            return string.Equals(value, "whole", StringComparison.OrdinalIgnoreCase) ? StreamLayout.Whole : StreamLayout.Stacked;
        }

        public static string Format(StreamLayout streamLayout)
        {
            return streamLayout == StreamLayout.Whole ? "whole" : "";
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor only. The SDK's stage material for each marker kind and StreamLayout, and the swap the inspector runs when
    /// the creator changes Stream Layout. The materials are found by GUID, so this works wherever the SDK is installed,
    /// and a built world only carries the materials its markers reference. The material also carries the no-stream
    /// placeholder picture and the tiling/offset the world uses at runtime.
    /// </summary>
    public static class StageStreamLayoutAssets
    {
        // Runtime/Materials
        private const string ArtistStackedMaterialGuid = "5342ce541d734b34b95d623117f03ef8"; // Artist-Calibrate-UV.mat
        private const string ArtistWholeMaterialGuid = "7d468dbcf7ab4a6eb502f8e725709664"; // Artist-Calibrate-UV-Whole.mat
        private const string ScreenStackedMaterialGuid = "014140da5d93116449ace730540fd22e"; // Screen-Calibrate-UV.mat
        private const string ScreenWholeMaterialGuid = "36dd0f4bb3074eb89ec69cbccae3313c"; // Screen-Calibrate-UV-Whole.mat

        private static readonly string[] SdkMaterialGuids = { ArtistStackedMaterialGuid, ArtistWholeMaterialGuid, ScreenStackedMaterialGuid, ScreenWholeMaterialGuid };

        public static Material GetMaterial(bool artist, StreamLayout streamLayout)
        {
            if (artist) { return Load<Material>(streamLayout == StreamLayout.Whole ? ArtistWholeMaterialGuid : ArtistStackedMaterialGuid); }
            return Load<Material>(streamLayout == StreamLayout.Whole ? ScreenWholeMaterialGuid : ScreenStackedMaterialGuid);
        }

        /// <summary>True for no material or one of the SDK's four stage materials; false for a creator's own.</summary>
        public static bool IsSdkMaterial(Material material)
        {
            return material == null || Array.IndexOf(SdkMaterialGuids, GuidOf(material)) >= 0;
        }

        /// <summary>True when the marker's mesh has a material of the creator's own, which a Stream Layout change leaves alone.</summary>
        public static bool HasCustomMaterial(UnityEngine.Object marker)
        {
            Renderer mesh = marker is Artist artist ? artist.Renderer : marker is Screen screen ? screen.Renderer : null;
            return mesh != null && !IsSdkMaterial(mesh.sharedMaterial);
        }

        /// <summary>
        /// Sets a Screen's or Artist's Stream Layout and, when its mesh material is none or an SDK stage material, swaps
        /// that material for the SDK one of the new stream layout. Nothing else changes (the Default Texture is left alone).
        /// Every change goes through a SerializedObject, so it is undoable and records as a prefab instance override.
        /// </summary>
        public static void SetStreamLayout(UnityEngine.Object marker, StreamLayout streamLayout)
        {
            bool artist = marker is Artist;
            if (!artist && !(marker is Screen)) { return; }

            UnityEditor.SerializedObject markerObject = new UnityEditor.SerializedObject(marker);
            markerObject.FindProperty("_streamLayout").intValue = (int)streamLayout;
            markerObject.ApplyModifiedProperties();

            Renderer mesh = markerObject.FindProperty(artist ? "_artistMesh" : "_screenMesh").objectReferenceValue as Renderer;
            if (mesh == null || !IsSdkMaterial(mesh.sharedMaterial)) { return; }
            Material material = GetMaterial(artist, streamLayout);
            if (material == null) { return; }
            UnityEditor.SerializedObject meshObject = new UnityEditor.SerializedObject(mesh);
            UnityEditor.SerializedProperty materials = meshObject.FindProperty("m_Materials");
            if (materials.arraySize == 0) { materials.arraySize = 1; }
            materials.GetArrayElementAtIndex(0).objectReferenceValue = material;
            meshObject.ApplyModifiedProperties();
        }

        private static T Load<T>(string guid) where T : UnityEngine.Object
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static string GuidOf(UnityEngine.Object asset)
        {
            return UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long _) ? guid : null;
        }
    }
#endif
}
