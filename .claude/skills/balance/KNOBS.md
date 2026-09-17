# 밸런스 손잡이 목록

> 위치와 **효과 범위**만 적는다. 현재 값은 에셋을 열어 본다. 코드 상수는 재컴파일이 필요하다(런처가 Refresh·컴파일 대기 후 판을 돌린다).
> "코드" 표시 = 게임 로직 안의 상수 — **루프에서는 건드리지 않고 `codeSuggestions`로 제안만** 한다.

## 축 A. 맵 — 적이 얼마나 센가

| 손잡이 | 위치 | 효과 범위 · 주의 |
|---|---|---|
| 스테이지별 물량·간격·버스트 | `StageTable_Farm`(농장) · `StageTable_Coast`(해변) · `StageTable`(우주)의 `stages[]` `spawnCount, spawnInterval, burstSize, burstRest` | 그 맵 그 스테이지. 판 길이 → G1 시간도 변한다 |
| 스테이지별 체력·피해·속도 배율 | 같은 표 `enemyHpMultiplier, enemyDamageMultiplier, enemySpeedMultiplier` | 그 맵 그 스테이지 |
| 특수 적 확률 | 같은 표 `eliteChance, paperPlaneChance, ufoChance, shieldChance, riderChance, hopperChance, surferChance, airshipChance` | 🔴 **else-if 사슬**(이 순서) — 앞 확률을 올리면 뒤 적이 조용히 줄어든다 |
| 벽·매복 | 같은 표 `evolutionItemDrops`(이름은 옛것, 지금은 **확정 엘리트 수**) · `ambushCount, ambushSquads` | 벽 스테이지 7·9·13·19·24 |
| 25스테이지 이후 | 같은 표 `extended*` | 현재 최종 25라 안 쓰인다 |
| 난이도 공통 배율 | `AscensionTable.asset` `tiers[] hpMult, speedMult, damageMult, essenceMult, finalStage` | 🔴 **세 맵 공통** — 맵 사이 서열(G2) 조정엔 부적합. 난이도 단계 간 간격 조정용 |
| 적 종류 기본 스탯 | `Assets/Data/Enemies/EnemyDef_*` `maxHealth, speed, damage, xpValue` | 그 적이 나오는 **모든 맵** |
| 군중제어 저항 | `EnemyDef_*` `crowdControlResistance`(0 그대로 · 1 면역) / 보스 슬롯은 `Enemy.BossCrowdControlScale` (코드) | 감속·기절 세기와 밀림 거리. 둘 중 강한 쪽 |
| 사망 분출 | `Assets/Prefabs/Enemy_*.prefab`의 `deathSpawnPrefabs, deathSpawnCount, deathSpawnInheritsMultipliers` | 비행선·보스. 배율 상속을 켜면 분출 몹이 그 스테이지 세기로 나온다 |
| 후반 체력 계단 | `ScalingTable.asset` `hpStepBonus, speedStepBonus` | 전 맵 공통, 3스테이지 단위 |
| 박치기 | `BalanceConstants` `HeadbuttInterval, HeadbuttDamageScale` (코드) | 전역 — 플레이어 피해의 유일한 경로 |
| 매복 위치 | `BalanceConstants` `AmbushBandMinX/MaxX` (코드) | 전역 |

## 축 B. 스킬 파워 — 빌드가 얼마나 센가

| 손잡이 | 위치 | 효과 범위 · 주의 |
|---|---|---|
| 기본 쿨·피해·관통·개수 | `Assets/Data/Skills/Prog_*` `baseCooldown, baseDamage, basePierce, baseProjectiles, baseHits` | 그 스킬의 진화 전 |
| 레벨업 커브 | `Prog_*` `levels[]` (stat 코드: 0 피해 · 1 쿨 · 3 관통 · 4 투사체 · 5 발동률 · 6 지속 · 7 크기 · 8 되감기량 · 9 틱 · 10 대상 수 / op 0 더하기 · 1 곱하기) | 진화 전. 🔴 **`Evo_*.levels`가 비어 있으면 진화 후에도 이 커브를 다시 탄다**(쿨 감소 스텝까지 반복) |
| **진화 시작값** | `Assets/Data/Evolutions/Evo_<스킬>_R<루트>_T<차수>` `baseCooldown, baseDamage, baseHits, basePierce, baseProjectiles, baseDuration` | 그 진화만. **0 = 코드 계산 유지.** 진화 쿨의 1순위 손잡이 |
| **진화 레벨업 커브** | `Evo_*` `levels[]` | 그 진화만. 채우면 Prog 커브 재적용이 멈춘다 |
| 시전 시점 쿨 가산 | `PlayerSkills` `AssassinArrowExtraCooldown`(화살 R0 +초) · `ArrowRainCooldownMult`(화살비 ×) (코드) | 쿨 감사표가 반영한다. 데이터로 옮기지 않는 이유: 레벨업 쿨 스텝과 순서가 달라 수치가 보존되지 않음 |
| 기본 개수 상수 | `BalanceConstants` `OrbBaseTargets, HomingBaseMissiles, EagleBaseDrops, ShotgunBasePellets, SnipingBaseShots` (코드) | 그 스킬 전 단계 |
| 진화 루트 효과 배수 | `PlayerSkills.ApplyPathTierEffect`·각 `Fire*`의 상수 (코드) | 제안만 |
| 패시브 | `Passive_*` `baseValue, perLevelBonus` / 진화 효과는 `PlayerPassives.cs` 상수 (코드) | 그 패시브 |
| 전역 쿨 | `BalanceConstants.GlobalCooldown` (코드) | QWER 동시 입력 시 슬롯 경쟁에 직결 |
| 낙뢰 발동 피해 | `LightningStorm.BaseProcDamage` (코드) — `Prog_Lightning.baseDamage`는 무시된다 | 낙뢰 |

## 축 C. 스킬트리 파워 — 메타 진행이 얼마나 세지는가

| 손잡이 | 위치 | 효과 범위 · 주의 |
|---|---|---|
| 일반 노드 효과량 | `Assets/SkillTree/MainSkillTree.asset` 노드 `perLevel, maxLevel` — 효과 축은 `effect`(공격·체력·쿨·경험·정수·치명·비행·치피·보스·리롤) | 그 노드를 산 뒤 전 판. 풀트리 G4에 직결 |
| 노드 가격대 | 노드 `tier` | 언제 사게 되는가 |
| 강화·해금 노드 효과 | `Meta.cs` `SkillEffects.Compute`의 id 분기 (코드) | 제안만 |
| 진화 게이트 | `New_Evolution`·`New_Evolution2` 노드의 `tier`·선행 | 진화가 언제부터 가능한가 → 진화 스킬 등장 시점 |

## 축 D. 경제·진행 속도 — 얼마나 빨리 강해지는가

| 손잡이 | 위치 | 효과 범위 |
|---|---|---|
| 노드 가격 곡선 | `SkillTreeData` `TierCostBase, TierCostRatio, UnlockCostMult` · 레벨당 ×1.5 (코드) | 전 노드 — G1 총 시간의 1순위 |
| 정수 드랍 | `EnemyDef_*` `essenceDropChance, essenceDropAmount` | 그 적 |
| 난이도별 정수 배율 | `AscensionTable` `essenceMult` | 그 난이도 |
| 부유(정수 획득) 노드 | `MainSkillTree.asset` gold 노드 `perLevel` | 트리 후반 가속 |
| 판 안 성장 | `ScalingTable` XP 곡선·스테이지 XP 계수 · 경험 노드 | 레벨업 횟수 → 진화 도달 |

## 축 E. 캐릭터 — 캐릭터끼리 공평한가

| 손잡이 | 위치 | 효과 범위 |
|---|---|---|
| 최대 체력 | `Assets/Data/Characters/Char_*` `baseHealth` | 그 캐릭터 |
| 시작 스킬·허용 풀 | `Char_*` `startingSkill, allowedActivePool, allowedPassivePool` | 그 캐릭터의 빌드 폭 |
| 시작 스킬 파워 | 축 B의 `Prog_BasicAttack`(딸기) · `Prog_Swing`(파인애플) · `Prog_GrapeToss`(포도)와 그 `Evo_*` | 그 캐릭터만(시작 스킬은 레벨업 풀에 없다) |
| 해금 조건 | `Char_*` `requiredEssenceEarned, requiredClearMap, requiredClearAscension` | 정주행에서 언제 순환에 들어오나 |
