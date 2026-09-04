# Changelog

All notable changes to `com.virtualvenues.sdk`.

## [0.9.20] - 2026-08-31

### Fixed — a slot pivot can no longer delete the avatar

Equipping a cosmetic starts by destroying **every child of the slot's pivot**. A pivot pointed at the
avatar root — or at a bone inside the rig — therefore wiped the skeleton and the body on the first
equip. There was no exception and no log: the character simply vanished, and the same scripts worked
fine in a project where the pivots happened to be authored correctly. Diagnosing one case took a day.

The runtime now gives such a slot a container transform of its own (`__slot_<slotId>`, created under
the authored pivot) and equips into that, so clearing only ever destroys that one slot's garment. A
slot gets a container when its pivot:

- **is the avatar itself**, or
- is a **rig node** — clearing it would destroy bones a SkinnedMeshRenderer is using. The body mesh is
  usually a *sibling* of the armature, so nothing renderable lives under the pivot and the mistake is
  invisible by eye, or
- is **shared with another slot**, so equipping into either would evict the other, or
- is **missing entirely** — the cosmetic used to load and then silently attach to nothing. It now
  attaches at the avatar's origin. For a Rigged Mesh slot that is a complete fix (the garment is rebound
  onto the skeleton); for a Mesh slot the item sits at the avatar's feet until a pivot is assigned.

A healthy dedicated pivot is left exactly as authored, so an avatar published wearing something still
has that item replaced rather than doubled. Material slots are untouched — they ignore `itemPivot`.

**This heals every already-published avatar with no republish.** The authoring rule is unchanged and
still worth following: give every Mesh / Rigged Mesh slot its **own empty transform**, placed **outside
the rig** (a `Slots` object beside the armature, one empty child per slot).


## [0.9.19] - 2026-08-26

### Changed — BREAKING (`VirtualVenues.AvatarSlotKind`)

The slot kinds are renamed to describe what a creator is choosing between — does this piece bend
with the body, or just sit on it — rather than the mechanism used to attach it.

| Old | New | Inspector label |
|---|---|---|
| `AvatarSlotKind.Attachment` | `AvatarSlotKind.Mesh` | **Mesh** |
| `AvatarSlotKind.Material` | *(unchanged)* | **Material** |
| `AvatarSlotKind.SkinnedMesh` | `AvatarSlotKind.RiggedMesh` | **Rigged Mesh** |

Behaviour is unchanged. This is a rename only.

**No data migration needed.** The enum's underlying values are unchanged — and are now pinned
explicitly (`Mesh = 0`, `Material = 1`, `RiggedMesh = 2`) so they cannot drift in a future
reorder — so **existing avatar prefabs and already-published avatar bundles keep their slot
kinds**. Nothing serializes the member *name*, on disk or on the wire.

Only **source** breaks: an editor script referencing `AvatarSlotKind.Attachment` or
`AvatarSlotKind.SkinnedMesh` no longer compiles. Rename the reference. If you configure slots in
the Inspector, there is nothing to do.

### Changed (`Avatar` inspector)

- A slot now shows **only the fields that slot type actually uses**. Previously every slot showed
  every field, and the ones the runtime ignores were silently discarded on load — a SkinnedMesh
  slot offered `Renderers` and `Material Index`, a Material slot offered `Item Pivot`.

  | Slot Type | Fields shown |
  |---|---|
  | Mesh | Item Pivot, Fallback |
  | Material | Renderers, Material Index, Fallback |
  | Rigged Mesh | Item Pivot, Hide When Equipped, Fallback |

- The `Kind` field is labelled **Slot Type**. The serialized field name is unchanged.
- **`Fallback`** is now typed to the slot: a Material slot offers only Materials, the other two only
  prefabs. It previously accepted any Object, and a wrong pick became a silent `null` at runtime.

### Added

- **Camera Look At Point Offset** is drawn in the Scene view while the Avatar is selected — a dotted
  line from the root to a labelled marker at the point the player camera frames. It updates live, so
  the offset can be dialled in by eye instead of by trial build.

## [0.9.18] - 2026-08-21

### Changed — BREAKING (`VirtualVenues.Avatar`)

The `Fallback Content` / `Loading Content` block was named after FutureBot, the original built-in
avatar, back when slots were a fixed enum (`BodyPaint`, `Face`, `Hat`, …). Slots are creator-named
now, so a per-slot-name default no longer makes sense: on an avatar with a `Shirt` slot, a failed
Shirt cosmetic was falling back to a material called **Face**.

The two component-level material fallbacks are merged into one generic **Fallback Material**, and the
inspector block is now **Fallback Material / Fallback Item / Loading Material / Loading Item**.

Precedence is unchanged: the **per-slot `Fallback` always wins**; these are the last resort.

**Removed properties** — a script referencing any of these will no longer compile:

| Removed | Use instead |
|---|---|
| `Avatar.FallbackFace` | `Avatar.FallbackMaterial` |
| `Avatar.FallbackGameObject` | `Avatar.FallbackItem` |
| `Avatar.FallbackBodyPaint` | `Avatar.FallbackMaterial` — see the data note below |

No `[Obsolete]` forwarders were left for these. `FallbackBodyPaint` has no backing field any more and
could only ever return null; a compile error is better than a silent null at runtime.

### Data migration

`FormerlySerializedAs` remaps the serialized fields automatically, so **existing avatar prefabs and
already-published avatar bundles keep their values** — `_fallbackFace` → `_fallbackMaterial`,
`_fallbackGameObject` → `_fallbackItem`. Nothing to do by hand.

Two things do NOT survive the upgrade:

1. **`_fallbackBodyPaint` is dropped.** If an avatar has a bodypaint-style Material slot with **no
   per-slot `Fallback` of its own**, that slot now falls back to the single Fallback Material — which
   may be a face material. Fix: set the slot's own `Fallback`, which takes priority anyway. Avatars
   whose bodypaint slot already carries a per-slot `Fallback` are unaffected.
2. **Prefab-variant overrides of these fields are lost.** `FormerlySerializedAs` does not remap
   `m_Modifications` property paths, so a *variant* that overrode `_fallbackFace` or
   `_fallbackGameObject` silently reverts to its base prefab's value. Re-assign the field on the
   variant after upgrading. Non-variant prefabs are unaffected.

### Fixed

- A Material slot whose cosmetic fails to load with no fallback configured (slot-level or
  avatar-level) now **keeps the material it is already wearing** instead of being assigned `null`,
  which rendered as missing-material magenta in a build. It logs a warning naming the slot.
  This also removes a blank flash during loading on avatars with no **Loading Material** set.

### Removed

- The Avatar validator no longer emits the Info-level notice on every `SkinnedMesh` slot about
  authoring the garment against this avatar's rig. The requirement is unchanged and is still
  documented in the slot's `kind` tooltip; the panel just doesn't repeat it per slot.
