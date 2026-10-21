// Eric/URP_AlphaBlendFlow 의 Built-in 포팅 (프로퍼티 이름 동일)
Shader "Spellbound/FxAlphaFlow"
{
    Properties
    {
        [MainTexture] _MainTex("Main Texture", 2D) = "white" {}
        [HDR] _TintColor("Tint Color", Color) = (1, 1, 1, 1)
        _FlowSpeed("MainTex Flow Speed", Vector) = (0, 0, 0, 0)
        _Brightness("Brightness Boost", Float) = 1.0
        [Toggle(_SOFTPARTICLES_ON)] _SoftParticlesEnabled("Enable Soft Particles (unused)", Float) = 0
        _SoftParticlesFadeDistance("Fade Distance", Range(0.01, 10.0)) = 1.0
        _DistortionMap("Distortion Map", 2D) = "white" {}
        _DistortionPower("Distortion Power", Range(0, 1)) = 0
        _DistortionSpeed("Distortion Speed", Vector) = (0, 0, 0, 0)
        [Toggle(_FRESNEL_ON)] _UseFresnel("Enable Fresnel", Float) = 0
        [Enum(Normal Based, 0, UV Based, 1)] _FresnelType("Fresnel Type", Float) = 0
        [HDR] _FresnelColor("Fresnel Color", Color) = (1, 1, 0, 1)
        _FresnelIntensity("Fresnel Intensity", Float) = 5.0
        _FresnelWidth("Fresnel Width", Range(0.1, 15.0)) = 5.0
        [Toggle(_FRESNEL_MASK_ON)] _FresnelMasksTexture("Use Fresnel as Texture Mask", Float) = 0
        [Toggle(_USEPARTICLEALPHADISSOLVE_ON)] _UseAlphaDissolve("Use Alpha as Dissolve", Float) = 0
        _DissolveTex("Dissolve Texture", 2D) = "white" {}
        _DissolveAmount("Dissolve Amount", Range(0, 1)) = 0.0
        _VertexAlphaRef("Alpha Ref", Range(0.01, 1.0)) = 1.0
        [HDR] _DissolveEdgeColor("Dissolve Edge Color", Color) = (1, 0, 0, 1)
        _DissolveEdgeWidth("Dissolve Edge Width", Range(0, 0.2)) = 0.05
        _OverlayTex("Overlay Texture", 2D) = "white" {}
        [HDR] _OverlayColor("Overlay Color", Color) = (1, 1, 1, 1)
        _OverlayFlowSpeed("Overlay Flow Speed", Vector) = (0, 0, 0, 0)
        [Toggle(_VERTEX_DISTORT_ON)] _UseVertexDistort("Enable Vertex Distort", Float) = 0
        _DistortStrength("Vertex Strength", Range(0, 0.5)) = 0.0
        _DistortSpeed("Vertex Speed", Float) = 1.0
        _DistortFrequency("Vertex Frequency", Float) = 2.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vertFlow
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _FRESNEL_ON
            #pragma shader_feature_local _FRESNEL_MASK_ON
            #pragma shader_feature_local _USEPARTICLEALPHADISSOLVE_ON
            #pragma shader_feature_local _VERTEX_DISTORT_ON
            #include "SpellboundFxFlow.cginc"

            fixed4 frag (v2f i) : SV_Target
            {
                float4 c = fragFlowColor(i);
                return float4(c.rgb, saturate(c.a));
            }
            ENDCG
        }
    }
}
