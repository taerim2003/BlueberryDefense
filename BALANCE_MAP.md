# BALANCE_MAP.md — 밸런스 수치 지도 & 데이터화 계획

> **목적**: 지금 흩어진 밸런스 수치를 전수조사하고, 각 항목을 **어느 SO로 뺄지(Tier A)** 또는 **코드에 남길지(Tier B)** 확정한다.
> 리팩토링 Phase 1(맵/적)·Phase 3(캐릭터/스킬)이 이 표를 보고 재결정 없이 추출하면 된다.
> **목표**: 사용자가 Claude를 안 거치고 유니티 내에서 밸런스를 직접 조절. 최종적으로 Phase 5의 Balance Dashboard(EditorWindow)가 아래 SO들을 탭으로 묶는다.

## Tier 기준
- **Tier A** = 깔끔한 스칼라(자주 튜닝) → **SO로 추출.** 사용자가 만지고 싶은 대부분.
- **Tier B** = 로직 모양(순환 스케줄·홀짝 교차·경로 분기·실시간 기믹) → **코드에 남기되 한 곳에 모음**(`BalanceConstants`) 또는 그대로. 값은 Tier A로 빼도 *순서/조건*은 코드.
- 원칙: **"값은 데이터, 흐름은 코드."** 억지로 흐름까지 데이터화하면 편집이 더 어려워짐(과설계).

---

## 현재 밸런스 3계층 (복습)
- **① 이미 SO**: `StageTable`(스테이지별 물량·간격·적종류 확률·hp/속도/피해 배율), `LevelUpStatOptionSO`. → 유지.
- **② 프리팹 SerializeField**: 적별 스탯([Enemy.cs:7-13](Assets/Scripts/Enemy.cs#L7-L13)), 플레이어 기본체력(PlayerHealth). → 편집 가능하나 흩어짐.
- **③ 코드 하드코딩**: 아래 표의 대부분. → **추출 본체.**

---

## 제안 SO 아키텍처 (Phase 1/3에서 생성, 확정 전 검토)

| SO | 보유 | 참조 관계 |
|---|---|---|
| `SkillDefinition` (스킬 1종당, 또는 테이블) | 기본 쿨·기본 피해·레벨업 증가치 블록·특수플래그 | `CharacterDefinition.allowedSkillPool`이 참조 |
| `EnemyDefinition` (적 1종당) | hp·이속·피해·xp·정수드랍 (②에서 이관) | `MapDefinition.enemyRoster`가 참조 |
| `MapDefinition` (맵 1종당) | 배경·BGM·StageTable참조·enemyRoster·기믹플래그·스폰파라미터 | `RunConfig.MapId` → RunBootstrap |
| `CharacterDefinition` (캐릭터 1종당) | startingSkill·allowedSkillPool·패시브셋·기본체력·스프라이트·애니메이터·초상화 | `RunConfig.CharacterId` → RunBootstrap |
| `ScalingTable` (전역 1개) | 후반 HP/속도 스텝 배열·스텝 분모·XP 커브 | GameManager/Spawner/Experience가 읽음 |
| `BalanceConstants` (전역 1개, Tier B 집결지) | GlobalCooldown·MaxCritChance·멀티히트·스나이핑·오브제단쿨 등 교차 상수 | 여러 시스템이 읽음 |

> `StageTable`은 유지하되 `MapDefinition`이 참조하도록 이관. 맵마다 다른 StageTable 에셋을 물림.

---

## 전수조사표

### A. 스킬 기본값 — ✅ **Phase 3 완료: `SkillTable` SO** (`Assets/Data/SkillTable.asset`)
| 항목 | 현위치 | 값 |
|---|---|---|
| 기본 쿨타임 (스킬 9종) | `SkillTable.Entry.baseCooldown` (PlayerSkills가 조회) | 기본1.5/회오리5/오브7/낙뢰12/독수리15/스나5/호밍8/산탄14/되감기6 |
| 기본 피해 (스킬 9종) | `SkillTable.Entry.baseDamage` | 16/7/7/(낙뢰=LightningStorm, 코드 특수)/11/18/8/12/0 |

> 단일 테이블(enum 인덱스) 1개. `GetDefaultCooldown/Damage`가 테이블 조회로 대체. 미할당 시 `SkillTable.Default`(코드 기본값=현행) 폴백. 낙뢰 피해만 `LightningStorm.ProcDamage`(동적값) — 코드 특수 유지.

### B. 레벨업 증가치 — ✅ **Phase 3(값 일부): `SkillTable`** / 스케줄=Tier B(코드)
| 항목 | 현위치 | Tier |
|---|---|---|
| 피해 배율 (%3==1): 기본1.13·기타1.2 | ✅ `SkillTable.Entry.levelDamageMultiplier` | A(값) |
| 쿨감 배율 (%3==2): 0.95 | ✅ `SkillTable.Entry.levelCooldownMultiplier` | A(값) |
| 3번째 슬롯 개별강화 값들(투속+0.1·관통+1·투사체+1·지속+0.5·크기+0.05·발동+0.03·독수리쿨×0.95) | [PlayerSkills.cs](Assets/Scripts/PlayerSkills.cs) `ApplyThirdUpgradeEffect` | **B(코드 잔류)** ※ |
| **3번째 슬롯 순환 순서**(occurrence%4·%2) | 같은 함수 | **B(스케줄)** |
| 되감기 ±0.15, **홀짝 교차** | `ApplyUpgradeEffect`(Rewind 분기) | **B(코드 잔류)** ※ |

> ※ **Phase 3 재판정(2026-07-20)**: 3번째 슬롯 개별강화·되감기 값은 당초 Tier A였으나, 값이 `occurrence%N` 스위치/홀짝 분기 **안에** 박혀 있어(값≠깨끗한 스칼라) SO로 빼면 그랩백 필드만 늘고 편집이 더 어려워짐 → **스케줄과 함께 코드 잔류**로 확정. 깨끗한 두 배율만 SkillTable로. Describe/Apply가 같은 배율 필드를 읽어 미리보기·실제 일치.

### C. 전역 스킬 상수 — ✅ **Phase 3 완료: `BalanceConstants`** (`Assets/Scripts/BalanceConstants.cs`)
| 항목 | 값 |
|---|---|
| 글로벌 쿨다운 | 0.4 |
| 오브 제단 쿨 | 15 |
| 크리 확률 상한 | 0.7 |
| 기본공격 멀티히트 수 | 3 |
| 스나이핑 저격수/간격 | 5 / 0.08 |
| 비행타격 발사점 상승 | 0.65 |

> 값은 `BalanceConstants`(public const)에 모으고, PlayerSkills는 기존 이름을 **별칭 const**로 물려받아 호출부(GlobalCooldown 18곳 등)를 안 건드림. 편집은 BalanceConstants 한 곳에서.

### D. 적 기본 스탯 — ✅ **Phase 1 완료: `EnemyDefinition` SO로 완전 이관**
| 항목 | 현위치 | 값 |
|---|---|---|
| 이속·피해·최대체력·XP·정수드랍 | `Assets/Data/Enemies/EnemyDef_*.asset` (7종) | 프리팹별 상이 (아래) |
| 사망분출(대왕) 구성·팝콘 중력·플래그(isFlying/isTreasure/blocksProjectiles)·VFX·스프라이트 | Enemy.cs 프리팹 SerializeField 잔류 | 프리팹 결합이라 이관 안 함 |

> **결정(2026-07-20)**: 완전 이관 선택. Enemy.cs가 `EnemyDefinition definition`을 참조, Awake에서 런타임 필드로 복사(공유 SO 오염 방지 — ApplyStageMultipliers는 복사본에만 곱함).
> 이관 대상 = 순수 밸런스 스칼라 6개뿐. 행동/연출 필드는 프리팹에 남김.
> **실효값(이관됨)**: Blueberry 2/10/14/5, Treasure 2/10/14/5, PaperPlane 5/6/6/5, Regent(elite) 1.6/15/80/15, Shield 1.5/10/68/8, Ufo 1/16/68/12, Boss 0.6/30/1500/200. 정수드랍은 Boss만 1/8, 나머지 0.15/2.

### E. 후반 스케일링 — ✅ **Phase 1 완료: `ScalingTable` SO** (`Assets/Data/ScalingTable.asset`)
| 항목 | 현위치 | 값 |
|---|---|---|
| HP 스텝 배열 | `ScalingTable.hpStepBonus` | {0,.06,.16,.30,.48,.72,1.0} |
| 이속 스텝 배열 | `ScalingTable.speedStepBonus` | {0,.025,.06,.11,.17,.24,.33} |
| 스텝 분모 (3스테이지마다) | [EnemySpawner.cs](Assets/Scripts/EnemySpawner.cs) `currentStage / 3` | 3 (**B: 구조 — 코드 잔류**) |

> EnemySpawner/PlayerExperience는 `scaling` SerializeField(=ScalingTable) 참조. 미할당 시 `ScalingTable.Default`(코드 기본값=현행) 폴백.

### F. XP 커브 — ✅ **Phase 1 완료: `ScalingTable` SO**
| 항목 | 현위치 | 값 |
|---|---|---|
| 초기 필요 XP·레벨당 증가 | `ScalingTable.xpToNextLevelBase` / `xpToNextLevelPerLevel` | 18 / +9 |
| 후반 XP 감쇠 양끝값·기준스테이지 | `ScalingTable.xpFactorMax` / `xpFactorMin` / `xpDecayReferenceStage` | 1.0 / 0.5 / 20 |
| 감쇠 lerp 형태 | `ScalingTable.XpStageFactor()` | 코드 (**B: lerp 형태**) |

### G. 스폰·스테이지 구조 — 일부 ✅ **Phase 1 완료: `MapDefinition`**
| 항목 | 현위치 | 값 | Tier |
|---|---|---|---|
| 폴백 물량·간격 | `MapDefinition.defaultSpawnCount` / `spawnInterval` | 20 / 1.5 | A ✅ |
| 보스 스테이지 번호 | `MapDefinition.bossStage` | 15 | A ✅ |
| 적 로스터(7종 프리팹) | `MapDefinition.*EnemyPrefab` | — | A ✅ |
| 보물상자 등장 텀 | [EnemySpawner.cs](Assets/Scripts/EnemySpawner.cs) `TreasureDelay` | 5 | A (아직 코드 — 필요 시 MapDefinition으로) |
| 11스테이지+ 보물 2개 / 7스테이지 종이비행기 전용 | EnemySpawner.Update | — | **B(구조 — 코드 잔류)** |
| 스테이지별 물량·확률·배율 | `StageTable` 에셋 (MapDefinition이 참조) | — | ① 이미 SO |

### H. 아웃게임/기타 — Tier A(낮은 우선순위) 또는 유지
| 항목 | 현위치 | 비고 |
|---|---|---|
| 스킬트리 노드 효과 수치 | [Meta.cs:115](Assets/Scripts/Meta.cs#L115) `SkillEffects.Compute` | 노드 id 키. 우선순위 낮음 — 나중에 `MetaTable`로 |
| 플레이어 기본 최대체력 | ✅ Phase 3: `CharacterDefinition.baseHealth`(선택 시 PlayerHealth.Awake가 반영, null이면 프리팹 100) | Char_Strawberry=100 |
| 진화 트리 티어별 수치 | `PlayerSkills.ApplyPathTierEffect` / `PlayerPassives` | **대부분 B**: 경로/티어 구조 + 실시간 기믹(관통·분열·재귀) 엮임. 순수 스칼라 티어만 선별 추출, 나머지 유지 |
| 패시브 레벨 효과·연계 보너스 | `PlayerPassives` | 진화와 동일 판단. Phase 3에서 스킬과 함께 검토 |
| 낙뢰 파라미터 | `LightningStorm` | 발동확률·재귀·체인. Tier A로 뺄 값 다수 — Phase 3 스킬 추출 시 함께 |

---

## Tier B로 코드에 남기는 것 (명시)
로직 모양이라 데이터화하면 오히려 편집 난이도↑:
1. 레벨업 3번째 슬롯 **순환 순서**(occurrence%N)
2. 되감기 **홀짝 교차**
3. 진화 트리 **경로/티어 분기 + 실시간 기믹**(관통/분열/재귀) — 스칼라 티어값만 선별 추출
4. XP 감쇠 **lerp 형태**, 스텝 **3스테이지 분모**
5. 11+/7 스테이지 **특수 케이스** 구조

→ 이들의 *값*은 Tier A로 빼되, *흐름*은 `BalanceConstants` 상수 참조로 정리.

## 결정 완료 (Phase 1, 2026-07-20)
- `RunConfig` id 타입 = **SO 직접참조** (`RunConfig.Map = MapDefinition`). 씬 로드 넘어 유지, 타입안전, 룩업 불필요. null이면 RunBootstrap의 `defaultMap` 폴백.
- EnemyDefinition = **완전 이관** (D 참고).
- BGM = **필드 + 최소 재생** (MapDefinition.bgm 있으면 RunBootstrap이 AudioSource 루프 재생. 기본맵 null=무음).

## 다음 세션 열린 결정
- **진화/패시브 티어 수치(H)** — Phase 3에서 **보류 확정**(경로/티어 분기 + 실시간 기믹과 엮여 데이터화 이득 낮음). 추후 순수 스칼라 티어만 선별 추출하려면 `PlayerSkills.ApplyPathTierEffect`/`PlayerPassives` 항목별 A/B 재판정 필요.
- ScalingTable을 전역 유지 vs MapDefinition이 참조(맵별 스케일링) — 현재 전역 1개. 맵별로 후반 난이도를 다르게 하고 싶으면 MapDefinition에 ScalingTable 참조 추가.
- Balance Dashboard(Phase 5, 선택): 이제 SkillTable·CharacterDefinition·MapDefinition·EnemyDefinition·ScalingTable·BalanceConstants가 다 모였으니 EditorWindow 탭으로 묶을 재료 완비.
