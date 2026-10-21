// C07 우편함 · 공지 (§23.1) — 공지 · 친구 요청 알림 · 친선전 초대 · 시스템 메시지, 읽음 · 삭제. 보상 · 아이템 없음.

using System;

namespace SpellboundVR.Backend
{
    public static class MailService
    {
        /// <summary>우편 + 공지를 최신순 한 목록으로</summary>
        public static void GetMailbox(Action<MailboxItemDto[]> onSuccess, Action<string> onError = null)
            => BackendClient.Instance.GetArray("/api/mailbox", onSuccess, onError);

        /// <summary>안 읽은 우편 수 (공지 제외)</summary>
        public static void GetUnreadCount(Action<int> onSuccess, Action<string> onError = null)
            => BackendClient.Instance.Get<UnreadCountDto>("/api/mails/unread-count", res => onSuccess?.Invoke(res.count), onError);

        /// <summary>읽음 처리 — itemId 는 MailboxItemDto.id ("m12" 우편 · "n3" 공지)</summary>
        public static void MarkRead(string itemId, Action onSuccess = null, Action<string> onError = null)
            => BackendClient.Instance.Post<MailboxReadRequestDto, MailboxReadResultDto>("/api/mailbox/read",
                new MailboxReadRequestDto { id = itemId }, _ => onSuccess?.Invoke(), onError);

        /// <summary>우편 삭제 (공지는 삭제할 수 없다)</summary>
        public static void Delete(string itemId, Action onSuccess = null, Action<string> onError = null)
        {
            if (string.IsNullOrEmpty(itemId) || itemId[0] != 'm' || !long.TryParse(itemId.Substring(1), out long mailId))
            {
                onError?.Invoke("우편만 삭제할 수 있습니다.");
                return;
            }
            BackendClient.Instance.Delete<MessageDto>($"/api/mails/{mailId}", _ => onSuccess?.Invoke(), onError);
        }

        /// <summary>친구에게 친선전 초대 우편 (친구에게만 가능)</summary>
        public static void SendInvite(long friendPlayerId, string roomCode, Action onSuccess = null, Action<string> onError = null)
            => BackendClient.Instance.Post<InviteRequestDto, MessageDto>("/api/mails/invite",
                new InviteRequestDto { friendId = friendPlayerId, roomCode = roomCode }, _ => onSuccess?.Invoke(), onError);
    }
}
