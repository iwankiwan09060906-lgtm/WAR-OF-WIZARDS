// 스킬 에셋 빌더 (에디터 전용) — 새로 들어온 에셋 팩으로 19종 스킬의 VFX · 소환수 · 설치물 · 스킬 데이터를 만든다.
//   Spellbound > Skills > Build All Skill Assets   : 아래 셋을 순서대로 + VFX 카탈로그 갱신
//   Spellbound > Skills > Rebuild Spell Definitions (19) : Spell_*.asset 19종을 코드 기본값으로 덮어쓰기 + SpellCatalog · Minion_* 에셋
//   Spellbound > Skills > Build Skill VFX Prefabs   : Assets/Prefabs/VFX/Skills/VFX_{SkillId}_{Name}_{Part}.prefab
//   Spellbound > Skills > Build Summon & Structure Models : Assets/Prefabs/Summon/MDL_Summon_* · MDL_Structure_*
//
// 렌더 파이프라인: 이 프로젝트는 Built-in이다. URP 전용 셰이더(Eric VFX Studio · ShieldEffect · Nora Squad)와
// HDRP 재질(ZNS3D)은 그대로 쓰면 분홍색이 되므로 Assets/Art/Materials/Skills/Converted 에 Built-in 셰이더 복제본을 만든다
// (Spellbound/FxAdditiveFlow · FxAlphaFlow · HexShield · Standard).
// 에셋 팩 원본은 수정하지 않는다. 다시 실행하면 결과물을 덮어쓴다.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SpellboundVR.EditorTools
{
    public static partial class SpellboundSkillAssetBuilder
    {
        // ── 원본 경로 ─────────────────────────────────────────
        private const string P52 = "Assets/52SpecialEffectPack/Effect/Effect(Shuriken)/";
        private const string Tex52 = "Assets/52SpecialEffectPack/Texture/";
        private const string Hun = "Assets/Hun0FX/FX/FireFX_vol1/Prefabs/";
        private const string Eric = "Assets/Eric VFX Studio/Free Game VFX/Prefab/";
        private const string EricMat = "Assets/Eric VFX Studio/Resource/Materials/";
        private const string EricTex = "Assets/Eric VFX Studio/Resource/Textures/";
        private const string EricSprite = "Assets/Eric VFX Studio/Free RPG VFX Sprite Sheet Starter Pack/Prefabs/";
        private const string ShieldFx = "Assets/ShieldEffect/Prefabs/";
        private const string Zns = "Assets/ZNS3D/MEDIEVAL_3D_GAME_PACK/PREFABS/";

        // ── 출력 경로 ─────────────────────────────────────────
        public const string VfxOut = "Assets/Prefabs/VFX/Skills";
        public const string ModelOut = "Assets/Prefabs/Summon";
        private const string MatOut = "Assets/Art/Materials/Skills";
        private const string ConvertedOut = MatOut + "/Converted";
        private const string CtrlOut = "Assets/Art/Models/Summon";

        private static Scene s_scene;
        private static readonly Dictionary<Material, Material> s_converted = new Dictionary<Material, Material>();
        private static readonly List<string> s_built = new List<string>();

        [MenuItem("Spellbound/Skills/Build All Skill Assets (VFX + models + spell data)", priority = 210)]
        public static void BuildAll()
        {
            BuildSpellData();
            BuildVfx();
            BuildModels();
            SpellboundSetupWizard.RefreshVfxCatalogMenu();
            Debug.Log("[Skill Builder] 전체 완료 — Play로 19종 테스트 (덱 밖 룬도 로컬 봇전에서 테스트 시전됨)");
        }

        [MenuItem("Spellbound/Skills/Build Skill VFX Prefabs", priority = 212)]
        public static void BuildVfx()
        {
            Run("VFX", () =>
            {
                SpellboundSetupWizard.EnsureFolder(VfxOut);
                BuildSkillVfx();
                BuildUnitVfx();
            });
            SpellboundSetupWizard.RefreshVfxCatalogMenu();
        }

        [MenuItem("Spellbound/Skills/Build Summon & Structure Models", priority = 213)]
        public static void BuildModels()
        {
            Run("Models", () =>
            {
                SpellboundSetupWizard.EnsureFolder(ModelOut);
                SpellboundSetupWizard.EnsureFolder(CtrlOut);
                BuildSummonModels();
            });
            SpellboundSetupWizard.RefreshVfxCatalogMenu();
        }

        private static void Run(string label, Action body)
        {
            s_converted.Clear();
            s_built.Clear();
            SpellboundSetupWizard.EnsureFolder(ConvertedOut);
            s_scene = EditorSceneManager.NewPreviewScene();
            try
            {
                body();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(s_scene);
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[Skill Builder] {label} {s_built.Count}개 생성: {string.Join(", ", s_built)}");
        }

        // ── 공용: 프리팹 조립 ─────────────────────────────────

        private static GameObject NewRoot(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, s_scene);
            return go;
        }

        private static GameObject Child(GameObject parent, string name, Vector3 pos = default, Vector3 euler = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            return go;
        }

        /// <summary>
        /// 에셋 팩 프리팹을 풀어서(Unpack) 자식으로 넣는다. 스크립트는 제거(풀링과 충돌 · 자체 파괴 방지),
        /// 파티클 크기 모드는 Hierarchy(부모 스케일로 줄이기), URP · HDRP 재질은 Built-in 복제본으로 교체.
        /// </summary>
        private static GameObject Add(GameObject parent, string prefabPath, float scale = 1f, Vector3 pos = default, Vector3 euler = default, string name = null)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (src == null)
            {
                Debug.LogWarning("[Skill Builder] 원본 없음: " + prefabPath);
                return null;
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, s_scene);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.SetParent(parent.transform, false);
            inst.transform.localPosition = pos;
            inst.transform.localRotation = Quaternion.Euler(euler) * src.transform.localRotation;
            inst.transform.localScale = src.transform.localScale * scale;
            if (name != null) inst.name = name;
            StripScripts(inst);
            foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var m = ps.main;
                m.scalingMode = ParticleSystemScalingMode.Hierarchy;
                m.playOnAwake = true;
                m.stopAction = ParticleSystemStopAction.None;
            }
            ConvertMaterials(inst);
            NoShadows(inst);
            return inst;
        }

        private static void StripScripts(GameObject go)
        {
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (var au in go.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(au);
            foreach (var l in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
        }

        private static void NoShadows(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        private static void Save(GameObject root, string folder = VfxOut)
        {
            string path = $"{folder}/{root.name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            s_built.Add(Path.GetFileNameWithoutExtension(path));
        }

        // ── 공용: 파티클 조정 ─────────────────────────────────

        private static void ForPs(GameObject go, Action<ParticleSystem> action)
        {
            if (go == null) return;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) action(ps);
        }

        /// <summary>반복 여부 · 길이 (VFXPlayer가 수명 끝에 방출을 멈추므로 지속형은 loop = true)</summary>
        private static void SetLoop(GameObject go, bool loop, float duration = -1f)
        {
            ForPs(go, ps =>
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var m = ps.main;
                m.loop = loop;
                if (duration > 0f) m.duration = duration;
            });
        }

        private static void SetSimulationSpace(GameObject go, ParticleSystemSimulationSpace space)
        {
            ForPs(go, ps =>
            {
                var m = ps.main;
                m.simulationSpace = space;
            });
        }

        /// <summary>색상(Hue)만 바꾼다 — 밝기 · 알파 · 그라데이션 모양은 유지 (보라 독 → 초록 독 등)</summary>
        private static void Recolor(GameObject go, Color target, float saturation = -1f)
        {
            Color.RGBToHSV(target, out float th, out float ts, out _);
            if (saturation >= 0f) ts = saturation;
            Color Shift(Color c)
            {
                Color.RGBToHSV(c, out _, out float s, out float v);
                var r = Color.HSVToRGB(th, Mathf.Max(s, 0.001f) > 0.02f ? Mathf.Lerp(s, ts, 0.85f) : s * 0.5f + ts * 0.5f, v);
                r.a = c.a;
                return r;
            }
            Gradient ShiftG(Gradient g)
            {
                if (g == null) return null;
                var keys = g.colorKeys;
                for (int i = 0; i < keys.Length; i++) keys[i].color = Shift(keys[i].color);
                var ng = new Gradient { mode = g.mode };
                ng.SetKeys(keys, g.alphaKeys);
                return ng;
            }
            ParticleSystem.MinMaxGradient ShiftMM(ParticleSystem.MinMaxGradient mm)
            {
                switch (mm.mode)
                {
                    case ParticleSystemGradientMode.Color: mm.color = Shift(mm.color); break;
                    case ParticleSystemGradientMode.TwoColors: mm.colorMin = Shift(mm.colorMin); mm.colorMax = Shift(mm.colorMax); break;
                    case ParticleSystemGradientMode.Gradient:
                    case ParticleSystemGradientMode.RandomColor: mm.gradient = ShiftG(mm.gradient); break;
                    case ParticleSystemGradientMode.TwoGradients: mm.gradientMin = ShiftG(mm.gradientMin); mm.gradientMax = ShiftG(mm.gradientMax); break;
                }
                return mm;
            }
            ForPs(go, ps =>
            {
                var m = ps.main;
                m.startColor = ShiftMM(m.startColor);
                var col = ps.colorOverLifetime;
                if (col.enabled) col.color = ShiftMM(col.color);
            });
        }

        /// <summary>시작 색에 곱하기 (어둡게 · 투명하게)</summary>
        private static void Multiply(GameObject go, Color mul)
        {
            ParticleSystem.MinMaxGradient Mul(ParticleSystem.MinMaxGradient mm)
            {
                switch (mm.mode)
                {
                    case ParticleSystemGradientMode.Color: mm.color *= mul; break;
                    case ParticleSystemGradientMode.TwoColors: mm.colorMin *= mul; mm.colorMax *= mul; break;
                    default:
                        var g = mm.mode == ParticleSystemGradientMode.TwoGradients ? mm.gradientMax : mm.gradient;
                        if (g != null)
                        {
                            var keys = g.colorKeys;
                            for (int i = 0; i < keys.Length; i++) keys[i].color *= mul;
                            var alphas = g.alphaKeys;
                            for (int i = 0; i < alphas.Length; i++) alphas[i].alpha *= mul.a;
                            var ng = new Gradient { mode = g.mode };
                            ng.SetKeys(keys, alphas);
                            if (mm.mode == ParticleSystemGradientMode.TwoGradients) { mm.gradientMax = ng; mm.gradientMin = ng; }
                            else mm.gradient = ng;
                        }
                        break;
                }
                return mm;
            }
            ForPs(go, ps =>
            {
                var m = ps.main;
                m.startColor = Mul(m.startColor);
            });
        }

        /// <summary>새 파티클 시스템 (기본: 방출 0, 모양 없음, 한 번, 로컬, 크기 계층 스케일)</summary>
        private static ParticleSystem NewPs(GameObject parent, string name, Material mat, Vector3 pos = default, Vector3 euler = default)
        {
            var go = Child(parent, name, pos, euler);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var m = ps.main;
            m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            m.playOnAwake = true;
            m.loop = false;
            m.duration = 1f;
            m.startSpeed = 0f;
            m.startLifetime = 1f;
            m.startSize = 1f;
            m.maxParticles = 200;
            m.simulationSpace = ParticleSystemSimulationSpace.Local;
            var e = ps.emission;
            e.rateOverTime = 0f;
            var sh = ps.shape;
            sh.enabled = false;
            return ps;
        }

        private static ParticleSystem.MinMaxCurve Range(float a, float b) => new ParticleSystem.MinMaxCurve(a, b);

        private static void Burst(ParticleSystem ps, int count, float time = 0f)
        {
            var e = ps.emission;
            e.enabled = true;
            e.SetBursts(new[] { new ParticleSystem.Burst(time, (short)count) });
        }

        private static void Rate(ParticleSystem ps, float rate)
        {
            var e = ps.emission;
            e.enabled = true;
            e.rateOverTime = rate;
        }

        /// <summary>알파 0 → 1 → 0 페이드 (색 유지)</summary>
        private static void FadeInOut(ParticleSystem ps, float inT = 0.15f, float outT = 0.6f, Color? tint = null)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            Color c = tint ?? Color.white;
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inT), new GradientAlphaKey(1f, outT), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        private static void SizeCurve(ParticleSystem ps, float start, float end)
        {
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, start, 1f, end));
        }

        private static Mesh BuiltinMesh(string file) => Resources.GetBuiltinResource<Mesh>(file);

        // ── 공용: 재질 ───────────────────────────────────────

        private static Material LoadMat(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) Debug.LogWarning("[Skill Builder] 재질 없음: " + path);
            return Convert(m);
        }

        private static void ConvertMaterials(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var c = Convert(mats[i]);
                    if (c != mats[i]) { mats[i] = c; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
                if (r is ParticleSystemRenderer psr && psr.trailMaterial != null) psr.trailMaterial = Convert(psr.trailMaterial);
            }
        }

        private static bool NeedsConversion(Material m)
        {
            if (m == null || m.shader == null) return false;
            string n = m.shader.name;
            return n.StartsWith("Eric/") || n.StartsWith("ShieldEffect/") || n.StartsWith("Nora Squad") || n.StartsWith("Universal Render Pipeline")
                   || n.StartsWith("HDRP") || n == "Hidden/InternalErrorShader" || !m.shader.isSupported;
        }

        /// <summary>URP · HDRP 재질 → Built-in 복제본 (원본은 그대로). 이미 호환이면 원본을 돌려준다.</summary>
        public static Material Convert(Material src)
        {
            if (src == null) return null;
            if (s_converted.TryGetValue(src, out var cached)) return cached;
            if (!NeedsConversion(src))
            {
                s_converted[src] = src;
                return src;
            }

            string n = src.shader.name;
            string path = $"{ConvertedOut}/{Sanitize(src.name)}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Material m;
            if (n == "Eric/URP_AdditiveFlow" || n == "Eric/URP_AlphaBlendFlow" || n == "ShieldEffect/HexShield")
            {
                var target = Shader.Find(n == "Eric/URP_AdditiveFlow" ? "Spellbound/FxAdditiveFlow" : n == "Eric/URP_AlphaBlendFlow" ? "Spellbound/FxAlphaFlow" : "Spellbound/HexShield");
                m = existing != null ? existing : new Material(target);
                m.shader = src.shader;
                m.CopyPropertiesFromMaterial(src);
                m.shader = target;
                SyncToggleKeyword(m, "_UseFresnel", "_FRESNEL_ON");
                SyncToggleKeyword(m, "_FresnelMasksTexture", "_FRESNEL_MASK_ON");
                SyncToggleKeyword(m, "_UseAlphaDissolve", "_USEPARTICLEALPHADISSOLVE_ON");
                SyncToggleKeyword(m, "_UseVertexDistort", "_VERTEX_DISTORT_ON");
                m.renderQueue = src.renderQueue > 0 ? Mathf.Max(3000, src.renderQueue) : 3000;
            }
            else
            {
                m = existing != null ? existing : new Material(Shader.Find("Standard"));
                m.shader = Shader.Find("Standard");
                var tex = SavedTexture(src, "_BaseColorMap", "_BaseMap", "_MainTex", "_Albedo", "_MainTexture");
                var normal = SavedTexture(src, "_NormalMap", "_BumpMap");
                m.SetTexture("_MainTex", tex);
                m.SetColor("_Color", SavedColor(src, Color.white, "_BaseColor", "_Color"));
                if (normal != null)
                {
                    m.SetTexture("_BumpMap", normal);
                    m.EnableKeyword("_NORMALMAP");
                }
                m.SetFloat("_Glossiness", 0.15f);
                m.SetFloat("_Metallic", 0f);
            }
            m.name = Sanitize(src.name);
            if (existing == null) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
            s_converted[src] = m;
            return m;
        }

        private static void SyncToggleKeyword(Material m, string prop, string keyword)
        {
            if (!m.HasProperty(prop)) return;
            if (m.GetFloat(prop) > 0.5f) m.EnableKeyword(keyword);
            else m.DisableKeyword(keyword);
        }

        /// <summary>셰이더가 없어도(InternalErrorShader) 재질 파일에 저장된 텍스처를 읽는다</summary>
        private static Texture SavedTexture(Material src, params string[] names)
        {
            var so = new SerializedObject(src);
            var envs = so.FindProperty("m_SavedProperties.m_TexEnvs");
            if (envs == null) return null;
            foreach (var want in names)
            {
                for (int i = 0; i < envs.arraySize; i++)
                {
                    var e = envs.GetArrayElementAtIndex(i);
                    if (e.FindPropertyRelative("first").stringValue != want) continue;
                    var tex = e.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
                    if (tex != null) return tex;
                }
            }
            return null;
        }

        private static Color SavedColor(Material src, Color fallback, params string[] names)
        {
            var so = new SerializedObject(src);
            var cols = so.FindProperty("m_SavedProperties.m_Colors");
            if (cols == null) return fallback;
            foreach (var want in names)
            {
                for (int i = 0; i < cols.arraySize; i++)
                {
                    var e = cols.GetArrayElementAtIndex(i);
                    if (e.FindPropertyRelative("first").stringValue == want) return e.FindPropertyRelative("second").colorValue;
                }
            }
            return fallback;
        }

        /// <summary>스킬 전용 재질 (변형 · 색 변경) — Assets/Art/Materials/Skills/{name}.mat 로 저장</summary>
        private static Material MatVariant(Material baseMat, string name, Action<Material> edit = null)
        {
            if (baseMat == null) return null;
            string path = $"{MatOut}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(baseMat) { name = name };
                edit?.Invoke(m);
                AssetDatabase.CreateAsset(m, path);
            }
            else
            {
                m.shader = baseMat.shader;
                m.CopyPropertiesFromMaterial(baseMat);
                m.shaderKeywords = baseMat.shaderKeywords;
                edit?.Invoke(m);
                EditorUtility.SetDirty(m);
            }
            return m;
        }

        /// <summary>텍스처 하나로 만드는 파티클 재질 (Spellbound/FxAdditiveFlow 또는 FxAlphaFlow)</summary>
        private static Material TexMat(string name, string texturePath, bool additive, Color tint, float brightness = 1f)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
            if (tex == null) Debug.LogWarning("[Skill Builder] 텍스처 없음: " + texturePath);
            var shader = Shader.Find(additive ? "Spellbound/FxAdditiveFlow" : "Spellbound/FxAlphaFlow");
            string path = $"{MatOut}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = m == null;
            if (create) m = new Material(shader);
            m.shader = shader;
            m.name = name;
            m.SetTexture("_MainTex", tex);
            m.SetColor("_TintColor", tint);
            m.SetFloat("_Brightness", brightness);
            m.renderQueue = 3000;
            if (create) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
            return m;
        }

        private static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace('[', '_').Replace(']', '_');
        }
    }
}
