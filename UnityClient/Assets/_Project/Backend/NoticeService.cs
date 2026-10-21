// 공지 (§23.1 C07) — 읽기 공개. 관리자 콘솔(http://서버/#/admin)에서 등록한다.
// 우편함 화면은 MailService.GetMailbox 가 공지까지 읽음 상태와 함께 돌려주므로, 공지만 따로 필요할 때 쓴다.

using System;

namespace SpellboundVR.Backend
{
    public static class NoticeService
    {
        public static void GetAll(Action<NoticeDto[]> onSuccess, Action<string> onError = null)
            => BackendClient.Instance.GetArray("/api/notices", onSuccess, onError);
    }
}
