# Changelog

All notable changes to `com.virtualvenues.sdk`.

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
