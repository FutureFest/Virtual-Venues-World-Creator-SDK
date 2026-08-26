using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
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
        [Tooltip("Creator-named cosmetic slots. The slot name is the wardrobe category and the Addressables prefix: a cosmetic for slot \"Hat\" must be published with the address Hat_<assetId>.")]
        [SerializeField] private List<AvatarSlot> _slots = new List<AvatarSlot>();

        // LEGACY (pre-0.9.17) slot lists. Kept forever and still read at runtime: avatar bundles ship a
        // TypeTree, so MonoBehaviour data is matched by FIELD NAME. Deleting these fields drops the slot
        // data out of every already-published avatar — it would still spawn, just with zero slots.
        [HideInInspector, SerializeField] private List<RendererSlot> _rendererSlots = new List<RendererSlot>();
        [HideInInspector, SerializeField] private List<ItemSlot> _itemSlots = new List<ItemSlot>();

        [Header("LOD")]
        [SerializeField] private LODGroup _lodGroup;
        [SerializeField] private float _cameraLookAtPointOffset = 0.75f;

        [Header("Fallback / Loading Content (optional)")]
        // The FormerlySerializedAs attributes below are PERMANENT, for the same reason as the legacy slot
        // lists above: bundles are never re-serialized, so the name remap runs on every load of an
        // already-published avatar, forever. Deleting an attribute silently blanks that avatar's fallback.
        [Tooltip("Shown on any Material slot whose cosmetic fails to load and that has no Fallback of its own. Leave empty to keep the avatar's own material.")]
        [FormerlySerializedAs("_fallbackFace")]
        [SerializeField] private Material _fallbackMaterial;

        [Tooltip("Spawned in any Attachment / SkinnedMesh slot whose cosmetic fails to load and that has no Fallback of its own. Leave empty to show nothing.")]
        [FormerlySerializedAs("_fallbackGameObject")]
        [SerializeField] private GameObject _fallbackItem;

        [Tooltip("Shown on Material slots while a cosmetic downloads. Empty falls back to the Fallback Material.")]
        [SerializeField] private Material _loadingMaterial;
        [Tooltip("Spawned in item slots while a cosmetic downloads. Empty falls back to the Fallback Item.")]
        [SerializeField] private GameObject _loadingItem;

        [Header("Custom Animations (optional)")]
        [Tooltip("Override individual FutureFest character animations with your own clips. Use Humanoid clips so they retarget onto the rig. Any slot you leave out keeps the default FutureFest animation.")]
        [SerializeField] private List<AvatarAnimationOverride> _animationOverrides = new List<AvatarAnimationOverride>();

        // Public accessors for runtime swap
        public Animator Animator => _animator;
        public List<AvatarSlot> Slots => _slots;
        public List<RendererSlot> RendererSlots => _rendererSlots;
        public List<ItemSlot> ItemSlots => _itemSlots;
        public LODGroup LodGroup => _lodGroup;
        public float CameraLookAtPointOffset => _cameraLookAtPointOffset;
        public Material FallbackMaterial => _fallbackMaterial;
        public GameObject FallbackItem => _fallbackItem;
        public Material LoadingMaterial => _loadingMaterial;
        public GameObject LoadingItem => _loadingItem;
        public IReadOnlyList<AvatarAnimationOverride> AnimationOverrides => _animationOverrides;
    }

    /// <summary>How a cosmetic equipped into a slot attaches to the avatar.</summary>
    public enum AvatarSlotKind
    {
        /// <summary>Instantiate the cosmetic prefab under <see cref="AvatarSlot.itemPivot"/>. Hats, backpacks.</summary>
        Attachment,
        /// <summary>Swap material <see cref="AvatarSlot.materialIndex"/> on <see cref="AvatarSlot.renderers"/>. Skins, faces.</summary>
        Material,
        /// <summary>Rebind the cosmetic's SkinnedMeshRenderers onto this avatar's skeleton. Jackets, hoodies.</summary>
        SkinnedMesh
    }

    /// <summary>
    /// One creator-named cosmetic slot. The <see cref="slotId"/> is the wardrobe category AND the
    /// Addressables address prefix — a cosmetic in slot "Hat" is loaded as "Hat_&lt;assetId&gt;".
    /// </summary>
    [Serializable]
    public class AvatarSlot
    {
        [Tooltip("Creator-named, e.g. \"Hat\" or \"Jacket\". Shown as the wardrobe tab label. \"Avatar\" and \"Costume\" are reserved.")]
        public string slotId;

        [Tooltip("Attachment: parent the cosmetic prefab under the pivot (hats, backpacks).\n" +
                 "Material: swap a material on the listed renderers (skins, faces).\n" +
                 "SkinnedMesh: rebind the cosmetic onto this avatar's skeleton (jackets, hoodies). The garment " +
                 "MUST be authored against THIS rig — same bone names and bind poses — or it will deform incorrectly.")]
        public AvatarSlotKind kind;

        [Tooltip("Attachment / SkinnedMesh: the bone or empty the cosmetic parents under.")]
        public Transform itemPivot;

        [Tooltip("Material: the renderers whose material is swapped.")]
        public List<Renderer> renderers = new List<Renderer>();
        [Tooltip("Material: which material slot on those renderers is swapped.")]
        public int materialIndex;

        [Tooltip("SkinnedMesh: body renderers hidden while something is equipped here (restored on unequip).")]
        public List<Renderer> hideWhenEquipped = new List<Renderer>();

        [Tooltip("Shown when the cosmetic fails to load. A Material for Material slots, a GameObject otherwise.")]
        public UnityEngine.Object fallback;
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
    /// LEGACY (pre-0.9.17) fixed slot categories, superseded by the free-string <see cref="AvatarSlot.slotId"/>.
    /// Kept permanently: it is the deserialization path for already-published avatar bundles, whose slot
    /// categories are stored as this enum's underlying int. Do not remove, reorder, or renumber it.
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
    /// model setup, legacy-slot conversion, and live validation. Lives here
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
            Avatar avatar = (Avatar)target;

            // First thing you press on a fresh avatar, so it goes above the fields it fills in.
            if (GUILayout.Button(new GUIContent("Setup Avatar from Model",
                "Convert the rig to Humanoid (if needed), add/link an Animator and a 3-tier LODGroup, and wire them into this Avatar.")))
            {
                SetupAvatarFromModel(avatar);
            }

            EditorGUILayout.Space();
            DrawDefaultInspector();

            EditorGUILayout.Space();
            DrawLegacyConversion(avatar);

            EditorGUILayout.Space();
            DrawValidation(avatar);
        }

        // Slots are added with the Slots list's own "+" button; this is only the pre-0.9.17 migration path.
        private void DrawLegacyConversion(Avatar avatar)
        {
            if (avatar.RendererSlots.Count + avatar.ItemSlots.Count == 0) { return; }

            if (GUILayout.Button(new GUIContent($"Convert legacy slots ({avatar.RendererSlots.Count + avatar.ItemSlots.Count})",
                "Copy this avatar's pre-0.9.17 renderer/item slots into the new slot list, keeping their category names as slot names.")))
            {
                ConvertLegacySlots(avatar);
            }
        }

        private void ConvertLegacySlots(Avatar avatar)
        {
            Undo.RecordObject(avatar, "Convert legacy slots");

            int added = 0;
            foreach (RendererSlot legacy in avatar.RendererSlots)
            {
                if (legacy == null || HasSlot(avatar, legacy.category.ToString())) { continue; }
                avatar.Slots.Add(new AvatarSlot
                {
                    slotId = legacy.category.ToString(),
                    kind = AvatarSlotKind.Material,
                    renderers = legacy.renderers != null ? new List<Renderer>(legacy.renderers) : new List<Renderer>(),
                    materialIndex = legacy.materialIndex,
                    fallback = legacy.fallbackMaterial
                });
                added++;
            }

            foreach (ItemSlot legacy in avatar.ItemSlots)
            {
                if (legacy == null || HasSlot(avatar, legacy.category.ToString())) { continue; }
                avatar.Slots.Add(new AvatarSlot
                {
                    slotId = legacy.category.ToString(),
                    kind = AvatarSlotKind.Attachment,
                    itemPivot = legacy.itemPivot,
                    fallback = legacy.fallbackItem
                });
                added++;
            }

            MarkDirty(avatar);
            EditorUtility.DisplayDialog("Convert legacy slots",
                added == 0 ? "Nothing to convert — every legacy slot is already in the new list."
                           : $"Added {added} slot(s). The legacy lists are kept as-is; they are ignored once this list is non-empty.", "OK");
        }

        private static bool HasSlot(Avatar avatar, string slotId)
        {
            return avatar.Slots.Exists(s => s != null && string.Equals(s.slotId, slotId, System.StringComparison.OrdinalIgnoreCase));
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
