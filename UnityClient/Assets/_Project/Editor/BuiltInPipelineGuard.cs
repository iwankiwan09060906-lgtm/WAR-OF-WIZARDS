// 이 프로젝트는 Built-in 렌더러다. URP 패키지가 GraphicsSettings 에 URP 전역 설정을 등록해 두면 (에디터가 켜져 있으면 지워도 다시 저장된다)
// 빌드가 쓰지도 않는 "Universal Render Pipeline/Lit" 셰이더 변형 362억 개를 훑느라 30분 ~ 몇 시간 걸린다.
// 빌드 직전에 그 등록을 푼다 — 정말 URP 를 쓰게 되면(파이프라인 에셋이 지정되면) 건드리지 않는다.

using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpellboundVR.EditorTools
{
    public sealed class BuiltInPipelineGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (GraphicsSettings.defaultRenderPipeline != null || QualitySettings.renderPipeline != null) return;
            if (GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>() == null) return;
            GraphicsSettings.UnregisterRenderPipelineSettings<UniversalRenderPipeline>();
            Debug.Log("[BuiltInPipelineGuard] Built-in 렌더러 빌드 — URP 전역 설정 등록을 풀었다 (URP Lit 셰이더 변형 362억 개를 빌드에서 뺀다)");
        }
    }
}
