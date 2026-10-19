"""
WAR OF WIZARDS - 서버(DB)에 모인 룬 제스처를 학습 데이터로 변환
File: WAR-OF-WIZARDS/AI/Tools/fetch_dataset.py

1) Spring 서버 /api/server/gestures/samples 에서 전부 내려받기 (X-Server-Key)
2) Dataset/raw/samples.json 에 원본 저장
3) dataset_converter.normalize_3d_stroke 로 64점·중심 정렬·크기 정규화
4) Dataset/normalized/gestures.npz (X: N×64×3, y: N) + classes.json 저장

  python fetch_dataset.py
  python fetch_dataset.py --url http://192.168.0.10:8080 --key <서버키>
"""

import argparse
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
from dataset_converter import normalize_3d_stroke, TARGET_POINTS  # noqa: E402
from wow_api import WowApi, DEFAULT_URL, DEFAULT_SERVER_KEY, unflatten  # noqa: E402

AI_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
RAW_PATH = os.path.join(AI_DIR, "Dataset", "raw", "samples.json")
NPZ_PATH = os.path.join(AI_DIR, "Dataset", "normalized", "gestures.npz")
CLASSES_PATH = os.path.join(AI_DIR, "Dataset", "normalized", "classes.json")


def build_dataset(samples, classes):
    index = {label: i for i, label in enumerate(classes)}
    X, y = [], []
    skipped = 0
    for s in samples:
        label = s["label"]
        if label not in index:
            skipped += 1
            continue
        points = np.array(unflatten(s["points"]), dtype=np.float32)
        if len(points) < 4:
            skipped += 1
            continue
        X.append(normalize_3d_stroke(points, TARGET_POINTS))
        y.append(index[label])
    if skipped:
        print(f"건너뛴 샘플: {skipped}개")
    return np.array(X, dtype=np.float32).reshape(-1, TARGET_POINTS, 3), np.array(y, dtype=np.int64)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", default=DEFAULT_URL)
    parser.add_argument("--key", default=DEFAULT_SERVER_KEY)
    args = parser.parse_args()

    api = WowApi(args.url, args.key)
    classes = api.model_info()["classes"]           # 서버가 정한 클래스 순서 (S01..S13, H01..H06)
    samples = api.export_gestures()
    print(f"서버에서 {len(samples)}개 샘플을 받았습니다.")

    os.makedirs(os.path.dirname(RAW_PATH), exist_ok=True)
    os.makedirs(os.path.dirname(NPZ_PATH), exist_ok=True)
    with open(RAW_PATH, "w", encoding="utf-8") as f:
        json.dump(samples, f, ensure_ascii=False)

    X, y = build_dataset(samples, classes)
    np.savez_compressed(NPZ_PATH, X=X, y=y)
    with open(CLASSES_PATH, "w", encoding="utf-8") as f:
        json.dump(classes, f, ensure_ascii=False, indent=2)

    counts = np.bincount(y, minlength=len(classes)) if len(y) else np.zeros(len(classes), dtype=int)
    print(f"저장: {NPZ_PATH}  X={X.shape}  y={y.shape}")
    for label, n in zip(classes, counts):
        print(f"  {label}: {n}")


if __name__ == "__main__":
    main()
