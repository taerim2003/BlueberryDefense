# 밸런스 손잡이 목록

> 위치와 **효과 범위**만 적는다. 현재 값은 에셋을 열어 본다. 코드 상수는 재컴파일이 필요하다(런처가 Refresh·컴파일 대기 후 판을 돌린다).
> "코드" 표시 = 게임 로직 안의 상수 — **루프에서는 건드리지 않고 `codeSuggestions`로 제안만** 한다.

## 축 A. 맵 — 적이 얼마나 센가

| 손잡이 | 위치 | 효과 범위 · 주의 |
|---|---|---|
| 스테이지별 물량·간격·버스트 | `StageTable_Farm`(농장) · `StageTable_Coast`(해변) · `StageTable`(우주)의 `stages[]` `spawnCount, spawnInterval, burstSize, burstRest` | 그 맵 그 스테이지. 판 길이 → G1 시간도 변한다 |
| 스테이지별 체력·피해·속도 배율 | 같은 표 `enemyHpMultiplier, enemyDamageMultiplier, enemySpeedMultiplier` | 그 맵 그 스테이지 |
| 특수 적 확률 | 같은 표 `eliteChance, paperPlaneChance, ufoChance, shieldChance, riderChance, hopperChance, surferChance, airshipChance` | **적어 놓은 값이 곧 실제 비율**입니다(2026-09-20 이후). 주사위를 한 번 굴려 누적 가중치로 고르므로 순서는 의미가 없습니다. 순차 굴림이 남아 있는 곳은 매복 소환(`PickAmbushPrefab`, 엘리트·콩콩이 둘뿐)입니다 |
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
| 레벨업 커브 | `Prog_*` `levels[]` — 레벨 2부터 10까지 **9칸**이고 한 칸에 축 하나입니다. stat 코드의 원본은 `SkillProgression.cs`의 `SkillStat` enum이고, 2026-09-29에 12번부터 13종(설치 지속 · 최대 스택 · 파생 피해 · 파생 범위 · 보스 피해 · 버프 타수 · 강화량 · 페널티 감소 · 전체 쿨감 · 적 둔화 · 기절 시간 · 기절 주기 · 흡혈)이 붙었습니다. op는 0 더하기 · 1 곱하기 | 진화 전. **쿨 감소는 초 단위 더하기로 씁니다**(음수). 카드에 "N초 감소"로 표시됩니다 |
| 같은 stat이 진화마다 다른 뜻 | `PlayerSkills.DescribeStep`·각 `Fire*` | 투사체는 스나이핑에서 연사 수 · 독수리에서 투하 횟수 · 사냥꾼에서 추격 화살 수 · 하늘 파쇄기에서 한 차례 화살 수 · 피뢰침에서 줄기 수(연출 전용)입니다. 대상 수는 스나이핑에서 저격 대상 · 추적 오브에서 알 개수 · 회오리 폭격에서 미니 회오리 수입니다. **문구 분기와 소비부를 같이 보지 않으면 카드가 거짓말을 합니다** |
| **진화 시작값** | `Assets/Data/Evolutions/Evo_<스킬>_R<루트>_T<차수>` `baseCooldown, baseDamage, baseHits, basePierce, baseProjectiles, baseDuration` | 그 진화만. **진화체의 쿨과 피해는 이 에셋이 단독으로 정합니다**(2026-09-28). 코드에서 곱하던 배율은 전부 없앴으므로 0으로 되돌리지 마십시오 |
| **진화 레벨업 커브** | `Evo_*` `levels[]` | 그 진화만. 44종 전부 자기 커브를 갖고 있습니다. `Evo_Rewind_R1_T2`(블루베리 절멸의 시간)는 액티브가 봉인이라 되감기·쿨 칸이 죽어서, 2026-09-29에 전체 쿨감과 적 둔화로 갈아끼웠습니다 |
| 연사형 진화의 지속시간 | `Evo_Shotgun_R1_*` `levels[]`의 지속 칸 · `PlayerSkills.Barrage` | 메카 버스터·전탄발사는 2026-09-29부터 **발당 피해가 기본 지속 기준으로 고정**이라 지속시간이 곧 총 피해입니다. 종전(총량 보존)과 반대이므로 옛 판단을 그대로 쓰지 마십시오 |
| 시전 시점 쿨 보정 | 스킬 고유 보정은 **없습니다**. 2026-09-28에 화살 R0의 +3초와 화살비의 ×2를 없애고 `Evo_BasicAttack_*.baseCooldown`으로 옮겼습니다 | 남은 것은 되감기 과충전의 ×2(그 한 번만)와 트리·패시브 쿨감입니다 |
| 쿨을 쓰지 않는 진화 | `PlayerSkills.IsPassiveState` — 하늘 파쇄기(상시 화살비) · 블루베리 절멸의 시간(액티브 봉인) | 이 둘은 `baseCooldown`이 발사에 쓰이지 않습니다. 다만 값이 힘·가속 트리 노드의 쿨 구간 판정에는 여전히 들어갑니다 |
| 기본 개수 상수 | `BalanceConstants` `HomingBaseMissiles, EagleBaseDrops, ShotgunBasePellets, SnipingBaseShots` (코드) | 그 스킬 전 단계 |
| 진화 루트 효과 | `PlayerSkills.ApplyPathTierEffect`·각 `Fire*`의 상수 (코드) | 제안만. **피해와 쿨 배수는 여기 없습니다** — 2026-09-28에 전부 에셋으로 옮겼습니다. 남은 것은 크기·개수·외형 같은 다른 축뿐입니다 |
| 서브 딜 비율 | `PlayerSkills`의 `MiniWhirlwindDamageRatio`(30%) · `HomingExplodeRatio`(80%) · `ChasingArrowDamageRatio`(5%) · `bombRatio`(80%) · `eagleRatio`(120%/300%) · 충격파 비율 (코드) | 본체와 **함께** 나가는 피해입니다. 본체를 대체하는 진화에는 비율을 쓰지 않습니다 |
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
