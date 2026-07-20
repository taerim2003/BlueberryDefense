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

### A. 스킬 기본값 — Tier A → `SkillDefinition` (Phase 3)
| 항목 | 현위치 | 값 |
|---|---|---|
| 기본 쿨타임 (스킬 9종) | [PlayerSkills.cs:1389](Assets/Scripts/PlayerSkills.cs#L1389) `GetDefaultCooldown` | 기본1.5/회오리5/오브7/낙뢰12/독수리15/스나5/호밍8/산탄14/되감기6 |
| 기본 피해 (스킬 9종) | [PlayerSkills.cs:1403](Assets/Scripts/PlayerSkills.cs#L1403) `GetDefaultDamage` | 16/7/7/(낙뢰=LightningStorm)/11/18/8/12/0 |

### B. 레벨업 증가치 — 값=Tier A / 스케줄=Tier B (Phase 3)
| 항목 | 현위치 | Tier |
|---|---|---|
| 피해 증가율 (%3==1): 기본13%·기타20% | [PlayerSkills.cs:297](Assets/Scripts/PlayerSkills.cs#L297) | A(값) |
| 쿨감율 (%3==2): 5% | [PlayerSkills.cs:298](Assets/Scripts/PlayerSkills.cs#L298) | A(값) |
| 3번째 슬롯 개별강화 값들(투속+0.1·관통+1·투사체+1·지속+0.5·크기+0.05·발동+0.03·독수리쿨×0.95) | [PlayerSkills.cs:259](Assets/Scripts/PlayerSkills.cs#L259) `ApplyThirdUpgradeEffect` | A(값) |
| **3번째 슬롯 순환 순서**(occurrence%4·%2) | 같은 함수 | **B(스케줄)** |
| 되감기 ±0.15, **홀짝 교차** | [PlayerSkills.cs:293](Assets/Scripts/PlayerSkills.cs#L293) | A(값)+**B(교차)** |

### C. 전역 스킬 상수 — Tier B → `BalanceConstants` (Phase 3)
| 항목 | 현위치 | 값 |
|---|---|---|
| 글로벌 쿨다운 | [PlayerSkills.cs:48](Assets/Scripts/PlayerSkills.cs#L48) | 0.4 |
| 오브 제단 쿨 | :49 | 15 |
| 크리 확률 상한 | :50 | 0.7 |
| 기본공격 멀티히트 수 | :57 | 3 |
| 스나이핑 저격수/간격 | :60-61 | 5 / 0.08 |
| 비행타격 발사점 상승 | :95 | 0.65 |

### D. 적 기본 스탯 — Tier A → `EnemyDefinition` (Phase 1, ②에서 이관)
| 항목 | 현위치 | 값 |
|---|---|---|
| 이속·피해·최대체력·XP | [Enemy.cs:7-10](Assets/Scripts/Enemy.cs#L7-L10) | 2 / 10 / 20 / 5 (프리팹별 상이) |
| 정수 드랍 확률·량 | [Enemy.cs:12-13](Assets/Scripts/Enemy.cs#L12-L13) | 0.15 / 2 |
| 사망분출(대왕) 구성·팝콘 중력 | [Enemy.cs:27-34](Assets/Scripts/Enemy.cs#L27-L34) | 프리팹별 |

> 결정필요: EnemyDefinition SO로 완전 이관 vs 프리팹 SerializeField 유지 + 대시보드에서 집계. 프로토 단계면 후자가 이관 리스크 적음.

### E. 후반 스케일링 — Tier A → `ScalingTable` (Phase 1)
| 항목 | 현위치 | 값 |
|---|---|---|
| HP 스텝 배열 | [EnemySpawner.cs:17](Assets/Scripts/EnemySpawner.cs#L17) | {0,.06,.16,.30,.48,.72,1.0} |
| 이속 스텝 배열 | :18 | {0,.025,.06,.11,.17,.24,.33} |
| 스텝 분모 (3스테이지마다) | :133 `currentStage/3` | 3 (**B: 구조**) |

### F. XP 커브 — 값=Tier A → `ScalingTable` (Phase 1)
| 항목 | 현위치 | 값 |
|---|---|---|
| 초기 필요 XP·레벨당 증가 | [PlayerExperience.cs:9,43](Assets/Scripts/PlayerExperience.cs#L43) | 18 / +9 |
| 후반 XP 감쇠 (스테이지1=100%→20=50%) | [PlayerExperience.cs:31-35](Assets/Scripts/PlayerExperience.cs#L31-L35) | 1.0→0.5, 기준스테이지 20 (양끝값 A / lerp 형태 B) |

### G. 스폰·스테이지 구조 — 일부 Tier A → `MapDefinition`/`ScalingTable` (Phase 1)
| 항목 | 현위치 | 값 | Tier |
|---|---|---|---|
| 폴백 물량·간격 | [EnemySpawner.cs:13-14](Assets/Scripts/EnemySpawner.cs#L13) | 1.5 / 20 | A |
| 보물상자 등장 텀 | :23 `TreasureDelay` | 5 | A |
| 보스 스테이지 번호 | :12 `bossStage` | 15 | A |
| 11스테이지+ 보물 2개 / 7스테이지 종이비행기 전용 | :72, :92 | — | **B(구조)** |
| 스테이지별 물량·확률·배율 | `StageTable` 에셋 | — | ① 이미 SO |

### H. 아웃게임/기타 — Tier A(낮은 우선순위) 또는 유지
| 항목 | 현위치 | 비고 |
|---|---|---|
| 스킬트리 노드 효과 수치 | [Meta.cs:115](Assets/Scripts/Meta.cs#L115) `SkillEffects.Compute` | 노드 id 키. 우선순위 낮음 — 나중에 `MetaTable`로 |
| 플레이어 기본 최대체력 | PlayerHealth SerializeField | → `CharacterDefinition.baseHealth`(캐릭터별) |
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

## 다음 세션 열린 결정
- `RunConfig`의 id 타입: string(에셋 이름) vs enum vs SO 직접참조 → Phase 1에서 RunBootstrap 만들며 확정.
- EnemyDefinition 완전 이관 vs 프리팹 유지(D 참고).
- 진화/패시브 수치 추출 범위(H) — Phase 3에서 항목별 A/B 재판정.
