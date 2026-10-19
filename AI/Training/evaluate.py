"""
WAR OF WIZARDS - Gesture Classifier Evaluation Script
File: WAR-OF-WIZARDS/AI/Training/evaluate.py

학습된 ONNX 모델(AI/Models/gesture_model.onnx)을 데이터셋(fetch_dataset.py 결과, 없으면 합성 데이터)으로 평가한다.
스킬(클래스)별 정밀도/재현율/F1 과 전체 정확도를 출력.
  python evaluate.py
"""

import numpy as np
import onnxruntime as ort

from train import ONNX_PATH, DEFAULT_CLASSES, load_dataset, generate_synthetic_samples


def evaluate_model():
    dataset = load_dataset()
    if dataset is not None and len(dataset[1]) > 0:
        X_test, y_test, classes = dataset
        print(f"서버 데이터셋으로 평가: {len(y_test)}개")
    else:
        classes = DEFAULT_CLASSES
        X_test, y_test = generate_synthetic_samples(samples_per_class=50, classes=classes)
        print(f"데이터셋이 없어 합성 데이터로 평가: {len(y_test)}개")

    session = ort.InferenceSession(ONNX_PATH)
    input_name = session.get_inputs()[0].name
    preds = np.argmax(session.run(None, {input_name: X_test})[0], axis=1)

    n = len(classes)
    confusion = np.zeros((n, n), dtype=int)
    for t, p in zip(y_test, preds):
        confusion[t, p] += 1

    print("=" * 55)
    print(f"{'Skill':<8} | {'Precision':>9} | {'Recall':>9} | {'F1':>9}")
    print("-" * 55)
    for i, name in enumerate(classes):
        tp = confusion[i, i]
        fp = confusion[:, i].sum() - tp
        fn = confusion[i, :].sum() - tp
        precision = tp / (tp + fp) if tp + fp else 0.0
        recall = tp / (tp + fn) if tp + fn else 0.0
        f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
        print(f"{name:<8} | {precision*100:>8.1f}% | {recall*100:>8.1f}% | {f1*100:>8.1f}%")
    print("=" * 55)
    print(f"전체 정확도: {np.trace(confusion) / len(y_test) * 100:.2f}% ({np.trace(confusion)}/{len(y_test)})")


if __name__ == '__main__':
    evaluate_model()
