Shader "Emberfall/BoundaryRuneBarrier"
{
    Properties { [MainColor] _BaseColor("Barrier color and authority-controlled opacity",Color)=(.08,.65,.8,.62) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // Authored blockers are thin closed cubes. Only the camera-facing side is needed;
            // drawing their rear faces duplicates the rune pattern through the transparent face.
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            V Vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; return o; }
            half4 Frag(V i):SV_Target
            {
                float edge = min(min(i.uv.x,1-i.uv.x),min(i.uv.y,1-i.uv.y));
                float border = 1-smoothstep(.022,.032,edge);
                float2 diamond = abs(frac(i.uv*float2(5,3))-.5);
                float rune = 1-smoothstep(.018,.035,abs(diamond.x+diamond.y-.28));
                float stripe = 1-smoothstep(.008,.016,abs(i.uv.y-.5));
                float pattern = max(border,max(rune*.65,stripe*.4));
                return half4(_BaseColor.rgb * lerp(.35,2.0,pattern),_BaseColor.a*lerp(.24,1,pattern));
            }
            ENDHLSL
        }
    }
}
