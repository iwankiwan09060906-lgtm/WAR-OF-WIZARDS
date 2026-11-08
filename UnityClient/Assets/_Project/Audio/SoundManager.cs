// 사운드 재생 — Audio/Resources/Sounds 의 클립을 파일 이름으로 재생한다 (설명: Docs/Sound.md)
//   음량 = 클립 크기 보정(게이트 RMS 를 같은 크기로) × 분류 음량(SoundVolume) × 환경설정(효과음 / 배경음)
//   3D 효과음은 방향만 주고 거리 감쇠는 거의 없다 (Linear 10~300m) — 크기는 분류 음량(내 스킬 > 적 스킬)으로 정한다.
//   전용 서버 · 배치 모드에서는 아무 소리도 내지 않는다 (Enabled = false).

using System.Collections.Generic;
using SpellboundVR.UI;
using UnityEngine;

namespace SpellboundVR.Audio
{
    /// <summary>분류 음량 (0~1, 클립 크기 보정 뒤에 곱한다)</summary>
    public static class SoundVolume
    {
        public const float MySkill = 0.85f;
        public const float EnemySkill = 0.45f;
        public const float Flow = 0.8f;       // 카운트다운 · 돌파 · 넥서스 파괴 · 강화 · 연결 끊김
        public const float Voice = 0.9f;
        public const float Status = 0.6f;     // 피격 · 회피 · 심장박동
        public const float Ui = 0.45f;
        public const float Gesture = 0.4f;    // 내 손 제스처 (룬 인식 · 무장 · 조준 · 이동) — 내 것만 난다
        public const float Unit = 0.12f;      // 전장 유닛 · 타워 공격 (방해되지 않게 최소)
        public const float Music = 0.35f;
    }

    public sealed class SoundManager : MonoBehaviour
    {
        private const float TargetRms = 0.1f; // -20 dBFS
        private const int Voices = 24;
        private const float MusicFadeSeconds = 1.5f;
        private const float LoopFadeSeconds = 0.35f;

        /// <summary>false = 소리를 내지 않는다 (PC 전용 서버 창)</summary>
        public static bool Enabled = true;

        private sealed class Keyed
        {
            public AudioSource Source;
            public float Volume;
            public float StopAt = float.MaxValue;
            public bool Stopping;
        }

        private struct Delayed
        {
            public float At;
            public string Name;
            public float Volume;
            public Vector3? Position;
        }

        private static SoundManager s_instance;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, float> _gain = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _lastPlay = new Dictionary<string, float>();
        private readonly Dictionary<string, Keyed> _keyed = new Dictionary<string, Keyed>();
        private readonly List<string> _keyBuffer = new List<string>();
        private readonly List<Delayed> _delayed = new List<Delayed>();
        private readonly AudioSource[] _music = new AudioSource[2];
        private AudioSource[] _voices;
        private AudioSource _ui;
        private int _nextVoice;
        private int _musicIndex;
        private string _musicName;

        private static SoundManager Instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                if (!Enabled || Application.isBatchMode) return null;
                var go = new GameObject("[Sound]");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<SoundManager>();
                return s_instance;
            }
        }

        private static float SfxSetting => Mathf.Clamp01(MetaState.Settings.sfx / 100f);
        private static float BgmSetting => Mathf.Clamp01(MetaState.Settings.bgm / 100f);

        private void Awake()
        {
            _voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++) _voices[i] = NewSource("Voice" + i);
            _ui = NewSource("Ui");
            for (int i = 0; i < 2; i++)
            {
                _music[i] = NewSource("Music" + i);
                _music[i].loop = true;
                _music[i].priority = 0;
            }
        }

        private AudioSource NewSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 10f;
            s.maxDistance = 300f;
            return s;
        }

        // ── 공개 API ─────────────────────────────────────────

        /// <summary>한 번 재생. at = 3D 위치 (null = 머리 안). minInterval 안에 같은 소리를 또 부르면 무시. 재생했으면 true</summary>
        public static bool Play(string name, float volume, Vector3? at = null, float minInterval = 0.05f, float pitch = 1f)
        {
            var m = Instance;
            if (m == null || volume <= 0f) return false;
            float now = Time.unscaledTime;
            if (m._lastPlay.TryGetValue(name, out float last) && now - last < minInterval) return false;
            var clip = m.Clip(name);
            if (clip == null) return false;
            m._lastPlay[name] = now;
            var s = m._voices[m._nextVoice];
            m._nextVoice = (m._nextVoice + 1) % Voices;
            m.Setup(s, clip, name, volume, at, pitch, false);
            s.Play();
            return true;
        }

        /// <summary>메뉴 소리 — 한 채널이라 같은 순간의 클릭 + 룬 선택은 뒤의 것만 들린다</summary>
        public static void PlayUi(string name, float volume = SoundVolume.Ui)
        {
            var m = Instance;
            var clip = m != null ? m.Clip(name) : null;
            if (clip == null) return;
            m.Setup(m._ui, clip, name, volume, null, 1f, false);
            m._ui.Play();
        }

        /// <summary>delay 초 뒤에 한 번 재생 (지속 스킬의 종료음 등)</summary>
        public static void PlayLater(float delay, string name, float volume, Vector3? at = null)
        {
            var m = Instance;
            m?._delayed.Add(new Delayed { At = Time.unscaledTime + delay, Name = name, Volume = volume, Position = at });
        }

        /// <summary>key 로 다루는 소리 시작 (이미 재생 중이면 그대로 두고 음량 · 끝 시각만 갱신). seconds > 0 이면 그 뒤 페이드아웃</summary>
        public static void StartKeyed(string key, string name, float volume, Vector3? at = null, float seconds = 0f, bool loop = true, float pitch = 1f)
        {
            var m = Instance;
            if (m == null) return;
            if (!m._keyed.TryGetValue(key, out var k))
            {
                var clip = m.Clip(name);
                if (clip == null) return;
                k = new Keyed { Source = m.NewSource("Keyed_" + key) };
                m.Setup(k.Source, clip, name, volume, at, pitch, loop);
                k.Source.Play();
                m._keyed[key] = k;
            }
            k.Volume = volume * m.GainOf(name, k.Source.clip);
            k.Stopping = false;
            k.StopAt = seconds > 0f ? Time.unscaledTime + seconds : float.MaxValue;
        }

        public static void StopKeyed(string key)
        {
            if (s_instance != null && s_instance._keyed.TryGetValue(key, out var k)) k.Stopping = true;
        }

        public static bool IsKeyedPlaying(string key) => s_instance != null && s_instance._keyed.TryGetValue(key, out var k) && !k.Stopping;

        /// <summary>경기 끝 · 화면 전환: 모든 지속음과 예약음을 끈다</summary>
        public static void StopAllKeyed()
        {
            if (s_instance == null) return;
            foreach (var k in s_instance._keyed.Values) k.Stopping = true;
            s_instance._delayed.Clear();
        }

        /// <summary>배경음 크로스페이드 (같은 곡이면 그대로). keepTime = 이전 곡의 재생 위치를 이어받음 (전투 3곡은 템포 · 길이가 같다)</summary>
        public static void Music(string name, bool keepTime = false)
        {
            var m = Instance;
            if (m == null || name == m._musicName) return;
            var clip = m.Clip(name);
            if (clip == null) return;
            var old = m._music[m._musicIndex];
            m._musicIndex = 1 - m._musicIndex;
            var next = m._music[m._musicIndex];
            next.clip = clip;
            next.volume = 0f;
            next.spatialBlend = 0f;
            next.time = keepTime && old.isPlaying && old.clip != null ? old.time % clip.length : 0f;
            next.Play();
            m._musicName = name;
        }

        public static string CurrentMusic => s_instance != null ? s_instance._musicName : null;

        // ── 내부 ─────────────────────────────────────────────

        private AudioClip Clip(string name)
        {
            if (_clips.TryGetValue(name, out var c)) return c;
            c = Resources.Load<AudioClip>("Sounds/" + name);
            if (c == null) Debug.LogWarning("[Sound] 클립 없음: " + name);
            _clips[name] = c;
            return c;
        }

        private void Setup(AudioSource s, AudioClip clip, string name, float volume, Vector3? at, float pitch, bool loop)
        {
            s.clip = clip;
            s.loop = loop;
            s.pitch = pitch;
            s.volume = Mathf.Clamp01(volume * GainOf(name, clip) * SfxSetting);
            bool spatial = at.HasValue && MetaState.Settings.spatialAudio;
            s.spatialBlend = spatial ? 1f : 0f;
            if (spatial) s.transform.position = at.Value;
            else s.transform.localPosition = Vector3.zero;
        }

        /// <summary>클립마다 크기를 맞추는 배율 (처음 한 번 계산). 배경음 · 데이터를 못 읽는 클립은 1</summary>
        private float GainOf(string name, AudioClip clip)
        {
            if (_gain.TryGetValue(name, out float g)) return g;
            g = 1f;
            if (!name.StartsWith("BGM_") && clip.loadType == AudioClipLoadType.DecompressOnLoad)
            {
                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    clip.LoadAudioData();
                    return g; // 다음 재생 때 계산
                }
                var data = new float[clip.samples * clip.channels];
                if (clip.GetData(data, 0))
                {
                    float rms = GatedRms(data, Mathf.Max(1, clip.frequency * clip.channels / 20));
                    if (rms > 1e-4f) g = Mathf.Clamp(TargetRms / rms, 0.3f, 3f);
                }
            }
            _gain[name] = g;
            return g;
        }

        /// <summary>50ms 블록 중 -40dB 보다 큰 블록만 모은 RMS (앞뒤 무음 · 긴 꼬리 때문에 작게 재지 않도록)</summary>
        public static float GatedRms(float[] data, int block)
        {
            double sum = 0;
            long n = 0;
            for (int start = 0; start < data.Length; start += block)
            {
                int end = Mathf.Min(data.Length, start + block);
                double b = 0;
                for (int i = start; i < end; i++) b += data[i] * data[i];
                if (b / (end - start) <= 1e-4) continue;
                sum += b;
                n += end - start;
            }
            return n > 0 ? (float)System.Math.Sqrt(sum / n) : 0f;
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            float sfx = SfxSetting;

            for (int i = _delayed.Count - 1; i >= 0; i--)
            {
                if (now < _delayed[i].At) continue;
                var d = _delayed[i];
                _delayed.RemoveAt(i);
                Play(d.Name, d.Volume, d.Position, 0f);
            }

            _keyBuffer.Clear();
            foreach (var kv in _keyed)
            {
                var k = kv.Value;
                if (now >= k.StopAt) k.Stopping = true;
                var s = k.Source;
                float target = k.Stopping ? 0f : Mathf.Clamp01(k.Volume * sfx);
                s.volume = Mathf.MoveTowards(s.volume, target, dt / LoopFadeSeconds);
                if ((k.Stopping && s.volume <= 0f) || (!s.loop && !s.isPlaying)) _keyBuffer.Add(kv.Key);
            }
            foreach (var key in _keyBuffer)
            {
                Destroy(_keyed[key].Source.gameObject);
                _keyed.Remove(key);
            }

            float bgm = SoundVolume.Music * BgmSetting;
            for (int i = 0; i < 2; i++)
            {
                var s = _music[i];
                if (!s.isPlaying) continue;
                float target = i == _musicIndex ? bgm : 0f;
                s.volume = Mathf.MoveTowards(s.volume, target, dt / MusicFadeSeconds);
                if (i != _musicIndex && s.volume <= 0f) s.Stop();
            }
        }
    }
}
