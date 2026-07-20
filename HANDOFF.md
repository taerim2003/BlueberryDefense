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

## 현재 상태 (2026-07-20, 세션 7)

**빌드**: 컴파일 에러 없음(경고 CS0618 기존 잔존, 무해). SampleScene 저장 완료. **세션7 = 리팩토링 Phase 3 완료(캐릭터/스킬 데이터화, 인프라)** — 아래 로드맵 §3 참고. 세션4의 실플레이 피드백 13건은 여전히 재검증 대기(맨 아래 목록).

### 세션7 — Phase 3 (캐릭터/스킬 데이터화)
- **신규 SO**: `SkillTable`(스킬 9종 기본쿨·기본뎀·레벨업 배율, `Assets/Data/SkillTable.asset`) / `CharacterDefinition`(시작스킬·허용풀·기본체력·외형, 기본 에셋 `Assets/Data/Characters/Char_Strawberry.asset`) / `BalanceConstants`(전역 상수 집결, 코드).
- **배선**: SampleScene `Player.PlayerSkills.skillTable` = SkillTable.asset. Char_Strawberry는 아직 런타임 미참조(캐릭터 선택 화면 = Phase 4 대기) — 현재 `RunConfig.Character`=null이라 프리팹 기본값(현행)으로 동작.
- **데이터화**: `PlayerSkills.Awake`가 `RunConfig.Character?.startingSkill`, `PlayerHealth.Awake`가 `baseHealth` 직접 읽음(순서 안전). 외형은 RunBootstrap이 적용. LevelUpUI 신규스킬·패시브 후보를 `allowedActivePool`/`allowedPassivePool`로 필터(빈 풀=전체=현행).
- **검증(플레이모드 스모크)**: 기본(RunConfig.Character=null) 진입 시 hp=110(기본100+메타10), BasicAttack L3(메타 화살시작) cd1.43(=1.5×0.95)·dmg16 — 리팩토링 전과 정확히 동일, 게임플레이 예외 0. SkillTable 에셋 9종 값이 기존 하드코딩과 전부 일치 확인.
- **재판정**: 3번째 슬롯 개별강화·되감기 값은 스케줄에 박혀 있어 SO 추출 안 함(코드 잔류, Tier B). BALANCE_MAP §B 참고.

### 세션6 — Phase 2 (맵 선택 화면)
- 흐름: `Btn_플레이` → `MapSelectUI.Open()`(패널) → 맵 카드 클릭=**선택(Frame 하이라이트)** → 하단 **`시작` 버튼(확인 단계)** → `RunConfig.Map=선택맵` + `SampleScene` 로드. 뒤로 버튼=닫기.
- 신규: `MapSelectUI.cs`(Controllers 부착, 카드는 `CardTemplate` 런타임 복제). `MapDefinition`에 `displayName`/`description` 필드 추가. `Map_BlueberryField.displayName="블루베리 밭"`.
- 수정: `TitleController.Play()`가 씬 직접 로드 대신 패널을 엶(`mapSelect` 참조 추가, 미사용 `using SceneManagement` 제거).
- 로스터 확장 = `MapSelectUI.maps[]`에 MapDefinition 드래그(현재 1장). 씬 배선은 `SCENE_MAP.md` Title 섹션.
- **검증(플레이모드 스모크)**: Play→패널 오픈, 카드1장(이름 "블루베리 밭"), 시작 활성/selectedIndex=0, Confirm→`RunConfig.Map=블루베리밭`+씬로드. SampleScene+BlueberryField 동작은 Phase1에서 검증됨.

### 세션5 이전 수정 (플레이 피드백 반영, 재검증 대기)
- **2스테이지 시작 버그 수정**: 첫 프레임 `GameManager.Update`가 `EnemySpawner`보다 먼저 돌면 `SpawnTarget`=0→`StageSpawnComplete` 참→즉시 스테이지 넘어감. `EnemySpawner.Start()`에서 물량 카운트 선-세팅(`BeginStageCount`)해 방지.
- **스킬 입력**: `wasPressedThisFrame`→`isPressed` (꾹 눌러도 쿨마다 재발동).
- **되감기 레벨업**: 매 레벨 쿨감+되감기 동시 → **홀수 레벨 쿨-0.15 / 짝수 레벨 되감기+0.15 교차**. 미리보기 텍스트도 맞춤.
- **밸런스**: 기본공격 쿨 1→1.5s. 스나이핑 데미지 9→18(2배)·쿨 6→5s. 데미지숫자 세로간격 0.42→0.62.
- **호밍 미사일**(`HomingMissile`/`FireHoming`): ①비행 유닛 우선 타격(`AcquireTarget` 2-tier) ②`GrowthStacks/15` 만큼 발사수 +1 ③명중 시 `hitVfxPrefab`(=`Impact_Sparks_01`, 프리팹 배선 완료).
- **산탄 아이콘 하이라이트**: uGUI `Outline`(이미지 4방향 복제, 덧씌운 느낌) 폐기 → 아이콘 뒤 별도 노란 프레임을 **런타임 생성**(`HUDController.CreateShotgunFrame`, `ActiveSlot.shotgunFrame`). 씬 배선 불필요.
- **아웃게임 경제(스킬트리)**: `root_hp`(체력회복) 드랍 레벨당 +1%→**+4%** 그리고 **maxLevel 1**(단일 개방, 4% 고정). `reroll_1`(리롤 해금) maxLevel 5→**1**. (에셋 `Assets/SkillTree/MainSkillTree.asset`)
- **Title 레이아웃**: 통화 텍스트(Essence/Crystal/Powder) 44px씩 아래로(EssenceText 화면 밖 잘림 해결). RespecButton·BuildBar를 레벨바 위(y 62)로 올려 겹침 해소. 씬 저장 완료.
- **ESC 일시정지 요약 강화**: 기존 `PauseMenu`(자동 부트스트랩, ESC)에 **레벨업 누적 증가치** 추가 — `PlayerSkills.DescribeLevelUpGains()`가 1렙 기본값 대비 피해·쿨·투사체속도·관통·투사체수·발동확률·지속·크기·되감기·호밍성장을 뽑아 스킬별로 진화 티어 제목 위에 표시. (한때 별도 `SkillDetailPanel`을 만들었다가 ESC 충돌로 제거·기존 PauseMenu에 통합)

**참고**: 세션3분(데미지숫자 재작성·보물상자 5초텀·스나이핑 재작업·보스 위치/팝콘·아웃게임 레벨바·되감기 신규)은 이번 세션 실플레이로 사용자 확인 완료. 기존 시스템 상세는 git.

---

## ★★ 다음 대작업 — 리팩토링 로드맵 (여러 세션)

> **목표**: 캐릭터/맵 선택 화면 + 유니티 내 밸런스 편집을 대비해, "단일 캐릭터·단일 맵·코드 하드코딩 수치"를 **데이터(SO)로 분리**한다. 상세 근거는 이 세션 대화 참고.

**확정 결정** (재논의 불필요):
- 새 캐릭터 = **고유 신규 스킬**을 Q에 갖고 시작 (스킬 구현은 오늘처럼 enum+Fire 추가, 캐릭터별 `allowedSkillPool`로 게이팅).
- 새 맵 = **적 로스터·기믹까지** 다름.
- **씬 복제 안 함** — 단일 게임 씬 + SO 스왑. 기존 `MetaBonuses`/`MetaRunApplier` 패턴 확장.
- **PlayerSkills 플러그인화는 지금 안 함**(과설계). 단, 수치 SO 추출로 파일이 자연히 줄어듦.

**핵심 신규 조각**:
- `RunConfig`(static, Phase 1 생성): 선택된 CharacterId+MapId. 씬 로드로 초기화 **금지**(선택값 유지). `MetaBonuses` 등은 판마다 리셋. 문서: `SCENE_MAP.md`(씬 배선)·`BALANCE_MAP.md`(수치→SO 매핑).
- `RunBootstrap`(MonoBehaviour): 판 시작 시 Character/MapDefinition을 씬에 적용(MetaRunApplier 형제, 순서 정렬 주의).
- SO: `CharacterDefinition`(startingSkill·allowedSkillPool·패시브셋·기본스탯·스프라이트), `MapDefinition`(배경·BGM·StageTable·enemyRoster·기믹), `SkillDefinition`(기본쿨/뎀·레벨증가·진화수치), `EnemyDefinition`, `ScalingTable`(후반 배열·XP커브).
- 문서: `SCENE_MAP.md`(씬 오브젝트·배선 지도, MCP 정찰), `BALANCE_MAP.md`(하드코딩 수치→목표 SO 매핑표).

**Phase 순서** (맵 먼저 = 결합 얕음. 매 Phase 완료조건에 "기본값 선택 시 현재와 동일" + "SCENE_MAP 갱신"):
0. **기반&정찰** ✅ **완료(2026-07-20 세션4)**: `SCENE_MAP.md`·`BALANCE_MAP.md` 작성, CLAUDE.md §7 갱신.
1. **맵/적 데이터** ✅ **완료(2026-07-20 세션5)**: `RunConfig`(static, SO직접참조) + `MapDefinition`(배경·BGM·stageTable·적로스터·스폰파라미터) + `RunBootstrap`(GameManager 형제) + `ScalingTable`(HP/이속스텝·XP커브) + `EnemyDefinition` 7종(적 스탯 완전이관). 기본맵 `Map_BlueberryField` 선택 시 현재와 동일 검증 완료(플레이모드 스모크: stage1, 물량스폰 정상, Blueberry spd2/hp14, XP18, 무예외). 신규 파일: RunConfig/ScalingTable/EnemyDefinition/MapDefinition/RunBootstrap.cs, `Assets/Data/*`.
2. **맵 선택 화면** ✅ **완료(2026-07-20 세션6)**: Title `Canvas/MapSelectRoot` 패널 + `Controllers/MapSelectUI`(카드=`CardTemplate` 런타임 복제, 클릭=선택·`시작`=확인). `Btn_플레이`→`Play()`→`Open()`. 선택 시 `RunConfig.Map=선택맵`→`SampleScene`. 로스터는 `maps[]`(현재 1장). 신규: MapSelectUI.cs, MapDefinition.displayName/description.
3. **캐릭터/스킬 데이터** ✅ **완료(2026-07-20 세션7)**: `SkillTable`(스킬 기본쿨·뎀·레벨업 배율) + `CharacterDefinition`(시작스킬·허용풀·기본체력·외형) + `BalanceConstants`(전역 상수). 시작스킬/기본체력=플레이어 컴포넌트가 `RunConfig.Character` 직접 읽기, 외형=RunBootstrap, LevelUpUI 후보=허용풀 필터. 기본 캐릭터(null) 동일 검증 완료. 신규: SkillTable/CharacterDefinition/BalanceConstants.cs, `Assets/Data/SkillTable.asset`·`Assets/Data/Characters/Char_Strawberry.asset`. 3번째슬롯·되감기 값은 코드 잔류(Tier B 재판정).
4. **캐릭터 선택 화면**: Phase 2(맵 선택)와 대칭. Title `Canvas`에 신규 패널 + `Controllers`에 컨트롤러 → 선택 시 `RunConfig.Character` 세팅. **2번째 캐릭터 실 에셋은 아직 없음**(Char_Strawberry 1종) — 새 CharacterDefinition 만들어 로스터에 추가.
5. **Balance Dashboard**(선택): 위 SO들을 탭으로 묶는 커스텀 EditorWindow(CheatWindow 전례 있음). 데이터 먼저, 대시보드는 UX 레이어.
6. **PlayerSkills 분리**(선택): 수치 추출 후 남은 로직 분리.

**밸런스 툴 원칙**: Tier A(깔끔한 스칼라, 자주 튜닝)=SO 추출 / Tier B(레벨업 순환·되감기 홀짝 등 로직모양)=코드에 남기고 한 곳에 모음. 경계선은 Phase 0의 BALANCE_MAP.md에서 항목별 확정 후 진행.

**현 밸런스 위치**: ①이미 SO=StageTable·LevelUpStatOptionSO·ScalingTable·EnemyDefinition 7종·MapDefinition·**SkillTable(신규)**·**CharacterDefinition(신규)** / ②프리팹=Enemy 플래그·VFX·사망분출 / ③코드 하드코딩=**BalanceConstants(신규, 전역 상수 집결)**·진화티어(Tier B 보류)·[Meta.cs:115](Assets/Scripts/Meta.cs#L115)(메타노드). **스킬 기본수치·캐릭터 스탯은 ③에서 빠졌음(Phase 3)**. 남은 ③ = 진화/패시브 티어(보류)·메타노드.

**착수점**: **Phase 4 (캐릭터 선택 화면)** — Phase 2(맵 선택)와 대칭. Title `Canvas`에 신규 패널 + `Controllers`에 컨트롤러 스크립트(MapSelectUI 복제) → 카드 선택 시 `RunConfig.Character` 세팅 후 SampleScene 로드. 씬 배선은 `SCENE_MAP.md`(Title 섹션·캐릭터 선택 화면 행). **선행 필요**: 2번째 CharacterDefinition 에셋 디자인(고유 시작스킬·허용풀·외형) — 사용자와 캐릭터 콘셉트 먼저 확정. (Phase 0·1·2·3 완료.)
- **2번째 맵 에셋**: 아직 없음(Phase 2는 UI 인프라만). 새 맵 = 새 MapDefinition 만들어 `MapSelectUI.maps[]`에 추가하면 카드 자동 생성.
- **밸런스 편집 지금 가능**: `Assets/Data/SkillTable.asset`(스킬 쿨/뎀/성장), `EnemyDefinition`, `MapDefinition`, `ScalingTable`을 인스펙터에서 직접 조절. 전역 상수는 `BalanceConstants.cs`.

---

## ★ 다음 세션 — 재검증 대상 (전부 씬 저장·컴파일 완료)
- [ ] **ESC 요약 표시** 실확인: 항목/표현/줄넘침(4스킬 다 찼을 때).
- [ ] **Title 레이아웃** 실확인: 통화 텍스트 위치·리셋/빌드바 간격(y값 감으로 잡음).
- [ ] **체력회복 4% 단일개방** 체감 확인(낮으면 상향).
- [ ] **호밍 타격 VFX**(Impact Sparks)가 미사일에 어울리는지.
- [ ] **호밍 아이콘**: 여전히 되감기와 `Icon_Rewind` 공유(임시). 전용 아이콘 그리면 `activeIcons[6]` 교체.

---

## ⚠️ 미해결 이슈 / 주의
- **씬 직접 조작 OK**(사용자 승인, 2026-07-20): MCP로 씬 열기/수정/저장 자유롭게. 과거 "씬 손대지 말 것" 원칙·메모리는 폐기됨.
- **에디터 인스펙터 예외(무해)**: 플레이 진입 시 `ObjectPreview.DrawPreview ... Image destroyed` — 인스펙터 프리뷰 표시 오류로 게임/빌드 무관. 하이라키 선택 해제 시 사라짐.
- **노션 토큰 노출**(기존): 폐기·재발급 권장.
- (기존) **사운드 볼륨 이중곱** — 우선순위 낮음. `SfxPlayer`/`AudioThrottle`/`ObjectPool`/Vefects.
- (참고) ONBOARDING의 `홈\.mcp.json` 노션 방식 자동로드 안 됨 → user scope 등록이 정답. 정정 필요.

---

## 🎨 스킬 이펙트 픽셀 크기 컨벤션
스킬 이펙트 스프라이트는 배경과 픽셀 밀도가 안 맞아, **그림은 작게 그리고 엔진에서 1.5배로 표시**한다(플레이어 딸기 프리팹 `localScale=1.5`에 맞춤).
- 새 이펙트 프리팹은 기존값에 곱하지 말고 `localScale`을 **직접 (1.5,1.5,1.5)로** 세팅.
- PPU **32**, Main Camera Orthographic Size 5(화면 세로 = 10유닛).
- 공식: `원본 그림 px = (목표 월드 유닛 × 32) ÷ 1.5` (예: 화면 세로 꽉 채우는 세로형 = 64 × 213px)
- 카메라 Size나 플레이어 localScale이 바뀌면 이 배율을 전부 다시 계산할 것.

---

## 보류 중인 결정 / 백로그
- UI 시스템: uGUI + TextMeshPro 확정 (한글 폰트 = `Galmuri11 SDF`)
- 배포(itch.io 등)는 아직 진행 안 함
- 아웃게임 컬렉션(캐릭터/스킬 해금) = 스킬트리 이후 Phase
- 진화 시스템·메타 신규 수치는 감으로 잡은 값 — 플레이테스트로 조정 필요
