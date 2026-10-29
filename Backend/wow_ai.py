# -*- coding: utf-8 -*-
"""
War of Wizards — AI 전략 코치 (VLM: 그림 + 상황 → 지금 할 최선의 수)

wow_server.py 의 POST /api/ai/strategy 가 부른다. 게임 화면 사진(선택)과 상황(HP · 위치 · 슬롯 · 구조물 ·
투사체 · 레인 유닛 · 상대들)을 받아 "무슨 스킬을 어느 열에, 어디로 움직일지 · 누구를 노릴지"를 돌려준다.
1:1 과 3 · 4인 개인전(팀 없이 나 빼고 모두 적)을 함께 다룬다.

    provider  claude : Claude (공식 anthropic SDK, 그림을 읽는 모델)  — pip install anthropic + ANTHROPIC_API_KEY
              ollama : 로컬 VLM (Ollama, 예: qwen2.5vl:7b)            — 무료 · 인터넷 없이
              local  : 이 게임으로 미세조정한 VLM (OpenAI 호환 서버, 예: llamafactory-cli api) — Docs/Backend/AiLearning.md
              rules  : 규칙 AI (외부 패키지 없음, 항상 동작)           — 위가 실패해도 이걸로 답한다
              auto   : Claude 를 쓸 수 있으면 Claude, 아니면 rules

학습: wow_learn.py 가 판 결과로 배운 것(이 상황에서 이긴 스킬 · 이긴 판 예시)을 규칙 AI 와 VLM 프롬프트에 함께 넣는다.

하이브리드 봇 (Unity 두뇌 + VLM, Docs/Bot/AiStrategyBot.md):
    Unity 두뇌 (빠른 반사, 0.25초) : 피하기 · 조준 깊이 · 시전 시점 · 위급할 때 스스로. 지금 고른 후보를 botPick 으로 보낸다
    VLM        (전략, 몇 초에 한 번) : botPick + 전장 그림 → orders(작전: 집중 레인 · 자세 · 먼저 쓸 / 아낄 스킬 · 유지 시간) + 지금 한 수
    Unity 두뇌는 그 작전을 0.25초마다 점수에 곱한다 → VLM 이 느려도 전략은 Unity 속도로 반응한다.
    그림이 없으면 상황 · 유닛 점으로 전장 지도를 그려 넣는다 (wow_board.py — 미세조정 데이터셋도 같은 그림).

열(column)은 0 = 왼쪽 · 1 = 가운데 · 2 = 오른쪽, 모두 "요청한 플레이어 자신의 시점" 기준이다
(Unity BotObservation · SpellCastRequest.TargetColumn 과 같은 규칙). 응답 형식은 Docs/Backend/AiStrategy.md.
"""

import base64
import json
import os
import re
import threading
import time
import urllib.error
import urllib.request

import wow_board
import wow_learn

DEFAULT_MODEL = "claude-opus-5-5"
MAX_IMAGE_BYTES = 4_500_000         # Claude 그림 한 장 한도(5MB)보다 조금 작게
COLUMN_KO = ["왼쪽", "가운데", "오른쪽"]

# 스킬 19종 요약 (서버 SKILLS 와 같은 코드). kind: aim 이 필요한지 판단용
NEEDS_AIM = {"projectile", "area", "beam", "summon", "install"}

# VLM 이 꼭 이 모양으로 답하게 하는 JSON 스키마 (Claude structured outputs · Ollama format 공용)
STRATEGY_SCHEMA = {
    "type": "object",
    "properties": {
        "summary": {"type": "string", "description": "지금 상황 한 문장 판단 (한국어)"},
        "threat": {"type": "string", "enum": ["low", "medium", "high"]},
        "plan": {"type": "array", "items": {"type": "string"}, "description": "우선순위 순 행동 1~4개 (한국어, 짧게)"},
        "cast": {
            "type": "object",
            "properties": {
                "slot": {"type": "integer", "description": "덱 슬롯 0~7, 쓸 스킬이 없으면 -1"},
                "spellId": {"type": "string", "description": "S01~S13 · H01~H06, 없으면 빈 문자열"},
                "column": {"type": "integer", "description": "조준 열 0 왼쪽 · 1 가운데 · 2 오른쪽, 조준 없는 스킬은 -1"},
                "depth": {"type": "number", "description": "범위 스킬 깊이 0(내 쪽)~1(상대 쪽)"},
                "reason": {"type": "string"},
            },
            "required": ["slot", "spellId", "column", "depth", "reason"],
            "additionalProperties": False,
        },
        "move": {
            "type": "object",
            "properties": {
                "direction": {"type": "integer", "description": "-1 왼쪽으로 한 칸 · 0 그대로 · 1 오른쪽으로 한 칸"},
                "reason": {"type": "string"},
            },
            "required": ["direction", "reason"],
            "additionalProperties": False,
        },
        "focus": {"type": "string", "description": "3 · 4인 개인전에서 지금 노릴 상대와 이유 (1:1 이면 빈 문자열)"},
        "observations": {"type": "array", "items": {"type": "string"}, "description": "그림에서 본 것 (그림이 없으면 빈 배열)"},
        "orders": {
            "type": "object",
            "description": "작전 — Unity 두뇌가 seconds 동안 0.25초마다 적용한다",
            "properties": {
                "lane": {"type": "integer", "description": "집중할 레인 0 왼쪽 · 1 가운데 · 2 오른쪽, 없으면 -1"},
                "stance": {"type": "string", "enum": ["defend", "balanced", "push"]},
                "prefer": {"type": "array", "items": {"type": "string"}, "description": "먼저 쓸 스킬 코드 0~3개"},
                "hold": {"type": "array", "items": {"type": "string"}, "description": "아껴 둘 스킬 코드 0~3개"},
                "seconds": {"type": "number", "description": "작전 유지 시간 3~15초"},
            },
            "required": ["lane", "stance", "prefer", "hold", "seconds"],
            "additionalProperties": False,
        },
    },
    "required": ["summary", "threat", "plan", "cast", "move", "focus", "observations", "orders"],
    "additionalProperties": False,
}

# 봇(brief)용 짧은 답 — 글이 짧을수록 빨리 온다. 나머지 칸은 힌트로 채운다
BRIEF_SCHEMA = {"type": "object", "properties": {k: STRATEGY_SCHEMA["properties"][k] for k in ("threat", "orders", "cast", "move")},
                "required": ["threat", "orders", "cast", "move"], "additionalProperties": False}
STANCES = ("defend", "balanced", "push")

RULES_TEXT = """\
게임: War of Wizards — Quest 3 VR 마법 대전. 1:1 또는 3 · 4인 개인전 (팀 없음 — 나 빼고 모두 적).
- 전장은 세 레인(왼쪽 · 가운데 · 오른쪽). 10초마다 모든 진영의 미니언이 세 레인으로 나온다.
- 플레이어는 자기 진영 뒤 Commander 발판의 왼쪽 · 가운데 · 오른쪽 세 칸만 이동한다 (왼손 휘두르기 = 한 칸).
- 순서: 상대 타워 → 상대 넥서스 → 상대 플레이어를 쓰러뜨리면 승리.
- 플레이어 스킬은 타워 · 넥서스를 공격하지 못한다. 구조물은 미니언 · 소환수 · 터렛이 부순다.
- 방어 계층: 상대 타워가 살아 있으면 상대 플레이어에게 스킬 피해 20%만, 타워가 무너지면 100%.
  넥서스까지 무너지면 상대 유닛이 플레이어를 직접 공격한다(회피 불가).
- 방어 계층은 상대마다 따로다: 그 상대의 타워가 살아 있으면 그 상대에게 20%, 무너졌으면 100%.
- 상대 타워 파괴 시 내 모든 공격 +20%, 넥서스 파괴 시 +40% (영구). 5분마다 모두 강화.
- 쿨타임: 일반 5초(소환 S11 · S12 는 8초), 고위력 20초. 고위력 스킬은 경기당 스킬별 3회까지. 덱 8장(고위력 최대 2장).
- 투사체 · 레이저 · 장판은 다른 칸으로 피하면 맞지 않는다. 30초 동안 아무것도 안 하면 기권패.
- 열 번호는 모두 이 플레이어 자신의 시점: 0 = 왼쪽, 1 = 가운데, 2 = 오른쪽."""

SYSTEM_PROMPT = f"""너는 War of Wizards 의 전략 담당이다. 그림과 상황 JSON 을 보고 작전(orders)과 지금 할 한 수(cast · move)를 정한다.

{RULES_TEXT}

하이브리드: 봇은 Unity 두뇌와 너, 둘이 함께 둔다.
- Unity 두뇌(빠른 반사): 0.25초마다 투사체 피하기 · 조준 깊이 · 시전 시점을 맡는다. botHint 는 Unity 두뇌가 지금 매긴 후보(점수 순)다.
- 너(전략): orders 를 정한다 — lane 집중할 레인, stance(defend 지키기 · balanced · push 밀기), prefer 먼저 쓸 스킬, hold 아껴 둘 스킬, seconds 유지 시간.
  Unity 두뇌가 그 작전을 seconds 동안 0.25초마다 점수에 반영하므로 한 수보다 "다음 몇 초의 방향"이 중요하다. 사람 코치 요청이면 orders 는 참고용이다.
- 그림: 게임 화면 사진이거나 상황으로 그린 전장 지도다. 지도는 위 = 상대 진영(빨강) · 아래 = 내 진영(파랑) · 세로 세 줄 = 레인(내 시점 왼쪽 · 가운데 · 오른쪽),
  파랑 점 = 우리 유닛 · 빨강 점 = 적 유닛(아래로 올수록 내 타워에 가깝다), 노란 마름모 = 내 칸으로 오는 투사체, 작은 네모 = 타워 · 큰 네모 = 넥서스(회색 X = 무너짐),
  세모 = 플레이어, 맨 아래 8칸 = 덱 슬롯(초록 = 쓸 수 있음 · 금색 테두리 = 고위력), 맨 위 · 맨 아래 막대 = HP.

판단 원칙:
- 쓸 수 있는 스킬은 상황 JSON 의 slots 중 ready 가 true 인 것뿐이다. cast.slot 은 그 슬롯 번호, cast.spellId 는 그 슬롯의 코드.
- 날아오는 투사체가 내 칸을 노리면 피하는 것(move)이 먼저다.
- 상대 타워가 살아 있으면 상대 플레이어를 때리는 것보다 레인의 적 유닛을 지우고 우리 유닛을 밀어 주는 쪽이 낫다.
- 고위력 스킬은 남은 횟수가 적으니 결정적인 순간(적 유닛이 몰렸을 때, 상대 타워가 무너진 뒤)에 쓴다.
- 3 · 4인 개인전이면 opponents 의 HP · 타워 상태를 보고 노릴 상대를 고른다 (타워가 무너진 상대 → HP 가 낮은 상대).
  다른 상대끼리 싸우게 두고 힘을 아끼는 것도 전략이다. 고른 상대와 이유를 focus 에 적는다 (1:1 이면 빈 문자열).
- 그림에서 확실히 보이지 않는 것은 지어내지 않는다. 상황 JSON 과 그림이 다르면 그림을 믿되 observations 에 적는다.
- hint 는 Unity 두뇌(botHint 1순위) · 규칙 AI · 학습 결과로 만든 제안(orders 포함)이다. 그림을 보고 더 나은 수가 있으면 바꿔도 된다.
- learned 는 이 게임의 실제 판 결과로 배운 것이다 (같은 상황에서 이긴 스킬 순위 · 이긴 판 예시). 표본(n)이 많을수록 믿는다.
  advantage 는 같은 상황 평균보다 얼마나 나은지다 (+ 좋음 · - 나쁨). 밀리는 상황이면 모든 스킬의 승률이 낮으니 승률보다 advantage 로 비교한다.
- 모든 문장은 한국어로 짧게 (게임 중에 한눈에 읽힌다)."""


# ─────────────────────────────────────────────────────────────────────────────
# 상황 정리 — Unity BotObservation(PascalCase) · 웹(camelCase) 둘 다 받는다
# ─────────────────────────────────────────────────────────────────────────────
def _lower_keys(d):
    out = {}
    for k, v in (d or {}).items():
        if isinstance(k, str) and k:
            out[k[0].lower() + k[1:]] = v
    return out


def _num(v, default=0.0):
    try:
        return float(v)
    except (TypeError, ValueError):
        return default


def _col(v, default=None):
    """0 · 1 · 2 · "L" · "C" · "R" · "Left" … → 0~2"""
    if v is None or v == "":
        return default
    s = str(v).strip().upper()
    if s[:1] in ("L", "C", "R") and not s[:1].isdigit():
        return "LCR".index(s[:1])
    try:
        return max(0, min(2, int(float(s))))
    except ValueError:
        return default


def _code(v):
    """"S03" · 3 · 102 → "S03" · "S03" · "H02" """
    s = str(v).strip().upper()
    if re.fullmatch(r"[SH]\d\d", s):
        return s
    try:
        n = int(float(s))
    except ValueError:
        return ""
    return f"H{n - 100:02d}" if n > 100 else f"S{n:02d}"


def normalize_state(raw, skills):
    st = _lower_keys(raw if isinstance(raw, dict) else {})
    by_code = {s[1]: s for s in skills}
    deck = [_code(x) for x in (st.get("deck") or [])][:8]
    deck = [c for c in deck if c in by_code]

    ready = st.get("ready")
    mask = st.get("selfSlotReadyMask", st.get("readyMask"))
    cooldowns = st.get("cooldowns")
    if isinstance(ready, list):
        ready = [bool(x) for x in ready]
    elif mask is not None:
        m = int(_num(mask))
        ready = [bool(m >> i & 1) for i in range(len(deck))]
    elif isinstance(cooldowns, list):
        ready = [_num(x) <= 0 for x in cooldowns]
    else:
        ready = [True] * len(deck)
    ready = (ready + [False] * len(deck))[:len(deck)]
    uses = st.get("uses") or st.get("highUses")
    uses = [int(_num(x, 3)) for x in uses] if isinstance(uses, list) else [3] * len(deck)
    uses = (uses + [3] * len(deck))[:len(deck)]

    slots = []
    for i, code in enumerate(deck):
        sk = by_code[code]
        high = sk[4] == "HighPower"
        ok = ready[i] and (not high or uses[i] > 0)
        slots.append({"slot": i, "spellId": code, "name": sk[2], "kind": sk[6], "high": high,
                      "ready": ok, "usesLeft": uses[i] if high else None, "effect": sk[11]})

    lanes = []
    for i, ln in enumerate((st.get("lanes") or [])[:3]):
        ln = _lower_keys(ln) if isinstance(ln, dict) else {}
        front = round(_num(ln.get("enemyFront"), 0.5), 2)
        lane = {"column": i, "allies": int(_num(ln.get("allies"))), "enemies": int(_num(ln.get("enemies"))), "enemyFront": front}
        if ln.get("clusterDepth") is not None:
            lane["clusterDepth"] = round(_num(ln.get("clusterDepth"), front), 2)
        if ln.get("allyFront") is not None:
            lane["allyFront"] = round(_num(ln.get("allyFront")), 2)
        if ln.get("nearBase") is not None:
            lane["nearBase"] = int(_num(ln.get("nearBase")))
        lanes.append(lane)

    # Unity 봇이 보낸 유닛 점 [열, 깊이, 적?] (전장 지도용, 프롬프트 글에는 안 넣는다)
    units = []
    for u in (st.get("units") or [])[:120]:
        if isinstance(u, dict):
            u = _lower_keys(u)
            c, d, e = u.get("c", u.get("column")), u.get("d", u.get("depth")), u.get("e", u.get("enemy"))
        elif isinstance(u, (list, tuple)) and len(u) >= 3:
            c, d, e = u[0], u[1], u[2]
        else:
            continue
        col = _col(c)
        if col is not None:
            units.append([col, round(max(0.0, min(1.0, _num(d, 0.5))), 3), bool(e)])

    # Unity 두뇌가 매긴 후보 (점수 순) — 준비된 슬롯 · 덱과 맞는 것만
    picks = []
    for p in (st.get("botPick") or [])[:4]:
        p = _lower_keys(p) if isinstance(p, dict) else {}
        slot = int(_num(p.get("slot"), -1))
        if 0 <= slot < len(slots) and slots[slot]["ready"] and _code(p.get("spellId", slots[slot]["spellId"])) == slots[slot]["spellId"]:
            picks.append({"slot": slot, "spellId": slots[slot]["spellId"], "column": _col(p.get("column"), -1),
                          "depth": round(max(0.0, min(1.0, _num(p.get("depth"), 0.5))), 2),
                          "score": round(_num(p.get("score")), 2), "why": str(p.get("why") or "")[:60]})

    def people(key):
        out = []
        for p in (st.get(key) or [])[:3]:
            p = _lower_keys(p) if isinstance(p, dict) else {}
            out.append({"nickname": str(p.get("nickname") or p.get("name") or "")[:20],
                        "hp": _num(p.get("hp"), -1), "column": _col(p.get("column")), "bot": bool(p.get("bot")),
                        "towerAlive": p.get("towerAlive", True) is not False, "nexusAlive": p.get("nexusAlive", True) is not False})
        return out

    incoming = int(_num(st.get("incomingProjectileCount", st.get("incoming"))))
    opponents = people("opponents")
    players = int(_num(st.get("players", st.get("maxPlayers")), 2))
    players = max(2, min(4, max(players, len(opponents) + 1)))
    return {
        "time": round(_num(st.get("matchTimeSeconds", st.get("time"))), 1),
        "mode": str(st.get("mode") or ""), "players": players,
        "selfHp": _num(st.get("selfHp"), 1000), "selfMaxHp": _num(st.get("selfMaxHp", st.get("maxHp")), 1000) or 1000,
        "opponentHp": _num(st.get("opponentHp"), 1000), "opponentMaxHp": _num(st.get("opponentMaxHp", st.get("maxHp")), 1000) or 1000,
        "selfColumn": _col(st.get("selfColumn"), 1), "opponentColumn": _col(st.get("opponentColumn")),
        "selfTowerAlive": st.get("selfTowerAlive", True) is not False, "selfNexusAlive": st.get("selfNexusAlive", True) is not False,
        "opponentTowerAlive": st.get("opponentTowerAlive", True) is not False,
        "opponentNexusAlive": st.get("opponentNexusAlive", True) is not False,
        "incoming": {"count": incoming,
                     "column": _col(st.get("nearestIncomingProjectileColumn", st.get("incomingColumn"))) if incoming else None,
                     "eta": round(_num(st.get("nearestIncomingProjectileEta", st.get("incomingEta")), 9), 2) if incoming else None},
        "slots": slots, "lanes": lanes, "opponents": opponents, "units": units, "botPick": picks,
    }


# ─────────────────────────────────────────────────────────────────────────────
# 규칙 AI — 외부 패키지 없이 항상 동작 (VLM 의 힌트 · 대체 답)
# ─────────────────────────────────────────────────────────────────────────────
def _pick(st, codes):
    """codes 순서대로 쓸 수 있는 첫 슬롯"""
    for code in codes:
        for s in st["slots"]:
            if s["spellId"] == code and s["ready"]:
                return s
    return None


def apply_learning(hint, st, adv, margin=0.25):
    """학습 결과가 충분히 확실하고 규칙 AI 의 수보다 확실히 낫다면 시전할 스킬을 바꾼다 (피하기는 그대로)."""
    if not adv or not adv["confident"]:
        return hint
    top = adv["ranked"][0]
    cur = next((x for x in adv["ranked"] if x["slot"] == hint["cast"]["slot"]), None)
    # 같은 상황 평균보다 나은 스킬(advantage > 0)로만, 지금 수보다 확실히 나을 때만 바꾼다
    if top["advantage"] <= 0 or hint["cast"]["slot"] == top["slot"] or (cur and cur["advantage"] + margin > top["advantage"]):
        hint["plan"] = (hint["plan"] + [wow_learn.describe(adv)])[:4]
        return hint
    s = next(x for x in st["slots"] if x["slot"] == top["slot"])
    col = -1
    if s["kind"] in NEEDS_AIM:
        lanes = st["lanes"]
        busiest = max(range(len(lanes)), key=lambda i: lanes[i]["enemies"]) if lanes else None
        col = hint["cast"]["column"] if hint["cast"]["column"] >= 0 else busiest if busiest is not None else (
            st["opponentColumn"] if st["opponentColumn"] is not None else 1)
    old = hint["cast"]["spellId"]
    hint = dict(hint)
    hint["cast"] = {"slot": s["slot"], "spellId": s["spellId"], "column": col, "depth": hint["cast"].get("depth", 0.5),
                    "reason": wow_learn.describe(adv)}
    plan = [x for x in hint["plan"] if not (old and x.startswith(old + " "))]      # 규칙 AI 의 스킬 줄만 바꾼다
    plan.insert(1 if hint["move"]["direction"] else 0,
                f"{s['spellId']} {s['name']}" + (f" → {COLUMN_KO[col]} 열" if col >= 0 else "") + " (학습)")
    hint["plan"] = plan[:4]
    return hint


def rules_strategy(st):
    plan, cast, move, threat = [], None, {"direction": 0, "reason": "지금 칸이 안전합니다."}, "low"
    me_col = st["selfColumn"]
    hp = st["selfHp"] / st["selfMaxHp"]
    lanes = st["lanes"]
    enemies = [ln["enemies"] for ln in lanes] if lanes else []
    total_enemies = sum(enemies)
    busiest = max(range(len(enemies)), key=lambda i: enemies[i]) if enemies else 1
    # 노릴 상대: 개인전이면 타워가 무너진 상대 → HP 가 낮은 상대, 1:1 이면 그 상대
    targets = [o for o in st["opponents"] if o["hp"] != 0] or [
        {"nickname": "", "hp": st["opponentHp"], "column": st["opponentColumn"], "towerAlive": st["opponentTowerAlive"],
         "nexusAlive": st["opponentNexusAlive"]}]
    focus = min(targets, key=lambda o: (o["towerAlive"], o["hp"] if o["hp"] >= 0 else 1e9))
    opp_col = focus["column"]
    opp_exposed = not focus["towerAlive"]

    # 1) 회피: 내 칸을 노리는 투사체가 곧 도착
    inc = st["incoming"]
    if inc["count"] and inc["column"] == me_col and (inc["eta"] or 9) < 2.0:
        threat = "high"
        safe = [c for c in (me_col - 1, me_col + 1) if 0 <= c <= 2]
        target = min(safe, key=lambda c: abs(c - 1))       # 가운데 쪽으로 피하면 다음 회피가 쉽다
        move = {"direction": 1 if target > me_col else -1,
                "reason": f"{inc['eta']}초 뒤 투사체가 내 칸으로 옵니다 → {COLUMN_KO[target]}으로 피하기"}
        plan.append(f"{COLUMN_KO[target]} 칸으로 피하기")

    # 2) 위험할 때 지키기
    if hp < 0.35:
        threat = "high"
        s = _pick(st, ["S08"] + (["H04"] if opp_exposed or hp < 0.2 else []) + (["H03"] if opp_exposed else []))
        if s:
            col = opp_col if s["spellId"] == "H03" and opp_col is not None else -1
            cast = {"slot": s["slot"], "spellId": s["spellId"], "column": col, "depth": 0.5,
                    "reason": f"HP {round(hp * 100)}% — {s['name']}(으)로 버티기"}
    if not st["selfNexusAlive"] and threat != "high":
        threat = "medium"

    # 3) 적 유닛이 몰린 레인 정리 (내 타워를 지키고 우리 유닛 길을 연다)
    if cast is None and total_enemies >= 6:
        s = _pick(st, ["H01"])
        if s:
            threat = "high"
            cast = {"slot": s["slot"], "spellId": "H01", "column": -1, "depth": 0.5,
                    "reason": f"적 유닛 {total_enemies}기 — 프리즈로 전부 멈추기"}
    if cast is None and enemies and enemies[busiest] >= 3:
        threat = "medium" if threat == "low" else threat
        s = _pick(st, ["S04", "S03", "S06", "S02", "S07", "S05", "H02"])
        if s:
            front = lanes[busiest]["enemyFront"]
            cast = {"slot": s["slot"], "spellId": s["spellId"], "column": busiest, "depth": round(max(0.15, min(0.85, front)), 2),
                    "reason": f"{COLUMN_KO[busiest]} 레인 적 유닛 {enemies[busiest]}기 정리"}

    # 4) 상대 타워가 무너졌으면 상대 플레이어를 직접 노린다 (피해 100%)
    if cast is None and opp_exposed and opp_col is not None:
        s = _pick(st, ["H02", "H03", "S07", "S05", "S01"])
        if s:
            cast = {"slot": s["slot"], "spellId": s["spellId"], "column": opp_col, "depth": 0.9,
                    "reason": f"{focus['nickname'] or '상대'} 타워가 없어 피해 100% — {COLUMN_KO[opp_col]} 열로"}

    # 5) 밀기: 우리 유닛이 많은 레인에 분노 · 소환
    if cast is None:
        weakest = min(range(len(lanes)), key=lambda i: lanes[i]["allies"] - lanes[i]["enemies"]) if lanes else busiest
        strongest = max(range(len(lanes)), key=lambda i: lanes[i]["allies"] - lanes[i]["enemies"]) if lanes else 1
        if lanes and lanes[strongest]["allies"] >= 3:
            s = _pick(st, ["H05"])
            if s:
                cast = {"slot": s["slot"], "spellId": "H05", "column": strongest, "depth": 0.6,
                        "reason": f"{COLUMN_KO[strongest]} 레인 우리 유닛 {lanes[strongest]['allies']}기에 분노 — 타워 밀기"}
        if cast is None:
            s = _pick(st, ["S11", "S12", "H06", "S13"])
            if s:
                cast = {"slot": s["slot"], "spellId": s["spellId"], "column": weakest, "depth": 0.3,
                        "reason": f"{COLUMN_KO[weakest]} 레인이 밀리고 있어 {s['name']} 보강"}
    # 6) 남는 슬롯: 버프 · 아무 공격
    if cast is None:
        s = _pick(st, ["S09", "S10"] if st["selfTowerAlive"] else ["S10"])
        s = s or next((x for x in st["slots"] if x["ready"] and not x["high"]), None)
        if s:
            col = busiest if s["kind"] in NEEDS_AIM else -1
            cast = {"slot": s["slot"], "spellId": s["spellId"], "column": col, "depth": 0.5,
                    "reason": f"쿨타임이 끝난 {s['name']} — 쉬지 말고 쓰기 (30초 무활동 기권 방지)"}
    if cast is None:
        cast = {"slot": -1, "spellId": "", "column": -1, "depth": 0.5, "reason": "쓸 수 있는 스킬이 없습니다. 쿨타임을 기다리세요."}
    elif cast["slot"] >= 0:
        plan.append(f"{cast['spellId']} {next(s['name'] for s in st['slots'] if s['slot'] == cast['slot'])}"
                    + (f" → {COLUMN_KO[cast['column']]} 열" if cast["column"] >= 0 else ""))

    if focus["towerAlive"]:
        plan.append("상대 타워가 살아 있는 동안은 유닛 정리 · 밀기가 먼저 (상대에게 스킬 피해 20%)")
    elif focus["nexusAlive"]:
        plan.append(f"{focus['nickname'] or '상대'} 타워 파괴! 내 공격 +20% — 넥서스 앞으로 유닛 밀기")
    if not st["selfTowerAlive"]:
        plan.append("내 타워가 없어 상대 스킬을 100% 맞습니다 — 칸 이동으로 피하기")
    focus_text = ""
    if st["players"] > 2 and focus["nickname"] and (opp_exposed or focus["hp"] >= 0):   # 근거(HP · 타워)가 있을 때만
        why = "타워가 무너져 피해 100%" if opp_exposed else f"HP {int(focus['hp'])}로 가장 약함"
        focus_text = f"{focus['nickname']} 노리기 — {why}"

    if lanes:
        summary = f"적 유닛 {total_enemies}기 (최다 {COLUMN_KO[busiest]} {enemies[busiest]}기), 내 HP {round(hp * 100)}%"
    else:
        summary = f"내 HP {round(hp * 100)}% · 경기 {int(st['time'] // 60)}분 {int(st['time'] % 60)}초 — 레인 정보 없이 판단"
    return {"summary": summary, "threat": threat, "plan": plan[:4], "cast": cast, "move": move,
            "focus": focus_text, "observations": []}


def bot_hint(st, base):
    """Unity 두뇌의 1순위 후보(botPick)가 있으면 규칙 AI 의 시전 대신 그것을 힌트로 (피하기 · 노릴 상대는 규칙 AI 그대로)"""
    if not st.get("botPick"):
        return base
    p = st["botPick"][0]
    s = next(x for x in st["slots"] if x["slot"] == p["slot"])
    col = (p["column"] if p["column"] >= 0 else st["selfColumn"]) if s["kind"] in NEEDS_AIM else -1
    old = base["cast"].get("spellId")
    hint = dict(base)
    hint["cast"] = {"slot": s["slot"], "spellId": s["spellId"], "column": col, "depth": p["depth"],
                    "reason": f"Unity 두뇌 1순위 — {p['why'] or s['name']} (점수 {p['score']:g})"}
    plan = [x for x in base["plan"] if not (old and x.startswith(old + " "))]
    plan.insert(1 if base["move"]["direction"] else 0,
                f"{s['spellId']} {s['name']}" + (f" → {COLUMN_KO[col]} 열" if col >= 0 else "") + " (Unity 두뇌)")
    hint["plan"] = plan[:4]
    return hint


def rules_orders(st, hint, adv=None, min_n=5):
    """VLM 없이 정하는 작전 — 레인 상황 · 힌트의 한 수 · 학습 결과(이긴 스킬은 먼저, 진 스킬은 아끼기)"""
    lanes = st["lanes"]
    hp = st["selfHp"] / st["selfMaxHp"]
    enemies = [ln["enemies"] for ln in lanes]
    allies = [ln["allies"] for ln in lanes]
    near = any(ln["enemies"] and (ln.get("nearBase", 0) > 0 or ln["enemyFront"] < 0.25) for ln in lanes)
    exposed = not st["opponentTowerAlive"] or any(not o["towerAlive"] for o in st["opponents"])
    if hp < 0.35 or not st["selfTowerAlive"] or near:
        stance = "defend"
    elif exposed or (lanes and sum(allies) - sum(enemies) >= 3):
        stance = "push"
    else:
        stance = "balanced"
    lane = -1
    if lanes and (stance == "defend" or max(enemies) >= 3):
        lane = max(range(len(lanes)), key=lambda i: (enemies[i], -lanes[i]["enemyFront"]))
    elif lanes and stance == "push":
        lane = max(range(len(lanes)), key=lambda i: allies[i] - enemies[i])
    # 학습: 같은 상황 평균보다 확실히 나은 스킬은 먼저, 이 상황에서도 전체에서도 못한 스킬은 아낀다 (표본 min_n 이상)
    learned = [x for x in (adv or {}).get("all") or [] if x["n"] >= min_n]
    bad = {x["spellId"] for x in learned if x["advantage"] < -0.3 and x["overall"] < 0}
    cast = hint["cast"].get("spellId")
    prefer = [cast] if cast and cast not in bad else []
    hold = sorted(bad, key=lambda s: next(x["advantage"] for x in learned if x["spellId"] == s))
    for x in learned:
        if x["advantage"] > 0.2 and x["spellId"] not in prefer:
            prefer.append(x["spellId"])
    return {"lane": lane, "stance": stance, "prefer": prefer[:3], "hold": [h for h in hold if h not in prefer][:3], "seconds": 8}


def make_hint(st, adv=None, min_n=5):
    """VLM 에 주는 제안이자 VLM 이 없을 때의 답: 규칙 AI → Unity 두뇌 1순위 → 학습 결과 + 작전"""
    hint = apply_learning(bot_hint(st, rules_strategy(st)), st, adv)
    hint["orders"] = rules_orders(st, hint, adv, min_n)
    return hint


def clean_orders(o, st, fallback):
    """VLM 작전을 게임 규칙에 맞게: 덱에 없는 스킬 · 잘못된 레인 · 시간은 고치고, 없으면 힌트 작전"""
    if not isinstance(o, dict):
        return dict(fallback)
    deck = [s["spellId"] for s in st["slots"]]

    def codes(v):
        out = []
        for x in v if isinstance(v, list) else []:
            c = _code(x)
            if c in deck and c not in out:
                out.append(c)
        return out[:3]
    lane = int(_num(o.get("lane"), -1))
    prefer = codes(o.get("prefer"))
    return {"lane": lane if 0 <= lane <= 2 else -1,
            "stance": o.get("stance") if o.get("stance") in STANCES else fallback["stance"],
            "prefer": prefer, "hold": [h for h in codes(o.get("hold")) if h not in prefer],
            "seconds": round(max(3.0, min(15.0, _num(o.get("seconds"), fallback["seconds"]))), 1)}


# ─────────────────────────────────────────────────────────────────────────────
# VLM 호출
# ─────────────────────────────────────────────────────────────────────────────
def parse_image(image):
    """"data:image/jpeg;base64,..." 또는 base64 문자열 → (media_type, base64). 없으면 (None, None)"""
    if not image or not isinstance(image, str):
        return None, None
    media = "image/jpeg"
    m = re.match(r"^data:(image/(?:png|jpeg|jpg|webp|gif));base64,(.*)$", image, re.S)
    data = m.group(2) if m else image
    if m:
        media = m.group(1).replace("jpg", "jpeg")
    data = re.sub(r"\s+", "", data)
    try:
        raw = base64.b64decode(data, validate=True)
    except ValueError:
        raise ValueError("image 는 base64 그림(data:image/...;base64,...)이어야 합니다.")
    if len(raw) > MAX_IMAGE_BYTES:
        raise ValueError(f"그림이 너무 큽니다 ({len(raw) // 1024} KB). 1280px 이하 JPEG 로 줄여 주세요.")
    if raw[:8] == b"\x89PNG\r\n\x1a\n":
        media = "image/png"
    elif raw[:3] == b"\xff\xd8\xff":
        media = "image/jpeg"
    elif raw[:4] == b"RIFF" and raw[8:12] == b"WEBP":
        media = "image/webp"
    return media, data


def _prompt_text(st, hint, adv=None, brief=False):
    view = {k: v for k, v in st.items() if k not in ("units", "botPick")}      # 유닛 점은 그림으로 본다
    view["selfColumnName"] = COLUMN_KO[st["selfColumn"]]
    text = ("상황 JSON (열 0 왼쪽 · 1 가운데 · 2 오른쪽, 내 시점):\n"
            + json.dumps(view, ensure_ascii=False))
    if st.get("botPick"):
        text += "\n\nbotHint (Unity 두뇌 후보, 점수 순):\n" + json.dumps(st["botPick"], ensure_ascii=False)
    text += "\n\nhint (Unity 두뇌 · 규칙 AI · 학습 제안):\n" + json.dumps(hint, ensure_ascii=False)
    if adv:
        text += "\n\nlearned (이 게임 판 결과로 배운 것):\n" + json.dumps(
            {"situation": adv["situation"], "games": adv["games"], "ranked": adv["ranked"], "examples": adv["examples"]}, ensure_ascii=False)
    if brief:
        return text + "\n\n봇 요청이다: threat · orders · cast · move 만 짧게 (reason 은 15자 이내) 스키마대로 답하라."
    return text + "\n\n지금 할 최선의 한 수와 작전을 스키마대로 답하라."


def training_prompt(st):
    """미세조정 데이터셋의 user 글 — 실제 요청과 같은 모양 (같은 힌트 · 작전)"""
    return _prompt_text(st, make_hint(st))


def claude_available():
    try:
        import anthropic  # noqa: F401
    except ImportError:
        return False, "anthropic 패키지가 없습니다 (pip install anthropic)"
    if not (os.environ.get("ANTHROPIC_API_KEY") or os.environ.get("ANTHROPIC_AUTH_TOKEN") or os.environ.get("ANTHROPIC_PROFILE")):
        return False, "ANTHROPIC_API_KEY 환경변수가 없습니다"
    return True, ""


_CLIENT = None
_CLIENT_LOCK = threading.Lock()


def _claude_client():
    global _CLIENT
    import anthropic
    with _CLIENT_LOCK:
        if _CLIENT is None:
            _CLIENT = anthropic.Anthropic(timeout=60.0, max_retries=1)
        return _CLIENT


def claude_strategy(st, hint, media, data, model, effort, adv=None, brief=False):
    import anthropic
    content = []
    if data:
        content.append({"type": "image", "source": {"type": "base64", "media_type": media, "data": data}})
    content.append({"type": "text", "text": _prompt_text(st, hint, adv, brief)})
    try:
        resp = _claude_client().beta.messages.create(
            model=model,
            max_tokens=16000,
            system=SYSTEM_PROMPT,
            messages=[{"role": "user", "content": content}],
            output_config={"effort": effort, "format": {"type": "json_schema", "schema": BRIEF_SCHEMA if brief else STRATEGY_SCHEMA}},
            betas=["server-side-fallback-2026-07-01"],
            fallbacks="default",          # 안전 분류기가 거절하면 Anthropic 추천 모델로 서버가 다시 시도
        )
    except anthropic.AuthenticationError:
        raise RuntimeError("Claude API 키가 올바르지 않습니다")
    except anthropic.RateLimitError:
        raise RuntimeError("Claude API 요청 한도 초과 — 잠시 뒤 다시")
    except anthropic.APIStatusError as e:
        raise RuntimeError(f"Claude API 오류 {e.status_code}: {getattr(e, 'message', e)}")
    except anthropic.APIConnectionError:
        raise RuntimeError("Claude API 에 연결하지 못했습니다 (인터넷 확인)")
    if resp.stop_reason == "refusal":
        raise RuntimeError("Claude 가 이 요청을 거절했습니다")
    if resp.stop_reason == "max_tokens":
        raise RuntimeError("Claude 답이 길이 한도에서 끊겼습니다")
    text = next((b.text for b in resp.content if b.type == "text"), "")
    return json.loads(text), resp.model


def ollama_strategy(st, hint, data, url, model, adv=None, brief=False):
    msg = {"role": "user", "content": _prompt_text(st, hint, adv, brief)}
    if data:
        msg["images"] = [data]
    body = json.dumps({"model": model, "stream": False, "format": BRIEF_SCHEMA if brief else STRATEGY_SCHEMA, "keep_alive": "30m",
                       "options": {"temperature": 0.2, "num_predict": 400 if brief else 1200},       # 모델을 올려 둔 채로 (다시 올리는 시간 없이)
                       "messages": [{"role": "system", "content": SYSTEM_PROMPT}, msg]}).encode("utf-8")
    req = urllib.request.Request(url.rstrip("/") + "/api/chat", data=body, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=90) as r:
            out = json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"Ollama 오류 {e.code}: {e.read()[:200].decode('utf-8', 'replace')}")
    except (urllib.error.URLError, OSError):
        raise RuntimeError(f"Ollama({url})에 연결하지 못했습니다 — ollama serve 실행 · ollama pull {model}")
    return json.loads(out["message"]["content"]), model


def local_strategy(st, hint, media, data, url, model, adv=None, brief=False, schema=True):
    """이 게임으로 미세조정한 VLM — OpenAI 호환 /chat/completions (llama.cpp llama-server · llamafactory-cli api · vLLM 등)
    schema: response_format json_schema 로 답 모양을 묶는다 (llama-server 는 문법으로 강제 → 깨진 JSON 없음 · 빠름)"""
    content = [{"type": "text", "text": _prompt_text(st, hint, adv, brief)}]
    if data:
        content.insert(0, {"type": "image_url", "image_url": {"url": f"data:{media};base64,{data}"}})
    req_body = {"model": model, "temperature": 0.2, "max_tokens": 400 if brief else 1024,
                "messages": [{"role": "system", "content": SYSTEM_PROMPT}, {"role": "user", "content": content}]}
    if schema:
        req_body["response_format"] = {"type": "json_schema", "json_schema": {"name": "wow_strategy", "strict": True,
                                                                              "schema": BRIEF_SCHEMA if brief else STRATEGY_SCHEMA}}
    body = json.dumps(req_body).encode("utf-8")
    req = urllib.request.Request(url.rstrip("/") + "/chat/completions", data=body, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=90) as r:
            out = json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"학습한 VLM 서버 오류 {e.code}: {e.read()[:200].decode('utf-8', 'replace')}")
    except (urllib.error.URLError, OSError):
        raise RuntimeError(f"학습한 VLM 서버({url})에 연결하지 못했습니다 — llamafactory-cli api 실행 확인")
    text = out["choices"][0]["message"]["content"] or ""
    a, b = text.find("{"), text.rfind("}")
    if a < 0 or b < a:
        raise RuntimeError("학습한 VLM 이 JSON 으로 답하지 않았습니다")
    return json.loads(text[a:b + 1]), out.get("model") or model


def clean_strategy(out, st, hint):
    """VLM 답을 게임 규칙에 맞게 고친다: 못 쓰는 슬롯 · 잘못된 열은 규칙 AI 답으로 바꾼다."""
    notes = []
    res = {"summary": str(out.get("summary") or hint["summary"])[:200],
           "threat": out.get("threat") if out.get("threat") in ("low", "medium", "high") else hint["threat"],
           "plan": [str(x)[:120] for x in (out.get("plan") or [])][:4] or hint["plan"],
           "focus": str(out.get("focus") or hint["focus"])[:120] if st["players"] > 2 else "",
           "observations": [str(x)[:160] for x in (out.get("observations") or [])][:5]}
    c = out.get("cast") if isinstance(out.get("cast"), dict) else {}
    slot = int(_num(c.get("slot"), -1))
    s = next((x for x in st["slots"] if x["slot"] == slot), None)
    if slot >= 0 and (s is None or not s["ready"]):
        notes.append(f"AI 가 고른 슬롯 {slot} 은 지금 못 써서 규칙 AI 의 수로 바꿨습니다")
        res["cast"] = dict(hint["cast"])
    elif s is None:
        res["cast"] = {"slot": -1, "spellId": "", "column": -1, "depth": 0.5, "reason": str(c.get("reason") or "")[:160]}
    else:
        col = _col(c.get("column"), -1) if s["kind"] in NEEDS_AIM else -1
        if s["kind"] in NEEDS_AIM and col == -1:
            col = st["selfColumn"]
        res["cast"] = {"slot": slot, "spellId": s["spellId"], "column": col,
                       "depth": round(max(0.0, min(1.0, _num(c.get("depth"), 0.5))), 2), "reason": str(c.get("reason") or "")[:160]}
    mv = out.get("move") if isinstance(out.get("move"), dict) else {}
    d = int(_num(mv.get("direction")))
    d = max(-1, min(1, d))
    if not 0 <= st["selfColumn"] + d <= 2:
        d = 0
    res["move"] = {"direction": d, "reason": str(mv.get("reason") or "")[:160]}
    res["orders"] = clean_orders(out.get("orders"), st, hint["orders"])
    if notes:
        res["notes"] = notes
    return res


# ─────────────────────────────────────────────────────────────────────────────
# 바깥에서 부르는 함수
# ─────────────────────────────────────────────────────────────────────────────
_LAST_CALL = {}
_LAST_LOCK = threading.Lock()


_LOCAL_SEEN = {}          # 로컬 VLM 주소 → (확인한 때, 켜져 있나)
_LOCAL_TTL = 10.0


def local_url(config):
    return (config.get("ai.local_url") or "http://127.0.0.1:8790/v1").rstrip("/")


def local_available(config):
    """로컬 VLM 서버(llama-server · llamafactory-cli api)가 켜져 있나 — GET /v1/models 가 200 이면. 10초 동안 기억한다."""
    url = local_url(config)
    now = time.time()
    seen = _LOCAL_SEEN.get(url)
    if seen and now - seen[0] < _LOCAL_TTL:
        return seen[1]
    try:
        with urllib.request.urlopen(url + "/models", timeout=0.5) as r:
            ok = r.status == 200
    except (urllib.error.URLError, OSError, ValueError):
        ok = False
    _LOCAL_SEEN[url] = (now, ok)
    return ok


def choose_provider(config, override=None):
    """auto: Claude 키가 있으면 Claude → 로컬 VLM 서버가 켜져 있으면 local (run_server_vlm.bat) → 아니면 규칙"""
    want = (override or config.get("ai.vlm_provider") or "auto").strip().lower()
    if want == "auto":
        ok, _ = claude_available()
        return "claude" if ok else "local" if local_available(config) else "rules"
    return want if want in ("claude", "ollama", "local", "rules") else "rules"


def status(config):
    provider = choose_provider(config)
    ok, why = claude_available()
    return {"provider": provider, "configured": (config.get("ai.vlm_provider") or "auto"),
            "model": config.get("ai.vlm_model") or DEFAULT_MODEL if provider == "claude" else
            (config.get("ai.ollama_model") or "") if provider == "ollama" else
            (config.get("ai.local_model") or "wow-vlm") if provider == "local" else "rules",
            "claude": {"available": ok, "reason": why}, "ollama": {"url": config.get("ai.ollama_url") or ""},
            "local": {"url": local_url(config), "available": local_available(config)},
            "learning": wow_learn.summary(wow_learn.load_policy())}


def board_enabled(config):
    return str((config or {}).get("ai.board_image", "true")).lower() != "false"


def strategy(raw_state, skills, config=None, image=None, caller=None, provider=None, brief=False):
    """상황(+그림) → 전략 + 작전(orders). VLM 이 실패하면 힌트(Unity 두뇌 · 규칙 AI · 학습) 답에 fallbackReason 을 붙여 돌려준다
    (예외를 던지지 않음). 그림이 잘못된 경우만 ValueError. 그림이 없으면 전장 지도를 그려 VLM 에 넣는다.
    brief: 봇 요청 — VLM 은 작전 · 한 수만 짧게 (빨리) 답하고 나머지는 힌트로 채운다."""
    config = config or {}
    t0 = time.time()
    st = normalize_state(raw_state, skills)
    min_n = int(_num(config.get("ai.learn_min_samples"), 5))
    adv = wow_learn.advise(st, min_n=min_n)
    hint = make_hint(st, adv, min_n)
    media, data = parse_image(image)
    image_kind = "screen" if data else ""
    chosen = choose_provider(config, provider)

    # 유료 API 를 너무 자주 부르지 않게 (같은 플레이어) — 그 사이는 규칙 AI
    min_gap = _num(config.get("ai.vlm_min_interval"), 3)
    if chosen != "rules" and caller is not None:
        with _LAST_LOCK:
            last = _LAST_CALL.get(caller, 0)
            if t0 - last < min_gap:
                chosen, wait = "rules", min_gap - (t0 - last)
            else:
                _LAST_CALL[caller] = t0
                wait = 0
    else:
        wait = 0

    result, model, why = hint, "rules", ""
    if wait:
        why = f"VLM 은 {min_gap:g}초에 한 번 — {wait:.1f}초 뒤 다시"
    elif chosen in ("claude", "ollama", "local"):
        if not data and board_enabled(config):
            media, data, image_kind = "image/png", base64.b64encode(wow_board.render(st)).decode("ascii"), "board"
        try:
            if chosen == "claude":
                out, model = claude_strategy(st, hint, media, data, config.get("ai.vlm_model") or DEFAULT_MODEL,
                                             (config.get("ai.vlm_effort") or "low").lower(), adv, brief)
            elif chosen == "local":
                out, model = local_strategy(st, hint, media, data, config.get("ai.local_url") or "http://127.0.0.1:8790/v1",
                                            config.get("ai.local_model") or "wow-vlm", adv, brief,
                                            str(config.get("ai.local_schema", "true")).lower() != "false")
            else:
                out, model = ollama_strategy(st, hint, data, config.get("ai.ollama_url") or "http://127.0.0.1:11434",
                                             config.get("ai.ollama_model") or "qwen2.5vl:7b", adv, brief)
            result = clean_strategy(out, st, hint)
        except (RuntimeError, ValueError, KeyError, json.JSONDecodeError) as e:
            chosen, model, why = "rules", "rules", str(e)
        if caller is not None:       # 간격은 앞 요청이 끝난 때부터 잰다 (느린 답이 이어서 몰리지 않게)
            with _LAST_LOCK:
                _LAST_CALL[caller] = time.time()
    else:
        chosen = "rules"
    result = dict(result)
    result.update({"source": chosen, "model": model, "usedImage": bool(data) and chosen != "rules",
                   "imageKind": image_kind if chosen != "rules" else "", "botHinted": bool(st["botPick"]),
                   "latencyMs": int((time.time() - t0) * 1000), "rulesHint": hint if chosen != "rules" else None,
                   "learned": {"note": wow_learn.describe(adv), "situation": adv["situation"], "games": adv["games"],
                               "ranked": adv["ranked"][:3], "confident": adv["confident"]} if adv else None,
                   "state": {"selfColumn": st["selfColumn"], "readySlots": [s["slot"] for s in st["slots"] if s["ready"]],
                             "players": st["players"]}})
    if why:
        result["fallbackReason"] = why
    return result


if __name__ == "__main__":     # 간단 확인: python wow_ai.py
    import sys
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    demo_skills = [(3, "S03", "파이어볼", "Fireball", "Normal", "Area", "area", 5, 14, 0, "", "소범위 집중 즉발 피해"),
                   (4, "S04", "화살 세례", "ArrowRain", "Normal", "Area", "area", 5, 8, 0, "", "광범위 다수 분산 피해"),
                   (8, "S08", "방어 쉴드", "Shield", "Normal", "Self", "buff", 5, 0, 0, "", "피해 흡수 쉴드"),
                   (101, "H01", "프리즈", "Freeze", "HighPower", "Global", "global", 20, 0, 0, "", "적 전체 정지")]
    demo = {"SelfHp": 820, "SelfColumn": 1, "MatchTimeSeconds": 75, "SelfSlotReadyMask": 0b1011,
            "deck": ["S03", "S04", "S08", "H01"], "IncomingProjectileCount": 1, "NearestIncomingProjectileColumn": 1,
            "NearestIncomingProjectileEta": 0.8, "lanes": [{"allies": 1, "enemies": 4}, {"allies": 2, "enemies": 1}, {"allies": 0, "enemies": 0}]}
    print(json.dumps(strategy(demo, demo_skills, {"ai.vlm_provider": "rules"}), ensure_ascii=False, indent=2))
