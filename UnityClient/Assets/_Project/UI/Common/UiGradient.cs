// 글자 · 판 그라데이션 — 프로토타입 제목의 linear-gradient(135deg, …) 글자색과 VICTORY 반짝임(shimmer-text)을 UGUI로.
// 꼭짓점 색을 왼쪽 위 → 오른쪽 아래 위치에 따라 2~3색 사이로 섞는다. Speed > 0 이면 색 띠가 계속 흘러간다.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    [RequireComponent(typeof(Graphic))]
    public sealed class UiGradient : BaseMeshEffect
    {
        public Color A = Color.white, B = Color.white, C = Color.white;
        /// <summary>색 띠가 흐르는 속도 (0 = 고정)</summary>
        public float Speed;
        /// <summary>true = 아래(A) → 위(C) 세로 그라데이션 (빛줄기 등)</summary>
        public bool Vertical;

        private float _shift;
        private static readonly List<UIVertex> s_verts = new List<UIVertex>();

        public static UiGradient Apply(Graphic g, Color a, Color b, Color c, float speed = 0f, bool vertical = false)
        {
            if (!g.TryGetComponent(out UiGradient fx)) fx = g.gameObject.AddComponent<UiGradient>();
            fx.A = a;
            fx.B = b;
            fx.C = c;
            fx.Speed = speed;
            fx.Vertical = vertical;
            g.color = Color.white; // 꼭짓점 색에 곱해지므로 흰색으로
            g.SetVerticesDirty();
            return fx;
        }

        private void Update()
        {
            if (Speed <= 0f || graphic == null) return;
            _shift = Mathf.Repeat(_shift + Speed * Time.unscaledDeltaTime, 2f);
            graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            s_verts.Clear();
            vh.GetUIVertexStream(s_verts);
            var r = graphic.rectTransform.rect;
            float w = Mathf.Max(1f, r.width), h = Mathf.Max(1f, r.height);
            for (int i = 0; i < s_verts.Count; i++)
            {
                var v = s_verts[i];
                // 135도: 왼쪽 위 0 → 오른쪽 아래 1 (가로 비중을 크게)
                float t = Vertical ? (v.position.y - r.yMin) / h
                                   : (v.position.x - r.xMin) / w * 0.8f + (r.yMax - v.position.y) / h * 0.2f;
                if (Speed > 0f) t = Mathf.PingPong(t + _shift, 1f);
                Color c = t < 0.5f ? Color.Lerp(A, B, t * 2f) : Color.Lerp(B, C, (t - 0.5f) * 2f);
                v.color = c * (Color)v.color;
                s_verts[i] = v;
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(s_verts);
        }
    }
}
