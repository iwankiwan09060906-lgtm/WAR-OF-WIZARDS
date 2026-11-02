// §6.3 조준 포인터 표시 (본인에게만 보임)
//   · 열 하이라이트 + 지면 마커(범위형은 스킬 반경) + 시전 진행 링
//   · 원형 마커 · 진행 링은 마법진 이펙트(VFX_Aim_Circle), 열 · 범위 하이라이트는 바닥에 깔리는 반투명 판
//   · 직사각형 범위는 SetShape로 반폭을 지정 — S02 독 장판 · H05 분노 = 3열 전체 띠, S04 화살 세례 · S06 번개 = 레인 폭 × 깊이

using SpellboundVR.Arena;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    public sealed class AimMarkerView : MonoBehaviour
    {
        private const string CirclePrefab = "VFX_Aim_Circle";
        /// <summary>VFX_Aim_Circle 마법진 지름 (배율 1, m)</summary>
        private const float CircleDiameter = 10f;

        private Transform _marker;
        private Transform _rect;
        private Transform _progress;
        private Transform _lane;
        private Renderer _markerRenderer;
        private Renderer _rectRenderer;
        private Renderer _laneRenderer;
        private Renderer _progressRenderer;
        private GameObject _circle;
        private GameObject _progressCircle;
        private float _radius;
        private Vector2 _halfExtents;
        private bool _spanAllLanes;

        private void Awake()
        {
            Build();
        }

        /// <summary>마법진 이펙트로 바꾼다 (카탈로그에 없으면 대체 원판을 그대로 쓴다)</summary>
        public void Initialize(VFXCatalog catalog)
        {
            Build();
            if (catalog == null || !catalog.TryGet(CirclePrefab, out var prefab)) return;
            _circle = Instantiate(prefab, transform);
            _progressCircle = Instantiate(prefab, transform);
            _markerRenderer.enabled = false;
            _progressRenderer.enabled = false;
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
            _rect = FallbackVisuals.Primitive(PrimitiveType.Cube, "AimRect", FallbackVisuals.Transparent(Color.white)).transform;
            _rect.SetParent(transform, false);
            _markerRenderer = _marker.GetComponent<Renderer>();
            _rectRenderer = _rect.GetComponent<Renderer>();
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
            _rectRenderer.sharedMaterial = _markerRenderer.sharedMaterial;
            _rect.gameObject.SetActive(false);
            _marker.gameObject.SetActive(true);
            _progressRenderer.sharedMaterial = FallbackVisuals.Transparent(new Color(color.r, color.g, color.b, 0.8f));
            _laneRenderer.sharedMaterial = FallbackVisuals.Transparent(new Color(color.r, color.g, color.b, 0.12f));
            VfxTint.Apply(_circle, color);
            VfxTint.Apply(_progressCircle, color);
            gameObject.SetActive(true);
        }

        /// <summary>원 대신 직사각형 범위 (반폭 x = 가로, y = 깊이, 0 = 원). spanAllLanes면 가로 중심을 전장 가운데로</summary>
        public void SetShape(Vector2 halfExtents, bool spanAllLanes)
        {
            _halfExtents = halfExtents;
            _spanAllLanes = spanAllLanes;
            bool rect = halfExtents.x > 0f;
            _rect.gameObject.SetActive(rect);
            _marker.gameObject.SetActive(!rect);
            if (_circle != null) _circle.SetActive(!rect);
        }

        public void UpdateMarker(Vector3 position, ArenaGeometry arena, Contracts.Team team, int worldColumn, float progress)
        {
            var frame = arena.GetLane(team, worldColumn);
            if (_spanAllLanes && arena.IsDuel)
            {
                Vector2 uv = arena.ToArena(position);
                position = arena.ToWorld(0f, uv.y, position.y - arena.GroundY);
            }
            var facing = Quaternion.LookRotation(arena.ToWorldDirection(frame.Dir), Vector3.up);
            if (_halfExtents.x > 0f)
            {
                _rect.SetPositionAndRotation(position, facing);
                _rect.localScale = new Vector3(_halfExtents.x * 2f, 0.01f, _halfExtents.y * 2f);
            }
            else
            {
                _marker.SetPositionAndRotation(position, facing);
                _marker.localScale = new Vector3(_radius * 2f, 0.01f, _radius * 2f);
            }

            _progress.position = position + Vector3.up * 0.02f;
            float p = Mathf.Clamp01(progress) * _radius * 2f;
            _progress.localScale = new Vector3(p, 0.012f, p);
            if (_circle != null)
            {
                _circle.transform.position = position;
                _circle.transform.localScale = Vector3.one * (_radius * 2f / CircleDiameter);
                _progressCircle.transform.position = position + Vector3.up * 0.02f;
                _progressCircle.transform.localScale = Vector3.one * (p / CircleDiameter);
            }

            _lane.position = arena.ToWorld(frame.Point(frame.Length * 0.5f), 0.02f);
            _lane.rotation = facing;
            _lane.localScale = new Vector3(arena.ColumnSpacing * 0.95f, 0.01f, frame.Length);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
