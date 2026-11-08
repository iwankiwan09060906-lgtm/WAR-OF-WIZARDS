# 사운드 적용 설명

`sounds/sound.md` 목록의 77개 소리를 게임에 넣은 방식, 각 소리가 언제 나는지, 음량을 어떻게 정했는지 정리한 문서입니다.

## 1. 파일 위치

| 위치 | 내용 |
|---|---|
| `sounds/` | 받은 원본 (폴더별). 게임은 이 폴더를 읽지 않습니다. |
| `UnityClient/Assets/_Project/Audio/Resources/Sounds/` | 게임이 쓰는 77개 파일 (폴더 없이 한곳에). 파일 이름 = `sound.md` 의 저장 파일명 |

- 원본 중 `.mp4` 18개는 Unity 가 오디오로 가져오지 못해서 **같은 이름의 `.wav`** 로 바꿔 넣었습니다 (Windows Media Foundation, 44.1kHz 16비트).
  해당 파일: H01×2, H02 Telegraph, H03×2, H04 End, H05 AuraCast, H06×2, S04×2, S06 Telegraph, S08 End, S09 End, S12 WingLoop, PlayerHit, VO×2
- `.wav` · `.mp3` · `.ogg` 는 그대로 복사했습니다.
- 가져오기 설정은 `Editor/SpellboundAudioImport.cs` 가 자동으로 정합니다.
  - `BGM_*` = Streaming (Quest 메모리 절약)
  - 나머지 = Decompress On Load (음량 보정 때 파형을 읽어야 해서)
  - 이미 가져온 파일에 다시 적용하려면 Sounds 폴더를 Reimport 하세요.

## 2. 코드 구조

| 파일 | 하는 일 |
|---|---|
| `Audio/SoundManager.cs` | 재생 엔진. 파일 이름으로 재생, 음량 보정, 3D 위치, 지속음(루프), 예약 재생, 배경음 크로스페이드 |
| `Audio/BattleAudio.cs` | 전투 이벤트 → 소리. 스킬, 경기 흐름, 내 손 제스처, 피격 · 상태, 전투 배경음 단계 |
| `Presentation/ArenaStateView.cs` | 유닛을 아는 곳에서 나는 소리. 소환 · 설치, 유닛 공격, 타워 공격, 타워 · 넥서스 파괴 |
| `UI/Common/MetaUiRoot.cs` | 화면별 배경음, 오류 알림음 |
| `UI/Common/UiKit.cs` | 모든 버튼 클릭음 |
| `UI/C09_DeckBuilder` · `UI/C10_MatchQueue` · `Network/Session/ClientConnector.cs` | 룬 선택 · 매칭 완료 · 연결 끊김 |

- PC 전용 서버 창(DedicatedServer)과 배치 모드에서는 소리를 내지 않습니다.
- 환경설정의 **효과음 · 배경음** 슬라이더는 재생할 때 바로 반영되고, **공간 음향**을 끄면 모든 소리가 머리 안(2D)에서 납니다.

## 3. 음량 정하는 법

최종 음량은 세 가지를 곱한 값입니다.

```
최종 음량 = 클립 크기 보정 × 분류 음량 × 환경설정(효과음 또는 배경음 %)
```

**① 클립 크기 보정 (자동)**
파일마다 원래 녹음 크기가 달라서, 처음 재생할 때 파형을 읽어 크기를 맞춥니다.
- 50ms 단위로 잘라 -40dB 보다 큰 부분만 평균(RMS)을 냅니다. 앞뒤 무음이나 긴 꼬리 때문에 작게 재지 않게 하려는 것입니다.
- 목표 크기는 -20dBFS 이고, 보정 배율은 0.3~3배 사이로 제한합니다.
- 그래서 **스킬끼리는 너무 작거나 크지 않게 비슷한 크기**로 들립니다.
- 배경음은 보정하지 않습니다 (원본이 이미 -20dB RMS 로 맞춰져 있음).

**② 분류 음량 (`SoundVolume`)**

| 분류 | 값 | 대상 |
|---|---|---|
| 내 스킬 | 0.85 | 내가 쓴 스킬의 모든 소리 |
| 상대 스킬 | **0.45** | 같은 스킬을 상대가 쓰면 내 것의 약 절반 |
| 경기 흐름 | 0.8 | 카운트다운, 돌파, 넥서스 파괴, 5분 강화, AFK, 연결 끊김, 매칭 완료 |
| 안내 멘트 | 0.9 | 승리 · 패배 |
| 피격 · 상태 | 0.6 | 피격, 회피, 빈사 심장박동 (0.48) |
| 메뉴 | 0.45 | 클릭, 오류, 룬 선택 |
| 내 손 제스처 | 0.4 | 룬 인식 · 실패, 무장, 조준 충전, 시간 초과, 이동, 쿨타임 완료 (0.32), 그리는 중 루프 (0.24) |
| **전장 유닛** | **0.12** | 타워 공격, 오크 도끼, 드래곤 브레스, 터렛 화살, 해골 — 같은 소리는 0.3초에 한 번만 |
| 배경음 | 0.35 | 모든 BGM |

- 웨이브 시작 신호음은 0.3 으로 작게 했습니다.
- 지속 루프(독 거품, 레이저 빔)는 스킬 음량의 0.7~0.8, 종료음(쉴드 · 타워 버프 · 결계 종료, 얼음 해제)은 0.8 배입니다.

**③ 3D 위치**
스킬 · 유닛 소리는 실제 위치에서 나서 방향을 알 수 있습니다. 하지만 거리에 따라 거의 줄지 않게 했습니다 (10m 까지 그대로, 300m 에서 0).
멀리 떨어진 상대 진영에 떨어지는 내 파이어볼이 작아지고, 내 앞에 떨어지는 상대 파이어볼이 커지는 일을 막으려는 것입니다. 크기는 ②의 내 것 / 상대 것으로 정합니다.

## 4. 내 것과 상대 것

- **스킬 소리**: 나와 상대 모두 납니다. 상대 것은 작게 납니다 (0.45).
- **스킬 시전 소리** (룬 그리기 · 인식 · 무장 · 조준 충전 · 시간 초과)와 **왼손 이동 소리**: 내 입력에서만 납니다. 상대 것은 나지 않습니다.
- 내 스킬인지는 이렇게 판단합니다.
  - 시전 연출(Cast)이 내 발판 5m 안이면 내 것입니다.
  - 그 밖의 연출(비행 · 착탄 · 장판 등)은 내가 그 스킬을 쓴 뒤 `지속 시간 + 4초` 안이면 내 것입니다.
  - 한계: 나와 상대가 **같은 스킬을 거의 동시에** 쓰면 상대 소리도 크게 날 수 있습니다. 서버 이벤트에 시전자 정보가 들어오면 그걸로 바꾸면 됩니다.

## 5. 소리별 재생 시점

### UI · 메타
| 소리 | 언제 |
|---|---|
| SFX_UIClick | 모든 버튼을 누를 때 |
| SFX_UIError | 빨간 알림(오류 토스트), 전투 중 시전 거절 |
| SFX_UIRuneSelect | 마법서(덱 빌더)에서 스킬을 넣거나 뺄 때 (클릭음 대신) |
| SFX_UIMatchFound | 매칭 완료 |

### 배경음 (1.5초 크로스페이드)
| 소리 | 언제 |
|---|---|
| BGM_LobbyMystic | 로그인 · 로비 · 마이페이지 · 설정 · 친구 · 방 · 우편 · 랭킹 · 튜토리얼 |
| BGM_SpellbookDeck | 마법서 |
| BGM_MatchQueue | 매칭 대기 |
| BGM_BattleBase | 전투 카운트다운이 시작될 때 |
| BGM_BattleTension | 누군가의 넥서스가 처음 무너졌을 때 |
| BGM_BattleClimax | 돌파가 시작됐을 때 |
| BGM_ResultVictory / Defeat / Draw | 경기가 끝날 때, 결과 화면 |

전투 3곡은 템포와 길이가 같아서 재생 위치를 이어받아 박자가 끊기지 않습니다.

### 경기 흐름
| 소리 | 언제 |
|---|---|
| SFX_BattleCountdown | 카운트다운 남은 3초 (파일 안에 3·2·1 비프와 시작음이 들어 있음) |
| SFX_WaveStart | 웨이브가 나올 때 (작게) |
| SFX_MatchPowerUp5Min | 5분 강화 단계가 오를 때 |
| SFX_TowerAttack | 타워가 쏠 때 (유닛 음량) |
| SFX_TowerDestroy | 타워가 무너질 때 |
| SFX_NexusDestroyRubble | 넥서스가 무너질 때 |
| SFX_BreachWarning | 돌파 시작 (내 돌파 · 상대 돌파 모두) |
| SFX_AFKWarning | AFK 경고가 처음 뜰 때 |
| SFX_NetworkDisconnected | 서버 연결이 끊겼을 때 |

### 오른손 제스처 · 시전 (내 것만)
| 소리 | 언제 |
|---|---|
| SFX_RuneDrawingLoop | 핀치 · 마우스 드래그로 그리는 동안 |
| SFX_RuneRecognized | 룬 인식 성공 |
| SFX_RuneFailCancel | 인식 실패, 손바닥 취소, 추적 끊김, 슬롯 사용 불가 |
| SFX_CastArmed | 주먹 쥠 (무장) |
| SFX_CastCharge2Sec | 조준 시작 → 조준 시간에 맞춰 속도를 바꿔 재생, 조준을 그만두면 멈춤 |
| SFX_CastTimeout | 시간 초과로 취소 |
| SFX_CooldownReadyChime | 슬롯 쿨타임이 끝났을 때 |

### 왼손 이동 (내 것만)
| 소리 | 언제 |
|---|---|
| SFX_SnapTeleport | 열 이동 (스냅) |

### 스킬 (나 = 0.85, 상대 = 0.45)
| 스킬 | 소리 → 언제 |
|---|---|
| S01 연막 | SmokeCast → 발사 · SmokeMuffledLoop → **내가** 연막에 맞아 앞이 안 보이는 동안 |
| S02 독 장판 | BottleThrow → 던짐 · BottleBreak → 착탄 · BubbleLoop → 장판 지속 시간 동안 |
| S03 파이어볼 | FireballProjectile → 발사 · FireballImpact → 착탄 |
| S04 화살 세례 | ArrowRainProjectile → 발사 · ArrowRainImpact → 착탄 (0.25초에 한 번) |
| S05 토네이도 | TornadoLoop → 지속 시간 동안 |
| S06 번개 | LightningTelegraph → 예고 · LightningImpact → 낙뢰 |
| S07 총난사 | BarrageFireLoop → 발 수 × 간격 동안 |
| S08 쉴드 | ShieldCast → 전개 · ShieldAbsorb → 플레이어 피해 흡수 · ShieldEnd → 지속 시간 끝 |
| S09 타워 버프 | TowerBuffCast → 전개 (타워 수와 상관없이 한 번) · TowerBuffAbsorb → 구조물 피해 흡수 · TowerBuffEnd → 지속 시간 끝 |
| S10 집중 | ConcentrateCharge → 시전 · ConcentrateAmplify → 집중 다음에 쓴 내 스킬 |
| S11 오크 | OrcSpawn + 0.5초 뒤 OrcRoar → 등장 · OrcAxeAttack → 공격 (유닛 음량) |
| S12 드래곤 | DragonWingLoop → 등장 후 3초 · DragonFireBreath → 공격 (유닛 음량) |
| S13 터렛 | TurretDeploy → 설치 · TurretArrowFire → 발사 (유닛 음량) |
| H01 프리즈 | FreezeImpact → 전장이 얼 때 · FreezeMelt → 지속 시간 끝 |
| H02 레이저 | LaserTelegraph → 예고 · LaserBeamLoop → 빔 지속 시간 동안 |
| H03 흡혈 | DrainAbsorb → 상대에게 닿을 때 · DrainHeal → 회복이 돌아올 때 |
| H04 반사 결계 | ReflectBarrierCast → 전개 · ReflectBarrierBounce → 결계 중 반사 · ReflectBarrierEnd → 지속 시간 끝 |
| H05 분노 | RageAuraCast + 0.4초 뒤 RageRoar → 전개 |
| H06 소환탑 | SpawnTowerDeploy → 설치 · SkeletonBoneRattle → 해골 소환 (유닛 음량) |

### 피격 · 상태 · 보이스
| 소리 | 언제 |
|---|---|
| SFX_PlayerHit | 내 HP 가 줄 때 (0.25초에 한 번) |
| SFX_PlayerLowHpHeartbeatLoop | 내 HP 25% 이하인 동안 |
| SFX_PlayerDodgeSuccess | 내가 회피했을 때 |
| SFX_DefenseMitigationDeflect | 타워가 살아 있어 내 피해가 무효화됐을 때 |
| VO_AnnouncerVictory / Defeat | 경기 끝 (무승부는 멘트 없음) |

## 6. 소리를 바꾸거나 더하려면

- **바꾸기**: `Audio/Resources/Sounds/` 의 파일을 같은 이름으로 덮어쓰면 됩니다. 확장자가 달라도 됩니다 (`.wav` · `.mp3` · `.ogg`). 크기는 자동으로 맞춰집니다.
- **크기 조절**: 분류 전체는 `SoundManager.cs` 의 `SoundVolume` 값을, 소리 하나는 호출하는 곳의 음량 인자를 바꿉니다.
- **확인**: 메뉴 `Spellbound > Debug > Sound Self-Check` 를 누르면 크기 계산과 클립 수(77)를 검사합니다.

## 7. 알려진 한계

- 쉴드 · 타워 버프 · 반사 결계 · 프리즈의 종료음은 **지속 시간이 끝날 때** 납니다. 쉴드가 먼저 깨지면 종료음이 늦게 납니다.
- `.mp3` 루프(독 거품, 토네이도, 총난사, 레이저, 심장박동, 연막 먹먹함)는 인코더가 넣은 앞뒤 무음 때문에 이음새가 살짝 들릴 수 있습니다. 거슬리면 `.wav` 나 `.ogg` 로 바꿔 넣으세요.
- 아직 사람이 직접 들어 보고 맞춘 값이 아닙니다. Quest 에서 들어 보고 `SoundVolume` 을 조정해 주세요.
