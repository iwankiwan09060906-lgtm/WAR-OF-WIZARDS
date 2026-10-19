using UnityEngine;
using WarOfWizards.Core;

namespace WarOfWizards.Gesture
{
    /// <summary>
    /// 모션(제스처)을 시각적으로 확인하기 위한 드로잉 매니저
    /// 핀치 상태에서 손가락이 움직이는 궤적을 TrailRenderer 또는 LineRenderer로 그려줍니다.
    /// </summary>
    [RequireComponent(typeof(TrailRenderer))]
    public class GestureVisualizer : MonoBehaviour
    {
        public PlayerCastingManager castingManager;
        private TrailRenderer trailRenderer;

        private void Awake()
        {
            trailRenderer = GetComponent<TrailRenderer>();
            
            // 시각적 트레일 기본 세팅 (불꽃 마법처럼 보이게)
            trailRenderer.time = 2.0f; // 2초간 자국 남김
            trailRenderer.startWidth = 0.05f;
            trailRenderer.endWidth = 0.01f;
            trailRenderer.emitting = false;
        }

        private void Update()
        {
            if (castingManager == null) return;

            // 시전 상태가 'Drawing(그리는 중)'일 때만 궤적을 생성
            if (castingManager.CurrentState == CastingState.Drawing)
            {
                trailRenderer.emitting = true;
            }
            else
            {
                // 그리기가 끝나거나 취소되면 궤적 생성 중단
                trailRenderer.emitting = false;
                
                // Idle 상태로 돌아가면 궤적 초기화 (다음 마법을 위해)
                if (castingManager.CurrentState == CastingState.Idle || castingManager.CurrentState == CastingState.Cast)
                {
                    trailRenderer.Clear();
                }
            }
        }
    }
}
