using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Controls whether <see cref="SplineTrack"/> support posts stand on this collider. Solid colliders already stop
    /// posts, so use this to make a trigger (an invisible box) stop them — e.g. to keep posts out of a tunnel or path —
    /// or to let posts pass through a prop (fence, lamp) they would otherwise stand on.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Collider))]
    [AddComponentMenu("VirtualVenues/Track Support Blocker")]
    public class TrackSupportBlocker : MonoBehaviour
    {
        public enum BlockMode
        {
            [Tooltip("Support posts stop on top of this collider, even if it is a trigger.")]
            Block,
            [Tooltip("Support posts pass through this collider as if it weren't there.")]
            LetSupportsThrough,
        }

        [Tooltip("Block: coaster support posts stop on top of this collider (works on triggers too).\n" +
                 "Let Supports Through: posts ignore this collider and keep going down.")]
        [SerializeField] private BlockMode _mode = BlockMode.Block;

        public BlockMode Mode => _mode;

        /// <summary>Set the mode and rebuild every track so posts update.</summary>
        public void SetMode(BlockMode mode)
        {
            _mode = mode;
            SplineTrack.RebuildAll();
        }

        private void OnEnable() { SplineTrack.RebuildAll(); }

        private void OnDisable() { SplineTrack.RebuildAll(); }

#if UNITY_EDITOR
        /// <summary>Makes an invisible trigger box that stops support posts, at `position`, selected and undoable.</summary>
        public static TrackSupportBlocker Create(Vector3 position, Transform parent = null)
        {
            var go = new GameObject("Support Blocker");
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Create Support Blocker");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = new Vector3(6f, 4f, 6f);
            go.AddComponent<BoxCollider>().isTrigger = true;
            TrackSupportBlocker blocker = go.AddComponent<TrackSupportBlocker>();
            UnityEditor.Selection.activeGameObject = go;
            return blocker;
        }

        [UnityEditor.MenuItem("GameObject/VirtualVenues/Support Blocker", isValidateFunction: false, priority: 0)]
        private static void CreateFromMenu(UnityEditor.MenuCommand menuCommand)
        {
            Transform pivot = UnityEditor.SceneView.lastActiveSceneView != null ? UnityEditor.SceneView.lastActiveSceneView.camera.transform : null;
            Create(pivot != null ? UnityEditor.SceneView.lastActiveSceneView.pivot : Vector3.zero, (menuCommand.context as GameObject)?.transform);
        }

        private void OnValidate() { SplineTrack.QueueRebuildAll(); }

        private void OnDrawGizmos()
        {
            Collider box = GetComponent<Collider>();
            if (box == null) { return; }
            Color c = _mode == BlockMode.Block ? new Color(1f, 0.5f, 0.1f) : new Color(0.3f, 0.9f, 0.4f);
            Gizmos.color = new Color(c.r, c.g, c.b, 0.15f);
            Gizmos.DrawCube(box.bounds.center, box.bounds.size);
            Gizmos.color = c;
            Gizmos.DrawWireCube(box.bounds.center, box.bounds.size);
        }
#endif
    }
}
