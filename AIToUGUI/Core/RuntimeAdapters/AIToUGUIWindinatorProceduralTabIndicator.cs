using System;
using System.Collections.Generic;
using PrimeTween;
using Riten.Windinator.Shapes;
using UnityEngine;

namespace AIToUGUI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGraphic))]
    public abstract class AIToUGUIWindinatorTabIndicatorBase : CanvasDrawer, IAIToUGUITabIndicator
    {
        [Header("Motion")]
        [SerializeField] protected Vector2 _baseSize = new Vector2(110f, 84f);
        [SerializeField] protected float _travelDuration = 0.34f;
        [SerializeField] protected float _settlePulse = 1f;
        [SerializeField] protected float _travelWidthBoost = 34f;
        [SerializeField] protected float _travelHeightCompress = 10f;
        [SerializeField] protected float _pulseWidthBoost = 10f;
        [SerializeField] protected float _pulseHeightBoost = 4f;

        protected struct IndicatorFrame
        {
            public bool IsValid;
            public bool IsTransition;
            public int ActiveIndex;
            public Vector2 HeadCenter;
            public Vector2 HeadSize;
            public Vector2 SourceCenter;
            public Vector2 SourceSize;
            public float SourcePresence;
            public Vector2 TargetCenter;
            public Vector2 TargetSize;
            public float TargetPresence;
            public Vector2 Direction;
            public float Pulse;
        }

        protected CanvasGraphic CanvasGraphicComponent => _canvasGraphic;
        protected RectTransform IndicatorRectTransform => _rectTransform;
        protected float PulseValue => _pulse;

        protected CanvasGraphic _canvasGraphic;
        protected RectTransform _rectTransform;
        protected RectTransform[] _slots = Array.Empty<RectTransform>();
        protected int _settledIndex = -1;
        protected int _fromIndex = -1;
        protected int _toIndex = -1;
        protected float _transitionT = 1f;
        protected float _pulse;

        private Tween _travelTween;
        private Tween _pulseTween;

        // 这层基类只负责“状态推进 + 帧参数生成”。
        // 真正画什么由子类决定，这样程序形状、Texture/Sprite 版可以共用同一套切换状态机。
        public void Configure(IReadOnlyList<RectTransform> slots, int initialIndex)
        {
            if (slots == null || slots.Count == 0)
            {
                _slots = Array.Empty<RectTransform>();
                _settledIndex = -1;
                _fromIndex = -1;
                _toIndex = -1;
                _transitionT = 1f;
                StopTweens();
                SetDirty();
                return;
            }

            _slots = new RectTransform[slots.Count];
            for (var i = 0; i < slots.Count; i++)
            {
                _slots[i] = slots[i];
            }

            CacheReferences();
            Select(initialIndex, true);
        }

        public void Select(int index, bool instant = false)
        {
            if (_slots == null || _slots.Length == 0)
            {
                return;
            }

            index = Mathf.Clamp(index, 0, _slots.Length - 1);
            CacheReferences();
            var isTransitioning = _transitionT < 0.999f && _fromIndex != _toIndex;

            if (_settledIndex < 0 || instant)
            {
                StopTweens();
                _settledIndex = index;
                _fromIndex = index;
                _toIndex = index;
                _transitionT = 1f;
                _pulse = 0f;
                SetDirty();
                return;
            }

            if (isTransitioning)
            {
                // 过渡中重复点击当前起点/终点会让 gooey 反复重启，看起来像连播抖动。
                // 这里直接吞掉，保持一次切换只对应一次形变轨迹。
                if (index == _toIndex || index == _fromIndex)
                {
                    return;
                }
            }
            else if (index == _settledIndex)
            {
                PlayPulse();
                return;
            }

            var fromCenter = ResolveSlotCenter(_settledIndex);
            var toCenter = ResolveSlotCenter(index);
            if (ApproximatelyEqual(fromCenter, toCenter))
            {
                StopTweens();
                _settledIndex = index;
                _fromIndex = index;
                _toIndex = index;
                _transitionT = 1f;
                PlayPulse();
                SetDirty();
                return;
            }

            StopTween(ref _travelTween);
            _fromIndex = _settledIndex;
            _toIndex = index;
            _transitionT = 0f;
            _travelTween = Tween.Custom(
                this,
                0f,
                1f,
                _travelDuration,
                (target, value) => target.SetTransition(value),
                PrimeTween.Ease.OutCubic,
                useUnscaledTime: true);
            PlayPulse();
            SetDirty();
        }

        protected override sealed void Draw(CanvasGraphic canvas, Vector2 size)
        {
            CacheReferences();
            PrepareCanvas(canvas);

            var frame = BuildFrame();
            if (!frame.IsValid)
            {
                OnInvalidFrame(canvas);
                return;
            }

            RenderFrame(canvas, frame);
        }

        protected virtual void PrepareCanvas(CanvasGraphic canvas)
        {
        }

        protected virtual void OnInvalidFrame(CanvasGraphic canvas)
        {
        }

        protected abstract void RenderFrame(CanvasGraphic canvas, IndicatorFrame frame);

        protected static void EnsureNeutralCanvas(CanvasGraphic canvas)
        {
            if (canvas == null)
            {
                return;
            }

            if (canvas.UseFillTexture)
            {
                canvas.UseFillTexture = false;
            }

            if (canvas.UseFlowTexture)
            {
                canvas.UseFlowTexture = false;
            }

            if (canvas.OutlineSize > 0f)
            {
                canvas.SetOutline(Color.clear, 0f);
            }

            if (canvas.ShadowSize > 0f || canvas.ShadowBlur > 0f)
            {
                canvas.SetShadow(Color.clear, 0f, 0f);
            }
        }

        protected float ResolveDistanceSticky(float distance, float stickyStart, float breakDistance)
        {
            var sticky = 1f - Mathf.Clamp01((distance - stickyStart) / Mathf.Max(1f, breakDistance - stickyStart));
            return Mathf.SmoothStep(0f, 1f, sticky);
        }

        protected Vector2 ResolveSlotCenter(int index)
        {
            if (_rectTransform == null || _slots == null || index < 0 || index >= _slots.Length)
            {
                return Vector2.zero;
            }

            var slot = _slots[index];
            if (slot == null)
            {
                return Vector2.zero;
            }

            var worldCenter = slot.TransformPoint(slot.rect.center);
            var localCenter = _rectTransform.InverseTransformPoint(worldCenter);
            return localCenter;
        }

        protected static bool ApproximatelyEqual(Vector2 a, Vector2 b)
        {
            return Mathf.Abs(a.x - b.x) <= 0.01f &&
                   Mathf.Abs(a.y - b.y) <= 0.01f;
        }

        private void Awake()
        {
            CacheReferences();
            OnIndicatorAwake();
        }

        private void OnEnable()
        {
            CacheReferences();
            OnIndicatorEnabled();
            SetDirty();
        }

        private void OnDisable()
        {
            StopTweens();
            OnIndicatorDisabled();
        }

        private void OnDestroy()
        {
            StopTweens();
            OnIndicatorDestroyed();
        }

        private void OnValidate()
        {
            CacheReferences();
            SetDirty();
        }

        private void OnRectTransformDimensionsChange()
        {
            SetDirty();
        }

        protected virtual void OnIndicatorAwake()
        {
        }

        protected virtual void OnIndicatorEnabled()
        {
        }

        protected virtual void OnIndicatorDisabled()
        {
        }

        protected virtual void OnIndicatorDestroyed()
        {
        }

        private void CacheReferences()
        {
            if (_rectTransform == null)
            {
                _rectTransform = GetComponent<RectTransform>();
            }

            if (_canvasGraphic == null)
            {
                _canvasGraphic = GetComponent<CanvasGraphic>();
            }
        }

        private IndicatorFrame BuildFrame()
        {
            if (_rectTransform == null || _slots == null || _slots.Length == 0)
            {
                return default;
            }

            var activeIndex = ResolveActiveIndex();
            if (activeIndex < 0)
            {
                return default;
            }

            if (_transitionT >= 0.999f || _fromIndex == _toIndex)
            {
                return new IndicatorFrame
                {
                    IsValid = true,
                    ActiveIndex = activeIndex,
                    HeadCenter = ResolveSlotCenter(activeIndex),
                    HeadSize = _baseSize + new Vector2(_pulse * _pulseWidthBoost, _pulse * _pulseHeightBoost),
                    Pulse = _pulse
                };
            }

            var fromCenter = ResolveSlotCenter(_fromIndex);
            var toCenter = ResolveSlotCenter(_toIndex);
            var direction = (toCenter - fromCenter).normalized;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector2.right;
            }

            var travelWave = Mathf.Sin(_transitionT * Mathf.PI);
            // frame 拆成 source/head/target 三段，不是为了视觉分层，而是为了给 gooey 提供可控的 union 输入。
            // source/target presence 让拖尾与目标前伸在时间轴上错开，避免整个 blob 同时等比缩放导致“硬切”感。
            var sourcePresence = Mathf.Clamp01(1f - _transitionT / 0.44f);
            sourcePresence = Mathf.SmoothStep(0f, 1f, sourcePresence);
            var targetPresence = Mathf.Clamp01((_transitionT - 0.56f) / 0.28f);
            targetPresence = Mathf.SmoothStep(0f, 1f, targetPresence);

            return new IndicatorFrame
            {
                IsValid = true,
                IsTransition = true,
                ActiveIndex = activeIndex,
                HeadCenter = Vector2.LerpUnclamped(fromCenter, toCenter, _transitionT),
                HeadSize = new Vector2(
                    _baseSize.x + travelWave * _travelWidthBoost + _pulse * (_pulseWidthBoost * 0.8f),
                    _baseSize.y - travelWave * _travelHeightCompress + _pulse * _pulseHeightBoost),
                SourceCenter = fromCenter,
                SourceSize = Vector2.Lerp(_baseSize * 0.48f, _baseSize, sourcePresence),
                SourcePresence = sourcePresence,
                TargetCenter = toCenter,
                TargetSize = Vector2.Lerp(_baseSize * 0.52f, _baseSize, targetPresence),
                TargetPresence = targetPresence,
                Direction = direction,
                Pulse = _pulse
            };
        }

        private void SetTransition(float value)
        {
            _transitionT = Mathf.Clamp01(value);
            if (_transitionT >= 0.999f)
            {
                _transitionT = 1f;
                _settledIndex = _toIndex;
            }

            SetDirty();
        }

        private void PlayPulse()
        {
            StopTween(ref _pulseTween);
            _pulse = _settlePulse;
            // pulse 独立于 travel tween，选中态重复点击时只触发轻量反馈，不重启整个位移动画。
            _pulseTween = Tween.Custom(
                this,
                _settlePulse,
                0f,
                0.24f,
                (target, value) => target.SetPulse(value),
                PrimeTween.Ease.OutCubic,
                useUnscaledTime: true);
        }

        private void SetPulse(float value)
        {
            _pulse = Mathf.Max(0f, value);
            SetDirty();
        }

        private int ResolveActiveIndex()
        {
            if (_transitionT < 0.999f && _toIndex >= 0 && _toIndex < _slots.Length)
            {
                return _toIndex;
            }

            if (_settledIndex >= 0 && _settledIndex < _slots.Length)
            {
                return _settledIndex;
            }

            return -1;
        }

        private void StopTweens()
        {
            StopTween(ref _travelTween);
            StopTween(ref _pulseTween);
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

    public class AIToUGUIWindinatorProceduralTabIndicator : AIToUGUIWindinatorTabIndicatorBase
    {
        [Header("Procedural Shape")]
        [SerializeField] private float _roundness = 38f;
        [SerializeField] private float _blend = 32f;
        [SerializeField] private float _bridgeBreakDistance = 148f;

        protected override void PrepareCanvas(CanvasGraphic canvas)
        {
            if (canvas == null)
            {
                return;
            }

            if (canvas.UseFillTexture)
            {
                canvas.UseFillTexture = false;
            }

            if (canvas.UseFlowTexture)
            {
                canvas.UseFlowTexture = false;
            }
        }

        protected override void RenderFrame(CanvasGraphic canvas, IndicatorFrame frame)
        {
            if (!frame.IsTransition)
            {
                DrawBody(canvas, frame.HeadCenter, frame.HeadSize, _blend * 1.16f);
                return;
            }

            if (frame.SourcePresence > 0.02f)
            {
                DrawBody(
                    canvas,
                    frame.SourceCenter,
                    frame.SourceSize,
                    Mathf.Lerp(_blend * 0.65f, _blend * 1.08f, frame.SourcePresence));

                var sticky = frame.SourcePresence * ResolveDistanceSticky(
                    Vector2.Distance(frame.SourceCenter, frame.HeadCenter),
                    Mathf.Max(_baseSize.x * 0.26f, 18f),
                    _bridgeBreakDistance);
                if (sticky > 0.02f)
                {
                    DrawBridge(canvas, frame.SourceCenter, frame.HeadCenter, frame.SourceSize, frame.HeadSize, sticky, frame.Direction);
                }
            }

            DrawBody(canvas, frame.HeadCenter, frame.HeadSize, _blend * 1.08f);

            if (frame.TargetPresence > 0.02f)
            {
                DrawBody(
                    canvas,
                    frame.TargetCenter,
                    frame.TargetSize,
                    Mathf.Lerp(_blend * 0.72f, _blend * 1.14f, frame.TargetPresence));

                var sticky = frame.TargetPresence * ResolveDistanceSticky(
                    Vector2.Distance(frame.HeadCenter, frame.TargetCenter),
                    Mathf.Max(_baseSize.x * 0.24f, 16f),
                    _bridgeBreakDistance);
                if (sticky > 0.02f)
                {
                    DrawBridge(canvas, frame.HeadCenter, frame.TargetCenter, frame.HeadSize, frame.TargetSize, sticky, frame.Direction);
                }
            }
        }

        private void DrawBody(CanvasGraphic canvas, Vector2 center, Vector2 size, float blend)
        {
            var roundness = Mathf.Min(size.y * 0.5f, _roundness + PulseValue * 4f);
            canvas.RectBrush.Draw(center, size * 0.5f, Vector4.one * roundness, blend);
        }

        private void DrawBridge(
            CanvasGraphic canvas,
            Vector2 startCenter,
            Vector2 endCenter,
            Vector2 startSize,
            Vector2 endSize,
            float sticky,
            Vector2 direction)
        {
            var axis = endCenter - startCenter;
            var distance = axis.magnitude;
            if (distance <= 0.001f)
            {
                return;
            }

            // bridge 不是一根矩形条，而是若干圆形 stamp 做 union。
            // 这样近距离切换时会更像“果冻牵连”，距离拉大后也能自然断开，不会出现硬边连接。
            var normal = new Vector2(-direction.y, direction.x);
            var segmentCount = Mathf.Clamp(Mathf.CeilToInt(distance / Mathf.Max(startSize.y * 0.28f, 12f)), 4, 12);
            var bridgeBlend = Mathf.Lerp(_blend * 0.52f, _blend * 1.2f, sticky);
            var neckRadius = Mathf.Lerp(8f, Mathf.Min(startSize.y, endSize.y) * 0.28f, sticky);
            var pulseBoost = PulseValue * 5f;

            for (var i = 1; i < segmentCount; i++)
            {
                var t = i / (float)segmentCount;
                var smoothT = Mathf.SmoothStep(0f, 1f, t);
                var center = Vector2.LerpUnclamped(startCenter, endCenter, smoothT);
                var arc = Mathf.Sin(smoothT * Mathf.PI) * Mathf.Lerp(0f, 10f, sticky + PulseValue * 0.3f);
                center += normal * arc * 0.06f;

                var thicknessProfile = 1f - Mathf.Abs(smoothT * 2f - 1f);
                var bodyRadius = Mathf.Lerp(startSize.y * 0.34f, endSize.y * 0.34f, smoothT);
                var radius = Mathf.Lerp(neckRadius, bodyRadius, Mathf.Pow(thicknessProfile, 0.60f));
                radius += Mathf.Sin(smoothT * Mathf.PI) * (sticky * 3.5f + pulseBoost);

                canvas.CircleBrush.Draw(center, Mathf.Max(5f, radius), bridgeBlend);
            }
        }
    }
}
