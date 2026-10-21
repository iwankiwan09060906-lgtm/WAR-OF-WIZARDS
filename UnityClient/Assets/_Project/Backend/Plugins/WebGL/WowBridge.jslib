// War of Wizards - Unity WebGL <-> 브라우저 연결 (Backend/WebGLBridge.cs 에서 호출)
mergeInto(LibraryManager.library, {

  WowBridge_GetPageOrigin: function () {
    var value = (window.location && window.location.origin) ? window.location.origin : "";
    var size = lengthBytesUTF8(value) + 1;
    var buffer = _malloc(size);
    stringToUTF8(value, buffer, size);
    return buffer;
  },

  WowBridge_GetLocalStorage: function (keyPtr) {
    var value = "";
    try { value = window.localStorage.getItem(UTF8ToString(keyPtr)) || ""; } catch (e) { }
    var size = lengthBytesUTF8(value) + 1;
    var buffer = _malloc(size);
    stringToUTF8(value, buffer, size);
    return buffer;
  },

  WowBridge_SetLocalStorage: function (keyPtr, valuePtr) {
    try { window.localStorage.setItem(UTF8ToString(keyPtr), UTF8ToString(valuePtr)); } catch (e) { }
  },

  WowBridge_RemoveLocalStorage: function (keyPtr) {
    try { window.localStorage.removeItem(UTF8ToString(keyPtr)); } catch (e) { }
  },

  // 페이지에서 받기: window.addEventListener("wow:match-end", function (e) { console.log(JSON.parse(e.detail)); });
  WowBridge_SendToPage: function (eventPtr, jsonPtr) {
    var name = UTF8ToString(eventPtr);
    var detail = UTF8ToString(jsonPtr);
    try { window.dispatchEvent(new CustomEvent("wow:" + name, { detail: detail })); } catch (e) { }
    if (window.parent && window.parent !== window) {
      try { window.parent.postMessage({ source: "wow-unity", event: name, data: detail }, window.location.origin); } catch (e) { }
    }
  }
});
