using UnityEditor;
using UnityEngine;

namespace VirtualVenues.Editor.ProjectSetup
{
    /// <summary>
    /// Thin host window for the Project Setup tool. Lives first under the VirtualVenues menu because
    /// setup should run before publishing. All logic is in <see cref="ProjectSetupView"/>.
    /// </summary>
    public class ProjectSetupWindow : EditorWindow
    {
        [MenuItem("VirtualVenues/Project Setup", priority = 0)]
        public static void ShowWindow()
        {
            ProjectSetupWindow window = GetWindow<ProjectSetupWindow>();
            window.titleContent = new GUIContent("Project Setup");
            window.minSize = new Vector2(460, 520);
        }

        private void CreateGUI()
        {
            rootVisualElement.Add(new ProjectSetupView());
        }
    }
}
