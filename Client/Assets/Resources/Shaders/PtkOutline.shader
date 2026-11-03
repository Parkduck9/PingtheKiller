// Inverted-hull outline (HUD_SPEC 6.2): back faces of a slightly enlarged copy of the character, unlit, one flat colour.
// The hull is scaled per frame so the visible rim is about 1 physical pixel wide.
Shader "PingTheKiller/Outline"
{
    Properties
    {
        [MainColor] _BaseColor ("Color", Color) = (1, 0.69, 0.125, 1)
        _OutlinePixels ("Outline pixels", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }
            Cull Front
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _OutlinePixels;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalVS = TransformWorldToViewDir(TransformObjectToWorldNormal(input.normalOS));
                float2 direction = normalVS.xy;
                direction /= max(length(direction), 0.0001);
                output.positionCS.xy += direction * (2 * _OutlinePixels / _ScaledScreenParams.xy) * output.positionCS.w;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return _BaseColor; }
            ENDHLSL
        }
    }
}
