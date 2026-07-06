#include "UnityCG.cginc"
#include "UnityUI.cginc"

#pragma vertex vert
#pragma fragment frag
#pragma target 2.0

#pragma multi_compile_local _ UNITY_UI_CLIP_RECT
#pragma multi_compile_local _ UNITY_UI_ALPHACLIP

struct appdata
{
    float4 vertex   : POSITION;
    float4 color    : COLOR;
    float4 texcoord : TEXCOORD0;
    float4 uv1 : TEXCOORD1;
    float4 uv2 : TEXCOORD2;
    float4 uv3 : TEXCOORD3;
    float4 tangent : TANGENT;
    float3 normals : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct v2f
{
    float4 vertex           : SV_POSITION;
    float4 color            : COLOR;
    float2 texcoord         : TEXCOORD0;
    float4 uv1 : TEXCOORD1;
    float4 uv2 : TEXCOORD2;
    float4 uv3 : TEXCOORD3;
    float4 uv4 : TEXCOORD4;
    float4 tangent : TANGENT;
    float3 normals : NORMAL;
    UNITY_VERTEX_OUTPUT_STEREO
};

sampler2D _MainTex;
float4 _MainTex_ST;
sampler2D _FillTex;
float4 _FillTex_ST;
float4 _FillTex_TexelSize;
sampler2D _FlowTex;
float4 _FlowTex_ST;

v2f vert (appdata v)
{
    v2f OUT;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

    OUT.vertex = UnityObjectToClipPos(v.vertex);

    OUT.uv4.xy = v.texcoord.zw;
    OUT.texcoord = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
    OUT.uv2 = v.uv2;
    OUT.uv3 = v.uv3;
    OUT.tangent = v.tangent;
    OUT.uv1 = v.uv1;
    OUT.color = v.color;
    OUT.normals = float3(v.normals.x, v.vertex.y, v.vertex.x);
    return OUT;
}

fixed4 _CircleColor;
fixed4 _TextureSampleAdd;
float4 _ClipRect;
float4 _FillTint;
float4 _FlowParams;
float _UseFillTexture;
float _FillTextureScaleMode;
float4 _FillSourceUVRect;
float _UseLocalFillMapping;
float4 _LocalFillRect;
float4 _LocalFillUVRect;

fixed4 _Roundness;
float _Padding;
float _OutlineSize;
float _GraphicBlur;
fixed4 _OutlineColor;

float _ShadowSize;
float _ShadowBlur;
float _ShadowPow;
fixed4 _ShadowColor;

float _CircleRadius;
float2 _CirclePos;
float _CircleAlpha;
float4 _MaskRect;
float4 _MaskOffset;

float _Alpha;
float2 _Size;

float _Union;
int _Operation;

float2 rotate(float2 pos, float angle)
{
    float sinX = sin(angle);
    float cosX = cos(angle);
    float2x2 rotationMatrix = float2x2(cosX, -sinX, sinX, cosX);
    return mul(pos, rotationMatrix);
}

inline float Encode(float dist)
{
    float maxLen = length(_Size * 0.5 + _Padding);
    return ((dist / maxLen) + 1) * 0.5;
}

inline float Decode(float value)
{
    float maxLen = length(_Size * 0.5 + _Padding);
    return ((value * 2) - 1) * maxLen;
}

inline float opSmoothUnion( float d1, float d2, float k ) {
    float h = clamp( 0.5 + 0.5*(d2-d1)/k, 0.0, 1.0 );
    return lerp( d2, d1, h ) - k*h*(1.0-h); 
}


inline float opSmoothSubtraction( float d1, float d2, float k ) {
    float h = clamp( 0.5 - 0.5*(d2+d1)/k, 0.0, 1.0 );
    return lerp( d2, -d1, h ) + k*h*(1.0-h);
}

inline float opSmoothIntersection( float d1, float d2, float k ) {
    float h = clamp( 0.5 - 0.5*(d2-d1)/k, 0.0, 1.0 );
    return lerp( d2, d1, h ) + k*h*(1.0-h); 
}

float AddSDF(float sdf, float old) {
    if (_Operation == 0) {
        return opSmoothUnion(sdf, old, _Union);
    } else if (_Operation == 1) {
        return opSmoothSubtraction(sdf, old, _Union);
    } else {
        return opSmoothIntersection(sdf, old, _Union);
    }
}

float AddSDF(float sdf, float old, float k) {
    if (_Operation == 0) {
        return opSmoothUnion(sdf, old, k);
    } else if (_Operation == 1) {
        return opSmoothSubtraction(sdf, old, k);
    } else {
        return opSmoothIntersection(sdf, old, k);
    }
}

void GetRawRect(float2 uv, out float2 position, out float2 halfSize, float extra)
{
    float2 normalizedPadding = float2(_Padding / _Size.x, _Padding / _Size.y);

    halfSize = (_Size + extra) * 0.5;


    // Transform UV based on padding so image stays inside its container
    uv = uv * (1 + normalizedPadding * 2) - normalizedPadding;

    // For simplicity, convert UV to pixel coordinates
    position = (uv - 0.5) * _Size - 0.5 + 0.5;
}

void GetRect(float2 uv, out float2 position, out float2 halfSize, float extra)
{
    GetRawRect(uv, position, halfSize, extra);

    float2 pos = ((position + 1) + halfSize - 0.5);

    bool shouldMask = ((_MaskRect.z > 0 && _MaskRect.w > 0 &&
        position.x >= _MaskRect.x && position.x <= _MaskRect.x + _MaskRect.z &&
        position.y >= _MaskRect.y && position.y <= _MaskRect.y + _MaskRect.w) ||
        pos.x < _MaskOffset.x - _Padding + 1.5 || pos.x > _Size.x + _MaskOffset.z + _Padding - 0.5 ||
        pos.y < _MaskOffset.y - _Padding + 1.5 || pos.y > _Size.y + _MaskOffset.w + _Padding - 0.5);

    _Alpha = min(_Alpha, 1 - shouldMask);
}

#define LOSE 20
#define WIN (255.0f - (LOSE * 2))
#define WIN_2 (127.0f - (LOSE * 2))

float2 DecodeVector2(float value)
{
    float x = floor(value) / 16384;
    float y = floor(value) % 16384.0;

    return float2((x / 4), (y / 4));
}

float2 DecodeVector2(float value, uint xmax)
{
    float x = (uint)(value) / xmax;
    float y = (uint)(value) % xmax;

    return float2((x / 4), (y / 4));
}

float4 DecodeColor(float value)
{
    uint v = asuint(value);

    float r = v & 0xFF;
    float g = (v >> 8) & 0xFF;
    float b = (v >> 16) & 0xFF;
    float a = (v >> 24) & 0xFF;

    return float4(
        (r - LOSE) / WIN,
        (g - LOSE) / WIN,
        (b - LOSE) / WIN, 
        (a - LOSE) / WIN_2
    ); 
}

float Encode01(float4 c)
{
    float max = 32768;

    uint r = (uint)(c.r * 31);
    uint g = (uint)(c.g * 31) << 5;
    uint b = (uint)(c.b * 15) << 10;

    uint res = (r | g | b);

    return (float)res / max;
}

float4 Decode01(float c)
{
    float max = 32768;

    c = c * max;

    uint color = (uint)c;

    // 0x1F
    float r = color & 0x1F;
    float g = (color >> 5) & 0x1F;
    float b = (color >> 10) & 0x1F;

    return float4(r / 31.0f, g / 31.0f, b / 15, 1);
}

float4 DecodeCoords(float value)
{
    int v = asint(value);

    float r = v & 0xFF;
    float g = (v >> 8) & 0xFF;
    float b = (v >> 16) & 0xFF;
    float a = (v >> 24) & 0xFF;

    return float4(r, g, b, a);
}

float _EmbossDirection;
float _EmbossStrength;
float _EmbossBlurTop;
float _EmbossBlurBottom;
float _EmbossSize;
float _EmbossDistance;

float4 _EmbossHColor;
float4 _EmbossLColor;
float _EmbossHPower;
float _EmbossLPower;

void LoadData(v2f v, out float2 worldPos)
{
    _Padding = v.uv4.y;

    float4 uv2 = v.uv2;
    float4 uv1 = v.uv1;
    float4 uv3Data = DecodeColor(v.uv3.w);

    _Size = uv2.xy;

    float4 emData = DecodeColor(v.normals.x);
    float4 emColorData = DecodeColor(uv2.w);

    _EmbossSize = uv1.z;
    _EmbossDistance = uv2.z;
    _EmbossDirection = emData.r * 6.28318530;
    _EmbossStrength = emData.a;
    _EmbossHColor = float4(emColorData.rgb, 1);
    _EmbossHPower = emColorData.a;

    float4 emLColorData = DecodeColor(v.uv4.x);

    _EmbossLColor = float4(emLColorData.rgb, 1);
    _EmbossLPower = emLColorData.a;

    _EmbossBlurTop = ((emData.y * 2) - 1) * _EmbossSize;
    _EmbossBlurBottom = ((emData.z * 2) - 1) * _EmbossSize;

    float2 padBlur = DecodeVector2(uv1.x);
    float2 outCirc = DecodeVector2(uv1.w);

    _ShadowSize = uv1.x;
    _ShadowBlur = uv1.y;
    _ShadowPow = 1;
    _ShadowColor = DecodeColor(v.uv3.y);

    _OutlineColor = DecodeColor(v.uv3.x);
    _OutlineSize = uv1.w;
    _Alpha = uv3Data.y;

    /*_CircleColor = float4(colDecoded.xyz, 1);
    _CircleAlpha = _CircleColor.a;
    _CirclePos = v.uv1.yz;
    _CircleRadius = uv2.z;*/

    worldPos = float2(v.normals.z, uv1.z);
}

float2 ResolveFillBaseUV(float2 position)
{
    if (_UseLocalFillMapping > 0.5 && _LocalFillRect.z > 0.0001 && _LocalFillRect.w > 0.0001)
    {
        float2 localPos = position - _LocalFillRect.xy;
        return localPos / max(_LocalFillRect.zw, float2(0.0001, 0.0001)) + 0.5;
    }

    return (position + _Size.xy * 0.5) / max(_Size.xy, float2(0.0001, 0.0001));
}

float2 ResolveFillTextureUV(float2 remappedUV)
{
    float2 sourceUV = remappedUV;

    if (_UseLocalFillMapping > 0.5)
    {
        sourceUV = _LocalFillUVRect.xy + remappedUV * _LocalFillUVRect.zw;
    }

    return _FillSourceUVRect.xy + sourceUV * _FillSourceUVRect.zw;
}

float2 ResolveFillReferenceSize()
{
    if (_UseLocalFillMapping > 0.5 && _LocalFillRect.z > 0.0001 && _LocalFillRect.w > 0.0001)
    {
        return _LocalFillRect.zw;
    }

    return _Size.xy;
}

float4 ResolveAlphaSafeSample(float4 sampleColor)
{
    if (sampleColor.a <= 0.0001)
    {
        sampleColor.rgb = 0;
    }

    return sampleColor;
}

fixed4 fragFunctionRaw(float2 uv, float2 worldPosition, float4 color, float dist, float2 position, float2 halfSize, float2 normal)
{
    float4 effects;

    float embossDist = dist + _EmbossDistance;
    float embossSizeDist = dist + _EmbossDistance + _EmbossSize;

    float outlineDist = dist - _OutlineSize;
    float shadowDist = dist - _ShadowSize;

    // Let shape edges follow GraphicBlur, but keep shadow AA independent so soft shadows
    // do not inherit the same sharpening/softening bias as the silhouette.
    float shapeAaScale = max(0.1f, 1.0f + _GraphicBlur);
    float shadowAaScale = 1.0f;
    float delta = fwidth(dist * 0.5) * shapeAaScale;
    float outlineDelta = fwidth(outlineDist) * shapeAaScale;
    float shadowDelta = fwidth(shadowDist) * shadowAaScale;
    float embossDelta = fwidth(embossDist) * shapeAaScale;
    float embossSizeDelta = fwidth(embossSizeDist) * shapeAaScale;

    // Calculate the different masks based on the SDF
    float graphicAlpha = smoothstep(delta, -delta, dist);
    float outlineAlpha = smoothstep(outlineDelta, -outlineDelta, outlineDist);
    float shadowAlpha = smoothstep(shadowDelta, -shadowDelta - _ShadowBlur, shadowDist);

    float embossStartAlpha = smoothstep(embossDelta - min(0, _EmbossBlurTop), -embossDelta - max(0, _EmbossBlurTop), embossDist);
    float embossEndAlpha = smoothstep(embossSizeDelta - min(0, _EmbossBlurBottom), -embossSizeDelta - max(0, _EmbossBlurBottom), embossSizeDist);

    float2 baseUV = ResolveFillBaseUV(position);
    float4 graphic = float4(color.rgb, color.a);
    if (_UseFillTexture > 0.5)
    {
        float2 remappedUV = baseUV;
        float aspectMask = 1.0;
        if (_FillTextureScaleMode > 0.5)
        {
            float2 fillReferenceSize = ResolveFillReferenceSize();
            float rectAspect = max(fillReferenceSize.x, 0.0001) / max(fillReferenceSize.y, 0.0001);
            float texAspect = max(_FillTex_TexelSize.z, 1.0) / max(_FillTex_TexelSize.w, 1.0);
            float2 scale = float2(1.0, 1.0);

                    if (_FillTextureScaleMode < 1.5)
                    {
                        if (texAspect > rectAspect)
                            scale.y = rectAspect / texAspect;
                        else
                            scale.x = texAspect / rectAspect;

                        remappedUV = (baseUV - 0.5) / max(scale, float2(0.0001, 0.0001)) + 0.5;
                        aspectMask = step(0.0, remappedUV.x) * step(remappedUV.x, 1.0) * step(0.0, remappedUV.y) * step(remappedUV.y, 1.0);
                    }
                    else
                    {
                        if (texAspect > rectAspect)
                            scale.x = rectAspect / texAspect;
                        else
                            scale.y = texAspect / rectAspect;

                        remappedUV = (baseUV - 0.5) * scale + 0.5;
                    }
                }

        float2 fillBaseUV = ResolveFillTextureUV(remappedUV);
        float2 fillUV = fillBaseUV * _FillTex_ST.xy + _FillTex_ST.zw;
        if (_FlowParams.w > 0.5)
        {
            float2 flowUV = remappedUV * _FlowTex_ST.xy + _FlowTex_ST.zw + _Time.y * _FlowParams.xy;
            float2 flow = tex2D(_FlowTex, flowUV).rg * 2.0 - 1.0;
            fillUV += flow * _FlowParams.z;
        }

        float4 fillColor = ResolveAlphaSafeSample(tex2D(_FillTex, fillUV)) * _FillTint;
        fillColor.a *= aspectMask;
        fillColor.a *= color.a;
        graphic = fillColor;
    }

    float4 outline = float4(_OutlineColor.rgb, _OutlineColor.a);
    float4 shadow = float4(_ShadowColor.rgb, _ShadowColor.a);

    float circleSDF = distance(position, _CirclePos) - _CircleRadius;
    float circleDelta = fwidth(circleSDF) * shapeAaScale;
    float circleAASDF = smoothstep(circleDelta, -circleDelta, circleSDF);

    float light = dot(normal, rotate(float2(0, 1), _EmbossDirection));

    if (light > 0)
         light *= _EmbossHPower;
    else light *= _EmbossLPower;

    light = sign(light) * pow(abs(light), (_EmbossStrength * 50) + 1);


    float4 lightTint = float4(_EmbossHColor.r, _EmbossHColor.g, _EmbossHColor.b, 1);
    float4 shadowTint = float4(_EmbossLColor.r, _EmbossLColor.g, _EmbossLColor.b, 1);
    float4 lightColor = lerp(graphic, (light > 0 ? lightTint : shadowTint), abs(light));

    graphic = lerp(graphic, lightColor, _EmbossSize > 0 ? (embossStartAlpha - embossEndAlpha) : 0);
    graphic = lerp(graphic, float4(_CircleColor.rgb, _CircleAlpha), _CircleRadius > 0 ? circleAASDF * _CircleAlpha : 0);

    effects = lerp(graphic, outline, _OutlineSize > 0 ? 1 - graphicAlpha : 0);
    effects = lerp(effects, shadow, _ShadowSize > _OutlineSize ? 1 - outlineAlpha : 0);

    effects.a *= max(graphicAlpha, max(outlineAlpha, shadowAlpha));
    effects.a *= _Alpha;

    // Unity stuff
    #ifdef UNITY_UI_CLIP_RECT
    effects.a *= UnityGet2DClipping(worldPosition.xy, _ClipRect);
    #endif

    #ifdef UNITY_UI_ALPHACLIP
    clip (effects.a - 0.001);
    #endif

    return effects;
}

fixed4 fragFunction(float2 uv, float2 worldPosition, float4 color, float dist, float2 position, float2 halfSize, float2 normal)
{
    float2 textUV = (position + _Size.xy * 0.5) / _Size.xy;
    float4 textCol = tex2D(_MainTex, textUV);

    return fragFunctionRaw(uv, worldPosition, color * textCol, dist, position, halfSize, normal);
}
