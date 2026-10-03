// §17 Object Pooling — 전투 중 Instantiate / Destroy 반복 금지
//   · "비활성 = 빈 슬롯" 규칙: VFX 프리팹이 파티클 Stop Action(Disable)로 스스로 꺼지면 자동으로 풀에 돌아온다.
//   · 풀이 모자라면 한 개씩 늘리고 경고를 남긴다 (Prewarm 수치 조정 신호).

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpellboundVR.Pooling
{
    public sealed class ObjectPool
    {
        private readonly List<GameObject> _items;
        private readonly Func<GameObject> _factory;
        private readonly Transform _root;
        private readonly string _label;
        private int _searchStart;

        public ObjectPool(GameObject prefab, Transform root, int prewarm)
            : this(() => UnityEngine.Object.Instantiate(prefab), root, prewarm, prefab != null ? prefab.name : "null")
        {
        }

        public ObjectPool(Func<GameObject> factory, Transform root, int prewarm, string label)
        {
            _factory = factory;
            _root = root;
            _label = label;
            _items = new List<GameObject>(Mathf.Max(1, prewarm));
            for (int i = 0; i < prewarm; i++) _items.Add(Create());
        }

        public int Count => _items.Count;

        public GameObject Get(Vector3 position, Quaternion rotation)
        {
            int n = _items.Count;
            for (int k = 0; k < n; k++)
            {
                int i = (_searchStart + k) % n;
                var go = _items[i];
                if (go == null)
                {
                    go = Create();
                    _items[i] = go;
                }
                if (go.activeSelf) continue;
                _searchStart = (i + 1) % n;
                Activate(go, position, rotation);
                return go;
            }

            var extra = Create();
            _items.Add(extra);
            if (_items.Count % 8 == 0)
                Debug.LogWarning($"[ObjectPool] '{_label}' 풀 확장 → {_items.Count}개 (Prewarm 상향 권장)");
            Activate(extra, position, rotation);
            return extra;
        }

        public void Release(GameObject go)
        {
            if (go != null) go.SetActive(false);
        }

        public void ReleaseAll()
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i] != null) _items[i].SetActive(false);
        }

        private GameObject Create()
        {
            var go = _factory();
            go.name = _label + " (Pooled)";
            go.SetActive(false);
            if (_root != null) go.transform.SetParent(_root, false);
            return go;
        }

        private static void Activate(GameObject go, Vector3 position, Quaternion rotation)
        {
            go.transform.SetPositionAndRotation(position, rotation);
            go.SetActive(true);
        }
    }
}
