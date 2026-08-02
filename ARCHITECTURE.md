# ARCHITECTURE.md — 코드 구조 지도

> **AI 코딩 도우미용 지도.** grep으로 찾을 수 있는 건 안 적음 (파일 목록·함수 시그니처 X).
> **여기 적는 것**: 폴더 책임, 매니저 호출 관계, 핵심 처리 흐름, "X 추가하려면 어디 손대야 하나".
>
> **유지보수 원칙**: 큰 구조 바뀔 때만 갱신. 함수 추가/리네임 정도로는 손대지 말 것.
> 코드와 어긋난다 싶으면 코드가 정답 — 이 문서를 의심할 것.

---

## 한눈에 보기

**블루베리 디펜스 = 뱀서라이크 디펜스.** 화면 **오른쪽**에 고정된 플레이어(움직이지 않음, 기본 맵 기준 x=+7.91)를 향해 **왼쪽**(스포너 x=−9)에서 블루베리 몬스터들이 우측으로 행진해 온다. 플레이어는 Q/W/E/R 4개 액티브 스킬 + 4개 패시브를 자동/키입력으로 발동해 몹을 잡고, 처치 XP로 레벨업할 때마다 뜨는 3지선다에서 신규 스킬 획득·레벨업·스탯강화 중 하나를 고른다. 스킬이 **만렙(Lv.10)**에 닿고 **진화 아이템**(벽 스테이지 엘리트 드랍)이 있으면 **진화 트리**(스킬/패시브마다 2루트 × 2티어)가 열린다 — 진화하면 Lv.1로 리셋되고 다시 만렙까지 큰다. 각 스테이지는 정해진 물량(`StageData.spawnCount`)을 다 스폰하고 잔몹이 전멸하면 클리어(타이머 아님) — **최종 스테이지의 보스**(승천1=15 / 2=20 / 3=25, 단일 출처 `GameManager.FinalStage`)까지 잡으면 게임 클리어, 체력 0이면 게임오버. (게임 비전·기획은 [GDD.md](GDD.md), 진행 상태는 [HANDOFF.md](HANDOFF.md).)

**기술 스택**: Unity 6 (6000.4.4f1) · URP 2D · uGUI(+TextMeshPro) · DOTween(트위닝) · JuicyUI(UI 연출) · Vefects Pixel Craft VFX(파티클). 코드는 순수 MonoBehaviour + static 상태 홀더, DI 프레임워크 없음.

---

## 폴더 책임

루트: `Assets/`

| 폴더 | 책임 | 새 코드는 어디로 |
|---|---|---|
| `Assets/Scripts/` | **모든 게임플레이 코드.** 매니저·플레이어 시스템·적/전투 오브젝트·UI 컨트롤러·static 상태 홀더·ScriptableObject 정의가 전부 여기 평면적으로 있음 (하위 폴더 없음) | 런타임 로직 전부 |
| `Assets/Editor/` | 에디터 전용 툴 (`CheatWindow` — Play 중 레벨/스킬/진화/스테이지 즉시 조작) | 에디터 툴·인스펙터 |
| `Assets/JuicyUI/` | 서드파티 UI 연출 플러그인 (`JuicyButton`, `UITransition`, `JuicyHealthBar`, 옵션 패널). 우리 코드는 `UITransition`만 참조 | 손대지 말 것 (외부) |
| `Assets/Plugins/Demigiant/DOTween/` | 트위닝 라이브러리 | 손대지 말 것 (외부) |
| `Assets/Vefects/` | 픽셀 VFX 프리팹 팩 (`VFX_2D_*`) | 손대지 말 것 (외부) |

> ScriptableObject **정의**(`StageTable`, `EvolutionTierTextTableSO`)는 `Scripts/`에, 실제 **에셋 인스턴스**는 프로젝트 데이터 폴더에 있고 씬/매니저가 SerializeField로 참조.

---

## 핵심 오브젝트 카탈로그 (매니저/싱글톤 등)

> 게임의 중심 객체들. "누가 누구를 부르나"가 핵심.

| 이름 | 책임 | 누구를 부르나 |
|---|---|---|
| **GameManager** (싱글톤) | 스테이지 진행(물량 소진+잔몹 전멸→전환 텀), 게임오버/클리어(`FinalStage`) 판정, `Time.timeScale` 종료 정지. Awake에서 `DamageMeter.Reset`·`DOTween` 전역설정 | `EnemySpawner.StageSpawnComplete`·`PlayerSkills.ResetAllCooldowns`, `Enemy` 수 폴링 |
| **EnemySpawner** | `StageData.spawnCount`만큼 적 스폰(물량 기반)하면 정지, HP/속도 스텝 보정 적용, 15라운드 마지막 물량=보스. 스폰 진행률(SpawnRatio) 소유. **중간 소환**(화면 안 예고→부대 투입)도 여기서 관장 | `GameManager`(현재 스테이지/스폰정지 조회), `Enemy.ApplyStageMultipliers`·`Enemy.PopIn`, `AmbushMarker` |
| **AmbushMarker** | 중간 소환 예고 링(런타임 생성, 씬 배선 없음). 커지며 점점 빠르게 깜빡이다 시간이 차면 콜백 호출 후 자멸 | `EnemySpawner`가 생성·소유 |
| **PlayerSkills** | 액티브 4종(Q/W/E/R) 캐스트 로직, 스킬 레벨업/진화 트리, 전투 오브젝트 스폰(투사체·회오리·오브·독수리·설치기), 낙뢰 파라미터 설정 | `LightningStorm`, `BuffTracker`, `PlayerPassives`(연계 조건·static 보너스), 모든 전투 프리팹, `ObjectPool` |
| **PlayerPassives** | 패시브 4종 보유/레벨업/진화, **연계 효과의 static 상태 필드**(치명타·반격·경험치 배율 등) 소유, 체력재생·반격·낙뢰연계 이벤트 처리 | `PlayerSkills`/`PlayerHealth`/`PlayerExperience`(스탯 반영), `LightningStorm.OnProc` 구독 |
| **PlayerHealth** | 현재체력/최대체력/오버힐(보호막), 피격 흡수, 게임오버 트리거, `OnDamageTaken` 이벤트 | `GameManager.GameOver` |
| **PlayerExperience** (싱글톤) | XP 누적·레벨업(스테이지별 XP 감쇠), 레벨업마다 UI 호출 | `LevelUpUI.Show`, `ObjectPool`(레벨업 VFX) |
| **LevelUpUI** (싱글톤) | 레벨업 3지선다 모달 구성(신규스킬/레벨업/스탯강화 후보 셔플), 보물상자 진화 라우팅 | `PlayerSkills`/`PlayerPassives`/`PlayerHealth`, `EvolutionTreeUI.Show`, `ModalPause` |
| **EvolutionTreeUI** (싱글톤) | 진화 모달(**씬 노드는 아직 3×3**, 런타임에 왼쪽 위 2×2만 켠다), 루트/티어 표시·클릭 시 진화 적용 | `PlayerSkills.EvolveSkill`/`PlayerPassives.EvolvePassive`, `ModalPause` |
| **HUDController** | 체력바·경험치바·스테이지·스킬슬롯 쿨다운·패시브·버프 표시(매 프레임 폴링, DOTween 연출) | `Player*` 조회, `BuffTracker.GetActive` |
| **DamageMeterUI** | 게임오버/클리어 시 스킬별 딜량 패널 | `DamageMeter.GetBreakdown` |
| **ObjectPool** (지연생성 싱글톤) | VFX·파티클·데미지숫자 풀링, 스폰 시 파티클/오디오 자동 재생 + `SfxLimiter`·`AudioThrottle` 적용 | `SfxLimiter`, `AudioThrottle` |

**static 상태 홀더 (씬 오브젝트 아님, 플레이어 1명 전제로 전역 상태 보관):**

| 이름 | 역할 |
|---|---|
| **LightningStorm** | 낙뢰 버프 스택(지속시간 리스트), 발동확률·재귀·체인·중첩피해 파라미터. `PlayerSkills`가 설정, `Enemy.TakeDamage`가 굴림 |
| **BuffTracker** | 지속시간 있는 HUD 버프 범용 레지스트리 (`Set`/`Clear`/`GetActive`). 발생측이 등록, HUD가 읽음 |
| **DamageMeter** | 스킬별 누적 딜 집계 (`Enemy.TakeDamage`가 기록) |
| **ModalPause** | 참조카운트 기반 모달 일시정지 (`Time.timeScale` 0↔1). 모달 여러 개 겹쳐도 안전 |
| **SfxPlayer** | 라운드로빈 AudioSource 풀로 효과음 재생 |
| **AudioThrottle** | 동일 클립 같은 프레임 중복재생 디바운스 |
| **PlayerPassives.static 필드** | 위 표의 PlayerPassives 항목 참조 — 연계 보너스가 여기 모여 있음 |

**의존성 방향**: UI(LevelUpUI/EvolutionTreeUI/HUD) → 플레이어 시스템(PlayerSkills/Passives/Health/Experience) → static 상태 홀더 + Enemy. 전투 오브젝트(Projectile/Whirlwind/Orb/SmallOrb/OrbAltar)는 `Enemy.TakeDamage`만 호출(상향 의존 없음). `Enemy`는 처치 시 `PlayerExperience`/`LevelUpUI`/`DamageMeter`를 부르는 예외적 상향 호출이 있음. **스킬 간 시너지는 정식 참조 대신 static 결합**으로 연결됨(`MiniWhirlwindDamageBonus`, `LightningStorm.*`, `PlayerPassives.*`) — 회오리에 투자하면 독수리투하의 미니 회오리도 강해지는 식.

---

## 핵심 루프 — 1판의 흐름

```
[GameManager.Awake] DamageMeter 리셋 · 하트프리팹 주입 · DOTween unscaled 설정
  → [EnemySpawner.Update] StageData.spawnCount 만큼 적 스폰하면 정지 (물량 기반, HP/속도 스텝 보정)
                          · 스폰 진행률(SpawnRatio)로 보물상자 후반 등장·15라운드 마지막 물량=보스 판정
  → [Enemy.Update] 왼쪽으로 행진 · sortingOrder를 x좌표로 갱신(원근)
      ├─ 플레이어와 충돌 → PlayerHealth.TakeDamage → (체력0) GameManager.GameOver
      └─ 스킬 피격 → Enemy.TakeSkillHit(멀티히트: 총뎀 유지·N분할·서브히트별 크리/첫히트만 낙뢰)
                         → Enemy.TakeDamage → 딜미터 기록·낙뢰/체인 판정·(체력0) 처치·사망분출(보스)
                         → PlayerExperience.AddXP  (보물상자면 LevelUpUI.ShowTreasureReward)
  → [PlayerExperience] XP 임계 도달 → 레벨업 → LevelUpUI.Show → ModalPause(timeScale 0)
      → 플레이어가 3지선다 선택 → AcquireSkill / UpgradeLevel / Evolve / 스탯강화 → ModalPause 해제
  → [GameManager.Update] EnemySpawner.StageSpawnComplete + 잔몹 0 → CurrentStage++ → 전환 텀(스폰 정지)
                          → 최종 스테이지(보스) 클리어 시 GameClear
  → [DamageMeterUI] 게임오버/클리어 감지 → 딜미터 패널 표시
```

**입력 처리**: 플레이어는 이동하지 않는다. 유일한 상시 입력은 `PlayerSkills.Update`에서 Q/W/E/R 키를 읽어(new Input System `Keyboard.current`) 해당 스킬을 `TryUseSkill`로 발동하는 것. 글로벌 쿨다운(0.4초) + 스킬별 쿨다운을 통과하면 캐스트. 나머지 상호작용은 모두 모달 UI 버튼 클릭(레벨업/진화).

**치명타 모델**: 캐스트 시점엔 확률만 확정하고, 실제 치명타 여부는 각 데미지 이벤트(투사체 명중·틱·낙뢰 등)마다 `PlayerPassives.ApplyCrit`로 개별적으로 굴린다("타격 기준 치명타").

---

## "X 추가하려면 어디 봐야 하나"

> 가장 자주 쓰는 표. 새 콘텐츠/시스템 추가 시 손댈 파일을 한 줄로.

| 변경하고 싶은 것 | 손대야 할 파일 |
|---|---|
| **새 액티브 스킬** | `PlayerSkills.cs`: `ActiveSkillId` enum + `GetDefaultCooldown/Damage` + `Fire*` 캐스트 로직 + 진화 3표(`ApplyPathTierEffect`/`DescribePathEffect`/`GetPathTierTitle`) + 연계 프리렉맵(`GetPassivePrereq`/`GetActivePrereq`) / `LevelUpUI.cs` 설명·아이콘 / 프리팹 + 스킬/젬 아이콘 배열 |
| **새 패시브** | `PlayerPassives.cs`: `PassiveSkillId` enum + `AcquirePassive`·`ApplyPassiveLevelEffect` + 진화 3표 + static 보너스 필드(필요시 소비처도) + 프리렉맵 / `LevelUpUI.cs` 설명 |
| **새 적 타입** | `Enemy` 프리팹(플래그: `isFlying`/`isTreasure`/`isHopper` 등) / `MapDefinition`에 프리팹 필드 / `EnemySpawner`의 프리팹 선택 else-if 사슬 + `PickAmbushPrefab`(지상 유닛이면) / `StageData`에 등장 확률 필드. ⚠️ **else-if 사슬이라 뒤에 있을수록 실효 확률이 낮다**(앞 확률에 안 걸린 나머지에만 굴림) |
| **새 스테이지 / 밸런스** | `StageTable` 에셋의 `StageData` 배열 (코드 X). 스텝 배율은 `EnemySpawner`의 `HpStepBonus`/`SpeedStepBonus` |
| **스테이지 스폰 리듬(꾸준 vs 무리)** | `StageData.burstSize`/`burstRest` (데이터). 0·1이면 기존 균일 간격, 2 이상이면 그만큼 연달아 쏟고 쉰다. **무리일 땐 `spawnInterval`이 무리 *안*의 간격**이 되므로 같이 낮춰야 한다 |
| **중간 소환 배치·강도** | 횟수는 `StageData.ambushCount` (데이터). 예고 시간·소환 구간·부대 크기는 `BalanceConstants.Ambush*` |
| **맵의 레인 높이(화면 안 위치)** | `MapDefinition.cameraYLift` (데이터). `RunBootstrap.ApplyMap`이 **카메라와 배경을 같이 위로** 올려서 레인이 화면 아래쪽으로 내려간 것처럼 보이게 한다. ⚠️ **월드 좌표는 일부러 안 건드린다** — 절대 y를 쓰는 것들(회오리 착지선 `groundY=0`)이 어긋나기 때문. 대신 카메라 기준 값(UFO 고도·강하 높이)은 자동으로 따라 올라간다 |
| **새 맵 / 맵 필드 크기** | `MapDefinition` 에셋 하나 (코드 X). `fieldScale`이 플레이 영역 배율 — `RunBootstrap.ApplyFieldScale`이 판 시작 시 카메라 ortho·플레이어/스포너 좌표에 곱한다. **캐릭터·적 크기는 안 변하므로 필드가 넓을수록 화면에서 작아 보인다.** 배경 그림도 같은 배율(PPU 18 기준 1배=320×180px)이어야 가장자리가 안 빈다. 만든 뒤 Title 씬 `MapSelectUI.maps` 배열에 추가 |
| **새 진화 티어 효과** | 영구 스탯이면 `PlayerSkills.ApplyPathTierEffect`/`PlayerPassives.ApplyPassivePathTierEffect`, 실시간 기믹(관통·분열·재귀 등)이면 해당 `Fire*`/`TakeDamage`에서 `PathTier` 직접 읽기 + 설명/제목 표. 표시 텍스트만 바꿀 땐 `EvolutionTierTextTableSO` 에셋 |
| **새 HUD 버프 표시** | 발생측에서 `BuffTracker.Set(key,...)` 호출 + `HUDController.GetBuffIcon`에 key→아이콘 한 줄 (슬롯은 자동 채워짐) |
| **레벨업 선택지 부족 시 대체 보상** | 레벨업 가능한 후보가 3개 미만이면 `LevelUpUI`가 '정수 +10' 선택지를 하나 끼우고, 그래도 모자라면 선택지 자체가 1~2개만 뜸. 지급량은 `LevelUpUI.EssenceReward` |
| **새 VFX/파티클** | 프리팹 준비 후 `ObjectPool.Instance.Spawn/Despawn` 호출 (풀링·오디오 자동 처리) |

---

## 자주 헷갈리는 것

- 🔴 **새 `SerializeField`를 추가할 때 클래스 기본값을 믿지 말 것.** 이미 임포트된 에셋·프리팹은 그 필드가 YAML에 없어도 **Unity 임포트 캐시에 구워진 옛 값**을 계속 쓴다. 스크립트 기본값만 고치면 소급되지 않는다. 두 번 크게 당했다 — `requiresAntiAir`를 `true`로 두는 바람에 **모든 지상 적이 "대공 필요"가 되어 기본공격·오브·회오리가 전부 적을 통과**했고, `extendedHpGrowth`는 1.08로 고쳐도 계속 1.28이 나왔다. 그래서:
  1. 필드를 추가하면 **그 즉시 모든 기존 에셋에 값을 명시적으로 써 넣는다.**
  2. **불리언 기본값은 언제나 "기존 동작과 같은 쪽"으로** — 특수 케이스를 기본값에 두지 않는다.
  3. 쓴 뒤 `AssetDatabase.LoadAssetAtPath`로 **되읽어 확인한다.**
- **static 상태가 도메인 리로드 없이는 안 풀린다.** `PlayerPassives`의 static 필드들, `LightningStorm.*`, `PlayerSkills.MiniWhirlwindDamageBonus`는 전역 static이라 `GameManager.Awake`에서 리셋하는 건 `DamageMeter`뿐이다. 씬 재시작(도메인 리로드 없이)이나 자동화 테스트에서 이전 판의 진화 보너스가 그대로 남아있을 수 있음 — "Enter Play Mode Options"로 도메인 리로드를 끄면 특히 주의.
- **진화는 레벨업의 연장이 아니라 리셋이다.** 레벨업은 **만렙(`BalanceConstants.MaxSkillLevel`=10)**에서 막히고(`CanUpgradeSkill`), 그 스킬은 레벨업 후보에서 빠진다. 만렙 스킬은 **진화 아이템**(벽 스테이지 엘리트 드랍 → `LevelUpUI.ShowEvolutionReward`)을 먹어야 진화하고, 진화하면 Lv.1로 리셋되어 다시 큰다. 루트는 2개뿐이고 **연계 스킬을 보유해야 열린다**(`IsRouteUnlocked` — path1=패시브 연계·path2=액티브 연계). 1차에서 고른 루트는 2차까지 고정. 좌표 번역은 전부 `EvolutionRoutes`가 하고, 저장은 여전히 `PathTier[3]`이다.
- **3번째 슬롯 레벨업은 occurrence(`ThirdSlotCount`) 순환이다.** `DescribeThirdUpgradeEffect`(미리보기)와 `ApplyThirdUpgradeEffect`(적용)가 **증가 전 같은 값**을 읽어야 미리보기와 실제가 일치한다. Apply가 끝에서 `ThirdSlotCount++`.
- **모달 일시정지는 참조카운트.** 레벨업+진화 패널이 겹쳐 뜰 수 있어 `ModalPause.Push/Pop`으로만 `timeScale`을 만진다. timeScale=0 중에도 DOTween 연출이 돌도록 `GameManager.Awake`에서 `DOTween.defaultTimeScaleIndependent=true`.
- **적은 풀링된다 — 죽은 적은 `null`이 아니다.** `EnemySpawner`·UFO 투하·사망 분출은 전부 `Enemy.Spawn`(→`ObjectPool`)을 거치고, 사망/캐리어 퇴장은 `Destroy` 대신 풀에 반납한다. 여기서 나오는 두 가지 함정:
  1. **적 참조를 프레임 넘어 들고 있으면 반드시 `Enemy.IsAlive`를 봐야 한다.** 예전엔 `Destroy`가 만든 가짜 null 덕에 `== null`만으로 정리됐지만, 풀은 비활성화만 하므로 참조가 살아남고 **재활용되면 엉뚱한 새 적을 가리킨다.** 현재 소비처는 `Orb`(overlapping·claimed)·`Whirlwind`(overlapping)·`HomingMissile`(target). 새 스킬이 적을 기억한다면 여기 합류할 것. (한 프레임 안에서만 쓰는 `FindObjectsByType`은 기본이 "비활성 제외"라 안전 — `GameManager`의 "잔몹 0" 판정도 이쪽이다.)
  2. **`Awake`는 재사용 시 다시 안 돈다.** 런타임에 변하는 필드는 전부 `Enemy.InitializeSpawn`에서 되돌린다. **`Enemy`에 런타임 상태 필드를 추가하면 거기도 같이 고칠 것** — 빠뜨리면 "소환되자마자 죽어 있는 적"처럼 간헐적으로만 재현되는 버그가 된다.
- **`Enemy.TakeDamage`는 같은 프레임 재진입에 안전해야 한다.** `Destroy`는 프레임 끝에 실행되므로 낙뢰 재귀/체인이 같은 프레임에 사망 처리를 두 번 돌 수 있어 `isDead` 가드가 두 군데 있다. 낙뢰/체인 판정은 사망 처리보다 **앞**에 있어야 한다(한 방 킬 타격도 낙뢰를 굴릴 기회를 갖도록).
- **호핑 적(`isHopper`)은 y를 매 프레임 덮어쓴다.** `UpdateHop`이 `hopBaseY + 포물선`을 **절대값으로** 대입한다(가산이 아님) — 박치기 돌진·기절처럼 x만 만지는 로직과 섞여도 높이가 어긋나 쌓이지 않는다. 부작용 두 가지가 **의도된 것**이다: ① 떠 있는 동안 지상 스킬 히트박스를 흘려보낸다(=이 적의 정체성), ② `BlockedAhead`의 레인 허용치(`EnemyLaneTolerance` 0.6)를 벗어나 **지상 무리를 뛰어넘는다**. `hopBaseY`는 스폰 시 확정되고, 팝인(중간 소환)으로 나온 개체는 착지 지점을 기준으로 갱신된다.
- **적의 y를 흔드는 방식이 두 가지고, 서로 바꿔 쓰면 안 된다.** 이동 방식이 다르기 때문이다.
  - **콩콩이 도약 = 절대 대입** (`UpdateHop`). 전진이 `Vector2.right` 이동이라 y는 아무도 안 건드린다 → 매 프레임 `y = hopBaseY + 포물선`으로 **덮어써도** 안전하고, 박치기 돌진처럼 x만 만지는 로직과 섞여도 어긋나 쌓이지 않는다.
  - **서핑 너울 = 차분 누적** (`UpdateDiveBob`). 강하 전진이 `Translate`로 y까지 **누적**해서 움직이므로 절대 대입을 쓰면 강하 자체가 지워진다. 그래서 `position += (이번 흔들림 − 지난 흔들림)`만 더한다. 🔴 **매 프레임 offset을 그냥 더하면 경로에 쌓여 적이 하늘로 떠오른다.** 멈춰 설 땐 0으로 되돌려야 뜬 채로 굳지 않는다.
- **비행 적(`isFlying`) 타격 규칙이 스킬마다 다르다.** 투사체는 진화(`CanHitFlying`) 전엔 못 맞히고 발사점을 위로 올려 세로 히트박스로 커버, 오브는 공중추가피해 진화로만 해금, 미니 회오리는 아예 불가. 새 스킬 만들 때 이 플래그 처리를 빠뜨리기 쉬움.
- **UFO 수송선(`isCarrier`)은 좌진 행진을 안 한다.** `Enemy.Update`가 `isCarrier`면 `UpdateCarrier`(하강→호버→상승 상태기계)로 분기하고 일반 이동/`spawnYOffset`을 건너뛴다. 스폰 y를 `carrierLaneY`(투하물 착지 지면)로 기억한 뒤 `Camera.main` 기준으로 화면 위 랜덤 x에 재배치. 호버 중 1회 `DropSquad`(투하물에 자기 스테이지 배율을 물려줌). 상승 완료 시 `Destroy`(=격추 안 하면 XP 없음). 투하 전 격추 시 부대 안 나옴.
- **🐞 에셋 캐시 함정 — 새 SO/프리팹 필드는 반드시 에셋에 값을 써 넣을 것.** 스크립트의 **기본값만 바꾸면 이미 임포트된 에셋의 캐시값이 이긴다.** 두 번 크게 당했다: ① `requiresAntiAir` 기본값을 true로 뒀더니 이 필드가 직렬화 안 된 기존 프리팹 전부가 대공 전용이 되어 **지상 적이 아무 스킬에도 안 맞았다**. ② `extendedHpGrowth`를 고쳤는데 계속 옛 값이 나왔다. → 새 필드를 추가하면 **모든 관련 에셋에 명시적으로 기록**하고, 기본값은 "안전한 쪽"(보통 false/0 = 기존 동작 유지)으로 둘 것.
- **단일/제한 타격 투사체는 `Destroy` 호출만으론 부족하다 — 플래그 가드가 필수.** `Destroy(gameObject)`는 **프레임 끝**에 처리되므로, 같은 물리 스텝에 이미 잡힌 나머지 `OnTriggerEnter2D` 콜백이 그대로 실행된다(관통 예산이 0이어도 그 프레임에 닿은 적을 전부 때렸다). `Projectile`은 `consumed` 플래그 + 콜라이더 즉시 비활성으로 막는다. **새 투사체를 만들 때 이 함정을 기억할 것.** (`Whirlwind`·`Orb`의 지속 타격은 원래 광역이라 다수 타격이 정상.)
- **`SkillStat` enum은 맨 뒤에만 추가할 것** — 중간에 끼우면 기존 에셋의 정수 직렬화가 밀린다. `ActiveSkillId`도 같다(아이콘 인덱스·저장값이 순서에 묶여 있음).
- **낙뢰만 데이터 출처가 다르다.** `Prog_Lightning.baseDamage`(=0)는 무시되고 `LightningStorm.BaseProcDamage`(코드 상수)가 실제 시작 피해다 — `GetDefaultDamage`가 낙뢰만 특수 처리. 낙뢰 피해를 못 찾겠으면 여기.
- **효과음 3중 안전장치.** `AudioThrottle`(같은 프레임 중복 차단) → `SfxPlayer`(라운드로빈 풀) → `SfxLimiter`(0dBFS 근접 원본 클립 브릭월 리미팅). 원본 VFX 팩 클립이 대부분 풀스케일이라 볼륨만 올리면 클리핑 남.

---

## 의도적으로 안 적은 것 (grep으로 찾을 것)

- 클래스의 public 메서드 목록 / 시그니처 → 파일 열어보면 됨
- 매니저별 SerializeField 필드 → Inspector 또는 코드 상단 보면 됨
- 진화 트리 각 티어의 정확한 수치·효과 → `PlayerSkills`/`PlayerPassives`의 `DescribePathEffect`/`ApplyPathTierEffect`
- 외부 패키지(JuicyUI/DOTween/Vefects) 내부 구조
- 밸런스 수치 (StageTable 에셋 / 각 컴포넌트 SerializeField)
- UI 텍스트
