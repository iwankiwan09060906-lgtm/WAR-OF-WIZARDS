// 마법서 · 덱 편성 — 바인더 페이지(19장) + 카드 상세(룬 ↔ 마법진 같은 크기) + 덱 슬리브 8칸
import { state, saveDeck, toast, confirmDialog, icon, cardHTML, busy, esc } from "../core.js";
import { SKILLS, SKILL, KINDS, RULES, deckProblem } from "../data.js";

export default {
  title: "마법서",
  shell: true,
  render(root) {
    let draft = [...state.deck];
    let filter = "all";
    let focus = draft[0] || "S01";
    const saved = () => JSON.stringify(draft) === JSON.stringify(state.deck);
    const highCount = () => draft.filter((id) => SKILL[id].high).length;

    const detailHTML = () => {
      const s = SKILL[focus];
      const kind = KINDS[s.kind];
      return `<div class="sheet">
        <div class="pair">
          ${cardHTML(s.id, { size: "lg" })}
          <span class="arrow">${icon("arrow", "lg")}</span>
          ${cardHTML(s.id, { size: "lg", flip: "on" })}
        </div>
        <div class="pair-cap"><span>그리는 룬</span><span></span><span>시전되는 마법진</span></div>
        <h2 class="page-title" style="font-size:2rem;margin-top:16px">${s.name}</h2>
        <div style="display:flex;gap:6px;flex-wrap:wrap;margin-top:8px">
          <span class="chip ink num">${s.id}</span>
          <span class="chip">${s.high ? "고위력" : "일반"}</span>
          <span class="chip">${kind.label}</span>
          <span class="chip">룬: ${esc(s.rune)}</span>
        </div>
        <p style="margin-top:12px">${s.effect}</p>
        <div class="facts" style="margin-top:14px">
          <div><b>${s.high ? RULES.cooldown.high : RULES.cooldown.normal}초</b><span>쿨타임</span></div>
          <div><b style="font-family:var(--font-ui);font-size:.95rem">${kind.aim}</b><span>조준</span></div>
          <div><b>${s.high ? `게임당 ${RULES.highUsesPerGame}회` : "없음"}</b><span>제한</span></div>
        </div>
        <p class="hint" style="margin-top:12px">오른손 핀치를 누른 채 허공에 그립니다. 인식되면 주먹으로 준비하고, 검지로 조준합니다. 웹에서는 게임 화면의 룬 그리기 칸에 마우스로 그리면 됩니다.</p>
      </div>`;
    };

    const deckHTML = () => {
      const pockets = [];
      for (let i = 0; i < RULES.deckSize; i++) {
        const id = draft[i];
        pockets.push(id
          ? `<div class="pocket"><span class="slot-no">${i + 1}</span>${cardHTML(id, { size: "sm", attrs: `data-focus="${id}"` })}
              <div class="pocket-tools">
                <button class="btn btn-quiet btn-icon sm" data-move="${i}:-1" aria-label="${SKILL[id].name} 앞으로" ${i === 0 ? "disabled" : ""}>${icon("left", "sm")}</button>
                <button class="btn btn-quiet btn-icon sm" data-move="${i}:1" aria-label="${SKILL[id].name} 뒤로" ${i === draft.length - 1 ? "disabled" : ""}>${icon("right", "sm")}</button>
                <button class="btn btn-quiet btn-icon sm" data-remove="${id}" aria-label="${SKILL[id].name} 빼기">${icon("x", "sm")}</button>
              </div></div>`
          : `<div class="pocket empty"><span class="slot-no">${i + 1}</span><span>빈 슬리브</span></div>`);
      }
      return `<div class="sheet deck-sleeve">
        <h2 class="section-title">내 덱 · HUD 슬롯 순서 <span class="count">2줄 × 4칸</span></h2>
        <div class="sleeve">${pockets.join("")}</div>
        <p class="hint" style="margin-top:12px">전투 중에는 슬롯 순환 없이 8장이 고정 배치됩니다. 슬롯 번호는 게임 화면의 단축키(1~8) 순서와 같아요.</p>
      </div>`;
    };

    const draw = () => {
      const problem = deckProblem(draft);
      const list = SKILLS.filter((s) => filter === "all" || (filter === "high" ? s.high : !s.high));
      const full = draft.length >= RULES.deckSize;
      root.innerHTML = `
        <header class="page-head">
          <div><h1 class="page-title">마법서 · 덱 편성</h1><p class="page-lede">카드를 눌러 덱에 넣거나 뺍니다. 19장 중 8장, 고위력은 최대 2장.</p></div>
          <div class="head-actions">
            <span class="chip num ${draft.length === RULES.deckSize ? "live" : ""}">${draft.length} / ${RULES.deckSize}장</span>
            <span class="chip num ${highCount() > RULES.highLimit ? "warn" : ""}">고위력 ${highCount()} / ${RULES.highLimit}</span>
            <button class="btn btn-quiet" data-act="clear" ${draft.length ? "" : "disabled"}>${icon("trash", "sm")}비우기</button>
            <button class="btn btn-plate" data-act="save" data-busy="저장 중…" ${saved() || problem ? "disabled" : ""}>${saved() ? `${icon("check", "sm")}저장됨` : "덱 저장"}</button>
          </div>
        </header>
        ${problem && draft.length ? `<div class="notice-box" style="margin-bottom:16px">${esc(problem)}</div>` : ""}
        <div class="book">
          <section class="sheet" aria-label="주문 카드 19장">
            <div class="tabs" role="tablist">
              ${[["all", "전체 19"], ["normal", "일반 13"], ["high", "고위력 6"]].map(([k, l]) => `<button role="tab" data-filter="${k}" aria-selected="${filter === k}">${l}</button>`).join("")}
            </div>
            <div class="binder-grid">
              ${list.map((s) => {
                const slot = draft.indexOf(s.id);
                const blocked = slot < 0 && (full || (s.high && highCount() >= RULES.highLimit));
                return `<div class="pocket">${cardHTML(s.id, {
                  size: "md", flip: "hover", clickable: true, inDeck: slot >= 0, disabled: blocked,
                  stamp: slot >= 0 ? slot + 1 : "",
                  attrs: `role="button" tabindex="0" data-toggle="${s.id}" aria-pressed="${slot >= 0}" aria-label="${s.name} ${slot >= 0 ? "덱에서 빼기" : "덱에 넣기"}"`,
                })}</div>`;
              }).join("")}
            </div>
          </section>
          <aside class="detail">${detailHTML()}${deckHTML()}</aside>
        </div>`;
    };

    const toggle = (id) => {
      focus = id;
      const i = draft.indexOf(id);
      if (i >= 0) draft.splice(i, 1);
      else if (draft.length >= RULES.deckSize) return toast("덱이 가득 찼습니다. (8/8) 다른 스킬을 먼저 빼주세요.", "error"), draw();
      else if (SKILL[id].high && highCount() >= RULES.highLimit) return toast("고위력 스킬은 덱에 최대 2장까지 넣을 수 있습니다.", "error"), draw();
      else draft.push(id);
      draw();
    };

    root.addEventListener("click", async (e) => {
      const t = e.target.closest("[data-toggle],[data-filter],[data-move],[data-remove],[data-act],[data-focus]");
      if (!t) return;
      if (t.dataset.toggle) toggle(t.dataset.toggle);
      else if (t.dataset.filter) { filter = t.dataset.filter; draw(); }
      else if (t.dataset.move) {
        const [i, d] = t.dataset.move.split(":").map(Number);
        const j = i + d;
        [draft[i], draft[j]] = [draft[j], draft[i]];
        draw();
      } else if (t.dataset.remove) { draft = draft.filter((x) => x !== t.dataset.remove); draw(); }
      else if (t.dataset.focus) { focus = t.dataset.focus; draw(); }
      else if (t.dataset.act === "clear") { draft = []; draw(); }
      else if (t.dataset.act === "save") {
        try {
          await busy(t, () => saveDeck(draft));
          toast("마법서를 저장했습니다.", "success");
        } catch (err) {
          toast(err.message, "error");
        }
        draw();
      }
    });
    root.addEventListener("keydown", (e) => {
      const card = e.target.closest?.("[data-toggle]");
      if (card && (e.key === "Enter" || e.key === " ")) { e.preventDefault(); toggle(card.dataset.toggle); }
    });
    // 마우스를 올린 카드가 오른쪽 상세에 나온다
    root.addEventListener("pointerover", (e) => {
      const id = e.target.closest?.(".binder-grid [data-toggle]")?.dataset.toggle;
      if (id && id !== focus) {
        focus = id;
        root.querySelector(".detail").innerHTML = detailHTML() + deckHTML();
      }
    });

    this.canLeave = () => (saved() ? Promise.resolve(true)
      : confirmDialog("저장하지 않은 덱", "편성한 덱을 저장하지 않고 나갈까요? 바꾼 내용은 사라집니다.", "저장하지 않고 나가기", true));
    draw();
    return () => { this.canLeave = null; };
  },
};
