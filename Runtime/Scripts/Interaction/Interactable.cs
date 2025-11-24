using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace VirtualVenues.WorldCreator
{
    public class Interactable : MonoBehaviour
    {
        // Static management for runtime discovery
        private static InstanceTracker<Interactable> _tracker = new InstanceTracker<Interactable>();
        public static InstanceTracker<Interactable> Tracker => _tracker;

        [Header("Interaction Settings")]
        [SerializeField] private string _interactionDisplayText = "Press {0} to interact";

        [Header("Highlight Renderers")]
        [Tooltip("Optional: Renderers that will be toggled on/off when highlighted")]
        [SerializeField] private Renderer[] _highlightRenderers = null;

        [Header("Custom Highlight Events")]
        [Tooltip("Invoked when player approaches and the interactable is highlighted")]
        [SerializeField] private UnityEvent _onHighlight = null;

        [Tooltip("Invoked when player moves away and the interactable is unhighlighted")]
        [SerializeField] private UnityEvent _onUnhighlight = null;

        public string InteractionDisplayText => _interactionDisplayText;
        public Renderer[] HighlightRenderers => _highlightRenderers;
        public UnityEvent OnHighlight => _onHighlight;
        public UnityEvent OnUnhighlight => _onUnhighlight;

        private void Awake()
        {
            // Register
            _tracker.AddInstance(this);
        }

        private void OnDestroy()
        {
            _tracker.RemoveInstance(this);
        }
    }
}
