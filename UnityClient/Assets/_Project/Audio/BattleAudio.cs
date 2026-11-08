// 전투 소리 — 게임 이벤트(ClientSession.Events) · 시전 흐름(CastStateMachine) · 입력을 구독해 SoundManager 로 재생한다 (설명: Docs/Sound.md)
//   · 스킬 소리는 나와 상대 모두 나지만 상대 것은 작게 (SoundVolume.EnemySkill)
//   · 손 제스처(룬 그리기 · 무장 · 조준 · 쿨타임 완료) · 이동 소리는 내 입력에서만 난다 — 상대 것은 원래 들리지 않는다
//   · 소환수 · 설치물 · 타워 공격 · 구조물 파괴 소리는 ArenaStateView 가 낸다 (유닛을 아는 곳)

using System.Collections.Generic;
using SpellboundVR.Arena;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Input;
using SpellboundVR.Network;
using SpellboundVR.Spells;
using UnityEngine;

namespace SpellboundVR.Audio
{
    public sealed class BattleAudio : MonoBehaviour
    {
        private ClientSession _session;
        private NetworkGameEventPublisher _events;
        private ISpellInputSource _input;
        private CastStateMachine _cast;
        private CommanderPlatform _platform;
        private Transform _head;

        /// <summary>내가 시전한 스킬의 소리로 칠 시각 (spellId → 끝)</summary>
        private readonly Dictionary<int, float> _mineUntil = new Dictionary<int, float>();
        /// <summary>반사 결계가 켜져 있는 동안 (0 = 내 것, 1 = 상대 것) — 결계 중 다시 오는 H04 Cast = 튕김</summary>
        private readonly float[] _reflectUntil = new float[2];
        private readonly bool[] _nexusAlive = { true, true, true, true };
        private readonly SlotState[] _slotState = new SlotState[8];
        private MatchPhase _phase;
        private int _hp = -1;
        private int _stage;
        private float _lastWaveNext;
        private bool _afk;
        private bool _focusPending;

        public void Initialize(ClientSession session, ISpellInputSource input, CastStateMachine cast, CommanderPlatform platform, Transform head)
        {
            _session = session;
            _events = session.Events;
            _input = input;
            _cast = cast;
            _platform = platform;
            _head = head;

            _events.OnPhaseInfo += HandlePhase;
            _events.OnMatchStarted += HandleMatchStarted;
            _events.OnMatchEnded += HandleMatchEnded;
            _events.OnDefenseLayerChanged += HandleDefense;
            _events.OnBreachStarted += HandleBreach;
            _events.OnScalingStageChanged += HandleStage;
            _events.OnWaveTimerChanged += HandleWave;
            _events.OnAfkWarning += HandleAfk;
            _events.OnSlotStateChanged += HandleSlot;
            _events.OnCastRejected += HandleRejected;
            _events.OnLocalCastAccepted += HandleAccepted;
            _events.OnSpellFxLane += HandleSpellFx;
            _events.OnHitFeedback += HandleHit;
            _events.OnHpChanged += HandleHp;
            if (_cast != null)
            {
                _cast.OnStateChanged += HandleCastState;
                _cast.OnRuneRecognized += HandleRecognized;
                _cast.OnRecognitionFailed += HandleRecognitionFailed;
                _cast.OnCancelled += HandleCancelled;
            }
            if (_platform != null) _platform.OnSnapped += HandleSnapped;
        }

        private void OnDestroy()
        {
            SoundManager.StopAllKeyed();
            if (_events == null) return;
            _events.OnPhaseInfo -= HandlePhase;
            _events.OnMatchStarted -= HandleMatchStarted;
            _events.OnMatchEnded -= HandleMatchEnded;
            _events.OnDefenseLayerChanged -= HandleDefense;
            _events.OnBreachStarted -= HandleBreach;
            _events.OnScalingStageChanged -= HandleStage;
            _events.OnWaveTimerChanged -= HandleWave;
            _events.OnAfkWarning -= HandleAfk;
            _events.OnSlotStateChanged -= HandleSlot;
            _events.OnCastRejected -= HandleRejected;
            _events.OnLocalCastAccepted -= HandleAccepted;
            _events.OnSpellFxLane -= HandleSpellFx;
            _events.OnHitFeedback -= HandleHit;
            _events.OnHpChanged -= HandleHp;
            if (_cast != null)
            {
                _cast.OnStateChanged -= HandleCastState;
                _cast.OnRuneRecognized -= HandleRecognized;
                _cast.OnRecognitionFailed -= HandleRecognitionFailed;
                _cast.OnCancelled -= HandleCancelled;
            }
            if (_platform != null) _platform.OnSnapped -= HandleSnapped;
        }

        // ── 경기 흐름 · 배경음 ───────────────────────────────

        private void HandlePhase(MatchPhase phase, float timeLeft)
        {
            bool entered = phase != _phase;
            _phase = phase;
            if (phase == MatchPhase.Countdown)
            {
                if (entered) ResetBattleMusic();
                // 파일 = 3·2·1 비프(1초 간격) + 시작음 — 남은 3초에 맞춰 한 번
                if (Mathf.CeilToInt(timeLeft) == 3) SoundManager.Play("SFX_BattleCountdown", SoundVolume.Flow, null, 2f);
            }
            else if (phase == MatchPhase.Playing && entered && !IsBattleMusic(SoundManager.CurrentMusic))
            {
                ResetBattleMusic();
            }
        }

        private void HandleMatchStarted(OpponentType _)
        {
            if (!IsBattleMusic(SoundManager.CurrentMusic)) ResetBattleMusic();
        }

        private void ResetBattleMusic()
        {
            _stage = 0;
            _hp = -1;
            _lastWaveNext = 0f;
            for (int i = 0; i < _nexusAlive.Length; i++) _nexusAlive[i] = true;
            SoundManager.Music("BGM_BattleBase");
        }

        private static bool IsBattleMusic(string name) => name != null && name.StartsWith("BGM_Battle");

        private void HandleMatchEnded(MatchResult result, MatchEndReason reason, float duration)
        {
            SoundManager.StopAllKeyed();
            PlayResult(result);
        }

        /// <summary>결과 배경음 + 안내 멘트 (메타 UI 결과 화면도 같은 곡을 부른다 — 같은 곡이면 그대로)</summary>
        public static void PlayResult(MatchResult result)
        {
            switch (result)
            {
                case MatchResult.Win:
                    SoundManager.Music("BGM_ResultVictory");
                    SoundManager.Play("VO_AnnouncerVictory", SoundVolume.Voice, null, 3f);
                    break;
                case MatchResult.Lose:
                    SoundManager.Music("BGM_ResultDefeat");
                    SoundManager.Play("VO_AnnouncerDefeat", SoundVolume.Voice, null, 3f);
                    break;
                default:
                    SoundManager.Music("BGM_ResultDraw");
                    break;
            }
        }

        private void HandleDefense(Team team, bool towerAlive, bool nexusAlive)
        {
            int t = (int)team;
            if (t < 0 || t >= _nexusAlive.Length) return;
            bool wasAlive = _nexusAlive[t];
            _nexusAlive[t] = nexusAlive;
            // 넥서스가 처음 무너지면 전투 배경음을 한 단계 올린다 (파괴음은 ArenaStateView)
            if (wasAlive && !nexusAlive && _phase == MatchPhase.Playing && _stage < 1)
            {
                _stage = 1;
                SoundManager.Music("BGM_BattleTension", true);
            }
        }

        private void HandleBreach(Team attacker)
        {
            SoundManager.Play("SFX_BreachWarning", SoundVolume.Flow, null, 1f);
            if (_stage < 2)
            {
                _stage = 2;
                SoundManager.Music("BGM_BattleClimax", true);
            }
        }

        private void HandleStage(int stage)
        {
            if (stage > 0 && _phase == MatchPhase.Playing) SoundManager.Play("SFX_MatchPowerUp5Min", SoundVolume.Flow, null, 5f);
        }

        private void HandleWave(float nextIn, bool brute)
        {
            // 남은 시간이 다시 커졌다 = 웨이브가 나왔다 (신호음은 짧고 작게)
            if (_phase == MatchPhase.Playing && nextIn > _lastWaveNext + 1f && _lastWaveNext > 0f)
                SoundManager.Play("SFX_WaveStart", 0.3f, null, 3f);
            _lastWaveNext = nextIn;
        }

        private void HandleAfk(float secondsLeft)
        {
            bool warn = secondsLeft >= 0f;
            if (warn && !_afk) SoundManager.Play("SFX_AFKWarning", SoundVolume.Flow, null, 3f);
            _afk = warn;
        }

        // ── 내 시전 흐름 (내 것만) ───────────────────────────

        private void HandleSlot(int slot, SlotState state)
        {
            if (slot < 0 || slot >= _slotState.Length) return;
            if (state == SlotState.Ready && _slotState[slot] == SlotState.Cooldown && _phase == MatchPhase.Playing)
                SoundManager.Play("SFX_CooldownReadyChime", SoundVolume.Gesture * 0.8f, null, 0.15f);
            _slotState[slot] = state;
        }

        private void HandleRejected(string reason) => SoundManager.Play("SFX_UIError", SoundVolume.Ui, null, 0.3f);

        private void HandleAccepted(int slot, int spellId)
        {
            var def = _session.Catalog.Get(spellId);
            float window = def != null ? Mathf.Max(def.EffectDuration, def.TelegraphDelay, def.FromOffMap ? def.OffMapFlightSeconds : 0f) : 0f;
            _mineUntil[spellId] = Time.time + window + 4f;
            // S10 집중 다음 스킬 = 증폭 발동음
            if (spellId == SpellIds.S10_Focus) _focusPending = true;
            else if (_focusPending)
            {
                _focusPending = false;
                SoundManager.Play("SFX_S10ConcentrateAmplify", SoundVolume.MySkill);
            }
        }

        private void HandleCastState(CastState prev, CastState next)
        {
            if (next == CastState.Armed) SoundManager.Play("SFX_CastArmed", SoundVolume.Gesture);
            if (next == CastState.Aiming)
            {
                // 2초짜리 충전음을 조준 시간에 맞춰 늘이거나 줄인다
                float aim = Mathf.Max(0.1f, _session.Rules.AimDurationSeconds);
                SoundManager.StartKeyed("charge", "SFX_CastCharge2Sec", SoundVolume.Gesture, null, 0f, false, Mathf.Clamp(2f / aim, 0.6f, 1.5f));
            }
            else if (prev == CastState.Aiming) SoundManager.StopKeyed("charge");
        }

        private void HandleRecognized(SpellDefinition def, int slot) => SoundManager.Play("SFX_RuneRecognized", SoundVolume.Gesture);

        private void HandleRecognitionFailed(string reason) => SoundManager.Play("SFX_RuneFailCancel", SoundVolume.Gesture, null, 0.4f);

        private void HandleCancelled(CastCancelReason reason)
        {
            if (reason == CastCancelReason.Timeout) SoundManager.Play("SFX_CastTimeout", SoundVolume.Gesture, null, 0.4f);
            else if (reason != CastCancelReason.None) SoundManager.Play("SFX_RuneFailCancel", SoundVolume.Gesture, null, 0.4f);
        }

        private void HandleSnapped() => SoundManager.Play("SFX_SnapTeleport", SoundVolume.Gesture, null, 0.1f);

        // ── 피격 · 상태 ─────────────────────────────────────

        private int LocalPlayerId => TeamUtil.PlayerId(_events.LocalTeam);

        private void HandleHit(int targetId, Vector3 position, int amount, HitResultKind kind)
        {
            bool me = _events.HasLocalTeam && targetId == LocalPlayerId;
            switch (kind)
            {
                case HitResultKind.Dodge:
                    if (me) SoundManager.Play("SFX_PlayerDodgeSuccess", SoundVolume.Status, null, 0.3f);
                    break;
                case HitResultKind.Immune:
                    if (me) SoundManager.Play("SFX_DefenseMitigationDeflect", SoundVolume.Status * 0.7f, null, 0.2f);
                    break;
                case HitResultKind.Absorbed:
                    if (TeamUtil.IsPlayerId(targetId))
                        SoundManager.Play("SFX_S08ShieldAbsorb", me ? SoundVolume.MySkill : SoundVolume.EnemySkill, position, 0.15f);
                    else
                        SoundManager.Play("SFX_S09TowerBuffAbsorb", IsMineWindow(SpellIds.S09_TowerBuff) ? SoundVolume.MySkill : SoundVolume.EnemySkill, position, 0.2f);
                    break;
            }
        }

        private void HandleHp(Team team, int current, int max)
        {
            if (!_events.HasLocalTeam || team != _events.LocalTeam) return;
            if (_hp >= 0 && current < _hp && _phase == MatchPhase.Playing)
                SoundManager.Play("SFX_PlayerHit", SoundVolume.Status, null, 0.25f);
            _hp = current;
            bool low = current > 0 && max > 0 && current <= max / 4 && _phase == MatchPhase.Playing;
            if (low) SoundManager.StartKeyed("heartbeat", "SFX_PlayerLowHpHeartbeatLoop", SoundVolume.Status * 0.8f);
            else SoundManager.StopKeyed("heartbeat");
        }

        private void Update()
        {
            if (_events == null) return;
            // 룬 그리는 중 (핀치 · 마우스 드래그를 누르고 있는 동안)
            if (_input != null && _input.IsDrawing && _phase == MatchPhase.Playing) SoundManager.StartKeyed("draw", "SFX_RuneDrawingLoop", SoundVolume.Gesture * 0.6f);
            else SoundManager.StopKeyed("draw");

            // S01 연막에 맞아 앞이 안 보이는 동안 먹먹한 소리
            var snap = _events.Latest;
            bool blind = snap != null && _events.HasLocalTeam && snap.Phase == MatchPhase.Playing &&
                         (snap.Players[(int)_events.LocalTeam].Flags & PlayerFlags.Blind) != 0;
            if (blind) SoundManager.StartKeyed("blind", "SFX_S01SmokeMuffledLoop", SoundVolume.EnemySkill);
            else SoundManager.StopKeyed("blind");
        }

        // ── 스킬 ────────────────────────────────────────────

        private bool IsMineWindow(int spellId) => _mineUntil.TryGetValue(spellId, out float until) && Time.time < until;

        /// <summary>
        /// 내 스킬인가 — 시전 연출(Cast)이 내 발판 근처면 내 것, 아니면 내가 최근에 이 스킬을 썼는지로 판단.
        /// shortcut: 나와 상대가 같은 스킬을 동시에 쓰면 상대 소리도 크게 날 수 있다 — 서버 이벤트에 시전자가 실리면 그걸로 바꾼다.
        /// </summary>
        private bool IsMine(int spellId, VfxPart part, Vector3 position)
        {
            if (part == VfxPart.Cast && _head != null)
            {
                Vector3 d = position - _head.position;
                d.y = 0f;
                return d.sqrMagnitude < 25f;
            }
            return IsMineWindow(spellId);
        }

        private void HandleSpellFx(int spellId, VfxPart part, Vector3 pos, int lane)
        {
            bool mine = IsMine(spellId, part, pos);
            float v = mine ? SoundVolume.MySkill : SoundVolume.EnemySkill;
            var def = _session.Catalog.Get(spellId);
            float dur = def != null && def.EffectDuration > 0f ? def.EffectDuration : 3f;
            string who = mine ? "m" : "e";
            switch (spellId)
            {
                case SpellIds.S01_Smoke:
                    if (part == VfxPart.Cast) SoundManager.Play("SFX_S01SmokeCast", v, pos);
                    break;
                case SpellIds.S02_PoisonField:
                    if (part == VfxPart.Cast) SoundManager.Play("SFX_S02PoisonBottleThrow", v, pos);
                    else if (part == VfxPart.Impact) SoundManager.Play("SFX_S02PoisonBottleBreak", v, pos);
                    else if (part == VfxPart.Field) SoundManager.StartKeyed("S02" + who + lane, "SFX_S02PoisonBubbleLoop", v * 0.7f, pos, dur);
                    break;
                case SpellIds.S03_Fireball:
                    if (part == VfxPart.Cast) SoundManager.Play("SFX_S03FireballProjectile", v, pos);
                    else if (part == VfxPart.Impact) SoundManager.Play("SFX_S03FireballImpact", v, pos);
                    break;
                case SpellIds.S04_ArrowRain:
                    if (part == VfxPart.Cast) SoundManager.Play("SFX_S04ArrowRainProjectile", v, pos);
                    else if (part == VfxPart.Impact) SoundManager.Play("SFX_S04ArrowRainImpact", v, pos, 0.25f);
                    break;
                case SpellIds.S05_Tornado:
                    if (part == VfxPart.Cast) SoundManager.StartKeyed("S05" + who, "SFX_S05TornadoLoop", v, pos, dur);
                    break;
                case SpellIds.S06_Lightning:
                    if (part == VfxPart.Telegraph) SoundManager.Play("SFX_S06LightningTelegraph", v, pos, 0.6f);
                    else if (part == VfxPart.Impact) SoundManager.Play("SFX_S06LightningImpact", v, pos, 0.2f);
                    break;
                case SpellIds.S07_Gatling:
                    if (part == VfxPart.Cast)
                    {
                        float fire = def != null ? def.ShotCount * def.ShotInterval + 0.2f : 1.6f;
                        SoundManager.StartKeyed("S07" + who, "SFX_S07BarrageFireLoop", v, pos, fire);
                    }
                    break;
                case SpellIds.S08_Shield:
                    if (part == VfxPart.Cast && SoundManager.Play("SFX_S08ShieldCast", v, pos, 0.3f))
                        SoundManager.PlayLater(dur, "SFX_S08ShieldEnd", v * 0.8f, pos); // shortcut: 쉴드가 먼저 깨져도 지속 시간 끝에 난다
                    break;
                case SpellIds.S09_TowerBuff:
                    // 살아 있는 타워마다 Cast 가 와서 한 번만
                    if (part == VfxPart.Cast && SoundManager.Play("SFX_S09TowerBuffCast", v, pos, 0.5f))
                        SoundManager.PlayLater(dur, "SFX_S09TowerBuffEnd", v * 0.8f, pos);
                    break;
                case SpellIds.S10_Focus:
                    if (part == VfxPart.Cast) SoundManager.Play("SFX_S10ConcentrateCharge", v, pos);
                    break;
                case SpellIds.H01_Freeze:
                    if (part == VfxPart.Field && SoundManager.Play("SFX_H01FreezeImpact", v, pos, 1f))
                        SoundManager.PlayLater(dur, "SFX_H01FreezeMelt", v * 0.8f, pos);
                    break;
                case SpellIds.H02_Laser:
                    if (part == VfxPart.Telegraph) SoundManager.Play("SFX_H02LaserTelegraph", v, pos, 0.3f);
                    else if (part == VfxPart.Field) SoundManager.StartKeyed("H02" + who, "SFX_H02LaserBeamLoop", v * 0.8f, pos, dur);
                    break;
                case SpellIds.H03_Drain:
                    if (part == VfxPart.Impact) SoundManager.Play("SFX_H03DrainAbsorb", v, pos, 0.3f);
                    else if (part == VfxPart.Loop) SoundManager.Play("SFX_H03DrainHeal", v, pos, 0.3f);
                    break;
                case SpellIds.H04_ReflectBarrier:
                    if (part != VfxPart.Cast) break;
                    int side = mine ? 0 : 1;
                    if (Time.time < _reflectUntil[side]) SoundManager.Play("SFX_H04ReflectBarrierBounce", v, pos, 0.15f);
                    else if (SoundManager.Play("SFX_H04ReflectBarrierCast", v, pos, 0.3f))
                    {
                        _reflectUntil[side] = Time.time + dur;
                        SoundManager.PlayLater(dur, "SFX_H04ReflectBarrierEnd", v * 0.8f, pos);
                    }
                    break;
                case SpellIds.H05_Rage:
                    // Cast 와 Field 가 함께 와서 한 번만
                    if ((part == VfxPart.Cast || part == VfxPart.Field) && SoundManager.Play("SFX_H05RageAuraCast", v, pos, 0.8f))
                        SoundManager.PlayLater(0.4f, "SFX_H05RageRoar", v * 0.8f, pos);
                    break;
                // S11 · S12 · S13 · H06 소환 · 설치 소리는 유닛이 나타날 때 ArenaStateView 가 낸다
            }
        }
    }
}
