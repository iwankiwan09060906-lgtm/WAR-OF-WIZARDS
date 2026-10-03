// §6.3 조준 포인터 표시 (본인에게만 보임)
//   · 열 하이라이트 + 지면 마커(범위형은 스킬 반경) + 1초 진행 링

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
            _markerRenderer.sharedMaterial = FallbackVisuals.Transparent(new Color(color.r, color.g, color.b, 0.35f));
            _progressRenderer.sharedMaterial = FallbackVisuals.Transparent(new Color(color.r, color.g, color.b, 0.8f));
            _laneRenderer.sharedMaterial = FallbackVisuals.Transparent(new Color(color.r, color.g, color.b, 0.12f));
            gameObject.SetActive(true);
        }

        public void UpdateMarker(Vector3 position, ArenaGeometry arena, int worldColumn, float progress)
        {
            _marker.position = position;
            _marker.rotation = Quaternion.identity;
            _marker.localScale = new Vector3(_radius * 2f, 0.01f, _radius * 2f);

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
