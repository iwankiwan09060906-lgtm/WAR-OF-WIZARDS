"""
손 모양 인식 -> 유니티로 스킬 번호(1~19) 전송 (UDP)

설치:  pip install opencv-python mediapipe
실행:  python hand_gesture.py      (종료: 영상 창을 클릭하고 q)

사용 팁: 손바닥이 카메라를 향하게, 카메라에서 30~50cm, 밝은 곳에서 하세요.
패턴 읽는 법: (엄지, 검지, 중지, 약지, 새끼) 순서로 1=폄, 0=접음
"""
import socket
import time

import cv2
import mediapipe as mp

UNITY_IP = "127.0.0.1"
UNITY_PORT = 5052      # 유니티 스크립트의 port와 같아야 함
STABLE_FRAMES = 8      # 같은 모양이 이만큼 연속으로 나와야 인정 (오인식 방지)
COOLDOWN = 0.8         # 같은 스킬을 다시 보낼 수 있는 최소 간격(초)

# 패턴(엄지,검지,중지,약지,새끼) -> (스킬 번호, 화면에 표시할 이름)
SKILLS = {
    "00000": (1, "BLIND"),
    "01000": (2, "POISON"),
    "01100": (3, "FREEZE MINIONS"),
    "01110": (4, "FIREBALL"),
    "01111": (5, "STRONG ARROW"),
    "11111": (6, "TORNADO"),
    "10000": (7, "LIGHTNING"),
    "11000": (8, "LIFESTEAL"),
    "11100": (9, "GUN SPRAY"),
    "10001": (10, "LASER"),
    "01001": (11, "REFLECT"),
    "11001": (12, "DMG REDUCE"),
    "00001": (13, "MINION BUFF"),
    "00100": (14, "TOWER BUFF"),
    "10100": (15, "ATK UP"),
    "00111": (16, "MELEE SUMMON"),
    "00110": (17, "RANGED SUMMON"),
    "11110": (18, "ATTACK TOWER"),
    "00011": (19, "SUMMON TOWER"),
}

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

mp_hands = mp.solutions.hands
mp_draw = mp.solutions.drawing_utils
hands = mp_hands.Hands(
    max_num_hands=1,
    min_detection_confidence=0.7,
    min_tracking_confidence=0.6,
)


def dist(a, b):
    return ((a.x - b.x) ** 2 + (a.y - b.y) ** 2) ** 0.5


def finger_pattern(lm):
    """손가락 5개의 폄/접음을 '01100' 같은 문자열로 만들어 줌"""
    bits = []
    # 엄지: 새끼 뿌리(17)에서 엄지 끝(4)이 엄지 관절(3)보다 멀면 폄
    bits.append("1" if dist(lm[4], lm[17]) > dist(lm[3], lm[17]) * 1.05 else "0")
    # 나머지: 손목(0)에서 손가락 끝이 두 번째 관절보다 멀면 폄 (손 방향과 상관없음)
    for tip, pip in [(8, 6), (12, 10), (16, 14), (20, 18)]:
        bits.append("1" if dist(lm[tip], lm[0]) > dist(lm[pip], lm[0]) else "0")
    return "".join(bits)


def main():
    cap = cv2.VideoCapture(0)
    if not cap.isOpened():
        print("웹캠을 열 수 없어요. 카메라 연결/권한을 확인해 주세요.")
        return

    candidate = ""       # 지금 후보로 보고 있는 패턴
    count = 0            # 후보가 연속으로 나온 프레임 수
    current = ""         # 확정된 현재 패턴
    last_sent = {}       # 스킬별 마지막 전송 시각
    shown = ""           # 화면에 표시할 마지막 발동 스킬

    while True:
        ok, frame = cap.read()
        if not ok:
            break

        frame = cv2.flip(frame, 1)  # 거울처럼 좌우 반전
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        result = hands.process(rgb)

        pattern = ""
        if result.multi_hand_landmarks:
            hand = result.multi_hand_landmarks[0]
            mp_draw.draw_landmarks(frame, hand, mp_hands.HAND_CONNECTIONS)
            pattern = finger_pattern(hand.landmark)

        # 같은 패턴이 연속으로 나와야 확정
        if pattern == candidate:
            count += 1
        else:
            candidate = pattern
            count = 1

        if count == STABLE_FRAMES and candidate != current:
            current = candidate
            if current in SKILLS:
                skill_id, name = SKILLS[current]
                now = time.time()
                if now - last_sent.get(skill_id, 0) > COOLDOWN:
                    sock.sendto(str(skill_id).encode("utf-8"), (UNITY_IP, UNITY_PORT))
                    last_sent[skill_id] = now
                    shown = "SKILL %d  %s" % (skill_id, name)
                    print("전송:", shown)

        cv2.putText(frame, "pattern: " + (candidate or "-"), (10, 35),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.9, (255, 255, 0), 2)
        cv2.putText(frame, shown, (10, 75),
                    cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 255, 0), 2)
        cv2.imshow("Hand Gesture (q to quit)", frame)

        if cv2.waitKey(1) & 0xFF == ord("q"):
            break

    cap.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()