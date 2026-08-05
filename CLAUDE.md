# CLAUDE.md

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- **코드를 고치기 전에 "무엇을 어디에 손대는지"를 한 줄로 선언할 것.** 묻는 게 아니라 **선언**이다 — 답을 기다리지 말고 바로 진행하고, 틀렸을 때만 사용자가 끊게 한다.
  **파일·화면·오브젝트 이름을 대야 틀린 게 드러난다**("우측 하단에 붙일게요"는 안 드러나고 "`SkillTreeUI` 우측 하단"은 드러난다).
  사고는 늘 *갈림길로 안 보이는 갈림길*에서 났다 — "새로운 맵"(세션20) · "벽 벽 애들"(세션21) · "화면 우측 하단 원티드 포스터"(세션26, 맵 선택 화면에 만들었다가 통째로 옮김. 정답은 스킬트리 창).
  전부 **대상이 하나뿐인 줄 알았는데 여럿**이었다. 기능 스펙은 갈림길마다 물어놓고 배치만 추측한 게 사고였다.
  특히 **피드백 왕복 중에 잘 난다** — 일기 34편 집계상 계획된 작업은 14%, 즉흥 왕복은 44%.
  → **UI 코드를 고치기 전에 그 코드가 그리는 화면을 전부 열거할 것.** `LevelUpUI` 하나가 **3택 레벨업 · 2택 갈림길 · 진화 대상 선택** 셋을 그린다.
  열거하면 "어느 화면인가"가 저절로 질문이 된다 — 세션28에 안 세고 "선택지 UI"를 전부로 읽어 **씬의 카드 3장을 고쳐 저장했다가 통째로 되돌렸다**(반대 방향 사고: 하나인데 여럿으로 읽음).
- **"넣어줘"를 받으면 넣을 곳이 아니라 *이미 있는 곳*부터 센다.** 위 열거 규칙은 "이 코드가 뭘 그리나"만 묻고 "거기 이미 뭐가 걸려 있나"는 안 묻는다 — 그래서 **중복으로 얹게 된다.**
  세션31에 "UI에 애니메이션 넣어줘"를 전부 새로 넣을 뻔했는데, 사용자가 *"체력바 같은거 지금 들어가있지 않아?"* 라고 짚어줘서 grep했더니 HUD·스킬트리·레벨업에 **이미 DOTween 연출이 여럿**이었다.
  → 기능을 추가하기 전에 **그 기능의 기존 구현을 `grep`으로 먼저 센다**(연출이면 `DOTween|DOScale|Punch`, 저장이면 `PlayerPrefs`…). 있으면 **겹치는지·경합하는지**부터 보고할 것 — 실제로 두 곳(`TreasureRewardDecor`·진화 노드 무한 펄스)은 얹으면 싸우는 자리였다.
- **사용자 아트를 반영할 땐 파일 수정 시각부터 확인할 것.** 여러 번 다시 그리는 워크플로에서 "내가 배선한 그림"과 "지금 디스크에 있는 그림"이 쉽게 어긋난다(세션26: 애니 작업 1시간 전의 낡은 스케치를 대기 프레임으로 쓰고 있었다).
- 선택지를 낼 땐 **손잡이 이름(`ambushCount` 같은 필드명)이 아니라 화면에서 벌어지는 일로** 먼저 한 줄 설명할 것. 사용자는 코드 필드로 생각하지 않는다 — 세션25에 "게릴라를 늘린다"를 서로 다른 뜻으로 쓰다 선택지 전체가 헛다리를 짚었다(정답은 내가 낸 3안 밖에 있었다).
- If a simpler approach exists, say so. Push back when warranted.
- **대안으로 도구·기능을 꺼낼 땐, 꺼내는 그 자리에서 그 문서를 연다.** "X로도 됩니다"라고 **쓰기 전에** 여는 것이지, 사용자가 "X가 뭔데?"라고 물은 뒤가 아니다.
  세션29에 `/schedule`을 안 열고 대안으로 밀었는데 **클라우드 실행이라 로컬 파일을 못 읽었다** — 결론이 통째로 뒤집혔다.
- If something is unclear, stop. Name what's confusing. Ask.
- **메모리·문서에는 규칙만 적고, 변하는 것(현재 값·구현 상태·프리팹 목록)은 "어디서 확인하는지"만 적을 것.** 상태를 박아두면 낡아서 다음 세션이 틀린 걸 사실로 읊는다. 세션27에 메모리 3건이 그렇게 낡아 있었고, 그중 하나는 **다른 메모리가 "틀렸다"고 기록해 둔 주장**을 그대로 갖고 있었다(둘 다 매 세션 주입된다). 일하는 방식 규칙(`feedback`)은 24일이 지나도 안 낡았다 — 차이는 그 시점 상태가 섞였는지뿐이다.

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
- ⚠️ **HANDOFF는 할 일의 목록이지 코드의 진실이 아니다.** 거기 적힌 "~해야 함"을 사용자에게 말하기 전에 **코드로 확인**할 것 — 이미 해결됐거나 필드명이 틀렸던 적이 여러 번 있다.

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
