using UnityEngine;

namespace AIToUGUI
{
    public enum Ease
    {
        Default,
        Linear,
        InSine,
        OutSine,
        InOutSine,
        InQuad,
        OutQuad,
        InOutQuad,
        InCubic,
        OutCubic,
        InOutCubic,
        InQuart,
        OutQuart,
        InOutQuart,
        InBack,
        OutBack,
        InOutBack
    }

    internal static class AIToUGUIEaseUtility
    {
        public static float Evaluate(float t, Ease ease)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case Ease.Default:
                case Ease.OutCubic:
                    return 1f - Mathf.Pow(1f - t, 3f);
                case Ease.Linear:
                    return t;
                case Ease.InSine:
                    return 1f - Mathf.Cos((t * Mathf.PI) * 0.5f);
                case Ease.OutSine:
                    return Mathf.Sin((t * Mathf.PI) * 0.5f);
                case Ease.InOutSine:
                    return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
                case Ease.InQuad:
                    return t * t;
                case Ease.OutQuad:
                    return 1f - (1f - t) * (1f - t);
                case Ease.InOutQuad:
                    return t < 0.5f
                        ? 2f * t * t
                        : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
                case Ease.InCubic:
                    return t * t * t;
                case Ease.InOutCubic:
                    return t < 0.5f
                        ? 4f * t * t * t
                        : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
                case Ease.InQuart:
                    return t * t * t * t;
                case Ease.OutQuart:
                    return 1f - Mathf.Pow(1f - t, 4f);
                case Ease.InOutQuart:
                    return t < 0.5f
                        ? 8f * t * t * t * t
                        : 1f - Mathf.Pow(-2f * t + 2f, 4f) * 0.5f;
                case Ease.InBack:
                {
                    const float c1 = 1.70158f;
                    const float c3 = c1 + 1f;
                    return c3 * t * t * t - c1 * t * t;
                }
                case Ease.OutBack:
                {
                    const float c1 = 1.70158f;
                    const float c3 = c1 + 1f;
                    var u = t - 1f;
                    return 1f + c3 * u * u * u + c1 * u * u;
                }
                case Ease.InOutBack:
                {
                    const float c1 = 1.70158f;
                    const float c2 = c1 * 1.525f;
                    return t < 0.5f
                        ? Mathf.Pow(2f * t, 2f) * ((c2 + 1f) * 2f * t - c2) * 0.5f
                        : (Mathf.Pow(2f * t - 2f, 2f) * ((c2 + 1f) * (t * 2f - 2f) + c2) + 2f) * 0.5f;
                }
                default:
                    return 1f - Mathf.Pow(1f - t, 3f);
            }
        }

        public static PrimeTween.Easing ToPrimeEasing(Ease ease)
        {
            return ease switch
            {
                Ease.Default => PrimeTween.Ease.OutCubic,
                Ease.Linear => PrimeTween.Ease.Linear,
                Ease.InSine => PrimeTween.Ease.InSine,
                Ease.OutSine => PrimeTween.Ease.OutSine,
                Ease.InOutSine => PrimeTween.Ease.InOutSine,
                Ease.InQuad => PrimeTween.Ease.InQuad,
                Ease.OutQuad => PrimeTween.Ease.OutQuad,
                Ease.InOutQuad => PrimeTween.Ease.InOutQuad,
                Ease.InCubic => PrimeTween.Ease.InCubic,
                Ease.OutCubic => PrimeTween.Ease.OutCubic,
                Ease.InOutCubic => PrimeTween.Ease.InOutCubic,
                Ease.InQuart => PrimeTween.Ease.InQuart,
                Ease.OutQuart => PrimeTween.Ease.OutQuart,
                Ease.InOutQuart => PrimeTween.Ease.InOutQuart,
                Ease.InBack => PrimeTween.Ease.InBack,
                Ease.OutBack => PrimeTween.Ease.OutBack,
                Ease.InOutBack => PrimeTween.Ease.InOutBack,
                _ => PrimeTween.Ease.OutCubic
            };
        }
    }
}
