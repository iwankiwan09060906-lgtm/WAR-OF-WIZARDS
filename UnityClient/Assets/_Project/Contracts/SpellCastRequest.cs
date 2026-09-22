// §25.1 계약서(Contracts) — 제공자: ㅊㄱㅇ(생성) · 봇(ㅈㅈㅇ), 사용자: ㅈㅈㅇ
// §7 전투 흐름 / §26 권장 데이터 구조

namespace SpellboundVR.Contracts
{
    /// <summary>
    /// VR 클라이언트(ㅊㄱㅇ) 또는 봇(ㅈㅈㅇ)이 서버로 보내는 시전 요청.
    /// 봇도 사람과 똑같은 요청 경로를 사용한다(§20.1).
    /// 클라이언트는 Origin / Direction을 보내지 않는다 — 서버가 계산한다(§26).
    /// </summary>
    public struct SpellCastRequest
    {
        /// <summary>S01~S13 → 1~13, H01~H06 → 101~106</summary>
        public int SpellId;

        /// <summary>0 ~ 7, 8슬롯 고정 배치(§13.2) 중 어느 칸에서 나왔는지</summary>
        public int SlotIndex;

        /// <summary>열 지정 / 범위 지정 / 소환 라인 / 설치 구역 조준 결과 (§6.3)</summary>
        public Column TargetColumn;

        /// <summary>0 ~ 1 정규화 깊이. Area(범위 지정) 조준 전용, 그 외는 무시(§5, §6.3)</summary>
        public float TargetDepth;
    }
}
