using System.Collections.Generic;
using UnityEngine;

namespace AIToUGUI
{
    public interface IAIToUGUITabIndicator
    {
        void Configure(IReadOnlyList<RectTransform> slots, int initialIndex);
        void Select(int index, bool instant = false);
    }
}
