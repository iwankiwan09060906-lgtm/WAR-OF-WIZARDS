// 4인 개인전 전장 표시 — 씬 아트는 1:1 직사각형 전장이라, 개인전이면 실행 중에 마름모 전장으로 바꾼다.
//   · 씬의 벽 · 바닥 · 장식 · 1:1 레인을 끄고 (먼 절벽 · 하늘 · 안개 · 1번 · 3번 발판과 구조물은 그대로 쓴다)
//   · 1:1 전장처럼: 레인 6개 · 발판 자리 · 가운데가 돌 바닥(1:1 바닥 재질)을 인 바위 덩어리(1:1 절벽 재질)이고,
//     아래로 좁아지며 안개 낀 심연으로 사라진다. 레인 가장자리를 따라 바위가 놓이고, 바닥에는 마법 문양이 은은하게 빛난다
//   · 2번 · 4번 발판을 1번 발판을 복제해 세우고, 3번 발판은 개인전 거리로 옮긴다 (TeamArt 가 보는 사람 기준 색을 입힌다)
//   · 발판 번호판 · 마나 폭풍이 시작되면 레인 테두리가 보라색으로 빛난다
// 레인 · 발판 위치는 ArenaGeometry 를 그대로 쓰므로 판정과 어긋나지 않는다 (표시 전용).

using System.Collections.Generic;
using SpellboundVR.Arena;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    public sealed class FfaArenaView : MonoBehaviour
    {
        /// <summary>끌 씬 오브젝트 — 1:1 직사각형 전장 전용 (먼 절벽 Art_FarCliffs 는 남긴다)</summary>
        private static readonly string[] DuelOnly =
        {
            "Art_Bridge/Art_Walls", "Art_Bridge/Art_Ground", "Art_Bridge/Art_Decor", "Art_Walls", "Art_Ground", "Art_Decor", "Lanes", "Ground",
        };
        /// <summary>레인 가장자리 바위 (VFXCatalog 의 AEGIS 바위)</summary>
        private static readonly string[] EdgeRocks = { "Rock1", "Rock2", "Small Rock1", "Small Rock4", "Small Rock5", "Small Rock7" };

        private static readonly Color Edge = new Color(0.55f, 0.45f, 0.95f);
        private static readonly Color StormColor = new Color(0.65f, 0.2f, 0.95f);
        private static readonly Color GlyphColor = new Color(0.45f, 0.75f, 1f);
        /// <summary>바위 덩어리 깊이 · 바닥까지 안으로 좁아지는 정도 (m) — 1:1 절벽처럼 안개 속으로</summary>
        private const float CliffDepth = 30f;
        private const float CliffInset = 6f;
        /// <summary>돌 · 바위 텍스처 한 장이 덮는 길이 (m) — 1:1 바닥과 같다</summary>
        private const float UvMeters = 4f;
        /// <summary>레인을 따라 룬 문양 · 가장자리 바위를 놓는 간격 (m)</summary>
        private const float GlyphStep = 7f;
        private const float RockStep = 4.5f;

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
        private Material _rock, _stone;
        private readonly List<Slab> _slabs = new List<Slab>(12);
        private readonly List<Renderer> _edges = new List<Renderer>(16);
        private readonly List<Material> _glyphMaterials = new List<Material>(20);
        private readonly List<Mesh> _meshes = new List<Mesh>(12);
        private bool _stormShown;

        public void Initialize(ClientSession session, VFXCatalog catalog)
        {
            _session = session;
            _catalog = catalog;
            _arena = session.Arena;
            _root = new GameObject("[FFA Arena]").transform;
            _root.SetParent(transform, false);

            TakeDuelMaterials();
            HideDuelArt();
            BuildGround();
            for (int lane = 0; lane < _arena.LaneCount; lane++) BuildLane(lane);
            BuildEdgeRocks();
            BuildGlyphs();
            ClonePlatforms();
            for (int t = 0; t < _arena.TeamCount; t++) BuildSeatSign((Team)t);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _glyphMaterials.Count; i++) if (_glyphMaterials[i] != null) Destroy(_glyphMaterials[i]);
            for (int i = 0; i < _meshes.Count; i++) if (_meshes[i] != null) Destroy(_meshes[i]);
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
            for (int t = 0; t < _arena.TeamCount; t++)
            {
                var team = (Team)t;
                Vector2 fwd = _arena.SeatForward(team);
                AddSlab("Seat" + t, _arena.BasePosition(team) + fwd * (_arena.NexusDepth * 0.5f), fwd, spacing * 3f + 3f, _arena.NexusDepth + spacing + 4f);
            }
            float mid = spacing * 2.4f;
            AddSlab("Center", _arena.Center, new Vector2(0.7071f, 0.7071f), mid, mid); // 마름모 전장에 맞춰 45° 돌린 판
            var mark = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "CenterMark", FallbackVisuals.Transparent(new Color(Edge.r, Edge.g, Edge.b, 0.35f)));
            mark.transform.SetParent(_root, false);
            mark.transform.position = _arena.ToWorld(_arena.Center, 0.03f);
            mark.transform.localScale = new Vector3(spacing * 1.6f, 0.01f, spacing * 1.6f);
        }

        /// <summary>레인 한 줄 — 돌 바닥 다리 + 그림처럼 이중선 테두리 (폭풍 때 색이 바뀐다)</summary>
        private void BuildLane(int lane)
        {
            var frame = _arena.GetLane(Team.Home, lane);
            var rotation = Quaternion.LookRotation(_arena.ToWorldDirection(frame.Dir), Vector3.up);
            float half = _arena.LaneHalfWidth;
            AddSlab("Lane" + lane, frame.Point(frame.Length * 0.5f), frame.Dir, half * 2f + 3f, frame.Length);
            Strip("Lane" + lane + "_L", frame.Point(frame.Length * 0.5f, -half), rotation, frame.Length);
            Strip("Lane" + lane + "_R", frame.Point(frame.Length * 0.5f, half), rotation, frame.Length);
        }

        /// <summary>윗면 = 지면(0)의 돌 바닥, 옆면 = 아래로 좁아지는 바위 절벽</summary>
        private void AddSlab(string name, Vector2 center, Vector2 dir, float width, float length)
        {
            _slabs.Add(new Slab { Center = center, Dir = dir, HalfWidth = width * 0.5f, HalfLength = length * 0.5f });
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_root, false);
            go.transform.SetPositionAndRotation(_arena.ToWorld(center), Quaternion.LookRotation(_arena.ToWorldDirection(dir), Vector3.up));
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

        private void Strip(string name, Vector2 at, Quaternion rotation, float length)
        {
            var strip = FallbackVisuals.Primitive(PrimitiveType.Cube, name, FallbackVisuals.Transparent(new Color(Edge.r, Edge.g, Edge.b, 0.75f)));
            strip.transform.SetParent(_root, false);
            strip.transform.SetPositionAndRotation(_arena.ToWorld(at, 0.02f), rotation);
            strip.transform.localScale = new Vector3(0.14f, 0.02f, length);
            _edges.Add(strip.GetComponent<Renderer>());
        }

        /// <summary>레인 바깥 가장자리를 따라 1:1 장식처럼 바위 (다른 덩어리 안쪽 = 길이 이어지는 곳은 건너뛴다)</summary>
        private void BuildEdgeRocks()
        {
            if (_catalog == null) return;
            var rng = new System.Random(56);
            float edge = _arena.LaneHalfWidth + 1.1f;
            for (int lane = 0; lane < _arena.LaneCount; lane++)
            {
                var frame = _arena.GetLane(Team.Home, lane);
                for (float along = RockStep * 0.5f; along < frame.Length; along += RockStep)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector2 p = frame.Point(along, side * edge);
                        bool inside = false;
                        for (int s = 0; s < _slabs.Count && !inside; s++) inside = _slabs[s].Contains(p, 0.3f);
                        if (inside || !_catalog.TryGet(EdgeRocks[rng.Next(EdgeRocks.Length)], out var prefab)) continue;
                        var rock = Instantiate(prefab, _root);
                        foreach (var c in rock.GetComponentsInChildren<Collider>(true)) Destroy(c);
                        float size = 1f;
                        var renderers = rock.GetComponentsInChildren<Renderer>();
                        if (renderers.Length > 0)
                        {
                            var b = renderers[0].bounds;
                            for (int r = 1; r < renderers.Length; r++) b.Encapsulate(renderers[r].bounds);
                            size = Mathf.Max(0.01f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
                        }
                        rock.transform.localScale *= (0.9f + (float)rng.NextDouble() * 0.8f) / size;
                        rock.transform.SetPositionAndRotation(_arena.ToWorld(p, -0.2f), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                    }
                }
            }
        }

        /// <summary>레인 바닥의 마법 문양 — 룬 그림(RuneTemplate.Icon)을 바닥에 눕혀 은은하게 빛나게 (그림이 없으면 빛나는 원)</summary>
        private void BuildGlyphs()
        {
            var shader = Shader.Find("Spellbound/RuneIcon");
            var icons = new List<Texture2D>();
            if (shader != null && _session.Runes != null)
                foreach (var rune in _session.Runes.Templates)
                    if (rune != null && rune.Icon != null) icons.Add(rune.Icon);
            if (icons.Count == 0) _glyphMaterials.Add(GlyphMaterial(null, null));
            else foreach (var icon in icons) _glyphMaterials.Add(GlyphMaterial(shader, icon));

            float size = _arena.LaneHalfWidth * 1.3f;
            int n = 0;
            for (int lane = 0; lane < _arena.LaneCount; lane++)
            {
                var frame = _arena.GetLane(Team.Home, lane);
                float yaw = Mathf.Atan2(frame.Dir.x, frame.Dir.y) * Mathf.Rad2Deg;
                for (float along = GlyphStep; along < frame.Length - GlyphStep * 0.5f; along += GlyphStep, n++)
                {
                    var material = _glyphMaterials[n % _glyphMaterials.Count];
                    GameObject go;
                    if (icons.Count == 0)
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
                    go.transform.SetParent(_root, true);
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
                        go = Instantiate(source, _root);
                        foreach (var art in go.GetComponentsInChildren<TeamArt>(true)) art.team = team;
                        go.transform.SetPositionAndRotation(pos, rot);
                    }
                    else
                    {
                        go = FallbackVisuals.Primitive(PrimitiveType.Cube, "Commander_" + team + c, FallbackVisuals.Opaque(FallbackVisuals.TeamColor(team)));
                        go.transform.SetParent(_root, false);
                        go.transform.SetPositionAndRotation(pos, rot);
                        go.transform.localScale = new Vector3(_arena.ColumnSpacing * 0.9f, 0.3f, _arena.ColumnSpacing * 0.9f);
                    }
                    go.name = "Commander_" + team + "_" + c;
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
            sign.transform.SetParent(_root, false);
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

        private void Update()
        {
            // 마법 문양이 숨 쉬듯 밝아졌다 어두워진다 (문양마다 조금씩 엇갈려)
            float glow = 0.4f + 0.2f * Mathf.Sin(Time.time * 1.3f);
            for (int i = 0; i < _glyphMaterials.Count; i++)
            {
                var c = GlyphColor;
                c.a = Mathf.Clamp01(glow + 0.12f * Mathf.Sin(Time.time * 0.7f + i * 1.9f));
                _glyphMaterials[i].color = c;
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
