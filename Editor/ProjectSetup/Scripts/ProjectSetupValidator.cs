using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VirtualVenues.Editor.ProjectSetup
{
    /// <summary>
    /// Outcome of a single project-setup check.
    /// </summary>
    public enum SetupState
    {
        /// <summary>Already configured correctly.</summary>
        Satisfied,
        /// <summary>Misconfigured and fixable by the installer.</summary>
        NeedsFix,
        /// <summary>Cannot be evaluated/fixed in this environment (e.g. WebGL Build Support not installed).</summary>
        Unavailable
    }

    /// <summary>
    /// Result of evaluating one project-setup requirement.
    /// </summary>
    public readonly struct SetupCheck
    {
        public readonly SetupState State;
        public readonly string Detail;

        public SetupCheck(SetupState state, string detail)
        {
            State = state;
            Detail = detail;
        }

        public static SetupCheck Ok(string detail) => new SetupCheck(SetupState.Satisfied, detail);
        public static SetupCheck Fix(string detail) => new SetupCheck(SetupState.NeedsFix, detail);
        public static SetupCheck NA(string detail) => new SetupCheck(SetupState.Unavailable, detail);
    }

    /// <summary>
    /// Pure, side-effect-free checks that compare the current project against the VirtualVenues
    /// template configuration. Worlds built in a project that diverges from these settings render
    /// pink in the WebGPU client; <see cref="ProjectSetupInstaller"/> applies the fixes.
    /// </summary>
    public static class ProjectSetupValidator
    {
        // Canonical VirtualVenues rendering assets (GUIDs shared with the core FutureFestXR project
        // and the VirtualVenuesTemplate). Resolved in the consumer project after the assets are copied.
        public const string UrpMobileLowGuid = "d7686b11d09df481bac3c76ecc5ea626";
        public const string UrpGlobalSettingsGuid = "7b03bcc14acd0e04f8f3a17b7fa47e40";

        public const string EnvironmentLayerName = "Environment";
        public const int EnvironmentLayerIndex = 3;

        // 0 = Input Manager (Old), 1 = Input System Package (New), 2 = Both. The template uses Both.
        public const int InputHandlerBoth = 2;

        public static SetupCheck CheckUrpAssetsPresent()
        {
            string path = AssetDatabase.GUIDToAssetPath(UrpMobileLowGuid);
            if (string.IsNullOrEmpty(path)) { return SetupCheck.Fix("VirtualVenues URP settings are not in the project."); }

            RenderPipelineAsset rp = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);
            return rp != null
                ? SetupCheck.Ok($"Present at {path}")
                : SetupCheck.Fix("URP asset GUID exists but failed to load.");
        }

        public static SetupCheck CheckColorSpace()
        {
            return PlayerSettings.colorSpace == ColorSpace.Linear
                ? SetupCheck.Ok("Linear")
                : SetupCheck.Fix($"Color space is {PlayerSettings.colorSpace}; should be Linear.");
        }

        public static SetupCheck CheckActivePipeline()
        {
            // The active quality level's override wins at runtime; fall back to the graphics default.
            RenderPipelineAsset active = QualitySettings.renderPipeline != null
                ? QualitySettings.renderPipeline
                : GraphicsSettings.defaultRenderPipeline;

            if (active == null) { return SetupCheck.Fix("No render pipeline assigned."); }

            string guid = GuidOf(active);
            return guid == UrpMobileLowGuid
                ? SetupCheck.Ok("URP_Mobile_Low is the active pipeline.")
                : SetupCheck.Fix($"Active pipeline is '{active.name}' ({guid}); should be URP_Mobile_Low.");
        }

        public static SetupCheck CheckGlobalSettings()
        {
            RenderPipelineGlobalSettings gs =
                EditorGraphicsSettings.GetRenderPipelineGlobalSettingsAsset<UniversalRenderPipeline>();
            if (gs == null) { return SetupCheck.Fix("No URP global settings assigned."); }

            string guid = GuidOf(gs);
            return guid == UrpGlobalSettingsGuid
                ? SetupCheck.Ok("VirtualVenues URP global settings are active.")
                : SetupCheck.Fix("URP global settings differ from the VirtualVenues settings.");
        }

        public static SetupCheck CheckEnvironmentLayer()
        {
            string name = LayerMask.LayerToName(EnvironmentLayerIndex);
            return name == EnvironmentLayerName
                ? SetupCheck.Ok($"Layer {EnvironmentLayerIndex} = '{EnvironmentLayerName}'")
                : SetupCheck.Fix($"Layer {EnvironmentLayerIndex} is '{name}'; should be '{EnvironmentLayerName}'.");
        }

        public static SetupCheck CheckWebGLGraphicsApi()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                return SetupCheck.NA("WebGL Build Support is not installed.");
            }

            if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.WebGL))
            {
                return SetupCheck.Fix("WebGL uses Auto graphics API; should be WebGPU only.");
            }

            GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.WebGL);
            bool ok = apis != null && apis.Length == 1 && apis[0] == GraphicsDeviceType.WebGPU;
            return ok
                ? SetupCheck.Ok("WebGPU")
                : SetupCheck.Fix($"WebGL APIs = [{string.Join(", ", apis ?? new GraphicsDeviceType[0])}]; should be [WebGPU].");
        }

        public static SetupCheck CheckWebGLStripping()
        {
            ManagedStrippingLevel level = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.WebGL);
            return level == ManagedStrippingLevel.Low
                ? SetupCheck.Ok("Low")
                : SetupCheck.Fix($"WebGL managed stripping is {level}; should be Low.");
        }

        public static SetupCheck CheckInputHandler()
        {
            int handler = GetActiveInputHandler();
            return handler == InputHandlerBoth
                ? SetupCheck.Ok("Both (legacy + Input System)")
                : SetupCheck.Fix($"Active input handling is '{InputHandlerName(handler)}'; should be 'Both'.");
        }

        /// <summary>The serialized <c>activeInputHandler</c> value, or -1 if it can't be read.</summary>
        public static int GetActiveInputHandler()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) { return -1; }

            SerializedProperty prop = new SerializedObject(assets[0]).FindProperty("activeInputHandler");
            return prop != null ? prop.intValue : -1;
        }

        private static string InputHandlerName(int handler)
        {
            switch (handler)
            {
                case 0: return "Input Manager (Old)";
                case 1: return "Input System Package (New)";
                case 2: return "Both";
                default: return "Unknown";
            }
        }

        /// <summary>Resolves the asset-database GUID of an asset reference, or null.</summary>
        public static string GuidOf(Object obj)
        {
            if (obj == null) { return null; }
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long _) ? guid : null;
        }
    }
}
