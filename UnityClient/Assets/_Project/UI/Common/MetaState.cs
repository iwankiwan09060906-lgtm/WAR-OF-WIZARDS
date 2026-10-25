// 메타 UI 공용 상태 — 웹 클라이언트 core.js 의 state 와 같은 역할. static 이라 경기 후 씬을 다시 불러와도 유지된다.
//   · 로그인 사용자 · 프로필 · 덱(스킬 코드 8개) · 우편 · 접속 중 친구 · 다음 대전 정보(Match) · 지난 대전 결과(LastMatch)
//   · 환경설정(PlayerPrefs) · 오프라인 모드(서버 없이 봇 연습만)
//   · 스킬 19종 표시 정보(MetaSkills) — 이름 · 종류 · 효과 · 룬 모양 (웹 core.js RAW 와 같은 내용)

using System;
using System.Collections.Generic;
using SpellboundVR.Backend;
using UnityEngine;

namespace SpellboundVR.UI
{
    public enum MetaScreenId { Login, Signup, Lobby, MyPage, Settings, Friends, Room, Mailbox, Ranking, Spellbook, Queue, Countdown, Result, Tutorial, BattleMenu }

    /// <summary>다음 대전 정보 (C02 · C06 · C10 → C11)</summary>
    public sealed class MatchContext
    {
        /// <summary>ranked · friendly · bot</summary>
        public string Mode = MatchResultService.ModeBot;
        public string OpponentName = "Bot";
        public int OpponentRating;
        public bool OpponentBot = true;
        public long MatchId;
        public string RoomCode;
    }

    /// <summary>지난 대전 결과 (C13)</summary>
    public sealed class MatchRecord
    {
        public Contracts.MatchResult Result;
        public string Reason = "";
        public float DurationSec;
        public int Casts;
        public int DamageDealt;
        public int DamageTaken;
        public string Mode = MatchResultService.ModeBot;
        public string OpponentName = "Bot";
        /// <summary>null = 저장 중, true = 저장됨, false = 실패</summary>
        public bool? Saved;
        public string SaveMessage = "";
        public int RatingDelta;
        public int NewRating;
    }

    [Serializable]
    public sealed class MetaSettings
    {
        public int camSensitivity = 65;
        public bool comfortVignette = true;
        public bool snapTurning;
        public int sfx = 80;
        public int bgm = 45;
        public bool spatialAudio = true;
        public int gestureSensitivity = 70;
        public int holdFrames = 15;
        public bool haptics = true;
        public bool subtitles;
        public bool uiLarge;
        public bool colorblind;
        /// <summary>VR 메뉴 포인터 크기 (0~100, 기본 40 — 작은 점)</summary>
        public int pointerSize = 40;
        /// <summary>손에서 나오는 레이 선도 보일지 (기본 꺼짐 — 점만)</summary>
        public bool showRayLine;
    }

    public static class MetaState
    {
        private const string SettingsKey = "wow.settings.v1";

        public static AuthResponseDto User => AuthService.CurrentUser;
        public static PlayerDto Profile;
        public static List<string> Deck = new List<string>();
        public static MailboxItemDto[] Mails = new MailboxItemDto[0];
        public static readonly HashSet<long> Online = new HashSet<long>();
        public static MatchContext Match;
        public static MatchRecord LastMatch;
        /// <summary>서버 없이 시작 (봇 연습만, 기록 저장 안 됨)</summary>
        public static bool Offline;
        /// <summary>씬을 다시 불러온 뒤 열 화면 (경기 후 로비 · 다시 대전)</summary>
        public static MetaScreenId? PendingScreen;
        /// <summary>우편 초대장으로 들어갈 친선전 방 코드</summary>
        public static string PendingRoomCode;
        /// <summary>씬을 다시 불러온 뒤 띄울 알림 (접속 실패 등)</summary>
        public static string PendingToast;

        private static MetaSettings s_settings;

        public static event Action<string> OnChanged;

        public static bool LoggedIn => Offline || AuthService.IsLoggedIn;
        public static string Nickname => Profile != null ? Profile.nickname : (User != null ? User.nickname : "Wizard");
        public static int Rating => Profile != null ? Profile.rating : 1000;

        public static void Notify(string key) => OnChanged?.Invoke(key);

        // ── 불러오기 ──────────────────────────────────────────

        /// <summary>로그인 직후: 프로필 · 덱 · 우편 · 접속 상태를 모두 받아 온다 (하나라도 실패해도 끝까지 진행)</summary>
        public static void LoadPlayer(Action done)
        {
            int pending = 4;
            void Step() { if (--pending == 0) done?.Invoke(); }
            ProfileService.GetMe(p => { Profile = p; Notify("profile"); Step(); }, _ => Step());
            DeckService.GetMine(d => { Deck = new List<string>(d ?? new string[0]); Notify("deck"); Step(); }, _ => Step());
            RefreshMails(Step);
            RefreshOnline(Step);
        }

        public static void RefreshProfile(Action done = null)
        {
            if (Offline || !AuthService.IsLoggedIn) { done?.Invoke(); return; }
            ProfileService.GetMe(p => { Profile = p; Notify("profile"); done?.Invoke(); }, _ => done?.Invoke());
        }

        public static void RefreshMails(Action done = null)
        {
            if (Offline || !AuthService.IsLoggedIn) { done?.Invoke(); return; }
            MailService.GetMailbox(m => { Mails = m ?? new MailboxItemDto[0]; Notify("mails"); done?.Invoke(); }, _ => done?.Invoke());
        }

        public static void RefreshOnline(Action done = null)
        {
            if (Offline || !AuthService.IsLoggedIn) { done?.Invoke(); return; }
            PresenceService.GetOnline(ids =>
            {
                Online.Clear();
                foreach (var id in ids) Online.Add(id);
                Notify("online");
                done?.Invoke();
            }, _ => done?.Invoke());
        }

        public static int UnreadMail()
        {
            int n = 0;
            foreach (var m in Mails) if (!m.read && !m.IsNotice) n++;
            return n;
        }

        public static int UnreadCount(bool notices)
        {
            int n = 0;
            foreach (var m in Mails) if (!m.read && m.IsNotice == notices) n++;
            return n;
        }

        public static void MarkRead(MailboxItemDto item)
        {
            if (item == null || item.read) return;
            item.read = true;
            Notify("mails");
            if (!Offline) MailService.MarkRead(item.id, null, err => MetaUiRoot.Toast(err, Tone.Enemy));
        }

        public static void SaveDeck(List<string> deck, Action onSuccess, Action<string> onError)
        {
            string problem = DeckService.Validate(deck.ToArray());
            if (problem != null) { onError?.Invoke(problem); return; }
            if (Offline)
            {
                Deck = new List<string>(deck);
                Notify("deck");
                onSuccess?.Invoke();
                return;
            }
            DeckService.Save(deck.ToArray(), saved => { Deck = new List<string>(saved ?? deck.ToArray()); Notify("deck"); onSuccess?.Invoke(); }, onError);
        }

        public static string DeckProblem() => DeckService.Validate(Deck.ToArray());

        /// <summary>서버 없이 시작 — 로컬 덱(GameBootstrap.localDeck)으로 봇 연습</summary>
        public static void StartOffline(int[] localDeck)
        {
            Offline = true;
            Profile = new PlayerDto { nickname = "오프라인 마법사", rating = 1000 };
            if (Deck.Count == 0) Deck = new List<string>(DeckService.ToCodes(Array.FindAll(localDeck ?? new int[0], id => id > 0)));
            Notify("profile");
        }

        public static void SignOut()
        {
            if (!Offline) AuthService.Logout();
            Offline = false;
            Profile = null;
            Deck = new List<string>();
            Mails = new MailboxItemDto[0];
            Online.Clear();
            Match = null;
            LastMatch = null;
            BackendSessionService.Existing?.LeaveSession();
            Notify("user");
        }

        // ── 환경설정 (이 기기에 저장, 웹 wow.settings.v1 과 같은 항목) ─────────

        public static MetaSettings Settings
        {
            get
            {
                if (s_settings != null) return s_settings;
                s_settings = new MetaSettings();
                string json = PlayerPrefs.GetString(SettingsKey, "");
                if (!string.IsNullOrEmpty(json))
                {
                    try { JsonUtility.FromJsonOverwrite(json, s_settings); } catch (Exception) { /* 손상된 값은 기본값 */ }
                }
                return s_settings;
            }
        }

        public static void SaveSettings()
        {
            PlayerPrefs.SetString(SettingsKey, JsonUtility.ToJson(Settings));
            PlayerPrefs.Save();
            ApplySettings();
            Notify("settings");
        }

        public static void ResetSettings()
        {
            s_settings = new MetaSettings();
            SaveSettings();
        }

        /// <summary>게임에 바로 반영되는 항목: 효과음 크기, 멀미 방지 비네팅</summary>
        public static void ApplySettings()
        {
            AudioListener.volume = Mathf.Clamp01(Settings.sfx / 100f);
            var vignette = UnityEngine.Object.FindAnyObjectByType<Presentation.SnapVignette>();
            if (vignette != null) vignette.enabled = Settings.comfortVignette;
        }

        // ── 표시 도우미 ───────────────────────────────────────

        public static string TierOf(int rating) =>
            rating >= 1500 ? "대마법사" : rating >= 1300 ? "마법사" : rating >= 1100 ? "숙련 마법사" : rating >= 900 ? "견습 마법사" : "입문자";

        public static int WinRate(int wins, int losses) => wins + losses > 0 ? Mathf.RoundToInt(100f * wins / (wins + losses)) : 0;

        public static string Mmss(float sec)
        {
            int t = Mathf.Max(0, Mathf.FloorToInt(sec));
            return $"{t / 60}:{t % 60:00}";
        }

        public static string TimeAgo(string iso)
        {
            if (string.IsNullOrEmpty(iso) || !DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t))
                return "";
            double s = (DateTime.UtcNow - t).TotalSeconds;
            if (s < 60) return "방금 전";
            if (s < 3600) return $"{(int)(s / 60)}분 전";
            if (s < 86400) return $"{(int)(s / 3600)}시간 전";
            return $"{(int)(s / 86400)}일 전";
        }

        public static string ModeName(string mode) =>
            mode == MatchResultService.ModeRanked || mode == "matchmaking" ? "랭킹전" : mode == MatchResultService.ModeFriendly ? "친선전" : "봇 연습전";
    }

    /// <summary>스킬 19종 표시 정보 (웹 Backend/static/client/core.js 와 같은 내용)</summary>
    public static class MetaSkills
    {
        public struct Info
        {
            public string Code, Name, Kind, Effect, Rune;
            public bool High => Code[0] == 'H';
            public float Cooldown => High ? 20f : 5f;
        }

        public static readonly Dictionary<string, (string label, string aim)> Kinds = new Dictionary<string, (string, string)>
        {
            { "projectile", ("투사체", "열 지정") }, { "area", ("범위", "범위 지정 (열+깊이)") }, { "beam", ("관통 빔", "열 지정") },
            { "buff", ("버프", "즉시 발동") }, { "summon", ("소환", "소환 라인 (아군)") }, { "install", ("설치", "설치 구역 (타워·넥서스 앞)") },
            { "global", ("전역", "즉시 발동") },
        };

        public static readonly Info[] All =
        {
            I("S01", "연막", "projectile", "명중하면 상대 시야를 5초 동안 완전히 가린다", "눈"),
            I("S02", "독 장판", "area", "조준 지점에 독병을 던져 3열을 가로지르는 독 장판 (머물수록 피해 증가)", "삼각형 + X"),
            I("S03", "파이어볼", "area", "포물선으로 날아가 착탄 지점에 범위 폭발", "왕관"),
            I("S04", "화살 세례", "area", "한 레인의 지점 주변에 화살 10발이 쏟아진다", "세 갈래 화살"),
            I("S05", "토네이도", "beam", "레인을 쓸고 지나가는 회오리 — 닿은 적 유닛을 뒤로 밀어내고 피해", "소용돌이"),
            I("S06", "번개", "area", "지정 위치의 세로로 긴 범위(한 레인)에 낙뢰 3번", "번개"),
            I("S07", "총난사", "projectile", "열 전방 유닛부터 순차 연사 타격 (15발)", "가로줄 5개"),
            I("S08", "방어 쉴드", "buff", "피해 흡수 쉴드 생성 (피격 없을 시 HP 회복)", "방패"),
            I("S09", "타워 버프", "buff", "본인 타워 방어력 · 공격력 일시 강화 (타워 파괴 시 비활성화)", "#"),
            I("S10", "집중", "buff", "다음 스킬 1회 강화 (공격 피해 · 방어 지속 · 소환 체력 ×1.5)", "위쪽 화살표"),
            I("S11", "도끼 전사", "summon", "체력이 높은 근접 범위 공격 소환수", "X"),
            I("S12", "드래곤", "summon", "공중 비행 원거리 화염 소환수", "육각별"),
            I("S13", "터렛", "install", "적 유닛 요격용 고정 포탑 설치 (진영당 1기)", "I"),
            I("H01", "프리즈", "global", "적 전체 미니언 · 소환수 · 설치물 · 타워 7초간 정지 + 피해", "원 안 모래시계"),
            I("H02", "레이저 빔", "beam", "0.5초 예고선 후 4초간 유지되는 빔으로 열 전체 관통", "별표"),
            I("H03", "흡혈", "beam", "조준 레인의 적 플레이어 HP를 흡수 (못 맞히면 레인의 유닛 · 설치물에서)", "심장 박동 하트"),
            I("H04", "반사 결계", "buff", "9초간 상대 스킬 피해 무효화 및 100% 반사", "원 + 위 화살표"),
            I("H05", "분노", "area", "지정 위치 3열 분노 장판 — 들어온 아군 유닛 대폭 강화", "화난 얼굴"),
            I("H06", "소환탑", "install", "2.5초마다 해골을 내보내는 탑 설치", "집"),
        };

        private static Info I(string code, string name, string kind, string effect, string rune) =>
            new Info { Code = code, Name = name, Kind = kind, Effect = effect, Rune = rune };

        public static bool TryGet(string code, out Info info)
        {
            foreach (var s in All)
            {
                if (s.Code != code) continue;
                info = s;
                return true;
            }
            info = default;
            return false;
        }

        public static string Name(string code) => TryGet(code, out var i) ? i.Name : code;
        public static bool IsHigh(string code) => !string.IsNullOrEmpty(code) && code[0] == 'H';
    }
}
