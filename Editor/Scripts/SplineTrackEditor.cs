using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Splines;

namespace VirtualVenues.WorldCreator.Editor
{
    /// <summary>
    /// Track inspector: numbered markers on every track point in the Scene view; clicking one opens Unity's spline
    /// tools on it (move / rotate / bend handles).
    /// </summary>
    [CustomEditor(typeof(SplineTrack))]
    public class SplineTrackEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditTrackShapeButton();
            EditorGUILayout.Space();
            DrawDefaultInspector();
            SupportsHelp((SplineTrack)target);
        }

        private static void SupportsHelp(SplineTrack track)
        {
            if (!track.ShowSupports) { return; }
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Each support post drops until it hits a solid collider (ground, a tunnel roof, a building), never below Ground Height.\n" +
                "• Tunnel: give its roof a collider, and posts stand on top of it.\n" +
                "• Invisible no-post zone: a trigger box with a Support Blocker.\n" +
                "• Posts landing on a fence or lamp: add a Support Blocker set to Let Supports Through.",
                MessageType.Info);
            if (GUILayout.Button(new GUIContent("Create Support Blocker",
                "Adds an invisible trigger box at the middle of the track. Posts stop on top of it. Move and scale it where you need it.")))
            {
                Bounds b = track.GetComponent<MeshRenderer>().bounds;
                TrackSupportBlocker.Create(new Vector3(b.center.x, b.min.y, b.center.z));
            }
        }

        private void OnSceneGUI()
        {
            DrawKnotMarkers(((SplineTrack)target).GetComponent<SplineContainer>());
        }

        private static bool Editing => ToolManager.activeContextType == typeof(SplineToolContext);

        /// <summary>Numbered, clickable dots on each track point. Hidden while Unity's spline tools are showing their own handles.</summary>
        public static void DrawKnotMarkers(SplineContainer container)
        {
            if (container == null || container.Spline == null || Editing) { return; }
            Handles.color = new Color(0.2f, 0.9f, 1f);
            for (int i = 0; i < container.Spline.Count; i++)
            {
                Vector3 p = container.transform.TransformPoint((Vector3)container.Spline[i].Position);
                float size = HandleUtility.GetHandleSize(p) * 0.1f;
                Handles.Label(p + Vector3.up * size * 2.5f, $"Point {i}", EditorStyles.whiteBoldLabel);
                if (Handles.Button(p, Quaternion.identity, size, size * 1.2f, Handles.SphereHandleCap))
                {
                    SplineSelection.Set(new SelectableKnot(new SplineInfo(container, 0), i));
                    StartEditing();
                }
            }
        }

        private static void StartEditing()
        {
            ToolManager.SetActiveContext<SplineToolContext>();
            Tools.current = Tool.Move;
            SceneView.RepaintAll();
        }

        /// <summary>Switches the Scene view to Unity's spline tools for the selected track. Shared with the ride inspector.</summary>
        public static void EditTrackShapeButton()
        {
            bool editing = Editing;
            if (GUILayout.Button(new GUIContent(editing ? "Stop Editing Track Shape" : "Edit Track Shape",
                "Show move / rotate / bend handles on the track points in the Scene view. Or click a numbered point in the Scene view.")))
            {
                if (editing)
                {
                    ToolManager.SetActiveContext<GameObjectToolContext>();
                    SceneView.RepaintAll();
                }
                else { StartEditing(); }
            }
            EditorGUILayout.HelpBox(editing
                ? "In the Scene view, click a track point, then:\n" +
                  "• Move (W): drag the point to a new position.\n" +
                  "• Rotate (E): turn the point. Rolling around the track direction banks the track.\n" +
                  "• Drag the small tangent dots on each side of a point to bend the curve.\n" +
                  "• Draw Splines (Scene view tool bar): click to add points.\n" +
                  "The rails and ride timing update as you drag."
                : "Click a numbered track point in the Scene view (or this button) to move, rotate or bend the track.",
                MessageType.Info);
        }
    }
}
