// IPlayerInput(계약, §6.6) 확장 — ㅊㄱㅇ 내부 인터페이스 (계약 변경 아님)
// 시전 흐름 · 조준 · 표시에 필요한 추가 정보를 입력 구현체(HandTrackingInput / KeyboardInput)가 제공한다.

using System;
using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Input
{
    public interface ISpellInputSource : IPlayerInput
    {
        /// <summary>§6.3 조준 레이 (검지 레이 / 마우스 레이)</summary>
        bool TryGetAimRay(out Ray ray);

        /// <summary>오른손(또는 대체 커서) 위치 — 룬 표시 기준점</summary>
        bool TryGetRightHandPosition(out Vector3 position);

        /// <summary>§6.6 키보드 대체 입력: 룬 드로잉을 건너뛰고 슬롯 번호로 바로 시전</summary>
        event Action<int> OnDirectSlotCast;

        /// <summary>그리는 중인 궤적 (월드 좌표, MagicLineRenderer 표시용)</summary>
        bool IsDrawing { get; }

        Vector3[] DrawPoints { get; }

        int DrawPointCount { get; }

        /// <summary>이 입력 장치에서 룬으로 인정할 최소 크기 (손: m, 마우스: px)</summary>
        float MinStrokeSize { get; }
    }
}
