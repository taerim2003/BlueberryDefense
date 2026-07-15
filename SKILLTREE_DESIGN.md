# 스킬트리 런타임 설계 (확정: 2026-07-15)

아웃게임 메타 = **노드 스킬트리**. `Assets/SkillTree/MainSkillTree.asset`(28노드)을 런타임에서 읽어
해금·진행·효과반영. 이 문서는 사용자와 확정한 설계 결정을 담는다. 구현 순서는 맨 아래.

---

## 1. 핵심 규칙 (확정)

- **모든 노드는 일회성 개방.** 레벨 없음. 한 번 찍으면 영구 해금 + flat 효과 부여.
  → 에셋의 `maxLevel/perLevel/costGrowth`는 레벨링 의미 폐기. `perLevel`은 이제 **효과 크기(flat)**로만 재해석,
    `maxLevel/costGrowth`는 런타임에서 무시(에디터 필드는 그대로 둠 — surgical).
- **2자원**:
  - **정수(essence)** — 인게임 적 처치로 획득. 일반 노드(`type=Normal/ActiveSkill`) 비용 = `cost`.
  - **태양결정(crystal)** — 스테이지 마일스톤 보상. 게이트 노드(`type=Gate`) 비용 = `gateCost`.
- **해금 조건**: `prereqIds` 전부 해금됨 AND 해당 자원 available ≥ 비용.
- **available 자원 = earned − Σ(해금된 노드 비용)**. 별도 "spent" 장부 없이 파생 계산.
  자원 총량(earned)은 단조 증가하므로, 한번 유효했던 빌드는 항상 재구매 가능.
- **리스펙**: 무료. 현재 해금집합 전체 초기화 → available 자동 환급.
- **빌드셋 1~5**: 해금집합 스냅샷 저장 슬롯. 로드 = 현재를 슬롯 내용으로 교체(항상 afford 가능).

## 2. 태양결정 획득 (확정)

- 스테이지 **15 클리어 +1**, 스테이지 **20 클리어(=게임클리어) +1**. `GameManager.cs`에서 훅.

## 3. 저장 스키마 (PlayerPrefs)

| 키 | 의미 |
|---|---|
| `meta.currency` | 정수 earned 총량 (기존 키 재사용 — 인게임 적립분 이어짐) |
| `meta.crystal` | 태양결정 earned 총량 (신규) |
| `skilltree.current` | 현재 해금 노드 id CSV |
| `skilltree.build.1` ~ `.5` | 빌드셋 슬롯 CSV (빈 슬롯 허용) |

- available_essence = `meta.currency` − Σ cost(해금된 Normal/ActiveSkill 노드)
- available_crystal = `meta.crystal` − Σ gateCost(해금된 Gate 노드)

## 4. 효과 훅 매핑 (28노드)

효과는 **노드 id 기준 코드 레지스트리**(`SkillEffects`)에서 적용. 에셋 effect 필드는 신뢰 안 함
(heal-drop·reroll·arrow-start 등은 MetaUpgradeId enum으로 표현 불가). 판 시작 시 현재 해금집합을 순회해 적용.

### 이번에 실제 연결 (단순스탯 → MetaBonuses/PlayerStats)
| 노드 | 효과 | 반영처 |
|---|---|---|
| atk_1/2/3 | 피해 +5/8/10% | damage mult |
| hp_1/2 | 최대체력 +10/15 | max hp |
| gate_crit | 기본 치명 +10% | CritBonus |
| crit_1/2 | 치명 +3/5% | CritBonus |
| exp_1/2 | 경험치 +5/8% | XP mult |
| gold_1/2/3 | 정수획득 +10/15/25% | CurrencyMult |
| gate_cooldown | 쿨 −10% | CooldownMult |
| cool_1/2 | 쿨 −3/5% | CooldownMult |

### 신규 훅 — 구현 완료 (2026-07-15 세션 2)
| 노드 | 효과 | 구현 위치 |
|---|---|---|
| root_hp | 적 확률 체력회복 드랍 +5%p | `MetaBonuses.HealDropChanceBonus` → `Enemy` 사망 드랍 |
| gate_fly / fly_1 / fly_2 | 비행 추가피해 +20/5/10% | `MetaBonuses.FlyDamageBonus` → `Enemy.TakeDamage`(isFlying) |
| eagle_fly | 독수리 비행피해 +30% | `MetaBonuses.EagleFlyDamageBonus` (source==EagleDrop) |
| orb_BasicFly | 오브 비행타격 가능 | `MetaBonuses.OrbCanHitFlying` → `Orb.cs` |
| tornado_CoolDownBonus | 회오리 쿨감 1.5배 | `MetaBonuses.WhirlwindCooldownBonus` → `PlayerSkills` 캐스트 쿨 |
| thunder_Cooldown | 낙뢰 칠때마다 쿨 −0.1초 | `MetaBonuses.ThunderCooldownPerStrike` → `LightningStorm.OnProc` 구독 |
| arrow_StartLev | 화살 3레벨 시작 | `PlayerSkills.SetSkillStartLevel` (MetaRunApplier.Start) |
| refresh_bonus | 리프레시 초기화 확률 +5%p | `MetaBonuses.RefreshChanceBonus` → `PlayerSkills` 캐스트 |
| reroll_1 / reroll_2 | 리롤 해금 / +1 (게임당) | `LevelUpUI` 리롤 버튼 + `InitRerolls` |

**→ 28노드 전부 실동작. 스텁 없음.**

## 5. 인게임 트리 UI

- Title 씬 내 패널(현 `UpgradeShopUI` 자리 교체). **팬 O, 줌 X**(에디터와 동일).
- `MainSkillTree.asset`을 읽어 `editorPos`로 노드 배치, `prereqIds`로 연결선.
- 노드 상태: 잠김(prereq 미충족·회색) / 가능(afford O·강조) / 불가(afford X) / 해금(체크).
- 색: Normal=하늘, Gate=금색, ActiveSkill=마젠타 (에디터 재사용).
- 하단: 정수·태양결정 2자원 표시, 선택노드 상세(이름/설명/비용/구매), 리스펙 버튼, 빌드셋 1~5 버튼.

---

## 구현 순서 (이번 세션)

1. **저장/데이터 계층** `SkillTreeSave` (정수·결정·해금집합·빌드셋) + available 파생. → script-execute 검증
2. **효과 적용** `SkillEffects.Apply(해금집합)` — 단순스탯 연결, 나머지 스텁. `MetaRunApplier` 교체
3. **태양결정 훅** `GameManager` 스15/스20
4. **인게임 트리 UI** 팬·렌더·상태·구매·리스펙·빌드셋
5. **리스트 상점 제거** (`UpgradeShopUI`/`UpgradeCell`/`MetaUpgrades` 상점경로) — UI 검증 후
