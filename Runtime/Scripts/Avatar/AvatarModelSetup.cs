#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VirtualVenues
{
    /// <summary>
    /// Shared avatar setup used by both the Avatar inspector's "Setup Avatar from Model" button and the
    /// "Assets ▸ VirtualVenues ▸ Create Avatar Prefab from Model" menu action. Configures a model so it
    /// can be a VirtualVenues avatar: rig → Humanoid (if needed), add/assign an Animator (root motion off,
    /// controller intentionally left empty — UPC injects FFCharacterAnimController at runtime), and add a
    /// 3-tier LODGroup. Editor-only; lives in the runtime asmdef under #if UNITY_EDITOR so the inspector
    /// (also runtime-side) can call it. Never references FutureFest assemblies.
    /// </summary>
    internal static class AvatarModelSetup
    {
        public struct Result
        {
            public Animator animator;
            public LODGroup lodGroup;
            public string summary;
            public bool aborted;
        }

        // Configures the rig/Animator/LODGroup on the given root. Does NOT add/link the VirtualVenues.Avatar
        // component or mark anything dirty — the caller does that in its own context. Idempotent: each step
        // is a no-op when already satisfied. recordUndo=true wraps mutations in Undo (scene editing);
        // recordUndo=false skips Undo (a throwaway instance about to be saved as a prefab).
        public static Result Configure(GameObject root, bool recordUndo)
        {
            var result = new Result();

            // 1. Resolve the source model asset (the .fbx/.glb this instance was created from).
            string assetPath = ResolveSourceModelPath(root);
            bool assetLinked = !string.IsNullOrEmpty(assetPath);
            ModelImporter importer = assetLinked ? AssetImporter.GetAtPath(assetPath) as ModelImporter : null;

            // 2. Rig -> Humanoid, only when an actual change is needed (already-Humanoid models never prompt).
            bool convertedRig = false;
            if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
            {
                bool confirmed = EditorUtility.DisplayDialog("Convert rig to Humanoid?",
                    $"This changes the import settings of the shared model asset:\n\n{assetPath}\n\n" +
                    "and reimports it. This affects every user of that model and CANNOT be undone. Continue?",
                    "Convert & Reimport", "Cancel");
                if (!confirmed) { result.aborted = true; return result; }

                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
                convertedRig = true;
            }

            // 3. Find the generated Humanoid rig avatar (only meaningful for an asset-linked model).
            UnityEngine.Avatar rig = assetLinked ? FindHumanoidAvatar(assetPath) : null;
            if (convertedRig && rig == null)
            {
                EditorUtility.DisplayDialog("Setup Avatar",
                    "The model was reimported as Humanoid, but Unity did not produce a valid Humanoid avatar " +
                    "(the model may not have a recognizable humanoid skeleton). Open the model's Rig tab and " +
                    "configure the avatar manually.", "OK");
                result.aborted = true;
                return result;
            }

            // 4. Animator: add if missing, assign the rig (if valid + not already), force root motion off.
            Animator animator = EnsureAnimator(root, rig, recordUndo, out bool addedAnimator, out bool assignedRig);

            // 5. LODGroup: reuse an existing one, else build a 3-tier group containing every renderer.
            LODGroup lodGroup = EnsureLodGroup(root, recordUndo, out bool addedLodGroup, out int rendererCount, out string lodError);

            // 6. Summary.
            var summary = new System.Text.StringBuilder();
            if (convertedRig) { summary.AppendLine("• Rig converted to Humanoid (permanent — cannot be undone)."); }
            else if (importer != null) { summary.AppendLine("• Rig was already Humanoid."); }
            else { summary.AppendLine("• Not linked to a model asset, so the rig was left as-is. If it isn't already Humanoid, set the model's Rig to Humanoid manually."); }

            summary.AppendLine(addedAnimator ? "• Added an Animator." : "• Reused the existing Animator.");
            summary.AppendLine(assignedRig ? "• Assigned the Humanoid rig avatar."
                : (rig != null ? "• Animator already had a valid Humanoid avatar." : "• No Humanoid avatar assigned (none available)."));
            summary.AppendLine("• Apply Root Motion off; animator controller left empty (injected at runtime).");

            if (lodGroup != null) { summary.AppendLine(addedLodGroup ? $"• Added a 3-tier LODGroup with {rendererCount} renderer(s)." : "• Reused the existing LODGroup."); }
            else { summary.AppendLine($"• LODGroup NOT created: {lodError}"); }

            result.animator = animator;
            result.lodGroup = lodGroup;
            result.summary = summary.ToString();
            return result;
        }

        // --- Asset menu: create a configured avatar prefab directly from a selected model ---------------

        [MenuItem("Assets/VirtualVenues/Create Avatar Prefab from Model", false)]
        private static void CreateAvatarPrefabFromModel()
        {
            string modelPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(modelPath) || !(AssetImporter.GetAtPath(modelPath) is ModelImporter))
            {
                EditorUtility.DisplayDialog("Create Avatar Prefab",
                    "Select a model asset (e.g. an .fbx) in the Project window first.", "OK");
                return;
            }

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null)
            {
                EditorUtility.DisplayDialog("Create Avatar Prefab", "Could not load the selected model.", "OK");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            try
            {
                // recordUndo:false — the instance is a throwaway we save as a prefab, then destroy.
                Result r = Configure(instance, recordUndo: false);
                if (r.aborted) { return; }

                // Add the Avatar component AFTER setup (so it isn't present during any reimport), then link.
                Avatar avatar = instance.AddComponent<Avatar>();
                var so = new SerializedObject(avatar);
                so.FindProperty("_animator").objectReferenceValue = r.animator;
                if (r.lodGroup != null) { so.FindProperty("_lodGroup").objectReferenceValue = r.lodGroup; }
                so.ApplyModifiedProperties();

                // Save next to the model, auto-named (never overwrites). Result is a variant of the model.
                string dir = System.IO.Path.GetDirectoryName(modelPath);
                string prefabPath = AssetDatabase.GenerateUniqueAssetPath(($"{dir}/{modelAsset.name}.prefab").Replace("\\", "/"));

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool ok);
                if (ok && prefab != null)
                {
                    Selection.activeObject = prefab;
                    EditorGUIUtility.PingObject(prefab);
                    EditorUtility.DisplayDialog("Create Avatar Prefab", $"{r.summary}\nCreated prefab:\n{prefabPath}", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Create Avatar Prefab", "Failed to save the prefab.", "OK");
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [MenuItem("Assets/VirtualVenues/Create Avatar Prefab from Model", true)]
        private static bool ValidateCreateAvatarPrefabFromModel()
        {
            if (Selection.activeObject == null) { return false; }
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter;
        }

        // --- Helpers -----------------------------------------------------------------------------------

        // Resolves the source model asset path behind a scene instance. Empty string => not asset-linked.
        private static string ResolveSourceModelPath(GameObject go)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (source != null)
            {
                string path = AssetDatabase.GetAssetPath(source);
                if (!string.IsNullOrEmpty(path)) { return path; }
            }
            return AssetDatabase.GetAssetPath(go);
        }

        private static UnityEngine.Avatar FindHumanoidAvatar(string assetPath)
        {
            return AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath)
                .OfType<UnityEngine.Avatar>()
                .FirstOrDefault(a => a != null && a.isHuman && a.isValid);
        }

        private static Animator EnsureAnimator(GameObject root, UnityEngine.Avatar rig, bool recordUndo, out bool added, out bool assignedRig)
        {
            added = false;
            assignedRig = false;

            Animator animator = root.GetComponent<Animator>();
            if (animator == null)
            {
                animator = recordUndo ? Undo.AddComponent<Animator>(root) : root.AddComponent<Animator>();
                added = true;
            }

            if (rig != null)
            {
                bool alreadyValid = animator.avatar != null && animator.avatar.isHuman && animator.avatar.isValid;
                if (!alreadyValid)
                {
                    if (recordUndo) { Undo.RecordObject(animator, "Setup Avatar — Animator"); }
                    animator.avatar = rig;
                    assignedRig = true;
                }
            }

            if (animator.applyRootMotion)
            {
                if (recordUndo) { Undo.RecordObject(animator, "Setup Avatar — Animator"); }
                animator.applyRootMotion = false;
            }

            return animator;
        }

        private static LODGroup EnsureLodGroup(GameObject root, bool recordUndo, out bool added, out int rendererCount, out string error)
        {
            added = false;
            rendererCount = 0;
            error = null;

            LODGroup existing = root.GetComponent<LODGroup>();
            if (existing != null) { return existing; } // don't clobber a hand-authored LODGroup

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                error = "No renderers were found under this object.";
                return null;
            }

            LODGroup lodGroup = recordUndo ? Undo.AddComponent<LODGroup>(root) : root.AddComponent<LODGroup>();
            lodGroup.SetLODs(BuildThreeTierLods(renderers));
            lodGroup.RecalculateBounds();
            added = true;
            rendererCount = renderers.Length;
            return lodGroup;
        }

        // All renderers in all three tiers; the avatar renders until it is a speck (1% screen height),
        // then culls. The runtime camera rig (PlayerLookBehaviourBase.SetCharacterRenderers) unions
        // renderers across all tiers, so first/third-person renderer toggling still sees the full set.
        private static LOD[] BuildThreeTierLods(Renderer[] renderers)
        {
            return new[]
            {
                new LOD(0.5f, renderers),
                new LOD(0.25f, renderers),
                new LOD(0.01f, renderers),
            };
        }
    }
}
#endif
