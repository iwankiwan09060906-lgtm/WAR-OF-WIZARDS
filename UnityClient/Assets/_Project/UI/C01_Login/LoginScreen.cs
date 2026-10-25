// C01 로그인 · 회원가입 — VR UI 프로토타입(Figma Make, 이식 후 삭제)의 LoginScreen.tsx · SignUpScreen.tsx 를 Unity로 옮김
//   가운데 카드 하나: 작은 머리말("VR MAGIC BATTLE" / "JOIN THE REALM") · Cinzel 제목(그라데이션) · 마름모 구분선 · 입력칸 · 보라 버튼
//   · "또는" · 아래 전환 링크. 회원가입은 배경이 청록으로 바뀌고 비밀번호 강도 막대(약함~매우 강함)가 붙는다.
//   "Google 계정으로 계속하기" = 기기 로그인(Device Flow, Backend/GoogleDeviceAuth): Quest에는 Google 로그인 창이 없으니
//   카드에 주소(google.com/device) · 코드 · 남은 시간을 띄우고, 유저가 휴대폰 · PC에서 승인할 때까지 기다렸다가 게임 서버 토큰으로 바꾼다.
//   그 아래 게스트 입장(이름만으로 바로).
//   덧붙인 것: 데모 계정 · 서버 없이 오프라인 시작 · 서버 주소 바꾸기(Quest는 서버 PC의 IP가 필요) · 비밀번호 재설정 요청.

using System.Text.RegularExpressions;
using SpellboundVR.Backend;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class LoginScreen : MetaScreen
    {
        private static readonly Regex NickRule = new Regex("^[A-Za-z0-9가-힣_]{2,12}$");
        private static readonly string[] StrengthLabels = { "", "약함", "보통", "강함", "매우 강함" };
        private static readonly Color[] StrengthColors = { UiTheme.Text3, UiTheme.Enemy, UiTheme.Orange, UiTheme.Cyan, UiTheme.Hex(0x22c55e) };

        private readonly bool _signup;
        private bool? _serverOk;
        private bool _editServer;
        private Text _error;

        // Google 기기 로그인
        private bool _googleMode;
        private GoogleDeviceAuth _google;
        private string _googleStatus = "";
        private bool _googleBusy;

        public LoginScreen(bool signup) => _signup = signup;

        public override string Title => null;
        public override bool ShowBackToLobby => false;
        public override Color Mood => _signup ? UiTheme.Cyan : UiTheme.Purple;
        public override float CircleAlpha => 0.2f;

        private bool _discovering;
        private static bool s_autoDiscovered;
        private string _discoverNote = "";

        protected override void OnOpen()
        {
            _serverOk = null;
            CheckServer();
        }

        /// <summary>같은 Wi-Fi에서 서버 PC를 찾아 주소를 자동으로 바꾼다 (헤드셋에서 IP를 칠 필요 없음)</summary>
        private void Discover()
        {
            if (_discovering) return;
            _discovering = true;
            _discoverNote = "같은 Wi-Fi에서 서버를 찾는 중… (최대 4초)";
            Redraw();
            BackendClient.Instance.DiscoverServer((ok, info) =>
            {
                _discovering = false;
                _discoverNote = ok ? "서버를 찾았습니다: " + info : info;
                _serverOk = null;
                if (IsOpen) Redraw();
                if (ok) CheckServer();
            });
        }

        protected override void OnClose() => StopGoogle();

        private void CheckServer()
        {
            BackendClient.Instance.Get<HealthDto>("/api/health", _ => { _serverOk = true; Redraw(); }, _ =>
            {
                _serverOk = false;
                Redraw();
                // 서버에 못 붙으면 앱을 켠 뒤 한 번 자동으로 찾아 본다
                if (!s_autoDiscovered && !_discovering) { s_autoDiscovered = true; Discover(); }
            });
        }

        protected override void Build(RectTransform body)
        {
            var center = UiKit.Stretch(UiKit.VBox(body, 0, 0, TextAnchor.MiddleCenter, "Auth"));
            var lg = center.GetComponent<VerticalLayoutGroup>();
            lg.childForceExpandWidth = false;
            lg.childForceExpandHeight = false;
            var card = UiKit.Card(center, 44, _signup ? 10 : 12, "Card", new Color(8 / 255f, 6 / 255f, 24 / 255f, 0.86f),
                                  UiTheme.Alpha(_signup ? UiTheme.Cyan : UiTheme.Purple, 0.32f));
            card.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(56, 56, _signup ? 34 : 40, _signup ? 30 : 34);
            UiKit.Size(card, 700, -1);

            BuildLogo(card);
            if (_googleMode) BuildGoogle(card);
            else if (_signup) BuildSignup(card);
            else BuildLogin(card);
            BuildServer(card);
        }

        // ── 로고 ───────────────────────────────────────────────

        private void BuildLogo(Transform card)
        {
            var logo = UiKit.VBox(card, 6, 0, TextAnchor.MiddleCenter, "Logo");
            UiKit.Kicker(logo, _signup ? "Join the Realm" : "VR Magic Battle", _signup ? UiTheme.PurpleLight : UiTheme.Cyan, TextAnchor.MiddleCenter, 15);
            var title = UiKit.Display(logo, _signup ? "마법사 등록" : "War of Wizards", _signup ? 50 : 56, null, TextAnchor.MiddleCenter, true);
            if (_signup) UiGradient.Apply(title, UiTheme.Hex(0xa5f3fc), UiTheme.Cyan, UiTheme.Purple);
            else UiGradient.Apply(title, UiTheme.PurplePale, UiTheme.Purple, UiTheme.Cyan);
            UiKit.Divider(logo, 200);
            UiKit.Label(logo, _signup ? "새 계정을 생성하고 전장에 참여하세요" : "마법사 계정으로 로그인", UiTheme.FontSmall, UiTheme.PurpleLight, false, TextAnchor.MiddleCenter);
        }

        // ── 로그인 ─────────────────────────────────────────────

        private void BuildLogin(Transform card)
        {
            var email = UiKit.Field(card, "이메일", "wizard@example.com", false, 80);
            email.contentType = InputField.ContentType.EmailAddress;
            var pw = UiKit.Field(card, "비밀번호", "6자 이상", true, 64);
            Peek(pw);

            var forgotRow = UiKit.HBox(card, 0, 0, TextAnchor.MiddleRight, "Forgot");
            forgotRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            UiKit.Spacer(forgotRow);
            Button forgot = null;
            forgot = SmallLink(forgotRow, "비밀번호를 잊으셨나요?", UiTheme.PurpleLight, () =>
            {
                string e = email.text.Trim();
                if (!e.Contains("@")) { Err("재설정할 계정의 이메일을 먼저 입력해주세요."); return; }
                var done = UiKit.Busy(forgot, "요청 중…");
                AuthService.RequestPasswordReset(e, msg => { done(); Err(""); MetaUiRoot.Toast(msg, Tone.Cyan); }, er => { done(); Err(er); });
            });

            _error = UiKit.Label(card, "", UiTheme.FontSmall - 1, UiTheme.Enemy, false, TextAnchor.MiddleLeft, "Error");
            Button submit = null;
            submit = UiKit.Button(card, "로그인", BtnStyle.Play, () =>
            {
                Err("");
                string e = email.text.Trim(), p = pw.text;
                if (!e.Contains("@")) { Err("올바른 이메일 주소를 입력해주세요."); return; }
                if (p.Length < 6) { Err("비밀번호는 6자 이상이어야 합니다."); return; }
                var done = UiKit.Busy(submit, "주문 시전 중…");
                AuthService.Login(e, p, _ => Enter(done), er => { done(); Err(er); });
            }, -1, 70, UiTheme.FontTitle - 2);
            UiKit.Halo(submit.transform, UiTheme.Purple);

            Or(card);

            // Google 계정으로 계속하기 (프로토타입 버튼 모양: 옅은 판 + 보라 테두리 + 컬러 G)
            var gb = UiKit.Button(card, "", BtnStyle.Ghost, StartGoogle, -1, 60);
            gb.GetComponent<Image>().sprite = UiTheme.Cut(new Color(1f, 1f, 1f, 0.03f), UiTheme.Alpha(UiTheme.Purple, 0.3f), 0, 2);
            UiKit.Clear(gb.transform);
            var gRow = UiKit.Stretch(UiKit.HBox(gb.transform, 14, 0, TextAnchor.MiddleCenter, "Row"));
            gRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            UiKit.Label(gRow, "<color=#4285F4>G</color><color=#EA4335>o</color><color=#FBBC05>o</color><color=#4285F4>g</color><color=#34A853>l</color><color=#EA4335>e</color>",
                        UiTheme.FontTitle - 2, Color.white, true).horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Label(gRow, "계정으로 계속하기", UiTheme.FontBody, UiTheme.Hex(0xddd6fe), true).horizontalOverflow = HorizontalWrapMode.Overflow;

            // 게스트 입장 (이름을 비우면 자동 이름)
            var g = UiKit.HBox(card, 10, 0, TextAnchor.MiddleLeft, "Guest");
            var gnick = UiKit.Input(g, "게스트 이름 (비우면 자동)", false, 12);
            UiKit.Size(gnick, -1, -1, 1);
            Button guest = null;
            guest = UiKit.Button(g, "게스트로 계속하기", BtnStyle.Info, () =>
            {
                string n = gnick.text.Trim();
                if (n.Length == 0) n = "Guest" + Random.Range(100, 1000); // VR에서 바로 들어갈 수 있게
                if (!NickRule.IsMatch(n)) { Err("이름은 2~12자, 한글·영문·숫자·_ 만 쓸 수 있습니다."); return; }
                var done = UiKit.Busy(guest, "입장 중…");
                AuthService.LoginAsGuest(n, _ => Enter(done), er => { done(); Err(er); });
            }, 250, UiTheme.InputHeight);

            var extra = UiKit.HBox(card, 10, 0, TextAnchor.MiddleCenter, "Extra");
            extra.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Button demo = null;
            demo = UiKit.Button(extra, "데모 계정으로 시작", BtnStyle.Ghost, () =>
            {
                var done = UiKit.Busy(demo, "입장 중…");
                AuthService.Login("demo@wow.dev", "demo1234", _ => Enter(done), er => { done(); Err(er); });
            }, -1, 54, UiTheme.FontSmall);
            UiKit.Size(demo, -1, -1, 1);
            var offline = UiKit.Button(extra, "서버 없이 시작 (봇 연습)", BtnStyle.Ghost, () => MetaFlow.Instance?.StartOffline(), -1, 54, UiTheme.FontSmall);
            UiKit.Size(offline, -1, -1, 1);

            Switcher(card, "계정이 없으신가요?", "회원가입", UiTheme.CyanLight, MetaScreenId.Signup);
        }

        // ── Google 기기 로그인 (Device Flow) ───────────────────

        private void StartGoogle()
        {
            Err("");
            _googleMode = true;
            _googleBusy = false;
            _googleStatus = "Google에 로그인 코드를 요청하는 중…";
            if (_google == null)
            {
                _google = new GameObject("[GoogleDeviceAuth]").AddComponent<GoogleDeviceAuth>();
                _google.OnStatus += m => { _googleStatus = m; };
                _google.OnCodeIssued += _ => Redraw();
                _google.OnFailed += m => { _googleStatus = m; Redraw(); };
                _google.OnSucceeded += HandleGoogleTokens;
            }
            _google.Begin();
            Redraw();
        }

        private void StopGoogle()
        {
            _googleMode = false;
            _googleBusy = false;
            if (_google != null) Object.Destroy(_google.gameObject);
            _google = null;
        }

        private void HandleGoogleTokens(GoogleDeviceAuth.GoogleTokens tokens)
        {
            _googleBusy = true;
            _googleStatus = "Google 승인 완료 — 게임 서버에 로그인하는 중…";
            Redraw();
            AuthService.LoginWithGoogle(tokens.id_token, tokens.access_token,
                user =>
                {
                    MetaUiRoot.Toast($"{user.nickname} 마법사님, 환영합니다!", Tone.Play);
                    Enter(StopGoogle);
                },
                er =>
                {
                    _googleBusy = false;
                    _googleStatus = "게임 서버 로그인 실패: " + er;
                    if (_google != null) _google.Cancel();
                    Redraw();
                });
        }

        private void BuildGoogle(Transform card)
        {
            var dc = _google != null ? _google.Current : null;
            bool waiting = dc != null && _google.IsRunning;

            var box = UiKit.Card(card, 26, 10, "Device", new Color(0f, 0f, 0f, 0.35f), UiTheme.Alpha(UiTheme.Cyan, 0.4f));
            box.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            UiKit.Kicker(box, "Google Device Login", UiTheme.CyanLight, TextAnchor.MiddleCenter, 14);
            UiKit.Label(box, "1. 휴대폰이나 PC 브라우저에서 이 주소를 여세요", UiTheme.FontSmall, UiTheme.Text2, false, TextAnchor.MiddleCenter);
            var url = UiKit.Label(box, dc != null ? dc.Url : "…", UiTheme.FontTitle, UiTheme.CyanLight, true, TextAnchor.MiddleCenter);
            url.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Spacer(box, 0, 6);
            UiKit.Label(box, "2. 아래 코드를 입력하고 Google 계정을 고르세요", UiTheme.FontSmall, UiTheme.Text2, false, TextAnchor.MiddleCenter);
            var code = UiKit.Display(box, dc != null ? dc.user_code : "- - - -", 64, UiTheme.Text, TextAnchor.MiddleCenter, true);
            UiKit.Size(code, -1, 84);
            var expiry = UiKit.Label(box, waiting ? GoogleDeviceAuth.FormatExpiry(Mathf.CeilToInt(_google.SecondsLeft)) : " ",
                                     UiTheme.FontSmall, UiTheme.Amber, true, TextAnchor.MiddleCenter);

            var status = UiKit.Label(card, _googleStatus, UiTheme.FontSmall - 1,
                                     waiting || _googleBusy ? UiTheme.PurpleLight : UiTheme.Enemy, false, TextAnchor.MiddleCenter, "Status");
            if (_google != null)
            {
                // GoogleDeviceAuth 가 남은 시간 · 진행 문구를 직접 갱신한다
                _google.urlText = url;
                _google.userCodeText = code;
                _google.expiryText = waiting ? expiry : null;
                _google.statusText = status;
            }

            var row = UiKit.HBox(card, 10, 0, TextAnchor.MiddleCenter, "Actions");
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            if (!waiting && !_googleBusy)
                UiKit.Size(UiKit.Button(row, "새 코드 받기", BtnStyle.Info, StartGoogle, -1, 58), -1, -1, 1);
            if (waiting && !Application.isMobilePlatform)
                UiKit.Size(UiKit.Button(row, "이 PC에서 브라우저 열기", BtnStyle.Info, () => Application.OpenURL(dc.Url), -1, 58), -1, -1, 1);
            UiKit.Size(UiKit.Button(row, "취소", BtnStyle.Ghost, () => { StopGoogle(); Redraw(); }, -1, 58), -1, -1, 1);
            UiKit.Label(card, "처음 로그인하는 Google 계정은 마법사 계정이 자동으로 만들어집니다.", UiTheme.FontSmall - 4, UiTheme.Text3, false, TextAnchor.MiddleCenter);
        }

        // ── 회원가입 ───────────────────────────────────────────

        private void BuildSignup(Transform card)
        {
            var nick = UiKit.Field(card, "마법사 이름 (닉네임)", "Arcane_Wizard", false, 12);
            var nickHint = UiKit.Label(card, "2~12자, 한글·영문·숫자·_ 사용 가능", UiTheme.FontSmall - 3, UiTheme.Text3, false, TextAnchor.MiddleLeft, "NickHint");
            nick.onEndEdit.AddListener(v =>
            {
                v = v.Trim();
                if (v.Length == 0) return;
                if (!NickRule.IsMatch(v)) { nickHint.color = UiTheme.Enemy; nickHint.text = "닉네임은 2~12자, 한글·영문·숫자·_ 만 사용할 수 있습니다."; return; }
                AuthService.CheckNickname(v, ok =>
                {
                    if (nickHint == null) return;
                    nickHint.color = ok ? UiTheme.Green : UiTheme.Enemy;
                    nickHint.text = ok ? $"'{v}' 은(는) 쓸 수 있는 이름입니다." : "이미 사용 중인 닉네임입니다.";
                });
            });
            var email = UiKit.Field(card, "이메일", "wizard@example.com", false, 80);
            email.contentType = InputField.ContentType.EmailAddress;

            // 비밀번호 · 확인을 한 줄에 (프로토타입 2열)
            var pwRow = UiKit.HBox(card, 14, 0, TextAnchor.UpperLeft, "Passwords");
            pwRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            var pw = UiKit.Field(pwRow, "비밀번호", "6자 이상", true, 64);
            UiKit.Size(pw.transform.parent, -1, -1, 1);
            Peek(pw);
            var pw2 = UiKit.Field(pwRow, "비밀번호 확인", "한 번 더", true, 64);
            UiKit.Size(pw2.transform.parent, -1, -1, 1);
            var strength = StrengthBar(card);
            pw.onValueChanged.AddListener(v => strength(v));
            strength("");

            var tRow = UiKit.HBox(card, 12, 0, TextAnchor.MiddleLeft, "Terms");
            var terms = UiKit.Switch(tRow, false, null, UiTheme.Cyan);
            UiKit.Label(tRow, $"<color={UiKit.Hex(UiTheme.PurplePale)}>[필수]</color> <color={UiKit.Hex(UiTheme.CyanLight)}>이용약관</color> 및 <color={UiKit.Hex(UiTheme.CyanLight)}>개인정보처리방침</color>에 동의합니다.",
                        UiTheme.FontSmall - 1, UiTheme.PurpleLight);

            _error = UiKit.Label(card, "", UiTheme.FontSmall - 1, UiTheme.Enemy, false, TextAnchor.MiddleLeft, "Error");
            Button submit = null;
            submit = UiKit.Button(card, "마법사 등록하기", BtnStyle.Info, () =>
            {
                Err("");
                string n = nick.text.Trim(), e = email.text.Trim(), p = pw.text;
                if (!NickRule.IsMatch(n)) { Err("닉네임은 2~12자, 한글·영문·숫자·_ 만 사용할 수 있습니다."); return; }
                if (!e.Contains("@")) { Err("올바른 이메일 주소를 입력해주세요."); return; }
                if (p.Length < 6) { Err("비밀번호는 6자 이상이어야 합니다."); return; }
                if (p != pw2.text) { Err("비밀번호가 일치하지 않습니다."); return; }
                if (!terms.isOn) { Err("필수 약관에 동의해주세요."); return; }
                var done = UiKit.Busy(submit, "계정 생성 중…");
                AuthService.Register(e, p, n, user => { MetaUiRoot.Toast($"{user.nickname} 마법사님, 환영합니다!", Tone.Play); Enter(done); }, er => { done(); Err(er); });
            }, -1, 70, UiTheme.FontTitle - 2);
            UiKit.Halo(submit.transform, UiTheme.Cyan);

            Switcher(card, "이미 계정이 있으신가요?", "로그인", UiTheme.PurpleLight, MetaScreenId.Login);
        }

        /// <summary>프로토타입 StrengthBar — 8자 이상 · 대문자 · 숫자 · 특수문자 하나마다 한 칸</summary>
        private static System.Action<string> StrengthBar(Transform card)
        {
            var box = UiKit.VBox(card, 4, 0, TextAnchor.UpperLeft, "Strength");
            var bars = UiKit.HBox(box, 6, 0, TextAnchor.MiddleLeft, "Bars");
            bars.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            var cells = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                cells[i] = UiKit.Panel(bars, UiTheme.Alpha(UiTheme.Purple, 0.15f));
                UiKit.Size(cells[i], -1, 6, 1);
            }
            var label = UiKit.Label(box, "", UiTheme.FontSmall - 4, UiTheme.Text3, true);
            return pw =>
            {
                int s = 0;
                if (!string.IsNullOrEmpty(pw))
                {
                    if (pw.Length >= 8) s++;
                    if (Regex.IsMatch(pw, "[A-Z]")) s++;
                    if (Regex.IsMatch(pw, "[0-9]")) s++;
                    if (Regex.IsMatch(pw, "[^A-Za-z0-9]")) s++;
                }
                for (int i = 0; i < 4; i++) cells[i].color = i < s ? StrengthColors[s] : UiTheme.Alpha(UiTheme.Purple, 0.15f);
                label.color = StrengthColors[s];
                label.text = string.IsNullOrEmpty(pw) ? "8자 이상 · 대문자 · 숫자 · 특수문자를 섞으면 강해집니다" : "비밀번호 강도: " + StrengthLabels[s];
            };
        }

        // ── 서버 상태 · 주소 (Quest에서는 서버 PC의 IP로 바꿔야 한다) ──

        private void BuildServer(Transform card)
        {
            var status = UiKit.HBox(card, 10, 0, TextAnchor.MiddleCenter, "Server");
            UiKit.Dot(status, _serverOk == null ? UiTheme.Amber : _serverOk.Value ? UiTheme.Green : UiTheme.Enemy, 10);
            UiKit.Label(status, (_serverOk == null ? "서버 확인 중… " : _serverOk.Value ? "게임 서버 연결됨 · " : "서버 꺼짐 · ") + BackendClient.Instance.ServerUrl,
                        UiTheme.FontSmall - 4, UiTheme.Text3).horizontalOverflow = HorizontalWrapMode.Overflow;
            SmallLink(status, "서버 자동 찾기", UiTheme.CyanLight, Discover);
            SmallLink(status, "주소 변경", UiTheme.CyanLight, () => { _editServer = !_editServer; Redraw(); });
            if (!string.IsNullOrEmpty(_discoverNote)) UiKit.Label(card, _discoverNote, UiTheme.FontSmall - 4, _discovering ? UiTheme.Amber : UiTheme.Text2, false, TextAnchor.MiddleCenter);
            if (_serverOk == false)
                UiKit.Label(card, "PC에서 Backend/run_server.bat 을 켜고 Quest와 같은 Wi-Fi인지 확인하세요.\n(처음 한 번 open-firewall.bat 을 관리자 권한으로 실행)", UiTheme.FontSmall - 4, UiTheme.Text3, false, TextAnchor.MiddleCenter);
            if (!_editServer) return;
            var r = UiKit.HBox(card, 10, 0, TextAnchor.MiddleLeft, "ServerUrl");
            var url = UiKit.Input(r, "서버 PC IP (예: 192.168.0.10)", false, 80, 52);
            url.text = BackendClient.Instance.ServerUrl;
            UiKit.Size(url, -1, -1, 1);
            UiKit.Button(r, "저장", BtnStyle.Default, () =>
            {
                BackendClient.Instance.SetServerUrl(url.text); // IP만 넣어도 http:// · :8080 을 붙인다, 비우면 빌드 기본 주소
                _editServer = false;
                _serverOk = null;
                Redraw();
                CheckServer();
            }, 110, 52);
        }

        // ── 작은 부품 ──────────────────────────────────────────

        /// <summary>"또는" 가로줄</summary>
        private static void Or(Transform card)
        {
            var row = UiKit.HBox(card, 16, 0, TextAnchor.MiddleCenter, "Or");
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            UiKit.Size(UiKit.Panel(row, UiTheme.Alpha(UiTheme.Purple, 0.3f)), -1, 1, 1);
            UiKit.Label(row, "또는", UiTheme.FontSmall - 3, UiTheme.Text3, false, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Size(UiKit.Panel(row, UiTheme.Alpha(UiTheme.Purple, 0.3f)), -1, 1, 1);
        }

        /// <summary>"계정이 없으신가요? 회원가입" — 앞은 흐린 글, 뒤는 링크</summary>
        private void Switcher(Transform card, string lead, string link, Color linkColor, MetaScreenId to)
        {
            var row = UiKit.HBox(card, 8, 0, TextAnchor.MiddleCenter, "Switch");
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            UiKit.Label(row, lead, UiTheme.FontSmall - 1, UiTheme.Text3, false, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
            SmallLink(row, link, linkColor, () => Ui.Go(to), UiTheme.FontSmall - 1, true);
        }

        private static Button SmallLink(Transform parent, string text, Color color, UnityEngine.Events.UnityAction onClick, int size = UiTheme.FontSmall - 3, bool bold = false)
        {
            var b = UiKit.Button(parent, text, BtnStyle.Text, onClick, -1, 40, size);
            var t = b.GetComponentInChildren<Text>();
            t.color = color;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            if (!bold) t.font = UiTheme.Regular;
            UiKit.Size(b, t.preferredWidth + 16, 40);
            UiKit.Stretch(t.rectTransform, 8, 0, 8, 0);
            return b;
        }

        /// <summary>비밀번호 칸 오른쪽 "보기 / 숨김" (프로토타입 눈 아이콘)</summary>
        private static void Peek(InputField input)
        {
            var rt = (RectTransform)input.transform;
            var b = UiKit.Button(rt, "보기", BtnStyle.Text, null, 78, 44, UiTheme.FontSmall - 3);
            var t = b.GetComponentInChildren<Text>();
            t.color = UiTheme.PurpleLight;
            var brt = (RectTransform)b.transform;
            var le = b.GetComponent<LayoutElement>();
            if (le != null) le.ignoreLayout = true;
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
            brt.pivot = new Vector2(1f, 0.5f);
            brt.anchoredPosition = new Vector2(-6f, 0f);
            brt.sizeDelta = new Vector2(78f, 44f);
            ((RectTransform)input.textComponent.transform).offsetMax = new Vector2(-88f, -4f);
            b.onClick.AddListener(() =>
            {
                bool hidden = input.contentType == InputField.ContentType.Password;
                input.contentType = hidden ? InputField.ContentType.Standard : InputField.ContentType.Password;
                input.ForceLabelUpdate();
                t.text = hidden ? "숨김" : "보기";
            });
        }


        private void Err(string message)
        {
            if (_error != null) _error.text = message ?? "";
        }

        private void Enter(System.Action done)
        {
            MetaState.Offline = false;
            MetaState.LoadPlayer(() =>
            {
                done?.Invoke();
                if (IsOpen) Ui.Go(MetaScreenId.Lobby);
            });
        }
    }
}
