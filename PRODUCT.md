# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Stack

Static HTML/CSS/JS with no build step (user's choice, 2026-10-13), served from `Backend/static/` by the single-file Python server `Backend/wow_server.py`. All server calls go through `Backend/static/app/wow-server-api.js`. The earlier React prototype bundle is replaced, not extended.

## Users

- **Players** of War of Wizards: they sign in, build an 8-card spell deck, find a match (ranked queue, friendly room with a friend, or bot practice), then play the 1:1 duel. The duel itself is a Unity game (Meta Quest 3 / PC, hand-tracked casting); the web is where everything around the duel happens.
- **The student team and its audience**: the same screens are shown in class presentations and demos on a PC browser, often with a webcam hand tracker running beside it.

## Product Purpose

A 1:1 wizard duel where spells are cast by drawing runes with your hand. The web front end owns the meta game (account, deck, social, matchmaking, results) and hosts the game screen: the real Unity WebGL game will be built and placed inside the web page; until then the web game screen shows the game UI itself (HP, eight spell slots, rune drawing, cast state, lane aim) and forwards casts to Unity through the server.

Success: a player can go from sign-in to a match and back to a recorded result without confusion, and a demo audience understands within seconds that drawing a rune is how you cast.

## Positioning

The rune is the interface. Each of the 19 spells is a pair of team-made images: a rune you draw (left) and the magic circle that fires when it is recognized (right). Drawing the rune, not pressing a button, is the act of casting; every screen should make that pairing legible.

## Operating Context

- Korean-language UI; PC browser at desktop width is the primary viewport, phones secondary.
- Local Python server (`run_server.bat`, http://localhost:8080), SQLite database, admin console at `/#/admin` (admin role). Unknown paths get `404.html`; a dead or failing server shows `50x.html`.
- Hand input: MediaPipe webcam tracker (`AI/MediaPipe`) or VR hand tracking; in the browser, drawing with the mouse on the rune pad. Gestures: right-hand pinch = draw, fist = arm, index point = aim, open palm = cancel; left-hand swipe = move lane.
- Unity side: spell signals arrive by UDP 127.0.0.1:5067 and SSE `spell` events; Unity WebGL builds talk to the page through `WowBridge.jslib` (`wow:<event>` CustomEvents / `postMessage` with `source: "wow-unity"`) and share the login token key `wow.spring.token`.

## Capabilities and Constraints

- 19 spells: S01–S13 normal (5 s cooldown), H01–H06 high power (20 s cooldown, max 2 per deck, 3 casts per game). Deck is exactly 8 distinct spells.
- Spell kinds: projectile, area, beam, buff, summon, install, global; aimed spells pick a lane (L / C / R) and depth.
- Modes: ranked (rating ±16; server pairs players by rating window, bot after 15 s), friendly room (6-character code, invite friends by mail, ready/start/kick), bot practice (no rating change).
- Social: friend requests (incoming / outgoing / accepted), presence (online), mailbox with rewards and room invites, notices with read state, ranking (top 100).
- Settings stored per browser: camera/gesture sensitivity, hold frames, SFX/BGM, spatial audio, haptics, snap turning, comfort vignette, subtitles, large UI, color-blind mode.
- Rune recognition runs on the server ($P point-cloud, multi-stroke, candidates limited to ready slots).
- Google sign-in works once a Google OAuth client ID is configured; without it the button explains the setup.
- Undecided: how the Unity WebGL build is loaded (expected under `/game/`), and which HUD values Unity will report back to the page.

## Brand Commitments

- Name: War of Wizards (워 오브 위자드).
- The team's spell artwork is binding: rune drawings and magic circles under `Backend/static/assets/skills/{rune,rune_line,circle}/<code>.png`, swapped by replacing files. Spell names and codes follow the team's skill sheet (S01 연막 … H06 소환탑).

## Evidence on Hand

- 57 spell images (19 × rune, rune line, magic circle) in `Backend/static/assets/skills/`.
- Demo accounts `demo@wow.dev` / `demo1234`, `admin@wow.dev` / `admin1234`; 18 demo NPC wizards (Merlin, Aria, …) are synthetic and exist only to populate ranking and friends.
- No real player counts, testimonials, store pages, or press exist; none may be invented.

## Product Principles

1. The rune is the button: wherever a spell appears, its rune and its magic circle appear together.
2. Unity owns the battle; the web owns everything around it and must hand over cleanly (token, deck, match info in; result out).
3. Every flow ends somewhere recorded: matches, ratings, friendships, and mail are server truth, never browser-only.
4. Demo-legible: a first-time audience should understand the screen from across a classroom.

## Accessibility & Inclusion

Keep the existing comfort options (color-blind mode, subtitles, large UI, comfort vignette) working; Korean text must stay legible at projector distance.
