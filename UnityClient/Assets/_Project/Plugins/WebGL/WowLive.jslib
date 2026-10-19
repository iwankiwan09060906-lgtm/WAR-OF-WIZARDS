// War of Wizards - 서버 실시간 이벤트(SSE) 를 WebGL 에서 받기 (Network/LiveEventStream.cs 에서 호출)
// 브라우저 EventSource 가 끊기면 자동으로 다시 연결한다.
mergeInto(LibraryManager.library, {

  WowLive_Connect: function (urlPtr, objectPtr) {
    var url = UTF8ToString(urlPtr);
    var target = UTF8ToString(objectPtr);
    if (window.__wowLive) { window.__wowLive.close(); }
    var es = new EventSource(url);
    es.onmessage = function (e) { SendMessage(target, "OnWebGLLiveEvent", e.data); };
    es.onerror = function () { SendMessage(target, "OnWebGLLiveError", ""); };
    window.__wowLive = es;
  },

  WowLive_Close: function () {
    if (window.__wowLive) { window.__wowLive.close(); window.__wowLive = null; }
  }
});
