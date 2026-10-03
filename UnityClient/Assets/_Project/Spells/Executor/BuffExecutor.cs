// §14.2 BuffExecutor — 조준 없음(주먹 즉시 시전) / 스킬: S08 방어 쉴드, S09 타워 버프, S10 집중, H04 반사 결계
// 현재 구현: S08 방어 쉴드 (ShieldRegenEffect)

using SpellboundVR.Contracts;
using SpellboundVR.Spells.Effects;

namespace SpellboundVR.Spells.Executor
{
    public sealed class BuffExecutor : ISpellExecutor
    {
        private readonly SpellDefinition _def;
        private readonly ISpellWorld _world;

        public BuffExecutor(SpellDefinition definition, ISpellWorld world)
        {
            _def = definition;
            _world = world;
        }

        public void Execute(in SpellCastRequest request, Team casterTeam)
        {
            int playerId = _world.GetPlayerId(casterTeam);
            int column = _world.GetPlayerWorldColumn(casterTeam);
            var pos = _world.GetPlayerPosition(casterTeam);

            switch (_def.Behavior)
            {
                case SpellBehaviorType.ShieldRegen:
                    _world.AddStatusEffect(playerId, new ShieldRegenEffect(_def));
                    break;

                default:
                    if (!string.IsNullOrEmpty(_def.StatusEffectId) && _def.EffectDuration > 0f)
                        _world.StatusEffects.ApplyStatusEffect(playerId, _def.StatusEffectId, _def.EffectDuration);
                    break;
            }

            _world.PublishFx(_def.SpellId, VfxPart.Cast, pos, 1.2f, column);
            _world.PublishFx(_def.SpellId, VfxPart.Loop, pos, 1.2f, column);
        }
    }
}
