// 상대의 시전 준비 표시 (PlayerSnap.CastPhase · 표시 전용, 판정과 무관)
//   · 아바타 앞 허공에 룬이 그려지고, 오른팔이 펜 끝을 따라 움직인다.
//       Drawing   : 상대가 실제로 그린 손끝 궤적 (아직 스킬을 모르므로 중립 색)
//       Preparing : 그린 궤적이 있으면 스킬 색으로 바뀌고, 없으면(손 모양 · 슬롯 키 · 봇) 그 스킬의 룬을 진행률만큼 그려 나간다
//   · 발밑 마법진(VFX_Cast_Circle)이 시전 진행률만큼 커지고, 펜 끝에 불꽃(VFX_Cast_Pen)이 붙고, 머리 위에 스킬 이름이 뜬다.
//     조준을 시작하면 아바타가 노리는 레인 쪽으로 돌아선다.
//   · 다 그린 룬이 시전되면 마법진(VFX_Cast_Release)이 터지며 룬이 퍼져 사라진다. 취소되면 조용히 사라진다.
//   · 41m 밖 상대도 보이도록 룬은 몸 크기보다 크게 그리고, 팔은 룬의 펜 끝을 가리킨다.
//   · 룬 선은 파티클 재질에 세로로 번지는 빛 텍스처를 입혀 빛나는 띠로 그린다.
//   · 아바타 위치는 ArenaStateView 가 정한다 — 그 뒤에, 애니메이터가 뼈를 움직인 뒤에 팔을 덮어쓴다.

using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Gesture;
using SpellboundVR.Network;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    [DefaultExecutionOrder(100)]
    public sealed class CastPreviewView : MonoBehaviour
    {
        private const int MaxStrokePoints = 256;
        private const int GlowTextureHeight = 32;
        /// <summary>이만큼 그린 룬이 사라지면 시전된 것으로 본다</summary>
        private const float ReleaseProgress = 0.9f;
        /// <summary>봇 룬 획의 흔들림 (룬 칸 비율) — 정해진 모양 그대로가 아니라 사람 손처럼 삐뚤게</summary>
        private const float StrokeNoise = 0.16f;
        /// <summary>펜 끝 불꽃 크기 (프리팹 지름 10m 기준)</summary>
        private const float PenScale = 0.07f;
        /// <summary>룬 선 재질을 빌려 올 프리팹 — 트레일에 파티클 빛 재질을 쓴다</summary>
        private const string TrailSource = "VFX_S04_ArrowRain_Projectile";

        private sealed class Seat
        {
            public Team Team;
            public Transform Rune;
            public Transform Strokes;
            public Transform Pen;
            public GameObject PenFx;
            public GameObject Circle;
            public Vector3 CircleScale;
            public TextMesh Label;
            public readonly List<LineRenderer> Lines = new List<LineRenderer>(8);
            public int LinesUsed;

            public Transform Avatar;
            public Transform UpperArm, ForeArm, Hand;
            public bool ArmSearched;

            public bool Active;
            /// <summary>시전 · 취소 뒤 사라지는 중 (1 → 0)</summary>
            public float Fade;
            public float Weight;
            public float Yaw;
            public int SpellId;
            public float Shown;
            public bool RealStrokes;
            public int Stroke;
            public Vector3 PenLocal;
            public Vector2 Min, Max;
            /// <summary>이번 룬의 흔들림 시드 (그릴 때마다 다르다)</summary>
            public float Noise;
        }

        [Tooltip("허공에 그리는 룬 크기 (m) — 멀리서도 보이도록 몸보다 크게")]
        public float runeSize = 3.2f;
        public float lineWidth = 0.28f;
        [Tooltip("룬 중심 높이 (아바타 크기 배율 기준)")]
        public float runeHeight = 1.55f;
        [Tooltip("룬이 아바타 앞으로 떨어진 거리 (아바타 크기 배율 기준)")]
        public float runeForward = 0.95f;
        [Tooltip("스킬을 아직 모를 때의 선 색")]
        public Color neutralColor = new Color(0.6f, 0.85f, 1f, 1f);
        public float fadeSeconds = 0.45f;

        private ClientSession _session;
        private ArenaStateView _view;
        private VFXPlayer _vfx;
        private VFXCatalog _catalog;
        private Transform _head;
        private Material _lineMaterial;
        private Texture2D _glow;
        private readonly List<Seat> _seats = new List<Seat>(4);
        /// <summary>스킬별 룬 획 (룬 칸 -0.5~0.5, 가로세로 비율 유지)</summary>
        private readonly Dictionary<int, Vector3[][]> _shapes = new Dictionary<int, Vector3[][]>();

        public void Initialize(ClientSession session, ArenaStateView view, Transform head, VFXCatalog catalog)
        {
            _session = session;
            _view = view;
            _head = head;
            _catalog = catalog;
            _vfx = GetComponent<VFXPlayer>();
            _lineMaterial = CreateLineMaterial();
            for (int t = 0; t < session.Arena.TeamCount; t++) _seats.Add(Build((Team)t));
        }

        /// <summary>파티클 재질(가산 혼합)을 복제하고, 가운데가 밝고 가장자리로 번지는 텍스처를 입힌다 — 선 길이와 상관없이 빛나는 띠가 된다</summary>
        private Material CreateLineMaterial()
        {
            Material source = null;
            if (_catalog != null && _catalog.TryGet(TrailSource, out var prefab))
            {
                var trail = prefab.GetComponentInChildren<TrailRenderer>(true);
                if (trail != null) source = trail.sharedMaterial;
            }
            if (source == null) return FallbackVisuals.Transparent(Color.white);

            _glow = new Texture2D(1, GlowTextureHeight, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "CastRuneGlow" };
            for (int y = 0; y < GlowTextureHeight; y++)
            {
                float d = Mathf.Abs((y + 0.5f) / GlowTextureHeight * 2f - 1f);
                float a = Mathf.Pow(1f - d, 2f);
                _glow.SetPixel(0, y, new Color(a, a, a, a));
            }
            _glow.Apply();
            return new Material(source) { mainTexture = _glow, name = "CastRuneLine" };
        }

        private GameObject Spawn(string prefabName, Transform parent)
        {
            if (_catalog == null || !_catalog.TryGet(prefabName, out var prefab)) return null;
            return Instantiate(prefab, parent, false);
        }

        private Seat Build(Team team)
        {
            var seat = new Seat { Team = team };
            var rune = new GameObject("CastRune_" + team).transform;
            rune.SetParent(transform, false);
            seat.Rune = rune;
            seat.Strokes = new GameObject("Strokes").transform;
            seat.Strokes.SetParent(rune, false);
            seat.Pen = new GameObject("Pen").transform;
            seat.Pen.SetParent(rune, false);
            seat.PenFx = Spawn("VFX_Cast_Pen", seat.Pen);
            if (seat.PenFx != null) seat.PenFx.transform.localScale = Vector3.one * PenScale;
            seat.Circle = Spawn("VFX_Cast_Circle", rune);
            if (seat.Circle != null) seat.CircleScale = seat.Circle.transform.localScale;
            seat.Label = FallbackVisuals.Text(rune, "", 0.1f, Color.white);
            rune.gameObject.SetActive(false);
            return seat;
        }

        private void LateUpdate()
        {
            var snapshot = _session?.Events.Latest;
            if (snapshot == null) return;
            for (int i = 0; i < _seats.Count; i++) Tick(_seats[i], ref snapshot.Players[(int)_seats[i].Team]);
        }

        private void Tick(Seat seat, ref PlayerSnap player)
        {
            var avatar = _view.VisibleAvatar(seat.Team);
            var phase = (CastPreviewPhase)player.CastPhase;
            if (avatar == null)
            {
                Hide(seat);
                return;
            }
            if (avatar != seat.Avatar) BindAvatar(seat, avatar);

            bool casting = phase != CastPreviewPhase.None;
            if (casting && (!seat.Active || seat.Fade > 0f)) Begin(seat);
            if (!casting && seat.Active && seat.Fade <= 0f) Release(seat);
            if (!seat.Active) return;

            float dt = Time.deltaTime;
            if (casting) UpdateRune(seat, phase, ref player, dt);
            else
            {
                seat.Fade -= dt / Mathf.Max(0.05f, fadeSeconds);
                if (seat.Fade <= 0f)
                {
                    Hide(seat);
                    return;
                }
            }

            seat.Weight = Mathf.MoveTowards(seat.Weight, casting ? 1f : 0f, dt * 6f);
            TurnToLane(seat, casting ? player.CastLane : PlayerSnap.NoCastLane, dt);
            Place(seat);
            Paint(seat);
            PointArm(seat);
        }

        private void BindAvatar(Seat seat, Transform avatar)
        {
            seat.Avatar = avatar;
            if (seat.ArmSearched) return;
            seat.ArmSearched = true;
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "PT_RightArm") seat.UpperArm = t;
                else if (t.name == "PT_RightForeArm") seat.ForeArm = t;
                else if (t.name == "PT_RightHand") seat.Hand = t;
            }
        }

        private void Begin(Seat seat)
        {
            ClearLines(seat);
            seat.Active = true;
            seat.Fade = 0f;
            seat.SpellId = 0;
            seat.Shown = 0f;
            seat.RealStrokes = false;
            seat.Stroke = -1;
            seat.PenLocal = Vector3.zero;
            seat.Min = seat.Max = Vector2.zero;
            seat.Noise = Random.Range(0f, 1000f);
            seat.Strokes.localPosition = Vector3.zero;
            seat.Strokes.localScale = Vector3.one;
            seat.Label.text = "";
            VfxTint.Apply(seat.Circle, neutralColor);
            seat.Rune.gameObject.SetActive(true);
        }

        /// <summary>시전 또는 취소 — 다 그린 룬이면 마법진이 터진다</summary>
        private void Release(Seat seat)
        {
            seat.Fade = 1f;
            if (_vfx != null && seat.Shown >= ReleaseProgress)
                _vfx.PlayNamed("VFX_Cast_Release", seat.Avatar.position + Vector3.up * 0.1f, Quaternion.identity, 1.5f);
        }

        private void Hide(Seat seat)
        {
            if (!seat.Active) return;
            seat.Active = false;
            seat.Fade = 0f;
            seat.Weight = 0f;
            seat.Yaw = 0f;
            ClearLines(seat);
            seat.Rune.gameObject.SetActive(false);
        }

        private void ClearLines(Seat seat)
        {
            for (int i = 0; i < seat.LinesUsed; i++) seat.Lines[i].positionCount = 0;
            seat.LinesUsed = 0;
        }

        private void UpdateRune(Seat seat, CastPreviewPhase phase, ref PlayerSnap player, float dt)
        {
            if (player.CastSpellId != seat.SpellId)
            {
                seat.SpellId = player.CastSpellId;
                var def = _session.Catalog.Get(seat.SpellId);
                seat.Label.text = def != null ? def.DisplayName : "";
                VfxTint.Apply(seat.Circle, def != null ? def.ThemeColor : neutralColor);
            }
            float target = player.CastProgress / 255f;
            seat.Shown = Mathf.MoveTowards(seat.Shown, target, dt * 3f);

            if (phase == CastPreviewPhase.Drawing)
            {
                AddPenPoint(seat, player.PenX / 100f, player.PenY / 100f, player.PenStroke);
            }
            else if (!seat.RealStrokes)
            {
                TraceShape(seat, seat.Shown);
            }
        }

        /// <summary>상대가 실제로 그린 손끝 궤적 — 획마다 선 하나, 그린 범위가 룬 칸 가운데 오도록 옮긴다</summary>
        private void AddPenPoint(Seat seat, float x, float y, int stroke)
        {
            seat.RealStrokes = true;
            if (stroke != seat.Stroke)
            {
                seat.Stroke = stroke;
                NextLine(seat);
            }
            var point = new Vector3(x, y, 0f) * runeSize * 0.5f;
            seat.PenLocal = point;
            seat.Min = Vector2.Min(seat.Min, point);
            seat.Max = Vector2.Max(seat.Max, point);
            seat.Strokes.localPosition = -(Vector3)((seat.Min + seat.Max) * 0.5f);

            var line = seat.Lines[seat.LinesUsed - 1];
            int count = line.positionCount;
            if (count >= MaxStrokePoints) return;
            if (count > 0 && (line.GetPosition(count - 1) - point).sqrMagnitude < 1e-4f) return;
            line.positionCount = count + 1;
            line.SetPosition(count, point);
        }

        /// <summary>그린 궤적이 없을 때 — 그 스킬의 룬을 진행률만큼 획 순서대로 그려 나간다</summary>
        private void TraceShape(Seat seat, float progress)
        {
            var shape = ShapeOf(seat.SpellId);
            if (shape == null) return;
            int total = 0;
            for (int s = 0; s < shape.Length; s++) total += shape[s].Length;
            float remaining = Mathf.Clamp01(progress) * total;
            for (int s = 0; s < shape.Length && remaining > 0f; s++)
            {
                if (s >= seat.LinesUsed) NextLine(seat);
                var line = seat.Lines[s];
                var points = shape[s];
                int count = Mathf.Min(points.Length, Mathf.CeilToInt(remaining));
                for (int i = line.positionCount; i < count; i++)
                {
                    line.positionCount = i + 1;
                    line.SetPosition(i, (points[i] + Jitter(seat, s, i)) * runeSize);
                }
                int last = Mathf.Max(0, count - 1);
                seat.PenLocal = (points[last] + Jitter(seat, s, last)) * runeSize;
                remaining -= points.Length;
            }
        }

        /// <summary>획을 따라 부드럽게 변하는 오차 + 획마다 조금 어긋난 시작점</summary>
        private static Vector3 Jitter(Seat seat, int stroke, int index)
        {
            float t = index * 0.18f, k = seat.Noise + stroke * 3.7f;
            return new Vector3(Mathf.PerlinNoise(k, t) - 0.5f, Mathf.PerlinNoise(t + 31f, k) - 0.5f, 0f) * StrokeNoise;
        }

        private Vector3[][] ShapeOf(int spellId)
        {
            if (_shapes.TryGetValue(spellId, out var shape)) return shape;
            if (RuneDesigns.TryGet(spellId, out var design))
            {
                var strokes = design.Strokes;
                Vector2 min = strokes[0][0], max = min;
                foreach (var stroke in strokes)
                    foreach (var p in stroke)
                    {
                        min = Vector2.Min(min, p);
                        max = Vector2.Max(max, p);
                    }
                Vector2 center = (min + max) * 0.5f;
                float size = Mathf.Max(0.01f, Mathf.Max(max.x - min.x, max.y - min.y));
                shape = new Vector3[strokes.Length][];
                for (int s = 0; s < strokes.Length; s++)
                {
                    shape[s] = new Vector3[strokes[s].Length];
                    for (int i = 0; i < strokes[s].Length; i++) shape[s][i] = (strokes[s][i] - center) / size;
                }
            }
            _shapes[spellId] = shape;
            return shape;
        }

        private void NextLine(Seat seat)
        {
            if (seat.LinesUsed == seat.Lines.Count)
            {
                var go = new GameObject("Stroke" + seat.LinesUsed);
                go.transform.SetParent(seat.Strokes, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.positionCount = 0;
                line.widthMultiplier = lineWidth;
                line.numCapVertices = 4;
                line.numCornerVertices = 2;
                line.sharedMaterial = _lineMaterial;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                seat.Lines.Add(line);
            }
            seat.Lines[seat.LinesUsed++].positionCount = 0;
        }

        /// <summary>조준을 시작하면 노리는 레인 쪽으로 돌아선다 (1:1 은 레인이 평행해서 정면 그대로)</summary>
        private void TurnToLane(Seat seat, byte lane, float dt)
        {
            bool aiming = lane != PlayerSnap.NoCastLane && lane < _session.Arena.LaneCount;
            seat.Yaw = Mathf.MoveTowards(seat.Yaw, aiming ? 1f : 0f, dt * 4f);
            if (!aiming || seat.Yaw <= 0f) return;
            var arena = _session.Arena;
            var facing = Quaternion.LookRotation(arena.ToWorldDirection(arena.GetLane(seat.Team, lane).Dir), Vector3.up);
            seat.Avatar.rotation = Quaternion.Slerp(seat.Avatar.rotation, facing, seat.Yaw);
        }

        private void Place(Seat seat)
        {
            float scale = _view.AvatarWorldScale;
            Vector3 forward = seat.Avatar.forward;
            Vector3 feet = seat.Avatar.position;
            seat.Rune.SetPositionAndRotation(feet + Vector3.up * (runeHeight * scale) + forward * (runeForward * scale),
                                             Quaternion.LookRotation(forward, Vector3.up));
            seat.Pen.localPosition = seat.Strokes.localPosition + seat.PenLocal;

            // 시전되면 룬이 퍼지며 사라진다
            float burst = seat.Fade > 0f ? 1f + (1f - seat.Fade) * 0.45f : 1f;
            seat.Strokes.localScale = Vector3.one * burst;

            if (seat.Circle != null)
            {
                seat.Circle.transform.SetPositionAndRotation(feet + Vector3.up * 0.05f, Quaternion.identity);
                seat.Circle.transform.localScale = seat.CircleScale * (Mathf.Lerp(0.4f, 1f, seat.Shown) * burst);
            }

            seat.Label.transform.position = feet + Vector3.up * (runeHeight * scale + runeSize * 0.5f + 0.6f);
            if (_head != null)
            {
                Vector3 toHead = seat.Label.transform.position - _head.position;
                if (toHead.sqrMagnitude > 1e-6f) seat.Label.transform.rotation = Quaternion.LookRotation(toHead, Vector3.up);
            }
        }

        private void Paint(Seat seat)
        {
            var def = seat.SpellId > 0 ? _session.Catalog.Get(seat.SpellId) : null;
            Color color = def != null ? def.ThemeColor : neutralColor;
            color = Color.Lerp(color, Color.white, 0.2f + 0.1f * Mathf.Sin(Time.time * 8f));
            color.a = seat.Fade > 0f ? seat.Fade : 1f;
            for (int i = 0; i < seat.LinesUsed; i++)
            {
                seat.Lines[i].startColor = color;
                seat.Lines[i].endColor = color;
            }
            seat.Label.color = color;
            if (seat.PenFx != null)
            {
                bool drawing = seat.Fade <= 0f && (seat.Shown < 1f || seat.RealStrokes);
                if (seat.PenFx.activeSelf != drawing) seat.PenFx.SetActive(drawing);
            }
        }

        /// <summary>오른팔을 펜 끝 쪽으로 뻗는다 (뼈 축과 상관없는 2관절 IK) — 대체 아바타는 뼈가 없어 건너뛴다</summary>
        private void PointArm(Seat seat)
        {
            if (seat.UpperArm == null || seat.ForeArm == null || seat.Hand == null || seat.Weight <= 0f) return;
            Vector3 shoulder = seat.UpperArm.position;
            Vector3 elbow = seat.ForeArm.position;
            Vector3 wrist = seat.Hand.position;
            float upperLength = Vector3.Distance(shoulder, elbow);
            float foreLength = Vector3.Distance(elbow, wrist);
            float reach = (upperLength + foreLength) * 0.92f;
            if (reach <= 1e-4f) return;

            Vector3 toPen = seat.Pen.position - shoulder;
            if (toPen.sqrMagnitude < 1e-6f) return;
            Vector3 dir = toPen.normalized;
            Vector3 target = shoulder + dir * reach;

            // 어깨-팔꿈치-손목 삼각형: 팔꿈치는 아래 · 바깥쪽으로 굽힌다
            float cos = Mathf.Clamp((upperLength * upperLength + reach * reach - foreLength * foreLength) / (2f * upperLength * reach), -1f, 1f);
            float sin = Mathf.Sqrt(1f - cos * cos);
            Vector3 bend = Vector3.ProjectOnPlane(seat.Avatar.right - Vector3.up, dir).normalized;
            Vector3 wantElbow = shoulder + dir * (upperLength * cos) + bend * (upperLength * sin);

            Quaternion upperAim = Quaternion.FromToRotation(elbow - shoulder, wantElbow - shoulder) * seat.UpperArm.rotation;
            seat.UpperArm.rotation = Quaternion.Slerp(seat.UpperArm.rotation, upperAim, seat.Weight);
            elbow = seat.ForeArm.position;
            wrist = seat.Hand.position;
            Quaternion foreAim = Quaternion.FromToRotation(wrist - elbow, target - elbow) * seat.ForeArm.rotation;
            seat.ForeArm.rotation = Quaternion.Slerp(seat.ForeArm.rotation, foreAim, seat.Weight);
        }

        private void OnDestroy()
        {
            if (_glow == null) return;
            Destroy(_lineMaterial);
            Destroy(_glow);
        }
    }
}
