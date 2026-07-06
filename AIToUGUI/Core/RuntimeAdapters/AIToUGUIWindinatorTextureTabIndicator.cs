using Riten.Windinator.Shapes;
using UnityEngine;

namespace AIToUGUI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGraphic))]
    public sealed class AIToUGUIWindinatorTextureTabIndicator : AIToUGUIWindinatorImageTabIndicatorBase
    {
        [SerializeField] private Texture _texture;

        protected override bool HasSourceAsset => _texture != null;

        protected override Texture2D ResolveStampSdfTexture()
        {
            return AIToUGUITextureSdfUtility.GetOrCreate(_texture);
        }

        protected override void ApplyFill(CanvasGraphic canvas)
        {
            canvas.SetFillTexture(_texture, Vector2.one, Vector2.zero, _tint, _texture != null, _scaleMode);
        }
    }
}
