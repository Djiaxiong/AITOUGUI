using UnityEngine;
using UnityEngine.UI;

namespace AIToUGUI
{
    // 测试场景驱动脚本：把底部 Tab 按钮接到程序化果冻指示器上。
    // 指示器自身只按状态作画，必须由这里调用 Configure/Select 才会推进切换动画。
    [DisallowMultipleComponent]
    public class TabIndicatorTestController : MonoBehaviour
    {
        [SerializeField] private AIToUGUIWindinatorTabIndicatorBase _indicatorComponent;
        [SerializeField] private RectTransform[] _slotRects;
        [SerializeField] private Button[] _slotButtons;
        [SerializeField] private Text[] _iconTexts;
        [SerializeField] private Text[] _labelTexts;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _bodyText;
        [SerializeField] private int _initialIndex;

        private static readonly Color ActiveIcon = new Color(0f, 0f, 0f, 1f);
        private static readonly Color InactiveIcon = new Color(0.88f, 0.92f, 0.98f, 0.55f);
        private static readonly Color ActiveLabel = new Color(0f, 0f, 0f, 1f);
        private static readonly Color InactiveLabel = new Color(0.8f, 0.86f, 0.94f, 0.45f);

        private int _activeIndex = -1;

        private void Start()
        {
            if (_indicatorComponent == null || _slotRects == null || _slotRects.Length == 0)
            {
                return;
            }

            _indicatorComponent.Configure(_slotRects, _initialIndex);
            BindButtons();
            Apply(_initialIndex, true);
        }

        private void BindButtons()
        {
            if (_slotButtons == null)
            {
                return;
            }

            for (var i = 0; i < _slotButtons.Length; i++)
            {
                var button = _slotButtons[i];
                if (button == null)
                {
                    continue;
                }

                var index = i;
                button.onClick.AddListener(() => Apply(index, false));
            }
        }

        private void Apply(int index, bool instant)
        {
            if (_slotRects == null || index < 0 || index >= _slotRects.Length)
            {
                return;
            }

            if (_indicatorComponent != null)
            {
                _indicatorComponent.Select(index, instant);
            }

            _activeIndex = index;
            RefreshHighlight();
            RefreshPreview(index);
        }

        private void RefreshHighlight()
        {
            if (_iconTexts != null)
            {
                for (var i = 0; i < _iconTexts.Length; i++)
                {
                    if (_iconTexts[i] != null)
                    {
                        _iconTexts[i].color = i == _activeIndex ? ActiveIcon : InactiveIcon;
                    }
                }
            }

            if (_labelTexts == null)
            {
                return;
            }

            for (var i = 0; i < _labelTexts.Length; i++)
            {
                if (_labelTexts[i] != null)
                {
                    _labelTexts[i].color = i == _activeIndex ? ActiveLabel : InactiveLabel;
                }
            }
        }

        private void RefreshPreview(int index)
        {
            var label = _labelTexts != null && index >= 0 && index < _labelTexts.Length && _labelTexts[index] != null
                ? _labelTexts[index].text
                : null;

            if (_titleText != null && !string.IsNullOrEmpty(label))
            {
                _titleText.text = label;
            }

            if (_bodyText != null && !string.IsNullOrEmpty(label))
            {
                _bodyText.text = $"{label} tab selected. Tap another tab to watch the gooey indicator travel and stretch.";
            }
        }
    }
}
