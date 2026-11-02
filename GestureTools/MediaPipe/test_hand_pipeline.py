# -*- coding: utf-8 -*-
"""
MediaPipe 손 입력 파이프라인 자동 검사 (카메라 없이 실행)

    python test_hand_pipeline.py

1. 가짜 손 랜드마크 → 손 자세 판정 (손바닥 / 주먹 / 가리키기 / 핀치)
2. 오른손 시나리오: 핀치로 삼각형 그리기 → 룬 S03 인식 → 주먹 → 가리키기 → 손바닥 취소 → 추적 끊김
3. 왼손 시나리오: 손바닥 휘두르기 → 이동 +1 / 연타 무시 / -1
4. 19종 룬 인식 정확도 (흔든 가짜 손그림)
5. UDP 송수신 (추적기 → Unity, Unity → 추적기 후보 목록)
6. 손 모양 입력: 손가락 패턴 19종 → 스킬, 유지 프레임 · 재선택 · 추적 끊김
"""

import json
import math
import os
import random
import socket
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hand_gestures as hg  # noqa: E402
import wow_runes  # noqa: E402


def make_hand(pose, dx=0.0, dy=0.0):
    """화면 좌표(y 아래)로 만든 단순한 오른손 21점."""
    wrist = (0.50, 0.80)
    mcps = {"index": (0.46, 0.62), "middle": (0.50, 0.61), "ring": (0.54, 0.62), "pinky": (0.575, 0.64)}

    def finger(name, state):
        mx, my = mcps[name]
        if state == "ext":
            return [(mx, my), (mx, my - 0.05), (mx, my - 0.085), (mx, my - 0.115)]
        if state == "curl":
            return [(mx, my), (mx, my - 0.04), (mx + 0.005, my - 0.015), (mx + 0.005, my + 0.02)]
        if state == "pinch":   # 검지를 구부려 엄지 끝에 댄 모양
            return [(mx, my), (mx - 0.015, my - 0.045), (mx - 0.035, my - 0.065), (mx - 0.055, my - 0.06)]
        raise ValueError(state)

    states = {
        "open": dict(index="ext", middle="ext", ring="ext", pinky="ext"),
        "fist": dict(index="curl", middle="curl", ring="curl", pinky="curl"),
        "point": dict(index="ext", middle="curl", ring="curl", pinky="curl"),
        "pinch": dict(index="pinch", middle="curl", ring="curl", pinky="curl"),
        "ok": dict(index="pinch", middle="ext", ring="ext", pinky="ext"),
    }[pose]
    thumb_tip = {"open": (0.36, 0.62), "fist": (0.47, 0.66), "point": (0.48, 0.66),
                 "pinch": (0.41, 0.565), "ok": (0.41, 0.565)}[pose]
    thumb = [(0.44, 0.76), (0.41, 0.71), ((0.41 + thumb_tip[0]) / 2, (0.71 + thumb_tip[1]) / 2), thumb_tip]
    pts = [wrist] + thumb
    for name in ("index", "middle", "ring", "pinky"):
        pts += finger(name, states[name])
    return [(x + dx, y + dy, 0.0) for x, y in pts]


class PoseTest(unittest.TestCase):
    def test_static_poses(self):
        for pose, expect in [("open", "open"), ("fist", "fist"), ("point", "point"), ("pinch", "pinch"), ("ok", "pinch")]:
            got, f = hg.classify_pose(make_hand(pose))
            self.assertEqual(got, expect, f"{pose}: {got} {f}")

    def test_aim_columns(self):
        hg.Settings.aim_mode = "position"
        self.assertEqual(hg.aim_from(make_hand("point", dx=-0.35))[0], 0)
        self.assertEqual(hg.aim_from(make_hand("point"))[0], 1)
        self.assertEqual(hg.aim_from(make_hand("point", dx=0.30))[0], 2)


class RightHandScenario(unittest.TestCase):
    def test_full_cast_flow(self):
        rh = hg.RightHand()
        t = 0.0
        events = []

        def feed(lm, n=1):
            nonlocal t
            for _ in range(n):
                events.extend(rh.update(lm, t))
                t += 1 / 30

        feed(make_hand("open"), 12)
        # 핀치한 채로 삼각형(꼭짓점 위) 그리기 — 손 전체를 옮긴다
        tri = [(0.0, -0.12), (-0.12, 0.08), (0.12, 0.08), (0.0, -0.12)]
        for a, b in zip(tri, tri[1:]):
            for k in range(20):
                u = k / 20
                feed(make_hand("pinch", a[0] + (b[0] - a[0]) * u, a[1] + (b[1] - a[1]) * u))
        feed(make_hand("open"), 10)          # 핀치를 떼면서 손을 폄 → 취소로 보면 안 됨
        feed(make_hand("fist"), 6)
        feed(make_hand("point", dx=0.30), 6)
        feed(make_hand("open"), 30)          # 시간이 지난 뒤 손바닥 → 취소
        feed(None, 12)                       # 손이 화면 밖으로

        names = [e["name"] for e in events]
        self.assertEqual(names, ["tracking", "palm_open", "pinch_start", "pinch_end", "fist", "point",
                                 "palm_open", "tracking"], names)
        end = events[names.index("pinch_end")]
        self.assertFalse(end["tooShort"])
        rune = wow_runes.recognize(end["points"])
        self.assertTrue(rune["recognized"], rune)
        self.assertEqual(rune["spellId"], "S03", rune)
        self.assertEqual(events[names.index("point")]["column"], 2)
        self.assertFalse(events[-1]["tracked"])

    def test_tracking_lost_while_drawing_cancels(self):
        rh = hg.RightHand()
        evs = []
        t = 0.0
        for i in range(10):
            evs += rh.update(make_hand("pinch", dx=i * 0.01), t)
            t += 1 / 30
        for _ in range(12):
            evs += rh.update(None, t)
            t += 1 / 30
        names = [e["name"] for e in evs]
        self.assertIn("pinch_cancel", names)
        self.assertNotIn("pinch_end", names)


class LeftHandScenario(unittest.TestCase):
    def test_swipe_moves(self):
        lh = hg.LeftHand()
        t = 0.0
        evs = []

        def sweep(x0, x1, frames):
            nonlocal t
            for k in range(frames):
                evs.extend(lh.update(make_hand("open", dx=x0 + (x1 - x0) * k / (frames - 1)), t))
                t += 1 / 30

        sweep(0, 0, 5)
        sweep(0, 0.16, 6)       # 오른쪽으로 빠르게
        sweep(0.16, 0.0, 6)     # 곧바로 왼쪽 — 연타 방지 시간 안이라 무시
        sweep(0.0, 0.0, 25)     # 쉬기
        sweep(0.0, -0.16, 6)    # 왼쪽
        sweep(-0.16, -0.16, 3)
        sweep(-0.16, -0.10, 30)  # 천천히 → 이동 아님
        moves = [e["direction"] for e in evs if e["name"] == "move"]
        self.assertEqual(moves, [1, -1], evs)

    def test_fist_does_not_move(self):
        lh = hg.LeftHand()
        evs = []
        for k in range(10):
            evs += lh.update(make_hand("fist", dx=k * 0.03), k / 30)
        self.assertFalse([e for e in evs if e["name"] == "move"])


class RuneTest(unittest.TestCase):
    def test_every_rune_recognized(self):
        rng = random.Random(42)
        wrong = []
        for code in wow_runes.SPELLS:
            for _ in range(4):
                r = wow_runes.recognize(wow_runes.fake_drawing(code, rng))
                if r["recognized"] and r["spellId"] != code:
                    wrong.append((code, r["spellId"]))
        self.assertLessEqual(len(wrong), 1, wrong)

    def test_candidates_limit(self):
        tri = wow_runes.RUNE_SHAPES["S03"]
        r = wow_runes.recognize(tri, candidates=["S02", "S09"])
        self.assertNotEqual(r["spellId"], "S03")
        r = wow_runes.recognize(tri, candidates=["S03", "S02"])
        self.assertEqual(r["spellId"], "S03")
        self.assertTrue(r["recognized"])
        self.assertEqual(r["spellNumber"], 3)
        self.assertEqual(wow_runes.spell_number("H02"), 102)

    def test_too_short(self):
        self.assertFalse(wow_runes.recognize([(0, 0), (0.01, 0.01)])["recognized"])


def shape_hand(pattern):
    """손가락을 패턴대로 편 오른손 21점 — 엄지는 손 옆으로 펴거나 손바닥 위로 접는다."""
    wrist = (0.50, 0.80)
    mcps = [(0.46, 0.62), (0.50, 0.61), (0.54, 0.62), (0.575, 0.64)]
    tip = (0.36, 0.62) if pattern[0] == "1" else (0.50, 0.66)
    pts = [wrist, (0.44, 0.76), (0.41, 0.71), ((0.41 + tip[0]) / 2, (0.71 + tip[1]) / 2), tip]
    for i, (mx, my) in enumerate(mcps):
        if pattern[i + 1] == "1":
            pts += [(mx, my), (mx, my - 0.05), (mx, my - 0.085), (mx, my - 0.115)]
        else:
            pts += [(mx, my), (mx, my - 0.04), (mx + 0.005, my - 0.015), (mx + 0.005, my + 0.02)]
    return [(x, y, 0.0) for x, y in pts]


class ShapeInputTest(unittest.TestCase):
    def feed(self, hand, lm, t, n):
        evs = []
        for _ in range(n):
            evs += hand.update(lm, t[0])
            t[0] += 1 / 30
        return evs

    def test_table_covers_every_spell(self):
        self.assertEqual(sorted(hg.SKILL_SHAPES.values()), sorted(wow_runes.SPELLS))

    def test_every_pattern_is_read_back(self):
        for pattern in hg.SKILL_SHAPES:
            self.assertEqual(hg.finger_pattern(shape_hand(pattern)), pattern)

    def test_hold_picks_once(self):
        hand, t = hg.ShapeHand(hold_frames=8), [0.0]
        evs = self.feed(hand, shape_hand("01110"), t, 7)
        self.assertEqual([e for e in evs if e["name"] == "shape"], [])
        self.assertGreater(hand.progress, 0.8)
        evs = self.feed(hand, shape_hand("01110"), t, 30)      # 계속 들고 있어도 한 번만
        self.assertEqual([e["spell"] for e in evs if e["name"] == "shape"], ["S03"])
        self.assertEqual(hand.progress, 0.0)

    def test_quick_change_is_ignored(self):
        hand, t = hg.ShapeHand(hold_frames=8), [0.0]
        evs = []
        for pattern in ("11111", "01110", "01000", "00000"):
            evs += self.feed(hand, shape_hand(pattern), t, 5)   # 손을 바꾸는 도중의 모양은 고르지 않는다
        self.assertEqual([e for e in evs if e["name"] == "shape"], [])

    def test_raising_hand_again_picks_again(self):
        hand, t = hg.ShapeHand(hold_frames=8), [0.0]
        evs = self.feed(hand, shape_hand("10000"), t, 12)
        evs += self.feed(hand, None, t, 30)                    # 다시 고를 수 있는 간격(0.8초)이 지나도록
        evs += self.feed(hand, shape_hand("10000"), t, 40)
        names = [e["name"] for e in evs]
        self.assertEqual(names, ["tracking", "shape", "tracking", "tracking", "shape"], names)

    def test_unknown_pattern_picks_nothing(self):
        hand, t = hg.ShapeHand(hold_frames=8), [0.0]
        evs = self.feed(hand, shape_hand("10010"), t, 30)
        self.assertEqual([e for e in evs if e["name"] == "shape"], [])

    def test_cursor_reaches_edges_inside_camera(self):
        hand, t = hg.ShapeHand(hold_frames=8), [0.0]
        lm = [(x - 0.40, y, z) for x, y, z in shape_hand("00000")]   # 손이 화면 왼쪽 가까이
        self.feed(hand, lm, t, 20)
        self.assertEqual(hand.cursor[0], 0.0)

    def test_packet_has_shape_and_no_nested_list(self):
        import wow_hand_tracker as tr
        hand = hg.ShapeHand(hold_frames=8)
        lm = shape_hand("01110")
        hand.update(lm, 0.0)
        pkt = tr.hand_packet(hand, lm, 4 / 3)
        self.assertEqual(pkt["shape"]["spell"], "S03")
        self.assertFalse(pkt["drawing"])
        self.assertFalse(_has_nested_list(pkt))
        table = tr.shapes_json()
        self.assertEqual(len(table["patterns"]), len(table["spells"]))
        self.assertFalse(_has_nested_list(table))


def _has_nested_list(v):
    if isinstance(v, list):
        return any(isinstance(x, list) or _has_nested_list(x) for x in v)
    if isinstance(v, dict):
        return any(_has_nested_list(x) for x in v.values())
    return False


class UnityJsonShapeTest(unittest.TestCase):
    """Unity JsonUtility 는 중첩 배열을 못 읽는다 → 보내는 메시지에 [[..]] 가 없어야 한다."""

    def test_messages_are_jsonutility_friendly(self):
        import wow_hand_tracker as tr
        rh, lh = hg.RightHand(), hg.LeftHand()
        lm = make_hand("point")
        rh.update(lm, 0.0)
        lh.update(make_hand("open"), 0.0)
        frame = {"type": "frame", "right": tr.hand_packet(rh, lm, 4 / 3), "left": tr.hand_packet(lh, make_hand("open"), 4 / 3)}
        self.assertEqual(len(frame["right"]["landmarks"]), 63)
        self.assertIsInstance(frame["right"]["pinch"], float)
        pts = [(0.5, 0.9), (0.3, 0.4), (0.7, 0.4), (0.5, 0.9)]
        ev = {"type": "event", "name": "pinch_end", "points": tr.points_json(pts),
              "rune": tr.rune_json(wow_runes.recognize(pts))}
        self.assertEqual(ev["points"][0], {"x": 0.5, "y": 0.9})
        self.assertEqual(ev["rune"]["spellId"], "S03")
        self.assertIsInstance(ev["rune"]["ranking"][0], dict)
        for msg in (frame, ev):
            self.assertFalse(_has_nested_list(json.loads(json.dumps(msg))), msg)


class HandSplitTest(unittest.TestCase):
    """MediaPipe 라벨은 그림 속 손 모양 그대로다 — 공식 손 사진(woman_hands.jpg)을 뒤집으면 라벨도 뒤집힌다."""

    @staticmethod
    def result(*hands):
        from types import SimpleNamespace as Ns
        return Ns(hand_landmarks=[[Ns(x=x, y=0.5, z=0.0) for _ in range(21)] for _, x in hands],
                  handedness=[[Ns(category_name=label)] for label, _ in hands])

    def test_mirrored_frame_swaps_labels(self):
        import wow_hand_tracker as tr
        right, left = tr.split_hands(self.result(("Left", 0.7), ("Right", 0.3)))
        self.assertEqual((right[0][0], left[0][0]), (0.7, 0.3))   # 거울 화면 오른쪽 = 사용자의 오른손 = 라벨 Left

    def test_raw_frame_keeps_labels(self):
        import wow_hand_tracker as tr
        right, left = tr.split_hands(self.result(("Right", 0.3), ("Left", 0.7)), mirrored=False)
        self.assertEqual((right[0][0], left[0][0]), (0.3, 0.7))

    def test_left_handed_swaps_roles(self):
        import wow_hand_tracker as tr
        right, left = tr.split_hands(self.result(("Left", 0.7), ("Right", 0.3)), swap=True)
        self.assertEqual((right[0][0], left[0][0]), (0.3, 0.7))   # 스킬 자리에 사용자의 왼손

    def test_same_label_falls_back_to_screen_side(self):
        import wow_hand_tracker as tr
        right, left = tr.split_hands(self.result(("Left", 0.2), ("Left", 0.8)))
        self.assertEqual((right[0][0], left[0][0]), (0.8, 0.2))


class SkillHandTest(unittest.TestCase):
    def test_input_mode_picks_hand(self):
        import wow_hand_tracker as tr
        self.assertIsInstance(tr.skill_hand(tr.parse_args(["--input", "rune"])), hg.RightHand)
        self.assertIsInstance(tr.skill_hand(tr.parse_args([])), hg.ShapeHand)


class UdpTest(unittest.TestCase):
    def test_roundtrip(self):
        import wow_hand_tracker as tr
        unity = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        unity.bind(("127.0.0.1", 0))
        unity.settimeout(2)
        port = unity.getsockname()[1]
        probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        probe.bind(("127.0.0.1", 0))
        ctrl_port = probe.getsockname()[1]
        probe.close()

        link = tr.Link("127.0.0.1", port, ctrl_port)
        link.send({"type": "event", "hand": "right", "name": "fist", "t": 1.0})
        data, _ = unity.recvfrom(65535)
        msg = json.loads(data.decode("utf-8"))
        self.assertEqual((msg["type"], msg["name"], msg["seq"]), ("event", "fist", 1))

        unity.sendto(json.dumps({"type": "candidates", "spells": ["S03", "H02"]}).encode(), ("127.0.0.1", ctrl_port))
        got = []
        for _ in range(50):
            got = link.poll()
            if got:
                break
            import time
            time.sleep(0.02)
        self.assertEqual(got, [{"type": "candidates", "spells": ["S03", "H02"]}])
        link.close()
        unity.close()


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    unittest.main(verbosity=2)
