using System;
using System.Collections.Generic;
using UnityEngine;
using VirtualVenues.WorldCreator;
// Disambiguate the VV SDK Screen component from UnityEngine.Screen (both are in scope here).
using Screen = VirtualVenues.WorldCreator.Screen;

namespace VirtualVenues.Editor.AssetPackPublisher
{
    // -------------------------------------------------------------------------------------------------
    // Composite-asset manifest builder (pack_manifest.json).
    //
    // For every pack item with "Expose children" on, walks the prefab and describes its EXPOSED nodes so
    // the World Editor can explode the asset into real, individually-editable child objects at drop time.
    // Ships as ONE extra S3 object per pack version — the same zero-backend trick as thumb_<key>.png.
    //
    // The DTOs below intentionally duplicate VirtualVenues.Layout.PackManifest* (same reason as
    // PublisherDtos.cs: creator projects don't have layout.runtime). FIELD NAMES ARE A FROZEN WIRE
    // CONTRACT — JsonUtility binds on exact names on both sides; keep them in sync with
    // Assets/VirtualVenuesLayout/Runtime/Schema/PackManifest.cs.
    //
    // The component detection mirrors LayoutLoader.DetectComponentsOn (reflection-free reads of the VV
    // SDK public getters) and the populated-shape rule (every union sub-block present, never null).
    // -------------------------------------------------------------------------------------------------

    [Serializable]
    public class MfPackManifest
    {
        public int manifestVersion;
        public MfManifestEntry[] entries;
    }

    [Serializable]
    public class MfManifestEntry
    {
        public string assetKey;
        public MfManifestNode[] nodes;
    }

    [Serializable]
    public class MfManifestNode
    {
        /// <summary>Sibling-index path from the prefab root, e.g. "2/0".</summary>
        public string path;

        /// <summary>The nearest EXPOSED ancestor's path; "" = the prefab root.</summary>
        public string parentPath;

        public string name;

        /// <summary>Local TRS relative to the nearest exposed ancestor (euler degrees).</summary>
        public MfTransform transform;

        public string kind;
        public MfComponent[] components;
    }

    [Serializable]
    public class MfTransform
    {
        public Vec3 position;
        public Vec3 rotation;
        public Vec3 scale;
    }

    [Serializable]
    public class MfRgba
    {
        public float r;
        public float g;
        public float b;
        public float a;
    }

    [Serializable]
    public class MfComponent
    {
        /// <summary>"spawnPoint" | "triggerZone" | "screen" | "speaker" | "stage" | "artist" | "light"</summary>
        public string type;

        public MfSpawnParams spawn;
        public MfTriggerParams trigger;
        public MfStageMediaParams stageMedia;
        public MfLightParams light;
    }

    [Serializable]
    public class MfSpawnParams
    {
        public bool isDefault;
        public string team;
    }

    [Serializable]
    public class MfTriggerParams
    {
        public string zoneType;
        public string colliderShape;
        public Vec3 size;
        public float radius;
        public MfVoiceParams voice;
        public MfAreaParams area;
    }

    [Serializable]
    public class MfVoiceParams
    {
        public float conversationalDistance;
        public float audibleDistance;
    }

    [Serializable]
    public class MfAreaParams
    {
        public string areaId;
        public string prompt;
        public string targetRoomId;
    }

    [Serializable]
    public class MfStageMediaParams
    {
        public int stageIndex;
        public string screenSource;
        public string speakerSource;
        public int speakerChannel;
        public Vec3 artistPivotOffset;
        public bool startInactive;
    }

    [Serializable]
    public class MfLightParams
    {
        public string lightType;
        public MfRgba color;
        public float intensity;
        public float range;
        public float spotAngle;
        public bool shadows;
    }

    /// <summary>
    /// Static builder. Failure-tolerant: a prefab that yields no exposable nodes simply contributes no
    /// manifest entry (the asset then drops monolithic — never a broken half-manifest).
    /// </summary>
    public static class AssetPackManifestBuilder
    {
        /// <summary>Safety cap on exposed nodes per item (a rigged/CAD import can carry hundreds of nodes).</summary>
        public const int MaxExposedNodesPerItem = 64;

        /// <summary>
        /// Build the pack_manifest.json body for the given (assetKey, prefab) composites. Returns null
        /// when no item yields exposable nodes (upload nothing — absence == "no composites").
        /// </summary>
        public static string BuildManifestJson(List<KeyValuePair<string, GameObject>> composites)
        {
            if (composites == null || composites.Count == 0) { return null; }

            var entries = new List<MfManifestEntry>();
            foreach (KeyValuePair<string, GameObject> kv in composites)
            {
                MfManifestEntry entry = BuildEntry(kv.Key, kv.Value);
                if (entry != null) { entries.Add(entry); }
            }
            if (entries.Count == 0) { return null; }

            var manifest = new MfPackManifest { manifestVersion = 1, entries = entries.ToArray() };
            return JsonUtility.ToJson(manifest);
        }

        /// <summary>One item's exposed-node entry, or null when the prefab has nothing exposable.</summary>
        public static MfManifestEntry BuildEntry(string assetKey, GameObject prefab)
        {
            if (string.IsNullOrEmpty(assetKey) || prefab == null) { return null; }

            // Pre-compute skinning topology once: bone regions are never exposed (extracting an animated
            // bone subtree would tear it off its SkinnedMeshRenderer), and any node whose boundary a
            // skin's bone set CROSSES is sealed (extract-by-subPath would orphan the mesh from its bones).
            var smrs = new List<SkinnedMeshRenderer>(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true));
            var boneRoots = new HashSet<Transform>();
            foreach (SkinnedMeshRenderer smr in smrs)
            {
                if (smr != null && smr.rootBone != null) { boneRoots.Add(smr.rootBone); }
            }

            var nodes = new List<MfManifestNode>();
            bool capped = false;
            Transform root = prefab.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                WalkChild(root.GetChild(i), i.ToString(), root, string.Empty, smrs, boneRoots, nodes, ref capped);
            }
            if (capped)
            {
                Debug.LogWarning($"[AssetPackPublisher] '{assetKey}': exposed-children manifest capped at {MaxExposedNodesPerItem} nodes; deeper nodes stay part of their parent's geometry.");
            }
            if (nodes.Count == 0) { return null; }

            return new MfManifestEntry { assetKey = assetKey, nodes = nodes.ToArray() };
        }

        // Pre-order DFS (parent before child — the manifest's topological-order contract). Returns true
        // when this subtree exposed at least one node (an organizational empty above it then exposes too
        // ... except the decision is made on the way DOWN, so structure empties are exposed when they
        // contain anything meaningful — see IsMeaningfulSubtree).
        private static void WalkChild(
            Transform node,
            string path,
            Transform exposedAncestor,
            string exposedAncestorPath,
            List<SkinnedMeshRenderer> smrs,
            HashSet<Transform> boneRoots,
            List<MfManifestNode> outNodes,
            ref bool capped)
        {
            if (node == null) { return; }
            if (node.gameObject.hideFlags != HideFlags.None) { return; }
            if (boneRoots.Contains(node)) { return; } // bone hierarchy: sealed, never exposed or recursed

            bool wouldExpose = IsMeaningfulSubtree(node) && !SkinningCrossesBoundary(node, smrs);
            bool expose = wouldExpose && outNodes.Count < MaxExposedNodesPerItem;
            if (wouldExpose && !expose) { capped = true; }

            Transform childAncestor = exposedAncestor;
            string childAncestorPath = exposedAncestorPath;
            if (expose)
            {
                outNodes.Add(BuildNode(node, path, exposedAncestor, exposedAncestorPath));
                childAncestor = node;
                childAncestorPath = path;
            }

            // Don't split a LODGroup's internals apart — the group node is one unit.
            if (node.GetComponent<LODGroup>() != null) { return; }

            for (int i = 0; i < node.childCount; i++)
            {
                WalkChild(node.GetChild(i), path + "/" + i, childAncestor, childAncestorPath, smrs, boneRoots, outNodes, ref capped);
            }
        }

        // A node earns exposure when it (or anything under it) is visible/behavioural: a renderer, a
        // light, or a VV SDK marker. Plain leaf empties (pure organization with nothing inside) don't.
        private static bool IsMeaningfulSubtree(Transform node)
        {
            if (node.GetComponentInChildren<Renderer>(true) != null) { return true; }
            if (node.GetComponentInChildren<Light>(true) != null) { return true; }
            if (node.GetComponentInChildren<SpawnPoint>(true) != null) { return true; }
            if (node.GetComponentInChildren<TriggerZone>(true) != null) { return true; }
            if (node.GetComponentInChildren<Screen>(true) != null) { return true; }
            if (node.GetComponentInChildren<Speaker>(true) != null) { return true; }
            if (node.GetComponentInChildren<Stage>(true) != null) { return true; }
            if (node.GetComponentInChildren<Artist>(true) != null) { return true; }
            return false;
        }

        // True when any SkinnedMeshRenderer's mesh-vs-bones relationship crosses this node's boundary in
        // either direction — extracting such a node by subPath would orphan the mesh from its skeleton.
        private static bool SkinningCrossesBoundary(Transform node, List<SkinnedMeshRenderer> smrs)
        {
            for (int i = 0; i < smrs.Count; i++)
            {
                SkinnedMeshRenderer smr = smrs[i];
                if (smr == null) { continue; }
                bool smrInside = smr.transform.IsChildOf(node);
                if (smr.rootBone != null && smr.rootBone.IsChildOf(node) != smrInside) { return true; }
                Transform[] bones = smr.bones;
                if (bones == null) { continue; }
                for (int b = 0; b < bones.Length; b++)
                {
                    if (bones[b] != null && bones[b].IsChildOf(node) != smrInside) { return true; }
                }
            }
            return false;
        }

        private static MfManifestNode BuildNode(Transform node, string path, Transform exposedAncestor, string exposedAncestorPath)
        {
            MfComponent[] components = DetectComponents(node.gameObject);
            return new MfManifestNode
            {
                path = path,
                parentPath = exposedAncestorPath ?? string.Empty,
                name = node.name,
                transform = RelativeTransform(node, exposedAncestor),
                kind = components.Length > 0 && !string.IsNullOrEmpty(components[0].type) ? components[0].type : "prop",
                components = components,
            };
        }

        // Local TRS relative to the nearest exposed ancestor. Exact when the ancestor is the direct
        // parent (the overwhelmingly common case); across unexposed intermediates it composes via world
        // matrices with a lossyScale ratio — exact for uniform/no intermediate scaling, best-effort
        // (documented) for rotated non-uniform-scale chains.
        private static MfTransform RelativeTransform(Transform node, Transform ancestor)
        {
            Vector3 pos;
            Quaternion rot;
            Vector3 scl;
            if (ancestor == node.parent)
            {
                pos = node.localPosition;
                rot = node.localRotation;
                scl = node.localScale;
            }
            else
            {
                pos = ancestor.InverseTransformPoint(node.position);
                rot = Quaternion.Inverse(ancestor.rotation) * node.rotation;
                Vector3 a = ancestor.lossyScale;
                Vector3 n = node.lossyScale;
                scl = new Vector3(
                    a.x != 0f ? n.x / a.x : n.x,
                    a.y != 0f ? n.y / a.y : n.y,
                    a.z != 0f ? n.z / a.z : n.z);
            }
            Vector3 euler = rot.eulerAngles;
            return new MfTransform
            {
                position = new Vec3 { x = pos.x, y = pos.y, z = pos.z },
                rotation = new Vec3 { x = euler.x, y = euler.y, z = euler.z },
                scale = new Vec3 { x = scl.x, y = scl.y, z = scl.z },
            };
        }

        // ---- component detection (mirror of LayoutLoader.DetectComponentsOn, editor-side) ---- //

        /// <summary>Detect the VV SDK behaviour components authored on this node (never null).</summary>
        public static MfComponent[] DetectComponents(GameObject go)
        {
            var list = new List<MfComponent>();
            if (go == null) { return list.ToArray(); }

            SpawnPoint sp = go.GetComponent<SpawnPoint>();
            if (sp != null)
            {
                MfComponent c = DefaultComponent("spawnPoint");
                c.spawn.isDefault = sp.IsDefault;
                c.spawn.team = sp.Team;
                list.Add(c);
            }

            TriggerZone tz = go.GetComponent<TriggerZone>();
            if (tz != null)
            {
                MfComponent c = DefaultComponent("triggerZone");
                TriggerZoneData data = go.GetComponent<TriggerZoneData>();
                c.trigger.zoneType = ZoneTypeToString(data);
                ReadTriggerCollider(go, c.trigger);
                if (data is VoiceZoneData voice)
                {
                    c.trigger.voice.conversationalDistance = voice.ConversationalDistance;
                    c.trigger.voice.audibleDistance = voice.AudibleDistance;
                }
                else if (data is AreaZoneData area)
                {
                    c.trigger.area.areaId = area.AreaId;
                    c.trigger.area.prompt = area.Prompt;
                    c.trigger.area.targetRoomId = area.TargetRoomId;
                }
                list.Add(c);
            }

            Screen screen = go.GetComponent<Screen>();
            if (screen != null)
            {
                MfComponent c = DefaultComponent("screen");
                c.stageMedia.stageIndex = screen.StageIndex;
                c.stageMedia.screenSource = screen.ScreenSource;
                list.Add(c);
            }

            Speaker speaker = go.GetComponent<Speaker>();
            if (speaker != null)
            {
                MfComponent c = DefaultComponent("speaker");
                c.stageMedia.stageIndex = speaker.StageIndex;
                c.stageMedia.speakerSource = speaker.Source.ToString();
                c.stageMedia.speakerChannel = (int)speaker.Channel;
                list.Add(c);
            }

            Stage stage = go.GetComponent<Stage>();
            if (stage != null)
            {
                MfComponent c = DefaultComponent("stage");
                c.stageMedia.stageIndex = stage.StageIndex;
                c.stageMedia.startInactive = !stage.ActivateOnStart;
                list.Add(c);
            }

            Artist artist = go.GetComponent<Artist>();
            if (artist != null)
            {
                MfComponent c = DefaultComponent("artist");
                c.stageMedia.stageIndex = artist.StageIndex;
                c.stageMedia.speakerSource = artist.SpeakerSource;
                if (artist.Pivot != null)
                {
                    Vector3 p = artist.Pivot.localPosition;
                    c.stageMedia.artistPivotOffset = new Vec3 { x = p.x, y = p.y, z = p.z };
                }
                list.Add(c);
            }

            Light light = go.GetComponent<Light>();
            if (light != null)
            {
                MfComponent c = DefaultComponent("light");
                c.light.lightType = light.type.ToString();
                Color col = light.color;
                c.light.color = new MfRgba { r = col.r, g = col.g, b = col.b, a = col.a };
                c.light.intensity = light.intensity;
                c.light.range = light.range;
                c.light.spotAngle = light.spotAngle;
                c.light.shadows = light.shadows != LightShadows.None;
                list.Add(c);
            }

            return list.ToArray();
        }

        private static string ZoneTypeToString(TriggerZoneData data)
        {
            if (data == null) { return string.Empty; }
            switch (data.ZoneType)
            {
                case TriggerZoneData.TriggerZoneType.Voice: return "Voice";
                case TriggerZoneData.TriggerZoneType.Area: return "Area";
                case TriggerZoneData.TriggerZoneType.Swim: return "Swim";
                default: return string.Empty;
            }
        }

        private static void ReadTriggerCollider(GameObject go, MfTriggerParams trigger)
        {
            Collider[] colliders = go.GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider col = colliders[i];
                if (col == null || !col.isTrigger) { continue; }
                if (col is BoxCollider box)
                {
                    trigger.colliderShape = "Box";
                    trigger.size = new Vec3 { x = box.size.x, y = box.size.y, z = box.size.z };
                    return;
                }
                if (col is SphereCollider sphere)
                {
                    trigger.colliderShape = "Sphere";
                    trigger.radius = sphere.radius;
                    return;
                }
            }
        }

        // Populated-shape rule: every union sub-block present + zeroed (JsonUtility no-null-nested
        // discipline — mirrors LayoutComponentFactory.DefaultComponent).
        private static MfComponent DefaultComponent(string type)
        {
            return new MfComponent
            {
                type = type ?? string.Empty,
                spawn = new MfSpawnParams { isDefault = false, team = string.Empty },
                trigger = new MfTriggerParams
                {
                    zoneType = string.Empty,
                    colliderShape = string.Empty,
                    size = new Vec3 { x = 0f, y = 0f, z = 0f },
                    radius = 0f,
                    voice = new MfVoiceParams { conversationalDistance = 0f, audibleDistance = 0f },
                    area = new MfAreaParams { areaId = string.Empty, prompt = string.Empty, targetRoomId = string.Empty },
                },
                stageMedia = new MfStageMediaParams
                {
                    stageIndex = 0,
                    screenSource = string.Empty,
                    speakerSource = string.Empty,
                    speakerChannel = 0,
                    artistPivotOffset = new Vec3 { x = 0f, y = 0f, z = 0f },
                    startInactive = false,
                },
                light = new MfLightParams
                {
                    lightType = string.Empty,
                    color = new MfRgba { r = 0f, g = 0f, b = 0f, a = 0f },
                    intensity = 0f,
                    range = 0f,
                    spotAngle = 0f,
                    shadows = false,
                },
            };
        }
    }
}
