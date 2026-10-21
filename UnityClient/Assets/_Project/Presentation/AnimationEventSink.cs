// 에셋 팩 애니메이션 클립에 들어 있는 이벤트(예: Nora Squad Archer 공격의 OnHit)를 받아 버리는 빈 수신기.
// 수신기가 없으면 Unity가 "AnimationEvent has no receiver" 오류를 내고, Error Pause가 켜진 에디터는 플레이가 멈춘다.
// 판정은 서버가 하므로 여기서는 아무것도 하지 않는다 (SpellboundSkillAssetBuilder가 Animator 옆에 붙인다).

using UnityEngine;

namespace SpellboundVR.Presentation
{
    public sealed class AnimationEventSink : MonoBehaviour
    {
        public void OnHit() { }
        public void Hit() { }
        public void Shoot() { }
        public void FootL() { }
        public void FootR() { }
        public void Footstep() { }
    }
}
