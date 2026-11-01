// AI 코치 — 사람 플레이어에게 전투 중 VLM · 규칙 AI · 학습 결과의 조언을 보여 준다 (POST /api/ai/strategy).
//   · 몇 초마다 지금 상황을 서버로 보내면, 서버가 전장 지도를 그려 VLM 에 묻고 (없으면 규칙 AI) 답한다
//   · 답(요약 · 추천 스킬 · 추천 레인 · 노릴 상대)은 HUD "AI 코치" 칸에 나온다. 판정에는 아무 영향이 없다
//   · 로그인하지 않았거나 서버가 없으면 조용히 쉰다 (칸이 비어 있다)

using System;
using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Spells;
using SpellboundVR.UI;
using UnityEngine;

namespace SpellboundVR.Backend
{
    public sealed class BattleCoach : MonoBehaviour
    {
        [Tooltip("조언 요청 간격 (초) — 서버 설정 ai.vlm_min_interval 보다 짧으면 그 사이는 규칙 AI 가 답한다")]
        public float interval = 6f;
        [Tooltip("연결 실패 뒤 다시 시도할 때까지 (초)")]
        public float retryAfterFailure = 30f;

        [Serializable] private class LaneDto { public int allies; public int enemies; public float enemyFront = 1f; }
        [Serializable] private class OpponentDto { public string nickname; public int seat; public int hp; public int column; public bool towerAlive; public bool nexusAlive; public bool bot; }

        [Serializable]
        private class StateDto
        {
            public string[] deck;
            public bool[] ready;
            public int selfHp;
            public int opponentHp;
            public int selfColumn;
            public int opponentColumn;
            public float time;
            public bool selfTowerAlive;
            public bool selfNexusAlive;
            public bool opponentTowerAlive;
            public bool opponentNexusAlive;
            public LaneDto[] lanes;
            public OpponentDto[] opponents;
            public int players;
            public string mode = "battle";
        }

        [Serializable] private class RequestDto { public StateDto state; public bool brief = true; public bool record; }
        [Serializable] private class CastDto { public int slot = -1; public string spellId; public int column = -1; public string reason; }
        [Serializable] private class ResponseDto { public string summary; public string focus; public string source; public CastDto cast; }

        private ClientSession _session;
        private BattleHudView _hud;
        private float _nextAt;
        private bool _inFlight;

        public void Initialize(ClientSession session, BattleHudView hud)
        {
            _session = session;
            _hud = hud;
            _nextAt = 4f;
        }

        private void Update()
        {
            if (_session == null || _inFlight || Time.time < _nextAt || !AuthService.IsLoggedIn) return;
            var s = _session.Events.Latest;
            if (s == null || !_session.Events.HasLocalTeam || (MatchPhase)s.Header.Phase != MatchPhase.Playing) return;
            if (Eliminated(s)) return; // 관전 중에는 조언하지 않는다
            _nextAt = Time.time + Mathf.Max(3f, interval);
            _inFlight = true;
            BackendClient.Instance.Post<RequestDto, ResponseDto>("/api/ai/strategy", new RequestDto { state = BuildState(s) },
                res =>
                {
                    _inFlight = false;
                    if (_hud != null && res != null && !Eliminated(_session.Events.Latest)) _hud.ShowCoach(Describe(res));
                },
                _ =>
                {
                    _inFlight = false;
                    _nextAt = Time.time + retryAfterFailure;
                });
        }

        private bool Eliminated(MatchSnapshot s) => s != null && s.Players[(int)_session.Events.LocalTeam].Place != 0;

        private string Describe(ResponseDto r)
        {
            string line = r.summary ?? "";
            if (r.cast != null && r.cast.slot >= 0 && !string.IsNullOrEmpty(r.cast.spellId))
                line += "\n→ " + (MetaSkills.TryGet(r.cast.spellId, out var info) ? info.Name : r.cast.spellId) + "  " + ColumnName(r.cast.column);
            if (!string.IsNullOrEmpty(r.focus)) line += "\n노릴 상대: " + r.focus;
            return line;
        }

        private string ColumnName(int column)
        {
            var arena = _session.Arena;
            if (column < 0 || column > 2) return "";
            string side = column == 0 ? "왼쪽" : column == 1 ? "가운데" : "오른쪽";
            if (arena.IsDuel) return side + " 열";
            return side + " 레인 (" + TeamUtil.SeatNumber(arena.ColumnTarget(_session.Events.LocalTeam, (Column)column)) + "번)";
        }

        /// <summary>스냅샷 → 서버 상황. 열은 모두 내 시점 (개인전의 column = 그 상대로 가는 레인)</summary>
        private StateDto BuildState(MatchSnapshot s)
        {
            var arena = _session.Arena;
            Team me = _session.Events.LocalTeam;
            Team focus = arena.IsDuel ? TeamUtil.Opponent(me) : WeakestEnemy(s, me);
            ref var self = ref s.Players[(int)me];
            ref var opp = ref s.Players[(int)focus];

            var st = new StateDto
            {
                deck = new string[MatchSnapshot.SlotsPerPlayer],
                ready = new bool[MatchSnapshot.SlotsPerPlayer],
                selfHp = self.Hp,
                opponentHp = opp.Hp,
                selfColumn = (int)TeamUtil.ToLocalColumn(me, self.WorldColumn),
                opponentColumn = arena.IsDuel ? (int)TeamUtil.ToLocalColumn(me, opp.WorldColumn) : (int)ColumnOf(me, focus),
                time = s.Header.MatchTime,
                selfTowerAlive = s.AllTowersStanding(me),
                selfNexusAlive = s.Structure(me, true).Alive != 0,
                opponentTowerAlive = s.TowerFacing(focus, me).Alive != 0,
                opponentNexusAlive = s.Structure(focus, true).Alive != 0,
                lanes = new LaneDto[TeamUtil.ColumnCount],
                players = arena.TeamCount,
                opponents = BuildOpponents(s, me),
            };
            for (int i = 0; i < st.deck.Length; i++)
            {
                ref var slot = ref s.Slot(me, i);
                st.deck[i] = slot.SpellId > 0 ? SpellIds.Code(slot.SpellId) : "";
                st.ready[i] = slot.State == (byte)SlotState.Ready;
            }

            for (int c = 0; c < st.lanes.Length; c++) st.lanes[c] = new LaneDto();
            for (int i = 0; i < s.UnitCount; i++)
            {
                ref var u = ref s.Units[i];
                var p = new Vector2(u.U, u.V);
                arena.ResolveAim(me, p, out int lane, out Column local, out float depth);
                if (Mathf.Abs(arena.GetLane(me, lane).Lateral(p)) > arena.LaneHalfWidth * 1.5f) continue; // 나와 이어지지 않은 레인의 싸움
                var info = st.lanes[(int)local];
                if (u.Team == (byte)me) info.allies++;
                else
                {
                    info.enemies++;
                    info.enemyFront = Mathf.Min(info.enemyFront, depth);
                }
            }
            return st;
        }

        private Column ColumnOf(Team me, Team enemy)
        {
            _session.Arena.TryLocalColumn(me, _session.Arena.LaneBetween(me, enemy), out Column c);
            return c;
        }

        private Team WeakestEnemy(MatchSnapshot s, Team me)
        {
            var best = TeamUtil.Opponent(me);
            float score = float.MaxValue;
            for (int c = 0; c < TeamUtil.ColumnCount; c++)
            {
                Team enemy = _session.Arena.ColumnTarget(me, (Column)c);
                ref var p = ref s.Players[(int)enemy];
                if (p.Alive == 0) continue;
                float v = p.Hp + (s.TowerFacing(enemy, me).Alive != 0 ? 10000f : 0f);
                if (v < score)
                {
                    score = v;
                    best = enemy;
                }
            }
            return best;
        }

        private OpponentDto[] BuildOpponents(MatchSnapshot s, Team me)
        {
            var arena = _session.Arena;
            if (arena.IsDuel) return Array.Empty<OpponentDto>();
            var list = new List<OpponentDto>(3);
            for (int c = 0; c < TeamUtil.ColumnCount; c++)
            {
                Team enemy = arena.ColumnTarget(me, (Column)c);
                ref var p = ref s.Players[(int)enemy];
                int seat = TeamUtil.SeatNumber(enemy);
                list.Add(new OpponentDto
                {
                    nickname = string.IsNullOrEmpty(s.Names[(int)enemy]) ? seat + "번" : s.Names[(int)enemy],
                    seat = seat, hp = p.Hp, column = c, bot = p.IsBot != 0,
                    towerAlive = s.TowerFacing(enemy, me).Alive != 0, nexusAlive = s.Structure(enemy, true).Alive != 0,
                });
            }
            return list.ToArray();
        }
    }
}
