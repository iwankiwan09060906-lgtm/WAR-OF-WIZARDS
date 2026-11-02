// 파티클 프리팹 색 바꾸기 — 같은 마법진 이펙트를 스킬 색으로 쓴다 (새로 나오는 입자부터 바뀐다).

using UnityEngine;

namespace SpellboundVR.Presentation
{
    public static class VfxTint
    {
        public static void Apply(GameObject effect, Color color)
        {
            if (effect == null) return;
            foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.startColor = color;
            }
        }
    }
}
