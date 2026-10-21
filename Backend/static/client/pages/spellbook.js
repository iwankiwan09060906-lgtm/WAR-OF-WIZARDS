// 9. 마법서 · 덱 편성 — 왼쪽 19종 스킬 목록(탭), 오른쪽 선택한 스킬(그리는 룬 → 발동 마법진) + 내 덱 8칸 + 저장
import { state, saveDeck, toast, busy, confirmDialog, icon, esc, headerHTML, skHTML, runeHTML, SKILLS, SKILL, KINDS, RULES, deckProblem } from "../core.js";

export default {
  title: "마법서",
  render(root) {
    let draft = [...state.deck];
    let tab = "all";
    let focus = draft[0] || "S01";
    const saved = () => JSON.stringify(draft) === JSON.stringify(state.deck);
    const highs = () => draft.filter((id) => SKILL[id].high).length;

    const detail = () => {
      const s = SKILL[focus];
      const k = KINDS[s.kind];
      return `<div class="detail">
        <div class="detail-top">${skHTML(s.id, 64)}<div><h2>${s.name}</h2><div class="tech faint">${s.id} · ${s.high ? "고위력" : "일반"} · ${k.label}</div></div></div>
        <p>${s.effect}</p>
        <div class="rune-to-circle">
          <figure>${runeHTML(s.id, 104)}<figcaption>그리는 룬 · ${esc(s.rune)}</figcaption></figure>
          <span class="arrow">${icon("arrow", "lg")}</span>
          <figure>${skHTML(s.id, 104)}<figcaption>발동하는 마법진</figcaption></figure>
        </div>
        <p class="hint">오른손 핀치를 누른 채 허공에 그립니다. 인식되면 주먹으로 준비하고, 검지로 조준합니다.</p>
        <div class="facts"><div><b class="num">${s.high ? RULES.cooldown.high : RULES.cooldown.normal}초</b><span>쿨타임</span></div><div><b>${k.aim}</b><span>조준</span></div><div><b>${s.high ? `게임당 ${RULES.highUses}회` : "없음"}</b><span>제한</span></div></div>
      </div>`;
    };
    const deckBox = () => `<div class="deck-box">
      <div class="label">내 덱 · HUD 슬롯 순서 (2줄 × 4칸)</div>
      <div class="deck-slots">${Array.from({ length: RULES.deckSize }, (_, i) => {
        const id = draft[i];
        if (!id) return `<div class="dslot empty"><span class="k">${i + 1}</span>빈 슬롯</div>`;
        return `<div class="dslot"><span class="k">${i + 1}</span><button class="btn text" data-focus="${id}" style="padding:0">${skHTML(id, 44)}</button><span class="n">${SKILL[id].name}</span>
          <span class="dslot-tools"><button data-move="${i}:-1" aria-label="${SKILL[id].name} 앞으로" ${i === 0 ? "disabled" : ""}>${icon("left")}</button><button data-move="${i}:1" aria-label="${SKILL[id].name} 뒤로" ${i === draft.length - 1 ? "disabled" : ""}>${icon("right")}</button><button data-remove="${id}" aria-label="${SKILL[id].name} 빼기">${icon("x")}</button></span></div>`;
      }).join("")}</div>
      <p class="hint" style="margin-top:10px">전투 중에는 슬롯 순환 없이 8장이 고정 배치됩니다. 슬롯 번호는 전투 HUD의 단축키(1~8) 순서와 같아요.</p>
    </div>`;

    const draw = () => {
      const problem = deckProblem(draft);
      const full = draft.length >= RULES.deckSize;
      const list = SKILLS.filter((s) => tab === "all" || (tab === "high" ? s.high : !s.high));
      root.innerHTML = `${headerHTML("마법서 · 덱 편성", { right: `<span class="chip ${draft.length === 8 ? "cyan" : ""} tech">${draft.length} / 8</span><span class="chip amber tech">고위력 ${highs()} / 2</span>` })}
        <div class="body flush" style="overflow:hidden"><div class="book">
          <section class="book-main">
            <div class="tabs" role="tablist">${[["all", "전체 19"], ["normal", "일반 13"], ["high", "고위력 6"]].map(([k, l]) => `<button role="tab" data-tab="${k}" aria-selected="${tab === k}">${l}</button>`).join("")}</div>
            ${problem && draft.length ? `<div class="note" style="margin-top:14px">${esc(problem)}</div>` : ""}
            <div class="skill-grid">${list.map((s) => {
              const slot = draft.indexOf(s.id);
              const blocked = slot < 0 && (full || (s.high && highs() >= RULES.highLimit));
              return `<button class="skill ${slot >= 0 ? "in" : ""} ${blocked ? "blocked" : ""} ${s.id === focus ? "focus" : ""}" data-toggle="${s.id}" aria-pressed="${slot >= 0}">
                ${skHTML(s.id, 46)}<span><span class="nm">${s.name}</span><br><span class="mt">${s.id} · ${KINDS[s.kind].label} · ${s.high ? RULES.cooldown.high : RULES.cooldown.normal}s</span></span>
                <span class="slotno">${slot + 1}</span></button>`;
            }).join("")}</div>
          </section>
          <aside class="book-side">${detail()}${deckBox()}
            <div class="book-foot"><button class="btn ghost" data-act="clear" ${draft.length ? "" : "disabled"}>비우기</button>
              <button class="btn play" data-act="save" ${saved() || problem ? "disabled" : ""}>${saved() ? `${icon("check")}저장됨` : "덱 저장"}</button></div>
          </aside>
        </div></div>`;
    };

    const toggle = (id) => {
      focus = id;
      const i = draft.indexOf(id);
      if (i >= 0) draft.splice(i, 1);
      else if (draft.length >= RULES.deckSize) toast("덱이 가득 찼습니다. (8/8) 다른 스킬을 먼저 빼주세요.", "error");
      else if (SKILL[id].high && highs() >= RULES.highLimit) toast("고위력 스킬은 덱에 최대 2장까지 넣을 수 있습니다.", "error");
      else draft.push(id);
      draw();
    };
    root.addEventListener("click", async (e) => {
      const t = e.target.closest("[data-toggle],[data-tab],[data-move],[data-remove],[data-act],[data-focus]");
      if (!t) return;
      const d = t.dataset;
      if (d.toggle) toggle(d.toggle);
      else if (d.tab) { tab = d.tab; draw(); }
      else if (d.focus) { focus = d.focus; draw(); }
      else if (d.move) { const [i, k] = d.move.split(":").map(Number); [draft[i], draft[i + k]] = [draft[i + k], draft[i]]; draw(); }
      else if (d.remove) { draft = draft.filter((x) => x !== d.remove); draw(); }
      else if (d.act === "clear") { draft = []; draw(); }
      else if (d.act === "save") {
        try { await busy(t, () => saveDeck(draft), "저장 중…"); toast("마법서를 저장했습니다.", "ok"); } catch (err) { toast(err.message, "error"); }
        draw();
      }
    });
    root.addEventListener("pointerover", (e) => {   // 마우스를 올린 스킬이 오른쪽에 보인다
      const id = e.target.closest?.("[data-toggle]")?.dataset.toggle;
      if (id && id !== focus) {
        focus = id;
        root.querySelectorAll(".skill.focus").forEach((x) => x.classList.remove("focus"));
        e.target.closest("[data-toggle]").classList.add("focus");
        root.querySelector(".detail").outerHTML = detail();
      }
    });
    this.canLeave = () => (saved() ? Promise.resolve(true) : confirmDialog("저장하지 않은 덱", "편성한 덱을 저장하지 않고 나갈까요? 바꾼 내용은 사라집니다.", "저장하지 않고 나가기", true));
    draw();
    return () => { this.canLeave = null; };
  },
};
