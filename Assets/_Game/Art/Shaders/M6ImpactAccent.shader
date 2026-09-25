Shader "Emberfall/M6ImpactAccent"
{
    Properties { _Arc ("Arc ribbon (otherwise spark)", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Arc;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.uv=input.uv; output.color=input.color;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float band = saturate(1-abs(input.uv.y*2-1));
                float diamond = saturate(1-abs(input.uv.x*2-1)-abs(input.uv.y*2-1));
                float mask = lerp(diamond, band, _Arc);
                float core = pow(mask, 5);
                return half4(lerp(input.color.rgb, half3(2.4,2.2,1.6),core), input.color.a*mask);
            }
            ENDHLSL
        }
    }
}
