# 맵 밖 스킬 · 상대 시전 준비 표시

## 맵 밖에서 날아오는 스킬

스킬이 시전자 손에서 나가는 대신, 전장 바깥에서 출발해 조준 지점으로 들어온다.
판정은 그대로 도착 지점 · 도착 순간으로 한다. 출발점은 날아오는 경로일 뿐이다.
**효과는 모두 레인 안에서 일어난다.** 4인전은 레인이 교차하므로 날아오는 길도 그 레인의 축 위에 둔다
(`Flank` = 레인 양 끝 바깥). 장판도 3열 폭이 아니라 레인 폭으로 줄어든다 (`ArenaGeometry.BandHalfWidth`).

| 스킬 | 출발 | 도착까지 | 동작 |
| --- | --- | --- | --- |
| S01 연막 | `Flank` | 2초 | 맵 밖에서 연막탄이 상대에게 날아든다 (열을 옮기면 피한다) |
| S02 독 장판 | `Flank` | 1.3초 | 독병이 날아와 떨어진 자리에 띠 장판 (1:1 은 3열, 4인전은 그 레인) |
| S03 파이어볼 | `Behind` 레인 축 시전자 뒤 | 1.3초 | 유성처럼 머리 위를 넘어가 폭발 (탄 크기 2.5배) |
| S04 화살 세례 | `Flank` 양쪽 번갈아 | 1초 | 화살이 맵 양쪽에서 쏟아진다 |
| S05 토네이도 | `Behind` 레인 축 시전자 뒤 | 2.5초 | 맵 밖에서 들어와 조준한 레인을 따라 쓸고 지나간다 |
| S11 도끼 전사 | `Sky` 하늘 | 0.9초 | 떨어져서 착지하면 유닛이 된다 |
| S12 드래곤 | `Flank` | 1.5초 | 날아 들어와 소환 지점에서 유닛이 된다 |
| H01 프리즈 | `Around` 사방 | 1초 | 얼음 혜성 6개가 내 세 레인 위로 떨어지고, 첫 혜성이 닿을 때 얼어붙는다 |

S06 · S07 · S08 · S09 · S10 · S13 · H02 ~ H06 은 그대로다.

### 다른 스킬에 붙이기

`SpellDefinition` 의 데이터로 정한다 (인스펙터 "맵 밖에서 날아오기", 코드 기본값은 `SetOffMap`).

| 필드 | 뜻 |
| --- | --- |
| `OffMap` | `None`(시전자 손) · `Behind` · `Flank` · `Sky` · `Around` |
| `OffMapDistance` | 전장 가장자리에서 바깥으로 몇 m |
| `OffMapHeight` / `OffMapArrivalHeight` | 출발 높이 / 도착 높이 (0 = 스킬 기본값) |
| `OffMapFlightSeconds` | 도착까지 걸리는 시간 — 거리와 상관없이 일정해서 피할 시간이 늘 같다 |
| `OffMapVisualScale` | 탄환 표시 크기 배율 (표시 전용) |

투사체를 쏘는 스킬은 발사 직전에 한 줄이면 된다.

```csharp
OffMapShot.Apply(def, world, casterTeam, target, ref launch);
world.LaunchProjectile(launch);
```

에셋을 코드 기본값으로 다시 만들면(`Spellbound > Skills > Rebuild Spell Definitions`) 인스펙터에서 바꾼 값은 덮어써진다.
밸런스 값은 `SpellDefinition.Create*Default` 에도 같이 넣는다.

## 상대 시전 준비 표시

```text
사람: CastStateMachine → CastPreviewReporter → IMatchClient.ReportCastPreview ─┐
봇  : BotPlayer (시전 전 CastWindupSeconds 대기) ──────────────────────────────┤
                                     MatchSimulation.SetCastPreview → PlayerSnap.Cast* · Pen*
                                                                                │
                                                     CastPreviewView (상대 아바타에 표시)
```

- 룬을 핀치 · 마우스로 그리면 실제 손끝 궤적이 상대 화면 허공에 그려진다 (이때는 스킬을 모르므로 중립 색).
- 손 모양 · 슬롯 키 · 봇은 궤적이 없으므로 그 스킬의 룬을 진행률만큼 그려 보인다.
- 아바타 오른팔이 펜 끝을 가리키고, 발밑 마법진 · 머리 위 스킬 이름이 뜬다. 조준을 시작하면 노리는 레인 쪽으로 돌아선다.
- 정확한 조준점은 보내지 않는다 — 조준 포인터는 본인에게만 보인다는 §6.3 규칙은 그대로다.
- 봇은 시전 전에 `BotConfig.CastWindupSeconds`(3초) 동안 준비를 보여 주고, 시전 간격은 `AiCastGapSeconds`(5초) 다.
- 표시 전용이다. 시전 가능 여부 · 판정은 여전히 서버가 정한다. 보고가 2.5초 끊기면 표시는 저절로 사라진다.
- `PlayerSnap` 이 바뀌었으므로 **PC 서버와 클라이언트를 함께 다시 빌드**해야 한다.

## 이펙트 에셋

스킬 표시는 기본 도형 대신 프리팹을 쓴다. 아래는 `52SpecialEffectPack` 을 복사해 `Assets/Prefabs/VFX/Skills` 에 둔 것이다.

| 프리팹 | 원본 | 쓰는 곳 |
| --- | --- | --- |
| `VFX_H01_Freeze_Projectile` | StarCore (얼음색) | 프리즈 혜성 |
| `VFX_H01_Freeze_Impact` | IceCloud | 혜성 착탄 |
| `VFX_Cast_Circle` · `VFX_Cast_Pen` · `VFX_Cast_Release` | SummonMagicCircle2 · StarCore · RuneOfMagic | 상대 시전 준비 |
| `VFX_Aim_Circle` | SummonMagicCircle3 | 내 조준 표시 |

팩 원본을 바로 쓰지 말 것 — `csDestroyEffect` 가 붙어 있어 X · Z · C 키를 누르면 이펙트가 파괴되고(X 는 게임의 취소 키), 입자 크기가 Transform 을 따르지 않는다. 사본은 이 스크립트를 빼고 크기 모드를 Hierarchy 로 바꿨다. 팩 원본의 마법진 지름은 약 10~40m 라서 배율로 줄여 쓴다.

## 검증

```bat
cd Tools\BotLab
dotnet run -c Release -- skilltest
```

실제 `MatchSimulation` 으로 스킬 출발 위치 · 비행 시간 · 4인전 레인 안 판정 · 토네이도 · 착지 · 프리즈 시점 · 봇 준비 · 끊김 처리를 검사하고, 실제 룬 인식기로 웹캠 궤적 인식률을 잰다.
화면 표시(룬 크기 · 팔 움직임 · 색)는 시뮬레이션으로 확인할 수 없으므로 Unity 에서 봇전으로 본다.
