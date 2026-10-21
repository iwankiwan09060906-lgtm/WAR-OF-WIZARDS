Shader "ShieldEffect/HexShield"
{
    Properties
    {
        [Header(Appearance)]
        [HDR] _BaseColor("Base Color", Color) = (0, 0.5, 1, 1)
        _GlowIntensity("Glow Intensity", Float) = 2
        _FresnelPower("Fresnel Power", Range(0.1, 10)) = 3

        [Header(Hex Grid)]
        _HexScale("Hex Scale", Float) = 10
        _LineWidth("Line Width", Range(0, 0.5)) = 0.05
        _ScrollSpeed("Scroll Speed", Float) = 0.1

        [Header(Intersection)]
        [Tooltip(Requires URP Depth Texture enabled)]
        _IntersectionRange("Intersection Range", Float) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _HexScale;
                float _LineWidth;
                float _GlowIntensity;
                float _FresnelPower;
                float _IntersectionRange;
                float _ScrollSpeed;
            CBUFFER_END

            float hexDist(float2 p)
            {
                p = abs(p);
                float r = dot(p, normalize(float2(1, 1.73205)));
                return max(r, p.x);
            }

            float2 myMod(float2 x, float2 y)
            {
                return x - y * floor(x / y);
            }

            float hexGrid(float2 uv)
            {
                float2 r = float2(1, 1.73205);
                float2 h = r * 0.5;
                float2 a = myMod(uv, r) - h;
                float2 b = myMod(uv - h, r) - h;
                float2 g = dot(a, a) < dot(b, b) ? a : b;
                return hexDist(g);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float3 viewDir = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 normal = normalize(input.normalWS);

                // Fresnel
                float fresnel = pow(1.0 - saturate(dot(normal, viewDir)), _FresnelPower);

                // Hex Grid
                float2 movingUV = input.uv * _HexScale;
                movingUV.y += _Time.y * _ScrollSpeed;
                float hDist = hexGrid(movingUV);
                float gridLines = smoothstep(0.5 - _LineWidth, 0.5 - _LineWidth + 0.02, hDist);

                // Intersection Glow
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float depth = SampleSceneDepth(screenUV);
                float eyeDepth = LinearEyeDepth(depth, _ZBufferParams);
                float surfaceDepth = input.screenPos.w;
                float intersection = 1.0 - saturate((eyeDepth - surfaceDepth) / _IntersectionRange);
                intersection = pow(intersection, 2.0);

                // Combine
                float alpha = saturate(gridLines * fresnel + intersection + gridLines * 0.2);
                float3 finalColor = _BaseColor.rgb * alpha * _GlowIntensity;

                return float4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
