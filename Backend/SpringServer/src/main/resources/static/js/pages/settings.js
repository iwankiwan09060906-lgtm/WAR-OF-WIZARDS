// 캔버스 4 · 환경설정
// HTML 의 data-setting="키" 를 보고 자동으로 값을 채우고 저장한다. (슬라이더 = 숫자, 토글 = true/false)
WOW.page({ auth: true }, function () {
  WOW.decorate();

  function render(s) {
    WOW.$$('[data-setting]').forEach(function (el) {
      const key = el.getAttribute('data-setting');
      if (el.classList.contains('toggle')) el.setAttribute('aria-checked', String(!!s[key]));
      else el.value = s[key];
    });
    WOW.$$('[data-value]').forEach(function (el) {
      el.textContent = s[el.getAttribute('data-value')] + (el.getAttribute('data-unit') || '');
    });
  }

  WOW.$$('[data-setting]').forEach(function (el) {
    const key = el.getAttribute('data-setting');
    if (el.classList.contains('toggle')) {
      el.onclick = function () {
        const patch = {};
        patch[key] = el.getAttribute('aria-checked') !== 'true';
        render(WOW.settings.update(patch));
      };
    } else {
      el.oninput = function () {
        const patch = {};
        patch[key] = Number(el.value);
        render(WOW.settings.update(patch));
      };
    }
  });

  WOW.$('#btn-reset').onclick = function () {
    render(WOW.settings.reset());
    WOW.toast('기본값으로 되돌렸습니다.', 'success');
  };

  render(WOW.settings.get());
});
