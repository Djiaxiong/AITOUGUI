using LeTai.TrueShadow.PluginInterfaces;
using UnityEngine;
using UnityEngine.UI;
using DTT.UI.ProceduralUI;
 
namespace AIToUGUI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Graphic))]
    public sealed class AIToUGUITrueShadowHashProvider : MonoBehaviour, ITrueShadowCustomHashProviderV2
    {
        public event System.Action<int> trueShadowCustomHashChanged;

        private Graphic _graphic;
        private RectTransform _rectTransform;
        private RoundedImage _roundedImage;
        private Border _border;
        private SignedDistanceFieldGraphic _signedDistanceFieldGraphic;
        private RectangleGraphic _rectangleGraphic;
        private PolygonGraphic _polygonGraphic;
        private int _lastHash = int.MinValue;
        private bool _dirty = true;

        private void OnEnable()
        {
            RefreshBindings();
            MarkDirty();
            PublishIfChanged();
        }

        private void OnDisable()
        {
            Unsubscribe();
            _lastHash = int.MinValue;
            trueShadowCustomHashChanged?.Invoke(0);
        }

        private void OnValidate()
        {
            RefreshBindings();
            MarkDirty();
        }

        private void OnTransformParentChanged()
        {
            MarkDirty();
        }

        private void OnRectTransformDimensionsChange()
        {
            MarkDirty();
        }

        private void LateUpdate()
        {
            if (_signedDistanceFieldGraphic != null)
            {
                _dirty = true;
            }

            if (!_dirty)
            {
                return;
            }

            PublishIfChanged();
        }

        private void RefreshBindings()
        {
            _graphic ??= GetComponent<Graphic>();
            _rectTransform ??= GetComponent<RectTransform>();

            var roundedImage = GetComponent<RoundedImage>();
            var border = GetComponent<Border>();
            _signedDistanceFieldGraphic = GetComponent<SignedDistanceFieldGraphic>();
            _rectangleGraphic = GetComponent<RectangleGraphic>();
            _polygonGraphic = GetComponent<PolygonGraphic>();
            if (_roundedImage == roundedImage && _border == border)
            {
                return;
            }

            Unsubscribe();
            _roundedImage = roundedImage;
            _border = border;

            if (_roundedImage != null)
            {
                _roundedImage.OnUpdate += HandleSourceChanged;
            }

            if (_border != null)
            {
                _border.OnUpdate += HandleSourceChanged;
            }
        }

        private void Unsubscribe()
        {
            if (_roundedImage != null)
            {
                _roundedImage.OnUpdate -= HandleSourceChanged;
            }

            if (_border != null)
            {
                _border.OnUpdate -= HandleSourceChanged;
            }
        }

        private void HandleSourceChanged()
        {
            MarkDirty();
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        private void PublishIfChanged()
        {
            _dirty = false;
            RefreshBindings();

            var hash = CalculateHash();
            if (hash == _lastHash)
            {
                return;
            }

            _lastHash = hash;
            trueShadowCustomHashChanged?.Invoke(hash);
        }

        private int CalculateHash()
        {
            unchecked
            {
                var hash = 17;

                if (_graphic != null)
                {
                    hash = Add(hash, _graphic.GetType().FullName?.GetHashCode() ?? 0);
                    hash = Add(hash, _graphic.color.GetHashCode());
                    var material = _graphic.materialForRendering;
                    hash = Add(hash, material != null ? material.ComputeCRC() : 0);
                    var texture = _graphic.mainTexture;
                    hash = Add(hash, texture != null ? texture.GetInstanceID() : 0);
                }

                if (_rectTransform != null)
                {
                    var rect = _rectTransform.rect;
                    hash = Add(hash, Mathf.RoundToInt(rect.width * 100f));
                    hash = Add(hash, Mathf.RoundToInt(rect.height * 100f));
                    hash = Add(hash, Mathf.RoundToInt(_rectTransform.pivot.x * 1000f));
                    hash = Add(hash, Mathf.RoundToInt(_rectTransform.pivot.y * 1000f));
                }

                if (_roundedImage != null)
                {
                    hash = Add(hash, (int)_roundedImage.Mode);
                    hash = Add(hash, (int)_roundedImage.RoundingUnit);
                    hash = Add(hash, Mathf.RoundToInt(_roundedImage.BorderThickness * 10000f));
                    hash = Add(hash, Mathf.RoundToInt(_roundedImage.DistanceFalloff * 10000f));
                    hash = Add(hash, _roundedImage.UseHitboxOutside ? 1 : 0);
                    hash = Add(hash, _roundedImage.UseHitboxInside ? 1 : 0);

                    foreach (var pair in _roundedImage.GetCornerRounding())
                    {
                        hash = Add(hash, (int)pair.Key);
                        hash = Add(hash, Mathf.RoundToInt(pair.Value * 10000f));
                    }
                }

                if (_signedDistanceFieldGraphic != null)
                {
                    hash = Add(hash, Mathf.RoundToInt(_signedDistanceFieldGraphic.Alpha * 10000f));
                    hash = Add(hash, _signedDistanceFieldGraphic.LeftUpColor.GetHashCode());
                    hash = Add(hash, _signedDistanceFieldGraphic.RightUpColor.GetHashCode());
                    hash = Add(hash, _signedDistanceFieldGraphic.RightDownColor.GetHashCode());
                    hash = Add(hash, _signedDistanceFieldGraphic.LeftDownColor.GetHashCode());
                    hash = Add(hash, _signedDistanceFieldGraphic.OutlineColor.GetHashCode());
                    hash = Add(hash, Mathf.RoundToInt(_signedDistanceFieldGraphic.OutlineSize * 1000f));
                    hash = Add(hash, _signedDistanceFieldGraphic.ShadowColor.GetHashCode());
                    hash = Add(hash, Mathf.RoundToInt(_signedDistanceFieldGraphic.ShadowSize * 1000f));
                    hash = Add(hash, Mathf.RoundToInt(_signedDistanceFieldGraphic.ShadowBlur * 1000f));
                    hash = Add(hash, Mathf.RoundToInt(_signedDistanceFieldGraphic.EmbossDirection * 1000f));
                    hash = Add(hash, Mathf.RoundToInt(_signedDistanceFieldGraphic.EmbossDistance * 1000f));
                    hash = Add(hash, Mathf.RoundToInt(_signedDistanceFieldGraphic.EmbossSize * 1000f));
                    hash = Add(hash, Mathf.RoundToInt(_signedDistanceFieldGraphic.EmbossPower * 1000f));
                    hash = Add(hash, _signedDistanceFieldGraphic.EmbossHighlightColor.GetHashCode());
                    hash = Add(hash, _signedDistanceFieldGraphic.EmbossLowlightColor.GetHashCode());
                    hash = Add(hash, _signedDistanceFieldGraphic.MaskRect.GetHashCode());
                    hash = Add(hash, _signedDistanceFieldGraphic.MaskOffset.GetHashCode());
                    hash = Add(hash, _signedDistanceFieldGraphic.CirclePos.GetHashCode());
                    hash = Add(hash, Mathf.RoundToInt(_signedDistanceFieldGraphic.CircleSize * 1000f));
                    hash = Add(hash, Mathf.RoundToInt(_signedDistanceFieldGraphic.CircleAlpha * 1000f));
                    hash = Add(hash, _signedDistanceFieldGraphic.CircleColor.GetHashCode());
                }

                if (_rectangleGraphic != null)
                {
                    hash = Add(hash, _rectangleGraphic.Roundness.GetHashCode());
                    hash = Add(hash, _rectangleGraphic.UniformRoundness ? 1 : 0);
                    hash = Add(hash, _rectangleGraphic.MaxRoundess ? 1 : 0);
                }

                if (_polygonGraphic != null && _polygonGraphic.Points != null)
                {
                    hash = Add(hash, Mathf.RoundToInt(_polygonGraphic.Roundness * 1000f));
                    hash = Add(hash, _polygonGraphic.Points.Length);
                    for (var i = 0; i < _polygonGraphic.Points.Length; i++)
                    {
                        hash = Add(hash, _polygonGraphic.Points[i].GetHashCode());
                    }
                }

                if (_border != null)
                {
                    hash = Add(hash, (int)_border.RoundingUnit);
                    hash = Add(hash, Mathf.RoundToInt(_border.BorderThickness * 10000f));
                    hash = Add(hash, _border.RenderOutside ? 1 : 0);
                    hash = Add(hash, _border.Color.GetHashCode());
                }

                return hash;
            }
        }

        private static int Add(int hash, int value)
        {
            unchecked
            {
                return hash * 31 + value;
            }
        }
    }
}
