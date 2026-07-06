using UnityEngine;

namespace Riten.Windinator.Shapes
{
    [System.Serializable]
    public sealed class TextureSDFDrawer : ShapeDrawer
    {
        public TextureSDFDrawer(CanvasGraphic canvas) : base(canvas)
        {
        }

        public override string MaterialName => "UI/Windinator/DrawTextureSDF";

        public void Draw(
            Texture stampTexture,
            Vector2 center,
            Vector2 size,
            float blend = 0f,
            float rotationInRadian = 0f,
            DrawOperation operation = DrawOperation.Union,
            LayerGraphic layer = null,
            SDFTextureScaleMode scaleMode = SDFTextureScaleMode.Fit)
        {
            if (stampTexture == null || size.x <= 0.01f || size.y <= 0.01f)
            {
                return;
            }

            SetupMaterial(blend, operation, layer);
            Material.SetTexture("_StampTex", stampTexture);
            Material.SetVector("_StampCenter", new Vector4(center.x, center.y, rotationInRadian, 0f));
            Material.SetVector("_StampHalfSize", new Vector4(size.x * 0.5f, size.y * 0.5f, 0f, 0f));
            Material.SetFloat("_StampScaleMode", (float)scaleMode);

            Dispatch(layer);
        }

        protected override void DrawBatches(LayerGraphic layer = null)
        {
        }
    }
}
