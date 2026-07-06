using Riten.Windinator.Shapes;
using UnityEngine;

namespace AIToUGUI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGraphic))]
    public abstract class AIToUGUIWindinatorImageTabIndicatorBase : AIToUGUIWindinatorTabIndicatorBase
    {
        [Header("Image Shape")]
        [SerializeField] protected Color _tint = Color.white;
        [SerializeField] protected SDFTextureScaleMode _scaleMode = SDFTextureScaleMode.Fit;
        [SerializeField] protected float _blend = 32f;
        [SerializeField] protected float _bridgeBreakDistance = 148f;
        [SerializeField] protected float _bridgeWidthBoost = 24f;
        [SerializeField] [Range(0.18f, 1f)] protected float _bridgeHeightRatio = 0.56f;
        [SerializeField] protected float _bridgeMinHeight = 16f;

        protected abstract bool HasSourceAsset { get; }

        protected abstract Texture2D ResolveStampSdfTexture();

        protected abstract void ApplyFill(CanvasGraphic canvas);

        protected override void PrepareCanvas(CanvasGraphic canvas)
        {
            if (canvas == null)
            {
                return;
            }

            EnsureNeutralCanvas(canvas);
            ClearLocalFillMapping(canvas);

            if (!HasSourceAsset)
            {
                canvas.UseFillTexture = false;
                return;
            }

            ApplyFill(canvas);
        }

        protected override void OnInvalidFrame(CanvasGraphic canvas)
        {
            ClearLocalFillMapping(canvas);
            if (canvas != null)
            {
                canvas.UseFillTexture = false;
            }
        }

        protected override void RenderFrame(CanvasGraphic canvas, IndicatorFrame frame)
        {
            if (canvas == null || !HasSourceAsset)
            {
                return;
            }

            var stampSdf = ResolveStampSdfTexture();
            if (stampSdf == null)
            {
                canvas.UseFillTexture = false;
                return;
            }

            canvas.UseFillTexture = true;
            var bounds = new BoundsAccumulator();

            if (!frame.IsTransition)
            {
                // 静止态只画一个 stamp，并把本地填充映射限制在当前 blob 包围盒内。
                // 否则图片会按整个容器采样，看起来像“底图被塞满面板”。
                DrawBody(canvas, stampSdf, frame.HeadCenter, frame.HeadSize, _blend * 1.16f);
                bounds.Encapsulate(frame.HeadCenter, frame.HeadSize);
                ApplyLocalFillMapping(canvas, bounds);
                return;
            }

            if (frame.SourcePresence > 0.02f)
            {
                DrawBody(
                    canvas,
                    stampSdf,
                    frame.SourceCenter,
                    frame.SourceSize,
                    Mathf.Lerp(_blend * 0.65f, _blend * 1.08f, frame.SourcePresence));
                bounds.Encapsulate(frame.SourceCenter, frame.SourceSize);

                var sticky = frame.SourcePresence * ResolveDistanceSticky(
                    Vector2.Distance(frame.SourceCenter, frame.HeadCenter),
                    Mathf.Max(_baseSize.x * 0.26f, 18f),
                    _bridgeBreakDistance);

                if (sticky > 0.02f)
                {
                    // 图片/Sprite 版的 gooey 仍然走真正的 SDF union。
                    // body 用纹理 stamp，bridge 用程序矩形补颈部，这样既保留素材轮廓，又能做细胞分裂式牵连。
                    DrawBridge(canvas, frame.SourceCenter, frame.HeadCenter, frame.SourceSize, frame.HeadSize, sticky);
                    bounds.EncapsulateBridge(frame.SourceCenter, frame.HeadCenter, frame.SourceSize, frame.HeadSize, sticky, _bridgeWidthBoost, _bridgeHeightRatio, _bridgeMinHeight, PulseValue, _pulseHeightBoost);
                }
            }

            DrawBody(canvas, stampSdf, frame.HeadCenter, frame.HeadSize, _blend * 1.08f);
            bounds.Encapsulate(frame.HeadCenter, frame.HeadSize);

            if (frame.TargetPresence > 0.02f)
            {
                DrawBody(
                    canvas,
                    stampSdf,
                    frame.TargetCenter,
                    frame.TargetSize,
                    Mathf.Lerp(_blend * 0.72f, _blend * 1.14f, frame.TargetPresence));
                bounds.Encapsulate(frame.TargetCenter, frame.TargetSize);

                var sticky = frame.TargetPresence * ResolveDistanceSticky(
                    Vector2.Distance(frame.HeadCenter, frame.TargetCenter),
                    Mathf.Max(_baseSize.x * 0.24f, 16f),
                    _bridgeBreakDistance);

                if (sticky > 0.02f)
                {
                    DrawBridge(canvas, frame.HeadCenter, frame.TargetCenter, frame.HeadSize, frame.TargetSize, sticky);
                    bounds.EncapsulateBridge(frame.HeadCenter, frame.TargetCenter, frame.HeadSize, frame.TargetSize, sticky, _bridgeWidthBoost, _bridgeHeightRatio, _bridgeMinHeight, PulseValue, _pulseHeightBoost);
                }
            }

            ApplyLocalFillMapping(canvas, bounds);
        }

        private void DrawBody(CanvasGraphic canvas, Texture2D stampSdf, Vector2 center, Vector2 size, float blend)
        {
            canvas.TextureSDFBrush.Draw(stampSdf, center, size, blend, 0f, DrawOperation.Union, null, _scaleMode);
        }

        private void DrawBridge(
            CanvasGraphic canvas,
            Vector2 startCenter,
            Vector2 endCenter,
            Vector2 startSize,
            Vector2 endSize,
            float sticky)
        {
            var axis = endCenter - startCenter;
            var distance = axis.magnitude;
            if (distance <= 0.001f)
            {
                return;
            }

            var midpoint = Vector2.LerpUnclamped(startCenter, endCenter, 0.5f);
            var halfWidth = distance * 0.5f + Mathf.Lerp(2f, _bridgeWidthBoost, sticky);
            var fullHeight = Mathf.Lerp(
                _bridgeMinHeight,
                Mathf.Max(_bridgeMinHeight, Mathf.Min(startSize.y, endSize.y) * _bridgeHeightRatio + PulseValue * (_pulseHeightBoost * 0.85f)),
                sticky);
            var halfHeight = Mathf.Max(_bridgeMinHeight * 0.5f, fullHeight * 0.5f);
            var angle = Mathf.Atan2(axis.y, axis.x);
            var roundness = Vector4.one * halfHeight;
            var bridgeBlend = Mathf.Lerp(_blend * 0.48f, _blend * 1.22f, sticky);

            canvas.RectBrush.Draw(midpoint, new Vector2(halfWidth, halfHeight), roundness, bridgeBlend, angle);
        }

        private static void ClearLocalFillMapping(CanvasGraphic canvas)
        {
            if (canvas?.defaultMaterial == null)
            {
                return;
            }

            canvas.defaultMaterial.SetFloat("_UseLocalFillMapping", 0f);
            canvas.defaultMaterial.SetVector("_LocalFillRect", Vector4.zero);
            canvas.defaultMaterial.SetVector("_LocalFillUVRect", new Vector4(0f, 0f, 1f, 1f));
        }

        private static void ApplyLocalFillMapping(CanvasGraphic canvas, BoundsAccumulator bounds)
        {
            if (canvas?.defaultMaterial == null || !bounds.HasValue)
            {
                ClearLocalFillMapping(canvas);
                return;
            }

            // local fill mapping 的目的不是裁剪 mesh，而是让图片 UV 只跟随当前 gooey 外接框。
            // 这样切换时素材会像“自己在形变移动”，而不是静止贴图下方有一团 mask 在滑。
            var rect = bounds.ToRect();
            canvas.defaultMaterial.SetFloat("_UseLocalFillMapping", 1f);
            canvas.defaultMaterial.SetVector("_LocalFillRect", new Vector4(rect.center.x, rect.center.y, rect.width, rect.height));
            canvas.defaultMaterial.SetVector("_LocalFillUVRect", new Vector4(0f, 0f, 1f, 1f));
        }

        private struct BoundsAccumulator
        {
            private bool _hasValue;
            private Vector2 _min;
            private Vector2 _max;

            public bool HasValue => _hasValue;

            public void Encapsulate(Vector2 center, Vector2 size)
            {
                var half = size * 0.5f;
                var min = center - half;
                var max = center + half;

                if (!_hasValue)
                {
                    _hasValue = true;
                    _min = min;
                    _max = max;
                    return;
                }

                _min = Vector2.Min(_min, min);
                _max = Vector2.Max(_max, max);
            }

            public void EncapsulateBridge(
                Vector2 startCenter,
                Vector2 endCenter,
                Vector2 startSize,
                Vector2 endSize,
                float sticky,
                float bridgeWidthBoost,
                float bridgeHeightRatio,
                float bridgeMinHeight,
                float pulse,
                float pulseHeightBoost)
            {
                var axis = endCenter - startCenter;
                var distance = axis.magnitude;
                if (distance <= 0.001f)
                {
                    return;
                }

                var fullWidth = distance + Mathf.Lerp(4f, bridgeWidthBoost * 2f, sticky);
                var fullHeight = Mathf.Lerp(
                    bridgeMinHeight,
                    Mathf.Max(bridgeMinHeight, Mathf.Min(startSize.y, endSize.y) * bridgeHeightRatio + pulse * (pulseHeightBoost * 0.85f)),
                    sticky);

                var midpoint = Vector2.LerpUnclamped(startCenter, endCenter, 0.5f);
                Encapsulate(midpoint, new Vector2(fullWidth, fullHeight));
            }

            public Rect ToRect()
            {
                if (!_hasValue)
                {
                    return default;
                }

                var size = _max - _min;
                var center = (_min + _max) * 0.5f;
                return new Rect(center.x - size.x * 0.5f, center.y - size.y * 0.5f, size.x, size.y);
            }
        }
    }
}
