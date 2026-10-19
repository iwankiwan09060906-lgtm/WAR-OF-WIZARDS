---
version: 1
slug: "backend-static-index-html"
primary_target: "Backend/static/index.html"
related_targets: []
---

# Surface brief — War of Wizards web game client (Backend/static)

Scope: the whole web front end for every feature wow_server.py exposes: sign-in / sign-up / guest / password reset / Google, lobby, spellbook (deck), ranked party matching → match room → Unity connection → result, friendly rooms, friends + presence, mailbox + notices + invites, ranking, my page (history, nickname, password), settings, rune practice (server rune recognition + spell signal to Unity), admin console, and 404 / 5xx error pages. Visitor mode: Operate (players and the team complete tasks), shown in class demos.

Audience and job: Korean student players and their demo audience. Sign in, keep a deck, find a 1:1 opponent through the Python server, hand over to the Unity VR duel, read the recorded result.

Constraints: static HTML/CSS/JS without build; server calls only through app/wow-server-api.js; team spell art (rune, rune line, magic circle PNGs) used unaltered; the Unity game (video "4조 XR") will be embedded later; no battlefield map on the web (user: "맵이 나오지 않도록, 마법사 1:1 이 기준").

Chosen direction: user-pinned (2026-10-14) — the composition of the team UI prototype artifact (MUFmcv7sUpN3zWsCpCm9bB: login/signup, main lobby with a left "1:1 마법 대전" card and six right tiles, my page, settings, friends, friendly room, mailbox, ranking, spellbook, queue, countdown, battle HUD, result, DEV screen list), restyled as a Steam game client with light cyberpunk cuts. User words: "너무 ai 스럽다 … 사이버펑크 느낌? 스팀 게임느낌으로", then "더 깔끔하고 편안하며 어떤게임인지 알도록". Supersedes the dusk wizard-HUD direction and the trading-card-binder roll (seed 7a7b7ace), both rejected.

## Direction contract

THESIS: A calm Steam-style game client for a VR wizard 1:1 duel: dark navy panels, one green PLAY action per screen, and the game explained on sight by the blue-vs-red wizard pair and the four cast steps (룬 그리기 → 준비 → 조준 → 발동). Refuses purple neon, glow halos, decorative grids, Cinzel-style fantasy display type, and any battlefield map.

OWN-WORLD: Navy ramp #0b1017 / #0f161f / #172231 / #1d2a3b with hairlines #273649 / #34485f; text #e3eaf1 / #a9b7c6 / #7d8ea1. Colour carries meaning only: green #6fb92a (gradient to #86cf3a) = the primary action, with one recorded exception: on the battle HUD a spell slot's READY state is green (1px green ring + green READY text, never a filled block), because the user's reference — the team's Unity game video "4조 XR" (YouTube aCmBbhvxsFE), which the user named as the game to build from — shows green READY slots, and the user delegated design calls ("너가 판단해봐라"); cyan #3cc8ff = me and information; pink-red #ff5470 = opponent and danger; amber #f3b53f = high-power spells (also the default warning note and the waiting dot). Small on/success indicators (online dot, checked switch, success toast, countdown GO) may use green as a dot or hairline, never as a filled block. Ranking medal colours (#ffd166 / #d9e2ec / #e09f5e) appear on the ranking table only. No signal yellow. Noto Sans KR for all Korean text, Chakra Petch for numbers, codes and English labels. Cyber touch = chamfered corners via clip-path (cards 12px with a thin diagonal corner line; buttons 8px; skill cards 10px and HUD slots 8px on one corner); no glow, no grid, no scanlines, no side stripes, no tracked-caps kickers. Team spell art (rune line drawings, magic circle PNGs) used unaltered; rune drawings render as light lines on dark tiles, circles sit in round slots, amber ring for high-power.

STORY: A first-time viewer reads "WAR OF WIZARDS — 손으로 룬을 그리면 마법이 나가는 VR 마법사 1:1 대전" beside two facing wizards and the four cast steps, signs in, sees the same duel card in the lobby with one green 랭킹전 매칭, waits while the red silhouette is searched for, counts down 3-2-1 with both wizards, plays in the Unity window framed by the web HUD (HP bars, cast steps, rune pad, eight READY slots), and lands on VICTORY / DEFEAT with the rating change.

FIRST VIEWPORT: Login: split screen, left art column (logo, one-line pitch, blue-vs-red wizards, four cast steps, three rune→circle pairs), right form card (email, password, green 로그인, Google, guest name + 게스트 입장, demo). Every signed-in screen: header with the "WAR OF WIZARDS" wordmark (links to the lobby) | hairline divider | h1 screen name, actions on the right. Lobby: that header (h1 메인 로비, nickname/tier, logout); left battle card (duel hero with dark foe silhouette, "1 : 1 마법 대전", cast steps, green 랭킹전 매칭, 친선전 / 봇 연습전, deck strip); right 2×3 tiles (마법서, 마이페이지, 친구, 우편함·공지, 랭킹, 환경설정) and a 룬 연습장 row.

FORM: User-pinned composition (team prototype) with a Steam-client skin. Signature move: the opponent is a dark red-outlined silhouette until the server pairs you, then lights up as the red wizard on the countdown screen; the same wizard pair carries login, lobby, queue, countdown, battle stage, result and the 404 page so the game is legible on every screen.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
