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
| 파일명 | **`{SkillId}_s.png`** — 현재 19종 (`S01_s.png` ~ `H06_s.png`). `Icon_{SkillId}_{Name}.png`도 인식한다 |
| 규격 | PNG, 투명 배경 위 선 (현재 500×500 검은 선). 표시할 때 선을 스킬 색으로 칠하고 뒤에 반투명 원판을 깐다 (`Presentation/Resources/RuneIcon.shader`) |
| 등록 | 메뉴 `Spellbound > Runes > Build Rune Templates` 또는 `Spellbound > Assets > Assign Rune Icons` → 각 `Rune_*.asset`의 `Icon`에 자동 할당 (알파 · Clamp · 밉맵 · 최대 512 설정도 자동) |
| 쓰이는 곳 | HUD 8슬롯 배경, 룬 인식 후 RuneReady · Armed · Aiming 동안 오른손 옆 (원본 가로세로 비율 유지, 긴 변 10cm, 천천히 회전) |

### 1.2 룬 모양 (인식 템플릿)

| 항목 | 값 |
| --- | --- |
| 저장 위치 | `UnityClient/Assets/_Project/Gesture/Templates/Data/Rune_{SkillId}_{Name}.asset` (점 목록) |
| 디자인 원본 | `UnityClient/Assets/Art/Runes/Designs/{SkillId}.png` (19종) — 코드는 이미지를 읽지 않는다. 같은 모양을 `Gesture/Runtime/RuneDesigns.cs`에 획 단위로 옮겨 적었다 |
| 현재 기본 모양 | 19종 디자인 도형 + 변형 3개(가로 · 세로로 늘림, 기울임). 메뉴 `Spellbound > Runes > Build Rune Templates`가 에셋 19개 · 라이브러리 · 아이콘을 만든다 (녹화 샘플은 유지) |
| 인식 테스트 | `GameBootstrap > Recognize All Runes`(기본 켜짐) — 덱 밖 룬도 인식해 아이콘 · 조준까지 진행하고 시전 요청 대신 `[RuneTest]` 로그. 메뉴 `Spellbound > Debug > Test All 19 Runes`로 19종 자동 확인 |

**새 모양 등록 방법 (디자인 확정 후)**
1. 04_Battle Play → Game 뷰 클릭.
2. 마우스 왼쪽 드래그로 룬을 그린다 (손으로 그릴 순서 · 방향 그대로). 여러 획 룬은 획마다 드래그를 나눠 그리고, 다 그렸으면 `F`(주먹)로 그리기를 끝낸다 — 인식에 실패해도 궤적은 남는다.
3. 메뉴 `Spellbound > Runes > Record Last Stroke > S03 Fireball` (해당 스킬 선택, 19종 모두 있음).
4. 2~3을 **스킬당 5~10번** 반복 (조금씩 다르게: 크게/작게/기울여서).
5. 기본 도형을 빼고 싶으면 `Rune_S03_*.asset` 인스펙터의 `Samples`에서 `base` · `wide` · `tall` · `italic` 항목을 지운다.
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
| 수명 | 일회성 = 4초, Field · Loop = 스킬 지속 시간, Telegraph = 예고 시간. 끝나면 코드가 방출만 멈추고 남은 입자가 사라진 뒤 풀로 회수 |
| 크기 | **실제 크기(1 = 1m)로 제작** — 코드가 크기를 바꾸지 않는다 (예외: H02 빔은 길이 1m(+Z) → 전장 길이로 늘림) |
| 방향 | Field · Telegraph · Impact는 레인 방향 = +Z, Projectile은 진행 방향 = +Z |
| 셰이더 | 프로젝트는 **Built-in RP**. URP · HDRP 셰이더는 분홍색이 된다 (아래 2.1 빌더가 자동 변환) |
| 예산 | 프리팹 1개 동시 파티클 ~100 이하 |
| 금지 | C# 스크립트, 외부 텍스처 참조 누락 |

### 2.1 스킬 에셋 빌더 — 19종 VFX · 소환수 · 설치물 자동 생성

메뉴 **`Spellbound > Skills > Build All Skill Assets (VFX + models + spell data)`** 한 번이면 아래를 전부 만들고 VFX 카탈로그까지 갱신한다 (약 1분, 다시 실행하면 덮어씀 — 에셋 팩 원본은 수정하지 않음).

| 생성물 | 위치 | 원본 에셋 |
| --- | --- | --- |
| 스킬 정의 19종 + SpellCatalog | `_Project/Spells/Definition/Data/Spell_*.asset` | 코드 기본값 (`SpellDefinition.CreateDefault`) — **수치를 에셋에서 고쳤다면 이 메뉴가 덮어쓴다** (따로: `Rebuild Spell Definitions (19)`) |
| 소환수 · 설치물 정의 | `_Project/Combat/Minion/Data/Minion_AxeOrc · Dragon · Skeleton · Turret · SummonHut.asset` | 없을 때만 생성 (튜닝 보존) |
| 스킬 VFX 56개 | `Prefabs/VFX/Skills/VFX_*.prefab` | 52SpecialEffectPack · Hun0FX · Eric VFX Studio · ShieldEffect · ZNS3D |
| 유닛 연출 | `Prefabs/VFX/Skills/VFX_Unit_*` (등장 · 사라짐 · 오크 베기 · 드래곤 화염 · 터렛 화살) | 52 · Eric · Hun0FX · ZNS3D |
| 모델 5종 + Animator | `Prefabs/Summon/MDL_Summon_AxeOrc · Dragon · Skeleton`, `MDL_Structure_Turret · SummonHut`, `Art/Models/Summon/AC_*.controller` | 3D Stylized Orc · FourEvilDragons(Usurper Red) · Mini Simple Skeleton · StoneKeep Tower_c + Nora Squad Archer · ZNS3D 벽/지붕 |
| Built-in 변환 재질 | `Art/Materials/Skills/Converted/` · `Art/Materials/Skills/Mat_*` | URP 셰이더 → `Spellbound/FxAdditiveFlow · FxAlphaFlow · HexShield`(`Art/Shaders/Skills`), HDRP · Nora → Standard |

- 크기 확인: `Spellbound > Skills > Render VFX Previews` — `UnityClient/Temp/vfx_preview_request.txt`의 각 줄(`프리팹경로|출력.png|시간들|카메라거리|카메라높이|바라볼높이`)을 1m 격자 · 1.8m 사람 막대와 함께 PNG로 렌더한다.
- 에셋 팩을 손볼 때 주의: 52SpecialEffectPack 이펙트는 원본 반경이 8~20m라 빌더가 0.1~0.2배로 줄인다. `MagicCircleRelease`는 서 있는 마법진이라 바닥 표시에는 `SummonMagicCircle2`를 쓴다.
- 함께 고친 에셋 팩 스크립트: `52SpecialEffectPack/Animation&Script/csShowAllEffect.cs`(Unity 6에서 삭제된 GUIText → 컴파일 실패), `Mini Simple Characters Skeleton Demo/Readme/.../ReadmeEditor.cs`(창 배치 강제 로드 예외 → Error Pause로 플레이 정지).

### 2.2 스킬별 프리팹 (VfxName · Part)

| 스킬 | 프리팹 (VFX_{Code}_{VfxName}_…) |
| --- | --- |
| S01 Smoke | Cast · Projectile(연막탄) · Impact(검은 연기 폭발) · Loop(연막 맞은 상대 머리) |
| S02 PoisonField | Cast · Impact(3열 독 폭발) · Field(3열 × 깊이 5m 띠) |
| S03 Fireball | Cast · Projectile(Hun0FX 불덩이) · Telegraph(착탄 마법진) · Impact(폭발 + 바닥 불) |
| S04 ArrowRain | Cast · Projectile(ZNS3D 화살) · Telegraph · Impact(흙먼지) |
| S05 Tornado | Cast · Field(회오리 기둥 1개 — 서버가 레인에 3개 발행) |
| S06 Lightning | Cast · Telegraph(먹구름) · Impact(낙뢰) |
| S07 Gatling | Cast(손 화염 2초) · Projectile · Impact |
| S08 Shield | Cast · Loop(구형 쉴드, 반경 1m) |
| S09 TowerBuff | Cast · Loop(상자 방어막, 1m 상자 — 코드가 타워 크기로 맞춤) |
| S10 Focus | Cast · Loop(발밑 마법진) |
| S11 AxeWarrior · S12 Mage · S13 Turret · H06 SummonTower | Cast(소환 마법진) |
| H01 Freeze | Cast · Field(얼음 장판 1칸 — 서버가 3열 × 5줄 발행) · Loop(타워 빙결) |
| H02 Laser | Cast · Telegraph(예고선) · Field(빔) · Impact |
| H03 Drain | Telegraph(시전) · Impact(상대) · Projectile(생명의 불) · Loop(회복) |
| H04 ReflectBarrier | Cast · Loop(육각 결계, 반경 1m · 높이 2m) · Projectile(반사 탄) · Impact |
| H05 Rage | Cast · Field(붉은 장판 — 서버가 3곳 발행) |

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

> 미니언 3종은 KayKit Adventurers(Knight · Rogue_Hooded · Barbarian)로 만들어져 있다. 메뉴 `Spellbound > Assets > Build KayKit Minion Models`가 프리팹(`Prefabs/Minion/`) · 컨트롤러(`Art/Models/Minion/AC_*`)를 다시 만들고 카탈로그까지 갱신한다. 캐릭터 · 키 · 무기 · 공격 클립은 `SpellboundMinionModelBuilder.cs`의 `Specs`에서 바꾼다.

**미니언 Animator `State` 값** (코드가 상태가 바뀔 때마다 `SetInteger("State", …)`)

| 값 | 의미 | 권장 클립 |
| --- | --- | --- |
| 0 | 이동 | Walk (Loop) |
| 1 | 공격 중 | Attack (Loop, 공격 간격 근접 1.0s · 원거리 1.2s · Brute 1.4s) |
| 2 | 돌파 이동 (상대 넥서스 파괴 후) | Walk 또는 Run |
| 3 | 빙결 (H01 프리즈, 추후) | Idle 정지 |

- 파라미터 `State`가 없으면 코드는 Animator를 건드리지 않는다 (에러 없음).
- **사망 시 유닛은 즉시 사라진다.** Death 클립은 지금 재생되지 않는다 → 필요하면 ㅊㄱㅇ에게 "사망 VFX/애니 훅" 요청.
- **팀 컬러**: 코드가 렌더러에 `_Color` · `_BaseColor` · `_TeamColor`를 파랑(Home) / 빨강(Away)으로 곱한다 (§25.4). 미니언은 텍스처 색이 살도록 흰색 쪽으로 연하게 섞은 색(`FallbackVisuals.LightTeamColor`)을 쓴다. Standard 셰이더면 `_Color`가 텍스처에 곱해지므로, **팀 색이 들어갈 부분은 흰색~회색**으로 칠한다. 더 정교하게 하려면 마스크를 쓰는 Shader Graph에 `_TeamColor` 프로퍼티를 만든다.
- **애니메이션 출처**: Mixamo 등에서 받을 때 Walk · Attack · Idle은 Loop Time 체크.

### 3.3 씬에 직접 배치하는 것 (이름이 아니라 위치로 연결)

`04_Battle` 씬 `Arena` 아래 구조. **빈 부모 오브젝트 = 게임 로직 위치이므로 옮기지 말 것.** 모양은 그 아래 `Body`만 교체한다.

| 오브젝트 | 위치 (z) | 교체 방법 | 높이 권장 |
| --- | --- | --- | --- |
| `Arena/Home/Home_Nexus` | 3 (발판 바로 앞 — 위치가 곧 게임 로직의 넥서스 깊이) | `Body`(큐브) 삭제 → 넥서스 모델을 자식으로, 로컬 (0,0,0) | 현재 2.4m |
| `Arena/Home/Home_Tower` | 13 (넥서스와 10m) | `Body`(실린더) 삭제 → 타워 모델 | 현재 4.8m |
| `Arena/Away/Away_Tower` · `Away_Nexus` | 28 · 38 (전장 길이 41m — 양 타워 사이 15m) | 같은 방식 (부모가 180° 돌아 있어 Home을 마주 봄) | Home과 같은 크기 |
| `Arena/Home/Home_Commander_L/C/R` | 0 (x = -5/0/+5) | 발판 메시 교체 (현재 Scene Teleportation Kit platform_blue/red, 폭 3.6m — 플레이어 2배 기준) | 가운데 디스크 윗면 (바꾸면 `[ArenaLayout] > Commander Platform Height`도 같이 — 메뉴 6이 자동 갱신) |
| `Arena/Away/Away_Commander_L/C/R` | 41 (x = +5/0/-5) | 〃 | 〃 |
| `Arena/Art_Bridge`, `Arena/Lanes/*` | — | 메뉴 6이 만드는 바닥 · 벽 · 절벽 · 장식 (레인 폭 5m — 레인 사이 빈틈 없음, 전장 길이 41m). 레인 판은 렌더러만 꺼져 있다 | — |

- **Home 쪽 높이 주의**: 플레이어(크기 2배) 눈높이가 지면 10m라, 자기 타워 · 넥서스가 높으면 가운데 레인 교전이 가려진다. 현재 타워는 4.8m.
- 파괴 연출: 코드는 파괴 시 **부모의 Y 스케일을 15%로 줄인다** → 모델 피벗이 바닥이어야 땅으로 무너지듯 보인다.
- **현재 적용된 아트**: 메뉴 `Spellbound > 6. Apply Arena Art`가 다음을 적용한다. 만든 오브젝트는 `Art_` 접두라 다시 실행하면 갈아 끼운다. 씬 저장은 직접(Ctrl+S).
  - 타워 `Turret.fbx`(4.8m) · 넥서스 `castle.fbx`(2.4m): 원본이 약 50만 삼각형이라 Unity Mesh LOD로 줄인 3단계(약 1만 · 2,700 · 700 삼각형, `Art/Models/Arena/MESH_Structure_*_LOD3/4/5.asset`, 압축 · 최적화)를 LODGroup으로 쓴다 — 가까운 자기 진영만 1만, 상대 진영은 2,700 이하. 원본 FBX는 씬이 참조하지 않아 빌드에 들어가지 않는다 (`StructureLodLevels` · `StructureLodHeights`, 처음 한 번 약 40초).
  - 재질은 **보는 사람 기준**(`TeamArt` — 로컬 진영 = 아군): 타워 아군 `roughness` · 적 `basecolor`, 넥서스 본인 `basecolor` · 적 `roughness`, 발판 본인 `platform_blue` · 적 `platform_red`. 각 앵커 아래 `Art_Ally` / `Art_Enemy`.
  - 전장 길이 41m · 타워 깊이 13m · 넥서스 발판 앞 3m (`fieldLength` · `towerDepth` · `nexusDepth` · `Commander Platform Height` 자동 갱신), 플레이어 눈높이 10m(OVRCameraRig).
  - 바닥: Hand Painted Stone Texture `Stone_normal` 재질 하나 (색 = `Stone_Ambient_Occlusion`, `MAT_Arena_Floor`, 4m마다 반복) — 절벽 덩어리의 윗면 한 장. 둘레는 `bricks_01` · `brick_01_Variant`(0.4배) 낮은 벽.
  - 낭떠러지: 바닥 아래 절차 생성 바위 덩어리(`MESH_Arena_CliffBody`, AEGIS `Rock mat`), 양옆 두 줄 협곡 벽(AEGIS 바위를 20~44배), 벽 안쪽 작은 바위 장식. 예전 400m `Ground`는 지운다.
  - 타워 총알: `VFX_Tower_Ally_Projectile` / `VFX_Tower_Enemy_Projectile` (Hovl Magic fire pro blue / red를 0.8배, 검은 연기 끔, `Prefabs/VFX`). 속도는 `ArenaStateView.towerShotSpeed` 12m/s.
  - 18시 해질녘 하늘(`TEX_Sky_Dusk` — 지평선 아래 = 안개색) · 조명 · 안개.
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
