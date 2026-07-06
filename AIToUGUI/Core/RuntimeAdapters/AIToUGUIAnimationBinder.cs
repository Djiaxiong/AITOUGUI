using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AIToUGUI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class AIToUGUIAnimationBinder : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private AIToUGUIMotionType _enterMotion = AIToUGUIMotionType.Fade;
        [SerializeField] private AIToUGUIMotionType _hoverMotion = AIToUGUIMotionType.HoverLift;
        [SerializeField] private AIToUGUIMotionType _pressMotion = AIToUGUIMotionType.ScaleIn;
        [SerializeField] private float _duration = 0.22f;
        [SerializeField] private float _distance = 26f;
        [SerializeField] private float _scale = 0.96f;
        [SerializeField] private Ease _ease = Ease.OutCubic;
        [SerializeField] private bool _playEnterOnEnable = true;
        [SerializeField] private bool _listenToPointerEvents = true;
        [SerializeField] private float _hoverScaleMultiplier = 1.04f;
        [SerializeField] private float _hoverLiftRatio = 0.18f;
        [SerializeField] private float _enterScaleMultiplier = 0.985f;

        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;
        private Selectable _selectable;
        private Vector2 _restAnchoredPosition;
        private Vector3 _restLocalPosition;
        private Vector3 _restScale;
        private bool _hasRestState;
        private bool _isHovered;
        private bool _isPressed;
        private bool _enterQueued;
        private int _enterQueuedFrame = -1;
        private Tween _alphaTween;
        private Tween _positionTween;
        private Tween _scaleTween;

        public bool RequiresRaycastTarget
        {
            get
            {
                EnsureReferences();
                return _listenToPointerEvents &&
                       (_hoverMotion != AIToUGUIMotionType.None ||
                        _pressMotion != AIToUGUIMotionType.None ||
                        _selectable != null ||
                        GetComponent<BaseElement>() != null);
            }
        }

        public void ApplyPreset(AIToUGUIMotionPreset preset)
        {
            if (preset == null)
            {
                return;
            }

            _enterMotion = preset.enterMotion;
            _hoverMotion = preset.hoverMotion;
            _pressMotion = preset.pressMotion;
            _duration = Mathf.Max(0f, preset.duration);
            _distance = Mathf.Max(0f, preset.distance);
            _scale = Mathf.Clamp(preset.scale, 0.5f, 1.2f);
            _ease = preset.ease;
        }

        public void SetListenToPointerEvents(bool value)
        {
            _listenToPointerEvents = value;
        }

        public void SetHoverScaleMultiplier(float value)
        {
            _hoverScaleMultiplier = Mathf.Max(1f, value);
        }

        public void SetPressScaleMultiplier(float value)
        {
            _scale = Mathf.Clamp(value, 0.5f, 1.2f);
        }

        public void ApplyRecommendedButtonFeedback()
        {
            if (_hoverMotion == AIToUGUIMotionType.None)
            {
                _hoverMotion = AIToUGUIMotionType.HoverLift;
            }

            if (_pressMotion == AIToUGUIMotionType.None)
            {
                _pressMotion = AIToUGUIMotionType.ScaleIn;
            }

            _duration = Mathf.Max(0.2f, _duration);
            _distance = Mathf.Max(14f, _distance);
            _scale = Mathf.Min(_scale, 0.92f);
            _hoverScaleMultiplier = Mathf.Max(_hoverScaleMultiplier, 1.075f);
            _hoverLiftRatio = Mathf.Max(_hoverLiftRatio, 0.42f);
            _enterScaleMultiplier = Mathf.Min(_enterScaleMultiplier, 0.972f);
        }

        private void Awake()
        {
            EnsureReferences();
            UpgradeLegacyFeedbackIfNeeded();
            CaptureRestState();
        }

        private void OnEnable()
        {
            EnsureReferences();
            UpgradeLegacyFeedbackIfNeeded();
            AIToUGUIInteractionUtility.EnsureInteractionEnvironment(this);
            CaptureRestState();
            StopTweens();
            _isHovered = false;
            _isPressed = false;
            ResetToRestState();
            if (_playEnterOnEnable)
            {
                QueueEnter();
            }
        }

        private void OnDisable()
        {
            _enterQueued = false;
            _enterQueuedFrame = -1;
            StopTweens();
            _isHovered = false;
            _isPressed = false;
            ResetToRestState();
        }

        private void LateUpdate()
        {
            if (_enterQueued && Time.frameCount > _enterQueuedFrame)
            {
                _enterQueued = false;
                _enterQueuedFrame = -1;
                PlayEnterNow();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_listenToPointerEvents)
            {
                return;
            }

            HandlePointerEnter();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_listenToPointerEvents)
            {
                return;
            }

            HandlePointerExit();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_listenToPointerEvents)
            {
                return;
            }

            HandlePointerDown();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_listenToPointerEvents)
            {
                return;
            }

            HandlePointerUp();
        }

        public void HandlePointerEnter()
        {
            _isHovered = true;
            ApplyInteractionState(_duration, Ease.OutBack);
        }

        public void HandlePointerExit()
        {
            _isHovered = false;
            _isPressed = false;
            ApplyInteractionState(_duration * 0.75f, Ease.OutCubic);
        }

        public void HandlePointerDown()
        {
            _isPressed = true;
            ApplyInteractionState(_duration * 0.55f, Ease.OutQuad);
        }

        public void HandlePointerUp()
        {
            _isPressed = false;
            ApplyInteractionState(_duration * 0.7f, _isHovered ? Ease.OutBack : Ease.OutCubic);
        }

        public void PlayEnter()
        {
            QueueEnter();
        }

        private void QueueEnter()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            _enterQueued = true;
            _enterQueuedFrame = Time.frameCount;
        }

        private void PlayEnterNow()
        {
            EnsureReferences();
            if (!_hasRestState || (!_isHovered && !_isPressed))
            {
                CaptureRestState();
            }
            StopTweens();
            ResetToRestState();

            switch (_enterMotion)
            {
                case AIToUGUIMotionType.Fade:
                    _canvasGroup.alpha = 0f;
                    TweenAlpha(1f, _duration, _ease);
                    break;
                case AIToUGUIMotionType.SlideUp:
                    PlaySlide(Vector2.down * _distance);
                    break;
                case AIToUGUIMotionType.SlideDown:
                    PlaySlide(Vector2.up * _distance);
                    break;
                case AIToUGUIMotionType.SlideLeft:
                    PlaySlide(Vector2.right * _distance);
                    break;
                case AIToUGUIMotionType.SlideRight:
                    PlaySlide(Vector2.left * _distance);
                    break;
                case AIToUGUIMotionType.ScaleIn:
                    _canvasGroup.alpha = 0f;
                    _rectTransform.localScale = _restScale * _enterScaleMultiplier;
                    TweenAlpha(1f, _duration, _ease);
                    TweenScale(_restScale, _duration, _ease);
                    break;
                default:
                    ResetToRestState();
                    break;
            }
        }

        private void PlaySlide(Vector2 offset)
        {
            _canvasGroup.alpha = 0f;
            ApplyPositionImmediate(offset);
            _rectTransform.localScale = _restScale * _enterScaleMultiplier;
            TweenAlpha(1f, _duration, _ease);
            TweenPosition(Vector2.zero, _duration, _ease);
            TweenScale(_restScale, _duration, _ease);
        }

        private void ApplyInteractionState(float duration)
        {
            ApplyInteractionState(duration, _ease);
        }

        private void ApplyInteractionState(float duration, Ease ease)
        {
            EnsureReferences();

            var targetOffset = Vector2.zero;
            var targetScale = _restScale;

            if (_isHovered)
            {
                switch (_hoverMotion)
                {
                    case AIToUGUIMotionType.HoverLift:
                        targetOffset += Vector2.up * (_distance * _hoverLiftRatio);
                        targetScale = _restScale * _hoverScaleMultiplier;
                        break;
                    case AIToUGUIMotionType.Pulse:
                        targetScale = _restScale * 1.055f;
                        break;
                }
            }

            if (_isPressed && _pressMotion == AIToUGUIMotionType.ScaleIn)
            {
                targetScale = ResolvePressedScale();
                if (_isHovered && _hoverMotion == AIToUGUIMotionType.HoverLift)
                {
                    targetOffset = Vector2.up * (_distance * _hoverLiftRatio * 0.18f);
                }
            }

            TweenPosition(targetOffset, duration, ease);
            TweenScale(targetScale, duration, ease);
        }

        private void EnsureReferences()
        {
            if (_rectTransform == null)
            {
                _rectTransform = GetComponent<RectTransform>();
            }

            if (_selectable == null)
            {
                _selectable = GetComponent<Selectable>();
            }

            if (_selectable != null)
            {
                _selectable.transition = Selectable.Transition.None;
            }

            if ((_selectable != null || GetComponent<BaseElement>() != null) && TryGetComponent<Graphic>(out var graphic))
            {
                graphic.raycastTarget = true;
            }

            if (!TryGetComponent(out _canvasGroup))
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
        }

        private void UpgradeLegacyFeedbackIfNeeded()
        {
            if (_selectable == null)
            {
                return;
            }

            if (_hoverMotion == AIToUGUIMotionType.None)
            {
                _hoverMotion = AIToUGUIMotionType.HoverLift;
            }

            if (_pressMotion == AIToUGUIMotionType.None)
            {
                _pressMotion = AIToUGUIMotionType.ScaleIn;
            }

            if (_hoverScaleMultiplier <= 1.041f)
            {
                _hoverScaleMultiplier = 1.075f;
            }

            if (_hoverMotion == AIToUGUIMotionType.HoverLift && _hoverLiftRatio <= 0.2f)
            {
                _hoverLiftRatio = 0.42f;
            }

            if (_pressMotion == AIToUGUIMotionType.ScaleIn && _scale >= 0.959f)
            {
                _scale = 0.92f;
            }

            if (_duration <= 0.18f)
            {
                _duration = 0.2f;
            }

            if (_enterScaleMultiplier >= 0.984f)
            {
                _enterScaleMultiplier = 0.972f;
            }
        }

        private void CaptureRestState()
        {
            if (_rectTransform == null)
            {
                return;
            }

            _restAnchoredPosition = _rectTransform.anchoredPosition;
            _restLocalPosition = _rectTransform.localPosition;
            _restScale = _rectTransform.localScale;
            _hasRestState = true;
        }

        private void ResetToRestState()
        {
            if (_rectTransform == null || !_hasRestState)
            {
                return;
            }

            _rectTransform.anchoredPosition = _restAnchoredPosition;
            _rectTransform.localPosition = _restLocalPosition;
            _rectTransform.localScale = _restScale;

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
            }
        }

        private void TweenAlpha(float targetValue, float duration, Ease ease)
        {
            StopTween(ref _alphaTween);
            if (_canvasGroup == null)
            {
                return;
            }

            if (duration <= 0f)
            {
                _canvasGroup.alpha = targetValue;
                return;
            }

            if (Mathf.Abs(_canvasGroup.alpha - targetValue) <= 0.001f)
            {
                _canvasGroup.alpha = targetValue;
                return;
            }

            _alphaTween = Tween.Alpha(
                _canvasGroup,
                targetValue,
                duration,
                AIToUGUIEaseUtility.ToPrimeEasing(ease),
                useUnscaledTime: true);
        }

        private void ApplyPositionImmediate(Vector2 offsetFromRest)
        {
            if (_rectTransform == null)
            {
                return;
            }

            if (ShouldAnimateWithLocalPosition())
            {
                _rectTransform.localPosition = _restLocalPosition + new Vector3(offsetFromRest.x, offsetFromRest.y, 0f);
                return;
            }

            _rectTransform.anchoredPosition = _restAnchoredPosition + offsetFromRest;
        }

        private void TweenPosition(Vector2 offsetFromRest, float duration, Ease ease)
        {
            StopTween(ref _positionTween);
            if (_rectTransform == null)
            {
                return;
            }

            var primeEase = AIToUGUIEaseUtility.ToPrimeEasing(ease);
            if (duration <= 0f)
            {
                ApplyPositionImmediate(offsetFromRest);
                return;
            }

            if (ShouldAnimateWithLocalPosition())
            {
                var targetValue = _restLocalPosition + new Vector3(offsetFromRest.x, offsetFromRest.y, 0f);
                if (ApproximatelyEqual(_rectTransform.localPosition, targetValue))
                {
                    _rectTransform.localPosition = targetValue;
                    return;
                }

                _positionTween = Tween.LocalPosition(
                    _rectTransform,
                    targetValue,
                    duration,
                    primeEase,
                    useUnscaledTime: true);
                return;
            }

            var anchoredTarget = _restAnchoredPosition + offsetFromRest;
            if (ApproximatelyEqual(_rectTransform.anchoredPosition, anchoredTarget))
            {
                _rectTransform.anchoredPosition = anchoredTarget;
                return;
            }

            _positionTween = Tween.UIAnchoredPosition(
                _rectTransform,
                anchoredTarget,
                duration,
                primeEase,
                useUnscaledTime: true);
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

        private Vector3 ResolvePressedScale()
        {
            var xMultiplier = Mathf.Lerp(_scale, 1f, 0.28f);
            return new Vector3(
                _restScale.x * xMultiplier,
                _restScale.y * _scale,
                _restScale.z);
        }

        private void StopTweens()
        {
            StopTween(ref _alphaTween);
            StopTween(ref _positionTween);
            StopTween(ref _scaleTween);
        }

        private bool ShouldAnimateWithLocalPosition()
        {
            if (_rectTransform == null)
            {
                return false;
            }

            return !Mathf.Approximately(_rectTransform.anchorMin.x, _rectTransform.anchorMax.x) ||
                   !Mathf.Approximately(_rectTransform.anchorMin.y, _rectTransform.anchorMax.y);
        }

        private static bool ApproximatelyEqual(Vector2 a, Vector2 b)
        {
            return Mathf.Abs(a.x - b.x) <= 0.01f && Mathf.Abs(a.y - b.y) <= 0.01f;
        }

        private static bool ApproximatelyEqual(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) <= 0.01f &&
                   Mathf.Abs(a.y - b.y) <= 0.01f &&
                   Mathf.Abs(a.z - b.z) <= 0.01f;
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
