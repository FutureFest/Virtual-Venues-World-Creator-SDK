using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VirtualVenues.WorldCreator
{
    public class Fixture : MonoBehaviour
    {
        public enum FixtureType { None, PAR, MovingHead, Strobe }

        private static List<Fixture> _instances = new List<Fixture>();

        [SerializeField] private Stage _stage = null;
        [Space]
        [SerializeField] private FixtureType _type = FixtureType.None;
        [Space]
        [Tooltip("For AutoShow Rotation for the `Moving Head` fixture type")]
        [SerializeField] private bool _mirrorPanRotation = false;

        public static List<Fixture> Instances => _instances;
        public static Action<Fixture> onFixtureAdded = null;

        public int StageIndex => _stage != null ? _stage.StageIndex : 0;
        public FixtureType Type => _type;
        public bool MirrorPanRotation => _mirrorPanRotation;

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
                if(stage == null)
                {
                    FindStageReference();
                }
            }

            AddFixture(this);
        }

        private static void AddFixture(Fixture fixture)
        {
            if (_instances.Contains(fixture))
            {
                return;
            }
            _instances.Add(fixture);
            onFixtureAdded?.Invoke(fixture);
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
    [CustomEditor(typeof(Fixture))]
    [CanEditMultipleObjects]
    public class FixtureEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            if (targets.All(t => t is Fixture))
            {
                string buttonLabel = "Reset Fixture";
                string typeLabel = $"Fixture Type: {((Fixture)target).Type}";
                if (targets.Length > 1)
                {
                    buttonLabel = "Reset Selected Fixtures";
                    typeLabel = "Fixture Type: Multiple Types Selected";
                }

                GUILayout.Space(10);
                GUILayout.Label(typeLabel);
                GUILayout.Space(10);

                base.OnInspectorGUI();


                if (GUILayout.Button(buttonLabel))
                {
                    foreach (var t in targets)
                    {
                        Fixture fixture = (Fixture)t;
                        Undo.RecordObject(fixture, "Reset Fixture");
                        fixture.EDITOR_RESET();
                        EditorUtility.SetDirty(fixture);
                    }
                }
            }
        }
    }

    [InitializeOnLoad]
    public static class FixtureDuplicationHandler
    {
        static FixtureDuplicationHandler()
        {
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        static double _lastCheckTime = 0;
        static void OnHierarchyChanged()
        {
            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - _lastCheckTime < 0.1f)
                return; // Debounce
            _lastCheckTime = currentTime;

            foreach (Fixture fixture in UnityEngine.Object.FindObjectsByType<Fixture>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                fixture.EDITOR_RESET();
            }
        }
    }
#endif

}
