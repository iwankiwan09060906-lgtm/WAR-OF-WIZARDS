# -*- coding: utf-8 -*-
"""
War of Wizards — AI 가 게임을 배우는 곳 (경험 → 학습 → 전략)

    ① 모으기   경기 중 상황 · 화면 · 행동 · 즉시 보상을 ai_experiences 표에 쌓는다 (wow_server.py 가 저장)
               — 웹 전투 화면의 시전, Unity · 봇이 보내는 관찰(/api/ai/experience), AI 코치가 추천한 수
    ② 이름표   판이 끝나 결과(WIN · LOSE · 등수)가 들어오면 그 판의 모든 행동에 결과를 붙인다
    ③ 배우기   train(): 상황을 몇 갈래(경기 시간 · 내 HP · 레인 압박 · 상대 타워 · 1:1/개인전 · 투사체)로 나누고,
               갈래마다 "어떤 스킬을 썼을 때 판을 이겼나"를 평균 내어 learned_policy.json 으로 저장한다
               (GPU 없이 바로 되는 학습 — 판이 쌓일수록 정확해진다)
    ④ 쓰기     advise(): 지금 상황과 같은 갈래에서 이긴 스킬 순위 + 비슷했던 과거 장면(예시)을
               규칙 AI 와 VLM 프롬프트에 넣는다 (wow_ai.py)
    ⑤ 내보내기 export_dataset(): 이긴 판의 장면을 VLM 미세조정(LoRA)용 데이터셋으로 — Docs/Backend/AiLearning.md

외부 패키지 없음 (표준 라이브러리만).
"""

import json
import math
import os
import shutil
import threading
import time

HERE = os.path.dirname(os.path.abspath(__file__))
DATA_DIR = os.environ.get("WOW_AI_DATA") or os.path.join(HERE, "ai_data")
POLICY_FILE = os.path.join(DATA_DIR, "learned_policy.json")
FRAMES_DIR = os.path.join(DATA_DIR, "frames")
COLUMN_KO = ["왼쪽", "가운데", "오른쪽"]
PRIOR_WEIGHT = 3.0           # 표본이 적은 스킬은 평균(0)쪽으로 당긴다 (베이즈 평활)
EXAMPLES_PER_KEY = 4

_POLICY = None
_POLICY_MTIME = 0.0
_LOCK = threading.Lock()


# ─────────────────────────────────────────────────────────────────────────────
# 상황 → 갈래 (학습 단위)
# ─────────────────────────────────────────────────────────────────────────────
def situation(st):
    """normalize_state 결과 → 갈래 이름 + 특징. 레인 정보가 없으면 'lane?' 로 따로 배운다."""
    t = st.get("time", 0) or 0
    phase = "early" if t < 60 else "mid" if t < 240 else "late"
    hp = (st.get("selfHp", 1000) or 0) / (st.get("selfMaxHp", 1000) or 1000)
    hp_b = "hpLow" if hp < 0.35 else "hpMid" if hp < 0.7 else "hpHigh"
    lanes = st.get("lanes") or []
    if lanes:
        most = max(ln.get("enemies", 0) for ln in lanes)
        lane_b = "calm" if most == 0 else "some" if most < 3 else "heavy"
    else:
        lane_b = "lane?"
    opps = st.get("opponents") or []
    exposed = (not st.get("opponentTowerAlive", True)) or any(not o.get("towerAlive", True) for o in opps)
    inc = st.get("incoming") or {}
    dodge = "dodge" if inc.get("count") and inc.get("column") == st.get("selfColumn") else "safe"
    mode = "ffa" if (st.get("players") or 2) > 2 else "1v1"
    key = "|".join((mode, phase, hp_b, lane_b, "open" if exposed else "closed", dodge))
    return key, {"mode": mode, "phase": phase, "hp": hp_b, "lanes": lane_b, "enemyTower": "down" if exposed else "up", "incoming": dodge}


def outcome_score(outcome, place=0, players=2):
    """판 결과 → -1 ~ +1 (개인전은 등수로: 4명 1등 +1 · 2등 +0.33 · 3등 -0.33 · 4등 -1)"""
    if players and players > 2 and place:
        return (players + 1 - 2 * place) / (players - 1)
    return {"WIN": 1.0, "LOSE": -1.0}.get((outcome or "").upper(), 0.0)


def value_of(row):
    """행동 하나의 가치 = 판 결과 + 즉시 보상(준 피해 - 받은 피해 등, 있으면) 조금"""
    v = outcome_score(row["outcome"], row["place"], row["players"])
    r = row["reward"] or 0.0
    return v + 0.5 * math.tanh(r / 50.0)


# ─────────────────────────────────────────────────────────────────────────────
# ③ 배우기
# ─────────────────────────────────────────────────────────────────────────────
def _acc(table, key, value, win):
    a = table.setdefault(key, {"n": 0, "sum": 0.0, "wins": 0})
    a["n"] += 1
    a["sum"] += value
    a["wins"] += 1 if win else 0


def _finish(a, prior=0.0):
    n = a["n"]
    return {"n": n, "value": round((a["sum"] + prior * PRIOR_WEIGHT) / (n + PRIOR_WEIGHT), 3),
            "winRate": round(a["wins"] / n, 3) if n else 0.0}


def train(conn):
    """이름표가 붙은 행동으로 정책을 다시 만든다 → learned_policy.json. 요약을 돌려준다."""
    rows = conn.execute("""SELECT * FROM ai_experiences WHERE outcome != '' AND kind IN ('action', 'step')
                           ORDER BY id""").fetchall()
    spells, buckets, follow, examples = {}, {}, {}, {}
    games = set()
    for r in rows:
        try:
            st = json.loads(r["state_json"] or "{}")
            act = json.loads(r["action_json"] or "{}")
        except ValueError:
            continue
        spell = act.get("spellId") or ""
        if not spell:
            continue
        games.add(r["game_id"] or f"m{r['match_id']}")
        v = value_of(r)
        win = v > 0
        key, feat = situation(st)
        _acc(spells, spell, v, win)
        _acc(buckets.setdefault(key, {}), spell, v, win)
        _acc(follow, "followed" if act.get("followedAdvice") else "own", v, win)
        if v > 0:      # 이긴 판의 장면만 예시로 남긴다 (가치 높은 순으로 몇 개)
            ex = examples.setdefault(key, [])
            ex.append({"value": round(v, 3), "spellId": spell, "column": act.get("column", -1), "move": act.get("move", 0),
                       "hp": round((st.get("selfHp", 1000) or 0) / (st.get("selfMaxHp", 1000) or 1000), 2),
                       "lanes": [[ln.get("allies", 0), ln.get("enemies", 0)] for ln in (st.get("lanes") or [])],
                       "time": st.get("time", 0), "outcome": r["outcome"], "place": r["place"], "image": bool(r["image_path"])})
            ex.sort(key=lambda e: -e["value"])
            del ex[EXAMPLES_PER_KEY:]
    spell_view = {s: _finish(a) for s, a in spells.items()}
    policy = {
        "version": 1, "trainedAt": time.strftime("%Y-%m-%d %H:%M:%S"), "samples": len(rows), "games": len(games),
        "spells": spell_view,
        "buckets": {k: {s: _finish(a, spell_view[s]["value"]) for s, a in t.items()} for k, t in buckets.items()},
        "follow": {k: _finish(a) for k, a in follow.items()},
        "examples": examples,
    }
    os.makedirs(DATA_DIR, exist_ok=True)
    tmp = POLICY_FILE + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(policy, f, ensure_ascii=False, indent=1)
    os.replace(tmp, POLICY_FILE)
    global _POLICY, _POLICY_MTIME
    with _LOCK:
        _POLICY, _POLICY_MTIME = policy, os.path.getmtime(POLICY_FILE)
    return summary(policy)


def load_policy():
    """learned_policy.json (파일이 바뀌면 다시 읽는다). 아직 학습 안 했으면 None"""
    global _POLICY, _POLICY_MTIME
    try:
        m = os.path.getmtime(POLICY_FILE)
    except OSError:
        return None
    with _LOCK:
        if _POLICY is None or m != _POLICY_MTIME:
            try:
                with open(POLICY_FILE, encoding="utf-8") as f:
                    _POLICY, _POLICY_MTIME = json.load(f), m
            except (OSError, ValueError):
                return None
        return _POLICY


def summary(policy):
    if not policy:
        return {"trained": False, "samples": 0, "games": 0}
    top = sorted(((s, v) for s, v in policy["spells"].items()), key=lambda kv: -kv[1]["value"])
    return {"trained": True, "trainedAt": policy["trainedAt"], "samples": policy["samples"], "games": policy["games"],
            "situations": len(policy["buckets"]), "follow": policy.get("follow", {}),
            "topSpells": [{"spellId": s, **v} for s, v in top[:5]],
            "worstSpells": [{"spellId": s, **v} for s, v in top[-3:]] if len(top) > 5 else []}


# ─────────────────────────────────────────────────────────────────────────────
# ④ 쓰기 — 지금 상황에서 배운 것
# ─────────────────────────────────────────────────────────────────────────────
def advise(st, policy=None, min_n=5):
    """지금 상황 갈래에서 쓸 수 있는 스킬의 학습 가치 순위 + 이긴 판 예시. 배운 게 없으면 None"""
    policy = policy or load_policy()
    if not policy:
        return None
    key, feat = situation(st)
    table = policy["buckets"].get(key) or {}
    ranked = []
    for s in st.get("slots") or []:
        if not s.get("ready"):
            continue
        b = table.get(s["spellId"])
        g = policy["spells"].get(s["spellId"])
        if b:
            ranked.append({"slot": s["slot"], "spellId": s["spellId"], "value": b["value"], "n": b["n"], "winRate": b["winRate"], "scope": "situation"})
        elif g:
            ranked.append({"slot": s["slot"], "spellId": s["spellId"], "value": g["value"], "n": g["n"], "winRate": g["winRate"], "scope": "overall"})
    ranked.sort(key=lambda x: (-(x["n"] >= min_n), -x["value"]))
    return {"situation": key, "features": feat, "ranked": ranked[:5], "examples": (policy.get("examples") or {}).get(key, [])[:3],
            "samples": policy["samples"], "games": policy["games"], "confident": bool(ranked and ranked[0]["n"] >= min_n)}


def describe(adv):
    """학습 결과 한 줄 (한국어)"""
    if not adv or not adv["ranked"]:
        return ""
    b = adv["ranked"][0]
    where = "이 상황" if b["scope"] == "situation" else "전체"
    return f"학습({adv['games']}판): {where}에서 {b['spellId']} 승률 {round(b['winRate'] * 100)}% · {b['n']}번"


# ─────────────────────────────────────────────────────────────────────────────
# ⑤ VLM 미세조정 데이터셋 (LLaMA-Factory sharegpt 형식, Qwen2.5-VL 등)
# ─────────────────────────────────────────────────────────────────────────────
def _target(st, act, row):
    """이긴 판의 실제 행동 → VLM 이 배워야 할 답 (wow_ai.STRATEGY_SCHEMA 모양)"""
    col = act.get("column", -1)
    if col is None:
        col = -1
    slot = next((s["slot"] for s in st.get("slots") or [] if s["spellId"] == act.get("spellId")), act.get("slot", -1))
    name = next((s["name"] for s in st.get("slots") or [] if s["spellId"] == act.get("spellId")), act.get("spellId", ""))
    key, feat = situation(st)
    move = int(act.get("move") or 0)
    place = f" {row['place']}등" if row["place"] else ""
    return {"summary": f"{feat['phase']} · {feat['hp']} · 레인 {feat['lanes']} · 상대 타워 {feat['enemyTower']}",
            "threat": "high" if feat["hp"] == "hpLow" or feat["incoming"] == "dodge" else "medium" if feat["lanes"] == "heavy" else "low",
            "plan": [f"{act.get('spellId')} {name}" + (f" → {COLUMN_KO[col]} 열" if 0 <= col <= 2 else "")],
            "cast": {"slot": slot, "spellId": act.get("spellId", ""), "column": col, "depth": act.get("depth", 0.5),
                     "reason": f"이 상황에서 이긴 판의 수 ({row['outcome']}{place})"},
            "move": {"direction": move, "reason": "피하기" if move else ""},
            "focus": act.get("focus", "") if (st.get("players") or 2) > 2 else "", "observations": []}


def export_dataset(conn, system_prompt, prompt_text, out_dir=None, min_value=0.01):
    """이긴 판(가치 > 0)의 장면 → out_dir/wow_vlm_sft.jsonl + dataset_info.json (+ 그림 복사). 줄 수를 돌려준다."""
    out_dir = out_dir or os.path.join(DATA_DIR, "dataset")
    os.makedirs(os.path.join(out_dir, "frames"), exist_ok=True)
    rows = conn.execute("""SELECT * FROM ai_experiences WHERE outcome != '' AND kind IN ('action', 'step') ORDER BY id""").fetchall()
    n = with_image = 0
    with open(os.path.join(out_dir, "wow_vlm_sft.jsonl"), "w", encoding="utf-8") as f:
        for r in rows:
            if value_of(r) < min_value:
                continue
            try:
                st = json.loads(r["state_json"] or "{}")
                act = json.loads(r["action_json"] or "{}")
            except ValueError:
                continue
            if not act.get("spellId"):
                continue
            user = prompt_text(st)      # 실제 전략 요청과 같은 모양 (규칙 AI 제안 포함) — 다르면 모델이 제안을 그대로 베낀다
            line = {"messages": [{"role": "system", "content": system_prompt},
                                 {"role": "user", "content": user},
                                 {"role": "assistant", "content": json.dumps(_target(st, act, r), ensure_ascii=False)}]}
            src = r["image_path"]
            if src and os.path.exists(os.path.join(DATA_DIR, src)):
                name = os.path.basename(src)
                shutil.copyfile(os.path.join(DATA_DIR, src), os.path.join(out_dir, "frames", name))
                line["messages"][1]["content"] = "<image>" + user
                line["images"] = [f"frames/{name}"]
                with_image += 1
            f.write(json.dumps(line, ensure_ascii=False) + "\n")
            n += 1
    info = {"wow_vlm": {"file_name": "wow_vlm_sft.jsonl", "formatting": "sharegpt",
                        "columns": {"messages": "messages", "images": "images"},
                        "tags": {"role_tag": "role", "content_tag": "content", "user_tag": "user",
                                 "assistant_tag": "assistant", "system_tag": "system"}}}
    with open(os.path.join(out_dir, "dataset_info.json"), "w", encoding="utf-8") as f:
        json.dump(info, f, ensure_ascii=False, indent=2)
    write_finetune_configs(out_dir)
    return {"lines": n, "withImage": with_image, "dir": out_dir}


def write_finetune_configs(out_dir, run_name="wow-vlm-lora"):
    """학습 · 서빙 설정을 데이터셋 폴더에. 학습 결과(LoRA)는 데이터셋 폴더가 아니라 Backend/ai_finetune/saves/<run_name> 에
    (데이터셋을 다시 만들 때 지워지지 않게 — 절대 경로)."""
    saves = os.path.join(SAVES_DIR, run_name).replace("\\", "/")
    for name, text in (("train_qwen2_5vl_lora.yaml", TRAIN_YAML), ("api_qwen2_5vl_lora.yaml", API_YAML)):
        with open(os.path.join(out_dir, name), "w", encoding="utf-8") as f:
            f.write(text.replace("saves/wow-vlm-lora", saves))


SAVES_DIR = os.path.join(HERE, "ai_finetune", "saves")

# LLaMA-Factory 설정 (데이터셋 폴더에서: llamafactory-cli train train_qwen2_5vl_lora.yaml — Backend/ai_finetune/run_finetune.bat)
# RTX 4060 노트북(VRAM 8GB)에서 확인: 3B + 4bit QLoRA + 배치 1 → 47장면 × 2번 = 6분 30초, VRAM 약 7.9GB.
TRAIN_YAML = """### War of Wizards VLM 미세조정 (LLaMA-Factory) — llamafactory-cli train train_qwen2_5vl_lora.yaml
model_name_or_path: Qwen/Qwen2.5-VL-3B-Instruct
image_max_pixels: 262144
trust_remote_code: true

stage: sft
do_train: true
finetuning_type: lora
lora_rank: 8
lora_target: all
quantization_bit: 4

dataset: wow_vlm
dataset_dir: .
template: qwen2_vl
cutoff_len: 3072            # 한 장면 = 글자 약 1950 + 그림 약 260 토큰 (2048 이면 답이 잘린다)
overwrite_cache: true
preprocessing_num_workers: 1
dataloader_num_workers: 0

output_dir: saves/wow-vlm-lora
logging_steps: 10
save_steps: 200
plot_loss: true
overwrite_output_dir: true

per_device_train_batch_size: 1
gradient_accumulation_steps: 8
learning_rate: 1.0e-4
num_train_epochs: 3.0
lr_scheduler_type: cosine
warmup_ratio: 0.1
bf16: true
report_to: none
"""

# 학습한 모델을 OpenAI 호환 서버로 (set API_HOST=127.0.0.1, set API_PORT=8790 → llamafactory-cli api api_qwen2_5vl_lora.yaml)
# 그다음 관리자 콘솔: ai.vlm_provider = local, ai.local_url = http://127.0.0.1:8790/v1
API_YAML = """### War of Wizards 학습한 VLM 서버 — set API_HOST=127.0.0.1 && set API_PORT=8790 && llamafactory-cli api api_qwen2_5vl_lora.yaml
model_name_or_path: Qwen/Qwen2.5-VL-3B-Instruct
adapter_name_or_path: saves/wow-vlm-lora
template: qwen2_vl
finetuning_type: lora
quantization_bit: 4         # VRAM 8GB: 그대로(bf16 7.5GB) 올리면 그림까지 못 넣는다
image_max_pixels: 262144
infer_backend: huggingface
trust_remote_code: true
"""
