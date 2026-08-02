# SCENE_MAP.md — 씬 구조 지도

> **AI(MCP)용 씬 지도.** ARCHITECTURE.md가 코드 지도라면, 이건 씬에 실제로 배치된 오브젝트·컴포넌트·핵심 배선 지도다.
> Claude가 MCP로 씬을 조작하므로 매 세션이 콜드 스타트 — 이 문서가 "어디에 뭐가 붙어 있나"를 미리 알려 MCP 왕복을 줄인다.
>
> **적는 것**: 매니저/싱글톤 오브젝트 위치, 오브젝트에 붙은 load-bearing 스크립트, Canvas/UI 루트, "새 오브젝트 어디 붙이나".
> **안 적는 것**(MCP 쿼리로 즉시 확인): 좌표·스케일·색상, 개별 위젯 나열, SerializeField 수치, 프리팹 내부, 밸런스값(→ `StageTable`·`EnemyDefinition` 등 에셋과 `BalanceConstants.cs`).
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
 ├─ Background · TitleImage
 ├─ Btn_플레이 · Btn_업그레이드 · Btn_컬렉션 · Btn_설정 · Btn_종료   (각 +Text 자식)
 ├─ SkillTreeRoot  [activeSelf=false — 버튼으로 여는 전체화면 패널]
 │    Viewport/Content · Title · EssenceText · CloseButton · Tooltip(Name/Desc/Cost)
 │    · UnlockPoster(우측 하단, 다음 해금 캐릭터 실루엣+조건)   ← 세션26
 ├─ MapSelectRoot  [activeSelf=false — Btn_플레이가 여는 맵 선택 패널, Phase 2]
 │    Title · CardContainer(HorizontalLayoutGroup) · CardTemplate(비활성 원본:
 │    Frame/Bg/Thumb/Name 자식) · StartButton · BackButton
 │    · CharNameLabel(현재 캐릭터 이름) · ChangeCharButton(캐릭터 변경 팝업 열기)   ← Phase 4
 └─ CharacterSelectRoot  [activeSelf=false — ChangeCharButton이 여는 캐릭터 팝업, Phase 4]
      Title · CardContainer(HorizontalLayoutGroup) · CardTemplate(비활성 원본:
      Frame/Bg/Thumb/Name 자식) · BackButton   (StartButton 없음 = 카드 클릭 즉시 선택·닫힘)
Controllers   ← TitleController + SkillTreeUI + MapSelectUI + CharacterSelectUI
```

**핵심 배선:**
- **`Controllers`** = 씬의 로직 허브. `TitleController`(메뉴 버튼), `SkillTreeUI`(SkillTreeRoot 패널), `MapSelectUI`(MapSelectRoot 패널), `CharacterSelectUI`(CharacterSelectRoot 팝업)가 여기 함께 붙어 있음. **새 화면 컨트롤러도 여기 붙이는 게 일관적.**
- **`SkillTreeRoot`** = "버튼으로 토글하는 전체화면 패널"의 **모범 사례**. 기본 비활성, `Controllers`의 컨트롤러가 On/Off.
  - **`UnlockPoster`**(세션26) = 다음에 열릴 캐릭터를 실루엣+조건 진행도로 띄우는 원티드 포스터. `CharacterUnlockPoster`가 붙어 있고, 자식 `Panel`을 껐다 켠다(**컴포넌트가 붙은 오브젝트 자체는 항상 켜 둘 것** — 자기를 끄면 다음에 패널이 열려도 `OnEnable`이 안 돌아 갱신 기회를 잃는다). 해금 조건이 누적 정수라 **정수를 쓰는 이 화면**에 둔다 — 캐릭터를 *고르는* 것은 `CharacterSelectRoot` 담당으로 역할이 갈린다.
- **`MapSelectRoot`**(Phase 2) = 맵 선택 패널. `Btn_플레이`→`TitleController.Play()`→`MapSelectUI.Open()`. 카드는 `CardTemplate`(자식 Frame/Bg/Thumb/Name)을 맵 수만큼 런타임 복제. 카드 클릭=선택(Frame 하이라이트), **`StartButton`이 확인 단계** — `RunConfig.Map=선택맵`+`RunConfig.Character=선택캐릭터` 후 `SampleScene` 로드. `maps[]`(SerializeField)에 MapDefinition 드래그로 로스터 확장(**현재 3장** — 블루베리 밭 / 해안가 / 광활한 밭).
- **`CharacterSelectRoot`**(Phase 4) = 맵 화면 위에 뜨는 캐릭터 선택 팝업. `MapSelectRoot`를 복제해 만듦(StartButton 제거). `MapSelectRoot/ChangeCharButton`→`CharacterSelectUI.Open()`. **카드 클릭 = 즉시 선택+팝업 닫힘**(맵과 달리 확인 단계 없음), `OnSelectionChanged`로 `MapSelectUI`가 `CharNameLabel`을 갱신. `characters[]`(SerializeField)에 CharacterDefinition 드래그로 로스터 확장(현재 `Char_Strawberry` 1종, `displayName`="딸기"). 초상화(`portrait`)는 아직 미설정 → 카드 Thumb 숨김·이름만 표시.
- `Btn_컬렉션`·`Btn_설정`은 현재 리스너 미연결(향후 자리).

---

## SampleScene.unity (인게임)

```
Main Camera
Global Light 2D
Player  [tag=Player]   ← SpriteRenderer · BoxCollider2D · Animator
                          + PlayerHealth · PlayerSkills · PlayerExperience · PlayerPassives
EnemySpawner           ← EnemySpawner (fallbackMap=Map_BlueberryField, scaling=ScalingTable)
GameManager            ← GameManager + MetaRunApplier + RunBootstrap(defaultMap=Map_BlueberryField) + ComicBurst
Canvas                 ← LevelUpUI · EvolutionTreeUI · DamageMeterUI  (+Canvas/Scaler/Raycaster)
 ├─ LevelUpPanel
 │    Dialog
 ├─ HUD                ← HUDController
 │    StageText · HealthPanel · LevelText · PassiveSlot_0~3 · ExpBar
 │    · ActiveSlot_0~3 · ExpLevelText · StageBanner · BuffSlot_0~2(비활성)
 ├─ XpGemLayer         ← XpGemFlight (전체 스트레치·빈 RectTransform, 보석은 런타임 자식으로 생성)
 ├─ EvolutionPanel/Window          [activeSelf=false]
 ├─ DamageMeterPanel               [activeSelf=false]
 │    Dialog · EarnedEssenceText · ReturnToTitleButton
 └─ TreasurePanel                  [activeSelf=false] ← 보물상자 전용(맨 뒤 형제 = 다른 UI 위에 그려짐)
        EssenceRain · TreasureChest · HeaderText · IconRow · ContinueText
EventSystem
Background
```

**핵심 배선:**
- **`Player`** = 캐릭터 정체성이 **단일 오브젝트에 전부** 모여 있음. SpriteRenderer(스프라이트) + Animator(컨트롤러) + PlayerSkills/PlayerPassives(스킬·패시브) + PlayerHealth/PlayerExperience(스탯). → **캐릭터 스왑은 이 오브젝트 하나를 갈아끼우거나 재구성하는 문제.**
  - **PlayerSkills.skillTable**(Phase 3) = `Assets/Data/SkillTable.asset` 배선(스킬 9종 기본 쿨/뎀·레벨업 배율). 미배선이어도 `SkillTable.Default`(현행값) 폴백.
  - **캐릭터 데이터(Phase 3)**: 시작스킬·기본체력은 `PlayerSkills.Awake`/`PlayerHealth.Awake`가 `RunConfig.Character`를 **직접** 읽어 반영(null이면 프리팹값=현행). 외형(스프라이트·애니메이터)은 RunBootstrap이 얹음. → 스탯은 컴포넌트가, 외형은 RunBootstrap이 담당해 Awake 순서 의존 없음.
- **`GameManager`** = GameManager + **MetaRunApplier** + **RunBootstrap**(Phase 1 신규, 형제로 붙음). RunBootstrap.Awake가 판 시작 시 선택된 맵(`RunConfig.Map`, 없으면 `defaultMap`)을 씬에 적용 — GameManager.stageTable 주입·EnemySpawner.ActiveMap 세팅·Background 스프라이트 교체·BGM 재생. **+ 선택된 캐릭터(`RunConfig.Character`)의 외형(스프라이트/애니메이터, null이면 스킵)도 적용**(Phase 3). **EnemySpawner.Start(물량 세팅) 전에 도는 Awake라 순서 안전.**
- `GameManager` SerializeField: `stageTable`(RunBootstrap이 맵값으로 덮음, 기본맵이면 동일), `heartPickupPrefab`, `essencePickupPrefab`, **`evolutionItemPrefab`**(=`Assets/Prefabs/EvolutionItemPickup.prefab`, 벽 스테이지 엘리트 드랍), `stageBreakDuration`.
- **`EvolutionPanel`은 여전히 3×3(9칸) 노드를 갖고 있지만 진화가 2루트×2티어로 바뀌어 `EvolutionTreeUI`가 런타임에 왼쪽 위 2×2(`Node_P0T1/P0T2/P1T1/P1T2` + `Arrow_P0_0`/`Arrow_P1_0`)만 켜고 나머지는 끈다.** 씬 배선은 그대로 두면 됨 — 3×3 → 2×2로 레이아웃을 정리하고 싶으면 남는 칸 5개(+화살표 4개)를 지우고 `EvolutionTreeUI.NodeIndex`/`HiddenNodes` 상수를 맞추면 된다.
- **`Canvas` 루트에 UI 싱글톤 3개**(LevelUpUI/EvolutionTreeUI/DamageMeterUI)가 컴포넌트로 직접 붙음. 패널 오브젝트(LevelUpPanel/EvolutionPanel/DamageMeterPanel)는 각 UI가 제어하는 뷰.
- **`TreasurePanel`은 레벨업 카드와 완전히 별개인 뷰**다(뱀서식 상자). 고르는 게 아니라 받는 것이라 제목/설명/버튼이 없고, **획득 아이콘만 `IconRow`에 하나씩 쌓인다**. 아이콘은 `LevelUpUI.AddTreasureIcon`이 **런타임에 생성**하므로 씬에 미리 깔아둘 것이 없다(HUD와 같은 `IconFrame` 스프라이트를 틀로 깔고 그 안에 스킬 아이콘). 전부 뜬 뒤에만 `ContinueText`가 켜지고 패널 전체를 덮는 버튼이 `interactable`이 되어 클릭으로 닫힌다.
  - 여전히 `LevelUpUI`가 소유한다(아이콘 배열·대기열·`ModalPause`를 이미 갖고 있어서 별도 싱글톤을 만들면 아이콘 13개를 새로 배선해야 함). 배선 필드: `treasurePanel`·`treasureIconRow`·`treasureContinueText`·`treasureDismissButton`·`treasureIconFrame`.
  - `treasureDecor`(=[TreasureChest, EssenceRain])는 **이 패널의 자식으로 옮겨졌다**(예전엔 LevelUpPanel 밑). `EssenceRain`엔 `TreasureRewardDecor`(정수 비 낙하·회전 + 보물상자 등장/흔들림, unscaled 시간)가 붙음. 순서(상자 먼저 활성)로 정수 비 OnEnable이 상자 트윈을 안전하게 건다.
  - ⚠️ **Canvas의 맨 뒤 형제여야 한다** — uGUI는 형제 순서가 그리기 순서라, 앞으로 옮기면 HUD에 가린다.
- **`XpGemLayer`**(신규) = 경험치 보석 연출 레이어. `HUD` 바로 다음 형제라 **바 위·모달 아래**로 그려진다. `XpGemFlight`가 `ExpBar/ExpFill`을 참조해 "현재 차 있는 끝 지점"을 목표로 잡고, 보석은 이 오브젝트의 자식으로 런타임 생성·풀링. **XP는 보석 도착 시점에 적립**(Enemy.Die는 `XpGemFlight.TrySpawn` 실패 시에만 즉시 적립).
- `Background` = 맵 배경(단일 오브젝트). RunBootstrap이 `GameObject.Find("Background")`로 찾아 스프라이트 교체 → **맵 스왑됨.**
- **BGM = 신규(Phase 1)**: `MapDefinition.bgm`(AudioClip) 있으면 RunBootstrap이 런타임 AudioSource를 GameManager에 추가해 루프 재생. 기본맵은 bgm=null → 무음(현행 유지). SfxPlayer/ObjectPool 효과음은 그대로.
- **적 로스터·스폰 파라미터는 EnemySpawner가 아니라 `MapDefinition`이 소유**(Phase 1). EnemySpawner는 `ActiveMap`(RunBootstrap이 세팅) 또는 `fallbackMap`(씬 단독 실행용, =Map_BlueberryField)에서 **적 프리팹 9종**(기본·보물·엘리트·종이비행기·UFO·방패·라이더·콩콩이·서핑)·보스·bossStage·spawnInterval·defaultSpawnCount를 읽는다. **맵마다 비워둘 수 있다** — null이면 그 적은 그 맵에 안 나온다(콩콩이·서핑은 현재 해안가에만 물려 있음).
- **`RunBootstrap`이 판 시작 시 씬 좌표를 손댄다** — `fieldScale`(카메라 ortho + 플레이어·스포너 x,y에 곱) / `cameraYLift`(**카메라와 `Background` 오브젝트를 같이** 위로 이동). ⚠️ **둘 다 런타임 적용이라 씬 파일은 안 바뀐다** — 에디터에서 씬을 열면 항상 기본 맵 기준 좌표로 보인다(정상).

---

## 리팩토링 스왑 포인트 요약 (Phase별 손댈 씬 지점)

| 대상 | 씬 지점 |
|---|---|
| **RunBootstrap** 부착 | SampleScene `GameManager` 오브젝트 (MetaRunApplier 형제) |
| **캐릭터 스왑** | ✅ Phase 3(인프라) 완료 — `CharacterDefinition` SO(시작스킬·허용풀·기본체력·외형). 스탯은 `PlayerSkills`/`PlayerHealth`가 `RunConfig.Character` 직접 읽음, 외형은 RunBootstrap. 기본 에셋 `Assets/Data/Characters/Char_Strawberry.asset`(=현행값). 새 캐릭터 = 새 CharacterDefinition 만들어 RunConfig에 넣기. **캐릭터 선택 화면은 Phase 4(미착수).** |
| **맵 스왑** | ✅ Phase 1 완료 — `MapDefinition` SO 하나가 배경·BGM·stageTable·적 로스터·스폰파라미터 소유. RunBootstrap이 `RunConfig.Map`(선택 화면이 세팅)을 씬에 적용. 새 맵 = 새 MapDefinition 에셋 만들어 RunConfig에 넣기만 하면 됨 |
| **맵 선택 화면** | ✅ Phase 2 완료 — Title `Canvas/MapSelectRoot` + `Controllers/MapSelectUI`. `Btn_플레이`가 엶 |
| **캐릭터 선택 화면** | ✅ Phase 4 완료 — Title `Canvas/CharacterSelectRoot` 팝업 + `Controllers/CharacterSelectUI`. 맵 화면 안 `ChangeCharButton`이 엶. 카드 클릭=즉시 선택. 새 캐릭터 = 새 CharacterDefinition 만들어 `CharacterSelectUI.characters[]`에 추가 |

## "X를 씬에 추가하려면 어디"

| 하려는 것 | 씬 작업 |
|---|---|
| 판 시작 시 도는 신규 로직 | `GameManager`에 컴포넌트 추가(MetaRunApplier 옆). Awake/Start 순서 주의 |
| 새 전체화면 UI(선택/컬렉션 등) | Title `Canvas` 아래 비활성 패널 + `Controllers`에 컨트롤러 + 여는 버튼 |
| 새 HUD 요소 | SampleScene `Canvas/HUD` 아래. `HUDController`가 폴링 |
| 새 인게임 매니저 | SampleScene 루트에 신규 오브젝트 (GameManager/EnemySpawner 형제) |
| 새 UI 싱글톤 | `Canvas` 루트에 컴포넌트로 부착(기존 3개와 동일 방식) |
