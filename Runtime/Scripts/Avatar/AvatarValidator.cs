#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues
{
    public enum AvatarCheckSeverity { Info, Warning, Error }

    public readonly struct AvatarCheck
    {
        public readonly AvatarCheckSeverity Severity;
        public readonly string Message;

        public AvatarCheck(AvatarCheckSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }
    }

    /// <summary>
    /// Editor-time validation for a VirtualVenues.Avatar authoring component. Pure data inspection over
    /// the Avatar's public model — no UnityEditor, no Addressables, no FutureFest runtime — so it stays
    /// package-safe and can be reused by both the Avatar custom inspector (AvatarEditor) and the
    /// Avatar Publisher's pre-publish checks.
    ///
    /// The skin/item category taxonomy below mirrors the runtime handlers in
    /// FutureFest.Character.Customization.AvatarCustomization (SetAsset / InitializeSkinContentRefs /
    /// InitializeItemContentRefs). That correspondence is a SOFT coupling: getting it wrong only means a
    /// slot silently does nothing at runtime — unlike the SlotCategory&lt;-&gt;SlotAssetCategory ordinal
    /// parity, which is hard-guarded by SlotCategoryParityCheck. Keep these lists in sync if the runtime
    /// gains or drops a per-slot handler.
    /// </summary>
    public static class AvatarValidator
    {
        /// <summary>Categories with a runtime SKIN (material-swap) handler. Use for RendererSlot.category.</summary>
        public static readonly IReadOnlyList<SlotCategory> SkinCategories = new[]
        {
            SlotCategory.BodyPaint, SlotCategory.Face, SlotCategory.FacePlate
        };

        /// <summary>Categories with a runtime ITEM (attachment) handler. Use for ItemSlot.category.</summary>
        public static readonly IReadOnlyList<SlotCategory> ItemCategories = new[]
        {
            SlotCategory.Back, SlotCategory.Hat, SlotCategory.Hair, SlotCategory.Neck, SlotCategory.Head
        };

        /// <summary>Returns every warning/error for the avatar. An empty list means the avatar is healthy.</summary>
        public static List<AvatarCheck> Validate(Avatar avatar)
        {
            var results = new List<AvatarCheck>();
            if (avatar == null)
            {
                results.Add(new AvatarCheck(AvatarCheckSeverity.Error, "No Avatar component to validate."));
                return results;
            }

            ValidatePlacement(avatar, results);
            ValidateAnimator(avatar, results);
            ValidateRendererSlots(avatar, results);
            ValidateItemSlots(avatar, results);
            ValidateAnimationOverrides(avatar, results);
            return results;
        }

        /// <summary>True if any check is an Error (would crash the runtime swap or fail a healthy publish).</summary>
        public static bool HasErrors(IEnumerable<AvatarCheck> checks)
        {
            foreach (AvatarCheck c in checks)
            {
                if (c.Severity == AvatarCheckSeverity.Error) { return true; }
            }
            return false;
        }

        private static void ValidatePlacement(Avatar avatar, List<AvatarCheck> results)
        {
            if (avatar.transform.parent == null) { return; }
            results.Add(new AvatarCheck(AvatarCheckSeverity.Error,
                "The Avatar component must be on the prefab ROOT. The runtime swap and teardown assume root placement, and a child-placed component is rejected at publish."));
        }

        private static void ValidateAnimator(Avatar avatar, List<AvatarCheck> results)
        {
            Animator animator = avatar.Animator;
            if (animator == null)
            {
                results.Add(new AvatarCheck(AvatarCheckSeverity.Error,
                    "Animator is not assigned. The avatar will not animate and cannot be published."));
                return;
            }

            // A shipped controller is a hard block, not a warning: it REPLACES the FutureFest character
            // controller VirtualVenues injects at runtime (so locomotion/dance/talk stop working unless the
            // creator reimplemented FF's whole state machine), and Addressables packs the controller's entire
            // animation-clip closure into the avatar bundle — megabytes per avatar. VVPreviewCharacterController
            // exists only to scrub animations in the editor; clear it before publishing.
            if (animator.runtimeAnimatorController != null)
            {
                results.Add(new AvatarCheck(AvatarCheckSeverity.Error,
                    $"The Animator has an Animator Controller assigned ('{animator.runtimeAnimatorController.name}'). " +
                    "Clear it — VirtualVenues injects the FutureFest character controller at runtime, and a shipped " +
                    "controller replaces it (the avatar will stand frozen) while adding all of its animation clips to " +
                    "your published bundle. VVPreviewCharacterController is for editor preview only. To ship your own " +
                    "animations, use the \"Custom Animations\" list on this component instead."));
            }

            UnityEngine.Avatar rig = animator.avatar;
            if (rig == null)
            {
                results.Add(new AvatarCheck(AvatarCheckSeverity.Warning,
                    "The assigned Animator has no Avatar rig. Assign a Humanoid rig so the FutureFest character controller and animations can drive it."));
            }
            else if (!rig.isHuman)
            {
                results.Add(new AvatarCheck(AvatarCheckSeverity.Warning,
                    "The Animator's rig is not Humanoid. The FutureFest character controller is applied only to Humanoid rigs, so a Generic rig will not play locomotion or dance animations."));
            }
        }

        // Skin/renderer slots are intentionally not surfaced in the authoring tooling for now, so we keep
        // only the duplicate-category guard here: a duplicate renderer-slot category THROWS at runtime
        // (AvatarCustomization.Initialize indexes slots by category into a dictionary).
        private static void ValidateRendererSlots(Avatar avatar, List<AvatarCheck> results)
        {
            List<RendererSlot> rendererSlots = avatar.RendererSlots;
            if (rendererSlots == null) { return; }

            var seen = new HashSet<SlotCategory>();
            for (int i = 0; i < rendererSlots.Count; i++)
            {
                RendererSlot slot = rendererSlots[i];
                if (slot == null) { continue; }

                if (!seen.Add(slot.category))
                {
                    results.Add(new AvatarCheck(AvatarCheckSeverity.Error,
                        $"Renderer slot {i} ('{slot.category}'): duplicate renderer-slot category. The runtime indexes renderer slots by category and THROWS on a duplicate — remove the extra slot."));
                }
            }
        }

        private static void ValidateItemSlots(Avatar avatar, List<AvatarCheck> results)
        {
            List<ItemSlot> itemSlots = avatar.ItemSlots;
            if (itemSlots == null) { return; }

            var seen = new HashSet<SlotCategory>();
            for (int i = 0; i < itemSlots.Count; i++)
            {
                ItemSlot slot = itemSlots[i];
                if (slot == null) { continue; }

                string label = $"Item slot {i} ('{slot.category}')";

                if (!seen.Add(slot.category))
                {
                    results.Add(new AvatarCheck(AvatarCheckSeverity.Error,
                        $"{label}: duplicate item-slot category. The runtime indexes item slots by category and THROWS on a duplicate — remove the extra slot."));
                }

                if (!Contains(ItemCategories, slot.category))
                {
                    results.Add(new AvatarCheck(AvatarCheckSeverity.Warning,
                        $"{label}: this category has no item handler at runtime. Item slots only do something for Back, Hat, Hair, Neck, or Head."));
                }

                if (slot.itemPivot == null)
                {
                    results.Add(new AvatarCheck(AvatarCheckSeverity.Warning,
                        $"{label}: no pivot Transform assigned. Items for this slot will not attach — assign the bone/empty the item should parent under."));
                }
            }
        }

        private static void ValidateAnimationOverrides(Avatar avatar, List<AvatarCheck> results)
        {
            IReadOnlyList<AvatarAnimationOverride> overrides = avatar.AnimationOverrides;
            if (overrides == null) { return; }

            var seen = new HashSet<AvatarAnimationSlot>();
            for (int i = 0; i < overrides.Count; i++)
            {
                AvatarAnimationOverride ov = overrides[i];
                if (ov == null) { continue; }

                if (ov.clip == null)
                {
                    results.Add(new AvatarCheck(AvatarCheckSeverity.Warning,
                        $"Animation override {i} ('{ov.slot}') has no clip assigned — it will be ignored."));
                }
                else if (!ov.clip.isHumanMotion)
                {
                    results.Add(new AvatarCheck(AvatarCheckSeverity.Warning,
                        $"Animation override '{ov.slot}' uses clip '{ov.clip.name}', which is not a Humanoid clip. It may not retarget onto the avatar — use a Humanoid clip."));
                }

                if (!seen.Add(ov.slot))
                {
                    results.Add(new AvatarCheck(AvatarCheckSeverity.Warning,
                        $"Animation override slot '{ov.slot}' is listed more than once — only the last clip will apply."));
                }
            }
        }

        private static bool Contains(IReadOnlyList<SlotCategory> list, SlotCategory value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) { return true; }
            }
            return false;
        }
    }
}
#endif
