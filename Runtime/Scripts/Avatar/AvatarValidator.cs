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
    /// Slot ids are creator-named, so there is no category taxonomy to check them against: the runtime
    /// routes by whatever the avatar declares. What IS checked is what actually breaks — duplicate slot
    /// ids (AvatarCustomization.Initialize indexes slots by id with Dictionary.Add, which THROWS) and the
    /// two reserved ids that mean "swap the whole avatar prefab", not "equip into a slot".
    /// </summary>
    public static class AvatarValidator
    {
        /// <summary>Slot ids the runtime handles as whole-prefab swaps (AvatarContainer), never as slots.</summary>
        public static readonly IReadOnlyList<string> ReservedSlotIds = new[] { "Avatar", "Costume" };

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
            ValidateSlots(avatar, results);
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

            // Warning, not an error (0.9.15 blocked here; downgraded in 0.9.16). The runtime already handles
            // this invisibly: UPC probes a shipped controller and injects the FutureFest one whenever it doesn't
            // implement FF's animation contract, so the avatar animates either way and needs no republish. The
            // only real cost is that Addressables packs the controller's animation-clip closure into the bundle,
            // so clearing the field keeps the creator's download smaller.
            if (animator.runtimeAnimatorController != null)
            {
                results.Add(new AvatarCheck(AvatarCheckSeverity.Warning,
                    $"The Animator has an Animator Controller assigned ('{animator.runtimeAnimatorController.name}'). " +
                    "This won't break animation — VirtualVenues replaces it at runtime with the FutureFest character " +
                    "controller unless yours implements FF's full animation contract. But its animation clips get " +
                    "packed into your published bundle, making the download bigger for no benefit, so clearing the " +
                    "field is recommended. VVPreviewCharacterController is for editor preview only. To ship your own " +
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

        private static void ValidateSlots(Avatar avatar, List<AvatarCheck> results)
        {
            List<AvatarSlot> slots = avatar.Slots;
            if (slots == null) { return; }

            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < slots.Count; i++)
            {
                AvatarSlot slot = slots[i];
                if (slot == null) { continue; }

                string label = $"Slot {i} ('{slot.slotId}')";

                if (string.IsNullOrWhiteSpace(slot.slotId))
                {
                    results.Add(new AvatarCheck(AvatarCheckSeverity.Error,
                        $"Slot {i} has no name. Give it a name — it is the wardrobe tab label and the Addressables prefix for its cosmetics."));
                    continue;
                }

                if (!seen.Add(slot.slotId))
                {
                    results.Add(new AvatarCheck(AvatarCheckSeverity.Error,
                        $"{label}: duplicate slot name (names are case-insensitive). The runtime indexes slots by name and THROWS on a duplicate — rename or remove the extra slot."));
                }

                if (ContainsIgnoreCase(ReservedSlotIds, slot.slotId))
                {
                    results.Add(new AvatarCheck(AvatarCheckSeverity.Error,
                        $"{label}: '{slot.slotId}' is a reserved name. It swaps the whole avatar prefab, so a slot with that name never receives anything — pick another name."));
                }

                if (slot.kind != AvatarSlotKind.Material && slot.itemPivot == null)
                {
                    results.Add(new AvatarCheck(AvatarCheckSeverity.Warning,
                        $"{label}: no pivot Transform assigned. Cosmetics for this slot will not attach — assign the bone or empty they should parent under."));
                }

                if (slot.kind == AvatarSlotKind.Material && (slot.renderers == null || slot.renderers.Count == 0))
                {
                    results.Add(new AvatarCheck(AvatarCheckSeverity.Warning,
                        $"{label}: a Material slot with no renderers does nothing. Add the renderers whose material this slot swaps."));
                }

                if (slot.kind == AvatarSlotKind.SkinnedMesh)
                {
                    results.Add(new AvatarCheck(AvatarCheckSeverity.Info,
                        $"{label}: SkinnedMesh cosmetics must be authored against THIS avatar's rig (same bone names and bind poses). A garment rigged to a different skeleton will deform incorrectly."));
                }
            }

            if (slots.Count > 0 || (avatar.RendererSlots.Count + avatar.ItemSlots.Count) == 0) { return; }
            results.Add(new AvatarCheck(AvatarCheckSeverity.Warning,
                "This avatar still uses the old fixed-category slots. They keep working, but new features (creator-named slots, skinned garments) need the new list — use \"Convert legacy slots\" on the Avatar component."));
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

        private static bool ContainsIgnoreCase(IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, System.StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }
    }
}
#endif
