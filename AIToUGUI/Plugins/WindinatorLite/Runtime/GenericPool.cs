using System;
using System.Collections.Generic;

namespace Riten.Windinator
{
    /// <summary>
    /// Minimal pool extracted for WindinatorLite shape batching.
    /// Keep this file independent from the original Windinator runtime.
    /// </summary>
    public sealed class GenericPool<T>
    {
        readonly Queue<T> _instances = new Queue<T>();
        readonly List<T> _active = new List<T>();
        readonly Func<T> _constructor;

        public GenericPool(Func<T> constructor)
        {
            _constructor = constructor ?? throw new ArgumentNullException(nameof(constructor));
        }

        public void Free(T instance)
        {
            _active.Remove(instance);
            _instances.Enqueue(instance);
        }

        public T Allocate()
        {
            if (_instances.Count > 0)
            {
                var reused = _instances.Dequeue();
                _active.Add(reused);
                return reused;
            }

            var created = _constructor();
            _active.Add(created);
            return created;
        }

        public void Clear()
        {
            _instances.Clear();
            _active.Clear();
        }
    }
}
