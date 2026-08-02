# HANDOFF.md — 세션 인계 문서

<!--
📋 유지 가이드 — 이 문서는 "다음 세션이 지금 뭘 해야 하는가"만 담는다.
- 담을 것: 현재 빌드 상태, 미해결 이슈, 다음 할 일. 그것뿐.
- 지울 것: 완료·검증된 피쳐/버그/이슈는 즉시 삭제 (git 히스토리에 이미 남음).
- 세션 로그를 누적하지 말 것. 매 세션 "추가 + 삭제" 둘 다 한다.
- 옮길 것: 구조적 함정 → ARCHITECTURE.md / 게임 스펙·설계 철학 → GDD.md / 씬 배선 → SCENE_MAP.md.
  여기엔 "무엇을 확인·수정해야 하는지"만 남긴다.
- 목표: 한 화면 안에 읽힐 것. (497줄까지 불어난 적 있음 — 그렇게 두지 말 것.)
- ⚠️ 이 문서는 **할 일의 목록이지 코드의 진실이 아니다.** 여기 적힌 "~해야 함"을 사용자에게
  말하기 전에 코드로 1건이라도 확인할 것. 이미 해결됐을 수 있다(실제로 여러 번 그랬다).
-->

## 🧭 North Star
> **"스킬을 강화해 적들을 시원하게 쓸어버리자."**
> QWER 액티브 4 + 패시브 4를 진화로 강화해 빌드를 만드는 벙커 디펜스. 스펙은 `GDD.md`.

**세션 시작 시**: 이 문서 → `ARCHITECTURE.md`(코드 작업 시) → `SCENE_MAP.md`(씬 작업 시). `GDD.md`는 grep으로 필요한 절만.

### 📅 일정 (노션 원본)
| 마일스톤 | 날짜 | 내용 |
|---|---|---|
| ~~POC 종료~~ | ~~7/31~~ | ✅ 완료 — 빌드 배포됨. **이후 게임 구성은 바꾸지 않는다** |
| **폴리싱(버티컬 슬라이스)** | **8/7** | ← **지금 여기.** UI 에셋 · **사운드** · 이펙트/애니 · 설정 패널 · 저장/불러오기 · 번역 시스템 |
| 상점 페이지 등록 | 8/24 | 상점용 번역 |
| 스팀용 빌드 마감 | 8/31 | 콘텐츠·밸런싱 전부 마무리 |
| 출시 빌드 마감 | 9/4 | QA 완료본 |
| 출시 | 9/11 (제한선 9/25) | 가격 3,000원 |

---

## 현재 상태 (2026-08-02, 세션 23)

**빌드**: 컴파일 에러 없음(경고 CS0618 기존 잔존, 무해). POC 빌드 배포 완료, 세션21·22 변경은 **플레이 확인 대부분 완료**.

### 🔒 되돌리지 말 것 (의도적 결정)

- **해안가 `StageTable_Coast`의 UFO 판(13·22)만 2배** — 나머지 판은 물량 ×3인데 여기만 2배다. UFO 1대가 12~15마리를 쏟는데 쿼터엔 1로 잡혀서, 3배면 실제 700마리가 된다.
- **해안가 `ambushCount`는 안 올린다** — 중간 소환 1회당 예고 2.5초 동안 정문 스폰이 멈춘다. 16판은 이미 예고 대기(20초)가 실제 스폰 시간(11.7초)보다 길다. 올리면 난이도가 아니라 **늘어짐**이 는다. 물량은 `spawnCount`/`burstSize`로.
- **`extendedHpGrowth = 1.28`** — 사용자가 의도적으로 고른 가파른 값. 못 깨면 1.08 방향으로 내리는 것이지 버그가 아니다.
- **최종 판에만 상자·진화 아이템이 없는 것** — 의도된 동작(깨면 게임이 끝나 보상을 쓸 데가 없다). **중간 보스 판(15·20)에는 둘 다 나온다.**
- **진화 설명에 수치가 없는 것** — BTD6 파라곤식으로 일부러 뺐다. 감이 아예 안 잡히면 *제목만* 기능어로 되돌리는 절충안이 있다.

---

## ⚠️ 미해결 이슈 / 주의

- 🔴 **맵 3종이 서로 다른 단계에 있다.** 밸런스를 고치기 전에 **어느 맵인지 먼저 확인할 것.**

  | 맵 | fieldScale | 스테이지 테이블 | 배경 | cameraYLift | 콩콩이·서핑 |
  |---|---|---|---|---|---|
  | 블루베리 밭 | 1 | `StageTable` | ✅ 320×180 | 0 | ✖ |
  | 해안가 | 1.5 | **`StageTable_Coast`** | ✅ 480×270 | 2.2 | ✅ |
  | 광활한 밭 | 2 | `StageTable`(공유) | 🔴 **640×360 미제작** | 🔴 0(미설정) | ✖ |

  → **광활한 밭은 배경 그림이 나와야 나머지가 따라온다.** 배경 없이 실행하면 가장자리가 빈다(버그 아님).
- ⚠️ **13·22스테이지(UFO 벽)는 투하물이 물량 쿼터에 안 잡힌다** — UFO 1대당 12~15마리를 쏟는데 `spawnCount`엔 UFO 수만 잡힌다. 게임에서 제일 무거운 판. 프레임이 떨어지면 **여기부터 의심**(해안가 일반 판에도 `ufoChance` 0.08~0.11이 남아 있다).
- ⚠️ **`GetActiveSkillDescription`(산탄)이 아직 실제와 다르다** — "5초 동안 공격 횟수 +1"만 적혀 있는데 실제로는 산탄 알도 같이 발사한다. 호밍·스나이핑·독수리의 개수 표기는 고쳤고 산탄만 남았다.
- ⚠️ **판 전환/게임오버 시 비행 중인 경험치 보석의 XP는 유실**(timeScale 0이면 멈춘 채 씬이 끝남). 무해하다고 판단했으나 신경 쓰이면 도착 강제 처리.
- ⚠️ **ESC의 "타이틀로 돌아가기"는 확인창 없이 한 번 클릭**에 게임오버 흐름을 탄다. 오발이 신경 쓰이면 2단계 확인.
- ⚠️ **Windows 빌드 세이브는 파일이 아니라 레지스트리** — `HKCU\Software\DefaultCompany\BlueberryDefense`. 삭제: `reg delete "HKCU\Software\DefaultCompany\BlueberryDefense" /f`(게임 종료 상태에서).
- **에디터 인스펙터 예외(무해)**: 플레이 진입 시 `ObjectPreview.DrawPreview ... Image destroyed`.
- **노션 토큰 노출**(기존): 폐기·재발급 권장.
- (기존) **사운드 볼륨 이중곱** — 볼륨 담당 지점이 개별 프리팹 `m_Volume`/`fireSfxVolume`/`castSfxVolume`/`SfxPlayer.MasterVolume`으로 흩어져 있다. **폴리싱에서 사운드를 손보면 이걸 먼저 단일 소스로 정리할 것.**

> ✅ **되살리지 말 것 (틀린 것으로 확인된 옛 메모)**
> - "시작 캐릭터 풀을 줄여야 루프가 성립" — **틀림.** 스킬 해금 게이팅은 스킬트리 해금 노드가 이미 담당한다(`MetaBonuses.SkillUnlockedForRun`). `CharacterDefinition.allowedActivePool`은 캐릭터 개성용이고 둘은 독립.
> - 노션 "다음으로 할일"의 미완료 3건(**고승천 정수 배율 / 고승천 스테이지 길게 / 거대 회오리 시 미니 회오리 안 나옴**) — **전부 이미 구현됨.** 순서대로 `AscensionTable.essenceMult`(1.25/1.5) · `AscensionTable.finalStage`(15/20/25) · `PlayerSkills.FireWhirlwind`(두 path 독립 소환, 세션14 수정). 노션 체크박스가 낡은 것.
> - 세션13~19의 "실플레이 확인 대기" 항목 — 전부 확인 완료.

---

## 다음 할 일 — 폴리싱 (8/7 마감)

노션 일정의 폴리싱 항목 기준. **POC가 끝났으므로 게임 구성 변경은 하지 않는다** — 아래는 전부 "양념".

1. 🔊 **사운드** (일정에서 유일하게 강조된 항목) — 착수 전 위 "볼륨 이중곱"부터 단일 소스로 정리할 것.
2. 🎨 **UI 에셋** — 사거나 그리거나. 트리 UI 레이아웃 손보기도 여기 포함.
3. ✨ **안 넣은 이펙트 / 쥬씨한 애니메이션**
4. ⚙️ **설정 패널** (볼륨·해상도 등)
5. 💾 **저장·불러오기** — 현재 세이브는 `PlayerPrefs`(Windows=레지스트리). 정식 세이브가 필요한지 판단 필요.
6. 🌐 **번역 시스템** — 상점 페이지 번역(8/24)이 여기 걸려 있다.

### 🪝 훅 발동 검증 (Unity를 다음에 열 때 1분)
`.claude/hooks/block-unity-hazards.ps1`(PreToolUse)을 새로 깔았다. **스크립트 로직은 8케이스 검증 완료**지만 Claude Code ↔ 훅 배선은 아직 확인 못 했다(설치 세션에 Unity MCP 툴이 없었음).
→ MCP를 쓰게 되면 일부러 `scene-open`을 한 번 호출해 **차단되는지** 볼 것. 차단되면 `CLAUDE.md` §6의 "못 미더운 MCP 툴" 4줄은 훅과 중복이므로 지운다. 안 걸리면 훅이 등록 안 된 것(Claude Code 재시작 필요할 수 있음).

### 폴리싱 전에 판단할 것
- **공중 유닛 2종 추가**(위아래 지그재그 / 직선 가로지르기)는 *콘텐츠*라 POC 종료 원칙과 충돌한다. 넣을지 말지 결정 필요. 절차는 세션20 콩콩이가 최신 예시(ARCHITECTURE "새 적 타입" 행 + `isHopper` 한 벌).
- **`Map_Wide20`(광활한 밭)** — 배경 640×360이 없으면 맵 자체가 미완성이다. 셋째 맵을 출시에 넣을지부터 결정. 넣는다면 PPU **18**로 임포트 후 `background` 배정 → `cameraYLift` → 전용 스테이지 테이블 순.

### 남은 청소거리 (급하지 않음)
- **`OrbCanHitFlying` 스킬트리 노드** — 히트박스 판정 도입 후 **UFO에만** 의미가 있다. 다른 효과로 용도 변경 필요(예: 오브 공전 반경 확대).
- **화살 발사점 `FlyingArrowSpawnRaise = 0.65` 땜빵** — 대공 진화를 "발사각/히트박스 확장"으로 재설계할 때 정리.
- **`EvolutionTierTextTableSO`** — 여전히 (path, tier) 좌표계라 새 루트/티어와 어긋난다. 지금은 설명 폴백이 코드라 무해하지만, 에디터에서 텍스트를 덮어쓸 거면 루트 좌표계로 바꿔야 함.
- **보스 박치기 피해** — `HeadbuttDamageScale`(0.35) 하나로 전 적을 곱해서 보스는 1회 70. 보스만 별도 배율이 필요할 수 있음.
- **경험치 보석이 임시 노란 사각형**(`UI_SolidFill` 44px) — 전용 도트를 그리면 `XpGemFlight.gemSprite`만 교체.
- **딸기 전용 초상화 미완**(`Player.png` 임시) — 그리면 `Char_Strawberry.portrait`만 교체.
- **`EvolutionPanel` 씬 노드가 아직 3×3** — 진화가 2×2로 바뀌어 런타임에 5칸을 끈다. 정리하려면 SCENE_MAP 참고.
- **사용자 아트 대기**: 정수 아이콘 · 체력회복 아이콘 · 다양한 타격 이펙트 (노션 "다음으로 할일")

---

## 🔧 손잡이 (감이 안 맞을 때 만질 곳)

| 바꾸고 싶은 것 | 어디 |
|---|---|
| 해안가 난이도 | `StageTable_Coast.asset` (체력 → 물량 순. ⚠️ `ambushCount`는 올리지 말 것) |
| 화면 밀도(위쪽 공간) | `paperPlaneChance`·`hopperChance`·`surferChance`. ⚠️ **else-if 사슬이라 앞 확률이 뒤를 갉아먹는다** — `eliteChance`를 올리면 나머지 전부가 같이 줄어든다 |
| 보물상자 후함 | `LevelUpUI.TreasureEscalateChance`(0.45) / `TreasureMaxRolls`(5) / `TreasureRevealInterval`(0.55초) / `treasureIconSize`(130) |
| 웨이브 리듬 | `StageData.burstSize`/`burstRest`. ⚠️ 무리 스테이지에선 `spawnInterval`이 **무리 안의 간격**. 0.03이 실질 하한(스포너가 프레임당 1마리) |
| 콩콩이 도약 | 프리팹 인스펙터 `hopHeight` 3.3 / `hopDuration` 0.62 / `hopGroundPause` 0.12 |
| 서핑 높이·무리감 | `diveSpawnHeightRatio` 0.03~0.14(물결 위) / `diveAimYSpread` 0.75(무리 두께) / `diveBobAmplitude` 0.25(너울) / `speedVariance` 0.15 |
| 맵의 레인 높이 | `MapDefinition.cameraYLift` (해안가 2.2). ⚠️ **서핑 강하 띠와 한 세트** — 리프트를 바꾸면 서핑 높이도 같이 움직인다 |
| 적 접촉 거리 | `BalanceConstants.ContactStopDistance`(2.55). ⚠️ **`HeadbuttLungeDistance`·`AmbushBandMaxX`와 3종 세트** — 하나만 바꾸면 박치기가 허공을 치거나 중간 소환이 대응 불가가 된다 |
| 후반 난이도 | `StageTable.extendedHpGrowth` → `ambushCount` → 적 체력 순 |

---

## 🧩 콘텐츠 확장 = 순수 데이터
- **밸런스 편집**: `Window > Blueberry Defense > Balance Dashboard` 또는 인스펙터 — `Prog_*`·`Passive_*`·`EnemyDefinition`·`MapDefinition`·`StageTable`·`ScalingTable`·`CharacterDefinition`·`AscensionTable`. 전역 상수는 `BalanceConstants.cs`.
- **스킬트리 편집**: `Blueberry Defense > Skill Tree Editor`. 백업본 `MainSkillTree_Backup.asset`.
  **새 강화 노드**: 에디터에서 노드를 만들고 그 **id**를 `SkillEffects.Compute`의 case에 추가(효과는 id 기준 레지스트리 — 에셋 `effect` 필드는 신뢰 안 함).
- **새 맵/캐릭터**: `MapDefinition`/`CharacterDefinition`을 만들어 Title 씬의 각 Select UI 배열에 드래그.
- **치트**: `Window > Blueberry Defense > Cheat Window` — 정수 지급, 만렙, 스킬트리 초기화, 승천 등급 설정.

## 🎨 스킬 이펙트 픽셀 크기 컨벤션
이펙트 스프라이트는 배경과 픽셀 밀도가 안 맞아, **그림은 작게 그리고 엔진에서 1.5배로 표시**(플레이어 프리팹 `localScale=1.5`에 맞춤).
- 새 이펙트 프리팹은 기존값에 곱하지 말고 `localScale`을 **직접 (1.5,1.5,1.5)로** 세팅.
- PPU **32**, 기본 맵 카메라 ortho 5(화면 세로 10유닛).
- 공식: `원본 그림 px = (목표 월드 유닛 × 32) ÷ 1.5`
- **배경만 PPU 18** — 스프라이트가 자기 px/PPU만큼 커져 화면을 정확히 덮는다(1배=320×180px, 1.5배=480×270, 2배=640×360).
- ⚠️ **사용자가 새 PNG를 넣으면 임포트 기본값(PPU100·Bilinear·압축·Multiple·좌하단 pivot)으로 들어온다.** 같은 폴더 동종 스프라이트의 `.meta`와 대조해 맞출 것. spriteMode Multiple→Single 전환은 서브에셋 ID를 바꿔 **프리팹 참조가 깨지므로** 재임포트 → 재배선 순서 고정.
