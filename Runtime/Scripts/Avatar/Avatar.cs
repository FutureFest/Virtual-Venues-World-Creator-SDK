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

        [Tooltip("Spawned in any Mesh / Rigged Mesh slot whose cosmetic fails to load and that has no Fallback of its own. Leave empty to show nothing.")]
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

    /// <summary>
    /// How a cosmetic equipped into a slot attaches to the avatar.
    ///
    /// The VALUES are permanent, for the same reason as <see cref="SlotCategory"/>: published avatar
    /// bundles store this as its underlying int and are never re-serialized. RENAMING a member is safe
    /// (nothing writes the name — checked); REORDERING or RENUMBERING one silently re-points every
    /// already-published slot at the wrong kind. Pinned explicitly below so that cannot drift.
    /// </summary>
    public enum AvatarSlotKind
    {
        /// <summary>Instantiate the cosmetic prefab under <see cref="AvatarSlot.itemPivot"/>. Hats, backpacks. (Named "Attachment" before 0.9.19.)</summary>
        Mesh = 0,
        /// <summary>Swap material <see cref="AvatarSlot.materialIndex"/> on <see cref="AvatarSlot.renderers"/>. Skins, faces.</summary>
        Material = 1,
        /// <summary>Rebind the cosmetic's SkinnedMeshRenderers onto this avatar's skeleton. Jackets, hoodies. (Named "SkinnedMesh" before 0.9.19.)</summary>
        RiggedMesh = 2,
        /// <summary>Set a texture property on material <see cref="AvatarSlot.materialIndex"/> of <see cref="AvatarSlot.renderers"/>. Skin tones, decals.</summary>
        Texture = 3
    }

    public static class AvatarSlotKindExtensions
    {
        /// <summary>Material and Texture slots act on <see cref="AvatarSlot.renderers"/>; the rest attach under a pivot.</summary>
        public static bool TargetsRenderers(this AvatarSlotKind kind)
        {
            return kind == AvatarSlotKind.Material || kind == AvatarSlotKind.Texture;
        }
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

        [Tooltip("Mesh: parent the cosmetic prefab under the pivot (hats, backpacks).\n" +
                 "Material: swap a material on the listed renderers (skins, faces).\n" +
                 "Texture: set a texture property on a material of the listed renderers (skin tones, decals).\n" +
                 "Rigged Mesh: rebind the cosmetic onto this avatar's skeleton (jackets, hoodies). The garment " +
                 "MUST be authored against THIS rig — same bone names and bind poses — or it will deform incorrectly.")]
        public AvatarSlotKind kind;

        [Tooltip("Mesh / Rigged Mesh: the bone or empty the cosmetic parents under.")]
        public Transform itemPivot;

        [Tooltip("Material / Texture: the renderers whose material is swapped or textured.")]
        public List<Renderer> renderers = new List<Renderer>();
        [Tooltip("Material / Texture: which material slot on those renderers is targeted.")]
        public int materialIndex;
        [Tooltip("Texture: shader property the texture is assigned to.")]
        public string textureProperty = "_BaseMap";

        [Tooltip("Rigged Mesh: body renderers hidden while something is equipped here (restored on unequip).")]
        public List<Renderer> hideWhenEquipped = new List<Renderer>();

        [Tooltip("Shown when the cosmetic fails to load. A Material for Material slots, a Texture2D for Texture slots, a GameObject otherwise.")]
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

        // The camera look-at point the runtime PlayerCameraRig builds from CameraLookAtPointOffset
        // (_lookAtPoint.localPosition = new Vector3(0f, offset, 0f)), so the creator can see it instead
        // of typing a blind number.
        private void OnSceneGUI()
        {
            Avatar avatar = (Avatar)target;
            Transform root = avatar.transform;
            Vector3 point = root.position + root.up * avatar.CameraLookAtPointOffset;

            Handles.color = Color.cyan;
            Handles.DrawDottedLine(root.position, point, 3f);
            if (Event.current.type == EventType.Repaint)
            {
                Handles.SphereHandleCap(0, point, Quaternion.identity, 0.04f, EventType.Repaint);
            }
            Handles.Label(point, "Camera Look At");
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
                    kind = AvatarSlotKind.Mesh,
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

    /// <summary>
    /// Draws an <see cref="AvatarSlot"/> showing ONLY the fields the runtime actually reads for the
    /// chosen kind — the rest is silently dropped by AvatarCustomization.ConvertSlots, so showing it
    /// just invites creators to fill in data that never arrives. Picked up automatically by the Slots
    /// list in <see cref="AvatarEditor"/>'s DrawDefaultInspector.
    /// </summary>
    [CustomPropertyDrawer(typeof(AvatarSlot))]
    public class AvatarSlotDrawer : PropertyDrawer
    {
        // Fields read by AvatarCustomization.ConvertSlots / ChangeEquippedItem, in draw order.
        // "fallback" is drawn separately (it needs a per-kind type filter) and is always last.
        private static readonly string[] MeshRows = { "slotId", "kind", "itemPivot" };
        private static readonly string[] MaterialRows = { "slotId", "kind", "renderers", "materialIndex" };
        private static readonly string[] TextureRows = { "slotId", "kind", "renderers", "materialIndex", "textureProperty" };
        private static readonly string[] RiggedMeshRows = { "slotId", "kind", "itemPivot", "hideWhenEquipped" };

        private static AvatarSlotKind KindOf(SerializedProperty property)
        {
            return (AvatarSlotKind)property.FindPropertyRelative("kind").intValue;
        }

        private static string[] RowsFor(SerializedProperty property)
        {
            AvatarSlotKind kind = KindOf(property);
            if (kind == AvatarSlotKind.Material) { return MaterialRows; }
            if (kind == AvatarSlotKind.Texture) { return TextureRows; }
            if (kind == AvatarSlotKind.RiggedMesh) { return RiggedMeshRows; }
            return MeshRows;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            Rect row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            // The list may hand us a plain "Element N"; the slot name is the useful header.
            string slotId = property.FindPropertyRelative("slotId").stringValue;
            GUIContent header = string.IsNullOrWhiteSpace(slotId) ? label : new GUIContent(slotId);
            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, header, toggleOnLabelClick: true);
            if (!property.isExpanded) { return; }

            EditorGUI.indentLevel++;
            foreach (string name in RowsFor(property))
            {
                SerializedProperty prop = property.FindPropertyRelative(name);
                row.y += row.height + spacing;
                row.height = EditorGUI.GetPropertyHeight(prop, includeChildren: true);
                // The serialized name stays "kind" (no data change); only the label is clearer.
                GUIContent rowLabel = name == "kind" ? new GUIContent("Slot Type", prop.tooltip) : null;
                if (rowLabel != null) { EditorGUI.PropertyField(row, prop, rowLabel, true); }
                else { EditorGUI.PropertyField(row, prop, true); }
            }

            // Typed picker: the runtime does `slot.fallback as GameObject` / `as Material` / `as Texture2D`,
            // so a wrong pick becomes a silent null. Existing mismatched values are left alone, never coerced.
            AvatarSlotKind kind = KindOf(property);
            Type fallbackType = kind == AvatarSlotKind.Material ? typeof(Material)
                              : kind == AvatarSlotKind.Texture ? typeof(Texture2D)
                              : typeof(GameObject);
            string fallbackTip = kind == AvatarSlotKind.Material ? "Material shown when this slot's cosmetic fails to load."
                               : kind == AvatarSlotKind.Texture ? "Texture shown when this slot's cosmetic fails to load."
                               : "Prefab spawned when this slot's cosmetic fails to load.";
            row.y += row.height + spacing;
            row.height = EditorGUIUtility.singleLineHeight;
            EditorGUI.ObjectField(row, property.FindPropertyRelative("fallback"), fallbackType, new GUIContent("Fallback", fallbackTip));

            EditorGUI.indentLevel--;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) { return line; }

            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float height = line;
            foreach (string name in RowsFor(property))
            {
                height += spacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(name), includeChildren: true);
            }
            return height + spacing + line; // + fallback
        }
    }
#endif
}
