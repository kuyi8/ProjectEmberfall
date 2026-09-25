Shader "Emberfall/M6ExecutionCrescent"
{
    Properties
    {
        [HDR] _EdgeColor ("Ember edge", Color) = (1.15,0.12,0.015,1)
        [HDR] _CoreColor ("Gold core, never white", Color) = (1.8,0.65,0.055,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            // Alpha compositing preserves the orange silhouette over pale bones;
            // additive white would bleach both into the same post-tonemap value.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _EdgeColor;
                half4 _CoreColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv; output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float band = saturate(1 - abs(input.uv.y * 2 - 1));
                float mask = smoothstep(0, .12, band);
                float core = pow(band, 3);
                return half4(lerp(_EdgeColor.rgb, _CoreColor.rgb, core) * input.color.rgb,
                    mask * input.color.a);
            }
            ENDHLSL
        }
    }
}
