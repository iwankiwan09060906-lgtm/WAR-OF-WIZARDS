// §6.5 왼손 이동 — 손가락을 모두 편 채(손바닥 펴기) 좌/우로 휘두름
//   판정: 손바닥 펴기 + 일정 거리 + 일정 속도 동시 만족, 연타 방지 재입력 제한
//   방향은 머리 기준 수평 오른쪽 축으로 잰다 (플레이어 자신의 시점 = 요청 규칙과 동일)
//   샘플 링버퍼는 고정 크기 (할당 0)

using UnityEngine;

namespace SpellboundVR.Input
{
    public sealed class MoveGestureDetector
    {
        private const int Capacity = 64;

        public float WindowSeconds = 0.25f;
        public float MinDistance = 0.14f;
        public float MinPeakSpeed = 0.9f;
        public float CooldownSeconds = 0.5f;

        private readonly float[] _times = new float[Capacity];
        private readonly float[] _lateral = new float[Capacity];
        private int _head;
        private int _count;
        private float _lastFireTime = -999f;

        /// <returns>-1 왼쪽, +1 오른쪽, 0 없음</returns>
        public sbyte Update(bool palmOpen, Vector3 handPosition, Vector3 headRight, float time)
        {
            if (!palmOpen)
            {
                _count = 0;
                return 0;
            }

            headRight.y = 0f;
            if (headRight.sqrMagnitude < 1e-6f) return 0;
            headRight.Normalize();
            float lateral = Vector3.Dot(handPosition, headRight);

            _times[_head] = time;
            _lateral[_head] = lateral;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;

            if (time - _lastFireTime < CooldownSeconds) return 0;

            // 창 안의 가장 오래된 샘플과 비교 + 최고 순간 속도
            int newest = (_head - 1 + Capacity) % Capacity;
            float oldestLateral = lateral;
            float peakSpeed = 0f;
            int idx = newest;
            for (int i = 0; i < _count - 1; i++)
            {
                int prev = (idx - 1 + Capacity) % Capacity;
                if (time - _times[prev] > WindowSeconds) break;
                float dt = _times[idx] - _times[prev];
                if (dt > 1e-4f)
                {
                    float speed = Mathf.Abs(_lateral[idx] - _lateral[prev]) / dt;
                    if (speed > peakSpeed) peakSpeed = speed;
                }
                oldestLateral = _lateral[prev];
                idx = prev;
            }

            float displacement = lateral - oldestLateral;
            if (Mathf.Abs(displacement) >= MinDistance && peakSpeed >= MinPeakSpeed)
            {
                _lastFireTime = time;
                _count = 0;
                return (sbyte)(displacement > 0f ? 1 : -1);
            }
            return 0;
        }

        public void Reset()
        {
            _count = 0;
        }
    }
}
