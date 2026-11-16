using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace VirtualVenues.WorldCreator
{
    public class Screen : MonoBehaviour
    {
        private static List<Screen> _instances = new List<Screen>();

        [SerializeField] private Stage _stage = null;
        [SerializeField] private int _stageIndex = 0;
        [Space]
        [SerializeField] private Renderer _screenMesh = null;
        [SerializeField] private Texture2D _defaultTexture = null;
        private string _texturePropertyName = "_BaseMap";
        // The mesh material's own tiling/offset, read in Awake. Without a mesh material the stream layout's code default applies.
        private bool _hasMaterialTransform = false;
        private Vector2 _screenTilingScale = Vector2.one;
        private Vector2 _screenTextureOffset = Vector2.zero;
        [SerializeField, HideInInspector] private string _screenSource = "";
        [Tooltip("Which of the stage's streams this screen shows. Stream 0 used to be called Artist, stream 1 Visuals.")]
        [FormerlySerializedAs("_source")]
        [SerializeField, Min(0)] private int _streamIndex = 0;
        [Tooltip("Which part of the video frame this screen shows. Whole video: the full frame, for a movie or a full-frame visuals stream. " +
                 "Stacked: the bottom half of one video that carries the artist on top and the visuals below (the top half when it shows the artist).")]
        // Stacked (0) is also what a screen saved before this field existed reads, so published worlds keep playing stacked.
        [FormerlySerializedAs("_videoArea")]
        [SerializeField] private StreamLayout _streamLayout = StreamLayout.Stacked;
        [Tooltip("What this screen shows. Visuals: the video as it is. Artist (green screen): the artist with the green screen keyed out, " +
                 "like the Artist cut-out, but flat. Put it in front of a visuals Screen.")]
        // Visuals (0) is also what a screen saved before this field existed reads, so published worlds keep the plain screen.
        [SerializeField] private ScreenContent _content = ScreenContent.Visuals;

        public static List<Screen> Instances => _instances;
        public static Action<Screen> onScreenAdded = null;

        // Nested under a Stage (e.g. a creator's Stage prefab, or a screen nested in the UWE
        // hierarchy) inherits the parent's index; a standalone screen uses its own _stageIndex.
        // Lazily resolve the parent so layout spawn-then-reparent ordering still inherits.
        public int StageIndex
        {
            get
            {
                if (_stage == null) { _stage = GetComponentInParent<Stage>(); }
                return _stage != null ? _stage.StageIndex : _stageIndex;
            }
        }
        public Renderer Renderer => _screenMesh;
        public Texture2D DefaultTexture => _defaultTexture;
        public string TexturePropertyName => _texturePropertyName;
        // What Awake read from the mesh material (for either stream layout: in the SDK the stream layout picks the material), else
        // the stream layout's code default (StageStreamLayouts): the whole frame, or the bottom half of a stacked video
        // (the top half when the screen shows the artist).
        public Vector2 TextureOffset { get { GetTransform(out _, out Vector2 offset); return offset; } }
        public Vector2 TextureTiling { get { GetTransform(out Vector2 tiling, out _); return tiling; } }
        public StreamLayout StreamLayout => _streamLayout;
        // Visuals: the plain screen shader. Artist: the Artist's chroma key shader on this flat screen (the app picks it).
        public ScreenContent Content => _content;
        public string ScreenSource => _screenSource;
        public int StreamIndex => _streamIndex;
        [Obsolete("Use StreamIndex.")]
        public SpeakerSource Source => _streamIndex == 1 ? SpeakerSource.Visuals : SpeakerSource.Artist;

        /// <summary>
        /// Initializes this screen from layout data. Safe to call after Awake/AddComponent.
        /// stageIndex is the explicit target stage (ignored when nested under a Stage); source names the
        /// stream this screen displays (see StageStreams).
        /// </summary>
        public void Configure(int stageIndex, string source)
        {
            _stageIndex = stageIndex;
            _screenSource = source;
            _streamIndex = StageStreams.Parse(source);
            // Register only AFTER the authored values are set. The UWE LayoutLoader AddComponents this
            // marker (firing Awake) and THEN calls Configure one step later; registering in Awake would
            // make StageCoreConnector bind using the pre-Configure defaults (stageIndex 0 / Artist)
            // instead of the authored stage/source. AddScreen is idempotent (Instances-dedup), so the
            // Start() fallback below is a no-op once Configure has registered.
            AddScreen(this);
        }

        /// <summary>
        /// As Configure(stageIndex, source), plus the part of the video frame this screen shows:
        /// "whole" = Whole video, anything else ("" included) = Stacked (see StageStreamLayouts).
        /// </summary>
        public void Configure(int stageIndex, string source, string streamLayout)
        {
            _streamLayout = StageStreamLayouts.Parse(streamLayout);
            Configure(stageIndex, source);
        }

        /// <summary>
        /// As Configure(stageIndex, source, streamLayout), plus what this screen shows: "artist" = the artist with the
        /// green screen keyed out, anything else ("" included) = Visuals (see ScreenContents).
        /// </summary>
        public void Configure(int stageIndex, string source, string streamLayout, string content)
        {
            _content = ScreenContents.Parse(content);
            Configure(stageIndex, source, streamLayout);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_stage != null) { _stageIndex = _stage.StageIndex; }
        }
#endif

        private void Awake()
        {
            if(_stage == null)
            {
                _stage = this.gameObject.GetComponentInParent<Stage>();
            }
            // Guard the mesh deref so a runtime-added Screen (e.g. via the UWE LayoutLoader, which
            // AddComponents onto a bare GameObject before assigning a mesh) does not NRE in Awake.
            if (_screenMesh != null && _screenMesh.material != null)
            {
                _screenTilingScale = _screenMesh.material.GetTextureScale(_texturePropertyName);
                _screenTextureOffset = _screenMesh.material.GetTextureOffset(_texturePropertyName);
                _hasMaterialTransform = true;
            }
            // Registration intentionally deferred to Configure()/Start() — see Configure().
        }

        private void GetTransform(out Vector2 tiling, out Vector2 offset)
        {
            if (_hasMaterialTransform)
            {
                tiling = _screenTilingScale;
                offset = _screenTextureOffset;
                return;
            }
            StageStreamLayouts.GetDefaultTransform(_streamLayout, _content == ScreenContent.Artist, out tiling, out offset);
        }

        private void Start()
        {
            // Fallback for authored (prefab-placed) screens that are never Configure()'d by the layout:
            // register with the serialized values. Idempotent — a no-op when Configure() already did.
            AddScreen(this);
        }

        private static void AddScreen(Screen screen)
        {
            if (_instances.Contains(screen))
            {
                return;
            }
            _instances.Add(screen);
            onScreenAdded?.Invoke(screen);
        }

        private void OnDestroy()
        {
            _instances.Remove(this);
        }
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(Screen))]
    [UnityEditor.CanEditMultipleObjects]
    public class ScreenEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() { StagePickerGUI.DrawInspector(serializedObject); }

        [UnityEditor.MenuItem(CreateMenu.StageMenu + "Screen", isValidateFunction: false, priority: CreateMenu.StageOrder + 2)]
        private static void CreateScreen(UnityEditor.MenuCommand menuCommand)
        {
            EditorHelpers.SpawnEditorObject("Screen", Vector3.zero);
        }
    }
#endif
}
