# HANDOFF.md — 세션 인계 문서

<!--
📋 유지 가이드 — "다음 세션이 지금 뭘 해야 하는가"와 "뭘 모르면 사고 나는가"만 담는다.
- 담을 것: 현재 상태 · 미해결 이슈 · 구조적 함정. 그것뿐.
- 지울 것: 완료·검증된 것은 즉시 삭제(git에 남는다). 세션 로그를 누적하지 말 것.
- 🔴 **축적형 문서가 아니다.** 매 세션 "추가 + 삭제" 둘 다 한다. 1022줄까지 불어난 적 있음(2026-08-26에 잘라냄).
- 사본 금지: 할 일·일정은 **칸반이 원본**, UI 규격은 **CLAUDE.md §5-1**, 씬 배선은 **SCENE_MAP.md**, 구조는 **ARCHITECTURE.md**.
  여기엔 포인터만 남긴다.
- 목표: 한 화면에 읽힐 것. **200줄을 넘기면 자른다.**
- ⚠️ 여기 적힌 "~해야 함"·예시·해법은 **낡는다.** 사용자에게 말하기 전에 코드로 1건 확인할 것.
-->

## 🧭 North Star
> **"스킬을 강화해 적들을 시원하게 쓸어버리자."**
> QWER 액티브 4 + 패시브 4를 진화로 강화해 빌드를 만드는 벙커 디펜스. 스펙은 `GDD.md`.

**세션 시작**: `brief` 스킬이 절차다. **세션 종료**: `/wrap`. `GDD.md`는 grep으로 필요한 절만.

### 📋 일정·할 일의 원본 = 노션 (여기에 사본을 만들지 말 것)
- **칸반 "POC 할일"** — DB `3b06394c-d983-803d-af4a-c26f924121b7`. 조회 절차·함정은 **`kanban` 스킬**.
  사용자가 세션과 무관하게 카드를 올린다 → **매번 조회해서 답할 것.** 어긋나면 칸반이 이긴다.
- **캘린더 "블루베리 디펜스 일정"** — `3a16394c-d983-80ac-98e8-caf8a4804dc0`. 날짜를 인용하기 전에 조회.
- 못 미루는 고정점 셋: **9/12 빌드 리뷰 제출**(리뷰 7영업일) · **9/25 출시**(3,000원) · 8/29~31 사용자 부재(여행 + 외부 테스트).
- 🧊 **콘텐츠 동결.** 폴리싱이 기본값이고, 콘텐츠 추가는 **칸반 카드로 올라온 것만**. 먼저 제안하지 말 것.

---

## 현재 상태 (2026-08-26, 세션 48)

**빌드**: 컴파일 통과. 태리미가 빌드로 통리뷰하고 지적한 **9건을 전부 수정 → 9건 다 태리미가 확인**했다.
항목별 상세는 칸반 **「08/26 통리뷰 수정 9건」 카드(완료)**. 여기엔 코드로 남는 함정만 적는다.

- 🔴 **`Image.type`이 `Simple`이면 `fillAmount`가 통째로 무시된다**(경고 없음). 체력·경험치 바가 안 움직이던 원인.
  §5-1이 "무조건 Simple"이라 **게이지 채움 층까지** 그렇게 바뀌어 있었다 — 거기만은 `Filled`가 맞다(CLAUDE.md §5-1에 예외로 박아 뒀다).
  `UISkinApply.InsetGaugeFills`가 채움 그림을 단색으로 덮어쓰던 것도 막았다(**그림이 비었을 때만** 넣는다).
- 🔴 **uGUI `Outline`을 판 그림에 걸지 말 것.** 스프라이트 메쉬를 4방향 복제해 색을 **곱하는** 방식이라,
  테두리가 검고 속이 흰 그림에 걸면 바깥에 테가 생기는 게 아니라 **원래 검은 테두리가 물든다.**
  → `_투명` 선화를 `Mask`로 쓰고 안을 채운 뒤 **대상보다 크게** 잡는다(`LevelUpUI.MakeEvolveGlow`).
  ⚠️ 자식은 부모보다 **나중에** 그려진다 — `SetAsFirstSibling()`을 지우면 카드 글자를 덮는다.
- 🔴 **`Enemy.TakeDamage`는 같은 호출 안에서 동기적으로 `isDead`를 세운다.** 그래서 `Projectile`이 `OnHitBonus`를
  부를 땐 이미 `IsAlive == false`다 — **명중 콜백에서 `IsAlive`로 가드하면 즉사시킨 경우가 통째로 빠진다.**
  암살 사격의 추격 화살이 한 번도 안 나오던 원인(치명타 = 거의 항상 즉사).
- 🔴 **레벨업 카드 3장은 `Dialog/Layout`(VerticalLayoutGroup) 소속이다** — 좌표를 코드로 쓰면 다음 리빌드에 덮인다.
  크기만 먹고 위치는 레이아웃이 잡아 갈림길 창이 반쪽으로 깨져 있었다. **씬 배치를 그대로 쓸 것.**
- ⬜ **확인 대기 1건**: 추격 화살의 **궤적·생성 위치·크기**는 아직 눈으로 안 봤다. 손잡이는 전부 `PlayerSkills` 상수 —
  `ChasingArrowBackOffset` · `ChasingArrowRiseOffset` · `ChasingArrowTurnDegPerSec`(200, **낮출수록 크게 휜다**) · `ChasingArrowScale`.
- ⬜ **미수정 2건**: 툴팁 "20 정수" 뒤 화살표가 **두부(□)**(폰트에 글리프 없음 — 문자를 정하면 TSV 한 줄) ·
  `HealthPanel`과 게임오버 `Dialog`가 아직 `Sliced`이고 **`Dialog`는 Image가 꺼져 결과창에 배경판이 아예 없다.**
- ⚠️ **`EvolutionPanel`·설정 창은 태리미가 직접 다시 만든다**(둘 다 Battle 씬 오브젝트, 설정 창만 런타임 생성).
  **갈림길 창만** 태리미 지시로 클로드가 플레이어블 수준까지 손댔다.

---

## 🧰 시스템별 소유권 (여기 안 적힌 곳을 고치면 되돌아간다)

- **UI 스킨** — `Window > Blueberry Defense > UI 스킨 적용`([UISkinApply.cs](Assets/Editor/UISkinApply.cs))이 **전 화면을 소유**한다.
  🔴 **씬을 손으로 고치면 다음 실행에 되돌아간다.** 도구 위쪽 토큰·표를 고치고 다시 돌릴 것.
  ⚠️ `UISkin.FitSlice`는 여전히 `type = Sliced`를 강제한다 — CLAUDE.md §5-1과 정면 충돌하므로 도구를 돌리기 전에 그 함수부터 볼 것.
  런타임 생성 UI는 `Assets/Resources/UISkin.asset`을 본다(손으로 만들지 말고 `UI 스킨 에셋 만들기`로 굽는다).
  버튼 손맛은 [JuicyTuning.cs](Assets/Scripts/JuicyTuning.cs)가 단독 소유(인스펙터에서 고쳐도 도구가 덮는다).
- **번역** — 조회는 `Loc.T/F/TOr` **한 곳만** 지난다. 표는 `Assets/Localization/Tables/Game`(ko/en 각 617).
  🔴 **TSV가 원본**(`Assets/Localization/*_ko.tsv`·`*_en.tsv`) — 표를 손으로 편집하면 되돌릴 수 없다.
  적재는 `Window > Blueberry Defense > 번역 - 모든 TSV를 표에 적재` 하나로 끝난다(파일명 접미사가 곧 로케일).
  🔴 **표시 문구를 SO에서 직접 읽지 말 것**(`SkillNode.Name` 등이 `Loc.TOr` 창구). `const string`으로 UI 문구를 두지 말 것.
  씬 TMP는 `LocalizedTmp`로 키에 묶되, **런타임에 코드가 값을 덮어쓰는 TMP에는 붙이지 말 것**.
  남은 것: **용어집 검수 4건 + en으로 한 판 돌려 보기**(Day1 패치 예정 — 영어는 초벌로 출시된다).
- **사운드** — `Assets/Resources/SfxLibrary.asset`에 15개가 꽂혀 있다. **꽂아둔 건 후보지 확정이 아니다**(듣고 거슬리는 것만 교체).
  ⚠️ `JuicyButton`은 별도 어셈블리라 `SfxPlayer`를 못 부른다 — `JuicyButton.Clicked` static 이벤트를 **역방향으로 구독**한다. 직접 호출로 되돌리면 컴파일이 깨진다.
  ⚠️ 스킬 캐스트음 9종은 `SfxLibrary`가 아니라 **`PlayerSkills` 인스펙터**에 슬롯이 있다(중복 배선 금지).
  ⚠️ **승리는 `GameOver()`를 안 거친다** — `GameClear()`에 따로 배선돼 있다. BGM은 `MapDefinition.bgm`(현재 전 맵 null).
- **컬렉션(도감)** — 씬 배치가 없다(`OptionsMenu`처럼 런타임 자체 캔버스). 발견 기록은 `CollectionSave`(PlayerPrefs CSV 한 줄),
  기록 지점은 `Acquire/Evolve` **4곳뿐**. 타이틀엔 `LevelUpUI`가 없어 아이콘을 못 집으므로
  `스킬 아이콘 라이브러리 굽기`로 `SkillIconLibrary.asset`을 굽는다(**enum ↔ 파일명 표는 그 도구가 단독 소유**).

---

## ⚠️ 미해결 이슈 / 주의

- 🔴 **맵 3종이 서로 다른 단계에 있다. 밸런스를 고치기 전에 어느 맵인지 확인할 것.**

  | 맵 | fieldScale | 스테이지 테이블 | 배경 | cameraYLift |
  |---|---|---|---|---|
  | 블루베리 밭 | **1.25** | `StageTable_Farm` | ✅ 400×225 4컷 애니 | 1.15 |
  | 해안가 | 1.5 | `StageTable_Coast` | ✅ 480×270 | 2.2 |
  | 광활한 밭 | **1.8** | `StageTable`(구 농장 표를 그대로 물려받음) | 🔴 **576×324 미제작** | 🔴 0(미설정) |

  → **광활한 밭은 배경(576×324, PPU 18)이 나와야 나머지가 따라온다.** 배경 없이 실행하면 가장자리가 빈다(버그 아님).
- 🔴 **2차 진화 그림이 전무하다** — 2차가 전부 1차 그림을 그대로 쓴다. 9/5~9/8 구간에 2일치로 잡혀 있다.
- 🔴 **세이브는 레지스트리이고 에디터와 빌드가 다른 키를 쓴다.**
  개발 세이브 = `HKCU\Software\Unity\UnityEditor\taerimgames\BlueberryDefense` · 빌드 = `HKCU\Software\taerimgames\BlueberryDefense`.
  ⚠️ Unity가 float PlayerPrefs를 **QWORD**로 저장하는데 `GetValueKind`는 **DWord라고 잘못 보고**한다.
  초기화: `reg delete "HKCU\Software\Unity\UnityEditor\taerimgames\BlueberryDefense" /f`(Unity 종료 상태에서).
- ⚠️ **13·22스테이지(UFO 벽)는 투하물이 물량 쿼터에 안 잡힌다** — 게임에서 제일 무거운 판. 프레임이 떨어지면 여기부터 의심.
- ⚠️ **맵별 클리어 기록은 소급되지 않는다** — 이미 깬 사람도 농장을 한 판 더 깨야 파인애플이 열린다.
- ⚠️ **VFX 프리팹 AudioSource 볼륨은 스폰 시점에만 반영된다** — 재생 중인 루프는 슬라이더를 움직여도 안 바뀐다(다음 스폰부터).
- ⚠️ **판 전환/게임오버 시 비행 중인 경험치 보석의 XP는 유실**된다(무해하다고 판단).
- **노션 토큰 노출**(기존): 폐기·재발급 권장.

---

## 🔧 손잡이 — **한 값이 다른 값과 묶여 있는 것만**

> 나머지는 여기서 찾지 말 것. 밸런스는 **에셋이 정답**(`StageTable*`·`EnemyDefinition`·`Prog_*`·`BalanceConstants.cs`),
> UI 손맛은 `JuicyTuning.cs`, 진화 파워는 `ApplyPathTierEffect` + 각 `Fire*`(실시간 기믹이 숨어 있다).

| 만질 것 | 같이 움직이는 것 |
|---|---|
| 적 종류 확률(`eliteChance`·`hopperChance`·`surferChance`…) | **else-if 사슬** — 앞 확률을 올리면 뒤가 전부 줄어든다 |
| `BalanceConstants.ContactStopDistance` | `HeadbuttLungeDistance` · `AmbushBandMaxX` **3종 세트**. 하나만 바꾸면 박치기가 허공을 친다 |
| `MapDefinition.cameraYLift` | 서핑 강하 띠. 리프트를 바꾸면 서핑 높이도 같이 움직인다 |
| `SwingNearOffset` · `SwingCenterYOffset` | **일부러 `skill.Scale`을 안 곱한다** — 곱하면 크기를 키울수록 코앞이 비거나 판정이 떠오른다 |
| `SwingImpactDelay`(0.225초) | `Pinapple_Attack.anim`의 프레임 타이밍(5프레임 = 0.525초) |
| `Projectile.OffscreenMargin`(3) | 화살비 생성 높이(화면 위 +2)·적 스폰 x(-9). **좁히면 살아야 할 화살이 태어나자마자 지워진다** |
| `Enemy.DamageNumberJitterX`(0.3) | `DamageNumberStackStep`(0.62). **훨씬 작게 유지**해야 숫자가 안 겹친다 |

---

## 🎨 스킬 이펙트 픽셀 크기 컨벤션
**그림은 작게 그리고 엔진에서 1.5배로 표시**(플레이어 프리팹 `localScale=1.5`에 맞춤). PPU **32**, 카메라 ortho 5.
- 공식: `원본 그림 px = (목표 월드 유닛 × 32) ÷ 1.5`. 새 이펙트 프리팹은 `localScale`을 **직접 (1.5,1.5,1.5)로**.
- **배경만 PPU 18**(1배=320×180px, 1.5배=480×270, 2배=640×360).
- ⚠️ **새 PNG는 임포트 기본값(PPU100·Bilinear·압축·Multiple·좌하단 pivot)으로 들어온다.** 동종 스프라이트의 `.meta`와 대조할 것.
  Multiple→Single 전환은 서브에셋 ID를 바꿔 **프리팹 참조가 깨진다** — 재임포트 → 재배선 순서 고정.
- ⚠️ **일괄 정규화로 `spriteAlignment`(pivot)를 건드리지 말 것** — 그림마다 의도가 다르다(`Effect_BigTornado`는 바닥중앙).

## 🧩 콘텐츠 확장 = 순수 데이터
- **밸런스**: `Window > Blueberry Defense > Balance Dashboard`. 전역 상수는 `BalanceConstants.cs`.
- **스킬트리**: `Skill Tree Editor`. 새 강화 노드는 **id**를 `SkillEffects.Compute`의 case에 추가(에셋 `effect` 필드는 신뢰 안 함).
- **치트**: `Cheat Window` — 정수 지급, 만렙, 스킬트리 초기화, 승천 등급.
