// VFX · 모델 미리보기 렌더러 (에디터 전용) — 프리팹을 미리보기 씬에 놓고 파티클을 여러 시점으로 시뮬레이션해 PNG 한 장으로 저장한다.
//   · 기준: 회색 바닥 1m 격자 + 키 1.8m 사람 막대(왼쪽) — 크기 확인용
//   · Spellbound > Skills > Render VFX Previews : Temp/vfx_preview_request.txt 의 각 줄 "프리팹경로|출력png|시간들(쉼표)|카메라거리|카메라높이|바라볼높이"
//     를 렌더한다 (빌더가 만든 VFX_* 확인 · 에셋 팩 원본 크기 조사용)

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellboundVR.EditorTools
{
    public static class SpellboundVfxPreview
    {
        public const string RequestFile = "Temp/vfx_preview_request.txt";
        private const int Width = 420;
        private const int Height = 360;

        [MenuItem("Spellbound/Skills/Render VFX Previews (Temp request)", priority = 214)]
        public static void RenderRequested()
        {
            if (!File.Exists(RequestFile))
            {
                Debug.LogWarning("[VFX Preview] 요청 파일 없음: " + RequestFile);
                return;
            }
            int n = 0;
            foreach (var raw in File.ReadAllLines(RequestFile))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var p = line.Split('|');
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p[0]);
                if (prefab == null)
                {
                    Debug.LogWarning("[VFX Preview] 프리팹 없음: " + p[0]);
                    continue;
                }
                var times = new List<float>();
                foreach (var t in (p.Length > 2 ? p[2] : "0.3,1").Split(',')) times.Add(float.Parse(t, System.Globalization.CultureInfo.InvariantCulture));
                float dist = p.Length > 3 ? float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture) : 8f;
                float camY = p.Length > 4 ? float.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture) : 3f;
                float lookY = p.Length > 5 ? float.Parse(p[5], System.Globalization.CultureInfo.InvariantCulture) : 1f;
                Render(prefab, p[1], times.ToArray(), dist, camY, lookY);
                n++;
            }
            Debug.Log($"[VFX Preview] {n}개 렌더 완료");
        }

        public static void Render(GameObject prefab, string outPath, float[] times, float distance, float camHeight, float lookHeight)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var lightGo = new GameObject("Light");
                SceneManager.MoveGameObjectToScene(lightGo, scene);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.localScale = new Vector3(6f, 1f, 6f);
                var gm = new Material(Shader.Find("Standard")) { color = new Color(0.33f, 0.35f, 0.33f) };
                ground.GetComponent<Renderer>().sharedMaterial = gm;
                for (int i = -10; i <= 10; i += 2)
                {
                    AddLine(scene, new Vector3(i, 0.01f, 0f), new Vector3(0.03f, 0.01f, 20f));
                    AddLine(scene, new Vector3(0f, 0.01f, i), new Vector3(20f, 0.01f, 0.03f));
                }
                var human = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                SceneManager.MoveGameObjectToScene(human, scene);
                human.transform.position = new Vector3(-2.5f, 0.9f, 0f);
                human.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
                human.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.9f, 0.8f, 0.6f) };

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                inst.transform.position = Vector3.zero;

                var camGo = new GameObject("Cam");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
                cam.fieldOfView = 55f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 300f;
                cam.allowHDR = false;
                camGo.transform.position = new Vector3(distance * 0.55f, camHeight, -distance);
                camGo.transform.LookAt(new Vector3(0f, lookHeight, 0f));

                var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                var sheet = new Texture2D(Width * times.Length, Height, TextureFormat.RGB24, false);
                var roots = RootSystems(inst);
                var anims = inst.GetComponentsInChildren<Animator>();

                for (int k = 0; k < times.Length; k++)
                {
                    foreach (var ps in roots) ps.Simulate(times[k], true, true, true);
                    foreach (var a in anims)
                    {
                        if (a.runtimeAnimatorController == null) continue;
                        a.Rebind();
                        a.Update(times[k]);
                    }
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    tex.Apply();
                    sheet.SetPixels(k * Width, 0, Width, Height, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                    RenderTexture.active = null;
                }
                sheet.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllBytes(outPath, sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void AddLine(Scene scene, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.45f, 0.47f, 0.45f) };
        }

        private static List<ParticleSystem> RootSystems(GameObject go)
        {
            var list = new List<ParticleSystem>();
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var parent = ps.transform.parent;
                if (parent != null && parent.GetComponentInParent<ParticleSystem>(true) != null && parent.IsChildOf(go.transform)) continue;
                list.Add(ps);
            }
            return list;
        }
    }
}
