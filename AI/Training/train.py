import argparse
import json
import os
import sys

import numpy as np
import torch
import torch.nn as nn
import torch.optim as optim

AI_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
DATASET_PATH = os.path.join(AI_DIR, 'Dataset', 'normalized', 'gestures.npz')
CLASSES_PATH = os.path.join(AI_DIR, 'Dataset', 'normalized', 'classes.json')
ONNX_PATH = os.path.join(AI_DIR, 'Models', 'gesture_model.onnx')
WEIGHTS_PATH = os.path.join(AI_DIR, 'Models', 'gesture_model_weights.json')

# 스킬 19종 (서버 /api/gestures/model/info 의 classes 와 같은 순서). classes.json 이 있으면 그 값을 쓴다.
DEFAULT_CLASSES = [f'S{i:02d}' for i in range(1, 14)] + [f'H{i:02d}' for i in range(1, 7)]


# 1. 모델 설계 (LSTM 기반 제스처 분류기)
# 시계열 데이터(순서대로 그려지는 점들)를 분석하기 위해 순환 신경망(LSTM)을 사용합니다.
class GestureModel(nn.Module):
    def __init__(self, input_size=3, hidden_size=64, num_classes=19):
        super(GestureModel, self).__init__()
        # input_size=3 : X, Y, Z 좌표
        # num_classes=19 : 인식할 마법 종류 (GDD 기준 19개)
        self.lstm = nn.LSTM(input_size, hidden_size, batch_first=True)
        self.fc = nn.Linear(hidden_size, num_classes)

    def forward(self, x):
        # x 형태: (배치 크기, 시퀀스 길이(점 개수), 3차원)
        lstm_out, _ = self.lstm(x)

        # 마지막 점까지 그렸을 때의 최종 결과를 추출
        last_time_step = lstm_out[:, -1, :]
        out = self.fc(last_time_step)
        return out


def load_dataset():
    """AI/Tools/fetch_dataset.py 가 서버(DB)에서 받아 만든 데이터. 없으면 None."""
    if not os.path.exists(DATASET_PATH):
        return None
    data = np.load(DATASET_PATH)
    classes = DEFAULT_CLASSES
    if os.path.exists(CLASSES_PATH):
        with open(CLASSES_PATH, encoding='utf-8') as f:
            classes = json.load(f)
    return data['X'].astype(np.float32), data['y'].astype(np.int64), classes


def generate_synthetic_samples(samples_per_class=20, classes=DEFAULT_CLASSES):
    """실제 데이터가 없을 때 쓰는 합성 룬 (evaluate.py 에서도 사용)."""
    sys.path.insert(0, os.path.join(AI_DIR, 'Tools'))
    from dataset_converter import normalize_3d_stroke
    from wow_api import synthetic_rune
    X, y = [], []
    for idx in range(len(classes)):
        for _ in range(samples_per_class):
            X.append(normalize_3d_stroke(np.array(synthetic_rune(idx), dtype=np.float32)))
            y.append(idx)
    return np.array(X, dtype=np.float32), np.array(y, dtype=np.int64)


def train(epochs=100, upload=False):
    print("AI 모델 학습을 시작합니다...")
    dataset = load_dataset()
    if dataset is not None and len(dataset[1]) > 0:
        X, y, classes = dataset
        print(f"서버 데이터셋 사용: {X.shape[0]}개 샘플, {len(classes)}개 클래스")
    else:
        classes = DEFAULT_CLASSES
        print("데이터셋이 없어 합성 데이터로 학습합니다. (python ../Tools/fetch_dataset.py 로 서버 데이터를 먼저 받으세요)")
        X, y = generate_synthetic_samples(20, classes)

    model = GestureModel(num_classes=len(classes))

    # 손실 함수 및 최적화 기법 (분류 문제이므로 CrossEntropy 사용)
    criterion = nn.CrossEntropyLoss()
    optimizer = optim.Adam(model.parameters(), lr=0.005)

    # 학습/검증 분리 (8:2)
    rng = np.random.default_rng(42)
    order = rng.permutation(len(y))
    split = max(1, int(len(y) * 0.8))
    train_idx, val_idx = order[:split], order[split:]
    inputs, labels = torch.tensor(X[train_idx]), torch.tensor(y[train_idx])

    for epoch in range(epochs):
        model.train()
        optimizer.zero_grad()

        # 1. 예측
        outputs = model(inputs)

        # 2. 오차 계산
        loss = criterion(outputs, labels)

        # 3. 역전파 (학습)
        loss.backward()
        optimizer.step()

        if (epoch + 1) % 20 == 0:
            print(f'Epoch [{epoch+1}/{epochs}], Loss: {loss.item():.4f}')

    if len(val_idx) > 0:
        model.eval()
        with torch.no_grad():
            preds = model(torch.tensor(X[val_idx])).argmax(dim=1).numpy()
        print(f"검증 정확도: {(preds == y[val_idx]).mean() * 100:.1f}% ({len(val_idx)}개)")

    print("학습 완료!")

    # 4. 유니티로 가져가기 위해 ONNX 포맷으로 추출 (매우 중요)
    export_to_onnx(model)
    # 4-1. Unity 가 패키지 없이 순수 C# 으로 추론할 수 있도록 가중치를 JSON 으로도 추출
    export_weights_json(model, classes)

    # 5. 서버에 올리면 Unity 가 /api/gestures/model(/weights) 로 최신 모델을 받아간다
    if upload:
        sys.path.insert(0, os.path.join(AI_DIR, 'Tools'))
        from wow_api import WowApi
        api = WowApi()
        api.upload_model(WEIGHTS_PATH)
        info = api.upload_model(ONNX_PATH)
        print(f"서버 업로드 완료: sha256={info['sha256'][:12]}..., {info['sizeBytes']} bytes")


def export_weights_json(model, classes):
    """LSTM/Linear 가중치를 1차원 배열로 저장 (Unity JsonUtility 로 읽을 수 있는 모양).
    게이트 순서는 PyTorch 와 같다: input, forget, cell(g), output"""
    sd = {k: v.detach().cpu().numpy().astype(np.float32) for k, v in model.state_dict().items()}
    payload = {
        'format': 'wow-lstm-v1',
        'inputSize': int(model.lstm.input_size),
        'hiddenSize': int(model.lstm.hidden_size),
        'numClasses': int(model.fc.out_features),
        'numPoints': 64,
        'classes': list(classes),
        'weightIh': sd['lstm.weight_ih_l0'].reshape(-1).tolist(),
        'weightHh': sd['lstm.weight_hh_l0'].reshape(-1).tolist(),
        'biasIh': sd['lstm.bias_ih_l0'].reshape(-1).tolist(),
        'biasHh': sd['lstm.bias_hh_l0'].reshape(-1).tolist(),
        'fcWeight': sd['fc.weight'].reshape(-1).tolist(),
        'fcBias': sd['fc.bias'].reshape(-1).tolist(),
    }
    with open(WEIGHTS_PATH, 'w', encoding='utf-8') as f:
        json.dump(payload, f)
    print(f"Unity(C#)용 가중치 JSON 이 추출되었습니다: {WEIGHTS_PATH}")


def export_to_onnx(model):
    model.eval()
    # 더미 입력값을 하나 넣어주어 ONNX가 모델 구조를 파악하게 함
    dummy_input = torch.randn(1, 64, 3)
    os.makedirs(os.path.dirname(ONNX_PATH), exist_ok=True)

    export_args = dict(
        export_params=True,
        opset_version=17,
        input_names=['input_points'],
        output_names=['spell_probabilities'],
        dynamic_axes={'input_points': {0: 'batch'}, 'spell_probabilities': {0: 'batch'}},
    )
    try:
        # torch 2.5+ : 기존 방식 exporter 사용 (LSTM 호환성이 좋음)
        torch.onnx.export(model, dummy_input, ONNX_PATH, dynamo=False, **export_args)
    except TypeError:
        torch.onnx.export(model, dummy_input, ONNX_PATH, **export_args)
    print(f"유니티용 ONNX 모델이 추출되었습니다: {ONNX_PATH}")


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='룬 제스처 분류기 학습')
    parser.add_argument('--epochs', type=int, default=100)
    parser.add_argument('--upload', action='store_true', help='학습한 ONNX 를 Spring 서버에 업로드')
    args = parser.parse_args()
    train(epochs=args.epochs, upload=args.upload)
