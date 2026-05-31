using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VirtualVenues
{
    /// <summary>
    /// Avatar component for VirtualVenues custom avatars.
    /// Configure renderer slots, item slots, and fallback content.
    /// At runtime in FutureFest, this is swapped for AvatarCustomization.
    /// </summary>
    [AddComponentMenu("VirtualVenues/Avatar")]
    public class Avatar : MonoBehaviour
    {
        [Header("Animation")]
        [SerializeField] private Animator _animator;

        [Header("Customization Slots")]
        [SerializeField] private List<RendererSlot> _rendererSlots = new List<RendererSlot>();
        [SerializeField] private List<ItemSlot> _itemSlots = new List<ItemSlot>();

        [Header("LOD")]
        [SerializeField] private LODGroup _lodGroup;
        [SerializeField] private float _cameraLookAtPointOffset = 0.75f;

        [Header("Fallback Content")]
        [SerializeField] private Material _fallbackBodyPaint;
        [SerializeField] private Material _fallbackFace;
        [SerializeField] private GameObject _fallbackGameObject;

        [Header("Loading Content")]
        [SerializeField] private Material _loadingMaterial;
        [SerializeField] private GameObject _loadingItem;

        [Header("Custom Animations (optional)")]
        [Tooltip("Override individual FutureFest character animations with your own clips. Use Humanoid clips so they retarget onto the rig. Any slot you leave out keeps the default FutureFest animation.")]
        [SerializeField] private List<AvatarAnimationOverride> _animationOverrides = new List<AvatarAnimationOverride>();

        // Public accessors for runtime swap
        public Animator Animator => _animator;
        public List<RendererSlot> RendererSlots => _rendererSlots;
        public List<ItemSlot> ItemSlots => _itemSlots;
        public LODGroup LodGroup => _lodGroup;
        public float CameraLookAtPointOffset => _cameraLookAtPointOffset;
        public Material FallbackBodyPaint => _fallbackBodyPaint;
        public Material FallbackFace => _fallbackFace;
        public GameObject FallbackGameObject => _fallbackGameObject;
        public Material LoadingMaterial => _loadingMaterial;
        public GameObject LoadingItem => _loadingItem;
        public IReadOnlyList<AvatarAnimationOverride> AnimationOverrides => _animationOverrides;
    }

    [Serializable]
    public class RendererSlot
    {
        public SlotCategory category;
        public List<Renderer> renderers = new List<Renderer>();
        public int materialIndex;
        public Material fallbackMaterial;
    }

    [Serializable]
    public class ItemSlot
    {
        public SlotCategory category;
        public Transform itemPivot;
        public GameObject fallbackItem;
    }

    /// <summary>
    /// Slot categories for avatar customization.
    /// Mirrors FutureFest.Character.Customization.SlotAssetCategory.
    /// </summary>
    public enum SlotCategory
    {
        Back,
        BodyPaint,
        Face,
        FacePlate,
        Team,
        Hat,
        Neck,
        Head,
        Avatar,
        Hair,
        Costume,
        ForeArm_L,
        ForeArm_R
    }

    /// <summary>
    /// A creator-supplied animation clip that overrides one of the FutureFest character animations at
    /// runtime. Applied via an AnimatorOverrideController on top of the FutureFest base controller
    /// (UPC-side), so the character's state machine and parameters are preserved.
    /// </summary>
    [Serializable]
    public class AvatarAnimationOverride
    {
        [Tooltip("Which FutureFest animation this clip replaces.")]
        public AvatarAnimationSlot slot;
        [Tooltip("Your replacement clip. Use a Humanoid clip so it retargets onto the rig.")]
        public AnimationClip clip;
    }

    /// <summary>
    /// The FutureFest character animations a creator can override. These map (UPC-side, via an
    /// AvatarAnimationClipMap) to the clips in the FutureFest base animator controller.
    /// </summary>
    public enum AvatarAnimationSlot
    {
        Idle,
        Walk,
        Run,
        Jump,
        FreeFall,
        Land,
        Swim,
        Dance,
        Sit,
        Talk,
        Interact,
        IdleTwitch0,
        IdleTwitch1
    }

#if UNITY_EDITOR
    /// <summary>
    /// Custom inspector for the VirtualVenues.Avatar authoring component: a "New Avatar" create-menu,
    /// guided item-slot creation (only valid, not-yet-used categories), and live validation. Lives here
    /// (runtime asmdef, editor-only) to match the Stage/SpawnPoint editor pattern; the package-safe
    /// AvatarValidator is shared with the Avatar Publisher.
    /// </summary>
    [CustomEditor(typeof(Avatar))]
    public class AvatarEditor : Editor
    {
        [MenuItem("GameObject/VirtualVenues/New Avatar", isValidateFunction: false, priority: 0)]
        private static void CreateAvatar(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Avatar");
            go.AddComponent<Avatar>();

            GameObject parent = menuCommand.context as GameObject;
            if (parent != null) { GameObjectUtility.SetParentAndAlign(go, parent); }

            Undo.RegisterCreatedObjectUndo(go, "Create Avatar");
            Selection.activeObject = go;
        }

        [MenuItem("GameObject/VirtualVenues/New Avatar", isValidateFunction: true)]
        private static bool ValidateCreateAvatar(MenuCommand menuCommand)
        {
            return true;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            Avatar avatar = (Avatar)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Authoring Tools", EditorStyles.boldLabel);

            if (GUILayout.Button(new GUIContent("Setup Avatar from Model",
                "Convert the rig to Humanoid (if needed), add/link an Animator and a 3-tier LODGroup, and wire them into this Avatar.")))
            {
                SetupAvatarFromModel(avatar);
            }

            if (GUILayout.Button(new GUIContent("Add Item Slot",
                "Add an attachment slot (Back, Hat, Hair, Neck, Head).")))
            {
                ShowAddItemSlotMenu(avatar);
            }

            EditorGUILayout.Space();
            DrawValidation(avatar);
        }

        private void DrawValidation(Avatar avatar)
        {
            List<AvatarCheck> checks = AvatarValidator.Validate(avatar);

            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
            if (checks.Count == 0)
            {
                EditorGUILayout.HelpBox("No issues found — this avatar is ready to publish.", MessageType.Info);
                return;
            }

            foreach (AvatarCheck c in checks)
            {
                MessageType type =
                    c.Severity == AvatarCheckSeverity.Error ? MessageType.Error :
                    c.Severity == AvatarCheckSeverity.Warning ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(c.Message, type);
            }
        }

        private void ShowAddItemSlotMenu(Avatar avatar)
        {
            var menu = new GenericMenu();
            foreach (SlotCategory category in AvatarValidator.ItemCategories)
            {
                SlotCategory captured = category;
                bool exists = avatar.ItemSlots.Exists(s => s != null && s.category == captured);
                if (exists)
                {
                    menu.AddDisabledItem(new GUIContent($"{category} (already added)"));
                }
                else
                {
                    menu.AddItem(new GUIContent(category.ToString()), false, () => AddItemSlot(avatar, captured));
                }
            }
            menu.ShowAsContext();
        }

        private void AddItemSlot(Avatar avatar, SlotCategory category)
        {
            Undo.RecordObject(avatar, "Add item slot");
            avatar.ItemSlots.Add(new ItemSlot { category = category });
            MarkDirty(avatar);
        }

        // --- Setup Avatar from Model -------------------------------------------------------------

        // Thin wrapper over the shared AvatarModelSetup.Configure: runs the rig/Animator/LODGroup setup on
        // this avatar's GameObject (with Undo), then links the resulting Animator + LODGroup into the
        // component. The same Configure powers the "Create Avatar Prefab from Model" asset menu action.
        private void SetupAvatarFromModel(Avatar avatar)
        {
            // The Avatar component and rig setup must be on the model ROOT (mirrors the runtime
            // swap/teardown and AvatarValidator.ValidatePlacement).
            if (avatar.transform.parent != null)
            {
                EditorUtility.DisplayDialog("Setup Avatar",
                    "The Avatar component must be on the model's ROOT object before running setup.", "OK");
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Setup Avatar from Model");

            AvatarModelSetup.Result result = AvatarModelSetup.Configure(avatar.gameObject, recordUndo: true);
            if (result.aborted) { return; }

            serializedObject.FindProperty("_animator").objectReferenceValue = result.animator;
            if (result.lodGroup != null) { serializedObject.FindProperty("_lodGroup").objectReferenceValue = result.lodGroup; }
            serializedObject.ApplyModifiedProperties();
            MarkDirty(avatar);

            Undo.CollapseUndoOperations(undoGroup);

            EditorUtility.DisplayDialog("Setup Avatar", result.summary, "OK");
        }

        private void MarkDirty(Avatar avatar)
        {
            EditorUtility.SetDirty(avatar);
            if (PrefabUtility.IsPartOfPrefabInstance(avatar))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(avatar);
            }
            serializedObject.Update();
        }
    }
#endif
}
