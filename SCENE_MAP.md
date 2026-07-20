# SCENE_MAP.md — 씬 구조 지도

> **AI(MCP)용 씬 지도.** ARCHITECTURE.md가 코드 지도라면, 이건 씬에 실제로 배치된 오브젝트·컴포넌트·핵심 배선 지도다.
> Claude가 MCP로 씬을 조작하므로 매 세션이 콜드 스타트 — 이 문서가 "어디에 뭐가 붙어 있나"를 미리 알려 MCP 왕복을 줄인다.
>
> **적는 것**: 매니저/싱글톤 오브젝트 위치, 오브젝트에 붙은 load-bearing 스크립트, Canvas/UI 루트, "새 오브젝트 어디 붙이나".
> **안 적는 것**(MCP 쿼리로 즉시 확인): 좌표·스케일·색상, 개별 위젯 나열, SerializeField 수치, 프리팹 내부, 밸런스값(→ BALANCE_MAP.md).
>
> **유지 원칙**: 큰 구조 바뀔 때만 갱신(오브젝트 추가/이동/스크립트 재배치). 씬과 어긋나면 씬이 정답 — 이 문서를 의심할 것.
> 갱신 시점: 리팩토링 각 Phase 완료 시 필수(HANDOFF 로드맵 참고).

---

## 씬 목록

| 씬 | buildIndex | 역할 |
|---|---|---|
| `Assets/Scenes/Title.unity` | 0 | 메인 메뉴 + 아웃게임 스킬트리 |
| `Assets/Scenes/SampleScene.unity` | 1 | **인게임 1판 전체** (유일한 게임 씬 — 맵 추가해도 복제 안 함) |

> `Assets/_Recovery/0.unity`는 복구 잔재 — 무시.

---

## Title.unity

```
Main Camera
EventSystem
Canvas
 ├─ Background · TitleImage · CurrencyText
 ├─ Btn_플레이 · Btn_업그레이드 · Btn_컬렉션 · Btn_설정 · Btn_종료   (각 +Text 자식)
 ├─ SkillTreeRoot  [activeSelf=false — 버튼으로 여는 전체화면 패널]
 │    Viewport/Content · Title · EssenceText · CrystalText · CloseButton
 │    · RespecButton · BuildBar · PowderText · Tooltip(Name/Desc/Cost)
 │    · OutgameLevelBar(Fill/LevelText/XpText)
 └─ MapSelectRoot  [activeSelf=false — Btn_플레이가 여는 맵 선택 패널, Phase 2]
      Title · CardContainer(HorizontalLayoutGroup) · CardTemplate(비활성 원본:
      Frame/Bg/Thumb/Name 자식) · StartButton · BackButton
Controllers   ← TitleController + SkillTreeUI + MapSelectUI
```

**핵심 배선:**
- **`Controllers`** = 씬의 로직 허브. `TitleController`(메뉴 버튼), `SkillTreeUI`(SkillTreeRoot 패널), `MapSelectUI`(MapSelectRoot 패널)가 여기 함께 붙어 있음. **새 화면 컨트롤러(캐릭터 선택)도 여기 붙이는 게 일관적.**
- **`SkillTreeRoot`** = "버튼으로 토글하는 전체화면 패널"의 **모범 사례**. 기본 비활성, `Controllers`의 컨트롤러가 On/Off. → 캐릭터 선택 화면을 이 패턴으로 복제(Canvas 아래 형제 패널 + Controllers에 컨트롤러).
- **`MapSelectRoot`**(Phase 2) = 맵 선택 패널. `Btn_플레이`→`TitleController.Play()`→`MapSelectUI.Open()`. 카드는 `CardTemplate`(자식 Frame/Bg/Thumb/Name)을 맵 수만큼 런타임 복제. 카드 클릭=선택(Frame 하이라이트), **`StartButton`이 확인 단계** — `RunConfig.Map=선택맵` 후 `SampleScene` 로드. `maps[]`(SerializeField)에 MapDefinition 드래그로 로스터 확장(현재 `Map_BlueberryField` 1장).
- `Btn_컬렉션`·`Btn_설정`은 현재 리스너 미연결(향후 자리).

---

## SampleScene.unity (인게임)

```
Main Camera
Global Light 2D
Player  [tag=Player]   ← SpriteRenderer · BoxCollider2D · Animator
                          + PlayerHealth · PlayerSkills · PlayerExperience · PlayerPassives
EnemySpawner           ← EnemySpawner (fallbackMap=Map_BlueberryField, scaling=ScalingTable)
GameManager            ← GameManager + MetaRunApplier + RunBootstrap(defaultMap=Map_BlueberryField)
Canvas                 ← LevelUpUI · EvolutionTreeUI · DamageMeterUI  (+Canvas/Scaler/Raycaster)
 ├─ LevelUpPanel/Dialog
 ├─ HUD                ← HUDController
 │    StageText · HealthPanel · LevelText · PassiveSlot_0~3 · ExpBar
 │    · ActiveSlot_0~3 · ExpLevelText · StageBanner · BuffSlot_0~2(비활성)
 ├─ EvolutionPanel/Window          [activeSelf=false]
 └─ DamageMeterPanel               [activeSelf=false]
        Dialog · EarnedEssenceText · ReturnToTitleButton
EventSystem
Background
```

**핵심 배선:**
- **`Player`** = 캐릭터 정체성이 **단일 오브젝트에 전부** 모여 있음. SpriteRenderer(스프라이트) + Animator(컨트롤러) + PlayerSkills/PlayerPassives(스킬·패시브) + PlayerHealth/PlayerExperience(스탯). → **캐릭터 스왑은 이 오브젝트 하나를 갈아끼우거나 재구성하는 문제.**
- **`GameManager`** = GameManager + **MetaRunApplier** + **RunBootstrap**(Phase 1 신규, 형제로 붙음). RunBootstrap.Awake가 판 시작 시 선택된 맵(`RunConfig.Map`, 없으면 `defaultMap`)을 씬에 적용 — GameManager.stageTable 주입·EnemySpawner.ActiveMap 세팅·Background 스프라이트 교체·BGM 재생. **EnemySpawner.Start(물량 세팅) 전에 도는 Awake라 순서 안전.**
- `GameManager` SerializeField: `stageTable`(RunBootstrap이 맵값으로 덮음, 기본맵이면 동일), `heartPickupPrefab`, `essencePickupPrefab`, `stageBreakDuration`.
- **`Canvas` 루트에 UI 싱글톤 3개**(LevelUpUI/EvolutionTreeUI/DamageMeterUI)가 컴포넌트로 직접 붙음. 패널 오브젝트(LevelUpPanel/EvolutionPanel/DamageMeterPanel)는 각 UI가 제어하는 뷰.
- `Background` = 맵 배경(단일 오브젝트). RunBootstrap이 `GameObject.Find("Background")`로 찾아 스프라이트 교체 → **맵 스왑됨.**
- **BGM = 신규(Phase 1)**: `MapDefinition.bgm`(AudioClip) 있으면 RunBootstrap이 런타임 AudioSource를 GameManager에 추가해 루프 재생. 기본맵은 bgm=null → 무음(현행 유지). SfxPlayer/ObjectPool 효과음은 그대로.
- **적 로스터·스폰 파라미터는 EnemySpawner가 아니라 `MapDefinition`이 소유**(Phase 1). EnemySpawner는 `ActiveMap`(RunBootstrap이 세팅) 또는 `fallbackMap`(씬 단독 실행용, =Map_BlueberryField)에서 7종 프리팹·bossStage·spawnInterval·defaultSpawnCount를 읽는다.

---

## 리팩토링 스왑 포인트 요약 (Phase별 손댈 씬 지점)

| 대상 | 씬 지점 |
|---|---|
| **RunBootstrap** 부착 | SampleScene `GameManager` 오브젝트 (MetaRunApplier 형제) |
| **캐릭터 스왑** | SampleScene `Player`: SpriteRenderer·Animator·PlayerSkills/Passives 구성 (RunBootstrap이 CharacterDefinition으로 적용) |
| **맵 스왑** | ✅ Phase 1 완료 — `MapDefinition` SO 하나가 배경·BGM·stageTable·적 로스터·스폰파라미터 소유. RunBootstrap이 `RunConfig.Map`(선택 화면이 세팅)을 씬에 적용. 새 맵 = 새 MapDefinition 에셋 만들어 RunConfig에 넣기만 하면 됨 |
| **맵 선택 화면** | ✅ Phase 2 완료 — Title `Canvas/MapSelectRoot` + `Controllers/MapSelectUI`. `Btn_플레이`가 엶 |
| **캐릭터 선택 화면** | Title `Canvas` 아래 신규 패널(SkillTreeRoot/MapSelectRoot 패턴 복제) + `Controllers`에 컨트롤러 스크립트 |

## "X를 씬에 추가하려면 어디"

| 하려는 것 | 씬 작업 |
|---|---|
| 판 시작 시 도는 신규 로직 | `GameManager`에 컴포넌트 추가(MetaRunApplier 옆). Awake/Start 순서 주의 |
| 새 전체화면 UI(선택/컬렉션 등) | Title `Canvas` 아래 비활성 패널 + `Controllers`에 컨트롤러 + 여는 버튼 |
| 새 HUD 요소 | SampleScene `Canvas/HUD` 아래. `HUDController`가 폴링 |
| 새 인게임 매니저 | SampleScene 루트에 신규 오브젝트 (GameManager/EnemySpawner 형제) |
| 새 UI 싱글톤 | `Canvas` 루트에 컴포넌트로 부착(기존 3개와 동일 방식) |
