using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace WarOfWizards.API
{
    // ════════════════════════════════════════════════════════════════════
    //  DB → Unity 데이터 받기
    //
    //  1) 게임 시작 시 한 번:   LiveApi.LoadBootstrap(data => { ... });
    //     프로필 · 덱 · 스킬 19종 · 게임 설정 · 안 읽은 우편 수 · 친구를 한 번에 받는다.
    //  2) 실시간:               LiveEventStream.Instance.Connect();
    //     서버가 보내는 우편/초대/친구/공지/설정 변경/관리자 메시지를 계속 받는다.
    //  3) 설정값 읽기:           GameConfigStore.GetFloat("cooldown.normal", 5f)
    //     관리자 페이지에서 값을 바꾸면 GameConfigStore.OnChanged 가 호출된다.
    //
    //  서버: SpringServer/.../controller/LiveController.java
    // ════════════════════════════════════════════════════════════════════

    /// <summary>게임 설정 1개 (서버 game_configs 테이블). value 는 문자열, type 은 NUMBER / TEXT / BOOL</summary>
    [Serializable]
    public class ConfigEntryDto
    {
        public string key;
        public string value;
        public string type;
        public string category;
        public string description;
        public long version;
    }

    /// <summary>
    /// 실시간 이벤트 1개.
    /// type: hello / config / mail / invite / notice / friend / broadcast / (관리자가 만든 이름, 예: event_start)
    /// payload: 추가 데이터 JSON 문자열 → JsonUtility.FromJson&lt;내클래스&gt;(payload) 로 한 번 더 읽는다.
    /// </summary>
    [Serializable]
    public class LiveEventDto
    {
        public string type;
        public string title;
        public string message;
        public string payload;
        public long serverTime;
    }

    /// <summary>GET /api/live/bootstrap 응답</summary>
    [Serializable]
    public class LiveBootstrapDto
    {
        public long serverTime;
        public ProfileDto profile;
        public string[] deck;
        public SkillDto[] skills;
        public ConfigEntryDto[] configs;
        public long unreadMail;
        public FriendDto[] friends;
        public long[] onlineFriendIds;
        public string eventsUrl;
    }

    // payload 예시 클래스 (필요한 것만 골라 쓰면 된다)
    [Serializable] public class InvitePayload { public string roomCode; public string from; public long fromPlayerId; }
    [Serializable] public class FriendPayload { public string action; public long playerId; public string nickname; }
    [Serializable] public class MailPayload { public string reward; }

    public static class LiveApi
    {
        /// <summary>게임 시작 데이터 한 번에 받기. 받은 설정은 GameConfigStore 에도 자동으로 들어간다.</summary>
        public static void LoadBootstrap(Action<LiveBootstrapDto> onSuccess, Action<string> onError = null)
        {
            ApiClient.Instance.Get<LiveBootstrapDto>("/api/live/bootstrap", data =>
            {
                if (data != null) GameConfigStore.Apply(data.configs);
                onSuccess?.Invoke(data);
            }, onError);
        }

        /// <summary>게임 설정만 다시 받기</summary>
        public static void LoadConfig(Action<ConfigEntryDto[]> onSuccess = null, Action<string> onError = null)
        {
            ApiClient.Instance.GetArray<ConfigEntryDto>("/api/live/config", entries =>
            {
                GameConfigStore.Apply(entries);
                onSuccess?.Invoke(entries);
            }, onError);
        }
    }

    /// <summary>
    /// 서버 게임 설정 보관소. 값이 없으면 기본값을 돌려주므로 서버 연결 전에도 안전하게 쓸 수 있다.
    ///   float cd = GameConfigStore.GetFloat("cooldown.normal", 5f);
    ///   GameConfigStore.OnChanged += key => { if (key == "maintenance") ShowMaintenance(); };
    /// </summary>
    public static class GameConfigStore
    {
        private static readonly Dictionary<string, ConfigEntryDto> entries = new Dictionary<string, ConfigEntryDto>();

        /// <summary>값이 바뀐 설정 키 (처음 받을 때도 호출)</summary>
        public static event Action<string> OnChanged;

        public static bool IsLoaded => entries.Count > 0;

        public static void Apply(ConfigEntryDto[] list)
        {
            if (list == null) return;
            foreach (ConfigEntryDto e in list)
            {
                if (e == null || string.IsNullOrEmpty(e.key)) continue;
                bool changed = !entries.TryGetValue(e.key, out ConfigEntryDto old) || old.value != e.value;
                entries[e.key] = e;
                if (changed) OnChanged?.Invoke(e.key);
            }
        }

        public static string GetString(string key, string fallback = "")
        {
            return entries.TryGetValue(key, out ConfigEntryDto e) && e.value != null ? e.value : fallback;
        }

        public static float GetFloat(string key, float fallback)
        {
            return entries.TryGetValue(key, out ConfigEntryDto e)
                   && float.TryParse(e.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }

        public static int GetInt(string key, int fallback)
        {
            return Mathf.RoundToInt(GetFloat(key, fallback));
        }

        public static bool GetBool(string key, bool fallback)
        {
            return entries.TryGetValue(key, out ConfigEntryDto e) && bool.TryParse(e.value, out bool v) ? v : fallback;
        }
    }
}
