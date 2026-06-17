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
        [Space]
        [SerializeField] private Renderer _screenMesh = null;
        [SerializeField] private Texture2D _defaultTexture = null;
        private string _texturePropertyName = "_BaseMap";
        private Vector2 _screenTilingScale = new Vector2(1f, 0.5f);
        private Vector2 _screenTextureOffset = Vector2.zero;
        [SerializeField] private string _screenSource = "";

        public static List<Screen> Instances => _instances;
        public static Action<Screen> onScreenAdded = null;

        public int StageIndex => _stage != null ? _stage.StageIndex : 0;
        public Renderer Renderer => _screenMesh;
        public Texture2D DefaultTexture => _defaultTexture;
        public string TexturePropertyName => _texturePropertyName;
        public Vector2 TextureOffset => _screenTextureOffset;
        public Vector2 TextureTiling => _screenTilingScale;
        public string ScreenSource => _screenSource;

        /// <summary>
        /// Initializes this screen from layout data. Safe to call after Awake/AddComponent.
        /// </summary>
        public void Configure(string screenSource)
        {
            _screenSource = screenSource;
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
