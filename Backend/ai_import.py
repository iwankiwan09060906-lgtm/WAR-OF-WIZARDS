# -*- coding: utf-8 -*-
"""
War of Wizards — 다른 서버 DB 의 AI 경험(판 결과 이름표 포함)을 이 서버로 가져와 다시 학습한다.

BotLab 하이브리드 봇 대전(`dotnet run -- vlm`)은 보통 시험용 서버(따로 띄운 DB)에 쌓인다. 그 판들을
진짜 서버 DB(Backend/wow_database.sqlite, WOW_DB_FILE 로 바꿀 수 있음)로 옮기고 learned_policy.json 을 다시 만든다.
켜져 있는 게임 서버는 파일이 바뀐 것을 보고 다음 전략 요청부터 바로 쓴다 (다시 켤 필요 없음).

    python ai_import.py <가져올 DB 파일> [<가져올 DB 의 ai_data 폴더 — 화면 사진(frames)을 같이 옮길 때>]

같은 경험(game_id · caller · created_at 이 같은 줄)은 다시 넣지 않으므로 여러 번 돌려도 된다.
"""

import os
import shutil
import sqlite3
import sys

import wow_learn
from wow_server import DB_FILE, init_db

COLUMNS = ("game_id", "match_id", "caller", "player_id", "kind", "source", "state_json", "action_json", "advice_json",
           "image_path", "reward", "outcome", "place", "players", "created_at")


def import_experiences(src_db, src_data=None):
    init_db()                                        # 처음이면 표를 만든다
    src = sqlite3.connect(src_db)
    src.row_factory = sqlite3.Row
    dst = sqlite3.connect(DB_FILE, timeout=10)
    dst.row_factory = sqlite3.Row
    seen = {(r["game_id"], r["caller"], r["created_at"]) for r in
            dst.execute("SELECT game_id, caller, created_at FROM ai_experiences")}
    added = frames = 0
    for r in src.execute(f"SELECT {', '.join(COLUMNS)} FROM ai_experiences ORDER BY id"):
        if (r["game_id"], r["caller"], r["created_at"]) in seen:
            continue
        row = dict(r)
        if row["image_path"] and src_data and os.path.exists(os.path.join(src_data, row["image_path"])):
            name = f"imp_{os.path.basename(row['image_path'])}"
            os.makedirs(wow_learn.FRAMES_DIR, exist_ok=True)
            shutil.copyfile(os.path.join(src_data, row["image_path"]), os.path.join(wow_learn.FRAMES_DIR, name))
            row["image_path"] = f"frames/{name}"
            frames += 1
        elif row["image_path"]:
            row["image_path"] = ""
        dst.execute(f"INSERT INTO ai_experiences ({', '.join(COLUMNS)}) VALUES ({', '.join('?' * len(COLUMNS))})",
                    tuple(row[c] for c in COLUMNS))
        added += 1
    dst.commit()
    summary = wow_learn.train(dst)
    dst.close()
    src.close()
    return added, frames, summary


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    if len(sys.argv) < 2 or not os.path.exists(sys.argv[1]):
        print(__doc__)
        sys.exit(1)
    added, frames, s = import_experiences(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else None)
    print(f"[*] 가져온 경험 {added}줄 (화면 사진 {frames}장) → {DB_FILE}")
    print(f"[*] 다시 학습: 판 {s.get('games')} · 경험 {s.get('samples')} · 상황 갈래 {s.get('situations')} → {wow_learn.POLICY_FILE}")
    spells = sorted(((k, v) for k, v in (wow_learn.load_policy() or {}).get("spells", {}).items() if v["n"] >= 20),
                    key=lambda kv: -kv[1]["value"])           # 20번 넘게 쓴 스킬만 (표본이 적으면 우연)
    for k, v in spells:
        print(f"    {k}: 승률 {round(v['winRate'] * 100)}% · {v['n']}번 · 가치 {v['value']:+.2f}")
