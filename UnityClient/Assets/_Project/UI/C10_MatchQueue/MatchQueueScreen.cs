// C10 매칭 대기 — 웹 pages/queue.js, 서버 대기열은 BackendSessionService
//   레이팅이 맞는 사람과 매칭, 서버 설정 시간(기본 30초)이 지나면 모인 사람만으로 시작하고 빈자리는 AI.
//   매칭되면 서버가 게임 방(matchId)을 만들고 1.5초 뒤 C11. 4인 개인전은 모인 인원을 "2 / 4명" 으로 보여 준다.
//   서버에 연결할 수 없으면 BackendSessionService가 오프라인 봇 매칭으로 대신한다.

using SpellboundVR.Backend;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class MatchQueueScreen : MetaScreen
    {
        private static readonly string[] Tips =
        {
            "적 타워가 살아있는 동안에는 상대 플레이어에게 20%의 피해만 들어갑니다.",
            "플레이어 스킬은 타워 · 넥서스를 공격할 수 없어요. 유닛으로 구조물을 밀어붙이세요.",
            "고위력 스킬은 게임당 3회까지만 쓸 수 있습니다. 결정적인 순간에 아껴두세요.",
            "왼손을 옆으로 휘두르면 좌/중/우 열을 한 칸씩 순간이동합니다.",
            "4인 개인전: 왼쪽 · 가운데 · 오른쪽 레인이 곧 세 상대입니다. 가리키는 쪽이 공격 대상이에요.",
            "4인 개인전: 시간이 지나면 마나 폭풍이 구조물과 마법사를 깎습니다. 숨어만 있으면 집니다.",
            "적 타워를 부수면 공격력이 영구 +20%, 넥서스를 부수면 +40% 강해집니다.",
        };

        private BackendSessionService _session;
        private float _elapsed;
        private float _botTimeout = 30f;
        private int _players = 2;
        private MatchmakingStatusDto _found;
        private float _foundAt;
        private string _tip;
        private Text _title, _timer, _sub;
        private RectTransform _bar;
        private Image _foe;

        public override string Title => null;
        public override bool ShowBackToLobby => false;

        protected override void OnOpen()
        {
            _elapsed = 0f;
            _found = null;
            _foundSent = false;
            _tip = Tips[Random.Range(0, Tips.Length)];
            _players = MetaState.PreferredPlayers;
            _session = BackendSessionService.Instance;
            _session.OnMatchmakingProgress += HandleProgress;
            _session.OnMatchFound += HandleFound;
            _session.OnSessionError += HandleError;
            _session.RequestMatchmaking(_players);
        }

        protected override void OnClose()
        {
            if (_session == null) return;
            _session.OnMatchmakingProgress -= HandleProgress;
            _session.OnMatchFound -= HandleFound;
            _session.OnSessionError -= HandleError;
            if (_found == null) _session.CancelMatchmaking();
        }

        protected override void Build(RectTransform body)
        {
            var col = UiKit.Stretch(UiKit.VBox(body, 22, 0, TextAnchor.MiddleCenter, "Queue"));
            col.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            _title = UiKit.Label(col, _players > 2 ? "4인 개인전 상대를 찾고 있습니다" : "상대를 찾고 있습니다", UiTheme.FontHeadline + 6, UiTheme.Text, true, TextAnchor.MiddleCenter);
            _title.horizontalOverflow = HorizontalWrapMode.Overflow;
            var duel = UiKit.HBox(col, 30, 0, TextAnchor.MiddleCenter, "Duel");
            UiKit.Wizard(duel, false, 280);
            UiKit.Label(duel, "VS", 46, UiTheme.Text3, true, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
            _foe = UiKit.Wizard(duel, true, _players > 2 ? 180 : 280, true);
            if (_players > 2)
            {
                UiKit.Wizard(duel, true, 180, true);
                UiKit.Wizard(duel, true, 180, true);
            }
            _timer = UiKit.Label(col, "0:00", 96, UiTheme.Text, true, TextAnchor.MiddleCenter);
            _timer.horizontalOverflow = HorizontalWrapMode.Overflow;
            var barHolder = UiKit.HBox(col, 0, 0, TextAnchor.MiddleCenter, "Bar");
            UiKit.Size(barHolder, 640, 12);
            barHolder.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            _bar = UiKit.Progress(barHolder, 0f, UiTheme.Cyan, 12);
            _sub = UiKit.Label(col, $"레이팅 {MetaState.Rating} ± 50 범위 탐색 중 · {_botTimeout:0}초 안에 안 들어오면 AI 가 자리를 채웁니다", UiTheme.FontBody, UiTheme.Text2, false, TextAnchor.MiddleCenter);
            _sub.horizontalOverflow = HorizontalWrapMode.Overflow;
            var tip = UiKit.Note(col, "TIP  " + _tip, Tone.Cyan);
            tip.alignment = TextAnchor.MiddleCenter;
            UiKit.Size((RectTransform)tip.transform.parent, 1180, -1);
            UiKit.Button(col, "매칭 취소", BtnStyle.Ghost, () => Ui.Go(MetaScreenId.Lobby), 240);
        }

        public override void Tick(float dt)
        {
            if (_found != null)
            {
                if (_foundSent || Time.unscaledTime - _foundAt < 1.5f) return;
                _foundSent = true; // 한 번만 (OnClose는 _found가 있으면 큐를 취소하지 않는다)
                var f = _found;
                MetaFlow.Instance.StartMatch(new MatchContext
                {
                    Mode = MatchResultService.ModeRanked,
                    OpponentName = string.IsNullOrEmpty(f.opponentNickname) ? "Bot" : f.opponentNickname,
                    OpponentRating = f.opponentRating,
                    OpponentBot = f.bot,
                    MatchId = f.matchId,
                    RoomCode = f.roomCode,
                    Players = f.maxPlayers > 0 ? f.maxPlayers : _players,
                    OpponentNames = NamesOf(f),
                });
                return;
            }
            _elapsed += dt;
            if (_timer != null) _timer.text = MetaState.Mmss(_elapsed);
            UiKit.SetProgress(_bar, _elapsed / Mathf.Max(1f, _botTimeout));
        }

        private static string[] NamesOf(MatchmakingStatusDto f)
        {
            if (f.opponents == null) return null;
            var names = new string[f.opponents.Length];
            for (int i = 0; i < names.Length; i++) names[i] = f.opponents[i].nickname;
            return names;
        }

        private bool _foundSent;

        private void HandleProgress(MatchmakingStatusDto s)
        {
            if (s.botTimeout > 0f) _botTimeout = s.botTimeout;
            string found = s.maxPlayers > 2 ? $"모인 인원 {Mathf.Max(1, s.playersFound)} / {s.maxPlayers}명" : $"대기 {Mathf.Max(1, s.queueSize)}명";
            if (_sub != null) _sub.text = $"레이팅 {MetaState.Rating} ± {(s.ratingWindow > 0 ? s.ratingWindow : 50)} 범위 탐색 중 · {found} · {_botTimeout:0}초 안에 안 들어오면 AI 가 자리를 채웁니다";
        }

        private void HandleFound(MatchmakingStatusDto s)
        {
            _found = s;
            _foundAt = Time.unscaledTime;
            _foundSent = false;
            if (_title != null) _title.text = s.bot ? "봇 매칭 완료" : "매칭 완료!";
            if (_sub != null)
            {
                _sub.text = s.bot ? "대기 시간이 지나 AI 가 자리를 채웠습니다." : $"{s.opponentNickname} 님과 매칭되었습니다!";
                _sub.color = UiTheme.Cyan;
            }
            if (_foe != null) _foe.color = Color.white;
            UiKit.SetProgress(_bar, 1f);
        }

        private void HandleError(string err)
        {
            MetaUiRoot.Toast(err, Tone.Enemy);
            Ui.Go(MetaScreenId.Lobby);
        }
    }
}
