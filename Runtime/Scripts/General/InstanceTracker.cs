using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    // Static management for runtime discovery
    public class InstanceTracker<T> where T : MonoBehaviour
    {
        private List<T> _instances = new List<T>();
        public List<T> Instances => _instances;
        public Action<T> onInstanceAdded = null;
        public Action<T> onInstanceRemoved = null;

        public void AddInstance(T instance)
        {
            if (_instances.Contains(instance))
            {
                return;
            }
            _instances.Add(instance);
            onInstanceAdded?.Invoke(instance);
        }

        public void RemoveInstance(T instance)
        {
            if (!_instances.Contains(instance))
            {
                return;
            }
            _instances.Remove(instance);
            onInstanceRemoved?.Invoke(instance);
        }

        /// <summary>
        /// Clears all instances and resets event subscriptions.
        /// Call this during domain reload or scene transitions.
        /// </summary>
        public void Clear()
        {
            _instances.Clear();
            onInstanceAdded = null;
            onInstanceRemoved = null;
        }

        /// <summary>
        /// Removes any null/destroyed instances from the list.
        /// Useful for cleaning up after scene unloads.
        /// </summary>
        public void PruneNullInstances()
        {
            _instances.RemoveAll(instance => instance == null);
        }
    }
}
