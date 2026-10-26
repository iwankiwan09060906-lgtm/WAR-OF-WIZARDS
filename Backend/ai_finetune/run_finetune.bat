@echo off
chcp 65001 > nul
setlocal
cd /d "%~dp0"
rem ================================================================
rem  War of Wizards - VLM 미세조정 (Qwen2.5-VL 3B + QLoRA, LLaMA-Factory)
rem    run_finetune.bat setup   : 가상환경(.venv) + PyTorch CUDA + LLaMA-Factory + 모델(7.5GB) 받기 (한 번, 약 11GB)
rem    run_finetune.bat smoke   : 가짜 데이터 47장면으로 설치 확인 학습 (약 10분)
rem    run_finetune.bat train   : 진짜 데이터 학습 (관리자 콘솔 - AI 학습 - 데이터셋 만들기 먼저)
rem    run_finetune.bat serve   : 학습한 모델 서버 http://127.0.0.1:8790/v1  (smoke 결과는: serve smoke)
rem  게임 서버 연결: 관리자 콘솔 게임 설정 ai.vlm_provider = local  (Docs/Backend/AiLearning.md)
rem ================================================================
set "PY=%~dp0.venv\Scripts\python.exe"
rem llamafactory-cli.exe 는 가상환경을 만든 경로가 박혀 있어 폴더를 옮기면 깨진다 → python -m 으로 부른다
set LF="%~dp0.venv\Scripts\python.exe" -m llamafactory.cli
set PYTHONUTF8=1
set PYTHONIOENCODING=utf-8
set DISABLE_VERSION_CHECK=1
if /i "%~1"=="setup" goto setup
if /i "%~1"=="smoke" goto smoke
if /i "%~1"=="train" goto train
if /i "%~1"=="serve" goto serve
echo 사용법: run_finetune.bat setup ^| smoke ^| train ^| serve [smoke]
goto :eof

:setup
if not exist "%PY%" python -m venv .venv
if errorlevel 1 goto nopython
"%PY%" -m pip install --disable-pip-version-check --upgrade pip
"%PY%" -m pip install --disable-pip-version-check -r constraints.txt --index-url https://download.pytorch.org/whl/cu128
if errorlevel 1 goto fail
"%PY%" -m pip install --disable-pip-version-check "llamafactory==0.9.5" bitsandbytes -c constraints.txt --extra-index-url https://download.pytorch.org/whl/cu128
if errorlevel 1 goto fail
echo [*] 모델 받기 (Qwen/Qwen2.5-VL-3B-Instruct, 7.5GB) - 멈추면 다시 실행하면 이어 받는다
"%PY%" -c "from huggingface_hub import snapshot_download as d; print(d('Qwen/Qwen2.5-VL-3B-Instruct'))"
if errorlevel 1 goto fail
"%PY%" -c "import torch; print('GPU', torch.cuda.is_available(), torch.cuda.get_device_name(0) if torch.cuda.is_available() else '')"
goto :eof

:smoke
"%PY%" make_smoke_dataset.py 80
if errorlevel 1 goto fail
cd smoke_dataset
set HF_HUB_OFFLINE=1
%LF% train train_qwen2_5vl_lora.yaml
goto :eof

:train
if not exist "..\ai_data\dataset\wow_vlm_sft.jsonl" (
  echo [경고] 데이터셋이 없습니다. 관리자 콘솔 - AI 학습 - "VLM 미세조정 데이터셋 만들기" 를 먼저 누르세요.
  goto :eof
)
cd ..\ai_data\dataset
set HF_HUB_OFFLINE=1
%LF% train train_qwen2_5vl_lora.yaml
goto :eof

:serve
set API_HOST=127.0.0.1
set API_PORT=8790
set HF_HUB_OFFLINE=1
if /i "%~2"=="smoke" (cd smoke_dataset) else (cd ..\ai_data\dataset)
echo [*] 학습한 모델 서버: http://127.0.0.1:8790/v1  (끄려면 Ctrl+C)
%LF% api api_qwen2_5vl_lora.yaml
goto :eof

:nopython
echo [경고] python 명령을 찾을 수 없습니다. Python 3.11 ~ 3.12 를 설치하세요.
pause
goto :eof
:fail
echo [경고] 실패했습니다. 위의 오류를 확인하세요.
pause
