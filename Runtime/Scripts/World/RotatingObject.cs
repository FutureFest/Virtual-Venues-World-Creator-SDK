using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Marker component that defines rotation behavior for a GameObject.
    /// Provides runtime preview when used standalone (e.g., in WorldCreatorSDK package).
    /// In production, core systems replace this with NetworkedRotation for proper synchronization.
    /// </summary>
    public class RotatingObject : MonoBehaviour
    {
        [Header("Rotation Settings")]
        [Tooltip("Rotation speed per axis in degrees per second (X, Y, Z)")]
        public Vector3 rotationAxis = Vector3.up * 90f;

        [Header("Network Settings")]
        [Tooltip("When enabled, rotation will be synchronized across all networked clients using deterministic time")]
        public bool networkSynced = false;

        // Static management for runtime discovery
        private static List<RotatingObject> _instances = new List<RotatingObject>();
        public static List<RotatingObject> Instances => _instances;
        public static Action<RotatingObject> onRotatingObjectAdded = null;

        private void Awake()
        {
            AddRotatingObject(this);
        }

        private void OnDestroy()
        {
            RemoveRotatingObject(this);
        }

        private static void AddRotatingObject(RotatingObject rotatingObject)
        {
            if (_instances.Contains(rotatingObject))
            {
                return;
            }
            _instances.Add(rotatingObject);
            onRotatingObjectAdded?.Invoke(rotatingObject);
        }

        private static void RemoveRotatingObject(RotatingObject rotatingObject)
        {
            _instances.Remove(rotatingObject);
        }

        private void Update()
        {
            // Provide runtime preview when this component is active
            // In production, this component gets disabled and NetworkedRotation takes over
            // This allows WorldCreatorSDK package users to preview rotation without core systems
            if (enabled)
            {
                transform.Rotate(rotationAxis * Time.deltaTime, Space.Self);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Draw rotation axis indicators
            Vector3 position = transform.position;

            // X-axis (Red)
            if (Mathf.Abs(rotationAxis.x) > 0.01f)
            {
                Gizmos.color = new Color(1f, 0f, 0f, 0.8f);
                float length = Mathf.Abs(rotationAxis.x) * 0.01f;
                Gizmos.DrawRay(position, transform.right * length);
                DrawArrowHead(position + transform.right * length, transform.right, 0.1f);
            }

            // Y-axis (Green)
            if (Mathf.Abs(rotationAxis.y) > 0.01f)
            {
                Gizmos.color = new Color(0f, 1f, 0f, 0.8f);
                float length = Mathf.Abs(rotationAxis.y) * 0.01f;
                Gizmos.DrawRay(position, transform.up * length);
                DrawArrowHead(position + transform.up * length, transform.up, 0.1f);
            }

            // Z-axis (Blue)
            if (Mathf.Abs(rotationAxis.z) > 0.01f)
            {
                Gizmos.color = new Color(0f, 0f, 1f, 0.8f);
                float length = Mathf.Abs(rotationAxis.z) * 0.01f;
                Gizmos.DrawRay(position, transform.forward * length);
                DrawArrowHead(position + transform.forward * length, transform.forward, 0.1f);
            }

            // Draw rotating circle to indicate networked status
            if (networkSynced)
            {
                Gizmos.color = new Color(0f, 1f, 1f, 0.5f); // Cyan for networked
                DrawCircle(position, 0.5f, 16);
            }
        }

        private void DrawArrowHead(Vector3 position, Vector3 direction, float size)
        {
            Vector3 right = Vector3.Cross(direction, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.01f)
            {
                right = Vector3.Cross(direction, Vector3.forward).normalized;
            }
            Vector3 up = Vector3.Cross(direction, right).normalized;

            Gizmos.DrawRay(position, -direction * size + right * size * 0.5f);
            Gizmos.DrawRay(position, -direction * size - right * size * 0.5f);
            Gizmos.DrawRay(position, -direction * size + up * size * 0.5f);
            Gizmos.DrawRay(position, -direction * size - up * size * 0.5f);
        }

        private void DrawCircle(Vector3 center, float radius, int segments)
        {
            float angleStep = 360f / segments;
            Vector3 prevPoint = center + new Vector3(radius, 0, 0);

            for (int i = 1; i <= segments; i++)
            {
                float angle = angleStep * i * Mathf.Deg2Rad;
                Vector3 newPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(prevPoint, newPoint);
                prevPoint = newPoint;
            }
        }
#endif
    }
}
