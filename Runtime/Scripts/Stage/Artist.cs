using System.Collections.Generic;
using System;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    public class Artist : MonoBehaviour
    {
        private static List<Artist> _instances = new List<Artist>();

        [SerializeField] private Stage _stage = null;
        [SerializeField] private int _stageIndex = 0;
        [Space]
        [SerializeField] private Renderer _artistMesh = null;
        [SerializeField] private Texture2D _defaultTexture = null;
        [Space]
        [SerializeField] private Transform _pivot = null;

        private string _texturePropertyName = "_BaseMap";
        private Vector2 _tilingScale = new Vector2(1f, 0.5f);
        private Vector2 _textureOffset = new Vector2(0f, -0.5f);

        [SerializeField] private string _speakerSource = "";

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
        public Texture2D DefaultTexture => _defaultTexture;
        public string TexturePropertyName => _texturePropertyName;
        public Vector2 TextureOffset => _textureOffset;
        public Vector2 TextureTiling => _tilingScale;
        public Transform Pivot => _pivot;
        public string SpeakerSource => _speakerSource;

        /// <summary>
        /// Initializes this artist from layout data. Safe to call after Awake/AddComponent.
        /// stageIndex is the explicit target stage (ignored when nested under a Stage);
        /// artistPivotOffset is applied to the pivot's local position when a pivot is assigned.
        /// </summary>
        public void Configure(int stageIndex, Vector3 artistPivotOffset, string speakerSource)
        {
            _stageIndex = stageIndex;
            _speakerSource = speakerSource;
            if (_pivot != null) { _pivot.localPosition = artistPivotOffset; }
            // Register AFTER the authored stage index is set (the UWE LayoutLoader adds this component
            // then Configures); else StageCoreConnector would bind using the pre-Configure default.
            // Idempotent — the Start() fallback no-ops once this has run.
            AddArtist(this);
        }

        private void OnValidate()
        {
            if (_artistMesh != null) { _artistMesh.gameObject.hideFlags = HideFlags.HideInHierarchy; }
        }

        private void Awake()
        {
            if (_stage == null)
            {
                _stage = this.gameObject.GetComponentInParent<Stage>();
            }
            // Guard the mesh deref so a runtime-added Artist (e.g. via the UWE LayoutLoader, which
            // AddComponents onto a bare GameObject before assigning a mesh) does not NRE in Awake.
            if (_artistMesh != null && _artistMesh.material != null)
            {
                _tilingScale = _artistMesh.material.GetTextureScale(_texturePropertyName);
                _textureOffset = _artistMesh.material.GetTextureOffset(_texturePropertyName);
            }
            // Registration intentionally deferred to Configure()/Start() — see Configure().
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
    }
}
