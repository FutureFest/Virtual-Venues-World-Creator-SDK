using System.Collections.Generic;
using System;
using UnityEngine;
using System.Linq;

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
        private Vector2 _screenTilingScale = new Vector2(1f, 0.5f);
        private Vector2 _screenTextureOffset = Vector2.zero;
        [SerializeField] private string _screenSource = "";
        // Defaults to Artist: the visuals stream is not opened on the rig yet (NetStage's HLS path
        // combines into one feed and its realtime path joins only the artist participant), so a
        // Visuals screen would render black until that plumbing lands. See plan Phase 2 §A.
        [SerializeField] private SpeakerSource _source = SpeakerSource.Artist;

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
        public Vector2 TextureOffset => _screenTextureOffset;
        public Vector2 TextureTiling => _screenTilingScale;
        public string ScreenSource => _screenSource;
        public SpeakerSource Source => _source;

        /// <summary>
        /// Initializes this screen from layout data. Safe to call after Awake/AddComponent.
        /// stageIndex is the explicit target stage (ignored when nested under a Stage); source is
        /// "Artist" or "Visuals" (which stream this screen displays).
        /// </summary>
        public void Configure(int stageIndex, string source)
        {
            _stageIndex = stageIndex;
            _screenSource = source;
            _source = ParseSource(source);
            // Register only AFTER the authored values are set. The UWE LayoutLoader AddComponents this
            // marker (firing Awake) and THEN calls Configure one step later; registering in Awake would
            // make StageCoreConnector bind using the pre-Configure defaults (stageIndex 0 / Artist)
            // instead of the authored stage/source. AddScreen is idempotent (Instances-dedup), so the
            // Start() fallback below is a no-op once Configure has registered.
            AddScreen(this);
        }

        private static SpeakerSource ParseSource(string source)
        {
            if (source == "Visuals") { return SpeakerSource.Visuals; }
            return SpeakerSource.Artist;
        }

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
            }
            // Registration intentionally deferred to Configure()/Start() — see Configure().
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
    }
}
