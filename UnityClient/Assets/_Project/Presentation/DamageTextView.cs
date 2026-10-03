// 데미지 텍스트 (§17 풀링 대상) — 피해량 · MISS · DODGE · BLOCK 표시
// NetworkGameEventPublisher.OnHitFeedback(클라이언트 확장 이벤트)만 구독한다.

using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Pooling;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    public sealed class DamageTextView : MonoBehaviour
    {
        public float lifetime = 0.9f;
        public float riseSpeed = 0.8f;
        public float characterSize = 0.012f;

        private struct Active
        {
            public TextMesh Text;
            public float Start;
            public Vector3 Origin;
            public Color Color;
        }

        private ClientSession _session;
        private Transform _head;
        private ObjectPool _pool;
        private readonly System.Collections.Generic.List<Active> _active = new System.Collections.Generic.List<Active>(32);

        public void Initialize(ClientSession session, Transform head)
        {
            _session = session;
            _head = head;
            var root = new GameObject("[DamageText Pool]").transform;
            root.SetParent(transform, false);
            _pool = new ObjectPool(() => FallbackVisuals.Text(null, "", characterSize, Color.white).gameObject, root, 16, "DamageText");
            _session.Events.OnHitFeedback += HandleHit;
        }

        private void OnDestroy()
        {
            if (_session != null) _session.Events.OnHitFeedback -= HandleHit;
        }

        private void HandleHit(int targetId, Vector3 position, int amount, HitResultKind kind)
        {
            string label;
            Color color;
            switch (kind)
            {
                case HitResultKind.Miss: label = "MISS"; color = Color.gray; break;
                case HitResultKind.Dodge: label = "DODGE"; color = new Color(0.5f, 1f, 1f); break;
                case HitResultKind.Immune: label = "IMMUNE"; color = Color.gray; break;
                case HitResultKind.Absorbed: label = "BLOCK"; color = new Color(0.4f, 0.75f, 1f); break;
                case HitResultKind.Heal: label = "+" + amount; color = Color.green; break;
                default:
                    if (amount <= 0) return;
                    label = "-" + amount;
                    color = TeamUtil.IsPlayerId(targetId) ? new Color(1f, 0.85f, 0.2f) : Color.white;
                    break;
            }

            var go = _pool.Get(position, Quaternion.identity);
            var tm = go.GetComponent<TextMesh>();
            tm.text = label;
            tm.color = color;
            float scale = TeamUtil.IsPlayerId(targetId) ? 2f : 1f;
            go.transform.localScale = Vector3.one * scale;
            _active.Add(new Active { Text = tm, Start = Time.time, Origin = position, Color = color });
        }

        private void LateUpdate()
        {
            float now = Time.time;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var a = _active[i];
                float t = (now - a.Start) / lifetime;
                if (t >= 1f || a.Text == null)
                {
                    if (a.Text != null) a.Text.gameObject.SetActive(false);
                    _active.RemoveAt(i);
                    continue;
                }
                var tr = a.Text.transform;
                tr.position = a.Origin + Vector3.up * (riseSpeed * t);
                if (_head != null)
                {
                    Vector3 d = tr.position - _head.position;
                    if (d.sqrMagnitude > 1e-6f) tr.rotation = Quaternion.LookRotation(d, Vector3.up);
                }
                var c = a.Color;
                c.a = 1f - t * t;
                a.Text.color = c;
            }
        }
    }
}
