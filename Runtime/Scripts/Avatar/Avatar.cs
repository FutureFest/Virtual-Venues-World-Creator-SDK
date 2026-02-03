using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues
{
    /// <summary>
    /// Avatar component for VirtualVenues custom avatars.
    /// Configure renderer slots, item slots, and fallback content.
    /// At runtime in FutureFest, this is swapped for AvatarCustomization.
    /// </summary>
    [AddComponentMenu("VirtualVenues/Avatar")]
    public class Avatar : MonoBehaviour
    {
        [Header("Animation")]
        [SerializeField] private Animator _animator;

        [Header("Customization Slots")]
        [SerializeField] private List<RendererSlot> _rendererSlots = new List<RendererSlot>();
        [SerializeField] private List<ItemSlot> _itemSlots = new List<ItemSlot>();

        [Header("LOD")]
        [SerializeField] private LODGroup _lodGroup;
        [SerializeField] private float _cameraLookAtPointOffset = 0.75f;

        [Header("Fallback Content")]
        [SerializeField] private Material _fallbackBodyPaint;
        [SerializeField] private Material _fallbackFace;
        [SerializeField] private GameObject _fallbackGameObject;

        [Header("Loading Content")]
        [SerializeField] private Material _loadingMaterial;
        [SerializeField] private GameObject _loadingItem;

        // Public accessors for runtime swap
        public Animator Animator => _animator;
        public List<RendererSlot> RendererSlots => _rendererSlots;
        public List<ItemSlot> ItemSlots => _itemSlots;
        public LODGroup LodGroup => _lodGroup;
        public float CameraLookAtPointOffset => _cameraLookAtPointOffset;
        public Material FallbackBodyPaint => _fallbackBodyPaint;
        public Material FallbackFace => _fallbackFace;
        public GameObject FallbackGameObject => _fallbackGameObject;
        public Material LoadingMaterial => _loadingMaterial;
        public GameObject LoadingItem => _loadingItem;
    }

    [Serializable]
    public class RendererSlot
    {
        public SlotCategory category;
        public List<Renderer> renderers = new List<Renderer>();
        public int materialIndex;
        public Material fallbackMaterial;
    }

    [Serializable]
    public class ItemSlot
    {
        public SlotCategory category;
        public Transform itemPivot;
        public GameObject fallbackItem;
    }

    /// <summary>
    /// Slot categories for avatar customization.
    /// Mirrors FutureFest.Character.Customization.SlotAssetCategory.
    /// </summary>
    public enum SlotCategory
    {
        Back,
        BodyPaint,
        Face,
        FacePlate,
        Team,
        Hat,
        Neck,
        Head,
        Avatar,
        Hair,
        Costume,
        ForeArm_L,
        ForeArm_R
    }
}
