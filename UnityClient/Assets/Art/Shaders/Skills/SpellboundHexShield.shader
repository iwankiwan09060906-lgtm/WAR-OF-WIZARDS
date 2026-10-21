// ShieldEffect "ShieldEffect/HexShield" (URP 전용) 의 Built-in 포팅 — 프로퍼티 이름 동일.
// 육각 격자 + Fresnel 테두리 + 스크롤. 교차부 발광(깊이 텍스처 필요)은 _Pulse(시간 맥동)로 대체했다.
// S08 방어 쉴드(구) · S09 타워 버프(상자) · H04 반사 결계(판) 에 색만 바꿔 쓴다.

Shader "Spellbound/HexShield"
{
    Properties
    {
        [HDR] _BaseColor("Base Color", Color) = (0, 0.5, 1, 1)
        _GlowIntensity("Glow Intensity", Float) = 2
        _FresnelPower("Fresnel Power", Range(0.1, 10)) = 3
        _HexScale("Hex Scale", Float) = 10
        _LineWidth("Line Width", Range(0, 0.5)) = 0.05
        _ScrollSpeed("Scroll Speed", Float) = 0.1
        _IntersectionRange("Intersection Range (unused)", Float) = 0.5
        _Fill("Inner Fill", Range(0, 1)) = 0.08
        _Pulse("Pulse", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _BaseColor;
            float _HexScale, _LineWidth, _GlowIntensity, _FresnelPower, _ScrollSpeed, _Fill, _Pulse;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float hexDist(float2 p)
            {
                p = abs(p);
                float r = dot(p, normalize(float2(1, 1.73205)));
                return max(r, p.x);
            }

            float2 myMod(float2 x, float2 y) { return x - y * floor(x / y); }

            float hexGrid(float2 uv)
            {
                float2 r = float2(1, 1.73205);
                float2 h = r * 0.5;
                float2 a = myMod(uv, r) - h;
                float2 b = myMod(uv - h, r) - h;
                float2 g = dot(a, a) < dot(b, b) ? a : b;
                return hexDist(g);
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normalWS = UnityObjectToWorldNormal(v.normal);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float fresnel = pow(1.0 - saturate(abs(dot(normalize(i.normalWS), viewDir))), _FresnelPower);

                float2 movingUV = i.uv * _HexScale;
                movingUV.y += _Time.y * _ScrollSpeed;
                float gridLines = smoothstep(0.5 - _LineWidth, 0.5 - _LineWidth + 0.02, hexGrid(movingUV));

                float pulse = 1.0 + _Pulse * sin(_Time.y * 4.0 + i.uv.y * 6.2831);
                float alpha = saturate(gridLines * fresnel + gridLines * 0.2 + fresnel * 0.35 + _Fill);
                float3 col = _BaseColor.rgb * alpha * _GlowIntensity * pulse;
                return float4(col, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
