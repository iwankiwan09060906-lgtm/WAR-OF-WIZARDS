// §9 $P+ Pipeline 앞단 — 오른손 궤적 샘플링 · 노이즈 제거
//   · 핀치 시작 순간 머리 기준 그리기 평면(오른쪽 · 위 축)을 고정한다.
//   · 이전 샘플에서 MinSampleDistance 이상 움직였을 때만 기록 (떨림 제거)
//   · 지수 평활(Smoothing)로 고주파 노이즈 감소
//   · 버퍼는 미리 할당 (매 프레임 할당 0, §33). 획 종료 시에만 결과 배열을 만든다.

using UnityEngine;

namespace SpellboundVR.Gesture
{
    public sealed class GestureRecorder
    {
        public const int MaxSamples = 512;

        private readonly Vector3[] _world = new Vector3[MaxSamples];
        private int _count;
        private Vector3 _origin;
        private Vector3 _right;
        private Vector3 _up;
        private Vector3 _smoothed;
        private bool _recording;

        public float MinSampleDistance { get; set; } = 0.004f;

        /// <summary>0 = 평활 없음, 0.9 = 강한 평활</summary>
        public float Smoothing { get; set; } = 0.35f;

        public bool IsRecording => _recording;

        public int Count => _count;

        /// <summary>표시용 월드 좌표 (MagicLineRenderer)</summary>
        public Vector3[] WorldPoints => _world;

        public Vector2[] LastStroke { get; private set; }

        public void Begin(Vector3 firstPoint, Vector3 planeRight, Vector3 planeUp)
        {
            _recording = true;
            _count = 0;
            _origin = firstPoint;
            _right = planeRight.sqrMagnitude > 1e-6f ? planeRight.normalized : Vector3.right;
            _up = planeUp.sqrMagnitude > 1e-6f ? planeUp.normalized : Vector3.up;
            _smoothed = firstPoint;
            _world[_count++] = firstPoint;
        }

        public void AddPoint(Vector3 worldPoint)
        {
            if (!_recording || _count >= MaxSamples) return;
            _smoothed = Vector3.Lerp(worldPoint, _smoothed, Smoothing);
            if ((_smoothed - _world[_count - 1]).sqrMagnitude < MinSampleDistance * MinSampleDistance) return;
            _world[_count++] = _smoothed;
        }

        /// <summary>획 종료 → 그리기 평면에 투영한 2D 궤적 (x = 오른쪽, y = 위)</summary>
        public Vector2[] End()
        {
            _recording = false;
            var result = new Vector2[_count];
            for (int i = 0; i < _count; i++)
            {
                Vector3 d = _world[i] - _origin;
                result[i] = new Vector2(Vector3.Dot(d, _right), Vector3.Dot(d, _up));
            }
            LastStroke = result;
            return result;
        }

        public void Cancel()
        {
            _recording = false;
            _count = 0;
        }
    }
}
