using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VirtualVenues.WorldCreator
{
    [RequireComponent(typeof(Collider))]
    public class StageTrigger : MonoBehaviour
    {
        private static List<StageTrigger> _instances = new List<StageTrigger>();

        [SerializeField] private Stage _stage = null;
        [Space]
        [SerializeField] private bool _useOffsetActivation = true;

        public static List<StageTrigger> Instances => _instances;
        public static Action<StageTrigger> onStageTriggerAdded = null;

        public int StageIndex => _stage != null ? _stage.StageIndex : 0;
        public bool UseOffsetActivation => _useOffsetActivation;
        public Collider TriggerCollider { get; private set; }

        private void Reset()
        {
            FindStageReference();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_stage == null)
            {
                FindStageReference();
            }
        }

        public void EDITOR_RESET()
        {
            Reset();
        }
#endif

        private void Awake()
        {
            if (_stage == null)
            {
                var stage = this.gameObject.GetComponentInParent<Stage>();
                if (stage == null)
                {
                    FindStageReference();
                }
                else
                {
                    _stage = stage;
                }
            }

            TriggerCollider = GetComponent<Collider>();

            Debug.Log($"[StageTrigger] Awake - {name}, Stage: {(_stage != null ? _stage.name : "NULL")}, StageIndex: {StageIndex}, Collider: {(TriggerCollider != null ? TriggerCollider.GetType().Name : "NULL")}");

            AddStageTrigger(this);
        }

        private static void AddStageTrigger(StageTrigger trigger)
        {
            if (_instances.Contains(trigger)) { return; }
            _instances.Add(trigger);
            onStageTriggerAdded?.Invoke(trigger);
        }

        private void FindStageReference()
        {
            var stages = FindObjectsByType<Stage>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            float closestDistance = float.MaxValue;
            Stage closestStage = null;

            Vector3 currentPosition = transform.position;

            foreach (var stage in stages)
            {
                float distance = Vector3.Distance(currentPosition, stage.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestStage = stage;
                }
            }

            _stage = closestStage;
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(StageTrigger))]
    [CanEditMultipleObjects]
    public class StageTriggerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            GUILayout.Space(10);
            GUILayout.Label("Stage Trigger Zone", EditorStyles.boldLabel);
            GUILayout.Label("Add any Collider to define the trigger area.");
            GUILayout.Space(10);

            base.OnInspectorGUI();

            if (GUILayout.Button("Reset Stage Reference"))
            {
                foreach (var t in targets)
                {
                    StageTrigger trigger = (StageTrigger)t;
                    Undo.RecordObject(trigger, "Reset Stage Trigger");
                    trigger.EDITOR_RESET();
                    EditorUtility.SetDirty(trigger);
                }
            }
        }

        [MenuItem("GameObject/VirtualVenues/Stage Trigger", isValidateFunction: false, priority: 0)]
        private static void CreateStageTrigger(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Stage Trigger");

            // Parent first so StageTrigger can find the correct Stage reference
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);

            // Add components after parenting
            var boxCollider = go.AddComponent<BoxCollider>();
            boxCollider.size = new Vector3(20f, 10f, 20f);
            var stageTrigger = go.AddComponent<StageTrigger>();

            // Reset to find the correct stage based on new position/hierarchy
            stageTrigger.EDITOR_RESET();

            Undo.RegisterCreatedObjectUndo(go, "Create Stage Trigger");
            Selection.activeObject = go;
        }
    }
#endif
}
