// §25.1 계약서(Contracts) — 제공자/사용자: ㅊㄱㅇ 내부
// §6.6 입력 추상화(키보드 대체 입력)
//
// 이 인터페이스 뒤에 실제 손 입력을 둔다.
// 구현체: HandTrackingInput(Quest) / KeyboardInput(에디터 개발·테스트용, §6.6)
// 목적: 헤드셋 없이 개발, 에디터를 두 번째 사람 플레이어로 쓴 PvP·친선전 테스트(§6.6).
//
// 아래 멤버는 §6.2(오른손 시전 상태 머신)·§6.5(왼손 이동)를 기반으로 한 초안이며,
// 계약서 확정 절차(§25.1: 4인 전원 동의 + ㅈㅈㅇ 반영)를 거쳐야 한다.
// KeyboardInput은 룬 드로잉을 건너뛰고 슬롯 번호로 바로 시전한다(§6.6) — 구현체 책임.

using System;
using UnityEngine;

namespace SpellboundVR.Contracts
{
    public interface IPlayerInput
    {
        /// <summary>오른손 엄지+검지 핀치 시작 → Drawing 진입(§6.2)</summary>
        event Action OnPinchStarted;

        /// <summary>핀치 해제 + 그려진 궤적 → Recognizing($P+) 요청(§6.2)</summary>
        event Action<Vector2[]> OnPinchEnded;

        /// <summary>오른손 주먹 → Armed 진입 / 조준 불필요 스킬은 즉시 시전(§6.2, §6.3)</summary>
        event Action OnFistDetected;

        /// <summary>오른손 검지로 가리킴 → Aiming 진입, 1초 카운트 시작(§6.2)</summary>
        event Action OnPointDetected;

        /// <summary>오른손 손바닥 펴기 → RuneReady/Armed/Aiming 취소, 쿨타임 미소모(§6.4)</summary>
        event Action OnPalmOpenDetected;

        /// <summary>왼손 손가락 펴기 + 일정 거리 + 일정 속도 동시 만족 시 이동(§6.5)</summary>
        event Action<sbyte> OnMoveGesture;

        bool IsRightHandTracked { get; }
        bool IsLeftHandTracked { get; }
    }
}
