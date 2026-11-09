// H01 프리즈 얼음 덮개 (VFXPlayer) — 시전자 자리(_Origin)에서 반경 _Radius 까지 부드러운 경계로 덮는다.
//   · 덮개 판(레인 · 발판 자리)은 처음부터 다 깔고 반경만 늘리므로 칸 단위로 툭툭 켜지지 않는다
//   · 겹친 판(레인 교차점)이 두 번 칠해져 진해지지 않도록 스텐실로 한 번만 칠한다
//   Resources 폴더에 있어 빌드에 항상 포함된다. Quest 싱글 패스 인스턴싱(스테레오) 대응.
Shader "Spellbound/FreezeCover"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.65, 1, 0.55)
        _Origin ("Origin (world)", Vector) = (0, 0, 0, 0)
        _Radius ("Radius (m)", Float) = 0
        _Softness ("Edge Softness (m)", Float) = 4
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        Stencil
        {
            Ref 71
            Comp NotEqual
            Pass Replace
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            float4 _Origin;
            float _Radius;
            float _Softness;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float d = distance(i.world.xz, _Origin.xz);
                float a = _Color.a * saturate((_Radius - d) / max(_Softness, 0.01));
                clip(a - 0.002); // 아직 안 덮인 곳은 스텐실도 남기지 않는다
                return fixed4(_Color.rgb, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
