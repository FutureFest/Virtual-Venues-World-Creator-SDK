using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VirtualVenues.Editor.ProjectSetup
{
    /// <summary>
    /// One configurable project-setup requirement: how to detect it (<see cref="Check"/>) and how to
    /// fix it (<see cref="Apply"/>). Both the checklist UI and <see cref="ProjectSetupInstaller.RunAll"/>
    /// drive off this single list, so there is one source of truth for the order and behaviour of steps.
    /// </summary>
    public class SetupStep
    {
        public string Id;
        public string Title;
        public string Description;
        /// <summary>True when leaving this unfixed causes pink worlds / broken builds (drives the publish warning).</summary>
        public bool Critical;
        /// <summary>True when applying forces a Unity restart (handled by the UI, skipped by bulk Fix All).</summary>
        public bool RequiresRestart;
        public Func<SetupCheck> Check;
        public Action Apply;
    }

    /// <summary>
    /// Brings a creator's Unity project in line with the VirtualVenues template so uploaded worlds do not
    /// render pink in the WebGPU client. Every apply method is idempotent (it no-ops when its check already
    /// passes), so the whole installer is safe to re-run. Works when the SDK is installed as an external
    /// package: bundled reference assets live in a non-imported "~" folder located via
    /// <see cref="PackageInfo.FindForAssembly"/>.
    /// </summary>
    public static class ProjectSetupInstaller
    {
        private const string ReferenceAssetsRelativePath = "Editor/ProjectSetup/ProjectSetupAssets~/URPSettings";
        private const string DestinationRelativePath = "Settings/URPSettings"; // under <project>/Assets

        private static readonly List<SetupStep> _steps = new List<SetupStep>
        {
            new SetupStep
            {
                Id = "urp-assets",
                Title = "Import VirtualVenues URP settings",
                Description = "Copies the URP pipeline assets, renderers, global settings and volume profiles into Assets/Settings/URPSettings.",
                Critical = true,
                Check = ProjectSetupValidator.CheckUrpAssetsPresent,
                Apply = ImportUrpSettings
            },
            new SetupStep
            {
                Id = "pipeline",
                Title = "Assign URP_Mobile_Low render pipeline",
                Description = "Sets the active and default render pipeline to URP_Mobile_Low.",
                Critical = true,
                Check = ProjectSetupValidator.CheckActivePipeline,
                Apply = AssignRenderPipeline
            },
            new SetupStep
            {
                Id = "global-settings",
                Title = "Assign URP global settings",
                Description = "Activates the VirtualVenues URP global settings (shader-variant stripping policy).",
                Critical = true,
                Check = ProjectSetupValidator.CheckGlobalSettings,
                Apply = AssignGlobalSettings
            },
            new SetupStep
            {
                Id = "color-space",
                Title = "Set color space to Linear",
                Description = "Worlds are authored in Linear color space.",
                Critical = true,
                Check = ProjectSetupValidator.CheckColorSpace,
                Apply = SetColorSpaceLinear
            },
            new SetupStep
            {
                Id = "webgl-api",
                Title = "Set WebGL graphics API to WebGPU",
                Description = "The player client runs WebGPU; the build must target it explicitly (not Auto).",
                Critical = true,
                Check = ProjectSetupValidator.CheckWebGLGraphicsApi,
                Apply = SetWebGLGraphicsApi
            },
            new SetupStep
            {
                Id = "environment-layer",
                Title = "Add the 'Environment' layer",
                Description = "Reserves layer 3 for 'Environment', matching the template.",
                Critical = false,
                Check = ProjectSetupValidator.CheckEnvironmentLayer,
                Apply = AddEnvironmentLayer
            },
            new SetupStep
            {
                Id = "webgl-stripping",
                Title = "Set WebGL managed stripping to Low",
                Description = "Matches the template's WebGL managed code stripping level.",
                Critical = false,
                Check = ProjectSetupValidator.CheckWebGLStripping,
                Apply = SetWebGLStripping
            },
            new SetupStep
            {
                Id = "input-handler",
                Title = "Enable Input System (Both)",
                Description = "Switches Active Input Handling to 'Both'. Requires a Unity restart.",
                Critical = false,
                RequiresRestart = true,
                Check = ProjectSetupValidator.CheckInputHandler,
                Apply = SetInputHandlerBoth
            },
        };

        public static IReadOnlyList<SetupStep> Steps => _steps;

        /// <summary>True when a critical (pink-causing) requirement still needs fixing.</summary>
        public static bool HasCriticalIssues()
        {
            return GetCriticalIssues().Count > 0;
        }

        /// <summary>Titles of the critical steps that currently need fixing, for surfacing to the user.</summary>
        public static List<string> GetCriticalIssues()
        {
            var issues = new List<string>();
            foreach (SetupStep step in _steps)
            {
                if (step.Critical && step.Check().State == SetupState.NeedsFix) { issues.Add(step.Title); }
            }
            return issues;
        }

        /// <summary>
        /// True when the SDK is installed as a UPM package — i.e. a creator's project. Returns false when
        /// the SDK is embedded in its own development repo (FutureFestXR), where Project Setup does not
        /// apply: that project intentionally uses a different (server + client) quality configuration.
        /// </summary>
        public static bool IsConsumerProject()
        {
            return PackageInfo.FindForAssembly(typeof(ProjectSetupInstaller).Assembly) != null;
        }

        /// <summary>
        /// Applies every step that needs fixing, skipping already-satisfied, unavailable, and (unless
        /// <paramref name="includeRestartSteps"/>) restart-required steps. Returns a per-step log.
        /// </summary>
        public static IReadOnlyList<string> RunAll(bool includeRestartSteps)
        {
            var log = new List<string>();
            foreach (SetupStep step in _steps)
            {
                SetupCheck before = step.Check();
                if (before.State == SetupState.Satisfied) { log.Add($"OK   {step.Title} — already configured"); continue; }
                if (before.State == SetupState.Unavailable) { log.Add($"--   {step.Title} — skipped ({before.Detail})"); continue; }
                if (step.RequiresRestart && !includeRestartSteps) { log.Add($"--   {step.Title} — skipped (fix individually; needs restart)"); continue; }

                log.Add(ApplyStep(step));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return log;
        }

        /// <summary>Applies a single step and returns a one-line result. Safe to call directly from the UI.</summary>
        public static string ApplyStep(SetupStep step)
        {
            try
            {
                step.Apply();
                SetupCheck after = step.Check();
                AssetDatabase.SaveAssets();
                return after.State == SetupState.Satisfied
                    ? $"OK   {step.Title} — fixed"
                    : $"WARN {step.Title} — {after.Detail}";
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return $"FAIL {step.Title} — {e.Message}";
            }
        }

        // ---- Apply implementations (each idempotent) -------------------------------------------------

        public static void ImportUrpSettings()
        {
            if (ProjectSetupValidator.CheckUrpAssetsPresent().State == SetupState.Satisfied) { return; }

            string source = GetReferenceAssetsRoot();
            if (string.IsNullOrEmpty(source) || !Directory.Exists(source))
            {
                Debug.LogError("[ProjectSetup] Could not locate the bundled URPSettings reference assets in the SDK package.");
                return;
            }

            string destination = Path.Combine(Application.dataPath, DestinationRelativePath);
            CopyDirectoryPreservingMeta(source, destination);
            AssetDatabase.Refresh();
        }

        public static void AssignRenderPipeline()
        {
            RenderPipelineAsset rp = LoadByGuid<RenderPipelineAsset>(ProjectSetupValidator.UrpMobileLowGuid);
            if (rp == null)
            {
                Debug.LogError("[ProjectSetup] URP_Mobile_Low not found. Run 'Import VirtualVenues URP settings' first.");
                return;
            }

            GraphicsSettings.defaultRenderPipeline = rp;
            QualitySettings.renderPipeline = rp; // override for the current quality level
            AssetDatabase.SaveAssets();
        }

        public static void AssignGlobalSettings()
        {
            // UniversalRenderPipelineGlobalSettings is internal in URP, so load/assign via the public
            // base type and the public UniversalRenderPipeline pipeline Type.
            RenderPipelineGlobalSettings gs =
                LoadByGuid<RenderPipelineGlobalSettings>(ProjectSetupValidator.UrpGlobalSettingsGuid);
            if (gs == null)
            {
                Debug.LogError("[ProjectSetup] URP global settings not found. Run 'Import VirtualVenues URP settings' first.");
                return;
            }

            EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset(typeof(UniversalRenderPipeline), gs);
            AssetDatabase.SaveAssets();
        }

        public static void SetColorSpaceLinear()
        {
            if (PlayerSettings.colorSpace != ColorSpace.Linear) { PlayerSettings.colorSpace = ColorSpace.Linear; }
        }

        public static void AddEnvironmentLayer()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) { return; }

            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null || layers.arraySize <= ProjectSetupValidator.EnvironmentLayerIndex) { return; }

            SerializedProperty slot = layers.GetArrayElementAtIndex(ProjectSetupValidator.EnvironmentLayerIndex);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = ProjectSetupValidator.EnvironmentLayerName;
                tagManager.ApplyModifiedProperties();
            }
            else if (slot.stringValue != ProjectSetupValidator.EnvironmentLayerName)
            {
                Debug.LogWarning($"[ProjectSetup] Layer {ProjectSetupValidator.EnvironmentLayerIndex} is already used by " +
                                 $"'{slot.stringValue}'; leaving it untouched.");
            }
        }

        public static void SetWebGLGraphicsApi()
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.WebGPU });
        }

        public static void SetWebGLStripping()
        {
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
        }

        public static void SetInputHandlerBoth()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) { return; }

            var playerSettings = new SerializedObject(assets[0]);
            SerializedProperty prop = playerSettings.FindProperty("activeInputHandler");
            if (prop == null)
            {
                Debug.LogError("[ProjectSetup] Could not find 'activeInputHandler' in PlayerSettings.");
                return;
            }

            if (prop.intValue == ProjectSetupValidator.InputHandlerBoth) { return; }
            prop.intValue = ProjectSetupValidator.InputHandlerBoth;
            playerSettings.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        // ---- Asset-location & copy helpers -----------------------------------------------------------

        /// <summary>
        /// Absolute path to the bundled reference assets, working both as an installed package
        /// (PackageInfo.resolvedPath) and when developed inside this repo's Assets/ folder.
        /// </summary>
        private static string GetReferenceAssetsRoot()
        {
            PackageInfo info = PackageInfo.FindForAssembly(typeof(ProjectSetupInstaller).Assembly);
            if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
            {
                string packagePath = Path.Combine(info.resolvedPath, ReferenceAssetsRelativePath);
                if (Directory.Exists(packagePath)) { return packagePath; }
            }

            // Fallback (SDK embedded in Assets/): find this script, walk up to package.json, then descend.
            string[] guids = AssetDatabase.FindAssets("t:MonoScript ProjectSetupInstaller");
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.EndsWith("ProjectSetupInstaller.cs", StringComparison.Ordinal)) { continue; }

                string dir = Path.GetDirectoryName(Path.GetFullPath(assetPath));
                for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
                {
                    if (File.Exists(Path.Combine(dir, "package.json")))
                    {
                        string embeddedPath = Path.Combine(dir, ReferenceAssetsRelativePath);
                        if (Directory.Exists(embeddedPath)) { return embeddedPath; }
                    }
                    dir = Path.GetDirectoryName(dir);
                }
            }

            return null;
        }

        /// <summary>
        /// Recursively copies assets and their .meta files (preserving GUIDs). Never overwrites an existing
        /// file, so a creator's edited assets are left intact and re-running is a no-op.
        /// </summary>
        private static void CopyDirectoryPreservingMeta(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (string file in Directory.GetFiles(source))
            {
                string target = Path.Combine(destination, Path.GetFileName(file));
                if (File.Exists(target)) { continue; }
                File.Copy(file, target);
            }

            foreach (string dir in Directory.GetDirectories(source))
            {
                CopyDirectoryPreservingMeta(dir, Path.Combine(destination, Path.GetFileName(dir)));
            }
        }

        private static T LoadByGuid<T>(string guid) where T : UnityEngine.Object
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }
    }
}
