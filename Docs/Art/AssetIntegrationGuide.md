# 에셋 투입 가이드 (룬 · 스킬 VFX · 모델)

> 대상: ㅇㅎㅅ(아트) · ㅊㄱㅇ(연결 확인)
> 원칙: **코드는 파일 이름으로 에셋을 찾는다.** 이름 · 폴더 · 피벗만 맞추면 코드 수정 없이 연결된다. 에셋에 C# 스크립트는 붙이지 않는다 (Rule 13).

---

## 0. 공통 규칙

| 항목 | 규칙 |
| --- | --- |
| 렌더 파이프라인 | **Built-in** (URP 아님). 머티리얼은 `Standard` 셰이더, Shader Graph는 **Built-In 타깃**으로 만든다 |
| 단위 · 방향 | 1 unit = 1m, **앞 = +Z**, 위 = +Y |
| 피벗 | 모델: **발바닥(바닥) 중앙**. VFX: 부위별 아래 표 참고 |
| 형식 | 모델은 FBX를 넣은 뒤 **같은 이름의 프리팹**을 만든다 (코드는 FBX가 아니라 프리팹을 찾는다) |
| 등록 | 파일을 넣은 뒤 Unity 메뉴 실행 → `Spellbound > 3. Refresh VFX Catalog` (VFX · 모델), `Spellbound > Assets > Assign Rune Icons` (룬 아이콘) |
| 확인 | Play → 콘솔에 에러가 없고 대체 도형(구체 · 캡슐)이 내 에셋으로 바뀌었는지 본다 |
| 라이선스 | 외부 에셋은 출처를 `Docs/Art/Licenses.md`에 기록 (§3.2) |

---

## 1. 룬

룬은 **아이콘(이미지)**과 **모양(인식용 궤적)** 두 가지다. **이미지는 인식에 쓰이지 않는다.** 인식은 손으로 그린 점 궤적끼리 비교한다($P+).

### 1.1 룬 아이콘 (이미지)

| 항목 | 값 |
| --- | --- |
| 폴더 | `UnityClient/Assets/Art/Runes/Icons/` |
| 파일명 | **`Icon_{SkillId}_{Name}.png`** — 예) `Icon_S03_Fireball.png`, `Icon_S07_Gatling.png`, `Icon_S08_Shield.png`, `Icon_H02_Laser.png` |
| 규격 | PNG, 투명 배경, 정사각형 256×256 권장, 밝은 선(어두운 HUD 위에 표시됨) |
| 등록 | 메뉴 `Spellbound > Assets > Assign Rune Icons` → 각 `Rune_Sxx_*.asset`의 `Icon`에 자동 할당 (알파 · Clamp 설정도 자동) |
| 쓰이는 곳 | HUD 8슬롯 배경, 룬 인식 성공 시 오른손 위 표시 |

> 규칙은 "파일명이 `Icon_` + 스킬 코드로 시작"이다. `Icon_S03.png`도 인식한다.

### 1.2 룬 모양 (인식 템플릿)

| 항목 | 값 |
| --- | --- |
| 저장 위치 | `UnityClient/Assets/_Project/Gesture/Templates/Data/Rune_{SkillId}_{Name}.asset` (점 목록) |
| 디자인 원본(참고용) | `UnityClient/Assets/Art/Runes/Designs/Rune_S03_Fireball.png` — 코드는 읽지 않음, 팀 공유용 |
| 현재 기본 모양 | S03 = 원 ○, S07 = Z, S08 = 역삼각형 ▽ (코드가 만든 도형) |

**새 모양 등록 방법 (디자인 확정 후)**
1. 04_Battle Play → Game 뷰 클릭.
2. 마우스 왼쪽 드래그로 **한 획**에 룬을 그린다 (손으로 그릴 순서 · 방향 그대로).
3. 메뉴 `Spellbound > Runes > Record Last Stroke → S03 Fireball` (해당 스킬 선택).
4. 2~3을 **스킬당 5~10번** 반복 (조금씩 다르게: 크게/작게/기울여서).
5. 기본 도형을 빼고 싶으면 `Rune_S03_*.asset` 인스펙터의 `Samples`에서 `circle_*` 항목을 지운다.
6. 잘못 녹화했으면 `Spellbound > Runes > Clear Recorded Samples` (녹화분만 삭제).

**디자인 규칙 (§9 유사도 검증)**
- 한 획으로 그릴 수 있을 것. 손목 1회 동작, 1~2초 이내.
- **룬의 방향(기울기)도 모양의 일부다.** $P+는 회전을 보정하지 않으므로 V와 ∧는 서로 다른 룬이다. 반대로 같은 룬을 많이 기울여 그리면 인식이 떨어진다.
- 닮은 쌍 금지: 원 ↔ U, 삼각형 ↔ ∧, Z ↔ N/S. 8슬롯에 동시에 들어올 수 있으므로 19종 전체가 서로 달라야 한다.
- 인식 로그(`$P+ → S03 d=2.4 2nd=S08 d2=5.8`)에서 **d2 / d 비율이 1.2 이상**이면 안전하다.

---

## 2. 스킬 VFX (프리팹)

| 항목 | 규칙 (§25.3) |
| --- | --- |
| 폴더 | `UnityClient/Assets/Prefabs/VFX/{SkillId}/` 권장 (예: `Prefabs/VFX/S03/`) — 사실 프로젝트 어디든 이름만 맞으면 됨 |
| 파일명 | **`VFX_{SkillId}_{Name}_{Part}.prefab`** — `{Name}`은 스킬 에셋의 `VfxName` 값 |
| 일회성 | 루트 파티클 **Stop Action = Disable** (끝나면 스스로 꺼져 풀로 돌아감). 안 해도 4초 뒤 강제 회수 |
| 반복형(Loop) | Looping 파티클. 상태 효과가 끝나면 코드가 끈다 |
| 크기 | 범위형(Impact · Field)은 **반경 1m 기준**으로 제작 → 코드가 스킬 반경만큼 확대 |
| 예산 | 프리팹 1개 동시 파티클 ~100 이하 |
| 금지 | C# 스크립트, 외부 텍스처 참조 누락 |

### 2.1 현재 구현된 3종에 필요한 프리팹

| 파일명 | 언제 · 어디에 생성 | 피벗 · 방향 | 수명 |
| --- | --- | --- | --- |
| `VFX_S03_Fireball_Cast` | 시전 순간, **시전자 발판 위 1.5m** | 중심 | 일회성 ~0.5s |
| `VFX_S03_Fireball_Projectile` | 시전 순간, **착탄 지점(바닥)** 에 생성 | 바닥 중심 — **프리팹 안에서 위(약 7m)에서 떨어지는 연출을 스스로 재생** (~0.25s) | 일회성 |
| `VFX_S03_Fireball_Impact` | 시전 순간, 착탄 지점 | 바닥 중심, 반경 1m 제작 (코드가 ×2.6) | 일회성 ~0.5s |
| `VFX_S07_Gatling_Cast` | 연사 시작, 시전자 앞 1.5m | 중심 | 일회성 |
| `VFX_S07_Gatling_Projectile` | **날아가는 탄환 1발** (8발 각각) — 서버 위치를 따라 매 프레임 이동 | 중심, **+Z = 진행 방향** | 코드가 끔 (짧은 트레일 권장) |
| `VFX_S07_Gatling_Impact` | 탄환 1발이 맞을 때마다 (유닛 0.6m / 플레이어 1.2m 높이) | 중심 | 일회성 ~0.3s |
| `VFX_S08_Shield_Cast` | 시전 순간, 시전자 1.2m | 중심 | 일회성 |
| `VFX_S08_Shield_Loop` | 쉴드가 켜져 있는 동안 시전자 위치 | 중심, 반경 ~1m 버블 | **Loop** — 쉴드가 깨지거나 8초 뒤 코드가 끔 |

> 나머지 16종도 같은 규칙이다. `{Name}`은 각 `Spell_Sxx_*.asset`의 `VfxName`에서 확인한다.

---

## 3. 모델 (FBX)

### 3.1 공통 절차

1. FBX를 `UnityClient/Assets/Art/Models/{Category}/`에 넣는다. Import: Scale Factor 1, 필요하면 Materials 추출.
2. FBX를 씬에 끌어다 놓고 머티리얼 · 크기 · 방향(+Z 앞)을 맞춘다.
3. 그것을 `UnityClient/Assets/Prefabs/{Category}/`로 끌어 **프리팹**으로 만든다. 이름은 아래 표 그대로.
4. 메뉴 `Spellbound > 3. Refresh VFX Catalog` (모델도 이 카탈로그로 찾는다).

### 3.2 코드가 이름으로 찾는 모델

| 프리팹 이름 | 용도 | 크기 · 피벗 | 애니메이션 |
| --- | --- | --- | --- |
| `MDL_Minion_Melee` | 근접 미니언 | 키 ~1.2m, 발바닥 중앙 | Animator 컨트롤러에 **int 파라미터 `State`** |
| `MDL_Minion_Ranged` | 원거리 미니언 | ~1.0m | 〃 |
| `MDL_Minion_Brute` | Brute | ~2.0m | 〃 |
| `MDL_Avatar_Wizard` | 상대 플레이어(Away 발판 위) | ~1.8m, 발바닥 중앙 | (현재 미사용) |

**미니언 Animator `State` 값** (코드가 상태가 바뀔 때마다 `SetInteger("State", …)`)

| 값 | 의미 | 권장 클립 |
| --- | --- | --- |
| 0 | 이동 | Walk (Loop) |
| 1 | 공격 중 | Attack (Loop, 공격 간격 근접 1.0s · 원거리 1.2s · Brute 1.4s) |
| 2 | 돌파 이동 (상대 넥서스 파괴 후) | Walk 또는 Run |
| 3 | 빙결 (H01 프리즈, 추후) | Idle 정지 |

- 파라미터 `State`가 없으면 코드는 Animator를 건드리지 않는다 (에러 없음).
- **사망 시 유닛은 즉시 사라진다.** Death 클립은 지금 재생되지 않는다 → 필요하면 ㅊㄱㅇ에게 "사망 VFX/애니 훅" 요청.
- **팀 컬러**: 코드가 렌더러에 `_Color` · `_BaseColor` · `_TeamColor`를 파랑(Home) / 빨강(Away)으로 곱한다 (§25.4). Standard 셰이더면 `_Color`가 텍스처에 곱해지므로, **팀 색이 들어갈 부분은 흰색~회색**으로 칠한다. 더 정교하게 하려면 마스크를 쓰는 Shader Graph에 `_TeamColor` 프로퍼티를 만든다.
- **애니메이션 출처**: Mixamo 등에서 받을 때 Walk · Attack · Idle은 Loop Time 체크.

### 3.3 씬에 직접 배치하는 것 (이름이 아니라 위치로 연결)

`04_Battle` 씬 `Arena` 아래 구조. **빈 부모 오브젝트 = 게임 로직 위치이므로 옮기지 말 것.** 모양은 그 아래 `Body`만 교체한다.

| 오브젝트 | 위치 (z) | 교체 방법 | 높이 권장 |
| --- | --- | --- | --- |
| `Arena/Home/Home_Nexus` | 5 | `Body`(큐브) 삭제 → 넥서스 모델을 자식으로, 로컬 (0,0,0) | **≤ 1.6m** |
| `Arena/Home/Home_Tower` | 8 | `Body`(실린더) 삭제 → 타워 모델 | **≤ 2.4m** |
| `Arena/Away/Away_Tower` · `Away_Nexus` | 12 · 15 | 같은 방식 (부모가 180° 돌아 있어 Home을 마주 봄) | Away 쪽은 높아도 됨 |
| `Arena/Home/Home_Commander_L/C/R` | 0 (x = -5/0/+5) | 발판 메시 교체 | 윗면 0.3m (바꾸면 `[ArenaLayout] > Commander Platform Height`도 같이) |
| `Arena/Away/Away_Commander_L/C/R` | 20 (x = +5/0/-5) | 〃 | 〃 |
| `Arena/Ground`, `Arena/Lanes/*` | — | 자유롭게 교체 · 장식 (레인 폭 5m, 전장 길이 20m) | — |

- **Home 쪽 높이 제한 이유**: 플레이어 눈높이가 지면 5m라, 자기 타워 · 넥서스가 높으면 가운데 레인 교전이 가려진다. 지금 플레이스홀더도 중앙 칸에서는 타워 바로 뒤 미니언이 가려진다.
- 파괴 연출: 코드는 파괴 시 **부모의 Y 스케일을 15%로 줄인다** → 모델 피벗이 바닥이어야 땅으로 무너지듯 보인다.
- 배경 장식은 `Arena/Environment/` 같은 새 폴더에 넣는다. 콜라이더 불필요 (판정은 서버 수학으로 함).
- 전체 배치를 플레이스홀더로 되돌리고 싶으면 `Spellbound > 5. Build Placeholder Arena` (Body가 없으면 다시 만든다).

---

## 4. 아이디어 (선택)

- **시야 문제 해결안**: ① Home 구조물을 낮고 넓게(성벽 · 제단 느낌) ② 자기 구조물은 반투명/디졸브 셰이더(시점 쪽에서 가릴 때만 투명) ③ 타워를 중앙이 아닌 레인 사이 경계에 두기(이 경우 `[ArenaLayout]` 깊이 값과 함께 ㅊㄱㅇ와 협의).
- **룬 = 스킬 아이덴티티**: 아이콘 · 손 위 표시 · VFX Cast가 같은 문양을 공유하면 학습이 쉽다. 아이콘을 그대로 Cast VFX의 파티클 텍스처로 재사용.
- **8슬롯 발광(RuneGlow8Slot)**: 아이콘을 흰 선으로 만들면 HUD에서 상태색(Ready 초록 / Cooldown 회색)과 잘 섞인다.
- **Quest 3 성능 예산**: 미니언 1~2k 트라이앵글 · 머티리얼 1개 · GPU Instancing 체크, 텍스처 512 이하(ASTC), 실시간 그림자 최소, 투명 파티클 겹침(오버드로우) 주의.
- **사운드**: 아직 재생 코드가 없다. `SFX_{SkillId}_{Name}_{Part}.wav` 규칙을 미리 정해 두면 VFX와 같은 방식으로 붙일 수 있다 (ㅊㄱㅇ 작업 필요).
- **손 모양**: 핸드트래킹 손 메시는 씬의 `OVRHandPrefab_Left/Right` 머티리얼만 바꾸면 "마법사 손"(발광 손끝 등)으로 바꿀 수 있다.
