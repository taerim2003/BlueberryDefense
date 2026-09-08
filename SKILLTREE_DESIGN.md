# 스킬트리 런타임 설계

아웃게임 메타 = **노드 스킬트리**. `Assets/SkillTree/MainSkillTree.asset`을 런타임에서 읽어 해금·진행·효과반영.
**트리 그래프(어떤 노드를 어디에 놓고 무엇을 선행으로 걸지)는 태리미가 에디터에서 만든다** — 이 문서는 그때 필요한 계약서다.

- 규칙·저장: `Assets/Scripts/SkillTreeData.cs`
- 효과 매핑의 **원본**: `Assets/Scripts/Meta.cs`의 `SkillEffects` (아래 표는 그걸 옮겨 적은 것 — 어긋나면 코드가 이긴다)
- 에디터 창: `Assets/Editor/SkillTreeEditorWindow.cs` (`Window > Blueberry Defense`)

---

## 1. 노드 4종 — 효과를 정하는 주체가 다르다

| 타입 | 레벨 | 효과를 정하는 곳 | 에디터에서 채울 것 |
|---|---|---|---|
| **Normal** (일반) | 1~`maxLevel` | **에셋** — `effect`(축) × `perLevel` × 노드 레벨 | id · 등급 · **효과 축 · 레벨당 · 단계** |
| **SkillUnlock** (해금) | 1 | 코드 — `node.skill`이 카드 풀에 열림 | id · 등급 · **스킬** |
| **SkillEnhance** (강화) | 1 | **코드** — 아래 §3의 **id가 곧 계약** | id(정확히) · 등급 |
| **SpecialUnlock** (기타) | 1 | **코드** — 아래 §4의 id | id(정확히) · 등급 |

🔴 **일반 노드는 코드를 안 고치고 얼마든지 늘릴 수 있다.** 축과 크기를 에셋이 들고 있기 때문 —
목표 100개 규모는 이 타입이 감당한다. 반대로 **강화·기타 노드는 id가 아래 목록과 한 글자라도 다르면 아무 효과가 없다.**
(오타는 조용히 실패한다 — 노드는 사고 정수만 나간다.)

## 2. 일반 노드의 효과 축 (`MetaUpgradeId`)

에디터의 "효과 축" 드롭다운. 크기는 "레벨당"에 적고, 단계 수는 "단계(만렙)".

| 축 | 단위 | 뜻 |
|---|---|---|
| Attack | % | 모든 피해 +N% |
| Health | 정수 | 최대 체력 +N |
| Regen | 정수 | 5초마다 +N 회복 |
| Cooldown | % | 모든 쿨타임 −N% |
| Duration | % | 스킬 지속시간 +N% |
| Xp | % | 경험치 획득 +N% |
| Wealth | % | 정수 획득 +N% |
| Crit | %p | 치명타 확률 +N%p |
| FlyDamage | % | 비행 적 추가피해 +N% |
| CritDamage | %p | 치명타 **피해 배율** +N/100 (기본 배율 3.0) |
| BossDamage | % | 보스 추가피해 +N% |
| Reroll | 회 | 게임당 리롤 +N회 |

⚠️ **enum은 정수로 직렬화된다 — 축을 추가할 땐 반드시 끝에만.** 중간에 끼우면 기존 노드의 축이 통째로 밀린다.

## 3. 강화 노드 id — 스킬당 2개

### 액티브
| 스킬 | id | 효과 |
|---|---|---|
| 화살 쏘기 | `arrow_StartLev` | 화살 3레벨로 시작 |
| | `arrow_Pierce` | 기본 관통 +1 |
| 휘두르기 | `swing_StartLev` | 휘두르기 3레벨로 시작 |
| | `swing_Knockback` | 넉백 1.5배 |
| 오브 | `orb_BasicSlow` | 기본 둔화 **강화**(감속 +0.1 · 지속 +0.5초) |
| | `orb_Pierce` | 붙잡는 적 수 +3 |
| 독수리 투하 | `eagle_DropNum` | 투하 횟수 +1 |
| | `eagle_fly` | 비행 적 추가피해 +30% |
| 번개 | `thunder_Stack` | 낙뢰 버프 중첩(스택당 피해) 개방 |
| | `thunder_Cooldown` | 낙뢰 1회 타격마다 쿨 −0.01초 |
| 샷건 | `shotgun_BonusHit` | 타수 버프 +1 |
| | `shotgun_Crit` | 산탄 전용 치명타 확률 +30%p |
| 스나이핑 | `Sniping_TwoTarget` | 저격 대상 +1 |
| | `sniping_Crit` | 스나이핑 전용 치명타 확률 +30%p |
| 회오리 | `tornado_CoolDownBonus` | 쿨감 효과를 1.5배로 받음 |
| | `tornado_Fly` | 비행 적 추가피해 +30% |
| 호밍 | `Homing_MissileNum` | 10회 사용마다 미사일 +1 |
| | `homing_Cooldown` | 기본 쿨 −1초 |
| 되감기 | `rewind_NoGcd` | 전역 쿨타임을 트리거하지 않음 |
| | `Rewind_Slow` | 사용 시 모든 적 둔화 |

### 패시브
🔴 **패시브 강화는 그 패시브를 판에서 얻어야 켜진다**(액티브 강화가 그 스킬을 얻어야 의미 있는 것과 같다).
"기본 +N"은 **획득 시 값**에만 얹힌다 — 레벨업 상승값은 안 바뀐다.

| 패시브 | id | 효과 |
|---|---|---|
| 건강 | `health_BaseHp` | 기본 최대체력 +30 (기본값 20 → 50) |
| | `health_HealItem` | 체력회복템 회복량 2배 |
| 힘 | `strength_BaseDmg` | 기본 피해량 +5%p (0.07 → 0.12) |
| | `strength_SlowSkill` | 쿨 **5초 이상** 스킬엔 힘 효과 2배 |
| 암살 | `assassin_BaseCrit` | 기본 치명타 확률 +10%p (0.15 → 0.25) |
| | `assassin_FullCritHit` | 치명타 확률 **100%** 인 스킬은 타수 +1 |
| 방어 | `defense_BaseReduce` | 기본 받는 피해감소 +10%p (0.06 → 0.16) |
| | `defense_Revive` | 사망 시 1회 부활(최대체력 50%) |
| 가속 | `accel_BaseCool` | 기본 쿨감 +5%p (0.05 → 0.10) |
| | `accel_FastSkillDmg` | 쿨 **4초 이하** 스킬 피해 +30% |
| 지식 | `knowledge_BaseXp` | 기본 경험치 +10%p (0.08 → 0.18) |
| | `knowledge_EvoHint` | 레벨업 카드에 진화 조건 표시 |

## 4. 해금·기타 노드 id

- **해금(SkillUnlock)** — id는 자유, `skill` 드롭다운이 계약이다: `New_Orb`·`New_Lightning`·`New_Homing`·`New_Shotgun`·`New_Rewind`.
  ⚠️ **해금 노드가 없는 스킬은 처음부터 카드 풀에 나온다.** 기본공격·회오리·독수리·스나이핑·휘두르기·포도 투척이 그렇다.
- **기타(SpecialUnlock)** — id가 계약이다:
  - `New_Reroll` — 레벨업 리롤 개방(+1회)
  - `New_Evolution` — **1차 진화 개방**
  - `New_Evolution2` — **2차 진화 개방**
  - `Root_Skilltree` — 트리의 뿌리(효과 없음, 비용 1정수)

🔴 **진화 게이트는 "노드가 트리에 있을 때만" 잠근다.** 트리에 `New_Evolution`이 없으면 진화는 지금처럼 그냥 열려 있다 —
노드를 안 만든 상태에서 진화가 통째로 막히는 사고를 막기 위한 설계다(스킬 해금 게이팅과 같은 원칙).

## 5. 비용

노드마다 정수를 직접 치지 않고 **등급(tier)** 만 지정 → 코드가 환산(`SkillTreeSave.TierCost`).
`tier 0 = 1정수`(루트 전용), `tier n = 15 + (n−1)×5`. 레벨업 비용은 `기본비용 × 1.5^(현재 레벨)`.
밸런싱은 `TierCostBase`·`TierCostStep` 두 상수만 만지면 전체에 반영된다.

## 6. 저장 (PlayerPrefs)

| 키 | 의미 |
|---|---|
| `meta.currency` | 정수 earned 총량 |
| `skilltree.current` | 노드별 레벨 CSV `id:level,id:level` |

available = earned − Σ(해금된 노드에 지불한 정수). **자원 1개 · 되돌리기 불가**(환불·리스펙·빌드셋 없음).

## 7. 표시 문구

노드 이름·설명은 `Loc.TOr("tree.name."+id, displayName)` / `"tree.desc."+id`.
**표에 없으면 에셋에 적은 값이 그대로 나온다** — 새 노드를 만들 때 번역을 먼저 안 채워도 화면이 비지 않는다.
정식 문구는 `Assets/Localization/assets_*.tsv`에 넣고 `Window > Blueberry Defense > 번역 - 모든 TSV를 표에 적재`.
