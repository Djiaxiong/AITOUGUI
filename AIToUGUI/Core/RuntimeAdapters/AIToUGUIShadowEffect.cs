using LeTai.Effects;
using LeTai.TrueShadow.PluginInterfaces;
using TrueShadowBlendMode = LeTai.TrueShadow.BlendMode;
using TrueShadowComponent = LeTai.TrueShadow.TrueShadow;
using UnityEngine;
using UnityEngine.UI;
using DTT.UI.ProceduralUI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AIToUGUI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Graphic))]
    public sealed class AIToUGUIShadowEffect : MonoBehaviour
    {
        [SerializeField] private float _shadowSize = 12f;
        [SerializeField] private float _shadowBlur = 28f;
        [SerializeField] private Color _shadowColor = new Color(0f, 0f, 0f, 0.28f);
        [SerializeField] private bool _enableGlow;
        [SerializeField] private Color _glowColor = new Color(0.45f, 0.76f, 1f, 0.35f);
        [SerializeField] private float _glowBlur = 24f;
        [SerializeField] private float _glowIntensity = 1f;

        [SerializeField] private AIToUGUITrueShadowHashProvider _hashProvider;
        [SerializeField] private TrueShadowComponent _dropShadow;
        [SerializeField] private TrueShadowComponent _glowShadow;

#if UNITY_EDITOR
        private bool _editorApplyQueued;
#endif

        public void Configure(
            float shadowSize,
            float shadowBlur,
            Color shadowColor,
            bool enableGlow,
            Color glowColor,
            float glowBlur,
            float glowIntensity)
        {
            _shadowSize = shadowSize;
            _shadowBlur = shadowBlur;
            _shadowColor = shadowColor;
            _enableGlow = enableGlow;
            _glowColor = glowColor;
            _glowBlur = glowBlur;
            _glowIntensity = glowIntensity;
            ApplyNow();
        }

        private void Awake()
        {
            ApplyNow();
        }

        private void OnEnable()
        {
            ApplyNow();
        }

        private void OnValidate()
        {
            QueueApply();
        }

        private void ApplyNow()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && AIToUGUIEditorUiMutationGuard.IsUnsafeToMutateUi())
            {
                QueueApply();
                return;
            }
#endif

            EnsureHashProvider();
            CleanupLegacyEffects();
            ConfigureDropShadow();
            ConfigureGlow();
        }

        private void EnsureHashProvider()
        {
            _hashProvider = GetComponent<AIToUGUITrueShadowHashProvider>();
            if (_hashProvider == null)
            {
                _hashProvider = gameObject.AddComponent<AIToUGUITrueShadowHashProvider>();
            }
        }

        private void QueueApply()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (_editorApplyQueued)
                {
                    return;
                }

                _editorApplyQueued = true;
                EditorApplication.delayCall += ApplyQueuedInEditor;
                return;
            }
#endif

            ApplyNow();
        }

#if UNITY_EDITOR
        private void ApplyQueuedInEditor()
        {
            _editorApplyQueued = false;
            if (this == null || gameObject == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (AIToUGUIEditorUiMutationGuard.IsUnsafeToMutateUi())
            {
                AIToUGUIEditorUiMutationGuard.QueueEditorCallback(ref _editorApplyQueued, ApplyQueuedInEditor);
                return;
            }
#endif

            ApplyNow();
        }
#endif

        private void ConfigureDropShadow()
        {
            if (_shadowColor.a <= 0.001f || (_shadowSize <= 0.001f && _shadowBlur <= 0.001f))
            {
                RemoveShadowComponent(ref _dropShadow, _glowShadow);
                return;
            }

            _dropShadow = EnsureShadowComponent(ref _dropShadow, _glowShadow);
            ConfigureTrueShadow(
                _dropShadow,
                ResolveShadowSize(_shadowSize, _shadowBlur),
                ResolveShadowSpread(_shadowSize, _shadowBlur),
                ResolveShadowDistance(_shadowSize, _shadowBlur),
                45f,
                _shadowColor,
                TrueShadowBlendMode.Normal);
        }

        private void ConfigureGlow()
        {
            if (!_enableGlow || _glowColor.a <= 0.001f || _glowBlur <= 0.001f)
            {
                RemoveShadowComponent(ref _glowShadow, _dropShadow);
                return;
            }

            var glowColor = _glowColor;
            glowColor.a *= Mathf.Clamp01(_glowIntensity);

            _glowShadow = EnsureShadowComponent(ref _glowShadow, _dropShadow);
            ConfigureTrueShadow(
                _glowShadow,
                Mathf.Max(1f, _glowBlur),
                Mathf.Clamp01(0.18f + Mathf.Clamp01(_glowIntensity) * 0.22f),
                0f,
                0f,
                glowColor,
                TrueShadowBlendMode.Screen);
        }

        private void CleanupLegacyEffects()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject) > 0)
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
            }
#endif

            var components = GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();
                if (type == typeof(Shadow) || type == typeof(Outline))
                {
                    RemoveComponent(component);
                }
            }
        }

        private TrueShadowComponent EnsureShadowComponent(ref TrueShadowComponent component, TrueShadowComponent other)
        {
            if (component != null)
            {
                return component;
            }

            var existing = GetComponents<TrueShadowComponent>();
            for (var i = 0; i < existing.Length; i++)
            {
                var candidate = existing[i];
                if (candidate != null && candidate != other)
                {
                    component = candidate;
                    return component;
                }
            }

            component = gameObject.AddComponent<TrueShadowComponent>();
            return component;
        }

        private static void ConfigureTrueShadow(
            TrueShadowComponent shadow,
            float size,
            float spread,
            float distance,
            float angle,
            Color color,
            TrueShadowBlendMode blendMode)
        {
            if (shadow == null)
            {
                return;
            }

            shadow.enabled = true;
            shadow.Algorithm = BlurAlgorithmSelection.Fast;
            shadow.Inset = false;
            shadow.UseGlobalAngle = false;
            shadow.IgnoreCasterColor = true;
            shadow.UseCasterAlpha = true;
            shadow.BlendMode = blendMode;
            shadow.DisableFitCompensation = false;
            shadow.Size = Mathf.Max(0f, size);
            shadow.Spread = Mathf.Clamp01(spread);
            shadow.OffsetAngle = angle;
            shadow.OffsetDistance = Mathf.Max(0f, distance);
            shadow.Color = color;
            shadow.SetLayoutDirty();
            shadow.SetTextureDirty();
        }

        private static float ResolveShadowSize(float shadowSize, float shadowBlur)
        {
            return Mathf.Max(1f, shadowBlur);
        }

        private static float ResolveShadowSpread(float shadowSize, float shadowBlur)
        {
            var denom = Mathf.Max(1f, shadowBlur + shadowSize);
            return Mathf.Clamp01(shadowSize / denom * 0.85f);
        }

        private static float ResolveShadowDistance(float shadowSize, float shadowBlur)
        {
            return Mathf.Max(0f, shadowSize);
        }

        private void RemoveShadowComponent(ref TrueShadowComponent component, TrueShadowComponent other)
        {
            if (component == null)
            {
                var existing = GetComponents<TrueShadowComponent>();
                for (var i = 0; i < existing.Length; i++)
                {
                    var candidate = existing[i];
                    if (candidate != null && candidate != other)
                    {
                        component = candidate;
                        break;
                    }
                }
            }

            if (component == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(component);
            }
            else
            {
                AIToUGUIEditorUiMutationGuard.DestroyComponentSafely(component);
            }

            component = null;
        }

        private void RemoveComponent(Component component)
        {
            if (component == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(component);
            }
            else
            {
                AIToUGUIEditorUiMutationGuard.DestroyComponentSafely(component);
            }
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            if (_editorApplyQueued)
            {
                EditorApplication.delayCall -= ApplyQueuedInEditor;
                _editorApplyQueued = false;
            }
#endif
        }
    }

}
