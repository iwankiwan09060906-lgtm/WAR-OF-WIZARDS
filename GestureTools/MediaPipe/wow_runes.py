# -*- coding: utf-8 -*-
"""
War of Wizards — 19종 룬 인식기 ($P Point-Cloud Recognizer, 여러 획 지원)

* 파이썬 표준 라이브러리만 사용한다 (MediaPipe 트래커 · Backend/wow_server.py 가 함께 쓴다).
* 룬 모양 = 팀이 정한 스킬 그림(왼쪽 룬 그림 → 오른쪽 마법진). 웹 화면의
  Backend/static/assets/skills/rune_line/*.png 를 192px 칸 위에서 그대로 따라 그린 좌표다.
* 입력 좌표계: x → 오른쪽, y → 위쪽. 크기와 위치는 상관없다 (인식 전에 정규화한다).
* 여러 획: recognize([[획1 점들], [획2 점들], ...]) 처럼 획 목록을 넘기면 된다. 한 획이면 점 목록 그대로.
* $P 는 획 순서 · 방향 · 시작점과 무관하고, 회전은 구별한다.
* 마스터 프롬프트 §9: "사용 가능한 슬롯의 룬만 후보" + "1·2순위 점수 차가 작으면 실패".
  → recognize(points, candidates=[...]) 로 후보를 좁힌다.

명령줄:
    python wow_runes.py --selftest           # 19종 룬 혼동 검사 (노이즈 섞은 가짜 손그림)
    python wow_runes.py --sheet runes.png    # 룬 안내 그림 만들기 (OpenCV 필요)
"""

import math
import random
import sys

N_POINTS = 40            # 리샘플 점 개수 (여러 획 그림이라 예전 32 → 40)
MIN_SCORE = 0.77         # 이 점수 미만이면 실패 (selftest: 휘갈긴 선 대부분이 0.77 미만)
MIN_GAP = 0.03           # 1·2순위 점수 차가 이보다 작으면 실패 (오발동 방지)
MIN_PATH = 0.06          # 화면 폭 대비 궤적 길이가 이보다 짧으면 실패 (손 떨림)

# 스킬 이름 = 팀 스킬 그림 문서 (S01 연막 ~ H06 소환탑). 두 번째 값 = 룬 그림 설명.
SPELLS = {
    "S01": ("연막", "눈"),
    "S02": ("독 장판", "독 문양"),
    "S03": ("파이어볼", "왕관"),
    "S04": ("화살 세례", "세 갈래 화살"),
    "S05": ("토네이도", "소용돌이"),
    "S06": ("번개", "번개"),
    "S07": ("총난사", "가로줄 5개"),
    "S08": ("방어 쉴드", "방패"),
    "S09": ("타워 버프", "#"),
    "S10": ("집중", "위쪽 화살표"),
    "S11": ("도끼 전사", "X"),
    "S12": ("드래곤", "육각별"),
    "S13": ("터렛", "I"),
    "H01": ("프리즈", "원 안 모래시계"),
    "H02": ("레이저 빔", "별표 *"),
    "H03": ("흡혈", "심장 박동 하트"),
    "H04": ("반사 결계", "원 + 위 화살표"),
    "H05": ("분노", "화난 얼굴"),
    "H06": ("소환탑", "집"),
}


# 계약서 SpellCastRequest.SpellId: S01~S13 → 1~13, H01~H06 → 101~106
def spell_number(code):
    return int(code[1:]) + (100 if code[0] == "H" else 0)


# ─────────────────────────────────────────────────────────────────────────────
# 룬 모양 — 192px 칸 좌표(x 오른쪽, y 아래쪽)로 적고 (x 오른쪽, y 위쪽, -1~1) 로 바꾼다
# ─────────────────────────────────────────────────────────────────────────────
def _tile(pts):
    return [(x / 96.0 - 1.0, 1.0 - y / 96.0) for x, y in pts]


def _arc(cx, cy, r, deg_from, deg_to, steps=24, ry=None):
    """칸 좌표 원호. 각도는 화면 기준(0 = 오른쪽, 90 = 위쪽)."""
    ry = r if ry is None else ry
    pts = []
    for i in range(steps + 1):
        a = math.radians(deg_from + (deg_to - deg_from) * i / steps)
        pts.append((cx + r * math.cos(a), cy - ry * math.sin(a)))
    return pts


def _circle(cx, cy, r, steps=36):
    return _arc(cx, cy, r, 90, 450, steps)


def _quad(p0, c, p1, steps=16):
    out = []
    for i in range(steps + 1):
        t = i / steps
        out.append(((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * c[0] + t * t * p1[0],
                    (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * c[1] + t * t * p1[1]))
    return out


def _spiral(mirror=False):
    pts = []
    for i in range(90):
        t = i / 89
        a = math.radians(-20 + 810 * t)        # 가운데에서 바깥으로 2바퀴 + 1/4
        r = 6 + 66 * t
        x = r * math.cos(a)
        pts.append((98 + (-x if mirror else x), 96 - r * math.sin(a)))
    return pts


def _heart():
    pts = []
    for i in range(49):
        t = math.pi * 2 * i / 48
        x = 16 * math.sin(t) ** 3
        y = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        pts.append((96 + x * 3.4, 92 - y * 3.4))
    return pts


# 룬 하나 = 모양 1개 이상(사람이 실제로 그리는 다른 방식) · 모양 하나 = 획 목록
_SHAPES_TILE = {
    "S01": [[_quad((20, 98), (98, 6), (180, 98)), _quad((20, 98), (98, 194), (180, 98)), _circle(98, 98, 24, 24)]],
    "S02": [[[(46, 21), (46, 41), (58, 66), (81, 76)], [(58, 68), (16, 76)],
             [(81, 76), (106, 76), (94, 96), (81, 76)],
             [(106, 76), (128, 66), (141, 41), (144, 21)], [(128, 68), (174, 78)],
             [(94, 96), (94, 134)], [(94, 134), (81, 151), (66, 168)], [(94, 134), (108, 151), (121, 168)]]],
    "S03": [[[(54, 166), (24, 60), (66, 108), (94, 56), (122, 108), (168, 60), (140, 166), (54, 166)]]],
    "S04": [[[(16, 40), (94, 165), (172, 40)], [(94, 18), (94, 172)]]],
    "S05": [[_spiral()], [_spiral(mirror=True)]],                                  # 반대로 감은 것도 인정
    "S06": [[[(96, 14), (56, 110), (92, 110), (96, 178), (136, 82), (100, 82), (96, 14)]],
            [[(120, 14), (60, 104), (132, 96), (72, 180)]]],                       # 한 줄 번개
    "S07": [[[(16, y), (176, y)] for y in (24, 60, 96, 132, 168)]],
    "S08": [[[(24, 28), (96, 44), (168, 28), (168, 130), (96, 170), (24, 130), (24, 28)]]],
    "S09": [[[(68, 26), (58, 166)], [(130, 26), (121, 166)], [(26, 62), (174, 62)], [(14, 128), (166, 128)]]],
    "S10": [[[(94, 18), (36, 88), (68, 96), (68, 176), (124, 176), (124, 96), (152, 88), (94, 18)]],
            [[(96, 176), (96, 20)], [(40, 80), (96, 20), (152, 80)]]],              # 선 + 화살촉
    "S11": [[[(96, 60), (150, 18), (174, 42), (132, 96), (174, 150), (150, 174), (96, 132),
              (42, 174), (18, 150), (60, 96), (18, 42), (42, 18), (96, 60)]],
            [[(24, 24), (168, 168)], [(168, 24), (24, 168)]]],                      # 두 줄 X
    "S12": [[[(96, 14), (172, 140), (20, 140), (96, 14)], [(96, 178), (20, 60), (172, 60), (96, 178)]]],
    "S13": [[[(51, 16), (141, 16), (141, 41), (111, 44), (111, 126), (141, 131), (141, 174),
              (51, 174), (51, 131), (74, 126), (74, 44), (51, 41), (51, 16)]],
            [[(50, 20), (142, 20)], [(96, 20), (96, 172)], [(50, 172), (142, 172)]]],  # 세 줄 I
    "H01": [[_circle(96, 96, 78), [(56, 34), (136, 34)], [(56, 156), (136, 156)],
             [(60, 38), (132, 152)], [(132, 38), (60, 152)]]],
    "H02": [[[(18, 96), (176, 96)], [(96, 14), (96, 176)], [(40, 40), (152, 152)], [(152, 40), (40, 152)]]],
    "H03": [[_heart(), [(16, 98), (70, 98), (84, 84), (96, 108), (110, 96), (176, 98)]]],
    "H04": [[_circle(96, 104, 48), [(96, 16), (96, 176)], [(78, 36), (96, 16), (114, 36)]]],
    "H05": [[_circle(96, 93, 73), [(48, 58), (84, 74)], [(108, 74), (142, 58)],
             [(80, 76), (80, 88)], [(112, 76), (112, 88)], _quad((74, 142), (96, 106), (118, 142), 10)]],
    "H06": [[[(14, 90), (96, 28), (178, 90)], [(40, 90), (40, 162), (152, 162), (152, 90), (40, 90)]]],
}

RUNE_SHAPES = {code: [[_tile(stroke) for stroke in shape] for shape in shapes]
               for code, shapes in _SHAPES_TILE.items()}


# ─────────────────────────────────────────────────────────────────────────────
# 입력 정리: 점 목록 / 획 목록 / {"x","y"} 딕셔너리 모두 받는다
# ─────────────────────────────────────────────────────────────────────────────
def _pt(p):
    return (float(p["x"]), float(p["y"])) if isinstance(p, dict) else (float(p[0]), float(p[1]))


def _is_point(p):
    return isinstance(p, dict) or (isinstance(p, (list, tuple)) and len(p) >= 2
                                   and isinstance(p[0], (int, float)))


def to_strokes(points):
    """[(x,y),...] → [[(x,y),...]] ,  [[(x,y),...], ...] → 그대로 (빈 획은 뺀다)."""
    if not points:
        return []
    if _is_point(points[0]):
        return [[_pt(p) for p in points]]
    return [[_pt(p) for p in stroke] for stroke in points if stroke]


# ─────────────────────────────────────────────────────────────────────────────
# $P 알고리즘 (Vatavu · Anthony · Wobbrock 2012)
# ─────────────────────────────────────────────────────────────────────────────
def path_length(strokes):
    return sum(math.dist(s[i - 1], s[i]) for s in strokes for i in range(1, len(s)))


def resample(strokes, n=N_POINTS):
    """획 사이 빈 공간은 길이로 치지 않고, 전체 획 길이를 n-1 등분한 간격으로 점을 다시 찍는다."""
    strokes = [list(s) for s in strokes if s]
    if not strokes:
        return []
    total = path_length(strokes)
    if total == 0:
        return [strokes[0][0]] * n
    step = total / (n - 1)
    out = [strokes[0][0]]
    acc = 0.0
    for s in strokes:
        i = 1
        while i < len(s):
            d = math.dist(s[i - 1], s[i])
            if d > 0 and acc + d >= step:
                t = (step - acc) / d
                q = (s[i - 1][0] + t * (s[i][0] - s[i - 1][0]), s[i - 1][1] + t * (s[i][1] - s[i - 1][1]))
                out.append(q)
                s.insert(i, q)
                acc = 0.0
            else:
                acc += d
            i += 1
    while len(out) < n:
        out.append(strokes[-1][-1])
    return out[:n]


def normalize(strokes, n=N_POINTS):
    pts = resample(strokes, n)
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    size = max(max(xs) - min(xs), max(ys) - min(ys)) or 1.0
    pts = [((x - min(xs)) / size, (y - min(ys)) / size) for x, y in pts]
    cx = sum(p[0] for p in pts) / n
    cy = sum(p[1] for p in pts) / n
    return [(x - cx, y - cy) for x, y in pts]


def _cloud_distance(a, b, start, limit):
    n = len(a)
    matched = [False] * n
    total = 0.0
    i = start
    while True:
        best = float("inf")
        index = -1
        ax, ay = a[i]
        for j in range(n):
            if not matched[j]:
                dx = ax - b[j][0]
                dy = ay - b[j][1]
                d = dx * dx + dy * dy
                if d < best:
                    best = d
                    index = j
        matched[index] = True
        weight = 1 - ((i - start + n) % n) / n
        total += weight * math.sqrt(best)
        if total >= limit:          # 이미 더 나쁜 결과 → 계산 중단 (속도 2~3배)
            return total
        i = (i + 1) % n
        if i == start:
            return total


def greedy_cloud_match(a, b, limit=float("inf")):
    n = len(a)
    step = max(1, int(n ** 0.5))
    best = limit
    for i in range(0, n, step):
        best = min(best, _cloud_distance(a, b, i, best), _cloud_distance(b, a, i, best))
    return best


# 사람마다 기울여 그리거나 가로 · 세로로 찌그러뜨려 그리므로 모양마다 변형본 5개를 둔다.
_VARIANTS = [(0, 1, 1), (-10, 1, 1), (10, 1, 1), (0, 1, 0.75), (0, 0.75, 1)]


def _variants(shape):
    out = []
    for rot, sx, sy in _VARIANTS:
        r = math.radians(rot)
        c, s = math.cos(r), math.sin(r)
        out.append(normalize([[(x * sx * c - y * sy * s, x * sx * s + y * sy * c) for x, y in stroke]
                              for stroke in shape]))
    return out


TEMPLATES = {code: [v for shape in shapes for v in _variants(shape)] for code, shapes in RUNE_SHAPES.items()}

# $P 거리(0 ~ 약 7)를 0~1 점수로 바꾸는 기준. 거리 0 → 1.0, 거리 SCORE_SCALE 이상 → 0.
SCORE_SCALE = 6.0


def recognize(points, candidates=None):
    """
    points     : 한 획 [(x, y), ...] / [{"x":..,"y":..}, ...]  또는 여러 획 [[...], [...]]  (y 는 위쪽이 +)
    candidates : 비교할 룬 코드 목록 (쿨타임 끝난 슬롯만). None 이면 19종 전체.
    반환       : {"spellId", "spellNumber", "name", "score", "gap", "recognized", "reason", "ranking", "strokes"}
    """
    strokes = to_strokes(points)
    codes = [c for c in (candidates or TEMPLATES.keys()) if c in TEMPLATES]
    result = {"spellId": None, "spellNumber": 0, "name": "", "score": 0.0, "gap": 0.0,
              "recognized": False, "reason": "", "ranking": [], "strokes": len(strokes)}
    pts = [p for s in strokes for p in s]
    if len(pts) < 3:
        result["reason"] = "too_few_points"
        return result
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    if max(max(xs) - min(xs), max(ys) - min(ys)) <= 0 or path_length(strokes) <= 0:
        result["reason"] = "no_movement"
        return result
    if not codes:
        result["reason"] = "no_candidates"
        return result

    cloud = normalize(strokes)
    ranking = []
    for code in codes:
        d = float("inf")
        for template in TEMPLATES[code]:
            d = greedy_cloud_match(cloud, template, d)
        ranking.append((code, max(0.0, 1.0 - d / SCORE_SCALE)))
    ranking.sort(key=lambda r: -r[1])

    best_code, best = ranking[0]
    gap = best - ranking[1][1] if len(ranking) > 1 else best
    result.update({
        "spellId": best_code,
        "spellNumber": spell_number(best_code),
        "name": SPELLS[best_code][0],
        "score": round(best, 3),
        "gap": round(gap, 3),
        "ranking": [[c, round(s, 3)] for c, s in ranking[:5]],
    })
    if best < MIN_SCORE:
        result["reason"] = "low_score"
    elif gap < MIN_GAP:
        result["reason"] = "ambiguous"
    else:
        result["recognized"] = True
        result["reason"] = "ok"
    return result


# ─────────────────────────────────────────────────────────────────────────────
# 자체 검사 — 손으로 그린 것처럼 흔든 가짜 궤적으로 혼동 여부 확인
# ─────────────────────────────────────────────────────────────────────────────
def fake_drawing(code, rng, noise=0.035, shape=None):
    """모양 하나를 골라 기울이고 · 늘리고 · 떨리게 하고, 획 순서 · 방향도 섞는다."""
    shapes = RUNE_SHAPES[code]
    strokes = shapes[rng.randrange(len(shapes)) if shape is None else shape]
    rot = math.radians(rng.uniform(-12, 12))
    sx = rng.uniform(0.85, 1.15)
    sy = rng.uniform(0.85, 1.15)
    shear = rng.uniform(-0.1, 0.1)
    out = []
    for stroke in strokes:
        length = path_length([stroke])
        pts = resample([stroke], max(4, int(6 + 30 * length)))
        wob_a = rng.uniform(0, 6.28)
        s = []
        for k, (x, y) in enumerate(pts):
            x, y = x * sx + shear * y, y * sy
            x, y = x * math.cos(rot) - y * math.sin(rot), x * math.sin(rot) + y * math.cos(rot)
            w = 0.03 * math.sin(wob_a + k * 0.25)
            s.append((x + rng.gauss(0, noise) + w, y + rng.gauss(0, noise) - w))
        if rng.random() < 0.5:
            s.reverse()
        if math.dist(stroke[0], stroke[-1]) < 1e-6:          # 닫힌 획은 아무 데서나 시작
            k = rng.randrange(len(s))
            s = s[k:] + s[1:k + 1]
        out.append(s)
    rng.shuffle(out)
    return out


def selftest(samples=30, seed=7, verbose=True):
    rng = random.Random(seed)
    codes = list(TEMPLATES)
    total = correct = rejected = 0
    confusion = {}
    for code in codes:
        for _ in range(samples):
            r = recognize(fake_drawing(code, rng))
            total += 1
            if not r["recognized"]:
                rejected += 1
            elif r["spellId"] == code:
                correct += 1
            else:
                confusion[(code, r["spellId"])] = confusion.get((code, r["spellId"]), 0) + 1

    # 아무렇게나 휘갈긴 선(1~3획)이 룬으로 오인되는 비율
    scribble_hits = 0
    for _ in range(150):
        strokes = []
        for _ in range(rng.randint(1, 3)):
            pts, x, y = [], rng.uniform(-1, 1), rng.uniform(-1, 1)
            for _ in range(rng.randint(6, 25)):
                x += rng.uniform(-0.6, 0.6)
                y += rng.uniform(-0.6, 0.6)
                pts.append((x, y))
            strokes.append(pts)
        if recognize(strokes)["recognized"]:
            scribble_hits += 1

    wrong = sum(confusion.values())
    if verbose:
        print(f"[룬 자체 검사] 가짜 손그림 {total}개 (19종 × {samples})")
        print(f"  정답 {correct} ({correct / total:.1%}) · 오인식 {wrong} ({wrong / total:.1%}) · 거절 {rejected} ({rejected / total:.1%})")
        print(f"  아무렇게나 휘갈긴 선 150개 중 룬으로 오인 {scribble_hits}개")
        for (a, b), n in sorted(confusion.items(), key=lambda kv: -kv[1])[:10]:
            print(f"  혼동 {a}({SPELLS[a][1]}) → {b}({SPELLS[b][1]}) : {n}회")
    return {"total": total, "correct": correct, "wrong": wrong, "rejected": rejected,
            "scribble_hits": scribble_hits, "confusion": confusion}


def draw_sheet(path, cell=180):
    """19종 룬 안내 그림 (획마다 시작점 = 초록 점). OpenCV + Pillow(한글) 필요."""
    import numpy as np
    import cv2
    cols = 5
    rows = math.ceil(len(RUNE_SHAPES) / cols)
    img = np.full((rows * cell, cols * cell, 3), 24, np.uint8)
    labels = []
    for idx, code in enumerate(RUNE_SHAPES):
        cx, cy = (idx % cols) * cell, (idx // cols) * cell
        shape = RUNE_SHAPES[code][0]
        pad = cell * 0.2
        scale = (cell - 2 * pad) / 2
        color = (90, 200, 255) if code.startswith("S") else (80, 120, 255)
        cv2.rectangle(img, (cx + 4, cy + 4), (cx + cell - 4, cy + cell - 4), (60, 60, 60), 1)
        for stroke in shape:
            poly = [(int(cx + cell / 2 + x * scale), int(cy + cell / 2 + 12 - y * scale)) for x, y in stroke]
            cv2.polylines(img, [np.array(poly, np.int32)], False, color, 3, cv2.LINE_AA)
            cv2.circle(img, poly[0], 5, (90, 230, 90), -1, cv2.LINE_AA)
        labels.append(((cx + 10, cy + 8), f"{code} {SPELLS[code][0]}"))
    try:
        from PIL import Image, ImageDraw, ImageFont
        pil = Image.fromarray(cv2.cvtColor(img, cv2.COLOR_BGR2RGB))
        font = ImageFont.truetype("C:/Windows/Fonts/malgun.ttf", 17)
        draw = ImageDraw.Draw(pil)
        for pos, text in labels:
            draw.text(pos, text, font=font, fill=(235, 235, 235))
        img = cv2.cvtColor(np.array(pil), cv2.COLOR_RGB2BGR)
    except Exception:
        for (x, y), text in labels:
            cv2.putText(img, text.split()[0], (x, y + 16), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (235, 235, 235), 1)
    ok, buf = cv2.imencode(".png", img)
    with open(path, "wb") as f:   # 한글 경로에서도 저장되도록 imwrite 대신 직접 쓴다
        f.write(buf.tobytes())
    return path


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    if "--sheet" in sys.argv:
        out = sys.argv[sys.argv.index("--sheet") + 1] if len(sys.argv) > sys.argv.index("--sheet") + 1 else "runes.png"
        print("룬 안내 그림:", draw_sheet(out))
    else:
        selftest()
