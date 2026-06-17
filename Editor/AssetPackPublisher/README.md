# Asset Pack Publisher

The public, creator-facing tool (ships in `com.virtualvenues.sdk`) that publishes a creator's
prefabs as a versioned Addressables catalog to the Marketplace, so they appear in the UWE World
Editor's asset grid. Opened from **VirtualVenues ▸ Asset Pack Publisher**.

It is a UI Toolkit window themed (via `VVEditorUI`) to match the World & Avatar publishers, and it
compiles into `com.virtualvenues.sdk.editor` like them — there is no longer a separate assembly
(`sdk.editor` references `com.virtualvenues.layout.runtime` for the shared `AssetMeta` /
`PackageMetadata` / `LibraryPackage` DTOs).

## Files

- `UI/AssetPackPublisherUI.{cs,uxml,uss}` — the themed EditorWindow: sign in (shared `AuthManager`
  device-flow login), pick prefabs, assign `Type_Id` asset keys, set pack name/version/price/tags/
  categories, validate, and Build & Publish. The auth + theme + version-label scaffolding mirrors the
  World/Avatar publishers; the dynamic asset rows mirror the Avatar prefab rows.
- `Scripts/AssetPackPublisherApi.cs` — the ff-api orchestrator (`docs/uwe/openapi.yaml`):
  `create-pack → reserve content_catalog.bin → build → upload (presigned PUT) → confirm-upload →
  add-to-library`. `contentBaseUrl` is **derived from the presigned `content_catalog.bin` URL**
  (so it always matches the bucket the backend presigns/HeadObjects against — no `FF_BUCKET_URL`
  guessing). Wrapped in `LockReloadAssemblies`/`Unlock` so the build-target switch can't tear down
  the async flow mid-publish.
- `Scripts/AssetPackCatalogBuilder.cs` — a subclass of the shared
  `VirtualVenues.Editor.Publishing.AddressablesCatalogBuilder` that builds an **isolated** catalog
  (own `VVSDKAssetPackSettings` + `VVSDKAssetPacks` group) so it never builds the avatar group's
  content (or the project default). Each prefab's address + label = its `Type_Id` assetKey.

## Notes / known gaps

- **Thumbnails** are not yet generated (`thumbnailUrl` is empty); the grid falls back to glyphs.
  The backend already anticipates `thumbs/*.png` upload objects — this is the main pending feature.
- **Unity 6.4:** `LockReloadAssemblies` won't hold across the build-target switch on 6.4. When the
  editor is upgraded, this needs the `SessionState` + `delayCall` reload-safe state machine —
  alongside the Avatar Publisher and Build Uploader (see `.claude/docs/unity-6.4-upgrade/`).

## Related (do not modify from here)

- Build engine: `Assets/VirtualVenuesSDK/Editor/Publishing/AddressablesCatalogBuilder.cs`
- Mirror tool (avatar path): `Assets/VirtualVenuesSDK/Editor/AvatarPublisher/`
- Shared theme: `Assets/VirtualVenuesSDK/Editor/UI/VVEditorUI.cs` + `VVTheme.uss`
- Auth token store: `Assets/VirtualVenuesSDK/Editor/Authentication/Scripts/EditorAuthToken.cs`
- DEV-ONLY local (no-backend) tester, FutureFestXR-internal, NOT in the SDK:
  `Assets/UnityWorldEditor/Editor/UweAssetPackLocalTester.cs`
- Backend contract: `ff-api/docs/uwe/openapi.yaml` (asset-pack §, `PackageMetadata`, `AssetMeta`).
