# CLAUDE.md

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

> 🔴 **이 문서에는 *규칙*만 적는다.** "언제 이래서 이랬다"는 사례·함정은 적지 말 것 —
> 한 번 고치고 끝난 것이면 **코드 주석**과 git 커밋 메시지에 남는다. 문서를 부풀리는 건 늘 사례 쪽이다.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- **해석이 갈리는 걸 *인지한 순간*이 묻는 시점이다.** 다 만들어 놓고 끝에 단서를 붙이는 건 묻는 게 아니다.
  갈림길이 **손맛·연출**일 때 특히 그렇다 — 검증으로는 안 갈리고, 틀리면 방향이 **정반대**가 된다.
- **요구 목록의 한 줄이 *현상*인지 *목표*인지 갈라 읽을 것.** "~한 느낌"·"~하는 거임" 같은 서술이 위험하다.
  → **지금 값을 먼저 재 본다. 값과 해석이 어긋나면 틀린 건 해석이다.**
- **코드를 고치기 전에 "무엇을 어디에 손대는지"를 한 줄로 선언할 것.** 묻는 게 아니라 **선언**이다 —
  답을 기다리지 말고 진행하고, 틀렸을 때만 사용자가 끊게 한다.
  **파일·화면·오브젝트 이름을 대야 틀린 게 드러난다**("우측 하단에"는 안 드러나고 "`SkillTreeUI` 우측 하단"은 드러난다).
- **UI 코드를 고치기 전에 그 코드가 그리는 화면을 전부 열거할 것.** 한 컴포넌트가 여러 화면을 그린다
  (`LevelUpUI` 하나가 3택 레벨업 · 2택 갈림길 · 진화 대상 선택 셋을 그린다). 열거하면 "어느 화면인가"가 저절로 질문이 된다.
- **"넣어줘"를 받으면 넣을 곳이 아니라 *이미 있는 곳*부터 `grep`으로 센다**(연출이면 `DOTween|DOScale|Punch`, 저장이면 `PlayerPrefs`…).
  있으면 **겹치는지·경합하는지**부터 보고할 것.
- **"A를 B로 바꿔줘"를 받으면 B를 짜기 전에 *A에 딸린 것*을 `grep`으로 전부 센다** — 호출부·전용 분기·프리팹 참조.
  요청은 "무엇으로 바꿀지"만 말하고 **무엇이 사라지는지는 안 말한다.** 죽는 게 있으면 **지울지 남길지 먼저 보고**한다.
- **사용자 아트를 반영할 땐 파일 수정 시각부터 확인할 것.** 여러 번 다시 그리는 워크플로에서
  "내가 배선한 그림"과 "지금 디스크에 있는 그림"이 쉽게 어긋난다.
  **배선 직전에 "한 파일 = 한 그림인가"도 볼 것** — 임포트 기본값이 Multiple이라 자동 슬라이스가 그림을 조각낸다
  (서브 스프라이트가 `_0`이 아니라 `_1`이면 이미 쪼개진 것). 절차는 `unity-mcp` 스킬.
- **파일명 접미사(`R1`/`R2` 등)의 뜻은 파일 종류마다 다르다** — 루트일 수도, 티어일 수도, **애니메이션 프레임 번호**일 수도 있다.
  **그림을 열어보는 것으로는 안 갈린다. 한 번 물을 것.**
- 선택지를 낼 땐 **손잡이 이름(필드명)이 아니라 화면에서 벌어지는 일로** 먼저 한 줄 설명할 것.
- **선택지 한쪽이 "지금 구조를 유지한다"면 그 항목에 *유지되면 남는 불편*을 적을 것.**
  바로 위 규칙은 *고르면 무엇이 생기는지*만 말해서 **고른 뒤에도 여전히 없는 것**은 안 적게 된다 —
  사용자는 그게 해결되는 줄 알고 고른다. 트리거 자명 — *선택지 한쪽이 현상 유지인 순간.*
- **"정할까요?"를 쓰기 전에 그 결정이 이미 내려져 있는지 이력을 볼 것.**
  트리거 자명 — *커밋 포함 여부·경로·설정처럼 **반복되는 것**을 물으려는 순간.*
  `git log`·`.gitignore` 한 번이 질문보다 싸고, 물으면 **이미 정한 걸 다시 정하게 만든다.**
- **한 값이 결과 둘을 동시에 움직이면, 고치기 전에 두 결과의 수식을 세운다.**
  트리거 자명 — *같은 파라미터를 고쳤는데 사용자가 **반대 방향 불만**을 말하는 순간*("얇다" 다음에 "너무 파고든다").
  그건 손잡이가 모자란 게 아니라 **내가 결과를 하나만 보고 있다는 신호**다.
- **"구조적 한계/불가능"을 말하기 전에 둘을 본다.** ① 전수 조사를 한 번 한다(표본 하나에 전체를 맞추고 있던 것일 수 있다).
  ② 그 "~라서"가 **사용자가 준 조건인지 내가 얹은 조건인지** 갈라 본다 — 사용자가 이미 예외를 허용해 둔 규칙을 엄격하게만 읽으면 난다.
  **손으로 한 계산으로 가능성을 닫지 말 것** — 실측 한 번이 더 싸다.
- **래퍼·파사드를 만들면 전제를 *모든 공개 진입점*에 건다.** 트리거 자명 — *`static` 창구 클래스에 두 번째 공개 멤버를 추가하는 순간.*
  하나에만 걸면 다른 진입점이 **조용히 다른 상태를 본다**(호출 순서가 우연히 맞으면 가려진다).
- **진단용 출력이 서로 다른 값을 같은 문자열로 뭉개지 않는지 볼 것.** 트리거 자명 — *숫자를 잘라서·줄여서 찍으려는 순간.*
  폭이 걱정되면 값을 줄이지 말고 **표본 수**를 줄인다.
- If a simpler approach exists, say so. Push back when warranted.
- **대안으로 도구·기능을 꺼낼 땐 꺼내는 그 자리에서 그 문서를 연다.** "X로도 됩니다"라고 **쓰기 전에** 여는 것이지,
  사용자가 "X가 뭔데?"라고 물은 뒤가 아니다.
- If something is unclear, stop. Name what's confusing. Ask.
- **외부 도구(MCP·API)에 한글을 넘길 땐 유니코드 이스케이프를 손으로 만들지 말고 한글을 그대로 쓸 것.** JSON에 직접 넣어도 문제없다.
- **메모리·문서에는 규칙만 적고, 변하는 것(현재 값·구현 상태·목록)은 "어디서 확인하는지"만 적을 것.**

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

## 5. 개발 환경 & 패키지

- **Unity 6** (6000.4.4f1), URP 2D
- **UI 시스템**: uGUI (미정 시 기본값. 필요 시 UI Toolkit으로 전환하고 여기 갱신)
- **빌트인 패키지**: 2D Animation, 2D Tilemap, 2D SpriteShape, URP
- **서드파티 플러그인**:
  - **Unity-MCP** (IvanMurzak, `com.ivanmurzak.unity.mcp`) — Claude Code가 Unity 에디터를 직접 조작하기 위한 MCP 브릿지. §6 참고.
  - VFX: `Assets/Vefects/Pixel Craft VFX URP` (에셋스토어, URP 2D용 임포트 완료 — `VFX_2D_...` 접두사 프리팹 사용)
  - 도트 그래픽: 캐릭터·몬스터·배경은 직접 그리기 (GDD §6 프로토타입 계획 참고)

### 🎨 픽셀 크기 컨벤션
**그림은 작게 그리고 엔진에서 1.5배로 표시**(플레이어 프리팹 `localScale=1.5`에 맞춤). PPU **32**, 카메라 ortho 5.
- 공식: `원본 그림 px = (목표 월드 유닛 × 32) ÷ 1.5`. **새 이펙트 프리팹은 `localScale`을 직접 `(1.5,1.5,1.5)`로.**
- **배경만 PPU 18**(1배=320×180px, 1.5배=480×270, 2배=640×360).
- ⚠️ 새 PNG는 임포트 기본값(PPU100·Bilinear·압축·Multiple·좌하단 pivot)으로 들어온다. 동종 스프라이트의 `.meta`와 대조할 것.
  Multiple→Single 전환은 서브에셋 ID를 바꿔 **프리팹 참조가 깨진다** — 재임포트 → 재배선 순서 고정.
- ⚠️ **일괄 정규화로 `spriteAlignment`(pivot)를 건드리지 말 것** — 그림마다 의도가 다르다.

---

## 5-1. 🔴 UI 이미지는 **무조건 Simple + Set Native Size** (사용자 결정, 예외 없음)

**칸 크기를 정해두고 거기 그림을 맞추지 말 것. 스프라이트 원본 크기가 칸 크기를 정하고, 텍스트·레이아웃을 그 안쪽에 맞춘다.**

- `Image Type` = **`Simple`**, 크기는 **`Set Native Size`**(코드로는 `img.SetNativeSize()`)로 원본 픽셀 그대로.
  ⚠️ `Set Native Size` 버튼은 **Type이 Simple일 때만** 인스펙터에 나타난다.
- 🚫 **`Sliced`(9-slice)를 쓰지 않는다.** border를 원본 픽셀 크기 그대로 그려서, 줄여 쓰면 테두리·아래 그림자 띠만
  상대적으로 두꺼워지고 늘려 쓰면 가운데가 뭉갠다.
- **`Preserve Aspect`는 켠다.** 사용자가 잡은 판·버튼이 전부 `Simple + NATIVE + PA`다.
- **`localScale`은 가급적 쓰지 않는다.** 꼭 필요하면 써도 되지만(사용자 결정) 그 Image는 무조건 `Simple + Preserve Aspect`여야 한다.
- **칸에 맞는 원본 크기 그림이 세트에 없으면 늘려 쓰지 말고 그 크기의 그림을 요청**한다.
  "늘려 쓸 큰 판을 그려 달라"는 **잘못된 요구**다.

**검증 — 값이 아니라 캡처로 한다. 셋 다 값 검사에 안 걸린다:**
- **rect의 "안쪽에 들어감"은 그림자 두께·실제 글리프 높이를 못 잡는다.** 한 화면 고칠 때마다
  `ScreenCapture.CaptureScreenshot`으로 찍어 눈으로 볼 것.
- **`칸 비율 vs 그림 비율`을 나란히 찍어 볼 것.** `Preserve Aspect`는 비율이 어긋나도 조용히 맞춰 그린다 —
  경고도 없고 rect도 정상이라 "넘치는 것"만 세는 전수 조사엔 **눌린 것**이 안 걸린다.
- **비율이 맞아도 틀린다 — 그림이 *칸 전체를 채우지 않는* 경우가 있다.** 판 그림에는 둘레의 **투명 여백**과
  **둥근 실루엣**이 있어서, rect 기준으로 놓은 자식이 **판 밖 허공에** 간다.
  트리거 자명 — *판 스프라이트를 깐 칸 안에 글자·버튼을 배치하려는 순간.* → 실루엣을 **PNG 알파로 재서** 그 안에 배치한다.
- **색·알파도 캡처로 판정할 것.** `Image.color`가 노랑이어도 **그림이 검정이면 화면은 검정**이다(셰이더가 텍스처에 색을 곱한다).

### 📐 판 그림별 용도와 글자 크기 (Title·Battle 씬 실측)

**새 화면을 만들 때 이 표에서 고른다. 칸 크기는 원본 그대로 쓰고, 글자를 거기 맞춘다.**

| 그림 | 원본 | 쓰이는 곳 · 글자 |
|---|---|---|
| `가로길쭉이_색칠` | 361×103 | 메인 메뉴 버튼(**42pt**) · 정수 표시(28pt Right) · 맵 이름표(26pt) |
| `가로안길쭉이_색칠` | 227×118 | 뒤로/선택 버튼(**32pt**) · 레벨 박스(26pt) |
| `베개같이생긴네모_색칠` | 373×195 | 제목 박스 — 스킬트리/캐릭터/맵/스테이지/레벨업 헤더(**40~42pt**) |
| `각진정사각형_색칠` | 203×221 | 캐릭터 카드 (이름 24pt) |
| `정사각형_색칠` | 372×372 | 승천 행 · 원티드 포스터 · 리롤 버튼(0.67배 PA, 28pt+18pt) |
| `개큰네모_색칠` | 721×289 | 스킬 설명 박스(이름 32pt Left · 설명 22pt) |
| `길쭉큰네모_색칠` | 900×210 | 레벨업 선택 버튼 3장 |
| `스킬아이콘하기좋은네모_색칠` | 143×141 | 아이콘 칸 |
| `동그라미_색칠` | 145×135 | 좌우 화살표 버튼(36pt) |

- **게이지는 3겹**: `OO바_색칠`(바탕) + `OO바_내용물`(채움) + `OO바_투명`(테두리를 맨 위에 덮음). 체력·경험치 둘 다.
  🔴 **가운데 채움 층만은 §5-1의 예외 — `Image.type`이 반드시 `Filled`(Horizontal · Left)여야 한다.**
  `Simple`이면 **`fillAmount`가 통째로 무시되어** 바가 늘 꽉 차 보인다(경고도 없다). `preserveAspect`도 꺼야 한다.
- **아이콘류만 확대 예외** — 원본이 24×29·32×48로 작아서 키워 쓰되 **`Preserve Aspect` 필수**.
- **딤 배경**: 스프라이트 없는 Image에 `#08050D` + 알파(전체 덮기 0.96 / 모달 0.55~0.85).
- **IDLE 모션은 [UIFloat.cs](Assets/Scripts/UIFloat.cs)를 붙이기만 하면 된다**(새로 만들지 말 것). 호버 중엔 자동으로 멈춘다.
- **버튼 연출은 `JuicyButton`**이 소유(`idleDim`으로 평소 0.92배·어둡게, 호버 1.06배). 값은 `JuicyTuning.cs`.
  `targetGraphic`은 `colorOnHover`를 켤 때만 쓰는 필드다 — **비어 있어도 정상**이다.

---

## 6. Unity-MCP — Claude가 씬을 직접 조작함

**이 프로젝트는 Claude Code가 Unity 에디터에 직접 연결되어 있다.** `ai-game-developer` MCP 서버(로컬, `http://localhost:23269`)를 통해 GameObject 생성·컴포넌트 부착/수정·프리팹 생성·씬 저장·플레이모드 진입까지 Claude가 직접 수행한다. **더 이상 "에디터 작업은 사용자가, 코드는 Claude가" 원칙이 아님** — 오브젝트 배치·참조 연결도 Claude가 MCP 툴로 처리한다.

- 연결 설정: `.mcp.json` (프로젝트 루트), Unity 쪽 설정은 `UserSettings/AI-Game-Developer-Config.json` — Unity 에디터의 `Window > AI Game Developer` 창에서 Local/Cloud 모드 확인 가능.
- 새 세션에서 Unity MCP 툴이 안 보이면: Claude Code 재시작해야 `.mcp.json` 변경이 반영됨.
- Unity 에디터가 열려 있어야 로컬 서버가 뜬다. `mcp__ai-game-developer__scene-list-opened`로 연결 확인.

### 🚫 씬·에셋·플레이모드를 건드리기 전에 `unity-mcp` 스킬을 열 것

모달 API 호출(→ Unity 정지·MCP 사망) · 사용자 미저장 편집 파괴 · 검증 4계층 · 플레이모드 측정 함정 ·
MCP 툴 우회법 · SO 독립 파일 규칙이 **전부 거기 있다. 안 열고 손대면 Unity를 멈추거나 사용자 작업물을 날린다.**

원본은 키트(`prototyping-kit/skills/unity-mcp/`)에 있고 `~/.claude/skills/`에 정션으로 연결돼 있다 — 모든 프로토타입이 같은 파일을 본다.

### 🪝 이 프로젝트의 안전장치 (배선)

`.claude/settings.json`의 `PreToolUse` 훅(`.claude/hooks/block-unity-hazards.ps1`)이 `mcp__ai-game-developer__*` 호출을 검사해
**모달 API · `scene-open` · `console-get-logs` · Additive 없는 `OpenScene`**을 차단하고 대안을 알려준다.
차단되면 stderr 메시지를 읽고 그대로 따를 것. 탈출구는 인자에 `HOOK-OK` — **사용자 승인을 받은 뒤에만.**

---

## 6-1. 노션 · 칸반보드 → `kanban` 스킬

노션 연동 정보(허브 링크·툴 이름 함정)와 칸반보드 "POC 할일"의 운영 원칙·조회 절차는 **`kanban` 스킬**에 있다.
세션 시작(`brief` 스킬)·종료(`/wrap`)와 노션에서 뭔가 찾을 때 그 스킬을 열 것.

---

## 7. 세션 시작 루틴

**세션 첫 턴에 `brief` 스킬을 실행한다.** 절차(칸반 조회 → HANDOFF 읽기 → **코드 대조** → 일기 → 브리핑)와
각 단계의 확인 항목이 전부 거기 있다. 전부 읽기 전용이라 **사용자가 안 쳐도 알아서 돈다** (`/wrap`과 반대).

- 이 프로젝트의 일기 경로: `d:\unity\prototyping-kit\journal\`
- 칸반 조회 절차는 `kanban` 스킬, 씬 구조는 `SCENE_MAP.md`, 코드 구조는 `ARCHITECTURE.md`

### 세션 내내 유효한 규칙 (스킬이 아니라 여기 있는 이유: 시작할 때만 지키는 게 아니다)

- `GDD.md`는 `grep`으로 필요한 섹션만 조각내어 읽을 것 (`cat GDD.md` 금지)
- **밸런스 수치는 문서가 아니라 에셋이 정답** — `StageTable*`·`EnemyDefinition`·`Prog_*`·`BalanceConstants.cs`
- ⚠️ **"코드에 스위치가 있다" ≠ "게임에 그 기능이 있다".** 스킬트리 노드·SO·씬 배열처럼 **에셋이 켜야 도는 것**은
  코드만 보면 있는 것처럼 보인다. 트리거 자명 — *코드의 `case`·필드를 근거로 현황을 보고하거나 선택지를 만들 때,
  그 스위치를 켜는 **에셋을 `grep`으로 한 번** 확인한다.*
- 🔴 **문서·카드에 적힌 건 전부 낡는다 — 인용하기 전에 코드·에셋으로 1건 확인한다.** 네 갈래 다 틀린다:
  - **주장**("~해야 함" / `[x]` 완료 / 미체크). 미완료 주장도 완료 주장도 양쪽 다 틀린다. 체크박스는 **그 줄만** 말하고
    아래 딸린 리스트는 말하지 않으며, **사용자가 항목 뒤에 덧붙여 놓은 메모가 곧 결정**이다(체크박스는 안 바뀌어 있다).
  - **포인터**(`파일:라인`) — 그 줄을 열어 **이름이 뜻과 맞는지** 본다. 애초에 다른 심볼을 짚어놨을 수도 있다.
  - **해법**("이렇게 하면 된다") — 적을 당시 검증까지 된 게 아닐 수 있다. 관찰은 맞는데 처방이 추측인 경우가 있다.
  - **예시**("이때 이랬다") — 규칙이 맞다고 예시까지 맞는 게 아니다. 예시가 가리키는 파일을 열거나 한 번 묻는다.
- 🔴 **"아직 안 돼 있다"·"비어 있다"를 말하기 전에 그 에셋·파일을 한 번 연다.** 바로 위 규칙은 *문서·카드에 적힌 주장*만
  말해서 **내가 방금 지어낸 현황 주장**엔 발동을 안 한다 — 그쪽이 더 위험하다. 사용자는 그걸 근거로 남은 일을 센다.
  트리거 자명 — *남은 일·미구현을 열거하려는 순간.*
- 🔴 **설정 파일에 "왜 이렇게 했는지" 주석이 달려 있으면 그건 결정문이다 — 다시 꺼내지 말 것.**
  트리거 자명 — *`.gitignore`·설정에 걸린 것을 보고 "이거 커밋해야 하나?"라는 의문이 드는 순간.*
  그 자리의 주석과 칸반을 먼저 읽는다. 이미 난 결정을 다시 물으면 사용자는 같은 답을 두 번 하게 된다.
- ⚠️ **같은 사실의 사본을 두 곳에 두지 말 것.** 두면 반드시 한쪽만 낡고, **낡은 쪽을 근거로 말하게 된다.**
  트리거 자명 — *이미 어딘가 있는 표·목록을 다른 문서에 또 적으려는 순간.* **원본 한 곳만 두고 나머지는 포인터만 남긴다.**

---

## 8. grep 사용

**코드 파일 (.cs) 수정 시:**
- 수정 전 `grep`으로 관련 클래스/함수의 정확한 위치(라인) 먼저 파악
- 불필요한 전체 파일 cat 금지, 해당 라인만 Read

---

## 9. 세션 종료 — `/wrap`

세션을 마무리할 땐 **`/wrap`** 을 실행한다. 절차(이번 세션 정리 → 칸반 → HANDOFF → 일기 → 커밋)와
각 단계의 확인 항목이 전부 거기 있다.

**부수효과가 있어서 사용자가 칠 때만 도는 스킬이다** — Claude가 임의로 커밋하거나 노션 카드를 고치지 않는다.

- 이 프로젝트의 키트 경로: `d:\unity\prototyping-kit` (일기는 그 아래 `journal/`, 틀은 `templates/JOURNAL_ENTRY.md`)
- 저장소가 둘이다 — 키트를 고쳤으면 거기도 따로 커밋·푸시할 것.

## 10. 프로젝트 종료 루틴

플레이 빌드 배포 후 또는 프로토타입 중단 결정 후:

1. `RETROSPECTIVE.md` 작성 (틀: `templates\RETROSPECTIVE.md`)
   — **철저히 사용자 중심으로 작성.** "구현된 피쳐"·"기술 스택" 같은 사실 나열 섹션만 AI가 채우고, 나머지(What Went Well/Wrong·과정 개선안·게임 개선방안)는 작성 전 플레이테스트 여부/결과부터 묻고 섹션별로 사용자 입장을 질문해서 그 답변으로 채운다. AI가 짚고 싶은 이슈는 먼저 언급하고 사용자 동의를 받아 반영한다.
   — What Went Well/Wrong은 **워크플로 수준**으로. 코드 버그 목록 아님.
   — 과정 개선안 중 Kit에 반영할 것은 **[→ Kit]** 표시 후 실제 반영
2. 노션 등 외부 도구에 공유 (선택, 확정 후에)
3. 다음 프로토타입 SESSION_ZERO 작성 시 이 회고 먼저 읽을 것
