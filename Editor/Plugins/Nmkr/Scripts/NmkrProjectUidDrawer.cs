using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VirtualVenues.Plugins.Nmkr;

namespace VirtualVenues.Plugins.Nmkr.Editor
{
    /// <summary>
    /// Draws an [NmkrProjectUid] string field as a dropdown of the creator's
    /// NMKR projects, fetched via <see cref="NmkrEditorApiClient"/>. Falls back
    /// to an inline help/error box when NMKR is not configured or the fetch
    /// fails. Includes a Refresh button that bypasses the project-list cache.
    /// </summary>
    [CustomPropertyDrawer(typeof(NmkrProjectUidAttribute))]
    public class NmkrProjectUidDrawer : PropertyDrawer
    {
        private const float REFRESH_WIDTH = 60f;
        private const double CONFIG_CACHE_TTL = 2.0;

        // Shared across all [NmkrProjectUid] fields — they all target the same
        // creator config, so a single fetch result serves every drawer.
        private static bool _loading;
        private static bool _fetchAttempted;
        private static NmkrEditorApiClient.FetchResult _result;

        private static NmkrPluginConfig _cachedConfig;
        private static double _configCacheTime = -100.0;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            NmkrPluginConfig cfg = GetConfig();
            if (cfg == null || string.IsNullOrEmpty(cfg.ApiKey))
            {
                return line * 2f;
            }
            if (_fetchAttempted && !_loading && !_result.Success)
            {
                return line * 2f + spacing + line;
            }
            return line;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "[NmkrProjectUid] only works on string fields.");
                return;
            }

            NmkrPluginConfig cfg = GetConfig();
            if (cfg == null || string.IsNullOrEmpty(cfg.ApiKey))
            {
                EditorGUI.HelpBox(position,
                    "NMKR not configured. Open VirtualVenues > Plugins > NMKR Settings.",
                    MessageType.Warning);
                return;
            }

            if (!_fetchAttempted && !_loading) { BeginFetch(cfg, false); }

            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            if (_loading)
            {
                Rect loadingRect = new Rect(position.x, position.y, position.width, line);
                EditorGUI.LabelField(loadingRect, label, new GUIContent("Loading NMKR projects..."));
                return;
            }

            if (!_result.Success)
            {
                Rect boxRect = new Rect(position.x, position.y, position.width, line * 2f);
                EditorGUI.HelpBox(boxRect, _result.Error, MessageType.Error);
                Rect retryRect = new Rect(position.xMax - REFRESH_WIDTH, position.y + line * 2f + spacing, REFRESH_WIDTH, line);
                if (GUI.Button(retryRect, "Refresh")) { BeginFetch(cfg, true); }
                return;
            }

            Rect popupRect = new Rect(position.x, position.y, position.width - REFRESH_WIDTH - spacing, line);
            Rect refreshRect = new Rect(position.xMax - REFRESH_WIDTH, position.y, REFRESH_WIDTH, line);
            DrawDropdown(popupRect, property, label, _result.Projects);
            if (GUI.Button(refreshRect, "Refresh")) { BeginFetch(cfg, true); }
        }

        private static void DrawDropdown(Rect rect, SerializedProperty property, GUIContent label,
            List<NmkrEditorApiClient.ProjectSummary> projects)
        {
            if (projects == null || projects.Count == 0)
            {
                EditorGUI.LabelField(rect, label, new GUIContent("No NMKR projects found for this account."));
                return;
            }

            string current = property.stringValue;

            List<string> options = new List<string> { "(none)" };
            int selected = 0;
            for (int i = 0; i < projects.Count; i++)
            {
                options.Add(projects[i].DisplayName);
                if (projects[i].uid == current) { selected = i + 1; }
            }

            // A stored uid that no longer matches any listed project (deleted,
            // or belongs to a different account) is shown as a trailing
            // "unknown" entry so the value isn't silently dropped.
            if (selected == 0 && !string.IsNullOrEmpty(current))
            {
                options.Add($"(unknown: {current})");
                selected = options.Count - 1;
            }

            int newSelected = EditorGUI.Popup(rect, label.text, selected, options.ToArray());
            if (newSelected == selected) { return; }

            if (newSelected == 0)
            {
                property.stringValue = string.Empty;
            }
            else if (newSelected >= 1 && newSelected <= projects.Count)
            {
                property.stringValue = projects[newSelected - 1].uid;
            }
            // Re-selecting the trailing "unknown" entry leaves the value as-is.
        }

        private static NmkrPluginConfig GetConfig()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _configCacheTime > CONFIG_CACHE_TTL)
            {
                _cachedConfig = NmkrConfigLocator.FindLocalConfig();
                _configCacheTime = now;
            }
            return _cachedConfig;
        }

        private static async void BeginFetch(NmkrPluginConfig cfg, bool bypassCache)
        {
            _loading = true;
            NmkrEditorApiClient.FetchResult result = await NmkrEditorApiClient.ListProjectsAsync(cfg, bypassCache);
            _result = result;
            _loading = false;
            _fetchAttempted = true;
        }
    }
}
