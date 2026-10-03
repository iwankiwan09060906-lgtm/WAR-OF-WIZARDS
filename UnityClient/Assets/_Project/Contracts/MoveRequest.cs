// §25.1 계약서(Contracts) — 제공자: ㅊㄱㅇ(생성) · 봇(ㅈㅈㅇ), 사용자: ㅈㅈㅇ
// §6.5 왼손 이동 / §26 권장 데이터 구조

namespace SpellboundVR.Contracts
{
    /// <summary>
    /// 왼손 좌/우 이동 요청. 클라이언트는 즉시 예측 표시하고,
    /// 서버가 확정한다. 거절되면 원래 위치로 되돌린다(§6.5).
    /// </summary>
    public struct MoveRequest
    {
        /// <summary>-1 왼쪽, +1 오른쪽</summary>
        public sbyte Direction;
    }
}
