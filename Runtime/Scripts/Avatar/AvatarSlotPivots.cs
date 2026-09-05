using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues
{
    /// <summary>
    /// Makes a badly-authored slot pivot harmless at runtime.
    ///
    /// Equipping a cosmetic begins by DESTROYING EVERY CHILD of the slot's pivot
    /// (AvatarCustomization.ChangeEquippedItem), so a pivot has to be a dedicated empty that owns nothing
    /// but the garment. Point it at the avatar root or at a rig node and the first equip silently deletes
    /// the body and the skeleton — no exception, no log, the avatar just vanishes. That shipped in a
    /// creator project and cost a day to find, because the code was byte-identical to the project where
    /// it worked; only the authoring differed.
    ///
    /// Rather than block it at publish time (which would need every affected avatar republished), the
    /// runtime slips a per-slot container transform under the unsafe pivot and equips into that. Clearing
    /// then only ever destroys that one slot's content, and every already-published avatar is healed
    /// without a republish.
    ///
    /// This is RUNTIME code, not editor-only: it ships in player builds.
    /// </summary>
    public static class AvatarSlotPivots
    {
        private const string ContainerPrefix = "__slot_";

        /// <summary>
        /// Returns an array parallel to <paramref name="slots"/>: the authored pivot where it is already
        /// safe to clear, otherwise a container transform created for that slot alone. Every named
        /// item slot comes back with a usable pivot; Material / Texture slots (which ignore itemPivot),
        /// null slots and unnamed slots come back null.
        ///
        /// A healthy dedicated pivot is returned UNCHANGED. That matters: the Avatar Publisher does not
        /// strip worn items, so an avatar published dressed carries its garment as a child of the pivot.
        /// Wrapping such a pivot would stop that item being cleared and the avatar would render the old
        /// garment and the new one at once.
        /// </summary>
        public static Transform[] EnsureSafePivots(Avatar avatar, List<AvatarSlot> slots)
        {
            var pivots = new Transform[slots != null ? slots.Count : 0];
            if (avatar == null || slots == null) { return pivots; }

            SkinnedMeshRenderer[] skinned = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            // How many item slots claim each pivot. Two slots on one pivot evict each other's garment.
            var claims = new Dictionary<Transform, int>();
            for (int i = 0; i < slots.Count; i++)
            {
                AvatarSlot slot = slots[i];
                if (!TakesAnItem(slot) || slot.itemPivot == null) { continue; }
                claims.TryGetValue(slot.itemPivot, out int count);
                claims[slot.itemPivot] = count + 1;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                AvatarSlot slot = slots[i];
                if (!TakesAnItem(slot)) { continue; }

                Transform pivot = slot.itemPivot;
                if (IsSafe(avatar, pivot, claims, skinned))
                {
                    pivots[i] = pivot;
                    continue;
                }

                // A null pivot has nowhere else to go, so the container hangs off the root. For a Rigged
                // Mesh slot that is a full fix — the retargeter rebinds the garment onto the skeleton, so
                // where its transform hangs is irrelevant. For a Mesh slot the item lands at the avatar's
                // origin, i.e. at its feet: deliberate, because guessing a bone would put the hat
                // somewhere plausible-but-wrong and hide the mistake. Still better than today, where the
                // cosmetic loads and then attaches to nothing at all.
                pivots[i] = GetOrCreate(pivot != null ? pivot : avatar.transform, ContainerPrefix + slot.slotId);
            }

            return pivots;
        }

        private static bool TakesAnItem(AvatarSlot slot)
        {
            return slot != null
                && !slot.kind.TargetsRenderers()
                && !string.IsNullOrWhiteSpace(slot.slotId);
        }

        private static bool IsSafe(Avatar avatar, Transform pivot, Dictionary<Transform, int> claims, SkinnedMeshRenderer[] skinned)
        {
            if (pivot == null) { return false; }
            if (pivot == avatar.transform) { return false; }
            if (claims[pivot] > 1) { return false; }
            return !IsRigNode(pivot, skinned);
        }

        /// <summary>
        /// A pivot set to a rig node is the same deletion wearing a disguise: in this asset family the body
        /// mesh is a SIBLING of the armature (see SkinnedMeshRetargeter.DestroySourceRigs), so nothing
        /// renderable lives under the pivot and a "does anything live here?" check sails past it — while
        /// clearing it still destroys the bones the body is skinned to. Walk the bone arrays instead.
        /// Strict descendants only: clearing destroys a pivot's children, not the pivot, so a pivot that
        /// IS a leaf bone stays legal and is common.
        /// </summary>
        private static bool IsRigNode(Transform pivot, SkinnedMeshRenderer[] skinned)
        {
            for (int i = 0; i < skinned.Length; i++)
            {
                SkinnedMeshRenderer smr = skinned[i];
                if (smr == null || smr.transform.IsChildOf(pivot)) { continue; }

                Transform[] bones = smr.bones;
                if (bones == null) { continue; }

                for (int b = 0; b < bones.Length; b++)
                {
                    Transform bone = bones[b];
                    if (bone != null && bone != pivot && bone.IsChildOf(pivot)) { return true; }
                }
            }
            return false;
        }

        // Reuses an existing container of that name (direct children only — Transform.Find without a
        // slash), so a second ConfigureFrom cannot stack containers inside each other.
        private static Transform GetOrCreate(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) { return existing; }

            var container = new GameObject(name).transform;
            // worldPositionStays: false keeps the fresh object's identity local TRS, so the garment lands
            // exactly where the authored pivot would have put it.
            container.SetParent(parent, false);
            return container;
        }
    }
}
