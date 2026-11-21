// 4인 개인전 전장 표시 — 씬 아트는 1:1 직사각형 전장이라, 개인전이면 실행 중에 마름모 전장으로 바꾼다.
//   · 씬의 벽 · 바닥 · 장식 · 1:1 레인을 끄고 (먼 절벽 · 하늘 · 안개 · 1번 · 3번 발판과 구조물은 그대로 쓴다)
//   · 1:1 전장처럼: 레인 6개 · 발판 자리 · 가운데가 돌 바닥(1:1 바닥 재질)을 인 바위 덩어리(1:1 절벽 재질)이고,
//     아래로 좁아지며 안개 낀 심연으로 사라진다. 레인 가장자리를 따라 1:1 둘레의 낮은 벽이 서고 (벽 위에 등불 기둥 · 폐허 조각), 바닥에는 마법 문양이 은은하게 빛난다
//   · 2번 · 4번 발판을 1번 발판을 복제해 세우고, 3번 발판은 개인전 거리로 옮긴다 (TeamArt 가 보는 사람 기준 색을 입힌다)
//   · 발판 번호판 · 마나 폭풍이 시작되면 레인 테두리가 보라색으로 빛난다
//   · 장식: 레인 사이 빈 삼각형마다 낮은 폐허 섬, 마름모 바깥마다 성채 섬, 심연에 떠다니는 바위 (충돌체 없음 · 레인 밖)
// 시연 전장(ArenaGeometry.FilledPolygon): 정N각형 안을 통째로 돌 바닥으로 채우고(발판까지 덮는 깎은 다각형 섬), 모든 두 자리 사이가 레인
//   (대각선도 소환 · 웨이브 길) — 레인 선은 평소엔 숨기고 그 레인을 조준하는 동안만 보인다. 가운데 심연 · 룬 핵 · 레인 벽 · 바닥 문양은 없다.
// 다인전(정N각형)도 이 컴포넌트가 그린다: 변 레인 = 돌다리(벽 · 등불), 대각선 = 가운데 심연을 가로지르는 빛줄기(직접 공격 길),
//   (대각선 빛줄기는 평소엔 숨기고, 내가 그 상대를 조준하는 동안만 보인다), 가운데 아래에 떠 있는 룬 핵, 변 바깥마다 성채 섬, 자리마다 1번 발판 복제. 탈락으로 링이 바뀌면 통째로 다시 짓는다
//   (클라이언트는 스냅샷으로 링을 맞춘다 — 그래서 다른 표시보다 먼저 돈다). 2명이 남으면 1:1 처럼 넓은 다리 하나에 레인 3개.
// 레인 · 발판 위치는 ArenaGeometry 를 그대로 쓰므로 판정과 어긋나지 않는다 (표시 전용).

using System.Collections.Generic;
using SpellboundVR.Arena;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    [DefaultExecutionOrder(-100)] // 다인전 링 맞추기 — 구조물 · 아바타 · HUD 가 같은 프레임에 새 자리를 읽도록
    public sealed class FfaArenaView : MonoBehaviour
    {
        /// <summary>끌 씬 오브젝트 — 1:1 직사각형 전장 전용 (먼 절벽 Art_FarCliffs 는 남긴다)</summary>
        private static readonly string[] DuelOnly =
        {
            "Art_Bridge/Art_Walls", "Art_Bridge/Art_Ground", "Art_Bridge/Art_Decor", "Art_Walls", "Art_Ground", "Art_Decor", "Lanes", "Ground",
        };
        /// <summary>레인 가장자리 벽 — 1:1 전장 둘레 벽과 같은 프리팹 · 배율 (SpellboundArenaArtBuilder.WallScale)</summary>
        private const string WallPrefab = "bricks_01";
        private const float WallScale = 0.4f;
        /// <summary>심연에 떠다니는 바위</summary>
        private static readonly string[] FloatRocks = { "Rock16", "Rock18", "Rock22", "Rock1", "Rock2" };

        private static readonly Color Edge = new Color(0.55f, 0.45f, 0.95f);
        private static readonly Color StormColor = new Color(0.65f, 0.2f, 0.95f);
        private static readonly Color GlyphColor = new Color(0.45f, 0.75f, 1f);
        /// <summary>바위 덩어리 깊이 · 바닥까지 안으로 좁아지는 정도 (m) — 1:1 절벽처럼 안개 속으로</summary>
        private const float CliffDepth = 30f;
        private const float CliffInset = 6f;
        /// <summary>돌 · 바위 텍스처 한 장이 덮는 길이 (m) — 1:1 바닥과 같다</summary>
        private const float UvMeters = 4f;
        /// <summary>레인을 따라 룬 문양을 놓는 간격 (m)</summary>
        private const float GlyphStep = 7f;
        /// <summary>가장자리 벽 위 등불 기둥 · 폐허 조각 간격 (m)</summary>
        private const float LampStep = 9f;

        /// <summary>그릴 레인 하나 — 반폭 · 바위 다리 · 가장자리 벽 여부 (2명 남은 다인전은 가운데 레인 하나가 3열 폭 다리)</summary>
        private struct DrawnLane
        {
            public int Lane;
            public float Half;
            public bool Slab, Walls;
            /// <summary>바닥 문양을 놓는 레인 (시연 전장은 바깥 변만)</summary>
            public bool Glyphs;
        }

        /// <summary>장식 섬을 놓을 바깥쪽 한 곳 — 두 발판 사이 바깥 (inner = 4인 개인전 안쪽 삼각형 폐허)</summary>
        private struct Side
        {
            public Vector2 A, B, Out;
            public bool Inner;
        }

        /// <summary>바위 덩어리 하나 (가장자리 바위가 다른 덩어리 안쪽에 놓이지 않게)</summary>
        private struct Slab
        {
            public Vector2 Center, Dir;
            public float HalfWidth, HalfLength;

            public bool Contains(Vector2 p, float margin)
            {
                Vector2 d = p - Center;
                float along = Vector2.Dot(d, Dir), lateral = Vector2.Dot(d, new Vector2(Dir.y, -Dir.x));
                return Mathf.Abs(along) < HalfLength - margin && Mathf.Abs(lateral) < HalfWidth - margin;
            }
        }

        private ClientSession _session;
        private VFXCatalog _catalog;
        private ArenaGeometry _arena;
        private Transform _root;
        /// <summary>링이 바뀌면 지우고 다시 짓는 부분 (다인전)</summary>
        private Transform _content;
        private int _builtMask = -1;
        private readonly List<DrawnLane> _lanes = new List<DrawnLane>(6);
        private readonly List<int> _chords = new List<int>(9);
        /// <summary>대각선 레인 → 빛줄기 (평소엔 꺼 두고 조준할 때만 켠다)</summary>
        private readonly Dictionary<int, GameObject> _chordBeams = new Dictionary<int, GameObject>(9);
        private SpellboundVR.Input.AimPointer _aim;
        private float _nextAimSearch;
        private int _shownChord = -1;
        private readonly List<Team> _seats = new List<Team>(6);
        /// <summary>다인전 자리마다 발판 세 칸 (한 번 만들어 두고 옮긴다)</summary>
        private readonly GameObject[,] _polyPlatforms = new GameObject[TeamUtil.TeamCount, TeamUtil.ColumnCount];
        private Material _rock, _stone;
        private readonly List<Slab> _slabs = new List<Slab>(12);
        /// <summary>레인 번호 → 그 레인 바위 덩어리의 _slabs 번호</summary>
        private readonly List<int> _laneSlabs = new List<int>(6);
        private readonly List<Renderer> _edges = new List<Renderer>(16);
        private readonly List<Material> _glyphMaterials = new List<Material>(20);
        private readonly List<Mesh> _meshes = new List<Mesh>(12);
        private bool _stormShown;
        private readonly List<Transform> _floaters = new List<Transform>(20);
        private readonly List<Vector3> _floatBase = new List<Vector3>(20);

        public void Initialize(ClientSession session, VFXCatalog catalog)
        {
            _session = session;
            _catalog = catalog;
            _arena = session.Arena;
            _root = new GameObject("[FFA Arena]").transform;
            _root.SetParent(transform, false);

            TakeDuelMaterials();
            BuildGlyphMaterials();
            if (_arena.IsPolygon) CreatePolygonPlatforms(); // 씬의 1번 발판을 숨기기 전에 복제
            HideDuelArt();
            Build();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _glyphMaterials.Count; i++) if (_glyphMaterials[i] != null) Destroy(_glyphMaterials[i]);
            for (int i = 0; i < _meshes.Count; i++) if (_meshes[i] != null) Destroy(_meshes[i]);
        }

        /// <summary>지금 링(다인전) 또는 마름모(4인)로 전장을 짓는다</summary>
        private void Build()
        {
            _builtMask = _arena.RingMask;
            _content = new GameObject("Field").transform;
            _content.SetParent(_root, false);
            CollectLayout();
            BuildGround();
            for (int i = 0; i < _lanes.Count; i++) BuildLane(i);
            BuildChords();
            BuildLaneWalls();
            BuildGlyphs();
            BuildScenery();
            if (_arena.IsPolygon) PlacePolygonPlatforms();
            else ClonePlatforms();
            foreach (var team in _seats) BuildSeatSign(team);
        }

        private void Rebuild()
        {
            if (_content != null) Destroy(_content.gameObject);
            for (int i = 0; i < _meshes.Count; i++) if (_meshes[i] != null) Destroy(_meshes[i]);
            _meshes.Clear();
            _slabs.Clear();
            _laneSlabs.Clear();
            _edges.Clear();
            _floaters.Clear();
            _floatBase.Clear();
            _chordBeams.Clear();
            _shownChord = -1;
            Build();
            _stormShown = false; // 새 테두리에 폭풍 색을 다시 입힌다
        }

        /// <summary>그릴 자리 · 레인 · 대각선 — 4인: 네 자리 · 레인 6개, 다인전: 링 · 변 레인 + 대각선, 2명: 1:1 레인 3개</summary>
        private void CollectLayout()
        {
            _seats.Clear();
            _lanes.Clear();
            _chords.Clear();
            float half = _arena.LaneHalfWidth;
            if (!_arena.IsPolygon)
            {
                for (int t = 0; t < _arena.TeamCount; t++) _seats.Add((Team)t);
                for (int lane = 0; lane < _arena.LaneCount; lane++) _lanes.Add(new DrawnLane { Lane = lane, Half = half, Slab = true, Walls = true, Glyphs = true });
                return;
            }
            int n = _arena.RingCount;
            for (int i = 0; i < n; i++) _seats.Add(_arena.RingTeam(i));
            if (_arena.PolygonDuelStage)
            {
                for (int c = 0; c < TeamUtil.ColumnCount; c++)
                {
                    bool middle = c == 1;
                    _lanes.Add(new DrawnLane { Lane = ArenaGeometry.PolygonDuelLaneBase + c, Half = middle ? _arena.BandHalfWidth : half, Slab = middle, Walls = middle, Glyphs = true });
                }
                return;
            }
            if (_arena.FilledPolygon)
            {
                // 바닥은 BuildFilledFloor 가 통째로 — 레인은 테두리만 (바깥 변 + 대각선 모두)
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        _lanes.Add(new DrawnLane { Lane = _arena.LaneBetween(_arena.RingTeam(i), _arena.RingTeam(j)), Half = half });
                    }
                return;
            }
            for (int i = 0; i < n; i++)
            {
                _lanes.Add(new DrawnLane { Lane = _arena.LaneBetween(_arena.RingTeam(i), _arena.RingTeam(i + 1)), Half = half, Slab = true, Walls = true, Glyphs = true });
                for (int j = i + 2; j < n; j++)
                    if (!(i == 0 && j == n - 1)) _chords.Add(_arena.LaneBetween(_arena.RingTeam(i), _arena.RingTeam(j)));
            }
        }

        /// <summary>1:1 전장 덩어리(Art_Ground)의 재질 — [0] 바위 절벽, [1] 돌 바닥 (없으면 단색)</summary>
        private void TakeDuelMaterials()
        {
            var ground = GameObject.Find("/Arena/Art_Bridge/Art_Ground");
            var r = ground != null ? ground.GetComponent<MeshRenderer>() : null;
            if (r != null && r.sharedMaterials.Length >= 2)
            {
                _rock = r.sharedMaterials[0];
                _stone = r.sharedMaterials[1];
            }
            if (_rock == null) _rock = FallbackVisuals.Opaque(new Color(0.24f, 0.22f, 0.24f));
            if (_stone == null) _stone = FallbackVisuals.Opaque(new Color(0.42f, 0.40f, 0.42f));
        }

        private void HideDuelArt()
        {
            var arena = GameObject.Find("/Arena");
            if (arena == null) return;
            foreach (var name in DuelOnly)
            {
                var t = arena.transform.Find(name);
                if (t != null) t.gameObject.SetActive(false);
            }
        }

        /// <summary>발판 자리 · 대각선 레인이 만나는 가운데</summary>
        private void BuildGround()
        {
            float spacing = _arena.ColumnSpacing;
            if (_arena.FilledPolygon && !_arena.PolygonDuelStage)
            {
                BuildFilledFloor(spacing * 3f + 3f, _arena.NexusDepth + spacing + 4f);
                return;
            }
            foreach (var team in _seats)
            {
                Vector2 fwd = _arena.SeatForward(team);
                AddSlab("Seat" + (int)team, _arena.BasePosition(team) + fwd * (_arena.NexusDepth * 0.5f), fwd, spacing * 3f + 3f, _arena.NexusDepth + spacing + 4f);
            }
            if (_arena.IsPolygon)
            {
                if (!_arena.PolygonDuelStage) BuildCore();
                return;
            }
            float mid = spacing * 2.4f;
            AddSlab("Center", _arena.Center, new Vector2(0.7071f, 0.7071f), mid, mid); // 마름모 전장에 맞춰 45° 돌린 판
            var mark = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "CenterMark", FallbackVisuals.Transparent(new Color(Edge.r, Edge.g, Edge.b, 0.35f)));
            mark.transform.SetParent(_content, false);
            mark.transform.position = _arena.ToWorld(_arena.Center, 0.03f);
            mark.transform.localScale = new Vector3(spacing * 1.6f, 0.01f, spacing * 1.6f);
        }

        /// <summary>
        /// 시연 전장: 자리마다 발판 판(폭 seatWidth · 길이 seatLength, 일반 다인전 발판과 같은 크기)의 바깥 두 모서리를 꼭짓점으로 하는
        /// 깎은 다각형 하나로 정N각형 안을 채운다 (모든 꼭짓점이 한 원 위라 볼록). 윗면 = 지면(0), 옆면 = 아래로 좁아지는 절벽
        /// </summary>
        private void BuildFilledFloor(float seatWidth, float seatLength)
        {
            Vector2 c = _arena.Center;
            var corners = new List<Vector2>(_seats.Count * 2);
            float back = seatLength * 0.5f - _arena.NexusDepth * 0.5f; // 발판 판의 뒤 끝 (BasePosition 뒤쪽 거리)
            foreach (var team in _seats)
            {
                Vector2 b = _arena.BasePosition(team), f = _arena.SeatForward(team), r = _arena.SeatRight(team);
                corners.Add(b - f * back - r * seatWidth * 0.5f - c);
                corners.Add(b - f * back + r * seatWidth * 0.5f - c);
            }
            corners.Sort((x, y) => Mathf.Atan2(x.y, x.x).CompareTo(Mathf.Atan2(y.y, y.x)));
            float radius = 0f;
            foreach (var p in corners) radius = Mathf.Max(radius, p.magnitude);
            // 장식 · 떠다니는 바위가 바닥을 뚫지 않게 막는 판 (외접 정사각형)
            _slabs.Add(new Slab { Center = c, Dir = Vector2.up, HalfWidth = radius, HalfLength = radius });

            var go = new GameObject("FilledFloor", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_content, false);
            go.transform.SetPositionAndRotation(_arena.ToWorld(c), Quaternion.LookRotation(_arena.ToWorldDirection(Vector2.up), Vector3.up)); // 로컬 x = u, z = v
            var mesh = FilledMesh(corners, radius);
            _meshes.Add(mesh);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { _rock, _stone };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>볼록 다각형 윗면(서브메시 1, 가운데에서 부채꼴) + 아래로 좁아지는 옆면(서브메시 0) — CliffMesh 와 같은 재질 · UV 규칙</summary>
        private static Mesh FilledMesh(List<Vector2> corners, float radius)
        {
            int n = corners.Count;
            float shrink = Mathf.Max(0.1f, (radius - CliffInset) / Mathf.Max(0.01f, radius));
            var verts = new List<Vector3>(n * 5 + 1);
            var uvs = new List<Vector2>(n * 5 + 1);
            var sides = new List<int>(n * 6);
            var cap = new List<int>(n * 3);
            verts.Add(Vector3.zero);
            uvs.Add(Vector2.zero);
            for (int i = 0; i < n; i++)
            {
                verts.Add(new Vector3(corners[i].x, 0f, corners[i].y));
                uvs.Add(corners[i] / UvMeters);
            }
            for (int i = 0; i < n; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % n;
                if (Vector3.Cross(verts[a] - verts[0], verts[b] - verts[0]).y > 0f) cap.AddRange(new[] { 0, a, b }); // 위를 향하게
                else cap.AddRange(new[] { 0, b, a });
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 t0 = verts[1 + i], t1 = verts[1 + (i + 1) % n];
                Vector3 b0 = new Vector3(t0.x * shrink, -CliffDepth, t0.z * shrink), b1 = new Vector3(t1.x * shrink, -CliffDepth, t1.z * shrink);
                int k = verts.Count;
                float run = Vector3.Distance(t0, t1) / UvMeters;
                verts.Add(t0); verts.Add(t1); verts.Add(b1); verts.Add(b0);
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(run, 0f));
                uvs.Add(new Vector2(run, -CliffDepth / UvMeters)); uvs.Add(new Vector2(0f, -CliffDepth / UvMeters));
                Vector3 outward = (t0 + t1) * 0.5f;
                Vector3 normal = Vector3.Cross(t1 - t0, b0 - t0);
                if (Vector3.Dot(normal, outward) > 0f) sides.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
                else sides.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
            }
            var mesh = new Mesh { name = "FilledPolygon" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(sides, 0);
            mesh.SetTriangles(cap, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>다인전: 정N각형 가운데 아래에 떠 있는 룬 핵 — 대각선 빛줄기가 그 위를 지나간다 (지면 아래라 시야를 막지 않는다)</summary>
        private void BuildCore()
        {
            float r = _arena.ColumnSpacing * 1.4f;
            AddSlab("Core", _arena.Center, new Vector2(0.7071f, 0.7071f), r, r, -3.5f, false);
            var disk = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "CoreRune", FallbackVisuals.Transparent(new Color(Edge.r, Edge.g, Edge.b, 0.4f)));
            disk.transform.SetParent(_content, false);
            disk.transform.position = _arena.ToWorld(_arena.Center, -3.45f);
            disk.transform.localScale = new Vector3(r * 1.6f, 0.01f, r * 1.6f);
            _edges.Add(disk.GetComponent<Renderer>());
            if (_glyphMaterials.Count > 0 && _glyphMaterials[0].mainTexture != null)
            {
                var rune = FallbackVisuals.Primitive(PrimitiveType.Quad, "CoreGlyph", _glyphMaterials[0]);
                rune.transform.SetParent(_content, false);
                rune.transform.SetPositionAndRotation(_arena.ToWorld(_arena.Center, -3.4f), Quaternion.Euler(90f, 0f, 0f));
                rune.transform.localScale = Vector3.one * r * 1.4f;
            }
            if (_catalog != null) Prop("Rock22", _arena.Center, -3.5f - r, r, Vector2.up); // 핵 아래로 매달린 바위
        }

        /// <summary>레인 한 줄 — 돌 바닥 다리 + 그림처럼 이중선 테두리 (폭풍 때 색이 바뀐다)</summary>
        private void BuildLane(int index)
        {
            var d = _lanes[index];
            var frame = _arena.GetLane(Team.Home, d.Lane);
            var rotation = Quaternion.LookRotation(_arena.ToWorldDirection(frame.Dir), Vector3.up);
            float half = _arena.LaneHalfWidth;
            _laneSlabs.Add(d.Slab ? _slabs.Count : -1);
            if (d.Slab) AddSlab("Lane" + d.Lane, frame.Point(frame.Length * 0.5f), frame.Dir, d.Half * 2f + 3f, frame.Length);
            var left = Strip("Lane" + d.Lane + "_L", frame.Point(frame.Length * 0.5f, -half), rotation, frame.Length);
            var right = Strip("Lane" + d.Lane + "_R", frame.Point(frame.Length * 0.5f, half), rotation, frame.Length);
            if (!_arena.FilledPolygon || _arena.PolygonDuelStage) return;
            // 시연 전장: 레인 선은 평소엔 숨기고, 그 레인을 조준하는 동안만 보인다 (ShowAimedChord)
            var group = new GameObject("Lane" + d.Lane).transform;
            group.SetParent(_content, false);
            left.SetParent(group, true);
            right.SetParent(group, true);
            group.gameObject.SetActive(false);
            _chordBeams[d.Lane] = group.gameObject;
        }

        /// <summary>다인전 대각선 — 이웃이 아닌 두 사람을 잇는 직접 공격 길. 바닥 없이 심연 위를 지나는 옅은 빛줄기 (조준하는 동안만 보인다)</summary>
        private void BuildChords()
        {
            var material = FallbackVisuals.Transparent(new Color(GlyphColor.r, GlyphColor.g, GlyphColor.b, 0.22f));
            foreach (int lane in _chords)
            {
                var frame = _arena.GetLane(Team.Home, lane);
                float start = _arena.NexusDepth + 1.5f, length = frame.Length - start * 2f; // 발판 · 넥서스 위는 덮지 않는다
                if (length <= 1f) continue;
                var beam = FallbackVisuals.Primitive(PrimitiveType.Cube, "Chord" + lane, material);
                beam.transform.SetParent(_content, false);
                beam.transform.SetPositionAndRotation(_arena.ToWorld(frame.Point(frame.Length * 0.5f), 0.05f),
                                                      Quaternion.LookRotation(_arena.ToWorldDirection(frame.Dir), Vector3.up));
                beam.transform.localScale = new Vector3(0.35f, 0.02f, length);
                beam.SetActive(false);
                _chordBeams[lane] = beam;
            }
        }

        /// <summary>윗면 = 지면(0)의 돌 바닥, 옆면 = 아래로 좁아지는 바위 절벽</summary>
        private void AddSlab(string name, Vector2 center, Vector2 dir, float width, float length, float height = 0f, bool blocks = true)
        {
            if (blocks) _slabs.Add(new Slab { Center = center, Dir = dir, HalfWidth = width * 0.5f, HalfLength = length * 0.5f });
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_content, false);
            go.transform.SetPositionAndRotation(_arena.ToWorld(center, height), Quaternion.LookRotation(_arena.ToWorldDirection(dir), Vector3.up));
            var mesh = CliffMesh(width, length);
            _meshes.Add(mesh);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterials = new[] { _rock, _stone };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>서브메시 0 = 옆면(바위), 1 = 윗면(돌 바닥). 텍스처는 UvMeters 마다 반복 — 크기가 달라도 늘어나 보이지 않게</summary>
        private static Mesh CliffMesh(float width, float length)
        {
            float w = width * 0.5f, l = length * 0.5f;
            float bw = Mathf.Max(0.5f, w - CliffInset), bl = Mathf.Max(0.5f, l - CliffInset);
            var top = new[] { new Vector3(-w, 0f, -l), new Vector3(-w, 0f, l), new Vector3(w, 0f, l), new Vector3(w, 0f, -l) };
            var bottom = new[] { new Vector3(-bw, -CliffDepth, -bl), new Vector3(-bw, -CliffDepth, bl), new Vector3(bw, -CliffDepth, bl), new Vector3(bw, -CliffDepth, -bl) };
            var verts = new List<Vector3>(20);
            var uvs = new List<Vector2>(20);
            var sides = new List<int>(24);
            var cap = new List<int>(6);

            for (int i = 0; i < 4; i++)
            {
                verts.Add(top[i]);
                uvs.Add(new Vector2(top[i].x, top[i].z) / UvMeters);
            }
            cap.AddRange(new[] { 0, 1, 2, 0, 2, 3 }); // 위에서 보면 시계 방향 = 위를 향함

            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                int b = verts.Count;
                float run = Vector3.Distance(top[i], top[j]) / UvMeters;
                verts.Add(top[i]); verts.Add(top[j]); verts.Add(bottom[j]); verts.Add(bottom[i]);
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(run, 0f));
                uvs.Add(new Vector2(run, -CliffDepth / UvMeters)); uvs.Add(new Vector2(0f, -CliffDepth / UvMeters));
                // 바깥을 향하도록 감는 방향을 고른다
                Vector3 outward = (top[i] + top[j]) * 0.5f;
                outward.y = 0f;
                Vector3 n = Vector3.Cross(top[j] - top[i], bottom[i] - top[i]);
                if (Vector3.Dot(n, outward) > 0f) sides.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                else sides.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }

            var mesh = new Mesh { name = "FfaCliff" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(sides, 0);
            mesh.SetTriangles(cap, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Transform Strip(string name, Vector2 at, Quaternion rotation, float length)
        {
            var strip = FallbackVisuals.Primitive(PrimitiveType.Cube, name, FallbackVisuals.Transparent(new Color(Edge.r, Edge.g, Edge.b, 0.75f)));
            strip.transform.SetParent(_content, false);
            strip.transform.SetPositionAndRotation(_arena.ToWorld(at, 0.02f), rotation);
            strip.transform.localScale = new Vector3(0.14f, 0.02f, length);
            _edges.Add(strip.GetComponent<Renderer>());
            return strip.transform;
        }

        /// <summary>레인 가장자리 지점이 다른 덩어리(교차로 · 발판) 위인가 — 자기 레인 판은 빼고 본다 (가장자리는 늘 자기 판 위다)</summary>
        private bool OnOtherSlab(Vector2 p, int index)
        {
            for (int s = 0; s < _slabs.Count; s++)
                if (s != _laneSlabs[index] && _slabs[s].Contains(p, 0.3f)) return true;
            return false;
        }

        /// <summary>레인 테두리 바로 바깥을 따라 1:1 둘레의 낮은 벽을 잇고, 벽 위에 등불 기둥 · 폐허 조각을 얹는다.
        /// 교차로 · 발판 위에서는 끊는다. 레인 폭 밖이고 충돌체가 없어 유닛 길을 막지 않는다</summary>
        private void BuildLaneWalls()
        {
            if (_catalog == null) return;
            // 한 토막 크기 (길이 = 로컬 x) — 1:1 과 같은 배율로 재 본다
            var probe = Place(WallPrefab, Vector2.zero, 0f, Quaternion.identity, 0f, WallScale);
            if (probe == null) return;
            var size = RenderBounds(probe.gameObject).size;
            Destroy(probe.gameObject);
            float segment = Mathf.Max(0.3f, size.x * 0.95f); // 토막이 살짝 겹치게 (1:1 과 같다)
            int every = Mathf.Max(1, Mathf.RoundToInt(LampStep / segment));
            var orbMaterial = FallbackVisuals.Transparent(new Color(Edge.r, Edge.g, Edge.b, 0.75f));
            int n = 0;
            for (int lane = 0; lane < _lanes.Count; lane++)
            {
                if (!_lanes[lane].Walls) continue;
                float edge = _lanes[lane].Half + 0.25f + size.z * 0.5f;
                var frame = _arena.GetLane(Team.Home, _lanes[lane].Lane);
                var along90 = Quaternion.LookRotation(_arena.ToWorldDirection(frame.Dir), Vector3.up) * Quaternion.Euler(0f, -90f, 0f); // 로컬 x = 레인 방향
                Vector2 halfSeg = frame.Dir * (segment * 0.5f);
                int i = 0;
                for (float along = segment * 0.5f; along < frame.Length - segment * 0.5f; along += segment, i++)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector2 p = frame.Point(along, side * edge);
                        if (OnOtherSlab(p - halfSeg, lane) || OnOtherSlab(p, lane) || OnOtherSlab(p + halfSeg, lane)) continue;
                        var wall = Place(WallPrefab, p, 0f, along90 * Quaternion.Euler(0f, i % 2 == 1 ? 180f : 0f, 0f), 0f, WallScale);
                        if (wall == null || i % every != every / 2) continue;
                        float top = RenderBounds(wall.gameObject).max.y - _arena.GroundY;
                        if (n++ % 3 == 2) Prop(n % 2 == 0 ? "Rubble" : "Rock16", p, top, 1.1f, frame.Dir * side);
                        else Lamp(p, top, orbMaterial);
                    }
                }
            }
        }

        /// <summary>등불 기둥 — 꼭대기 빛 구슬은 테두리와 같이 폭풍 때 보라색</summary>
        private void Lamp(Vector2 p, float groundY, Material orbMaterial)
        {
            var pillar = Prop("Column", p, groundY, 2.2f, Vector2.up);
            if (pillar == null) return;
            var orb = FallbackVisuals.Primitive(PrimitiveType.Sphere, "LampOrb", orbMaterial);
            orb.transform.SetParent(_content, false);
            orb.transform.position = _arena.ToWorld(p, RenderBounds(pillar.gameObject).max.y - _arena.GroundY + 0.25f);
            orb.transform.localScale = Vector3.one * 0.4f;
            _edges.Add(orb.GetComponent<Renderer>());
        }

        private static Bounds RenderBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++) b.Encapsulate(renderers[r].bounds);
            return b;
        }

        private static float MaxExtent(GameObject go)
        {
            var s = RenderBounds(go).size;
            float m = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            return m > 0.01f ? m : 1f;
        }

        /// <summary>그 지점에서 가장 가까운 레인 중심선까지 거리 (장식이 레인을 덮지 않게)</summary>
        private float LaneClearance(Vector2 p)
        {
            // 넓은 다리(2명 남은 다인전)는 그만큼 더 떨어져야 한다 — 레인 폭이 LaneHalfWidth 인 것처럼 맞춘 거리
            float best = float.MaxValue;
            foreach (var d in _lanes) best = Mathf.Min(best, Distance(p, d.Lane) - d.Half + _arena.LaneHalfWidth);
            foreach (int lane in _chords) best = Mathf.Min(best, Distance(p, lane));
            return best;
        }

        private float Distance(Vector2 p, int lane)
        {
            var f = _arena.GetLane(Team.Home, lane);
            return Vector2.Distance(p, f.Point(Mathf.Clamp(f.Along(p), 0f, f.Length)));
        }

        /// <summary>장식 섬 자리 — 4인: 마름모 네 변 (안쪽 삼각형 폐허 포함), 다인전: 변마다 바깥, 2명: 다리 양옆</summary>
        private List<Side> Sides()
        {
            var sides = new List<Side>(6);
            Vector2 c = _arena.Center;
            if (!_arena.IsPolygon)
            {
                foreach (var a in new[] { Team.Home, Team.Away })
                foreach (var b in new[] { Team.Seat2, Team.Seat4 })
                {
                    Vector2 pa = _arena.BasePosition(a), pb = _arena.BasePosition(b);
                    sides.Add(new Side { A = pa, B = pb, Out = ((pa + pb) * 0.5f - c).normalized, Inner = true });
                }
                return sides;
            }
            if (_arena.PolygonDuelStage)
            {
                Vector2 right = _arena.SeatRight(_arena.RingTeam(0));
                sides.Add(new Side { A = c, B = c, Out = right });
                sides.Add(new Side { A = c, B = c, Out = -right });
                return sides;
            }
            for (int i = 0; i < _arena.RingCount; i++)
            {
                Vector2 pa = _arena.BasePosition(_arena.RingTeam(i)), pb = _arena.BasePosition(_arena.RingTeam(i + 1));
                sides.Add(new Side { A = pa, B = pb, Out = ((pa + pb) * 0.5f - c).normalized });
            }
            return sides;
        }

        /// <summary>장식 (표시 전용): 레인 사이 빈 삼각형 = 시야를 가리지 않는 낮은 폐허, 마름모 바깥 = 성채, 심연 = 떠다니는 바위</summary>
        private void BuildScenery()
        {
            if (_catalog == null) return;
            Vector2 c = _arena.Center;
            float clearNeed = _arena.LaneHalfWidth + 2.2f; // 레인 반폭 + 가장자리 바위
            foreach (var s in Sides())
            {
                Vector2 pa = s.A, pb = s.B;
                Vector2 outDir = s.Out;
                Vector2 side = new Vector2(outDir.y, -outDir.x);

                // 안쪽 폐허 섬 — 두 발판과 가운데가 만드는 삼각형의 내심 (세 레인에서 가장 먼 곳)
                float la = (pb - c).magnitude, lb = (pa - c).magnitude, lc = (pa - pb).magnitude;
                Vector2 inner = (la * pa + lb * pb + lc * c) / Mathf.Max(0.01f, la + lb + lc);
                float half = (LaneClearance(inner) - clearNeed) / 1.42f; // 정사각 판 모서리까지 레인에서 떨어지게
                if (s.Inner && half > 1.2f)
                {
                    const float h = -0.6f;
                    AddSlab("Ruin", inner, outDir, half * 2f, half * 2f, h);
                    Prop("Column", inner + (side - outDir) * half * 0.45f, h, 3f, outDir);
                    Prop("Column", inner + (outDir - side) * half * 0.3f, h, 1.8f, side); // 부러진 기둥
                    Prop("Rubble", inner + (side + outDir) * half * 0.35f, h, 2.2f, -outDir);
                    Prop("Small Rock4", inner - (side + outDir) * half * 0.4f, h, 1f, side);
                }

                // 바깥 성채 섬 — 레인 너머로 섬 반폭만큼 더 밀어낸다
                const float keepW = 18f, keepL = 12f, kh = -1.2f;
                Vector2 keep = (pa + pb) * 0.5f;
                for (int i = 0; i < 40 && LaneClearance(keep) < clearNeed + keepW * 0.5f + 1f; i++) keep += outDir;
                AddSlab("Keep", keep, outDir, keepW, keepL, kh);
                Vector2 face = -outDir; // 전장을 바라보게
                Prop("Castle", keep + outDir * 2f, kh, 11f, face);
                Prop("Tower A", keep + side * 7f + outDir * 2.5f, kh, 8f, face);
                Prop("Tower B", keep - side * 7f + outDir * 2.5f, kh, 8f, face);
                Prop("Wall", keep + side * 4.6f + outDir * 3.5f, kh, 4.5f, face);
                Prop("Wall", keep - side * 4.6f + outDir * 3.5f, kh, 4.5f, face);
                Prop("House A", keep + side * 4f - outDir * 3.5f, kh, 3.8f, face);
                Prop("House B", keep - side * 4f - outDir * 3.5f, kh, 3.8f, face);
                Prop("Rock18", keep + side * 8.2f - outDir * 4.5f, kh, 2.5f, side);
                Prop("Rock22", keep - side * 8.5f - outDir * 4f, kh, 2f, side);
            }
            BuildFloaters(clearNeed);
        }

        /// <summary>마름모 안팎의 심연에 떠서 천천히 오르내리는 바위 (지면 아래 — 레인 · 섬 아래는 피한다)</summary>
        private void BuildFloaters(float clearNeed)
        {
            var rng = new System.Random(77);
            float reach = _arena.FieldLength * 0.75f;
            for (int tries = 0; tries < 200 && _floaters.Count < 18; tries++)
            {
                Vector2 p = _arena.Center + new Vector2((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f) * reach;
                if (LaneClearance(p) < clearNeed + 4f) continue;
                bool blocked = false;
                for (int s = 0; s < _slabs.Count && !blocked; s++) blocked = _slabs[s].Contains(p, -5f);
                if (blocked) continue;
                float yaw = (float)rng.NextDouble() * 360f;
                var t = Prop(FloatRocks[rng.Next(FloatRocks.Length)], p, -3f - (float)rng.NextDouble() * 7f, 1.5f + (float)rng.NextDouble() * 3.5f,
                             new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)));
                if (t == null) continue;
                _floaters.Add(t);
                _floatBase.Add(t.position);
            }
        }

        /// <summary>가장 긴 변 = size (m), facing = 앞 방향</summary>
        private Transform Prop(string name, Vector2 at, float groundY, float size, Vector2 facing) =>
            Place(name, at, groundY, Quaternion.LookRotation(_arena.ToWorldDirection(facing), Vector3.up), size, 0f);

        /// <summary>카탈로그 모델 하나를 충돌체 없이 놓는다 — 프리팹 크기의 scale 배 (0 이면 가장 긴 변 = size m), 모델 중심 = at, 바닥면 = groundY</summary>
        private Transform Place(string name, Vector2 at, float groundY, Quaternion rotation, float size, float scale)
        {
            if (!_catalog.TryGet(name, out var prefab)) return null;
            var go = Instantiate(prefab, _content);
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) Destroy(col);
            var t = go.transform;
            Vector3 target = _arena.ToWorld(at, groundY);
            t.SetPositionAndRotation(target, rotation);
            t.localScale *= scale > 0f ? scale : size / MaxExtent(go);
            var b = RenderBounds(go);
            if (b.size != Vector3.zero) t.position += new Vector3(target.x - b.center.x, target.y - b.min.y, target.z - b.center.z); // 피벗이 바닥 · 가운데가 아니어도
            return t;
        }

        /// <summary>레인 바닥의 마법 문양 — 룬 그림(RuneTemplate.Icon)을 바닥에 눕혀 은은하게 빛나게 (그림이 없으면 빛나는 원)</summary>
        private void BuildGlyphMaterials()
        {
            var shader = Shader.Find("Spellbound/RuneIcon");
            if (shader != null && _session.Runes != null)
                foreach (var rune in _session.Runes.Templates)
                    if (rune != null && rune.Icon != null) _glyphMaterials.Add(GlyphMaterial(shader, rune.Icon));
            if (_glyphMaterials.Count == 0) _glyphMaterials.Add(GlyphMaterial(null, null));
        }

        private void BuildGlyphs()
        {
            bool icons = _glyphMaterials[0].mainTexture != null;
            float size = _arena.LaneHalfWidth * 1.3f;
            int n = 0;
            foreach (var d in _lanes)
            {
                if (!d.Glyphs) continue;
                var frame = _arena.GetLane(Team.Home, d.Lane);
                float yaw = Mathf.Atan2(frame.Dir.x, frame.Dir.y) * Mathf.Rad2Deg;
                for (float along = GlyphStep; along < frame.Length - GlyphStep * 0.5f; along += GlyphStep, n++)
                {
                    var material = _glyphMaterials[n % _glyphMaterials.Count];
                    GameObject go;
                    if (!icons)
                    {
                        go = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "Glyph", material);
                        go.transform.localScale = new Vector3(size, 0.005f, size);
                    }
                    else
                    {
                        go = FallbackVisuals.Primitive(PrimitiveType.Quad, "Glyph", material);
                        go.transform.localScale = new Vector3(size, size, 1f);
                        go.transform.rotation = Quaternion.Euler(90f, yaw + n * 37f, 0f); // 바닥에 눕혀 하늘을 보게
                    }
                    go.transform.SetParent(_content, true);
                    go.transform.position = _arena.ToWorld(frame.Point(along), 0.03f);
                }
            }
        }

        private static Material GlyphMaterial(Shader shader, Texture2D icon)
        {
            Material m;
            if (shader != null && icon != null)
            {
                m = new Material(shader) { mainTexture = icon, name = "FfaGlyph_" + icon.name };
                m.SetColor("_Backdrop", new Color(0f, 0f, 0f, 0f)); // 받침 없이 선만
            }
            else
            {
                m = new Material(FallbackVisuals.Transparent(Color.white)) { name = "FfaGlyph_Ring" };
            }
            m.color = GlyphColor;
            return m;
        }

        /// <summary>1번 발판 세 칸을 2 · 4번 자리에 복제하고, 3번 발판은 개인전 거리로 옮긴다 (씬 아트가 없으면 판 하나씩 — 위치만 맞으면 된다)</summary>
        private void ClonePlatforms()
        {
            for (int t = 0; t < _arena.TeamCount; t++)
            {
                var team = (Team)t;
                if (team == Team.Home) continue;
                if (team == Team.Away)
                {
                    MoveAwayPlatforms();
                    continue;
                }
                for (int c = 0; c < TeamUtil.ColumnCount; c++)
                {
                    Vector3 pos = _arena.ToWorld(_arena.PlayerPosition(team, c));
                    var rot = _arena.FacingRotation(team);
                    var source = GameObject.Find("/Arena/Home/Home_Commander_" + "LCR"[c]);
                    GameObject go;
                    if (source != null)
                    {
                        go = Instantiate(source, _content);
                        foreach (var art in go.GetComponentsInChildren<TeamArt>(true)) art.team = team;
                        go.transform.SetPositionAndRotation(pos, rot);
                    }
                    else
                    {
                        go = FallbackVisuals.Primitive(PrimitiveType.Cube, "Commander_" + team + c, FallbackVisuals.Opaque(FallbackVisuals.TeamColor(team)));
                        go.transform.SetParent(_content, false);
                        go.transform.SetPositionAndRotation(pos, rot);
                        go.transform.localScale = new Vector3(_arena.ColumnSpacing * 0.9f, 0.3f, _arena.ColumnSpacing * 0.9f);
                    }
                    go.name = "Commander_" + team + "_" + c;
                }
            }
        }

        /// <summary>다인전: 1번 발판 세 칸을 자리마다 복제해 둔다 (씬의 1 · 3번 발판은 숨긴다 — 링마다 자리가 바뀌므로 전부 복제본을 옮긴다)</summary>
        private void CreatePolygonPlatforms()
        {
            for (int c = 0; c < TeamUtil.ColumnCount; c++)
            {
                var source = GameObject.Find("/Arena/Home/Home_Commander_" + "LCR"[c]);
                for (int t = 0; t < TeamUtil.TeamCount; t++)
                {
                    var team = (Team)t;
                    GameObject go;
                    if (_arena.FilledPolygon) go = CleanPad(team); // 시연: 채운 바닥 위에 씬 발판 모델 대신 낮은 돌 원판 + 팀 색 테두리
                    else if (source != null)
                    {
                        go = Instantiate(source, _root);
                        foreach (var art in go.GetComponentsInChildren<TeamArt>(true)) art.team = team;
                    }
                    else
                    {
                        go = FallbackVisuals.Primitive(PrimitiveType.Cube, "Commander", FallbackVisuals.Opaque(FallbackVisuals.TeamColor(team)));
                        go.transform.SetParent(_root, false);
                        go.transform.localScale = new Vector3(_arena.ColumnSpacing * 0.9f, 0.3f, _arena.ColumnSpacing * 0.9f);
                    }
                    go.name = "Commander_" + team + "_" + c;
                    _polyPlatforms[t, c] = go;
                }
                if (source != null) source.SetActive(false);
                var away = GameObject.Find("/Arena/Away/Away_Commander_" + "LCR"[c]);
                if (away != null) away.SetActive(false);
            }
        }

        /// <summary>시연 전장의 이동 칸 하나 — 바닥에서 살짝 솟은 돌 원판, 둘레에 팀 색 테두리 (피벗 = 바닥 가운데)</summary>
        private GameObject CleanPad(Team team)
        {
            float d = _arena.ColumnSpacing * 0.8f;
            var pad = new GameObject("Pad");
            pad.transform.SetParent(_root, false);
            var rim = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "Rim", FallbackVisuals.Transparent(UiColor(FallbackVisuals.TeamColor(team), 0.6f)));
            rim.transform.SetParent(pad.transform, false);
            rim.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            rim.transform.localScale = new Vector3(d * 1.08f, 0.01f, d * 1.08f);
            var disc = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "Disc", _stone);
            disc.transform.SetParent(pad.transform, false);
            disc.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            disc.transform.localScale = new Vector3(d, 0.04f, d);
            return pad;
        }

        private static Color UiColor(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>다인전: 링에 있는 자리의 발판만 그 자리 세 칸에 놓는다</summary>
        private void PlacePolygonPlatforms()
        {
            for (int t = 0; t < TeamUtil.TeamCount; t++)
            {
                var team = (Team)t;
                bool used = _arena.HasTeam(team);
                for (int c = 0; c < TeamUtil.ColumnCount; c++)
                {
                    var go = _polyPlatforms[t, c];
                    if (go == null) continue;
                    if (go.activeSelf != used) go.SetActive(used);
                    if (used) go.transform.SetPositionAndRotation(_arena.ToWorld(_arena.PlayerPosition(team, c)), _arena.FacingRotation(team));
                }
            }
        }

        /// <summary>씬의 3번 발판은 1:1 거리에 있다 — 개인전 전장 길이에 맞춰 옮긴다 (좌우는 그대로)</summary>
        private void MoveAwayPlatforms()
        {
            foreach (var slot in new[] { "L", "C", "R" })
            {
                var t = GameObject.Find("/Arena/Away/Away_Commander_" + slot);
                if (t == null) continue;
                Vector2 uv = _arena.ToArena(t.transform.position);
                uv.y = _arena.FieldLength;
                t.transform.position = _arena.ToWorld(uv, t.transform.position.y - _arena.GroundY);
            }
        }

        /// <summary>발판 뒤 공중에 떠 있는 번호판 — 어느 쪽이 몇 번인지 전장 어디서나 보이게</summary>
        private void BuildSeatSign(Team team)
        {
            var sign = new GameObject("Seat" + TeamUtil.SeatNumber(team));
            sign.transform.SetParent(_content, false);
            sign.transform.position = _arena.ToWorld(_arena.BasePosition(team) - _arena.SeatForward(team) * 3f, 8f);
            sign.transform.rotation = Quaternion.LookRotation(_arena.ToWorldDirection(-_arena.SeatForward(team)), Vector3.up);
            var text = sign.AddComponent<TextMesh>();
            text.text = TeamUtil.SeatNumber(team).ToString();
            text.font = FallbackVisuals.DefaultFont;
            text.fontSize = 96;
            text.characterSize = 0.35f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = FallbackVisuals.TeamColor(team);
            sign.GetComponent<MeshRenderer>().sharedMaterial = FallbackVisuals.DefaultFont.material;
        }

        /// <summary>내가 대각선 너머 상대를 조준하는 동안만 그 빛줄기를 켠다 (이웃 레인 · 다른 대각선은 그대로)</summary>
        private void ShowAimedChord()
        {
            if (_chordBeams.Count == 0) return;
            if (_aim == null && Time.time >= _nextAimSearch)
            {
                _nextAimSearch = Time.time + 1f; // 서버 창에는 없다 — 매 프레임 찾지 않는다
                _aim = FindAnyObjectByType<SpellboundVR.Input.AimPointer>();
            }
            int lane = _aim != null && _aim.IsActive && _aim.Current.Valid ? _aim.Current.WorldColumn : -1;
            if (lane == _shownChord) return;
            if (_chordBeams.TryGetValue(_shownChord, out var old) && old != null) old.SetActive(false);
            _shownChord = lane;
            if (_chordBeams.TryGetValue(lane, out var beam) && beam != null) beam.SetActive(true);
        }

        private void Update()
        {
            // 다인전: 스냅샷의 링으로 전장을 맞추고 (서버 · 로컬 경기는 이미 같은 값), 바뀌었으면 다시 짓는다
            var latest = _session != null ? _session.Events.Latest : null;
            if (_arena.IsPolygon && latest != null)
            {
                int mask = latest.RingMask();
                if (mask != 0) _arena.SetRing(mask);
                if (_arena.RingMask != _builtMask) Rebuild();
            }
            ShowAimedChord();

            // 마법 문양이 숨 쉬듯 밝아졌다 어두워진다 (문양마다 조금씩 엇갈려)
            float glow = 0.4f + 0.2f * Mathf.Sin(Time.time * 1.3f);
            for (int i = 0; i < _glyphMaterials.Count; i++)
            {
                var c = GlyphColor;
                c.a = Mathf.Clamp01(glow + 0.12f * Mathf.Sin(Time.time * 0.7f + i * 1.9f));
                _glyphMaterials[i].color = c;
            }

            for (int i = 0; i < _floaters.Count; i++)
            {
                _floaters[i].position = _floatBase[i] + Vector3.up * (0.6f * Mathf.Sin(Time.time * 0.35f + i * 1.7f));
                _floaters[i].Rotate(0f, (i % 2 == 0 ? 4f : -4f) * Time.deltaTime, 0f, Space.World);
            }

            var s = _session != null ? _session.Events.Latest : null;
            if (s == null) return;
            bool storm = s.Header.Phase == (byte)Network.MatchPhase.Playing && s.Header.StormIn == 0f;
            if (storm == _stormShown) return;
            _stormShown = storm;
            Color e = storm ? StormColor : Edge;
            var material = FallbackVisuals.Transparent(new Color(e.r, e.g, e.b, storm ? 0.95f : 0.75f));
            for (int i = 0; i < _edges.Count; i++) _edges[i].sharedMaterial = material;
        }
    }
}
