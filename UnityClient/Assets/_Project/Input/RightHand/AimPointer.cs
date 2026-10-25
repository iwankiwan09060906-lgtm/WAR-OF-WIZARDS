// §6.2 Aiming — 포인터 표시, 1초 카운트
//   · 포인터가 열 경계를 넘나들어도 1초 타이머는 리셋하지 않는다 (§6.3).
//   · 조준 포인터는 본인에게만 보인다 (로컬 표시만, 네트워크 전송 없음).

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Presentation;
using SpellboundVR.Spells;
using SpellboundVR.Spells.Behaviors;
using UnityEngine;

namespace SpellboundVR.Input
{
    public sealed class AimPointer : MonoBehaviour
    {
        public AimMarkerView markerView;

        private ISpellInputSource _input;
        private ClientSession _session;
        private SpellDefinition _spell;
        private AimResult _last;

        public bool IsActive { get; private set; }

        public AimResult Current => _last;

        public void Initialize(ClientSession session, ISpellInputSource input)
        {
            _session = session;
            _input = input;
        }

        public void Begin(SpellDefinition spell)
        {
            _spell = spell;
            IsActive = true;
            _last = default;
            if (markerView != null)
            {
                markerView.Show(spell != null ? spell.ThemeColor : Color.white, spell != null && spell.Targeting == SpellTargeting.Area ? spell.Radius : 0f);
                var arena = _session.Arena;
                var behavior = spell != null ? spell.Behavior : SpellBehaviorType.None;
                if (behavior == SpellBehaviorType.PoisonField)
                    markerView.SetShape(new Vector2(arena.ColumnSpacing * 1.5f, spell.FieldHalfDepth), true);                // 3열 전체 띠
                else if (behavior == SpellBehaviorType.RageField)
                    markerView.SetShape(new Vector2(arena.ColumnSpacing * 1.5f, RageRoutine.HalfDepth(spell, arena)), true); // 3열 × 타워~넥서스 폭
                else if (behavior == SpellBehaviorType.ArrowVolley)
                    markerView.SetShape(new Vector2(arena.LaneHalfWidth * 0.8f, spell.FieldHalfDepth), false);              // 레인 폭 × 깊이
                else if (behavior == SpellBehaviorType.LightningStrikes)
                    markerView.SetShape(new Vector2(arena.LaneHalfWidth, spell.FieldHalfDepth), false);                     // 레인 폭 × 세로로 긴 범위
                else
                    markerView.SetShape(Vector2.zero, false);
            }
            Tick(0f);
        }

        /// <summary>매 프레임 조준 갱신. progress = 0~1 (1초 카운트 진행률)</summary>
        public void Tick(float progress)
        {
            if (!IsActive || _session == null || _input == null || _spell == null) return;
            if (_input.TryGetAimRay(out var ray))
            {
                var team = _session.Events.HasLocalTeam ? _session.Events.LocalTeam : Team.Home;
                var r = ColumnDepthResolver.Resolve(ray, _session.Arena, team, _spell.Targeting, _spell.MaxDepth);
                if (r.Valid) _last = r;
            }
            if (markerView != null && _last.Valid)
                markerView.UpdateMarker(_last.MarkerPosition, _session.Arena, _last.WorldColumn, progress);
        }

        public void End()
        {
            IsActive = false;
            _spell = null;
            if (markerView != null) markerView.Hide();
        }
    }
}
