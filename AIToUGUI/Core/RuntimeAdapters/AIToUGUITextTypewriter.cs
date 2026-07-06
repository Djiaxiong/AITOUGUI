using PrimeTween;
using TMPro;
using UnityEngine;

namespace AIToUGUI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class AIToUGUITextTypewriter : MonoBehaviour
    {
        [SerializeField] private float _charactersPerSecond = 30f;
        [SerializeField] private float _startDelay = 0.18f;
        [SerializeField] private float _punctuationPause = 0.08f;
        [SerializeField] private bool _playOnEnable = true;

        private TMP_Text _text;
        private Tween _playTween;
        private bool _restartQueued;
        private int _restartQueuedFrame = -1;

        public void Configure(float charactersPerSecond, float startDelay, float punctuationPause)
        {
            _charactersPerSecond = Mathf.Max(1f, charactersPerSecond);
            _startDelay = Mathf.Max(0f, startDelay);
            _punctuationPause = Mathf.Max(0f, punctuationPause);
        }

        private void Awake()
        {
            EnsureReference();
            RevealImmediately();
        }

        private void OnEnable()
        {
            if (_playOnEnable)
            {
                QueueRestart();
            }
            else
            {
                RevealImmediately();
            }
        }

        private void LateUpdate()
        {
            if (_restartQueued && Time.frameCount > _restartQueuedFrame)
            {
                _restartQueued = false;
                _restartQueuedFrame = -1;
                RestartNow();
            }
        }

        private void OnDisable()
        {
            _restartQueued = false;
            _restartQueuedFrame = -1;
            StopPlayback();
            RevealImmediately();
        }

        public void Restart()
        {
            QueueRestart();
        }

        private void QueueRestart()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            _restartQueued = true;
            _restartQueuedFrame = Time.frameCount;
        }

        private void RestartNow()
        {
            EnsureReference();
            StopPlayback();

            if (_text == null || !isActiveAndEnabled)
            {
                RevealImmediately();
                return;
            }

            Canvas.ForceUpdateCanvases();
            _text.ForceMeshUpdate();
            var totalVisibleCharacters = _text.textInfo.characterCount;
            if (totalVisibleCharacters <= 0)
            {
                RevealImmediately();
                return;
            }

            _text.maxVisibleCharacters = 0;
            var duration = ResolveDuration(totalVisibleCharacters);
            if (duration <= 0f)
            {
                RevealImmediately();
                return;
            }

            var punctuationPauseUnits = ResolvePunctuationPauseUnits();
            if (punctuationPauseUnits <= 0)
            {
                _playTween = Tween.TextMaxVisibleCharacters(
                    _text,
                    0,
                    totalVisibleCharacters,
                    duration,
                    PrimeTween.Ease.Linear,
                    startDelay: _startDelay,
                    useUnscaledTime: true);
                return;
            }

            var remappedCount = ResolveRemappedCount(totalVisibleCharacters, punctuationPauseUnits);
            _playTween = Tween.Custom(
                this,
                0f,
                remappedCount,
                duration,
                (target, progress) => target.UpdateVisibleCharacters(progress),
                PrimeTween.Ease.Linear,
                startDelay: _startDelay,
                useUnscaledTime: true);
        }

        public void RevealImmediately()
        {
            EnsureReference();
            if (_text != null)
            {
                _text.maxVisibleCharacters = int.MaxValue;
            }
        }

        private void StopPlayback()
        {
            if (_playTween.isAlive)
            {
                _playTween.Stop();
            }

            _playTween = default;
        }

        private void EnsureReference()
        {
            if (_text == null)
            {
                _text = GetComponent<TMP_Text>();
            }
        }

        private float ResolveDuration(int totalVisibleCharacters)
        {
            var baseDuration = totalVisibleCharacters / Mathf.Max(1f, _charactersPerSecond);
            var pauseUnits = ResolvePunctuationPauseUnits();
            if (pauseUnits <= 0 || _text == null)
            {
                return baseDuration;
            }

            var remappedCount = ResolveRemappedCount(totalVisibleCharacters, pauseUnits);
            return remappedCount / Mathf.Max(1f, _charactersPerSecond);
        }

        private int ResolvePunctuationPauseUnits()
        {
            return Mathf.Max(0, Mathf.RoundToInt(_punctuationPause * Mathf.Max(1f, _charactersPerSecond)));
        }

        private int ResolveRemappedCount(int totalVisibleCharacters, int punctuationPauseUnits)
        {
            RemapVisibleCharacters(totalVisibleCharacters, punctuationPauseUnits, int.MaxValue, out var remappedCount, out _);
            return remappedCount;
        }

        private void UpdateVisibleCharacters(float progress)
        {
            if (_text == null)
            {
                return;
            }

            var punctuationPauseUnits = ResolvePunctuationPauseUnits();
            RemapVisibleCharacters(
                _text.textInfo.characterCount,
                punctuationPauseUnits,
                Mathf.RoundToInt(progress),
                out _,
                out var visibleCharacters);

            if (_text.maxVisibleCharacters != visibleCharacters)
            {
                _text.maxVisibleCharacters = visibleCharacters;
            }
        }

        private void RemapVisibleCharacters(
            int totalVisibleCharacters,
            int punctuationPauseUnits,
            int remappedEndIndex,
            out int remappedCount,
            out int visibleCharacters)
        {
            remappedCount = 0;
            visibleCharacters = 0;
            if (_text == null || totalVisibleCharacters <= 0)
            {
                return;
            }

            var characterInfos = _text.textInfo.characterInfo;
            for (var i = 0; i < totalVisibleCharacters; i++)
            {
                if (remappedCount >= remappedEndIndex)
                {
                    break;
                }

                remappedCount++;
                visibleCharacters++;

                if (!IsPausePunctuation(characterInfos[i].character))
                {
                    continue;
                }

                var nextIndex = i + 1;
                if (nextIndex < totalVisibleCharacters && !IsPausePunctuation(characterInfos[nextIndex].character))
                {
                    remappedCount += punctuationPauseUnits;
                }
            }
        }

        private static bool IsPausePunctuation(char character)
        {
            return ".,!?;:".IndexOf(character) >= 0;
        }
    }
}
