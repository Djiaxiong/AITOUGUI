Shader "UI/Windinator/DrawTextureSDF"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [PerRendererData] _StampTex ("Stamp Texture", 2D) = "gray" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM

            #include "../shared.cginc"

            sampler2D _StampTex;
            float4 _StampTex_TexelSize;
            float4 _StampCenter;
            float4 _StampHalfSize;
            float _StampScaleMode;

            float4 frag (v2f IN) : SV_Target
            {
                float2 position;
                float2 halfSize;

                GetRawRect(IN.texcoord, position, halfSize, 1);

                half4 color = tex2D(_MainTex, IN.texcoord);
                float dist = Decode(color.r);

                float2 localPos = position - _StampCenter.xy;
                localPos = rotate(localPos, _StampCenter.z);

                float2 stampHalfSize = max(_StampHalfSize.xy, float2(0.0001, 0.0001));
                float2 uv = localPos / stampHalfSize * 0.5 + 0.5;
                float2 remappedUV = uv;
                bool outsideFitBounds = false;

                if (_StampScaleMode > 0.5)
                {
                    float rectAspect = stampHalfSize.x / stampHalfSize.y;
                    float texAspect = max(_StampTex_TexelSize.z, 1.0) / max(_StampTex_TexelSize.w, 1.0);
                    float2 scale = float2(1.0, 1.0);

                    if (_StampScaleMode < 1.5)
                    {
                        if (texAspect > rectAspect)
                            scale.y = rectAspect / texAspect;
                        else
                            scale.x = texAspect / rectAspect;

                        remappedUV = (uv - 0.5) / max(scale, float2(0.0001, 0.0001)) + 0.5;
                        outsideFitBounds = remappedUV.x < 0.0 || remappedUV.x > 1.0 || remappedUV.y < 0.0 || remappedUV.y > 1.0;
                    }
                    else
                    {
                        if (texAspect > rectAspect)
                            scale.x = rectAspect / texAspect;
                        else
                            scale.y = texAspect / rectAspect;

                        remappedUV = (uv - 0.5) * scale + 0.5;
                    }
                }

                if (!outsideFitBounds)
                {
                    float stampEncoded = tex2D(_StampTex, remappedUV).r;
                    float stampDist = ((stampEncoded * 2.0) - 1.0) * length(stampHalfSize);
                    dist = AddSDF(stampDist, dist, _Union);
                }

                return float4(Encode(dist), 1, 1, 1);
            }
            ENDCG
        }
    }
}
