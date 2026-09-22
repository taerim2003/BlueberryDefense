# SCENE_MAP.md — 씬 구조 지도

> **AI(MCP)용 씬 지도.** ARCHITECTURE.md가 코드 지도라면, 이건 씬에 실제로 배치된 오브젝트·컴포넌트·핵심 배선 지도다.
> 매 세션이 콜드 스타트 — "어디에 뭐가 붙어 있나"를 미리 알려 MCP 왕복을 줄인다.
>
> **적는 것**: 매니저/싱글톤 오브젝트 위치, load-bearing 스크립트, Canvas/UI 루트, "새 오브젝트 어디 붙이나".
> **안 적는 것**(MCP 쿼리로 즉시 확인): 좌표·스케일·색상, 개별 위젯, SerializeField 수치, 프리팹 내부, 밸런스값(→ 에셋과 `BalanceConstants.cs`).
>
> **유지 원칙**: 오브젝트 추가/이동/스크립트 재배치 때만 갱신. 씬과 어긋나면 씬이 정답 — 이 문서를 의심할 것.
> 아래 계층은 2026-09-18에 씬 YAML에서 직접 뽑았다(깊이 2, 비활성 표시, 붙은 스크립트).

---

## 씬 목록

| 씬 | buildIndex | 역할 |
|---|---|---|
| `Assets/Scenes/Title.unity` | 0 | 메인 메뉴 · 아웃게임 스킬트리 · 맵/캐릭터 선택 · 컬렉션 · 설정 |
| `Assets/Scenes/Battle.unity` | 1 | **인게임 1판 전체** (유일한 게임 씬 — 맵·캐릭터가 늘어도 복제 안 함) |

---

## Title.unity

```
Controllers   ← TitleController · SkillTreeUI · MapSelectUI · CharacterSelectUI · CreditsUI · CursorSetter · TitleBgm
Main Camera
EventSystem
Canvas
 ├─ Background · Dim(UIHorizontalFade) · TitleImage(UIFloat)
 ├─ Btn_크레딧 (우하단 블루베리 버튼 → CreditsUI.Open)
 ├─ Layout (TitleMenuIntro)
 │    Btn_플레이 · Btn_업그레이드 · Btn_컬렉션 · Btn_설정 · Btn_종료   (각 JuicyButton + UIFloat)
 ├─ SkillTreeRoot        [inactive]  ← UITransition
 │    Wallpaper · Viewport(TreePanDrag) · TitleBox · EssenceBox · CloseButton · Tooltip · UnlockPoster(CharacterUnlockPoster)
 ├─ CreditsRoot          [inactive]  ← UITransition   (명단 = CreditsContent 프리팹 — 엔딩과 공유)
 │    Wallpaper · Scroll(진짜큰네모 판 · ScrollRect) → Viewport(RectMask2D, 판 안쪽) → Content[prefab CreditsContent](Heading_*·Names_*) · TitleBox · CloseButton
 ├─ CharacterSelectRoot  [inactive]  ← PanelSplitTransition
 │    Wallpaper · TitleBox · CardContainer · CloseButton · SkillBox · SelectButton
 └─ MapSelectRoot        [inactive]  ← PanelSplitTransition
      Wallpaper · MainPanel · CharNameLabel · ChangeCharButton · CharPortrait
[prefab] OptionsPanel · CollectionPanel   (씬 루트의 프리팹 인스턴스)
```

**핵심 배선:**
- **`Controllers`** = 씬의 로직 허브. 화면 컨트롤러가 전부 여기 붙어 있다. **새 화면 컨트롤러도 여기 붙이는 게 일관적.**
- **`SkillTreeRoot`** = "버튼으로 토글하는 전체화면 패널"의 모범 사례. 기본 비활성, `Controllers`의 컨트롤러가 On/Off.
  - **`UnlockPoster`** = 다음 해금 캐릭터를 실루엣+조건으로 띄우는 원티드 포스터. 자식 `Panel`을 껐다 켠다 —
    **컴포넌트가 붙은 오브젝트 자체는 항상 켜 둘 것**(자기를 끄면 `OnEnable` 갱신 기회를 잃는다).
- **`Layout` + `TitleMenuIntro`** = 메인 메뉴 버튼 등장 연출. 자식 순서가 곧 등장 순서.
  ⚠️ 버튼 x는 `VerticalLayoutGroup`이 정하므로 Awake·OnEnable에 읽으면 0이다 — 두 프레임 뒤에 읽고 그 사이엔 `CanvasGroup`으로 가린다.
- **맵 선택 → 판 시작**: `Btn_플레이` → `MapSelectUI.Open()`. 시작 시 `RunConfig.Map = maps[선택]` + `RunConfig.Character = CharacterSelectUI.Selected` → `SceneFade.LoadScene`으로 Battle.
  로스터 확장 = `MapSelectUI.maps[]`(현재 3장) · `CharacterSelectUI.characters[]`(현재 3장: 딸기 · 파인애플 · 포도)에 에셋 드래그.
- **컬렉션·설정**은 씬 루트의 프리팹 인스턴스(`CollectionPanel` · `OptionsPanel`)다 — 구조를 바꾸려면 프리팹을 연다.

---

## Battle.unity (인게임)

```
GameManager      ← GameManager · MetaRunApplier · RunBootstrap · ComicBurst
Main Camera
Global Light 2D
EnemySpawner     ← EnemySpawner (fallbackMap = 씬 단독 실행용)
EventSystem
Canvas           ← LevelUpUI · EvolutionTreeUI · DamageMeterUI
 ├─ HUD                   ← HUDController
 │    StageBox · HealthPanel · PassiveSlot_0~3 · ExpBar · ExpOuter · ActiveSlot_0~3 · ExpLevelBox · StageBanner · BuffBar
 ├─ XpGemLayer            ← XpGemFlight
 ├─ LevelUpPanel          [inactive] ← UITransition   (레벨업 3택 + 진화 대상 선택)
 ├─ TreasureChoicePanel   [inactive] ← UITransition   (보물 상자 갈림길 2택)
 ├─ EvolutionPanel        [inactive] ← UITransition   → Window/Node_R{0,1}T{1,2} · Arrow_R{0,1}
 ├─ DamageMeterPanel      [inactive] ← UITransition   (게임오버/클리어: ReturnToTitle · Retry · Upgrade 버튼)
 └─ TreasurePanel         [inactive]                  (보물 상자 보상: EssenceRain · TreasureChest · HeaderText · IconRow · ContinueText)
Background
Player  [tag=Player]   ← PlayerHealth · PlayerSkills · PlayerExperience · PlayerPassives · StormCloudAura   (자식 Hammer)
[prefab] PausePanel · OptionsPanel   (씬 루트의 프리팹 인스턴스)
[runtime] EndingSequence   (광활한 우주 어려움 클리어 시 GameManager가 Resources에서 생성 — 씬에 없음. 군집체 = Enemy_BlueberryCluster)
```

**핵심 배선:**
- **`Player`** = 캐릭터 정체성이 **단일 오브젝트에 전부** 모여 있다(스프라이트·애니메이터·스킬·패시브·체력·경험치). 캐릭터 스왑은 이 오브젝트를 재구성하는 문제.
  - 시작 스킬·기본 체력은 `PlayerSkills.Awake`/`PlayerHealth.Awake`가 `RunConfig.Character`를 직접 읽고(null이면 프리팹값), 외형은 `RunBootstrap`이 얹는다.
  - `PlayerSkills`에 `Prog_*`(스킬 기본 수치·레벨업 커브)와 `evolutions[]`(`Evo_*` 44칸), 아이콘 배열이 배선돼 있다.
- **`GameManager`** = GameManager + MetaRunApplier(스킬트리 보너스 적용) + **RunBootstrap**. RunBootstrap.Awake가 판 시작 시 선택된 맵(`RunConfig.Map`, 없으면 기본 맵)을 적용 —
  stageTable 주입 · `EnemySpawner.ActiveMap` · `Background` 스프라이트 교체(`GameObject.Find("Background")`) · BGM · 캐릭터 외형. `EnemySpawner.Start`보다 먼저 돌아 순서 안전.
- **`RunBootstrap`이 판 시작 시 씬 좌표를 손댄다** — `fieldScale`(카메라 ortho + 플레이어·스포너 좌표에 곱) / `cameraYLift`(카메라와 `Background`를 같이 위로).
  ⚠️ 런타임 적용이라 씬 파일은 안 바뀐다 — 에디터에서 열면 항상 기본 맵 좌표로 보인다(정상).
- **적 로스터·스폰 파라미터는 `MapDefinition`이 소유**한다. 맵마다 적 프리팹 칸을 비워 둘 수 있다 — null이면 그 맵에 안 나온다.
- **`Canvas` 루트에 UI 싱글톤 3개**(LevelUpUI/EvolutionTreeUI/DamageMeterUI). 패널 오브젝트는 각 UI가 제어하는 뷰.
  - `LevelUpUI`가 레벨업 3택 · 갈림길(`TreasureChoicePanel`) · 보상(`TreasurePanel`)을 모두 소유한다. 보상 아이콘은 `IconRow`에 **런타임 생성**.
  - ⚠️ **`TreasurePanel`은 Canvas의 맨 뒤 형제여야 한다** — uGUI는 형제 순서가 그리기 순서라, 앞으로 옮기면 HUD에 가린다.
  - 🔴 **패널을 씬에서 다시 만들면 인스펙터 참조가 통째로 끊긴다.** 진화 노드의 `Button`까지 사라지면 `EvolutionTreeUI.Awake`가 경고를 찍고 그 칸을 건너뛴다 — **콘솔 경고부터 볼 것.**
- **`XpGemLayer`** = 경험치 보석 연출. `HUD` 바로 다음 형제라 바 위·모달 아래로 그려진다. **XP는 보석 도착 시점에 적립**(`XpGemFlight.TrySpawn` 실패 시에만 즉시).

---

## "X를 씬에 추가하려면 어디"

| 하려는 것 | 씬 작업 |
|---|---|
| 판 시작 시 도는 신규 로직 | Battle `GameManager`에 컴포넌트 추가(MetaRunApplier 옆). Awake/Start 순서 주의 |
| 새 전체화면 UI(타이틀) | Title `Canvas` 아래 비활성 패널 + `Controllers`에 컨트롤러 + 여는 버튼 |
| 새 HUD 요소 | Battle `Canvas/HUD` 아래. `HUDController`가 폴링 |
| 새 인게임 매니저 | Battle 루트에 신규 오브젝트 (GameManager/EnemySpawner 형제) |
| 새 UI 싱글톤·모달 | `Canvas` 루트에 컴포넌트로 부착(기존 3개와 동일). 키보드 이동은 `UIFocusGroup` 짝 규칙(ARCHITECTURE) |
| 새 맵 / 새 캐릭터 | 에셋 하나 만들어 Title `MapSelectUI.maps[]` / `CharacterSelectUI.characters[]`에 추가 (씬 구조 변경 없음) |
