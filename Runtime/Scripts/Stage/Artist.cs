using System.Collections.Generic;
using System;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    public class Artist : MonoBehaviour
    {
        private static List<Artist> _instances = new List<Artist>();

        [SerializeField] private Stage _stage = null;
        [Space]
        [SerializeField] private Renderer _artistMesh = null;
        [SerializeField] private Texture2D _defaultTexture = null;
        [Space]
        [SerializeField] private Transform _pivot = null;

        private string _texturePropertyName = "_BaseMap";
        private Vector2 _tilingScale = new Vector2(1f, 0.5f);
        private Vector2 _textureOffset = new Vector2(0f, -0.5f);

        public static List<Artist> Instances => _instances;
        public static Action<Artist> onArtistAdded = null;

        public int StageIndex => _stage != null ? _stage.StageIndex : 0;
        public Renderer Renderer => _artistMesh;
        public Texture2D DefaultTexture => _defaultTexture;
        public string TexturePropertyName => _texturePropertyName;
        public Vector2 TextureOffset => _textureOffset;
        public Vector2 TextureTiling => _tilingScale;
        public Transform Pivot => _pivot;

        private void Awake()
        {
            if (_stage == null)
            {
                _stage = this.gameObject.GetComponentInParent<Stage>();
            }
            _tilingScale = _artistMesh.material.GetTextureScale(_texturePropertyName);
            _textureOffset = _artistMesh.material.GetTextureOffset(_texturePropertyName);

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
