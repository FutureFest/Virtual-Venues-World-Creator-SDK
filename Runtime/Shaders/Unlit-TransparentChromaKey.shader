Shader "Custom/UnlitChromaKeyURP"
{
    Properties
    {
        _BaseMap("Base (RGB + Alpha)", 2D) = "white" {}
        _Color("Tint Color", Color) = (1,1,1,1)

        _KeyColor("Key Color", Color) = (0,1,0,1)
        _Similarity("Chroma Similarity", Range(0,1)) = 0.3
        _Smoothness("Chroma Smoothness", Range(0,1)) = 0.1
        _LumaThreshold("Luma Threshold", Range(0,1)) = 0.1
        _LumaSmooth("Luma Smooth", Range(0,1)) = 0.1
        _Despill("Despill Strength", Range(0,1)) = 1
        _EdgePower("Edge Darkening Power", Range(1,8)) = 3.0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        ZTest LEqual

        Pass
        { 
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;

            float4 _Color;
            float4 _KeyColor;
            float _Similarity;
            float _Smoothness;
            float _LumaThreshold;
            float _LumaSmooth;
            float _Despill;
            float _EdgePower;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  fogFactor  : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            // RGB → Luma + Chroma
            void RGBToYCbCr(float3 rgb, out float Y, out float Cb, out float Cr)
            {
                Y  = dot(rgb, float3(0.299, 0.587, 0.114));
                Cb = rgb.b - Y;
                Cr = rgb.r - Y;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                col.rgb *= _Color.rgb;

                float Ys, Cbs, Crs;
                RGBToYCbCr(col.rgb, Ys, Cbs, Crs);

                float Yk, Cbk, Crk;
                RGBToYCbCr(_KeyColor.rgb, Yk, Cbk, Crk);

                float distChroma = length(float2(Cbs - Cbk, Crs - Crk));
                float distLuma   = abs(Ys - Yk);

                float alpha = col.a;
                if (distChroma < _Similarity && distLuma < _LumaThreshold)
                {
                    float aChroma = saturate((distChroma - (_Similarity - _Smoothness)) / _Smoothness);
                    float aLuma   = saturate((distLuma   - (_LumaThreshold - _LumaSmooth)) / _LumaSmooth);
                    alpha = min(alpha, max(aChroma, aLuma));
                }
                col.a = alpha;


                float fadeT = pow(alpha, _EdgePower);
                col.rgb = lerp(float3(0,0,0), col.rgb, fadeT);

                float avgRB = (col.r + col.b) * 0.5;
                if (col.g > avgRB)
                    col.g = lerp(col.g, avgRB, _Despill);

                col.rgb = MixFog(col.rgb, IN.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }
}
