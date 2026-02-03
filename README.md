# VirtualVenues World Creator SDK

A Unity package for creating custom virtual worlds and stages for the VirtualVenues platform.

## Requirements

- **Unity 6000.1** or later
- **Dependencies**: `com.unity.nuget.newtonsoft-json` (auto-installed)

## Installation

### Via Package Manager (Recommended)

Add the package via git URL in Unity Package Manager:

```
https://github.com/VirtualVenues/VirtualVenuesSDK.git
```

### Manual Installation

1. Download or clone the repository
2. Copy the `VirtualVenuesSDK` folder into your project's `Assets/` or `Packages/` directory

## Features

### World Publisher

Publish your custom worlds directly to VirtualVenues from the Unity Editor.

**Access:** `VirtualVenues > World Publisher`

- **Authentication**: Secure login via Auth0 device flow
- **Upload Worlds**: Publish scenes as UMS/UPC world map bundles
- **Manage Worlds**: View, rename, and delete your published worlds

### Avatar Publisher

Publish avatar and cosmetic asset bundles to the VirtualVenues platform.

**Access:** `VirtualVenues > Avatar Publisher`

- **Authentication**: Secure login via Auth0 device flow (shared with World Publisher)
- **Catalog Management**: Create new catalogs or add versions to existing ones
- **Bundle Upload**: Select your Addressables build folder and auto-detect `.bundle`, `.bin`, `.hash` files
- **Metadata Entry**: Define avatar and cosmetic metadata (id, name, gameId, guid)
- **Version Tagging**: Tag each catalog version for easy identification
- **Manage Catalogs**: View, rename, and delete your published catalogs

#### Avatar Publisher Quick Start

1. Build your avatars/cosmetics using Unity Addressables
2. Open `VirtualVenues > Avatar Publisher`
3. Login with your VirtualVenues account
4. Select "Create New Catalog" or "Add Version to Existing"
5. Enter a catalog name and version tag
6. Browse to your Addressables build output folder
7. Add avatar/cosmetic metadata entries as needed
8. Click "Publish Catalog"

### Stage Components

Create interactive performance stages with lighting and video screens.

| Component | Description | Menu Path |
|-----------|-------------|-----------|
| **Stage** | Defines a performance stage area | `GameObject > VirtualVenues > New Stage` |
| **Screen** | Video display surface for artist content | Child of Stage prefab |
| **Fixture** | DMX lighting fixtures (PAR, Moving Head, Strobe) | `GameObject > VirtualVenues > Fixtures > *` |

**Quick Add Fixtures**: Select a Stage in the scene and click the floating "+" button to add fixtures.

### World Components

Define player spawning and navigation areas.

| Component | Description | Menu Path |
|-----------|-------------|-----------|
| **SpawnPoint** | Player spawn location with directional indicator | `GameObject > VirtualVenues > Spawn Point` |
| **TriggerZone** | Area-based trigger for events | `GameObject > VirtualVenues > Trigger Zone` |
| **RotatingObject** | Animated rotating object with optional network sync | Add component manually |

### Interaction System

Create interactive objects in your world.

| Component | Description |
|-----------|-------------|
| **Interactable** | Interactive object with highlight states and UnityEvents |

## Quick Start

1. **Create a new scene** for your world
2. **Add a Stage**: `GameObject > VirtualVenues > New Stage`
3. **Add SpawnPoints**: `GameObject > VirtualVenues > Spawn Point` (add multiple for variety)
4. **Add Fixtures**: Click the "+" button on the Stage gizmo to add lighting
5. **Test locally**: Add `TestPlayerSpawner` prefab from `Runtime/Resources/`
6. **Publish**: Open `VirtualVenues > World Publisher`, login, and upload

## Scene Testing

Use the `TestPlayerSpawner` prefab to test your world locally:

1. Drag `Runtime/Resources/TestPlayerSpawner.prefab` into your scene
2. Enter Play mode
3. A test player will spawn at a random SpawnPoint

## Namespace

All SDK components are under the `VirtualVenues.WorldCreator` namespace:

```csharp
using VirtualVenues.WorldCreator;
```

## Component Reference

### Stage

```csharp
// Get all stages
List<Stage> stages = Stage.Instances;

// Listen for new stages
Stage.onStageAdded += (stage) => { };

// Get stage index
int index = stage.StageIndex;
```

### SpawnPoint

```csharp
// Get all spawn points
List<SpawnPoint> spawns = SpawnPoint.Instances;

// Listen for new spawn points
SpawnPoint.onSpawnPointAdded += (spawn) => { };

// Ground a spawn point to the floor (Editor only)
// Use "Ground to Floor" button in Inspector
```

### Fixture

```csharp
// Fixture types: PAR, MovingHead, Strobe
Fixture.FixtureType type = fixture.Type;

// Stage association
int stageIndex = fixture.StageIndex;

// Get all fixtures
List<Fixture> fixtures = Fixture.Instances;
```

### Screen

```csharp
// Get all screens
List<Screen> screens = Screen.Instances;

// Get screen properties
Renderer renderer = screen.Renderer;
int stageIndex = screen.StageIndex;
```

### TriggerZone

```csharp
// Track zones
TriggerZone.Tracker.onInstanceAdded += (zone) => { };
TriggerZone.Tracker.onInstanceRemoved += (zone) => { };

// Get zone name
string name = zone.ZoneName;
```

### RotatingObject

```csharp
// Get all rotating objects
List<RotatingObject> objects = RotatingObject.Instances;

// Configure via Inspector:
// - rotationAxis: Vector3 (degrees per second)
// - networkSynced: bool (synchronize across clients)
```

### Interactable

```csharp
// Track interactables
Interactable.Tracker.onInstanceAdded += (obj) => { };

// Configure in Inspector:
// - InteractionDisplayText: "Press {0} to interact"
// - OnHighlight/OnUnhighlight: UnityEvents for visual feedback
// - OnLocalInteract: UnityEvent when player interacts
```

## Support

For issues or questions, contact the VirtualVenues development team.

## License

See LICENSE file for details.
