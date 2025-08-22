#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEditor;
#endif
using UnityEngine;
using System.Collections.Generic;
using System;

namespace VirtualVenues.WorldCreator
{
    public class Stage : MonoBehaviour
    {
        private static List<Stage> _instances = new List<Stage>();
        public static List<Stage> Instances => _instances;

        [SerializeField] private int _stageIndex = 0;

        public int StageIndex => _stageIndex;

        public static Action<Stage> onStageAdded = null;



        private void Awake()
        {
            AddStage(this);
        }

        private static void AddStage(Stage stage)
        {
            if (_instances.Contains(stage))
            {
                return;
            }
            _instances.Add(stage);
            onStageAdded?.Invoke(stage);
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(Stage))]
    public class StageEditor : Editor
    {
        private static readonly string[] fixtureOptions = new string[]
        {
            "Moving Head Fixture",
            "PAR Fixture",
            "Strobe Fixture"
        };

        [MenuItem("GameObject/VirtualVenues/New Stage", isValidateFunction: false, priority: 0)]
        private static void CreateStage(MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("Stage", Vector3.zero);
        }

        [MenuItem("GameObject/VirtualVenues/New Stage", isValidateFunction: true)]
        private static bool ValidateCreateStage(MenuCommand menuCommand)
        {
            return true;
        }

        private void OnSceneGUI()
        {
            Stage stage = (Stage)target;
            if (stage == null) return;

            // Draw the spawn fixture button gizmo
            DrawFixtureSpawnButton(stage);
        }

        private void DrawFixtureSpawnButton(Stage stage)
        {
            Vector3 buttonPosition = stage.transform.position + Vector3.up * 2f;
            float buttonSize = HandleUtility.GetHandleSize(buttonPosition) * 0.5f;
            
            // Check if mouse is near button for highlighting
            float mouseDistance = HandleUtility.DistanceToCircle(buttonPosition, buttonSize);
            bool isHovering = mouseDistance <= 0;
            
            // Set color based on hover state
            Handles.color = isHovering ? Color.cyan : Color.grey;
            
            // Draw outer glow when hovering
            if (isHovering)
            {
                Handles.color = new Color(0f, 1f, 1f, 0.3f);
                Handles.DrawWireDisc(buttonPosition, Camera.current.transform.forward, buttonSize * 1.3f);
                Handles.color = Color.cyan;
            }

            if (Handles.Button(buttonPosition, Quaternion.identity, buttonSize, buttonSize, Handles.SphereHandleCap))
            {
                ShowFixtureMenu(stage);
            }
            
            // Draw Stage title - larger and more prominent
            GUIStyle titleStyle = new GUIStyle();
            titleStyle.normal.textColor = Color.white;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.fontSize = 20; // Larger font size
            
            // Add shadow effect for better visibility
            GUIStyle shadowStyle = new GUIStyle(titleStyle);
            shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.5f);
            
            string stageTitle = $"STAGE {stage.StageIndex}";
            Vector3 labelPosition = buttonPosition + Vector3.up * buttonSize * 1.8f;
            
            // Draw shadow
            Handles.Label(labelPosition + Vector3.right * 0.01f + Vector3.down * 0.01f, stageTitle, shadowStyle);
            // Draw main text
            Handles.Label(labelPosition, stageTitle, titleStyle);
            
            // Draw "+ Add Fixture" text below button when hovering
            if (isHovering)
            {
                GUIStyle hintStyle = new GUIStyle();
                hintStyle.normal.textColor = Color.cyan;
                hintStyle.alignment = TextAnchor.MiddleCenter;
                hintStyle.fontSize = 12;
                Handles.Label(buttonPosition + Vector3.down * buttonSize * 1.2f, "+ Add Fixture", hintStyle);
            }
        }

        private void ShowFixtureMenu(Stage stage)
        {
            GenericMenu menu = new GenericMenu();
            
            foreach (string fixtureName in fixtureOptions)
            {
                menu.AddItem(new GUIContent(fixtureName), false, () => SpawnFixture(stage, fixtureName));
            }
            
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Cancel"), false, null);
            
            menu.ShowAsContext();
        }

        private void SpawnFixture(Stage stage, string fixtureName)
        {
            GameObject prefab = EditorHelpers.LoadFromResources(fixtureName);
            if (prefab == null)
            {
                Debug.LogError($"[StageEditor] Failed to load fixture prefab: {fixtureName}");
                return;
            }
            
            // Instantiate as prefab to maintain prefab connection
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, stage.transform);
            instance.name = fixtureName;
            
            // Position fixture on stage surface
            Vector3 spawnPosition = stage.transform.position;
            spawnPosition.y += 0.5f; // Slight offset above stage
            instance.transform.position = spawnPosition;
            
            // Register undo
            Undo.RegisterCreatedObjectUndo(instance, $"Spawn {fixtureName}");
            
            // Mark scene dirty
            EditorSceneManager.MarkSceneDirty(stage.gameObject.scene);
            
            // Select the new fixture
            Selection.activeObject = instance;
            
            Debug.Log($"[StageEditor] Spawned {fixtureName} on stage '{stage.name}'.");
        }
    }
#endif
}
