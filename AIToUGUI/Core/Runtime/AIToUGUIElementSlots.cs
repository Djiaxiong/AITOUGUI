using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIToUGUI
{
    [Serializable]
    public sealed class AIToUGUIElementSlotBinding
    {
        public string slotId;
        public RectTransform target;
        public Component primaryComponent;
    }

    [DisallowMultipleComponent]
    public sealed class AIToUGUIElementSlots : MonoBehaviour
    {
        [SerializeField]
        private List<AIToUGUIElementSlotBinding> _slots = new List<AIToUGUIElementSlotBinding>();

        public IReadOnlyList<AIToUGUIElementSlotBinding> Slots => _slots;

        public void SetSlot(string slotId, RectTransform target, Component primaryComponent = null)
        {
            if (string.IsNullOrWhiteSpace(slotId))
            {
                return;
            }

            for (var i = 0; i < _slots.Count; i++)
            {
                var binding = _slots[i];
                if (binding == null || !string.Equals(binding.slotId, slotId, StringComparison.Ordinal))
                {
                    continue;
                }

                binding.target = target;
                binding.primaryComponent = primaryComponent;
                return;
            }

            _slots.Add(new AIToUGUIElementSlotBinding
            {
                slotId = slotId,
                target = target,
                primaryComponent = primaryComponent
            });
        }

        public RectTransform GetSlotTransform(string slotId)
        {
            return TryGetSlot(slotId, out var binding) ? binding.target : null;
        }

        public Component GetPrimaryComponent(string slotId)
        {
            return TryGetSlot(slotId, out var binding) ? binding.primaryComponent : null;
        }

        public T GetPrimaryComponent<T>(string slotId) where T : Component
        {
            if (!TryGetSlot(slotId, out var binding))
            {
                return null;
            }

            if (binding.primaryComponent is T typed)
            {
                return typed;
            }

            return binding.target != null ? binding.target.GetComponent<T>() : null;
        }

        private bool TryGetSlot(string slotId, out AIToUGUIElementSlotBinding binding)
        {
            binding = null;
            if (string.IsNullOrWhiteSpace(slotId))
            {
                return false;
            }

            for (var i = 0; i < _slots.Count; i++)
            {
                var candidate = _slots[i];
                if (candidate != null && string.Equals(candidate.slotId, slotId, StringComparison.Ordinal))
                {
                    binding = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
