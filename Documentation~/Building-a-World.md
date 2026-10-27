# Building a world from code

A recipe for AI agents driving a creator's Unity Editor through the `unity` CLI. The SDK is at
`Packages/com.virtualvenues.sdk/`. Components and their fields: `Components.md`. Publishing: `Publishing.md`.

## 0. Connect

1. `unity status` — the project must be listed with state `ready`. With several Editors open, add
   `--project-path <path>` to every command. If it lists nothing while the Editor is open, the project lacks
   `com.unity.pipeline`: run `unity pipeline install` in the project once.
2. `unity command` — lists the commands this Editor exposes. Run C# with `unity command eval '<C#>'`.
3. Check the project compiles before trusting anything: `unity recompile`. An Editor with compile errors may
   still report the last good build.

The CLI gives up after about 5 seconds but the Editor keeps working. After a timeout, check the result
(`unity command eval` a query) instead of repeating the action — **never retry a scene save**.

## 1. Set up the project (once)

```csharp
foreach (var line in VirtualVenues.Editor.ProjectSetup.ProjectSetupInstaller.RunAll(false)) UnityEngine.Debug.Log(line);
UnityEngine.Debug.Log(string.Join("\n", VirtualVenues.Editor.ProjectSetup.ProjectSetupInstaller.GetCriticalIssues()));
```

It assigns the SDK's URP settings, Linear colour, layer 3 `Environment`, WebGPU and stripping settings. Every
step is safe to repeat. `RunAll(true)` also sets Input Handling = Both, which restarts the Editor — ask the
user first. Human equivalent: **VirtualVenues > Project Setup**.

## 2. Make the scene

```csharp
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
    UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects, UnityEditor.SceneManagement.NewSceneMode.Single);
UnityEngine.Object.DestroyImmediate(UnityEngine.Camera.main.gameObject); // worlds bring no camera
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, "Assets/Worlds/MyWorld/MyWorld.unity");
```

Give walkable geometry a **solid** collider. Set lighting, skybox and fog in the scene's Lighting settings —
they ship with the world.

## 3. Place components

Two ways; both put the object in the active scene.

**Prefab (preferred, you control the parent and position):**
```csharp
var prefab = UnityEngine.Resources.Load<UnityEngine.GameObject>("Seat");
var go = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
go.transform.SetPositionAndRotation(new UnityEngine.Vector3(2, 0, 5), UnityEngine.Quaternion.identity);
```
Prefab names are listed in `Components.md`.

**Menu item (same as a human clicking it):**
```csharp
UnityEditor.EditorApplication.ExecuteMenuItem("GameObject/VirtualVenues/Spawn Point");
var go = UnityEditor.Selection.activeGameObject; // the menu selects what it made
go.transform.position = new UnityEngine.Vector3(0, 0, -5);
```
Menu items place the object at the origin with no parent, so always move or re-parent it afterwards.

**Set fields** with the component's `Configure(...)` (list in `Components.md`) or `SerializedObject`:
```csharp
var so = new UnityEditor.SerializedObject(go.GetComponent<VirtualVenues.WorldCreator.SpawnPoint>());
so.FindProperty("_isDefault").boolValue = true;
so.ApplyModifiedProperties();
```

Then save: `UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();`

## 4. A minimal world

- Ground with a solid collider.
- One `SpawnPoint` with `_isDefault` on.
- Optional: a **New Stage** (stage index 0) and a **Stage Trigger** around the audience area.
- Anything else from `Components.md`.

## 5. Test it

1. Instantiate `Packages/com.virtualvenues.sdk/Runtime/Prefabs/TestPlayerSpawner.prefab`
   (`AssetDatabase.LoadAssetAtPath<GameObject>(...)` + `PrefabUtility.InstantiatePrefab`).
2. Enter Play (`EditorApplication.isPlaying = true`), check the Console for errors, exit Play.
3. **Delete the TestPlayerSpawner before publishing.**

The test player covers walking, seats, vehicles, trigger events and moving platforms. Zones, physics objects,
stage video, voice and NMKR only work in the real app — see the **Test player** column in `Components.md`.

## Traps

- **No Camera or AudioListener in the world.** The app brings its own.
- **Only SDK and Unity-package scripts load in the app.** Your own scripts arrive as missing scripts.
- **`AddComponent<Seat>()` / `AddComponent<Vehicle>()` skip `Reset()`**, so no `Interactable` is added.
  Use the prefab, or add `Interactable` and a trigger collider yourself.
- **Menu items spawn at (0,0,0)** and ignore the selected parent.
- **`SplineMover` must be on a child of the spline object.** `PingPong` does not work on a `SplineRide` track.
  More in `Rides.md`.
- **Turn off Occlusion Static** on anything that moves.
- **One `NMKR_Plugin` per scene** if the world mints NFTs; publishing is blocked until it's configured.
- **`AmbientAudio` (old name `RoomAmbientAudio`) is silent in the editor** — it waits for the app to report the
  listener position.
- **Additional scenes** (loaded additively with the main one) can be published together; lighting comes from
  the main scene only.
