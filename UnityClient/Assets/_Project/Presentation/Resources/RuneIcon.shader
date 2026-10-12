// 손 옆 룬 아이콘 (RuneReadyDisplay) — 아이콘 PNG는 투명 배경 위 검은 선이라 어두운 전장에서 안 보인다.
//   선(알파) = _Color, 그 뒤에 원형 반투명 받침(_Backdrop)을 깐다. Resources 폴더에 있어 빌드에 항상 포함된다.
//   Quest 싱글 패스 인스턴싱(스테레오) 대응.
Shader "Spellbound/RuneIcon"
{
    Properties
    {
        _MainTex ("Icon", 2D) = "white" {}
        _Color ("Line Color", Color) = (1, 1, 1, 1)
        _Backdrop ("Backdrop", Color) = (0.05, 0.04, 0.08, 0.55)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _Backdrop;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed lineA = tex2D(_MainTex, i.uv).a * _Color.a;
                float d = distance(i.uv, float2(0.5, 0.5));
                fixed backA = _Backdrop.a * (1 - smoothstep(0.46, 0.5, d));
                fixed a = lineA + backA * (1 - lineA);
                fixed3 rgb = (_Color.rgb * lineA + _Backdrop.rgb * backA * (1 - lineA)) / max(a, 1e-4);
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
