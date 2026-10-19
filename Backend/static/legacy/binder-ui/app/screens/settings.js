// 환경설정 — 이 브라우저에 자동 저장 (wow.settings.v1). Unity WebGL 도 같은 키를 읽을 수 있다
import { state, updateSettings, resetSettings, toast, go, icon } from "../core.js";

const GROUPS = [
  ["카메라 · 멀미 방지", [
    ["range", "camSensitivity", "카메라 감도", "", 0, 100, "%"],
    ["switch", "comfortVignette", "비네팅", "전투 중 시야 가장자리를 어둡게 하여 멀미를 줄입니다."],
    ["switch", "snapTurning", "스냅 이동", "왼손 스냅으로 좌/중/우 열을 한 칸씩 순간 이동합니다."],
  ]],
  ["사운드", [
    ["range", "sfx", "효과음", "", 0, 100, "%"],
    ["range", "bgm", "배경음", "", 0, 100, "%"],
    ["switch", "spatialAudio", "공간 음향", "소리가 나는 방향으로 들립니다."],
  ]],
  ["제스처 인식", [
    ["range", "gestureSensitivity", "인식 민감도", "", 0, 100, "%"],
    ["range", "holdFrames", "유지 시간 (포인팅)", "검지로 조준을 확정하기까지 필요한 프레임 수", 5, 45, "프레임"],
    ["switch", "haptics", "햅틱 피드백", "시전 · 피격 때 컨트롤러 진동"],
  ]],
  ["접근성", [
    ["switch", "subtitles", "자막", "전투 이벤트를 화면 하단에 큰 글씨로 표시합니다."],
    ["switch", "uiLarge", "UI 크게", "VR 가독성과 발표 화면을 위해 기본값은 켜짐입니다."],
    ["switch", "colorblind", "색약 모드", "적/아군 표시를 색 외에 무늬로도 구분합니다."],
  ]],
];

export default {
  title: "환경설정",
  shell: true,
  render(root) {
    const draw = () => {
      const s = state.settings;
      root.innerHTML = `
        <header class="page-head">
          <div><h1 class="page-title">환경설정</h1><p class="page-lede">변경 사항은 자동으로 저장됩니다.</p></div>
          <div class="head-actions"><button class="btn btn-quiet" data-act="reset">${icon("refresh", "sm")}기본값</button><button class="btn btn-plate" data-act="done">완료</button></div>
        </header>
        <div class="settings">
          ${GROUPS.map(([title, items]) => `<section class="sheet"><h2 class="section-title">${title}</h2>
            ${items.map(([type, key, label, desc, min, max, unit]) => type === "switch"
              ? `<div class="set-row"><div class="grow"><label class="label" for="s-${key}">${label}</label>${desc ? `<div class="hint">${desc}</div>` : ""}</div>
                  <span class="switch"><input type="checkbox" role="switch" id="s-${key}" data-key="${key}" ${s[key] ? "checked" : ""}></span></div>`
              : `<div class="set-range"><label class="label" for="s-${key}">${label}</label><span class="val" id="v-${key}">${s[key]}${unit === "%" ? "%" : ""}</span>
                  <input class="range" type="range" id="s-${key}" data-key="${key}" min="${min}" max="${max}" value="${s[key]}">
                  ${desc ? `<div class="hint" style="grid-column:1/3">${desc}</div>` : ""}</div>`).join("")}
          </section>`).join("")}
        </div>
        <p class="hint" style="margin-top:18px">로비·메타 UI는 Hand Ray 로 조작하며, 전투에 진입하면 UI Ray 는 자동으로 비활성화됩니다.</p>`;
    };
    root.addEventListener("input", (e) => {
      const key = e.target.dataset.key;
      if (!key) return;
      const v = e.target.type === "checkbox" ? e.target.checked : Number(e.target.value);
      updateSettings({ [key]: v });
      const out = root.querySelector(`#v-${key}`);
      if (out) out.textContent = key === "holdFrames" ? v : `${v}%`;
    });
    root.addEventListener("click", (e) => {
      const a = e.target.closest("[data-act]")?.dataset.act;
      if (a === "reset") { resetSettings(); draw(); toast("기본값으로 되돌렸습니다.", "success"); }
      if (a === "done") go("lobby");
    });
    draw();
  },
};
