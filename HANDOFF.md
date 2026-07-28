# HANDOFF.md — 세션 인계 문서

<!--
📋 유지 가이드 — 이 문서는 "다음 세션이 지금 뭘 해야 하는가"만 담는다. 길어지지 않게 유지할 것.
- 담을 것: 현재 빌드 상태, 미해결 이슈, 다음 할 일. 그것뿐.
- 지울 것: 완료·검증된 피쳐/버그/이슈는 즉시 삭제 (git 히스토리에 이미 남음).
- 세션 로그를 누적하지 말 것. 매 세션 새로 쓰는 게 아니라, 끝난 항목은 지우고 새 항목만 갱신.
- 게임 스펙(스킬 수치·시스템 규칙 등)은 여기 말고 GDD.md에. 여기엔 "무엇을 확인/수정해야 하는지"만.
- 목표: 항상 한 화면 안에 읽힐 것.
-->

## 🧭 North Star
> **"스킬을 강화해 적들을 시원하게 쓸어버리자."**
> QWER 액티브 4 + 패시브 4를 진화 트리로 강화해 빌드를 만드는 벙커 디펜스. 상세 스펙은 `GDD.md` §4 참고.

---

## 현재 상태 (2026-07-28, 세션 13)

**빌드**: 컴파일 에러 없음(경고 CS0618 기존 잔존, 무해). Title 씬 저장 완료. StageTable·프리팹·EnemyDef 반영.
**세션13 = 스테이지 특징 강화(벽 스테이지) + UFO 리워크 + 라이더 신규 적 + 11+ 밸런싱.** **적 관련 전부 실플레이 확인 대기.**

### 루프 설계 (확정)
> 스킬트리는 **자원 1개·되돌리기 불가**의 영구 성장. **승천 = 슬더식 난이도 등급**(맵을 난이도별로 다시 도전).
> "지금 빌드론 못 깸 → 정수 벌어 스킬 해금·강화 → 클리어 → 다음 승천" 반복.

### 이번 세션 변경 (요약)
- **🛸 UFO 리워크 = 수송선(carrier)**: 좌진 행진 유닛 → **화면 위에서 하강→호버하며 부대 투하→다시 상승 퇴장**. `Enemy.cs`에 `isCarrier` 모드 추가(하강/호버/상승 상태기계, Y좌표는 `Camera.main` 기준 런타임 계산). **투하 전 격추하면 부대 안 나옴**(대공 보상). 투하물=기본 블루베리 3~4마리, 캐리어가 받은 스테이지 배율을 물려받음(`ApplyStageMultipliers` 기억). UFO HP 40→**45**(요격 대상이라 낮게), 하강속도 3. `EnemySpawner`의 `currentStage>=11` UFO 게이트 제거(이제 `ufoChance` 데이터가 배치 제어).
- **🏇 라이더 블루베리 신규 적**: 종이비행기의 지상판 — 빠른 지상 돌진·저HP. `EnemyDef_Rider`(속도4.5/피해10/HP28/xp7), `Enemy_RiderBlueberry.prefab`(Enemy_Blueberry 복제), 3프레임 워크 애니(`RiderBlueberry_Walk.anim`+`.controller`). `StageData.riderChance`+`MapDefinition.riderEnemyPrefab`+스포너 분기 추가. 라이더 스프라이트는 PPU 100→**32**·Single·중앙피벗으로 재임포트(다른 적과 정렬).
- **🧱 벽 스테이지 + 11+ 밸런싱**(StageTable): **9=방패 벽**(shield 1.0, 관통/광역 시험), **13=UFO 벽**(ufo 1.0, 대공 순간화력 시험) 신규. 7=종이비행기 벽 유지. 라이더는 5·6·8·10·11·14에 뿌림. 11~15 HP/피해/물량 상향(예: 11 hp3.8→4.3, 15 hp7.5→8.6, spawn 90→102).

> **벽 스테이지 설계 철학**: BTD6식 — 난이도가 평탄히 오르는 게 아니라 **특정 능력을 크게 시험하는 near-pure 스테이지**를 중간중간 배치. 7=대공/9=관통·광역/13=대공순간화력. 새 벽 추가 시 이 언어를 따를 것.

> ⚠️ **Windows 빌드 세이브는 파일이 아니라 레지스트리** — `HKCU\Software\DefaultCompany\BlueberryDefense`. `AppData/LocalLow`엔 로그만 있음. 수동 삭제: `reg delete "HKCU\Software\DefaultCompany\BlueberryDefense" /f` (게임 종료 상태에서).

---

## 🧩 콘텐츠 확장 = 순수 데이터
- **밸런스 편집**: `Window > Blueberry Defense > Balance Dashboard` 또는 인스펙터 — `Prog_*`·`Passive_*`·`EnemyDefinition`·`MapDefinition`·`StageTable`·`ScalingTable`·`CharacterDefinition`·**`AscensionTable`**. 전역 상수는 `BalanceConstants.cs`.
- **스킬트리 편집**: `Blueberry Defense > Skill Tree Editor` — 노드는 id/이름/**타입**/**등급(비용 자동표시)**/(해금·강화면)스킬/설명/선행. 백업본 `MainSkillTree_Backup.asset` 있음.
- **새 강화 노드 추가법**: 에디터에서 노드 만들고, 그 **id**를 `SkillEffects.Compute`의 case에 추가(효과는 id 기준 레지스트리 — 에셋 `effect` 필드는 신뢰 안 함).
- **새 맵/캐릭터**: `MapDefinition`/`CharacterDefinition` 만들어 각 Select UI 배열에 드래그.
- **치트**: `Window > Blueberry Defense > Cheat Window` — 정수 지급, 스킬트리 초기화, **승천 등급 설정·해금 최대로**.

---

## ⚠️ 미해결 이슈 / 주의
- **실플레이 확인 대기(세션13 적/스테이지)**:
  - **UFO 수송선** — 11+/13스테이지에서 화면 위에서 내려와 호버·투하 후 상승 퇴장하는지. 투하물이 지면에 착지 후 좌진하는지. 투하 전 격추 시 부대가 안 나오는지. 등장 x/호버 높이가 화면 안에 자연스러운지(`carrierHoverY`=화면 상단40%, `carrierDescendSpeed`=3).
  - **라이더** — 5스테이지부터 등장, 3프레임 워크 애니 재생·크기·정렬이 다른 적과 맞는지(스프라이트 60×31px, PPU32, scale1.5).
  - **벽 스테이지** — 9(방패만)/13(UFO만)이 실제로 벽처럼 느껴지는지, 물량(9=28·13=15UFO)·간격이 과하거나 부족한지.
  - **11+ 밸런싱** — 여전히 쉬운지/과한지. 수치는 `Balance Dashboard` 또는 StageTable에서 조정.
- **UFO 격추 난이도 잔여 튜닝**: 13스테이지(×5.6)면 UFO 45HP→약 252HP를 ~1.3초 하강 중 녹여야 격추 보상 성립. 대공 빌드가 실제로 가능한지 확인, 안 되면 HP↓ 또는 `carrierDescendSpeed`↓.
- **시작 캐릭터 풀 축소 필요**: 루프가 성립하려면 `Char_Strawberry.allowedPool`을 1~2개로 줄여야 함(지금은 넓어서 트리 해금 없이도 스킬이 나옴).
- **`ESC 요약`의 "전체 피해량"은 스킬트리 보너스까지 포함한 합계** — 힘 패시브 단독 기여분이 아님.
- **씬 직접 조작 OK**(사용자 승인): MCP로 씬 열기/수정/저장 자유.
- **에디터 인스펙터 예외(무해)**: 플레이 진입 시 `ObjectPreview.DrawPreview ... Image destroyed`.
- **노션 토큰 노출**(기존): 폐기·재발급 권장.
- (기존) **사운드 볼륨 이중곱** — 우선순위 낮음.

---

## 🎨 스킬 이펙트 픽셀 크기 컨벤션
스킬 이펙트 스프라이트는 배경과 픽셀 밀도가 안 맞아, **그림은 작게 그리고 엔진에서 1.5배로 표시**(플레이어 딸기 프리팹 `localScale=1.5`에 맞춤).
- 새 이펙트 프리팹은 기존값에 곱하지 말고 `localScale`을 **직접 (1.5,1.5,1.5)로** 세팅.
- PPU **32**, Main Camera Orthographic Size 5(화면 세로 = 10유닛).
- 공식: `원본 그림 px = (목표 월드 유닛 × 32) ÷ 1.5` (예: 화면 세로 꽉 = 64 × 213px)

---

## 다음 할 일
1. **세션13 적/스테이지 실플레이 확인** (위 목록) — 특히 UFO 수송선 궤적·투하, 벽 스테이지 물량 감각, 11+ 난이도.
2. **실플레이로 루프 검증** — 승천 1 클리어 → 승천 2 해금 → 못 깸 → 트리 해금 → 재도전이 실제로 굴러가는지.
3. **시작 캐릭터 풀 축소** (위 이슈).
4. **밸런스 조율** — 벽 스테이지(9/13) 수치, 11+ 곡선, 라이더 뿌리는 확률, 승천 배율.
5. 트리 UI 레이아웃 손보기(죽은 `BuildBar`·`OutgameLevelBar`는 세션13에 제거 완료).

## 📌 다음 세션 예정: 승천별 스테이지 수 확장
> **목표**: 총 스테이지 수(=보스가 나오는 최종 스테이지)가 승천 레벨마다 늘어난다. **승천1=20, 승천2=25, 승천3=30**(+5/레벨). 스테이지당 물량이 아니라 **판 길이(스테이지 개수) 자체**가 늘어나는 것. (현재는 15 고정.)
>
> **손댈 접점**:
> 1. `AscensionTier`에 `finalStage`(또는 stageCount) 필드 추가 — 승천1=20/2=25/3=30. ([AscensionTable.cs](Assets/Scripts/AscensionTable.cs))
> 2. `GameManager.FinalStage` **const(=15) 제거** → `AscensionTable.Get(RunConfig.AscensionLevel).finalStage` 런타임 참조. GameClear 판정 2곳([GameManager.cs:73](Assets/Scripts/GameManager.cs#L73)·L86)이 이걸 봄.
> 3. **보스 스테이지 이동**: `EnemySpawner`의 `isBossStage = currentStage == map.bossStage` — 보스가 승천 최종 스테이지에 나오도록 `bossStage`(현재 `MapDefinition`=15 고정)를 승천 최종값으로. RunBootstrap이 판 시작 시 덮거나 EnemySpawner가 직접 승천값 참조.
> 4. **StageTable 데이터 확장**: 현재 15개 엔트리, `GetStage`가 16+를 마지막(15=보스 config)으로 클램프 → **스테이지 16~최종−1의 실제 데이터 필요.** 선택: (a) StageTable에 엔트리 추가(수작업), (b) 15 초과는 non-boss 마지막(14) 기반으로 `ScalingTable` 스텝을 계속 곱해 절차 생성. **클램프가 보스(15) config를 물면 안 됨.**
> 5. HUD "Stage X/N"의 N도 승천 최종값 참조.
> 6. 확장된 판에 벽 스테이지(방패/UFO 등) 재배치 고려.

## 보류 중인 결정 / 백로그
- UI 시스템: uGUI + TextMeshPro 확정 (한글 폰트 = `Galmuri11 SDF`)
- 배포(itch.io 등)는 아직 진행 안 함
- 아웃게임 컬렉션(캐릭터/스킬 해금) = 스킬트리 이후 Phase
- 딸기 전용 초상화 도트 미완(`Player.png` 임시) — 그리면 `Char_Strawberry.portrait`만 교체
