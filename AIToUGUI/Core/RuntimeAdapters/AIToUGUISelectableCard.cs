using System;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AIToUGUI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class AIToUGUISelectableCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        private static readonly Dictionary<string, AIToUGUISelectableCard> SelectedByGroup = new Dictionary<string, AIToUGUISelectableCard>(StringComparer.Ordinal);

        [SerializeField] private string _groupId = "default";
        [SerializeField] private Color _accentColor = new Color(1f, 0.82f, 0.36f, 1f);
        [SerializeField] private float _hoverScale = 1.035f;
        [SerializeField] private float _selectedScale = 1.055f;
        [SerializeField] private float _pressScale = 0.985f;
        [SerializeField] private float _duration = 0.16f;
        [SerializeField] private float _hoverTintStrength = 0.10f;
        [SerializeField] private float _selectedTintStrength = 0.22f;
        [SerializeField] private float _selectedHoverScaleBoost = 1.02f;
        [SerializeField] private float _selectedHoverTintBoost = 0.08f;
        [SerializeField] private bool _startSelected;

        private RectTransform _rectTransform;
        private Graphic _graphic;
        private Selectable _selectable;
        private Vector3 _restScale;
        private Color _restColor = Color.white;
        private bool _isHovered;
        private bool _isPressed;
        private bool _isSelected;
        private Tween _scaleTween;
        private Tween _colorTween;

        public void Configure(string groupId, Color accentColor, bool startSelected)
        {
            _groupId = string.IsNullOrWhiteSpace(groupId) ? "default" : groupId.Trim();
            _accentColor = accentColor;
            _startSelected = startSelected;
        }

        public void ApplyRecommendedProfile()
        {
            _duration = Mathf.Max(0.18f, _duration);
            _hoverScale = Mathf.Max(_hoverScale, 1.065f);
            _selectedScale = Mathf.Max(_selectedScale, 1.09f);
            _pressScale = Mathf.Min(_pressScale, 0.94f);
            _hoverTintStrength = Mathf.Max(_hoverTintStrength, 0.18f);
            _selectedTintStrength = Mathf.Max(_selectedTintStrength, 0.38f);
            _selectedHoverScaleBoost = Mathf.Max(_selectedHoverScaleBoost, 1.02f);
            _selectedHoverTintBoost = Mathf.Max(_selectedHoverTintBoost, 0.08f);
        }

        private void Awake()
        {
            EnsureReferences();
            UpgradeLegacyProfileIfNeeded();
        }

        private void OnEnable()
        {
            EnsureReferences();
            UpgradeLegacyProfileIfNeeded();
            AIToUGUIInteractionUtility.EnsureInteractionEnvironment(this);
            _isHovered = false;
            _isPressed = false;
            _isSelected = false;
            ApplyVisualState(true);

            if (_startSelected)
            {
                Select(true);
            }
        }

        private void OnDisable()
        {
            StopTweens();

            if (!string.IsNullOrWhiteSpace(_groupId) &&
                SelectedByGroup.TryGetValue(_groupId, out var selected) &&
                selected == this)
            {
                SelectedByGroup.Remove(_groupId);
            }

            _isHovered = false;
            _isPressed = false;
            _isSelected = false;
            ApplyVisualState(true);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;
            _isPressed = false;
            ApplyVisualState(_duration, Ease.OutBack);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
            _isPressed = false;
            ApplyVisualState(_duration * 0.8f, Ease.OutCubic);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _isPressed = true;
            ApplyVisualState(_duration * 0.55f, Ease.OutQuad);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _isPressed = false;
            ApplyVisualState(_duration * 0.75f, (_isHovered || _isSelected) ? Ease.OutBack : Ease.OutCubic);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            Select(false);
        }

        public void Select(bool instant)
        {
            if (!string.IsNullOrWhiteSpace(_groupId) &&
                SelectedByGroup.TryGetValue(_groupId, out var previousSelected) &&
                previousSelected != null &&
                previousSelected != this)
            {
                previousSelected.SetSelected(false, instant);
            }

            if (!string.IsNullOrWhiteSpace(_groupId))
            {
                SelectedByGroup[_groupId] = this;
            }

            SetSelected(true, instant);
        }

        public void Deselect(bool instant = false)
        {
            if (!string.IsNullOrWhiteSpace(_groupId) &&
                SelectedByGroup.TryGetValue(_groupId, out var selected) &&
                selected == this)
            {
                SelectedByGroup.Remove(_groupId);
            }

            SetSelected(false, instant);
        }

        private void SetSelected(bool value, bool instant)
        {
            _isSelected = value;
            _isPressed = false;
            ApplyVisualState(instant ? 0f : _duration, value ? Ease.OutBack : Ease.OutCubic);
        }

        private void EnsureReferences()
        {
            if (_rectTransform == null)
            {
                _rectTransform = GetComponent<RectTransform>();
            }

            if (_graphic == null)
            {
                _graphic = GetComponent<Graphic>() ?? GetComponentInChildren<Graphic>(true);
            }

            if (_graphic != null)
            {
                _graphic.raycastTarget = true;
            }

            if (_selectable == null)
            {
                _selectable = GetComponent<Selectable>();
            }

            if (_selectable != null)
            {
                _selectable.transition = Selectable.Transition.None;
            }

            _restScale = _rectTransform != null ? _rectTransform.localScale : Vector3.one;
            if (_graphic != null)
            {
                _restColor = _graphic.color;
            }
        }

        private void UpgradeLegacyProfileIfNeeded()
        {
            if (_hoverScale <= 1.04f)
            {
                _hoverScale = 1.065f;
            }

            if (_selectedScale <= 1.06f)
            {
                _selectedScale = 1.09f;
            }

            if (_pressScale >= 0.98f)
            {
                _pressScale = 0.94f;
            }

            if (_duration <= 0.16f)
            {
                _duration = 0.18f;
            }

            if (_hoverTintStrength <= 0.11f)
            {
                _hoverTintStrength = 0.18f;
            }

            if (_selectedTintStrength <= 0.22f)
            {
                _selectedTintStrength = 0.38f;
            }

            if (_selectedHoverScaleBoost <= 1.001f)
            {
                _selectedHoverScaleBoost = 1.02f;
            }

            if (_selectedHoverTintBoost <= 0.001f)
            {
                _selectedHoverTintBoost = 0.08f;
            }
        }

        private void ApplyVisualState(bool instant)
        {
            ApplyVisualState(instant ? 0f : _duration, Ease.OutCubic);
        }

        private void ApplyVisualState()
        {
            ApplyVisualState(_duration, Ease.OutCubic);
        }

        private void ApplyVisualState(float duration, Ease ease)
        {
            TweenScale(ResolveTargetScale(), duration, ease);

            if (_graphic != null)
            {
                TweenColor(ResolveTargetColor(), duration, ease);
            }
        }

        private Vector3 ResolveTargetScale()
        {
            if (_isPressed)
            {
                return _restScale * (ResolveBaseScaleMultiplier() * _pressScale);
            }

            return _restScale * ResolveBaseScaleMultiplier();
        }

        private Color ResolveTargetColor()
        {
            var tintStrength = 0f;
            if (_isSelected)
            {
                tintStrength = _selectedTintStrength;
            }

            if (_isHovered)
            {
                tintStrength = Mathf.Max(tintStrength, _hoverTintStrength);
            }

            if (_isSelected && _isHovered)
            {
                tintStrength = Mathf.Clamp01(_selectedTintStrength + _selectedHoverTintBoost);
            }

            return tintStrength <= 0.001f
                ? _restColor
                : BlendColor(_restColor, _accentColor, tintStrength);
        }

        private float ResolveBaseScaleMultiplier()
        {
            if (_isSelected && _isHovered)
            {
                return Mathf.Max(_selectedScale, _hoverScale) * _selectedHoverScaleBoost;
            }

            if (_isSelected)
            {
                return _selectedScale;
            }

            if (_isHovered)
            {
                return _hoverScale;
            }

            return 1f;
        }

        private static Color BlendColor(Color baseColor, Color accentColor, float blend)
        {
            var color = Color.Lerp(baseColor, accentColor, Mathf.Clamp01(blend));
            color.a = baseColor.a;
            return color;
        }

        private void TweenScale(Vector3 targetValue, float duration, Ease ease)
        {
            StopTween(ref _scaleTween);
            if (_rectTransform == null)
            {
                return;
            }

            if (duration <= 0f)
            {
                _rectTransform.localScale = targetValue;
                return;
            }

            if (ApproximatelyEqual(_rectTransform.localScale, targetValue))
            {
                _rectTransform.localScale = targetValue;
                return;
            }

            _scaleTween = Tween.Scale(
                _rectTransform,
                targetValue,
                duration,
                AIToUGUIEaseUtility.ToPrimeEasing(ease),
                useUnscaledTime: true);
        }

        private void TweenColor(Color targetValue, float duration, Ease ease)
        {
            StopTween(ref _colorTween);
            if (_graphic == null)
            {
                return;
            }

            if (duration <= 0f)
            {
                _graphic.color = targetValue;
                return;
            }

            if (ApproximatelyEqual(_graphic.color, targetValue))
            {
                _graphic.color = targetValue;
                return;
            }

            _colorTween = Tween.Color(
                _graphic,
                targetValue,
                duration,
                AIToUGUIEaseUtility.ToPrimeEasing(ease),
                useUnscaledTime: true);
        }

        private void StopTweens()
        {
            StopTween(ref _scaleTween);
            StopTween(ref _colorTween);
        }

        private static bool ApproximatelyEqual(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) <= 0.01f &&
                   Mathf.Abs(a.y - b.y) <= 0.01f &&
                   Mathf.Abs(a.z - b.z) <= 0.01f;
        }

        private static bool ApproximatelyEqual(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) <= 0.001f &&
                   Mathf.Abs(a.g - b.g) <= 0.001f &&
                   Mathf.Abs(a.b - b.b) <= 0.001f &&
                   Mathf.Abs(a.a - b.a) <= 0.001f;
        }

        private static void StopTween(ref Tween tween)
        {
            if (tween.isAlive)
            {
                tween.Stop();
            }

            tween = default;
        }
    }
}
