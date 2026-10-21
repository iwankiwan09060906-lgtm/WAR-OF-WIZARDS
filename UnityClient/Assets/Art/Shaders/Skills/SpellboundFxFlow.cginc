// Eric VFX Studio "URP_AdditiveFlow / URP_AlphaBlendFlow" 의 Built-in 파이프라인 포팅 (같은 프로퍼티 이름).
// 이 프로젝트는 Built-in 렌더 파이프라인이라 원본 URP 셰이더가 그려지지 않는다 →
// SpellboundSkillAssetBuilder가 Eric 머티리얼을 복제해 이 셰이더로 바꿔 쓴다.
// 지원: 메인 텍스처 흐름 · 틴트 · 밝기 · 버텍스 컬러 · UV 왜곡 · Overlay · 디졸브(파티클 알파) · Fresnel · 버텍스 흔들림
// (Soft Particles는 깊이 텍스처가 필요해 생략)

#ifndef SPELLBOUND_FX_FLOW_INCLUDED
#define SPELLBOUND_FX_FLOW_INCLUDED

#include "UnityCG.cginc"

sampler2D _MainTex; float4 _MainTex_ST;
sampler2D _DissolveTex; float4 _DissolveTex_ST;
sampler2D _DistortionMap; float4 _DistortionMap_ST;
sampler2D _OverlayTex; float4 _OverlayTex_ST;
float4 _TintColor, _FlowSpeed, _DistortionSpeed, _FresnelColor, _DissolveEdgeColor, _OverlayColor, _OverlayFlowSpeed;
float _Brightness, _FresnelWidth, _FresnelIntensity, _DistortStrength, _DistortSpeed, _DistortFrequency, _DistortionPower,
      _DissolveAmount, _VertexAlphaRef, _DissolveEdgeWidth, _FresnelType;

struct appdata
{
    float4 vertex : POSITION;
    float2 uv : TEXCOORD0;
    float3 normal : NORMAL;
    fixed4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct v2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float3 viewDirWS : TEXCOORD2;
    fixed4 color : COLOR;
    UNITY_VERTEX_OUTPUT_STEREO
};

v2f vertFlow (appdata v)
{
    v2f o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 wPos = mul(unity_ObjectToWorld, v.vertex).xyz;
    #ifdef _VERTEX_DISTORT_ON
        v.vertex.xyz += v.normal * sin(_Time.y * _DistortSpeed + (wPos.x + wPos.y + wPos.z) * _DistortFrequency) * _DistortStrength;
    #endif
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.uv;
    o.color = v.color;
    o.normalWS = UnityObjectToWorldNormal(v.normal);
    o.viewDirWS = _WorldSpaceCameraPos - wPos;
    return o;
}

// rgb = 색, a = 알파 (호출 쪽에서 블렌딩 방식에 맞게 조합)
float4 fragFlowColor (v2f i)
{
    float2 uvOff = (tex2D(_DistortionMap, i.uv * _DistortionMap_ST.xy + _DistortionMap_ST.zw + _DistortionSpeed.xy * _Time.y).rg * 2.0 - 1.0) * _DistortionPower;
    float4 tex = tex2D(_MainTex, i.uv * _MainTex_ST.xy + _MainTex_ST.zw + _FlowSpeed.xy * _Time.y + uvOff);
    float4 over = tex2D(_OverlayTex, i.uv * _OverlayTex_ST.xy + _OverlayTex_ST.zw + _OverlayFlowSpeed.xy * _Time.y + uvOff) * _OverlayColor;

    float dAmt = _DissolveAmount;
    #ifdef _USEPARTICLEALPHADISSOLVE_ON
        dAmt = saturate(_DissolveAmount + (1.0 - saturate(i.color.a / max(0.001, _VertexAlphaRef))));
    #endif
    float dVal = tex2D(_DissolveTex, i.uv * _DissolveTex_ST.xy + _DissolveTex_ST.zw).r;
    float bAC = step(0.001, tex.a);
    float dMask = step(dAmt, dVal) * bAC;
    float eMask = saturate(step(dAmt - _DissolveEdgeWidth, dVal) - step(dAmt, dVal)) * bAC * saturate(dAmt * 100.0);

    float3 rgb = tex.rgb * _TintColor.rgb * i.color.rgb * over.rgb * _Brightness;
    float alpha = tex.a * _TintColor.a * i.color.a * over.a;

    #ifdef _FRESNEL_ON
        float fPower = max(0.1, 15.1 - _FresnelWidth);
        float fNormal = pow(1.0 - saturate(abs(dot(normalize(i.normalWS), normalize(i.viewDirWS)))), fPower);
        float2 uvD = min(i.uv, 1.0 - i.uv);
        float fEff = lerp(fNormal, pow(saturate(1.0 - min(uvD.x, uvD.y) * 2.0), fPower), step(0.5, _FresnelType));
        #ifdef _FRESNEL_MASK_ON
            rgb *= fEff; alpha *= fEff;
        #endif
        rgb += _FresnelColor.rgb * fEff * _FresnelIntensity;
    #endif

    rgb = rgb * dMask + _DissolveEdgeColor.rgb * eMask;
    return float4(rgb, alpha * max(dMask, eMask));
}

#endif
