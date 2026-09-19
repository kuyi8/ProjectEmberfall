Shader "Emberfall/VFX/SwordTrail"
{
    Properties
    {
        [HDR] _BaseColor("Base Color", Color) = (0.56, 0.86, 1.0, 0.52)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SwordTrail"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half rootFade = smoothstep(0.02h, 0.23h, input.uv.y);
                half tipFade = 1.0h - smoothstep(0.9h, 1.0h, input.uv.y);
                half bladeMask = saturate(rootFade * (0.45h + 0.55h * tipFade));
                half timeGlow = lerp(0.55h, 1.0h, input.uv.x);
                half4 color = _BaseColor * input.color;
                color.rgb *= timeGlow;
                color.a *= bladeMask;
                return color;
            }
            ENDHLSL
        }
    }
}
