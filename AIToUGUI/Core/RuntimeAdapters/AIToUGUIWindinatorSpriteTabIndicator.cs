using Riten.Windinator.Shapes;
using UnityEngine;

namespace AIToUGUI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGraphic))]
    public sealed class AIToUGUIWindinatorSpriteTabIndicator : AIToUGUIWindinatorImageTabIndicatorBase
    {
        [SerializeField] private Sprite _sprite;

        protected override bool HasSourceAsset => _sprite != null;

        protected override Texture2D ResolveStampSdfTexture()
        {
            return AIToUGUITextureSdfUtility.GetOrCreate(_sprite);
        }

        protected override void ApplyFill(CanvasGraphic canvas)
        {
            canvas.SetFillTexture(_sprite, Vector2.one, Vector2.zero, _tint, _sprite != null, _scaleMode);
        }
    }
}
