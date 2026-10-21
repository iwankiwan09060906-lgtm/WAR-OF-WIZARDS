// 스킬 VFX 레시피 — 이름 규칙 VFX_{SkillId}_{Name}_{Part} (§25.3), 실제 크기로 제작 (VFXPlayer가 크기를 바꾸지 않음)
//   Projectile: +Z = 진행 방향 · Field/Telegraph: 레인 방향 = +Z · Loop: 상태 표시(쉴드 등)
//   52SpecialEffectPack 원본은 반경 8~20m로 매우 커서 계층 스케일로 줄인다.

using UnityEditor;
using UnityEngine;

namespace SpellboundVR.EditorTools
{
    public static partial class SpellboundSkillAssetBuilder
    {
        private static void BuildSkillVfx()
        {
            BuildS01Smoke();
            BuildS02Poison();
            BuildS03Fireball();
            BuildS04ArrowRain();
            BuildS05Tornado();
            BuildS06Lightning();
            BuildS07Gatling();
            BuildS08Shield();
            BuildS09TowerBuff();
            BuildS10Focus();
            BuildSummonCasts();
            BuildH01Freeze();
            BuildH02Laser();
            BuildH03Drain();
            BuildH04Reflect();
            BuildH05Rage();
        }

        // ── S01 연막 (52SpecialEffectPack Smoke) ──────────────────

        private static Material SmokeMat => AssetDatabase_Load(Tex52 + "Smoke.mat");

        /// <summary>밝은 연기 (Eric FX_smoke_02 — 흰색, 색은 startColor로)</summary>
        private static Material LightSmokeMat => LoadMat(EricMat + "FX_smoke_02.mat");

        /// <summary>바닥에 눕는 원형 마법진 (52 SummonMagicCircle2, 반경 ≈ 11.5 × scale)</summary>
        private static GameObject GroundCircle(GameObject parent, string name, Color color, float radius, bool loop, float duration = 1.2f)
        {
            var c = Add(parent, P52 + "SummonMagicCircle2.prefab", radius / 11.5f, new Vector3(0f, 0.04f, 0f), name: name);
            if (c == null) return null;
            SetLoop(c, loop, duration);
            Recolor(c, color);
            return c;
        }

        /// <summary>빛나는 구 (52 Core4 가산)</summary>
        private static ParticleSystem GlowCore(GameObject parent, string name, Color color, float size, bool worldTrail)
        {
            var ps = NewPs(parent, name, LoadMat(Tex52 + "Core4.mat"));
            var m = ps.main;
            m.loop = true;
            m.startLifetime = worldTrail ? 0.25f : 0.15f;
            m.startSize = Range(size * 0.85f, size * 1.1f);
            m.startColor = color;
            m.simulationSpace = worldTrail ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            Rate(ps, worldTrail ? 60f : 30f);
            SizeCurve(ps, 1f, worldTrail ? 0.2f : 0.9f);
            return ps;
        }

        private static Material AssetDatabase_Load(string path) => LoadMat(path);

        /// <summary>검은 연기 덩어리 (알파 블렌드)</summary>
        private static ParticleSystem DarkSmoke(GameObject parent, string name, float size, float life, float rate, float radius, Color color)
        {
            var ps = NewPs(parent, name, SmokeMat);
            var m = ps.main;
            m.loop = true;
            m.duration = 1f;
            m.startLifetime = Range(life * 0.7f, life);
            m.startSize = Range(size * 0.7f, size * 1.2f);
            m.startRotation = Range(0f, Mathf.PI * 2f);
            m.startColor = color;
            m.startSpeed = Range(0.05f, 0.3f);
            Rate(ps, rate);
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius;
            var rol = ps.rotationOverLifetime;
            rol.enabled = true;
            rol.z = Range(-1.2f, 1.2f);
            FadeInOut(ps, 0.15f, 0.55f);
            SizeCurve(ps, 0.6f, 1.4f);
            return ps;
        }

        private static void BuildS01Smoke()
        {
            var dark = new Color(0.08f, 0.08f, 0.1f, 0.9f);

            // 날아가는 연막탄: 짙은 연기 공 + 월드 공간 연기 꼬리
            var root = NewRoot("VFX_S01_Smoke_Projectile");
            DarkSmoke(root, "Core", 0.9f, 0.35f, 40f, 0.15f, dark);
            var trail = DarkSmoke(root, "Trail", 0.8f, 0.9f, 45f, 0.1f, new Color(0.15f, 0.15f, 0.17f, 0.7f));
            var tm = trail.main;
            tm.simulationSpace = ParticleSystemSimulationSpace.World;
            Add(root, P52 + "DeadBall.prefab", 0.06f, name: "DarkCore");
            Save(root);

            // 명중: 짙은 연기 폭발 (상대 머리 높이)
            root = NewRoot("VFX_S01_Smoke_Impact");
            var burst = DarkSmoke(root, "Burst", 2.2f, 1.6f, 0f, 0.6f, dark);
            var bm = burst.main;
            bm.loop = false;
            bm.startSpeed = Range(1f, 3f);
            Burst(burst, 34);
            var wave = DarkSmoke(root, "Wave", 1.4f, 1.2f, 0f, 0.2f, new Color(0.12f, 0.12f, 0.14f, 0.8f));
            var wm = wave.main;
            wm.loop = false;
            wm.startSpeed = Range(3f, 5f);
            wm.startDelay = 0.05f;
            Burst(wave, 20);
            Save(root);

            // 시전: 손에서 피어오르는 작은 연기
            root = NewRoot("VFX_S01_Smoke_Cast");
            var cast = DarkSmoke(root, "Puff", 1f, 0.9f, 0f, 0.2f, new Color(0.2f, 0.2f, 0.22f, 0.8f));
            var cm = cast.main;
            cm.loop = false;
            cm.startSpeed = Range(0.5f, 1.4f);
            Burst(cast, 16);
            Save(root);

            // 연막에 맞은 상대 머리 주변 (상태 표시, 반복)
            root = NewRoot("VFX_S01_Smoke_Loop");
            DarkSmoke(root, "Cloud", 1.1f, 1.4f, 18f, 0.5f, new Color(0.05f, 0.05f, 0.06f, 0.85f));
            Save(root);
        }

        // ── S02 독 장판 (52 PoisonExplode, 3열을 가로지르는 얇고 넓은 띠) ─────

        private static void BuildS02Poison()
        {
            var green = new Color(0.35f, 1f, 0.2f);
            const float laneSpacing = 5f;   // ArenaLayout.columnSpacing 기본값
            const float halfDepth = 2.5f;   // Spell_S02 FieldHalfDepth

            var root = NewRoot("VFX_S02_PoisonField_Field");
            for (int c = -1; c <= 1; c++)
            {
                for (int r = -1; r <= 1; r += 2)
                {
                    var p = Add(root, P52 + "PoisonExplode.prefab", 0.2f, new Vector3(c * laneSpacing, 0.05f, r * halfDepth * 0.5f), name: $"Poison_{c}_{r}");
                    p.transform.localScale = new Vector3(p.transform.localScale.x, p.transform.localScale.y * 0.3f, p.transform.localScale.z); // 얇게
                    SetLoop(p, true, 0.6f);
                    Recolor(p, green);
                }
            }
            // 바닥에 깔리는 독 안개 (띠 전체)
            var fog = NewPs(root, "GroundFog", LoadMat(Tex52 + "Smoke2.mat"), new Vector3(0f, 0.1f, 0f));
            var fm = fog.main;
            fm.loop = true;
            fm.startLifetime = Range(1.2f, 2f);
            fm.startSize = Range(1.6f, 2.8f);
            fm.startRotation = Range(0f, 6.28f);
            fm.startColor = new Color(0.25f, 0.85f, 0.1f, 0.4f);
            fm.startSpeed = Range(0.05f, 0.25f);
            fm.maxParticles = 300;
            Rate(fog, 90f);
            var fs = fog.shape;
            fs.enabled = true;
            fs.shapeType = ParticleSystemShapeType.Box;
            fs.scale = new Vector3(laneSpacing * 3f, 0.1f, halfDepth * 2f);
            FadeInOut(fog, 0.2f, 0.6f);
            // 올라오는 독 거품
            var bubbles = NewPs(root, "Bubbles", LoadMat(Tex52 + "Core4.mat"), new Vector3(0f, 0.05f, 0f));
            var bbm = bubbles.main;
            bbm.loop = true;
            bbm.startLifetime = Range(0.6f, 1.2f);
            bbm.startSize = Range(0.12f, 0.3f);
            bbm.startColor = new Color(0.45f, 1f, 0.25f, 0.8f);
            bbm.startSpeed = Range(0.4f, 1.2f);
            Rate(bubbles, 60f);
            var bs = bubbles.shape;
            bs.enabled = true;
            bs.shapeType = ParticleSystemShapeType.Box;
            bs.scale = new Vector3(laneSpacing * 3f, 0.05f, halfDepth * 2f);
            FadeInOut(bubbles, 0.1f, 0.5f);
            Save(root);

            // 떨어지는 순간: 큰 독 폭발 (3열)
            root = NewRoot("VFX_S02_PoisonField_Impact");
            for (int c = -1; c <= 1; c++)
            {
                var p = Add(root, P52 + "PoisonExplode.prefab", 0.2f, new Vector3(c * laneSpacing, 0.1f, 0f), name: "Burst_" + c);
                Recolor(p, green);
            }
            Save(root);

            root = NewRoot("VFX_S02_PoisonField_Cast");
            var rune = Add(root, P52 + "RuneOfMagic.prefab", 0.12f, name: "Rune");
            Recolor(rune, green);
            Save(root);
        }

        // ── S03 파이어볼 (Hun0FX 불꽃으로 새로 만든 불덩이) ─────────────

        private static void BuildS03Fireball()
        {
            // 불덩이: Hun 화염 줄기(FX_Fire_06)의 불꽃 · 불씨를 구 모양으로 다시 모아 만든다 + 월드 공간 불꼬리
            var root = NewRoot("VFX_S03_Fireball_Projectile");
            var ball = Add(root, Hun + "FX_Fire_06.prefab", 0.7f, name: "FlameBall");
            ForPs(ball, ps =>
            {
                var m = ps.main;
                m.startSpeed = Range(0.2f, 0.8f);
                m.loop = true;
                m.startLifetime = new ParticleSystem.MinMaxCurve(Mathf.Min(m.startLifetime.constantMax, 0.45f));
                m.simulationSpace = ParticleSystemSimulationSpace.World;
                var sh = ps.shape;
                sh.enabled = true;
                sh.shapeType = ParticleSystemShapeType.Sphere;
                sh.radius = 0.5f;
            });
            GlowCore(root, "Core", new Color(1f, 0.55f, 0.15f, 1f), 1.1f, false);
            GlowCore(root, "Trail", new Color(1f, 0.35f, 0.05f, 0.8f), 0.9f, true);
            Save(root);

            // 폭발: 52 Explode + Hun 바닥 불(반경 약 2.6m = Spell_S03 Radius)
            root = NewRoot("VFX_S03_Fireball_Impact");
            Add(root, P52 + "Explode.prefab", 0.13f, new Vector3(0f, 0.3f, 0f), name: "Blast");
            var ground = Add(root, Hun + "FX_Fire_02.prefab", 0.9f, name: "GroundFire");
            SetLoop(ground, true, 1f);
            Add(root, Hun + "FX_Fire_01.prefab", 0.7f, name: "FirePillar");
            SetLoop(root.transform.Find("FirePillar").gameObject, false, 1.2f);
            Save(root);

            // 시전: 손끝 불꽃 소용돌이
            root = NewRoot("VFX_S03_Fireball_Cast");
            var c = Add(root, Hun + "FX_Fire_01.prefab", 0.25f, name: "Swirl");
            SetLoop(c, false, 0.6f);
            Save(root);

            // 착탄 예고: 바닥 붉은 마법진
            root = NewRoot("VFX_S03_Fireball_Telegraph");
            GroundCircle(root, "Circle", new Color(1f, 0.4f, 0.1f), 2.6f, true, 0.6f);
            Save(root);
        }

        // ── S04 화살 세례 (ZNS3D Arrow) ─────────────────────────

        /// <summary>ZNS3D 화살: 촉이 -X → +Z(진행 방향)로 돌린다</summary>
        private static GameObject ArrowModel(GameObject parent, float scale)
        {
            var arrow = Add(parent, Zns + "Arrow.prefab", scale, euler: new Vector3(0f, 90f, 0f), name: "Arrow");
            if (arrow == null) return null;
            // 피벗이 화살 중앙이 아니므로 바운드 중심을 원점으로
            var b = SpellboundMinionModelBuilder.RendererBounds(arrow);
            arrow.transform.position -= b.center - parent.transform.position;
            var tr = arrow.AddComponent<TrailRenderer>();
            tr.time = 0.25f;
            tr.startWidth = 0.16f;
            tr.endWidth = 0f;
            tr.sharedMaterial = LoadMat(Tex52 + "Core4.mat");
            tr.startColor = new Color(1f, 0.95f, 0.8f, 0.5f);
            tr.endColor = new Color(1f, 1f, 1f, 0f);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return arrow;
        }

        private static void BuildS04ArrowRain()
        {
            var root = NewRoot("VFX_S04_ArrowRain_Projectile");
            ArrowModel(root, 2.6f);
            Save(root);

            // 착탄: 흙먼지 + 작은 충격
            root = NewRoot("VFX_S04_ArrowRain_Impact");
            var dust = NewPs(root, "Dust", LightSmokeMat);
            var dm = dust.main;
            dm.startLifetime = Range(0.5f, 0.9f);
            dm.startSize = Range(0.4f, 0.8f);
            dm.startSpeed = Range(0.6f, 1.6f);
            dm.startColor = new Color(0.55f, 0.45f, 0.32f, 0.75f);
            dm.gravityModifier = 0.3f;
            Burst(dust, 8);
            var ds = dust.shape;
            ds.enabled = true;
            ds.shapeType = ParticleSystemShapeType.Hemisphere;
            ds.radius = 0.2f;
            FadeInOut(dust, 0.05f, 0.4f);
            Add(root, P52 + "Explode2.prefab", 0.08f, new Vector3(0f, 0.1f, 0f), name: "Spark");
            Save(root);

            // 낙하 범위 예고 (레인 폭 × 6m)
            root = NewRoot("VFX_S04_ArrowRain_Telegraph");
            var circle = GroundCircle(root, "Circle", new Color(0.95f, 0.8f, 0.45f), 2.4f, true, 0.6f);
            if (circle != null) circle.transform.localScale = Vector3.Scale(circle.transform.localScale, new Vector3(1f, 1f, 1.3f));
            Save(root);

            root = NewRoot("VFX_S04_ArrowRain_Cast");
            var rune = Add(root, P52 + "RuneOfMagic.prefab", 0.12f, name: "Rune");
            Recolor(rune, new Color(0.95f, 0.8f, 0.45f), 0.4f);
            Save(root);
        }

        // ── S05 토네이도 (Eric VFX Studio 연기 · 메시로 만든 회색 회오리) ─────────

        private static void BuildS05Tornado()
        {
            var grey = new Color(0.5f, 0.52f, 0.56f, 0.6f);
            var smoke = MatVariant(LightSmokeMat, "Mat_S05_TornadoSmoke", m =>
            {
                if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.6f, 0.62f, 0.66f, 1f));
                if (m.HasProperty("_Brightness")) m.SetFloat("_Brightness", 1f);
            });
            var root = NewRoot("VFX_S05_Tornado_Field");

            // 깔때기: 위로 올라가며 넓어지고(원뿔) 축을 중심으로 돈다(Orbital)
            var funnel = NewPs(root, "Funnel", smoke, euler: new Vector3(-90f, 0f, 0f));
            var fm = funnel.main;
            fm.loop = true;
            fm.startLifetime = Range(1.4f, 1.8f);
            fm.startSpeed = Range(4.5f, 5.5f);
            fm.startSize = Range(1.4f, 2.2f);
            fm.startRotation = Range(0f, 6.28f);
            fm.startColor = grey;
            fm.maxParticles = 400;
            Rate(funnel, 110f);
            var fs = funnel.shape;
            fs.enabled = true;
            fs.shapeType = ParticleSystemShapeType.Cone;
            fs.angle = 26f;
            fs.radius = 0.5f;
            var vel = funnel.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.orbitalX = 0f;
            vel.orbitalY = 0f;
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(5.5f);
            vel.radial = new ParticleSystem.MinMaxCurve(1.2f);
            SizeCurve(funnel, 0.5f, 2.6f);
            FadeInOut(funnel, 0.1f, 0.7f);
            var rol = funnel.rotationOverLifetime;
            rol.enabled = true;
            rol.z = Range(2f, 4f);

            var inner = Object.Instantiate(funnel.gameObject, root.transform);
            inner.name = "FunnelInner";
            var ip = inner.GetComponent<ParticleSystem>();
            var im = ip.main;
            im.startColor = new Color(0.35f, 0.36f, 0.4f, 0.7f);
            im.startSize = Range(0.8f, 1.3f);
            var ish = ip.shape;
            ish.angle = 14f;
            ish.radius = 0.25f;
            var iv = ip.velocityOverLifetime;
            iv.orbitalZ = new ParticleSystem.MinMaxCurve(8f);
            iv.radial = new ParticleSystem.MinMaxCurve(0.4f);

            // 바닥 먼지 고리
            var ring = NewPs(root, "DustRing", LightSmokeMat, new Vector3(0f, 0.2f, 0f), new Vector3(-90f, 0f, 0f));
            var rm = ring.main;
            rm.loop = true;
            rm.startLifetime = Range(0.8f, 1.2f);
            rm.startSize = Range(1.2f, 2f);
            rm.startColor = new Color(0.55f, 0.53f, 0.5f, 0.55f);
            rm.startRotation = Range(0f, 6.28f);
            Rate(ring, 40f);
            var rs = ring.shape;
            rs.enabled = true;
            rs.shapeType = ParticleSystemShapeType.Circle;
            rs.radius = 1.6f;
            var rv = ring.velocityOverLifetime;
            rv.enabled = true;
            rv.orbitalZ = new ParticleSystem.MinMaxCurve(4f);
            rv.radial = new ParticleSystem.MinMaxCurve(0.8f);
            FadeInOut(ring, 0.15f, 0.6f);

            // 휘말린 돌 조각
            var debris = NewPs(root, "Debris", LoadMat(EricMat + "stone02_3.mat"), euler: new Vector3(-90f, 0f, 0f));
            var dm = debris.main;
            dm.loop = true;
            dm.startLifetime = Range(1.2f, 1.6f);
            dm.startSpeed = Range(3f, 5f);
            dm.startSize = Range(0.12f, 0.3f);
            dm.startRotation = Range(0f, 6.28f);
            dm.startColor = new Color(0.55f, 0.5f, 0.45f, 1f);
            Rate(debris, 25f);
            var dsh = debris.shape;
            dsh.enabled = true;
            dsh.shapeType = ParticleSystemShapeType.Cone;
            dsh.angle = 22f;
            dsh.radius = 0.6f;
            var dv = debris.velocityOverLifetime;
            dv.enabled = true;
            dv.orbitalZ = new ParticleSystem.MinMaxCurve(6f);
            dv.radial = new ParticleSystem.MinMaxCurve(0.5f);
            FadeInOut(debris, 0.05f, 0.8f);
            Save(root);

            root = NewRoot("VFX_S05_Tornado_Cast");
            var gust = DarkSmoke(root, "Gust", 0.9f, 0.7f, 0f, 0.2f, new Color(0.7f, 0.72f, 0.75f, 0.6f));
            gust.GetComponent<ParticleSystemRenderer>().sharedMaterial = LightSmokeMat;
            var gm = gust.main;
            gm.loop = false;
            gm.startSpeed = Range(1f, 2.5f);
            Burst(gust, 12);
            var gv = gust.velocityOverLifetime;
            gv.enabled = true;
            gv.orbitalY = new ParticleSystem.MinMaxCurve(6f);
            Save(root);
        }

        // ── S06 번개 (Eric decal_fire10 낙뢰 3번) ─────────────────

        private static void BuildS06Lightning()
        {
            var bolt = TexMat("Mat_S06_Bolt", EricTex + "decal_fire10.png", true, new Color(1f, 0.95f, 0.8f, 1f), 2.5f);
            var root = NewRoot("VFX_S06_Lightning_Impact");

            // 하늘에서 내려꽂히는 번개 (세로 빌보드 2장, 짧게 깜빡임)
            for (int i = 0; i < 2; i++)
            {
                var ps = NewPs(root, "Bolt" + i, bolt, new Vector3(i == 0 ? 0f : 0.35f, 4.5f, 0f));
                var m = ps.main;
                m.startLifetime = Range(0.25f, 0.4f);
                m.startSize3D = true;
                m.startSizeX = Range(6f, 7.5f);
                m.startSizeY = Range(12.5f, 14f);
                m.startSizeZ = 1f;
                m.startColor = new Color(1f, 0.95f, 0.75f, 1f);
                m.startRotation = i == 0 ? 0f : Mathf.PI;
                Burst(ps, 1, i * 0.06f);
                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.VerticalBillboard;
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.9f, 0.6f), 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.3f, 0.25f), new GradientAlphaKey(1f, 0.45f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
            }
            Add(root, P52 + "Explode4.prefab", 0.3f, new Vector3(0f, 0.3f, 0f), name: "Flash");
            var sparks = Add(root, P52 + "LightningBall2.prefab", 0.12f, new Vector3(0f, 0.6f, 0f), name: "Sparks");
            SetLoop(sparks, false, 0.4f);
            Save(root);

            // 예고: 낙뢰 지점 위 먹구름
            root = NewRoot("VFX_S06_Lightning_Telegraph");
            var cloud = Add(root, P52 + "StormCloud.prefab", 0.12f, new Vector3(0f, 7f, 0f), name: "StormCloud");
            SetLoop(cloud, true, 1f);
            Multiply(cloud, new Color(0.7f, 0.7f, 0.85f, 1f));
            GroundCircle(root, "Mark", new Color(1f, 0.85f, 0.3f), 2f, true, 0.5f);
            Save(root);

            root = NewRoot("VFX_S06_Lightning_Cast");
            var orb = Add(root, P52 + "LightningRotateBall.prefab", 0.04f, name: "Orb");
            SetLoop(orb, false, 0.8f);
            Save(root);
        }

        // ── S07 총난사 (Hun0FX 불꽃 탄 15발) ─────────────────────

        private static void BuildS07Gatling()
        {
            var root = NewRoot("VFX_S07_Gatling_Projectile");
            var shot = Add(root, Hun + "FX_Fire_06.prefab", 0.3f, name: "FlameShot");
            ForPs(shot, ps =>
            {
                var m = ps.main;
                m.loop = true;
                m.startSpeed = Range(0.1f, 0.4f);
                m.startLifetime = new ParticleSystem.MinMaxCurve(Mathf.Min(m.startLifetime.constantMax, 0.25f));
                m.simulationSpace = ParticleSystemSimulationSpace.World;
                var sh = ps.shape;
                sh.enabled = true;
                sh.shapeType = ParticleSystemShapeType.Sphere;
                sh.radius = 0.3f;
            });
            GlowCore(root, "Core", new Color(1f, 0.65f, 0.2f, 1f), 0.5f, false);
            var trail = root.AddComponent<TrailRenderer>();
            trail.time = 0.15f;
            trail.startWidth = 0.25f;
            trail.endWidth = 0f;
            trail.sharedMaterial = LoadMat(Tex52 + "Core4.mat");
            trail.startColor = new Color(1f, 0.6f, 0.15f, 0.9f);
            trail.endColor = new Color(1f, 0.3f, 0f, 0f);
            Save(root);

            // 손에서 2초간 뿜어나오는 화염 (총구 불꽃)
            root = NewRoot("VFX_S07_Gatling_Cast");
            var muzzle = Add(root, Hun + "FX_Fire_06.prefab", 0.7f, euler: new Vector3(0f, -90f, 0f), name: "Muzzle");
            SetLoop(muzzle, false, 2f);
            Save(root);

            root = NewRoot("VFX_S07_Gatling_Impact");
            Add(root, P52 + "Explode7.prefab", 0.12f, name: "Hit");
            Recolor(root, new Color(1f, 0.55f, 0.15f));
            Save(root);
        }

        // ── S08 방어 쉴드 (ShieldEffect 구, 반경 1m 기준) ─────────────

        private static Material ShieldMat(string name, Color color, float glow, float hexScale)
        {
            var src = AssetDatabase.LoadAssetAtPath<Material>("Assets/ShieldEffect/Materials/M_HexShield_Blue.mat");
            var baseMat = Convert(src);
            return MatVariant(baseMat, name, m =>
            {
                m.SetColor("_BaseColor", color);
                m.SetFloat("_GlowIntensity", glow);
                m.SetFloat("_HexScale", hexScale);
            });
        }

        private static GameObject ShieldMesh(GameObject parent, string prefab, Material mat, string name)
        {
            var go = Add(parent, ShieldFx + prefab, 1f, name: name);
            if (go == null) return null;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = mat;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            return go;
        }

        private static void BuildS08Shield()
        {
            var mat = ShieldMat("Mat_S08_Shield", new Color(0.25f, 0.7f, 1f), 1.6f, 12f);
            var root = NewRoot("VFX_S08_Shield_Loop");
            var sphere = ShieldMesh(root, "PF_Shield_Sphere.prefab", mat, "Sphere");
            if (sphere != null) sphere.transform.localScale = Vector3.one * 2f; // 반경 1m
            var sparkle = NewPs(root, "Sparkle", LoadMat(Tex52 + "Core4.mat"));
            var sm = sparkle.main;
            sm.loop = true;
            sm.startLifetime = Range(0.6f, 1.1f);
            sm.startSize = Range(0.05f, 0.12f);
            sm.startColor = new Color(0.5f, 0.85f, 1f, 0.9f);
            Rate(sparkle, 25f);
            var ss = sparkle.shape;
            ss.enabled = true;
            ss.shapeType = ParticleSystemShapeType.Sphere;
            ss.radius = 1f;
            ss.radiusThickness = 0f;
            FadeInOut(sparkle, 0.2f, 0.5f);
            Save(root);

            root = NewRoot("VFX_S08_Shield_Cast");
            GroundCircle(root, "Circle", new Color(0.3f, 0.7f, 1f), 1.4f, false);
            Add(root, P52 + "Portal2.prefab", 0.15f, new Vector3(0f, 0.1f, 0f), name: "Flash");
            SetLoop(root.transform.Find("Flash").gameObject, false, 0.8f);
            Save(root);
        }

        // ── S09 타워 버프 (ShieldEffect 상자, 1m 상자 · 바닥 피벗 기준) ─────────

        private static void BuildS09TowerBuff()
        {
            var mat = ShieldMat("Mat_S09_TowerBuff", new Color(1f, 0.75f, 0.2f), 1.3f, 6f);
            var root = NewRoot("VFX_S09_TowerBuff_Loop");
            var cube = ShieldMesh(root, "PF_Shield_Cube.prefab", mat, "Cube");
            if (cube != null)
            {
                cube.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                cube.transform.localScale = Vector3.one;
            }
            var rise = NewPs(root, "RisingGold", LoadMat(Tex52 + "Core4.mat"));
            var rm = rise.main;
            rm.loop = true;
            rm.startLifetime = Range(0.8f, 1.4f);
            rm.startSize = Range(0.03f, 0.07f);
            rm.startSpeed = 0f;
            rm.startColor = new Color(1f, 0.8f, 0.3f, 1f);
            rm.scalingMode = ParticleSystemScalingMode.Shape; // 상자 크기만 따라가고 입자 크기는 유지
            Rate(rise, 40f);
            var rs = rise.shape;
            rs.enabled = true;
            rs.shapeType = ParticleSystemShapeType.Box;
            rs.position = new Vector3(0f, 0.5f, 0f);
            rs.scale = new Vector3(1f, 1f, 1f);
            var rv = rise.velocityOverLifetime;
            rv.enabled = true;
            rv.y = new ParticleSystem.MinMaxCurve(1.2f);
            FadeInOut(rise, 0.2f, 0.6f);
            Save(root);

            root = NewRoot("VFX_S09_TowerBuff_Cast");
            GroundCircle(root, "Circle", new Color(1f, 0.78f, 0.25f), 3f, false, 1.5f);
            var up = Add(root, Eric + "FX_LightPillar.prefab", 1.2f, name: "Pillar");
            SetLoop(up, false, 1f);
            Save(root);
        }

        // ── S10 집중 ─────────────────────────────────────────

        private static void BuildS10Focus()
        {
            var purple = new Color(0.7f, 0.45f, 1f);
            var root = NewRoot("VFX_S10_Focus_Loop");
            GroundCircle(root, "Circle", purple, 0.9f, true, 1f);
            var motes = NewPs(root, "Motes", LoadMat(Tex52 + "Core4.mat"));
            var mm = motes.main;
            mm.loop = true;
            mm.startLifetime = Range(1f, 1.6f);
            mm.startSize = Range(0.05f, 0.1f);
            mm.startColor = new Color(0.8f, 0.6f, 1f, 1f);
            Rate(motes, 20f);
            var ms = motes.shape;
            ms.enabled = true;
            ms.shapeType = ParticleSystemShapeType.Circle;
            ms.radius = 0.9f;
            ms.rotation = new Vector3(-90f, 0f, 0f);
            var mv = motes.velocityOverLifetime;
            mv.enabled = true;
            mv.y = new ParticleSystem.MinMaxCurve(0.8f);
            mv.orbitalY = new ParticleSystem.MinMaxCurve(1.5f);
            FadeInOut(motes, 0.2f, 0.6f);
            Save(root);

            root = NewRoot("VFX_S10_Focus_Cast");
            var rune = Add(root, P52 + "RuneOfMagic.prefab", 0.2f, new Vector3(0f, 0.1f, 0f), name: "Rune");
            Recolor(rune, purple);
            var star = Add(root, P52 + "StarCore.prefab", 0.06f, new Vector3(0f, 1.4f, 0f), name: "Star");
            SetLoop(star, false, 0.8f);
            Recolor(star, purple);
            Save(root);
        }

        // ── 소환 · 설치 시전 (마법진) ──────────────────────────

        private static void SummonCircle(string name, string circlePrefab, Color color, float scale)
        {
            var root = NewRoot(name);
            GroundCircle(root, "Ring", color, 1.6f, false, 1.4f);
            var circle = Add(root, P52 + circlePrefab, scale, name: "Circle");
            SetLoop(circle, false, 1.2f);
            Recolor(circle, color);
            Add(root, P52 + "Explode10.prefab", 0.08f, new Vector3(0f, 0.3f, 0f), name: "Dust");
            Multiply(root.transform.Find("Dust").gameObject, new Color(0.6f, 0.55f, 0.5f, 0.8f));
            Save(root);
        }

        private static void BuildSummonCasts()
        {
            SummonCircle("VFX_S11_AxeWarrior_Cast", "SummonMagicCircle2.prefab", new Color(0.45f, 0.95f, 0.3f), 0.13f);
            SummonCircle("VFX_S12_Mage_Cast", "SummonMagicCircle3.prefab", new Color(1f, 0.35f, 0.15f), 0.22f);
            SummonCircle("VFX_S13_Turret_Cast", "SummonMagicCircle3.prefab", new Color(0.55f, 0.75f, 1f), 0.18f);
            SummonCircle("VFX_H06_SummonTower_Cast", "SummonMagicCircle2.prefab", new Color(0.75f, 0.5f, 1f), 0.15f);
        }

        // ── H01 프리즈 (Eric VFX_SpriteSheet_18x18 얼음 장판) ──────────

        private static Material IceSheetMat => TexMat("Mat_H01_IceSheet", EricTex + "VFX_SpriteSheet_18x18_224749.png", true, Color.white, 1.4f);

        /// <summary>18×18 스프라이트 시트 얼음 — 세로 빌보드, lifetime 동안 한 번 재생 (생성 → 유지 → 깨짐)</summary>
        private static ParticleSystem IceSheet(GameObject parent, string name, Vector3 pos, Vector3 box, int count, float life, float size, bool loop)
        {
            var ps = NewPs(parent, name, IceSheetMat, pos);
            var m = ps.main;
            m.loop = loop;
            m.duration = loop ? life : 0.2f;
            m.startLifetime = life;
            m.startSize = Range(size * 0.85f, size * 1.15f);
            m.startColor = new Color(0.85f, 0.95f, 1f, 1f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            r.pivot = new Vector3(0f, 0.15f, 0f); // 얼음이 시트 가운데에 있어 바닥에 닿도록 내림
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.numTilesX = 18;
            tsa.numTilesY = 18;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            tsa.cycleCount = 1;
            if (loop) Rate(ps, count / Mathf.Max(0.1f, life));
            else
            {
                var e = ps.emission;
                e.enabled = true;
                e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            }
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = box;
            return ps;
        }

        private static void BuildH01Freeze()
        {
            // 한 칸(레인 폭 5m × 깊이 약 8m)마다 하나 — 서버가 3열 × 5줄 = 15칸에 발행 (얼음은 5초 동안 생성 → 유지 → 깨짐)
            var root = NewRoot("VFX_H01_Freeze_Field");
            IceSheet(root, "Ice", Vector3.zero, new Vector3(3.2f, 0f, 6f), 3, 5f, 8f, false);
            var mist = NewPs(root, "FrostMist", LightSmokeMat, new Vector3(0f, 0.15f, 0f));
            var mm = mist.main;
            mm.loop = true;
            mm.duration = 1f;
            mm.startLifetime = Range(1.2f, 2f);
            mm.startSize = Range(1.5f, 2.5f);
            mm.startRotation = Range(0f, 6.28f);
            mm.startColor = new Color(0.75f, 0.9f, 1f, 0.35f);
            Rate(mist, 14f);
            var ms = mist.shape;
            ms.enabled = true;
            ms.shapeType = ParticleSystemShapeType.Box;
            ms.scale = new Vector3(4.5f, 0.1f, 7.5f);
            FadeInOut(mist, 0.2f, 0.6f);
            var burst = Add(root, P52 + "IceCloud.prefab", 0.12f, new Vector3(0f, 0.3f, 0f), name: "Burst");
            Save(root);

            // 타워 · 구조물 빙결 표시 (반경 1m 기준, 유지 구간 프레임 반복)
            root = NewRoot("VFX_H01_Freeze_Loop");
            var ice = IceSheet(root, "Ice", Vector3.zero, new Vector3(0.8f, 0f, 0.8f), 2, 2f, 3.2f, true);
            var tsa = ice.textureSheetAnimation;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.12f, 1f, 0.62f));
            FadeInOut(ice, 0.15f, 0.85f);
            Save(root);

            root = NewRoot("VFX_H01_Freeze_Cast");
            Add(root, P52 + "IceCloud.prefab", 0.1f, name: "Cloud");
            var star = Add(root, P52 + "StarCore.prefab", 0.07f, new Vector3(0f, 0.5f, 0f), name: "Star");
            SetLoop(star, false, 0.8f);
            Recolor(star, new Color(0.5f, 0.85f, 1f));
            Save(root);
        }

        // ── H02 레이저 빔 (Eric 빔 재질, 길이 1m 기준 → VFXPlayer가 전장 길이만큼 늘림) ──────

        private static GameObject BeamQuad(GameObject parent, string name, Material mat, float width, Quaternion rot)
        {
            var go = Child(parent, name);
            go.transform.localRotation = rot;
            go.transform.localScale = new Vector3(width, 1f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = BuiltinMesh("Quad.fbx");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private static void BuildH02Laser()
        {
            var cyan = new Color(0.5f, 0.95f, 1f, 1f);
            var beamMat = MatVariant(LoadMat(EricMat + "Gradient_Beam_007.mat"), "Mat_H02_Beam", m =>
            {
                m.SetColor("_TintColor", cyan * 1.6f);
                m.SetFloat("_Brightness", 2.2f);
                m.SetVector("_FlowSpeed", new Vector4(0f, -3f, 0f, 0f));
            });
            var coreMat = MatVariant(LoadMat(EricMat + "Gradient_Beam_007.mat"), "Mat_H02_BeamCore", m =>
            {
                m.SetColor("_TintColor", new Color(1f, 1f, 1f, 1f) * 2f);
                m.SetFloat("_Brightness", 3f);
                m.SetVector("_FlowSpeed", new Vector4(0f, -6f, 0f, 0f));
            });
            // 쿼드 길이축(Y)을 +Z로: 가로 띠 · 세로 띠 십자
            var flat = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var upright = Quaternion.LookRotation(Vector3.right, Vector3.forward);

            var root = NewRoot("VFX_H02_Laser_Field");
            BeamQuad(root, "BeamFlat", beamMat, 2.2f, flat);
            BeamQuad(root, "BeamUp", beamMat, 2.2f, upright);
            BeamQuad(root, "CoreFlat", coreMat, 0.6f, flat);
            BeamQuad(root, "CoreUp", coreMat, 0.6f, upright);
            var sparks = NewPs(root, "Sparks", LoadMat(Tex52 + "Core4.mat"));
            var sm = sparks.main;
            sm.loop = true;
            sm.startLifetime = Range(0.3f, 0.6f);
            sm.startSize = Range(0.08f, 0.2f);
            sm.startSpeed = Range(1f, 3f);
            sm.startColor = cyan;
            sm.scalingMode = ParticleSystemScalingMode.Shape;
            sm.maxParticles = 400;
            Rate(sparks, 220f);
            var ss = sparks.shape;
            ss.enabled = true;
            ss.shapeType = ParticleSystemShapeType.Box;
            ss.scale = new Vector3(1.2f, 1.2f, 1f);
            FadeInOut(sparks, 0.05f, 0.5f);
            Save(root);

            // 0.5초 예고선 (가늘게)
            var warnMat = MatVariant(LoadMat(EricMat + "Gradient_Beam_007.mat"), "Mat_H02_Warning", m =>
            {
                m.SetColor("_TintColor", new Color(1f, 0.25f, 0.2f, 1f));
                m.SetFloat("_Brightness", 1.2f);
                m.SetVector("_FlowSpeed", new Vector4(0f, -8f, 0f, 0f));
            });
            root = NewRoot("VFX_H02_Laser_Telegraph");
            BeamQuad(root, "LineFlat", warnMat, 0.25f, flat);
            BeamQuad(root, "LineUp", warnMat, 0.25f, upright);
            Save(root);

            root = NewRoot("VFX_H02_Laser_Cast");
            var pillar = Add(root, Eric + "FX_LightPillar.prefab", 0.6f, name: "Pillar");
            SetLoop(pillar, false, 2.5f);
            Recolor(pillar, cyan);
            Save(root);

            root = NewRoot("VFX_H02_Laser_Impact");
            Add(root, P52 + "Explode8.prefab", 0.2f, name: "Hit");
            Recolor(root, cyan);
            Save(root);
        }

        // ── H03 흡혈 (Eric 불덩이가 상대 → 나에게 2초) ─────────────

        private static void BuildH03Drain()
        {
            var crimson = new Color(1f, 0.12f, 0.2f);
            var root = NewRoot("VFX_H03_Drain_Projectile");
            var ball = Add(root, Eric + "FX_Fireball.prefab", 0.6f, name: "LifeFire");
            Recolor(ball, crimson);
            SetSimulationSpace(ball, ParticleSystemSimulationSpace.World);
            Save(root);

            // 상대에게서 생명력이 뜯겨 나가는 순간
            root = NewRoot("VFX_H03_Drain_Impact");
            var hit = Add(root, Eric + "FX_Purple_Hit_02.prefab", 1f, name: "Hit");
            Recolor(hit, crimson);
            Add(root, P52 + "DeadExplode.prefab", 0.08f, name: "Burst");
            Recolor(root.transform.Find("Burst").gameObject, crimson);
            Save(root);

            // 시전 (내 쪽)
            root = NewRoot("VFX_H03_Drain_Telegraph");
            var rune = Add(root, P52 + "RuneOfMagic.prefab", 0.18f, name: "Rune");
            Recolor(rune, crimson);
            Save(root);

            // 도착 → 회복
            root = NewRoot("VFX_H03_Drain_Loop");
            var heal = Add(root, Eric + "FX_Greenlight_shrink.prefab", 0.45f, new Vector3(0f, 1f, 0f), name: "Heal");
            SetLoop(heal, false, 1.2f);
            Recolor(heal, crimson);
            GroundCircle(root, "Circle", crimson, 1.2f, false);
            Save(root);
        }

        // ── H04 반사 결계 (ShieldEffect 판 6장 육각 프리즘, 반경 1m · 높이 2m 기준) ─────

        private static void BuildH04Reflect()
        {
            var purple = new Color(0.75f, 0.35f, 1f);
            var mat = ShieldMat("Mat_H04_Reflect", purple, 2.2f, 8f);
            var root = NewRoot("VFX_H04_ReflectBarrier_Loop");
            var spin = Child(root, "Spin");
            const int panels = 6;
            float side = 2f * Mathf.Tan(Mathf.PI / panels); // 반경 1m 육각형 한 변
            for (int i = 0; i < panels; i++)
            {
                float a = i * 360f / panels;
                var pivot = Child(spin, "Panel" + i, euler: new Vector3(0f, a, 0f));
                var quad = ShieldMesh(pivot, "PF_Shield_Quad.prefab", mat, "Quad");
                if (quad == null) continue;
                quad.transform.localPosition = new Vector3(0f, 1f, 1f);
                quad.transform.localRotation = Quaternion.identity;
                quad.transform.localScale = new Vector3(side * 1.02f, 2f, 1f);
            }
            AddSpinAnimation(spin, 8f);
            var floor = Add(root, P52 + "SummonMagicCircle3.prefab", 0.09f, new Vector3(0f, 0.05f, 0f), name: "FloorCircle");
            Recolor(floor, purple);
            var cube = Add(root, P52 + "MagicCube.prefab", 0.08f, new Vector3(0f, 2.15f, 0f), name: "Crown");
            Recolor(cube, purple);
            var motes = NewPs(root, "Motes", LoadMat(Tex52 + "Core4.mat"));
            var mm = motes.main;
            mm.loop = true;
            mm.startLifetime = Range(1f, 1.6f);
            mm.startSize = Range(0.05f, 0.14f);
            mm.startColor = new Color(0.85f, 0.6f, 1f, 1f);
            Rate(motes, 40f);
            var ms = motes.shape;
            ms.enabled = true;
            ms.shapeType = ParticleSystemShapeType.Circle;
            ms.radius = 1f;
            ms.radiusThickness = 0f;
            ms.rotation = new Vector3(-90f, 0f, 0f);
            var mv = motes.velocityOverLifetime;
            mv.enabled = true;
            mv.y = new ParticleSystem.MinMaxCurve(1.3f);
            mv.orbitalY = new ParticleSystem.MinMaxCurve(2f);
            FadeInOut(motes, 0.2f, 0.6f);
            Save(root);

            // 결계를 펼칠 때 · 반사할 때 번쩍
            root = NewRoot("VFX_H04_ReflectBarrier_Cast");
            Add(root, P52 + "RainbowExplode2.prefab", 0.15f, new Vector3(0f, 0.8f, 0f), name: "Flash");
            Recolor(root, purple);
            Save(root);

            // 되돌아가는 작은 탄 (무조건 명중)
            root = NewRoot("VFX_H04_ReflectBarrier_Projectile");
            var bolt = Add(root, P52 + "ElementalBall3.prefab", 0.05f, name: "Bolt");
            Recolor(bolt, purple);
            GlowCore(root, "Core", new Color(0.85f, 0.5f, 1f, 1f), 0.6f, false);
            GlowCore(root, "Trail", new Color(0.7f, 0.35f, 1f, 0.8f), 0.45f, true);
            Save(root);

            root = NewRoot("VFX_H04_ReflectBarrier_Impact");
            Add(root, P52 + "RainbowExplode.prefab", 0.25f, name: "Hit");
            Recolor(root, purple);
            Save(root);
        }

        /// <summary>스크립트 없이 천천히 도는 레거시 애니메이션 (Y축 360°)</summary>
        private static void AddSpinAnimation(GameObject go, float secondsPerTurn)
        {
            string path = $"{MatOut}/Anim_Spin_{secondsPerTurn:0}s.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { legacy = true, wrapMode = WrapMode.Loop, name = "Spin" };
                var curve = AnimationCurve.Linear(0f, 0f, secondsPerTurn, 360f);
                UnityEditor.AnimationUtility.SetEditorCurve(clip, UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Transform), "localEulerAnglesRaw.y"), curve);
                UnityEditor.AnimationUtility.SetEditorCurve(clip, UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Transform), "localEulerAnglesRaw.x"), AnimationCurve.Constant(0f, secondsPerTurn, 0f));
                UnityEditor.AnimationUtility.SetEditorCurve(clip, UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Transform), "localEulerAnglesRaw.z"), AnimationCurve.Constant(0f, secondsPerTurn, 0f));
                UnityEditor.AssetDatabase.CreateAsset(clip, path);
            }
            var anim = go.AddComponent<Animation>();
            anim.clip = clip;
            anim.AddClip(clip, clip.name);
            anim.playAutomatically = true;
            anim.wrapMode = WrapMode.Loop;
        }

        // ── H05 분노 (3열을 덮는 붉은 장판) ──────────────────────

        private static void BuildH05Rage()
        {
            var red = new Color(1f, 0.2f, 0.1f);
            const float laneSpacing = 5f;
            var root = NewRoot("VFX_H05_Rage_Field");
            var big = Add(root, P52 + "SummonMagicCircle2.prefab", 0.85f, new Vector3(0f, 0.05f, 0f), name: "BigCircle");
            big.transform.localScale = new Vector3(big.transform.localScale.x, big.transform.localScale.y * 0.3f, big.transform.localScale.z);
            Recolor(big, red);
            for (int c = -1; c <= 1; c++)
            {
                var fire = Add(root, Hun + "FX_Fire_02.prefab", 0.8f, new Vector3(c * laneSpacing, 0f, 0f), name: "LaneFire" + c);
                SetLoop(fire, true, 1f);
            }
            var embers = NewPs(root, "Embers", LoadMat(Tex52 + "Core4.mat"));
            var em = embers.main;
            em.loop = true;
            em.startLifetime = Range(1f, 1.8f);
            em.startSize = Range(0.06f, 0.16f);
            em.startColor = new Color(1f, 0.35f, 0.15f, 1f);
            em.maxParticles = 300;
            Rate(embers, 120f);
            var es = embers.shape;
            es.enabled = true;
            es.shapeType = ParticleSystemShapeType.Box;
            es.scale = new Vector3(laneSpacing * 3f, 0.1f, 12f);
            var ev = embers.velocityOverLifetime;
            ev.enabled = true;
            // 세 축은 같은 모드여야 한다 (다르면 "Particle Velocity curves must all be in the same mode" 오류)
            ev.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            ev.y = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            ev.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            FadeInOut(embers, 0.1f, 0.6f);
            Save(root);

            root = NewRoot("VFX_H05_Rage_Cast");
            Add(root, P52 + "Explode5.prefab", 0.08f, new Vector3(0f, 0.5f, 0f), name: "Roar");
            Recolor(root, red);
            Save(root);
        }

        // ── 유닛 연출 (ArenaStateView) ──────────────────────────

        private static void BuildUnitVfx()
        {
            var root = NewRoot("VFX_Unit_Spawn");
            var portal = Add(root, P52 + "Portal2.prefab", 0.12f, new Vector3(0f, 0.05f, 0f), name: "Portal");
            SetLoop(portal, false, 0.8f);
            GroundCircle(root, "Ring", new Color(0.75f, 0.55f, 1f), 1.1f, false, 0.8f);
            Add(root, P52 + "Explode10.prefab", 0.06f, new Vector3(0f, 0.4f, 0f), name: "Dust");
            Save(root);

            root = NewRoot("VFX_Unit_Death");
            Add(root, P52 + "DeadExplode.prefab", 0.12f, name: "Poof");
            var smoke = DarkSmoke(root, "Smoke", 0.9f, 0.9f, 0f, 0.3f, new Color(0.25f, 0.25f, 0.28f, 0.7f));
            var sm2 = smoke.main;
            sm2.loop = false;
            sm2.startSpeed = Range(0.5f, 1.5f);
            Burst(smoke, 10);
            Save(root);

            root = NewRoot("VFX_Unit_AxeOrc_Attack");
            Add(root, Eric + "FX_Orange_Slash_1.prefab", 1.2f, euler: new Vector3(0f, 0f, 0f), name: "Slash");
            Save(root);

            root = NewRoot("VFX_Unit_Dragon_Attack");
            var breath = Add(root, Hun + "FX_Fire_06.prefab", 1.1f, euler: new Vector3(0f, -90f, 0f), name: "Breath");
            SetLoop(breath, false, 0.8f);
            Save(root);

            root = NewRoot("VFX_Unit_Dragon_Impact");
            var fire = Add(root, Hun + "FX_Fire_02.prefab", 0.6f, name: "Fire");
            SetLoop(fire, false, 0.8f);
            Save(root);

            root = NewRoot("VFX_Unit_Turret_Projectile");
            ArrowModel(root, 2.0f);
            Save(root);
        }
    }
}
