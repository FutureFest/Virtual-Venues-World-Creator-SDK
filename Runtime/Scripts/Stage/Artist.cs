using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace VirtualVenues.WorldCreator
{
    public class Artist : MonoBehaviour
    {
        private static List<Artist> _instances = new List<Artist>();

        [SerializeField] private Stage _stage = null;
        [SerializeField] private int _stageIndex = 0;
        [Space]
        [SerializeField] private Renderer _artistMesh = null;
        [Tooltip("Which of the mesh's materials the stream goes on, for a mesh with several materials. The other materials are left alone.")]
        [SerializeField, Min(0)] private int _materialIndex = 0;
        [SerializeField] private Texture2D _defaultTexture = null;
        [Space]
        [SerializeField] private Transform _pivot = null;

        private string _texturePropertyName = "_BaseMap";
        // The mesh material's own tiling/offset, read in Awake. Without a mesh material the stream layout's code default applies.
        private bool _hasMaterialTransform = false;
        private Vector2 _tilingScale = Vector2.one;
        private Vector2 _textureOffset = Vector2.zero;

        [Tooltip("Which of the stage's streams this artist cut-out shows. Stream 0 used to be called Artist.")]
        [SerializeField, Min(0)] private int _streamIndex = 0;
        [Tooltip("Which part of the video frame this artist cut-out shows. Whole video: the full frame, for a full-frame artist stream. " +
                 "Stacked: the top half of one video that carries the artist on top and the visuals below.")]
        // Stacked (0) is also what an artist saved before this field existed reads, so published worlds keep playing stacked.
        [FormerlySerializedAs("_videoArea")]
        [SerializeField] private StreamLayout _streamLayout = StreamLayout.Stacked;
        // Legacy layout string ("Artist", "Visuals", or digits). Read once into _streamIndex.
        [SerializeField, HideInInspector] private string _speakerSource = "";

        public static List<Artist> Instances => _instances;
        public static Action<Artist> onArtistAdded = null;

        // Nested under a Stage inherits the parent's index; a standalone artist uses its own
        // _stageIndex. Lazily resolve the parent so layout spawn-then-reparent ordering still inherits.
        public int StageIndex
        {
            get
            {
                if (_stage == null) { _stage = GetComponentInParent<Stage>(); }
                return _stage != null ? _stage.StageIndex : _stageIndex;
            }
        }
        public Renderer Renderer => _artistMesh;
        // The material slot the stream goes on, as authored. StageMaterialSlots.Clamp fits it to the mesh actually used.
        public int MaterialIndex => _materialIndex;
        public Texture2D DefaultTexture => _defaultTexture;
        public string TexturePropertyName => _texturePropertyName;
        // What Awake read from the mesh material (for either stream layout: in the SDK the stream layout picks the material), else
        // the stream layout's code default (StageStreamLayouts): the whole frame, or the top half of a stacked video.
        public Vector2 TextureOffset { get { GetTransform(out _, out Vector2 offset); return offset; } }
        public Vector2 TextureTiling { get { GetTransform(out Vector2 tiling, out _); return tiling; } }
        public StreamLayout StreamLayout => _streamLayout;
        public Transform Pivot => _pivot;
        public int StreamIndex => string.IsNullOrEmpty(_speakerSource) ? _streamIndex : StageStreams.Parse(_speakerSource);
        [Obsolete("Use StreamIndex.")]
        public string SpeakerSource => StageStreams.Format(StreamIndex);

        /// <summary>
        /// Initializes this artist from layout data. Safe to call after Awake/AddComponent.
        /// stageIndex is the explicit target stage (ignored when nested under a Stage);
        /// artistPivotOffset is applied to the pivot's local position when a pivot is assigned;
        /// speakerSource names the stream (see StageStreams).
        /// </summary>
        public void Configure(int stageIndex, Vector3 artistPivotOffset, string speakerSource)
        {
            _stageIndex = stageIndex;
            _streamIndex = StageStreams.Parse(speakerSource);
            _speakerSource = string.Empty;
            if (_pivot != null) { _pivot.localPosition = artistPivotOffset; }
            // Register AFTER the authored stage index is set (the UWE LayoutLoader adds this component
            // then Configures); else StageCoreConnector would bind using the pre-Configure default.
            // Idempotent — the Start() fallback no-ops once this has run.
            AddArtist(this);
        }

        /// <summary>
        /// As Configure(stageIndex, artistPivotOffset, speakerSource), plus the part of the video frame this artist
        /// shows: "whole" = Whole video, anything else ("" included) = Stacked (see StageStreamLayouts).
        /// </summary>
        public void Configure(int stageIndex, Vector3 artistPivotOffset, string speakerSource, string streamLayout)
        {
            _streamLayout = StageStreamLayouts.Parse(streamLayout);
            Configure(stageIndex, artistPivotOffset, speakerSource);
        }

        /// <summary>
        /// As Configure(stageIndex, artistPivotOffset, speakerSource, streamLayout), plus the mesh's material slot the stream
        /// goes on (negative reads 0; past the mesh's last slot, the last slot is used).
        /// </summary>
        public void Configure(int stageIndex, Vector3 artistPivotOffset, string speakerSource, string streamLayout, int materialIndex)
        {
            _materialIndex = Mathf.Max(0, materialIndex);
            Configure(stageIndex, artistPivotOffset, speakerSource, streamLayout);
        }

        private void OnValidate()
        {
            if (_artistMesh != null) { _artistMesh.gameObject.hideFlags = HideFlags.HideInHierarchy; }
            if (!string.IsNullOrEmpty(_speakerSource))
            {
                _streamIndex = StageStreams.Parse(_speakerSource);
                _speakerSource = string.Empty;
            }
            if (_stage != null) { _stageIndex = _stage.StageIndex; }
        }

        private void Awake()
        {
            if (_stage == null)
            {
                _stage = this.gameObject.GetComponentInParent<Stage>();
            }
            // Guard the mesh deref so a runtime-added Artist (e.g. via the UWE LayoutLoader, which
            // AddComponents onto a bare GameObject before assigning a mesh) does not NRE in Awake.
            // The material in the marker's Material Slot (clamped). Read from the shared material: same values, no instance.
            Material material = StageMaterialSlots.SharedMaterial(_artistMesh, _materialIndex);
            if (material != null)
            {
                _tilingScale = material.GetTextureScale(_texturePropertyName);
                _textureOffset = material.GetTextureOffset(_texturePropertyName);
                _hasMaterialTransform = true;
            }
            // Registration intentionally deferred to Configure()/Start() — see Configure().
        }

        private void GetTransform(out Vector2 tiling, out Vector2 offset)
        {
            if (_hasMaterialTransform)
            {
                tiling = _tilingScale;
                offset = _textureOffset;
                return;
            }
            StageStreamLayouts.GetDefaultTransform(_streamLayout, true, out tiling, out offset);
        }

        private void Start()
        {
            // Fallback for authored (prefab-placed) artists never Configure()'d by the layout:
            // register with the serialized values. Idempotent — a no-op when Configure() already did.
            AddArtist(this);
        }

        private static void AddArtist(Artist artist)
        {
            if (_instances.Contains(artist))
            {
                return;
            }
            _instances.Add(artist);
            onArtistAdded?.Invoke(artist);
        }

        private void OnDestroy()
        {
            _instances.Remove(this);
        }
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(Artist))]
    [UnityEditor.CanEditMultipleObjects]
    public class ArtistEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() { StagePickerGUI.DrawInspector(serializedObject); }

        [UnityEditor.MenuItem(CreateMenu.StageMenu + "Artist", isValidateFunction: false, priority: CreateMenu.StageOrder + 3)]
        private static void CreateArtist(UnityEditor.MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("Artist", Vector3.zero);
        }
    }
#endif
}
