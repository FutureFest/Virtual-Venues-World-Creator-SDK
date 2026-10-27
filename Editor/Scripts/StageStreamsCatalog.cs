using System;
using System.Collections.Generic;
using Auth0;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace VirtualVenues.WorldCreator.Editor
{
    /// <summary>
    /// Lets signed-in creators see their own venue's stage and stream names in the Stage, Screen, Speaker
    /// and Artist inspectors. The creator picks a "preview venue"; its stages and streams (named in the venue
    /// dashboard) fill the pickers. Only the numbers are saved in the world. The venue choice is a per-user
    /// EditorPref, so nothing is written into the creator's project.
    /// </summary>
    [InitializeOnLoad]
    internal static class StageStreamsCatalog
    {
        private const string VenuePrefKey = "VirtualVenues.StageStreams.PreviewVenue";

        [Serializable] private class EventsResponse { public Venue[] events; }
        [Serializable] private class Venue { public string id; public string title; }
        // The host stages response also carries stream keys; only these fields are read, nothing is kept or logged.
        [Serializable] private class StagesResponse { public VenueStage[] stages; }
        [Serializable] private class VenueStage { public int stage_index; public string name; public VenueStream[] streams; }
        [Serializable] private class VenueStream { public int stream_index; public string name; public string type; }

        private static Venue[] _venues = null;
        private static VenueStage[] _stages = null;
        private static string _stagesVenueId = null;
        private static bool _loadingVenues = false;
        private static bool _loadingStages = false;
        private static string _error = null;

        static StageStreamsCatalog()
        {
            StagePickerGUI.DrawVenuePicker = DrawVenuePicker;
            StagePickerGUI.VenueStages = VenueStages;
            StagePickerGUI.VenueStreams = VenueStreams;
            AuthManager.AuthStateChanged += Forget;
        }

        private static string SelectedVenueId
        {
            get => EditorPrefs.GetString(VenuePrefKey, string.Empty);
            set => EditorPrefs.SetString(VenuePrefKey, value ?? string.Empty);
        }

        private static void Forget()
        {
            _venues = null;
            _stages = null;
            _stagesVenueId = null;
            _error = null;
        }

        private static bool SignedIn()
        {
            if (EditorAuthToken.IsValid) { return true; }
            if (AuthManager.Instance.Credentials.TryGetCachedCredentials(out var cached))
            {
                EditorAuthToken.Set(cached.AccessToken, cached.ExpiresAt);
            }
            return EditorAuthToken.IsValid;
        }

        private static void DrawVenuePicker()
        {
            if (!SignedIn()) { return; }
            if (_venues == null && !_loadingVenues) { LoadVenues(); }

            Venue[] venues = _venues ?? new Venue[0];
            string selected = SelectedVenueId;
            string[] labels = new string[venues.Length + 1];
            labels[0] = _loadingVenues ? "Loading venues..." : "None (show numbers)";
            int current = 0;
            for (int i = 0; i < venues.Length; i++)
            {
                labels[i + 1] = string.IsNullOrEmpty(venues[i].title) ? venues[i].id : venues[i].title;
                if (venues[i].id == selected) { current = i + 1; }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                int picked = EditorGUILayout.Popup(new GUIContent("Preview venue", "Shows this venue's stage and stream names in the pickers. Only numbers are saved in the world."), current, labels);
                if (picked != current)
                {
                    SelectedVenueId = picked > 0 ? venues[picked - 1].id : string.Empty;
                    _stages = null;
                    _stagesVenueId = null;
                }
                if (GUILayout.Button(new GUIContent("↻", "Reload venues and their streams"), GUILayout.Width(24))) { Forget(); }
            }
            if (!string.IsNullOrEmpty(_error)) { EditorGUILayout.HelpBox(_error, MessageType.Warning); }
            EditorGUILayout.Space(2);
        }

        private static VenueStage[] CurrentStages()
        {
            string venueId = SelectedVenueId;
            if (string.IsNullOrEmpty(venueId) || !EditorAuthToken.IsValid) { return null; }
            if (_stagesVenueId != venueId && !_loadingStages) { LoadStages(venueId); }
            return _stagesVenueId == venueId ? _stages : null;
        }

        private static KeyValuePair<int, string>[] VenueStages()
        {
            VenueStage[] stages = CurrentStages();
            if (stages == null) { return null; }
            List<KeyValuePair<int, string>> list = new List<KeyValuePair<int, string>>();
            foreach (VenueStage stage in stages) { list.Add(new KeyValuePair<int, string>(stage.stage_index, stage.name)); }
            return list.ToArray();
        }

        private static KeyValuePair<int, string>[] VenueStreams(int stageIndex)
        {
            VenueStage[] stages = CurrentStages();
            if (stages == null) { return null; }
            foreach (VenueStage stage in stages)
            {
                if (stage.stage_index != stageIndex || stage.streams == null) { continue; }
                List<KeyValuePair<int, string>> list = new List<KeyValuePair<int, string>>();
                foreach (VenueStream stream in stage.streams)
                {
                    string kind = stream.type == "realtime" ? "realtime" : "OBS";
                    list.Add(new KeyValuePair<int, string>(stream.stream_index, $"{stream.name} ({kind})"));
                }
                list.Sort((a, b) => a.Key.CompareTo(b.Key));
                return list.ToArray();
            }
            return null;
        }

        private static void LoadVenues()
        {
            _loadingVenues = true;
            Get("vv/events", text =>
            {
                _loadingVenues = false;
                _venues = text != null ? (JsonUtility.FromJson<EventsResponse>(text)?.events ?? new Venue[0]) : new Venue[0];
            });
        }

        private static void LoadStages(string venueId)
        {
            _loadingStages = true;
            Get($"vv/events/{UnityWebRequest.EscapeURL(venueId)}/stages", text =>
            {
                _loadingStages = false;
                _stagesVenueId = venueId;
                _stages = text != null ? (JsonUtility.FromJson<StagesResponse>(text)?.stages ?? new VenueStage[0]) : new VenueStage[0];
            });
        }

        private static void Get(string endpoint, Action<string> done)
        {
            UnityWebRequest www = UnityWebRequest.Get($"{WorldPublisher.WorldPublisherApi.BaseUrl}/{endpoint}");
            www.SetRequestHeader("Authorization", $"Bearer {EditorAuthToken.AccessToken}");
            www.SendWebRequest().completed += _ =>
            {
                bool ok = www.result == UnityWebRequest.Result.Success;
                _error = ok ? null : $"Couldn't load venue streams (HTTP {www.responseCode}). Numbers still work.";
                done(ok ? www.downloadHandler.text : null);
                www.Dispose();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            };
        }
    }
}
