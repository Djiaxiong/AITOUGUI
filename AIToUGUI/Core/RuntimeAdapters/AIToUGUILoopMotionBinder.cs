using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AIToUGUI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class AIToUGUILoopMotionBinder : MonoBehaviour
    {
        [SerializeField] private AIToUGUILoopMotionType _loopType = AIToUGUILoopMotionType.None;
        [SerializeField] private float _duration = 12f;
        [SerializeField] private float _amplitude = 1f;
        [SerializeField] private Ease _ease = Ease.Linear;
        [SerializeField] private float _phaseOffset;

        private RectTransform _rectTransform;
        private Vector2 _restAnchoredPosition;
        private Vector3 _restScale = Vector3.one;
        private float _restRotationZ;
        private bool _hasRestState;

        public void Configure(AIToUGUILoopMotionPreset preset, float phaseOffset = 0f)
        {
            if (preset == null)
            {
                _loopType = AIToUGUILoopMotionType.None;
                _duration = 0f;
                _amplitude = 0f;
                _ease = Ease.Linear;
                _phaseOffset = 0f;
                ResetToRestState();
                return;
            }

            _loopType = preset.loopType;
            _duration = Mathf.Max(0.01f, preset.duration);
            _amplitude = preset.amplitude;
            _ease = preset.ease;
            _phaseOffset = phaseOffset;
            CaptureRestState();
            ApplyCurrentState(GetEditorAwareTime());
        }

        public void RebaseRestState()
        {
            _hasRestState = false;
            CaptureRestState();
            ApplyCurrentState(GetEditorAwareTime());
        }

        private void Awake()
        {
            CaptureRestState();
        }

        private void OnEnable()
        {
            CaptureRestState();
            ApplyCurrentState(GetEditorAwareTime());
        }

        private void OnDisable()
        {
            ResetToRestState();
        }

        private void Update()
        {
            if (_loopType == AIToUGUILoopMotionType.None)
            {
                return;
            }

            CaptureRestState();
            ApplyCurrentState(GetEditorAwareTime());
        }

        private void OnValidate()
        {
            _duration = Mathf.Max(0.01f, _duration);
            CaptureRestState();
            ApplyCurrentState(GetEditorAwareTime());
        }

        private void CaptureRestState()
        {
            var rect = CachedRectTransform;
            if (rect == null)
            {
                return;
            }

            if (!_hasRestState)
            {
                _restAnchoredPosition = rect.anchoredPosition;
                _restScale = rect.localScale;
                _restRotationZ = rect.localEulerAngles.z;
                _hasRestState = true;
            }
        }

        private void ResetToRestState()
        {
            var rect = CachedRectTransform;
            if (!_hasRestState || rect == null)
            {
                return;
            }

            rect.anchoredPosition = _restAnchoredPosition;
            rect.localScale = _restScale;
            var rotation = rect.localEulerAngles;
            rotation.z = _restRotationZ;
            rect.localEulerAngles = rotation;
        }

        private void ApplyCurrentState(float time)
        {
            var rect = CachedRectTransform;
            if (rect == null)
            {
                return;
            }

            if (!_hasRestState)
            {
                CaptureRestState();
            }

            var duration = Mathf.Max(0.01f, _duration);
            var cycle = Mathf.Repeat(time - _phaseOffset, duration) / duration;
            var easedLoop = EvaluateLoopWave(cycle, _ease);

            rect.anchoredPosition = _restAnchoredPosition;
            rect.localScale = _restScale;
            var rotation = rect.localEulerAngles;
            rotation.z = _restRotationZ;

            switch (_loopType)
            {
                case AIToUGUILoopMotionType.Rotate:
                    rotation.z = _restRotationZ + cycle * 360f * Mathf.Max(0f, _amplitude);
                    break;
                case AIToUGUILoopMotionType.RotateReverse:
                    rotation.z = _restRotationZ - cycle * 360f * Mathf.Max(0f, _amplitude);
                    break;
                case AIToUGUILoopMotionType.Float:
                    rect.anchoredPosition = _restAnchoredPosition + Vector2.up * (_amplitude * easedLoop);
                    break;
                case AIToUGUILoopMotionType.Pulse:
                    rect.localScale = _restScale * (1f + _amplitude * easedLoop);
                    break;
            }

            rect.localEulerAngles = rotation;
        }

        private static float EvaluateLoopWave(float t, Ease ease)
        {
            var pingPong = 0.5f * (Mathf.Sin((t * Mathf.PI * 2f) - Mathf.PI * 0.5f) + 1f);
            var eased = AIToUGUIEaseUtility.Evaluate(pingPong, ease);
            return eased * 2f - 1f;
        }

        private float GetEditorAwareTime()
        {
            if (Application.isPlaying)
            {
                return Time.unscaledTime;
            }

#if UNITY_EDITOR
            return (float)EditorApplication.timeSinceStartup;
#else
            return 0f;
#endif
        }

        private RectTransform CachedRectTransform
        {
            get
            {
                if (_rectTransform == null)
                {
                    _rectTransform = GetComponent<RectTransform>();
                }

                return _rectTransform;
            }
        }
    }
}
