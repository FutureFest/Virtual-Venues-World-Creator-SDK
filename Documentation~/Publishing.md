# Publishing a world from code

For AI agents (and scripts) publishing a world without clicking through **VirtualVenues > World Publisher**.
Namespace `WorldPublisher`. Build a world first: `Building-a-World.md`.

## What publishing does

A world is two scene bundles built from the same scene(s): **UMS** (Linux, the server) and **UPC** (WebGL, the
browser). Publishing switches the build target to Linux, builds, switches to WebGL, builds, uploads both,
then switches back. Each switch reloads all scripts, so the publish is a **job you start and then poll**, not a
call you await.

Bundles are written to `Assets/WorldMapAssetBundles/UMS` and `.../UPC`, named
`world_{ums|upc}_<MainScene>_<yyMMdd>_<NN>`. The app finds the main scene by that name — don't rename them.

## Sign-in (needs a human, once)

Uploading needs a signed-in VirtualVenues account. Sign-in is a browser device-code flow that **a person must
approve**: ask the user to open **VirtualVenues > World Publisher** and log in. The token is cached and every
SDK publisher reuses it until it expires (there is no automatic refresh). The job reloads the cached token by
itself after each script reload.

A **build-only** run (`upload: false`) needs no sign-in — use it to check a world builds.

## Start and poll

```csharp
WorldPublisher.WorldPublishJob.Start(
    "Assets/Worlds/MyWorld/MyWorld.unity",   // main scene
    null,                                    // or additional scene paths (loaded additively)
    "My World",                              // world name, max 100 chars; required when uploading
    upload: false);                          // true = also upload
```

`Start` throws right away if: a publish is already running, the path is not a scene, scripts don't compile,
an NMKR Mint Interactable is misconfigured, the world name is missing (when uploading), or nobody is signed in
(when uploading). Fix the message and start again.

Then poll until the state is not `Running`:

```csharp
UnityEngine.Debug.Log(WorldPublisher.WorldPublishJob.Status);
// Running 45% Building UPC asset bundle for WebGL...
// Succeeded 100% Publishing Complete! worldId=... ums=Assets/WorldMapAssetBundles/UMS/... upc=...
// Failed 0% Publishing failed at step 1: ...
```

`Status` has `state` (`Idle`/`Running`/`Succeeded`/`Failed`), `progress` (0–1), `message`, `worldId`,
`worldName`, `umsPath`, `upcPath`. The Console also gets one `[WorldPublishJob] SUCCEEDED ...` or
`[WorldPublishJob] FAILED: ...` line. Expect the CLI to time out while the Editor switches platforms; keep polling.
A full publish takes minutes; the first switch to a platform in a project takes longer.

The job always clears the bundle names it set and switches back to the original build target, even on failure.

## Manage published worlds

All async, all need sign-in:

```csharp
WorldPublisher.WorldPublisherApi.GetAllWorldsAsync().ContinueWith(t =>
    UnityEngine.Debug.Log(string.Join("\n", System.Linq.Enumerable.Select(t.Result, w => $"{w.worldId} {w.worldName}"))));
```

`RenameWorldAsync(worldId, newName)`, `DeleteWorldAsync(worldId)` (throws `WorldInUseException` if an event
uses it). **Deleting cannot be undone — ask the user first.**

## Avatars, cosmetics and asset packs

No one-call job yet. Use the windows (**VirtualVenues > Avatar Publisher / Asset Pack Publisher**).
The building blocks are public if you must script it: `AvatarPublisher.AvatarPublisherApi`,
`VirtualVenues.Editor.AvatarPublisher.AddressablesBuildManager`, and
`VirtualVenues.Editor.AssetPackPublisher.AssetPackPublisherApi.PublishAsync(...)`. Wrap builds in
`EditorApplication.LockReloadAssemblies()` / `UnlockReloadAssemblies()`.

## After publishing

The world uses the player app the event runs on. A world that uses a component added in a newer SDK version
needs an app build that knows it — check `CHANGELOG.md` and tell the user if the world relies on a new one.
