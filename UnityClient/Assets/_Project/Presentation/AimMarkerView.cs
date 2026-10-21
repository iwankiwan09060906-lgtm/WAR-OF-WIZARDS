// §6.3 조준 포인터 표시 (본인에게만 보임)
//   · 열 하이라이트 + 지면 마커(범위형은 스킬 반경) + 1초 진행 링
//   · 띠 · 타원 범위(S02 독 장판 = 3열 전체 띠, S04 화살 세례 = 레인 폭 × 깊이)는 SetShape로 반폭을 지정

using SpellboundVR.Arena;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    public sealed class AimMarkerView : MonoBehaviour
    {
        private Transform _marker;
        private Transform _progress;
        private Transform _lane;
        private Renderer _markerRenderer;
        private Renderer _laneRenderer;
        private Renderer _progressRenderer;
        private float _radius;
        private Vector2 _halfExtents;
        private bool _spanAllLanes;

        private void Awake()
        {
            Build();
        }

        private void Build()
        {
            if (_marker != null) return;
            _marker = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "AimMarker", FallbackVisuals.Transparent(Color.white)).transform;
            _marker.SetParent(transform, false);
            _progress = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "AimProgress", FallbackVisuals.Transparent(Color.white)).transform;
            _progress.SetParent(transform, false);
            _lane = FallbackVisuals.Primitive(PrimitiveType.Cube, "AimLane", FallbackVisuals.Transparent(Color.white)).transform;
            _lane.SetParent(transform, false);
            _markerRenderer = _marker.GetComponent<Renderer>();
            _progressRenderer = _progress.GetComponent<Renderer>();
            _laneRenderer = _lane.GetComponent<Renderer>();
            Hide();
        }

        public void Show(Color color, float radius)
        {
            Build();
            _radius = radius > 0f ? radius : 0.6f;
            _halfExtents = Vector2.zero;
            _spanAllLanes = false;
            _markerRenderer.sharedMaterial = FallbackVisuals.Transparent(new Color(color.r, color.g, color.b, 0.35f));
            _progressRenderer.sharedMaterial = FallbackVisuals.Transparent(new Color(color.r, color.g, color.b, 0.8f));
            _laneRenderer.sharedMaterial = FallbackVisuals.Transparent(new Color(color.r, color.g, color.b, 0.12f));
            gameObject.SetActive(true);
        }

        /// <summary>원 대신 타원 범위 (반폭 x = 레인 가로, y = 깊이). spanAllLanes면 가로 중심을 전장 가운데로</summary>
        public void SetShape(Vector2 halfExtents, bool spanAllLanes)
        {
            _halfExtents = halfExtents;
            _spanAllLanes = spanAllLanes;
        }

        public void UpdateMarker(Vector3 position, ArenaGeometry arena, int worldColumn, float progress)
        {
            if (_spanAllLanes)
            {
                Vector2 uv = arena.ToArena(position);
                position = arena.ToWorld(0f, uv.y, position.y - arena.GroundY);
            }
            _marker.position = position;
            _marker.rotation = Quaternion.LookRotation(arena.Forward, Vector3.up);
            _marker.localScale = _halfExtents.x > 0f
                ? new Vector3(_halfExtents.x * 2f, 0.01f, _halfExtents.y * 2f)
                : new Vector3(_radius * 2f, 0.01f, _radius * 2f);

            _progress.position = position + Vector3.up * 0.02f;
            float p = Mathf.Clamp01(progress) * _radius * 2f;
            _progress.localScale = new Vector3(p, 0.012f, p);

            Vector3 laneCenter = arena.ToWorld(arena.ColumnU(worldColumn), arena.FieldLength * 0.5f, 0.02f);
            _lane.position = laneCenter;
            _lane.rotation = Quaternion.LookRotation(arena.Forward, Vector3.up);
            _lane.localScale = new Vector3(arena.ColumnSpacing * 0.95f, 0.01f, arena.FieldLength);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
