// 메타 UI 화면 하나 (C01~C13). 웹 화면처럼 상태가 바뀌면 Redraw()로 본문을 통째로 다시 그린다.

using UnityEngine;

namespace SpellboundVR.UI
{
    public abstract class MetaScreen
    {
        public MetaUiRoot Ui { get; private set; }
        protected RectTransform Body { get; private set; }

        /// <summary>머리글 제목 (null = 머리글 없는 몰입 화면: 매칭 대기 · 카운트다운 · 결과)</summary>
        public abstract string Title { get; }
        /// <summary>머리글 오른쪽 "로비" 버튼</summary>
        public virtual bool ShowBackToLobby => true;
        /// <summary>true = 배경(마법진 판)을 숨기고 내용만 띄운다 — 경기 중 메뉴처럼 뒤의 전장이 보여야 할 때</summary>
        public virtual bool Overlay => false;
        /// <summary>배경 분위기 색 (프로토타입: 기본 보라, 회원가입 청록, 패배 빨강)</summary>
        public virtual Color Mood => UiTheme.Purple;
        /// <summary>배경 마법진 밝기 (0~1)</summary>
        public virtual float CircleAlpha => 0.22f;

        public bool IsOpen { get; private set; }

        internal void Attach(MetaUiRoot ui) => Ui = ui;

        internal void Open(RectTransform body)
        {
            Body = body;
            IsOpen = true;
            MetaState.OnChanged += HandleStateChanged;
            OnOpen();
            Redraw();
        }

        internal void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            MetaState.OnChanged -= HandleStateChanged;
            OnClose();
        }

        /// <summary>본문 다시 그리기</summary>
        public void Redraw()
        {
            if (!IsOpen || Body == null) return;
            UiKit.Clear(Body);
            Ui.ClearHeaderRight();
            Build(Body);
        }

        protected abstract void Build(RectTransform body);

        /// <summary>다른 화면으로 가기 전 확인 (false를 돌려주면 이동을 멈추고, 확인 뒤 proceed()를 부른다) — C09 저장 안 한 덱</summary>
        public virtual bool CanLeave(System.Action proceed) => true;

        protected virtual void OnOpen() { }
        protected virtual void OnClose() { }
        public virtual void Tick(float dt) { }

        /// <summary>MetaState 가 바뀌었을 때 (기본: 프로필 · 덱 · 우편 변경이면 다시 그리기)</summary>
        protected virtual void HandleStateChanged(string key) { }
    }
}
