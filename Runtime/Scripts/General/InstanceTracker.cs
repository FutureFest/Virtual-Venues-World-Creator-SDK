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
        }
    }
}
