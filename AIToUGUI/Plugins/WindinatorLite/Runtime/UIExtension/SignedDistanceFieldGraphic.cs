using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

public enum SDFTextureScaleMode
{
    Stretch = 0,
    Fit = 1,
    Cover = 2
}

public class SignedDistanceFieldGraphic : MaskableGraphic
{
    [Header("Graphic")]

    [SerializeField] Texture m_texture;

    [SerializeField, Range(0f, 1f)] float m_graphicAlphaMult = 1f;
    [SerializeField, Range(-1f, 2f)] float m_graphicBlur = 0f;

    [Header("Circle")]

    [SerializeField, Range(0f, 1f)] float m_circleAlpha = 0.2f;

    [SerializeField] Vector2 m_circlePos;

    [SerializeField] Color32 m_circleColor;

    [SerializeField] float m_circleRadius = 0f;
    

    [Header("Emboss")]

    [SerializeField] float m_embossDirection = 0f;
    [SerializeField] float m_embossBlurTop = 0f;
    [SerializeField] float m_embossBlurBottom = 0f;
    [SerializeField] float m_embossDistance = 0f;
    [SerializeField] float m_embossSize = 0f;
    [SerializeField] float m_embossPow = 0f;
    [SerializeField] Color m_embossHighlightCol = Color.white;
    [SerializeField] Color m_embossLowlightCol = Color.black;

    [Header("Outline")]

    [SerializeField, Min(0f)] float m_outlineSize = 0f;

    [SerializeField] Color32 m_outlineColor = Color.black;

    [Header("Shadow")]

    [SerializeField, Min(0f)] float m_shadowSize = 0f;

    [SerializeField, Min(0f)] float m_shadowBlur = 0f;

    [SerializeField] Color32 m_shadowColor = Color.black;

    [Header("Mask")]

    [SerializeField] Vector4 m_maskRect;

    [SerializeField] Vector4 m_maskOffset;

    Material m_material;

    [Header("Gradient")]

    [SerializeField] Color m_leftUpColor = Color.white;
    [SerializeField] Color m_rightUpColor = Color.white;
    [SerializeField] Color m_rightDownColor = Color.white;
    [SerializeField] Color m_leftDownColor = Color.white;

    [Header("Fill Texture")]

    [SerializeField] bool m_useFillTexture;
    [SerializeField] Texture m_fillTexture;
    [SerializeField] Sprite m_fillSprite;
    [SerializeField] Color m_fillTint = Color.white;
    [SerializeField] Vector2 m_fillTiling = Vector2.one;
    [SerializeField] Vector2 m_fillOffset = Vector2.zero;
    [SerializeField] SDFTextureScaleMode m_fillTextureScaleMode = SDFTextureScaleMode.Stretch;
    [SerializeField] bool m_useFlowTexture;
    [SerializeField] Texture m_flowTexture;
    [SerializeField] Vector2 m_flowTiling = new Vector2(1.4f, 1.2f);
    [SerializeField] Vector2 m_flowOffset = Vector2.zero;
    [SerializeField] Vector2 m_flowSpeed = new Vector2(0.06f, 0f);
    [SerializeField, Range(0f, 0.25f)] float m_flowStrength = 0.03f;

    public Texture Texture {
        get => m_texture;
        set {
            m_texture = value; 
            SetMaterialDirty();
        }
    }

    public float EmbossDirection
    {
        get => m_embossDirection;
        set
        {
            m_embossDirection = Mathf.Min(1, Mathf.Max(0, value));
            SetVerticesDirty();
        }
    }

    public Color EmbossHighlightColor
    {
        get => m_embossHighlightCol;
        set
        {
            m_embossHighlightCol = value;
            SetVerticesDirty();
        }
    }

    public Color EmbossLowlightColor
    {
        get => m_embossLowlightCol;
        set
        {
            m_embossLowlightCol = value;
            SetVerticesDirty();
        }
    }

    public float EmbossPower
    {
        get => m_embossPow;
        set
        {
            m_embossPow = Mathf.Min(1, Mathf.Max(0, value));
            SetVerticesDirty();
        }
    }

    public float EmbossBlurTop
    {
        get => m_embossBlurTop;
        set
        {
            m_embossBlurTop = Mathf.Min(1, Mathf.Max(-1, value));
            SetVerticesDirty();
        }
    }

    public float EmbossBlurBottom
    {
        get => m_embossBlurBottom;
        set
        {
            m_embossBlurBottom = Mathf.Min(1, Mathf.Max(-1, value));
            SetVerticesDirty();
        }
    }

    public float EmbossDistance
    {
        get => m_embossDistance;
        set
        {
            m_embossDistance = Mathf.Max(0, value);
            SetVerticesDirty();
        }
    }

    public float EmbossSize
    {
        get => m_embossSize;
        set
        {
            m_embossSize = Mathf.Max(0, value);
            SetVerticesDirty();
        }
    }

    public virtual float Margin => Mathf.Max(m_outlineSize, m_shadowSize) + 2 + ExtraMargin;

    public virtual float ExtraMargin => 0;

    protected event Action<float, float> onMaterialUpdate;

    public override Texture mainTexture => m_texture;

    public float Alpha {get => m_graphicAlphaMult; set
    {
        m_graphicAlphaMult = value;
        SetAllDirty();

        if (value == 0) canvasRenderer.cull = true;
        else            canvasRenderer.cull = false;
    }}

    public float GraphicBlur
    {
        get => m_graphicBlur;
        set
        {
            m_graphicBlur = value;
            SetMaterialDirty();
        }
    }

    static Material RectangleRendererShader;

    protected override void OnEnable()
    {
        base.OnEnable();
        UpdateInstanciable();
    }

    private void LoadMaterial()
    {
        if (RectangleRendererShader == null)
        {
            RectangleRendererShader = new Material(Shader.Find("UI/Windinator/RectangleRenderer"));
            RectangleRendererShader.enableInstancing = true;
        }
        m_material = RectangleRendererShader;
    }

    public override Material defaultMaterial
    {
        get
        {
            if (m_material == null)
            {
                LoadMaterial();
            }
            return m_material;
        }
    }

    public Color CircleColor
    {
        get => m_circleColor;
        set
        {
            m_circleColor = value;
            SetVerticesDirty();
        }
    }

    public float CircleAlpha
    {
        get => m_circleAlpha;
        set
        {
            m_circleAlpha = Mathf.Clamp01(value);
            SetVerticesDirty();
        }
    }

    public float CircleSize
    {
        get => m_circleRadius;
        set
        {
            m_circleRadius = Mathf.Max(0, value);
            SetMaterialDirty();
        }
    }

    public Color OutlineColor
    {
        get => m_outlineColor;
        set
        {
            m_outlineColor = value;
            SetVerticesDirty();
        }
    }

    public float OutlineSize
    {
        get => m_outlineSize;
        set
        {
            m_outlineSize = Mathf.Max(0, value);
            SetVerticesDirty();
        }
    }

    public Color ShadowColor
    {
        get => m_shadowColor;
        set
        {
            m_shadowColor = value;
            SetVerticesDirty();
        }
    }

    public float ShadowSize
    {
        get => m_shadowSize;
        set
        {
            m_shadowSize = Mathf.Max(0, value);
            SetVerticesDirty();
        }
    }

    public float ShadowBlur
    {
        get => m_shadowBlur;
        set
        {
            m_shadowBlur = value;
            SetVerticesDirty();
        }
    }

    public Vector2 CirclePos { get => m_circlePos; set { m_circlePos = value; SetMaterialDirty();} }

    public Vector4 MaskRect { 
        get => m_maskRect; 
        set { 
            m_maskRect = value;
            SetMaterialDirty(); 
        } 
    }

    public Vector4 MaskOffset { get => m_maskOffset; set { 
            m_maskOffset = value;
            SetMaterialDirty(); 
        }  }

    public Color LeftUpColor { get => m_leftUpColor; set => m_leftUpColor = value; }
    
    public Color RightUpColor { get => m_rightUpColor; set => m_rightUpColor = value; }

    public Color RightDownColor { get => m_rightDownColor; set => m_rightDownColor = value; }

    public Color LeftDownColor { get => m_leftDownColor; set => m_leftDownColor = value; }

    public bool UseFillTexture
    {
        get => m_useFillTexture;
        set
        {
            if (m_useFillTexture == value)
            {
                return;
            }

            m_useFillTexture = value;
            SetMaterialDirty();
        }
    }

    public Texture FillTexture
    {
        get => m_fillTexture;
        set
        {
            if (m_fillTexture == value && m_fillSprite == null)
            {
                return;
            }

            m_fillTexture = value;
            if (value != null)
            {
                m_fillSprite = null;
            }

            SetMaterialDirty();
        }
    }

    public Sprite FillSprite
    {
        get => m_fillSprite;
        set
        {
            if (m_fillSprite == value)
            {
                return;
            }

            m_fillSprite = value;
            if (value != null)
            {
                m_fillTexture = null;
            }

            SetMaterialDirty();
        }
    }

    public Color FillTint
    {
        get => m_fillTint;
        set
        {
            m_fillTint = value;
            SetMaterialDirty();
        }
    }

    public Vector2 FillTiling
    {
        get => m_fillTiling;
        set
        {
            m_fillTiling = value;
            SetMaterialDirty();
        }
    }

    public Vector2 FillOffset
    {
        get => m_fillOffset;
        set
        {
            m_fillOffset = value;
            SetMaterialDirty();
        }
    }

    public SDFTextureScaleMode FillTextureScaleMode
    {
        get => m_fillTextureScaleMode;
        set
        {
            if (m_fillTextureScaleMode == value)
            {
                return;
            }

            m_fillTextureScaleMode = value;
            SetMaterialDirty();
        }
    }

    public bool UseFlowTexture
    {
        get => m_useFlowTexture;
        set
        {
            if (m_useFlowTexture == value)
            {
                return;
            }

            m_useFlowTexture = value;
            SetMaterialDirty();
        }
    }

    public Texture FlowTexture
    {
        get => m_flowTexture;
        set
        {
            if (m_flowTexture == value)
            {
                return;
            }

            m_flowTexture = value;
            SetMaterialDirty();
        }
    }

    public Vector2 FlowTiling
    {
        get => m_flowTiling;
        set
        {
            m_flowTiling = value;
            SetMaterialDirty();
        }
    }

    public Vector2 FlowOffset
    {
        get => m_flowOffset;
        set
        {
            m_flowOffset = value;
            SetMaterialDirty();
        }
    }

    public Vector2 FlowSpeed
    {
        get => m_flowSpeed;
        set
        {
            m_flowSpeed = value;
            SetMaterialDirty();
        }
    }

    public float FlowStrength
    {
        get => m_flowStrength;
        set
        {
            m_flowStrength = Mathf.Clamp(value, 0f, 0.25f);
            SetMaterialDirty();
        }
    }

    public void SetCircle(Vector2 pos, Color color, float size, float alpha)
    {
        m_circleAlpha = alpha;
        m_circleRadius = size;
        CirclePos = pos;
        m_circleColor = color;

        SetAllDirty();
    }

    public void SetOutline(Color color, float size)
    {
        m_outlineSize = size;
        m_outlineColor = color;

        SetAllDirty();
    }

    public void SetMask(Vector4 mask)
    {
        MaskRect = mask;
        SetAllDirty();
    }

    public void SetShadow(Color color, float size, float blur)
    {
        m_shadowColor = color;
        m_shadowSize = size;
        m_shadowBlur = blur;

        SetAllDirty();
    }

    public void SetFillTexture(Texture texture, Vector2 tiling, Vector2 offset, Color tint, bool useFillTexture)
    {
        m_fillTexture = texture;
        m_fillSprite = null;
        m_fillTiling = tiling;
        m_fillOffset = offset;
        m_fillTint = tint;
        m_useFillTexture = useFillTexture;
        SetMaterialDirty();
    }

    public void SetFillTexture(
        Texture texture,
        Vector2 tiling,
        Vector2 offset,
        Color tint,
        bool useFillTexture,
        SDFTextureScaleMode scaleMode)
    {
        m_fillTexture = texture;
        m_fillSprite = null;
        m_fillTiling = tiling;
        m_fillOffset = offset;
        m_fillTint = tint;
        m_useFillTexture = useFillTexture;
        m_fillTextureScaleMode = scaleMode;
        SetMaterialDirty();
    }

    public void SetFillTexture(Sprite sprite, Vector2 tiling, Vector2 offset, Color tint, bool useFillTexture, SDFTextureScaleMode scaleMode)
    {
        m_fillSprite = sprite;
        if (sprite != null)
        {
            m_fillTexture = null;
        }

        m_fillTiling = tiling;
        m_fillOffset = offset;
        m_fillTint = tint;
        m_useFillTexture = useFillTexture;
        m_fillTextureScaleMode = scaleMode;
        SetMaterialDirty();
    }

    public void SetFlowTexture(Texture texture, Vector2 tiling, Vector2 offset, Vector2 speed, float strength, bool useFlowTexture)
    {
        m_flowTexture = texture;
        m_flowTiling = tiling;
        m_flowOffset = offset;
        m_flowSpeed = speed;
        m_flowStrength = Mathf.Clamp(strength, 0f, 0.25f);
        m_useFlowTexture = useFlowTexture;
        SetMaterialDirty();
    }

    private Vector2 m_sizeCached;

    private void UpdateShaderDimensions()
    {
        float width = rectTransform.rect.width;
        float height = rectTransform.rect.height;

        m_sizeCached.x = width;
        m_sizeCached.y = height;

        onMaterialUpdate?.Invoke(width, height);
    }

    public override void SetLayoutDirty()
    {
        base.SetLayoutDirty();
        UpdateShaderDimensions();
    }

    public override void SetVerticesDirty()
    {
        base.SetVerticesDirty();

        #if UNITY_EDITOR
        if (canvas != null && !Application.isPlaying)
        {
            canvas.additionalShaderChannels = 
                AdditionalCanvasShaderChannels.Normal | 
                AdditionalCanvasShaderChannels.Tangent | 
                AdditionalCanvasShaderChannels.TexCoord1 | 
                AdditionalCanvasShaderChannels.TexCoord2 | 
                AdditionalCanvasShaderChannels.TexCoord3;
        }
        #endif
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();

        UpdateShaderDimensions();
    }

    protected virtual Vector4 UpdateMeshData(float width, float height)
    {
        return default;
    }

    static readonly Vector4 ZERO = new Vector2(0, 0), UP = new Vector4(0, 1), ONE = new Vector4(1, 1), RIGHT = new Vector4(1, 0);

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Vector2 size = rectTransform.rect.size;

        float width = size.x;
        float height = size.y;

        Vector3 pivot = new Vector3(
            rectTransform.pivot.x * width,
            rectTransform.pivot.y * height, 0);

        Vector2 extraData = new Vector2(DecodeFloatRGBA(EmbossLowlightColor), Margin);

        var uv1 = new Vector4(
            m_shadowSize,
            m_shadowBlur,
            m_embossSize,
            m_outlineSize
        );

        var uv2 = new Vector4(
            m_sizeCached.x,
            m_sizeCached.y,
            m_embossDistance,
            DecodeFloatRGBA(EmbossHighlightColor)
        );

        var uv3 = new Vector4(
            DecodeFloatRGBA(m_outlineColor),
            DecodeFloatRGBA(m_shadowColor),
            0,
            DecodeFloatRGBA(m_embossDistance, m_graphicAlphaMult, 0, 0)
        );

        var normal = new Vector3(
            DecodeFloatRGBA(m_embossDirection, (m_embossBlurTop + 1) * 0.5f, (m_embossBlurBottom + 1) * 0.5f, m_embossPow),
            0, // Don't touch this
            0 // Don't touch this
        );


        Vector4 extraDataV4 = new Vector4(0, 0, extraData.x, extraData.y);

        float marginedWidth = width + Margin - pivot.x;
        float marginedHeight = height + Margin - pivot.y;
        float negativeMarginX = -Margin - pivot.x;
        float negativeMarginY = -Margin - pivot.y;

        var tangent = UpdateMeshData(width, height);

        vh.AddVert(
            new Vector3(negativeMarginX, negativeMarginY),
            color * LeftDownColor,
            ZERO + extraDataV4,
            uv1, uv2, uv3, normal, tangent);

        vh.AddVert(
            new Vector3(negativeMarginX, marginedHeight),
            color * LeftUpColor,
            UP + extraDataV4,
            uv1, uv2, uv3, normal, tangent);

        vh.AddVert(
            new Vector3(marginedWidth, marginedHeight),
            color * RightUpColor,
            ONE + extraDataV4,
            uv1, uv2, uv3, normal, tangent);

        vh.AddVert(
            new Vector3(marginedWidth, negativeMarginY),
            color * RightDownColor,
            RIGHT + extraDataV4,
            uv1, uv2, uv3, normal, tangent);

        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
    }

    const int LOSE = 20;
    const int WIN = (255 - (LOSE * 2));
    const int WIN_2 = (127 - (LOSE * 2));
    const int WIN_SHORT = (65535 - (LOSE * 2));

    byte RemapColor(byte chanel)
    {
        float f = chanel / 255f;
        return (byte)((f * WIN) + LOSE);
    }

    byte RemapColor2(byte chanel)
    {
        float f = chanel / 255f;
        return (byte)((f * WIN_2) + LOSE);
    }

    byte RemapColor(float f)
    {
        return (byte)((f * WIN) + LOSE);
    }

    ushort RemapColorShort(float f)
    {
        return (ushort)((f * WIN_SHORT) + LOSE);
    }

    byte RemapColor2(float f)
    {
        return (byte)((f * WIN_2) + LOSE);
    }

    float DecodeSizeFloat(float value, float maxLength)
    {
        return ((value / maxLength) + 1) * 0.5f;
    }

    /// <summary>
    /// Max value = 16383 
    /// Second axis (y) will lose precision if x is big
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    float DecodeUnsignedVector2(Vector2 value)
    {
        return DecodeUnsignedVector2(value.x, value.y);
    }

    struct DecodePair
    {
        public uint Value;
        public uint MaxValue;

        public DecodePair(uint v, uint max)
        {
            Value = v;
            MaxValue = max;
        }
    }

    /// <summary>
    /// Bigger "MaxValues" should ALWAYS go first in order.
    /// </summary>
    float DecodeValues(DecodePair x, DecodePair y, uint z)
    {
        return z * x.MaxValue * y.MaxValue + y.Value * x.MaxValue + x.Value;
    }

    float DecodeValues(DecodePair x, uint y)
    {
        return y * x.MaxValue + x.Value;
    }

    float DecodeUnsignedVector2(float _x, float _y)
    {
        float x = Mathf.Round(Mathf.Max(0, Mathf.Min(_x * 4, 16383)));
        float y = Mathf.Round(Mathf.Max(0, Mathf.Min(_y * 4, 16383)));

        float compiled = x * 16384 + y;

        return compiled;
    }

    float DecodeFloatRGBA(float r, float g, float b, float a)
    {
        int result = RemapColor(r);
        result |= RemapColor(g) << 8;
        result |= RemapColor(b) << 16;
        result |= RemapColor2(a) << 24;

        return Int32BitsToSingle(result);
    }

    float DecodeFloatRGBA(Color color)
    {
        int result = RemapColor(color.r);
        result |= RemapColor(color.g) << 8;
        result |= RemapColor(color.b) << 16;
        result |= RemapColor2(color.a) << 24;

        return Int32BitsToSingle(result);
    }

    float DecodeFloatRGBA(Color32 color)
    {
        int result = RemapColor(color.r);
        result |= RemapColor(color.g) << 8;
        result |= RemapColor(color.b) << 16;
        result |= RemapColor2(color.a) << 24;

        return Int32BitsToSingle(result);
    }

    public static unsafe float Int32BitsToSingle(int value) {
        return *(float*)(&value);
    }

    public static unsafe float UInt32BitsToSingle(uint value)
    {
        return *(float*)(&value);
    }

    public static unsafe uint SingleBitsToUInt32(float value)
    {
        return *(uint*)(&value);
    }

    public override void SetMaterialDirty()
    {
        if (defaultMaterial == null) LoadMaterial();

        base.SetMaterialDirty();

        UpdateInstanciable();
        UpdateShaderDimensions();

        defaultMaterial.SetTexture("_MainTex", mainTexture);
        defaultMaterial.SetFloat("_GraphicBlur", m_graphicBlur);

        defaultMaterial.SetVector("_MaskRect", MaskRect);
        defaultMaterial.SetVector("_MaskOffset", MaskOffset);

        defaultMaterial.SetVector("_CirclePos", CirclePos);
        defaultMaterial.SetVector("_CircleColor", new Color(CircleColor.r, CircleColor.g, CircleColor.b, CircleAlpha));
        defaultMaterial.SetFloat("_CircleAlpha", CircleAlpha);
        defaultMaterial.SetFloat("_CircleRadius", CircleSize);
        var resolvedFillTexture = GetResolvedFillTexture();
        var fillSourceUvRect = GetFillSourceUVRect();

        defaultMaterial.SetTexture("_FillTex", resolvedFillTexture != null ? resolvedFillTexture : Texture2D.whiteTexture);
        defaultMaterial.SetVector("_FillTex_ST", new Vector4(m_fillTiling.x, m_fillTiling.y, m_fillOffset.x, m_fillOffset.y));
        defaultMaterial.SetVector("_FillSourceUVRect", new Vector4(fillSourceUvRect.x, fillSourceUvRect.y, fillSourceUvRect.width, fillSourceUvRect.height));
        defaultMaterial.SetColor("_FillTint", m_fillTint);
        defaultMaterial.SetFloat("_UseFillTexture", m_useFillTexture && resolvedFillTexture != null ? 1f : 0f);
        defaultMaterial.SetFloat("_FillTextureScaleMode", (float)m_fillTextureScaleMode);
        defaultMaterial.SetTexture("_FlowTex", m_flowTexture != null ? m_flowTexture : Texture2D.whiteTexture);
        defaultMaterial.SetVector("_FlowTex_ST", new Vector4(m_flowTiling.x, m_flowTiling.y, m_flowOffset.x, m_flowOffset.y));
        defaultMaterial.SetVector(
            "_FlowParams",
            new Vector4(
                m_flowSpeed.x,
                m_flowSpeed.y,
                Mathf.Clamp(m_flowStrength, 0f, 0.25f),
                m_useFlowTexture && m_flowTexture != null ? 1f : 0f));
    }

    void UpdateInstanciable()
    {
        if (m_material == null) LoadMaterial();

        bool canInstance = mainTexture == null &&
                           MaskRect.z == 0 &&
                           MaskRect.w == 0 &&
                           MaskOffset == default &&
                           m_circleRadius == default &&
                           !(m_useFillTexture && GetResolvedFillTexture() != null) &&
                           !(m_useFlowTexture && m_flowTexture != null);
        bool isInstance = m_material == RectangleRendererShader;

        if (canInstance != isInstance)
        {
            m_material = canInstance ? RectangleRendererShader : Instantiate(RectangleRendererShader);
            SetAllDirty();
        }
    }

    public Texture GetResolvedFillTexture()
    {
        if (m_fillSprite != null && m_fillSprite.texture != null)
        {
            return m_fillSprite.texture;
        }

        return m_fillTexture;
    }

    public Rect GetFillSourceUVRect()
    {
        if (m_fillSprite == null || m_fillSprite.texture == null)
        {
            return new Rect(0f, 0f, 1f, 1f);
        }

        var texture = m_fillSprite.texture;
        var outerUv = DataUtility.GetOuterUV(m_fillSprite);
        var xMin = Mathf.Min(outerUv.x, outerUv.z);
        var yMin = Mathf.Min(outerUv.y, outerUv.w);
        var xMax = Mathf.Max(outerUv.x, outerUv.z);
        var yMax = Mathf.Max(outerUv.y, outerUv.w);
        var uvRect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);

        var insetU = texture.width > 0 ? 0.5f / texture.width : 0f;
        var insetV = texture.height > 0 ? 0.5f / texture.height : 0f;
        var insetWidth = Mathf.Max(0f, uvRect.width - insetU * 2f);
        var insetHeight = Mathf.Max(0f, uvRect.height - insetV * 2f);
        return new Rect(
            uvRect.x + insetU,
            uvRect.y + insetV,
            insetWidth,
            insetHeight);
    }
}
