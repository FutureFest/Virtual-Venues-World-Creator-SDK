# VirtualVenues SDK — Project & Content Requirements

What your project, scene, and prefabs need before you can build and publish to VirtualVenues.
Requirements are grouped as:

- **Required** — publishing is blocked until you fix it.
- **Recommended** — publishing still works, but your content may look or behave wrong (e.g. pink worlds).
- **Gotcha** — nothing warns you; it just breaks at runtime. Read these.

---

## 1. Before you start (project setup)

| Requirement | Level | Notes |
|---|---|---|
| **Unity 6000.1** or later | Required | The SDK targets this Editor version. |
| **WebGL Build Support** module installed | Required | The player client (UPC) build is WebGL/WebGPU. Publishing a world or auto‑building an avatar switches to this target. |
| **Linux Build Support** module installed | Required (worlds) | The dedicated‑server (UMS) world bundle is built for `StandaloneLinux64`. |
| **Run `VirtualVenues → Project Setup`** | Required | One click configures the rendering settings below. The checklist must be all‑green before you publish. |

> Missing build‑support modules surface only as a publish‑time error ("could not switch build target"). Install them from the Unity Hub (Add Modules) up front.

### What `Project Setup` configures

Running **`VirtualVenues → Project Setup → Fix All`** applies the rendering configuration that makes worlds render correctly in the browser:

- **VirtualVenues URP render pipeline** assigned as the active and default pipeline.
- **VirtualVenues URP global settings** assigned (shader‑variant stripping policy).
- **Linear** color space.
- **WebGL graphics API = WebGPU** (not Auto).
- *Recommended:* WebGL managed stripping = Low, an `Environment` layer, and Active Input Handling = Both.

> **Why this matters:** a project that uses Unity's default URP assets or the wrong color space produces shader variants the WebGPU client can't render — worlds appear **pink/magenta**. The World Publisher shows a "Fix now" banner if any of the critical items above are missing.

---

## 2. Publishing a World

**Tool:** `VirtualVenues → World Publisher`

### Required

- You are **logged in** (Auth0 device flow; session not expired).
- A **world name** — non‑empty, **100 characters or fewer**.
- A **scene selected** to publish (a `.unity` asset).
- **WebGL** and **Linux** build support installed (see §1).
- **Project Setup** critical items all green (see §1) — otherwise the world renders pink.

### Required *only if your scene uses NMKR minting*

If the scene contains any **NMKR Mint Interactable**:

- An **`NMKR_Plugin` prefab** must be present in the scene.
- A **`NmkrPluginConfig`** must exist with both **Customer ID** and **API Key** filled in.
- **Every** Mint Interactable must have a **project selected** (a Project UID).

Configure these via `VirtualVenues → Plugins → NMKR Settings`. Publishing is blocked with a clear message if any are missing.

### Recommended scene contents

- **At least one SpawnPoint** (`GameObject → VirtualVenues → Spawn Point`) — players spawn here.
- A **Stage** (`GameObject → VirtualVenues → New Stage`) with Screens/Fixtures for performances.
- Test locally first with `Runtime/Resources/TestPlayerSpawner.prefab`.

### Gotchas

- **Empty scene** → there's nothing to bundle and the build fails. Put your world content in the scene you select.
- **No SpawnPoint** → players have nowhere to spawn at runtime. There's no pre‑publish check for this.

---

## 3. Publishing Avatars & Cosmetics

**Tool:** `VirtualVenues → Avatar Publisher`

The publisher has two modes: **Auto Build** (add prefabs, the SDK builds the Addressables bundle for you) and **Manual** (point it at an Addressables build folder you produced yourself).

### Required (all modes)

- You are **logged in**.
- Either a **new catalog name** (non‑empty, **≤100 characters**) **or** an **existing catalog selected**.
- A **version tag** — non‑empty, and **unique within that catalog** (re‑using a tag is rejected).

### Required — Auto Build mode

- **At least one** avatar or cosmetic prefab added.
- **Avatar prefabs** must:
  - be a **project prefab** (not a scene object),
  - have the **VirtualVenues Avatar component on the prefab root** (not on a child),
  - have an **Animator assigned** on that component.
- **Cosmetic prefabs** must be a **project prefab**.

> Leaving the Animator's **Controller** field empty is *recommended, not required* — see Animation below.

### Animation

VirtualVenues drives your avatar with the FutureFest character controller, which it **injects at runtime**.
You never assign it yourself.

- **Leave the Animator's Controller field empty** (recommended). Publishing still works if you don't, and your
  avatar will still animate — VirtualVenues replaces the controller at runtime. The reason to clear it is
  **download size**: every clip your controller references gets packed into your published bundle, so players
  download animations that are never used.
- **Use a Humanoid rig.** Locomotion, dance, sit and talk are Humanoid clips that retarget onto your skeleton.
  A Generic rig publishes but won't animate.
- **To ship your own animations**, fill in the **Custom Animations** list on the Avatar component: pick a slot
  (Idle, Walk, Run, Jump, FreeFall, Land, Swim, Dance, Sit, Talk, Interact, IdleTwitch0/1) and assign a
  **Humanoid** clip. Any slot you leave out keeps the FutureFest default. This is the *only* supported way to
  override animations — a hand-authored controller is not.
- **To preview animations in the editor**, temporarily assign
  `Packages/VirtualVenues SDK/Runtime/Animation/VVPreviewCharacterController` to your Animator. It carries the
  locomotion set (idle/walk/run blend on the `Speed` parameter, plus Jumping/FreeFall/Landing states) so you can
  check retargeting on your rig. **It is editor-only — clear it before publishing.**

### Required — Manual mode

- An **Addressables build folder** that contains:
  - your **`.bundle`** files,
  - the catalog **`.bin`** and **`.hash`** files (e.g. `content_catalog.bin` / `content_catalog.hash`).
- Bundle filenames must **end in `.bundle`** with no folder paths in the name.

### Gotchas

- **Duplicate prefab names** → avatars/cosmetics are keyed by name; two prefabs with the same name collide and one silently overwrites the other. **Give every prefab a unique name.**
- **Cosmetic slot category** must match a slot the avatar exposes, or the cosmetic won't attach at runtime.
- **Rig/skeleton compatibility** — cosmetics attach to the avatar's bones; an incompatible rig means cosmetics won't fit. There's no pre‑publish check.

---

## Quick checklist

**World:** logged in · world name ≤100 · scene selected · WebGL + Linux build support · Project Setup green · (NMKR configured if used) · ≥1 SpawnPoint.

**Avatar:** logged in · catalog name/selection · unique version tag · ≥1 prefab · Avatar component on root + Animator · unique prefab names. (Clearing the Animator's Controller field is recommended, not required.)

If a publish is blocked, the tool tells you exactly which item to fix. For rendering issues, start with `VirtualVenues → Project Setup`.
