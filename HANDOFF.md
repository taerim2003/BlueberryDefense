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

## 현재 상태 (2026-07-21, 세션 10)

**빌드**: 컴파일 에러 없음(경고 CS0618 기존 잔존, 무해). SampleScene 저장 완료. **리팩토링 로드맵(Phase 0~5) 완료** — 게임은 이제 단일 씬 + SO 스왑 구조. 세션10은 밸런스/UI 다듬기.

### 세션10 — 밸런스/UI 다듬기 (전부 컴파일·저장 완료, 실플레이 확인만 대기)
- **패시브 데이터화**: `PassiveProgression.cs`(SO, 액티브 SkillProgression 미러링) + `Assets/Data/Skills/Passive_<5종>.asset`(기본값·레벨업당 상승값). `PlayerPassives`가 하드코딩 상수 대신 SO 참조(공통경로 `ApplyPassiveValue`, 정적 조회맵). 치명타/초기화 기본값도 SO 이관 + Awake 판마다 리셋. 카드 설명은 SO값 기반 동적 생성. 진화 트리는 현행 하드코딩 유지(범위 밖).
- **낙뢰 표시버그 수정**: 일시정지 요약 낙뢰 피해 음수 표기. 원인=`GetDefaultDamage(Lightning)`가 실시간 `ProcDamage`(배율 적용값) 반환. `LightningStorm.BaseProcDamage=12f` 고정상수로 교체. (실제 피해는 원래 정상)
- **일시정지 창 2열 개편**: `PauseMenu` 1760×940, 왼쪽=액티브·오른쪽=패시브, 항목별 스킬 아이콘. **스크롤 없음 — 후반 액티브 다 모으면 한 열 넘칠 수 있음(그때 스크롤 추가)**.
- **레벨업 대체보상 = 정수 +10**: `LevelUpStatOptionSO` 폐기 → 레벨업 후보 3개 미만이면 '정수 +10' 1개 끼움, 그래도 모자라면 선택지 1~2개만 뜸.
- **종이비행기 전용 스테이지 SO화**: `EnemySpawner` `currentStage==7`·`>=5` 게이트 제거 → StageTable 데이터(7스테이지 `paperPlaneChance=1`)로 표현.
- **호밍 아이콘**: `Icon_Homing.png` 추가·배선(`activeIcons[6]` 2곳)·임포트 설정(Point·Uncompressed·PPU32).

---

## 🧩 콘텐츠 확장 = 순수 데이터 (리팩토링 완료)
- **밸런스 편집**: `Window > Blueberry Defense > Balance Dashboard` 또는 인스펙터 — `Prog_*.asset`(액티브)·`Passive_*.asset`(패시브)·`EnemyDefinition`·`MapDefinition`·`StageTable`·`ScalingTable`·`CharacterDefinition`. 전역 상수는 `BalanceConstants.cs`. **진화 티어 수치는 아직 코드**(PlayerSkills/PlayerPassives 하드코딩, Tier B 보류).
- **새 맵**: `MapDefinition` 에셋 만들어 `MapSelectUI.maps[]`에 드래그 → 카드 자동 생성. (현재 `Map_BlueberryField` 1장)
- **새 캐릭터**: `CharacterDefinition`(고유 시작스킬·허용풀·기본체력·외형) 만들어 `CharacterSelectUI.characters[]`에 드래그. (현재 `Char_Strawberry` 1종)
- **새 스킬**: `ActiveSkillId` enum + `Fire*` 메서드 추가, 캐릭터 `allowedPool`로 게이팅.
- **미착수(선택)**: Phase 6 = `PlayerSkills.cs` 분리(약 1400줄, 과설계 우려로 보류). 콘텐츠 확장(2번째 맵·캐릭터)으로 넘어가도 됨 — 선행: 사용자와 콘셉트 확정.

---

## ⚠️ 미해결 이슈 / 주의
- **씬 직접 조작 OK**(사용자 승인): MCP로 씬 열기/수정/저장 자유. 과거 "씬 손대지 말 것" 원칙은 폐기.
- **에디터 인스펙터 예외(무해)**: 플레이 진입 시 `ObjectPreview.DrawPreview ... Image destroyed` — 인스펙터 프리뷰 오류, 게임/빌드 무관.
- **노션 토큰 노출**(기존): 폐기·재발급 권장.
- (기존) **사운드 볼륨 이중곱** — 우선순위 낮음(`SfxPlayer`/`AudioThrottle`/`ObjectPool`/Vefects).
- (참고) ONBOARDING의 `홈\.mcp.json` 노션 자동로드 안 됨 → user scope 등록이 정답. 정정 필요.

---

## 🎨 스킬 이펙트 픽셀 크기 컨벤션
스킬 이펙트 스프라이트는 배경과 픽셀 밀도가 안 맞아, **그림은 작게 그리고 엔진에서 1.5배로 표시**(플레이어 딸기 프리팹 `localScale=1.5`에 맞춤).
- 새 이펙트 프리팹은 기존값에 곱하지 말고 `localScale`을 **직접 (1.5,1.5,1.5)로** 세팅.
- PPU **32**, Main Camera Orthographic Size 5(화면 세로 = 10유닛).
- 공식: `원본 그림 px = (목표 월드 유닛 × 32) ÷ 1.5` (예: 화면 세로 꽉 = 64 × 213px)
- 카메라 Size나 플레이어 localScale이 바뀌면 이 배율을 전부 다시 계산할 것.

---

## 보류 중인 결정 / 백로그
- UI 시스템: uGUI + TextMeshPro 확정 (한글 폰트 = `Galmuri11 SDF`)
- 배포(itch.io 등)는 아직 진행 안 함
- 아웃게임 컬렉션(캐릭터/스킬 해금) = 스킬트리 이후 Phase
- 진화 시스템·메타 신규 수치는 감으로 잡은 값 — 플레이테스트로 조정 필요
- 딸기 전용 초상화 도트 미완(`Player.png` 임시) — 그리면 `Char_Strawberry.portrait`만 교체
