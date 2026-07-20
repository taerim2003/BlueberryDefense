# CLAUDE.md

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

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
- 새 세션에서 Unity MCP 툴이 안 보이면: Claude Code 세션을 재시작해야 `.mcp.json` 변경이 반영됨.
- Unity 에디터가 열려 있어야 로컬 서버가 뜬다. `mcp__ai-game-developer__scene-list-opened`로 연결 확인.

---

## 6-1. 노션(Notion) 연동

이 프로젝트의 원본 기획은 Notion에도 있음 — GDD.md/SESSION_ZERO.md는 여기서 옮겨 적은 것.

- 페이지: "🍓 블루베리 디펜스" (Notion 워크스페이스 "천진난만배 게임잼 챌린지" > "Prototyping" 데이터소스 하위)
- URL: https://app.notion.com/p/3956394cd98380aba9abf02072b96d6c
- Notion MCP(`mcp__claude_ai_Notion__*`)가 연결되어 있어 `notion-search`/`notion-fetch`로 직접 조회 가능. GDD.md와 내용이 어긋나면 — 원본은 Notion이지만, 코딩 중 결정 사항은 GDD.md/SESSION_ZERO.md를 우선 신뢰할 것 (Notion은 초기 기획, 로컬 문서가 최신 결정 반영).

---

## 7. 세션 시작 루틴

0. **코딩 첫 세션이면**: `SESSION_ZERO.md` 완료 여부 확인. 미완성이면 코딩 전에 채울 것.
1. `HANDOFF.md` 읽어서 현재 상태 파악
2. 코드 작업이 예상되면 `ARCHITECTURE.md` 함께 읽어 구조 파악 (폴더 책임·매니저 호출관계·"X 추가하려면 어디 손대나" 표)
3. `GDD.md`는 `grep`으로 필요한 섹션만 조각내어 읽을 것 (`cat GDD.md` 금지)
4. 씬 작업이 예상되면 Unity-MCP 툴(`mcp__ai-game-developer__*`)이 로드됐는지 확인. 안 보이면 사용자에게 Claude Code 재시작 요청 (§6 참고). **씬 구조는 `SCENE_MAP.md` 먼저 읽어 파악**(오브젝트 위치·배선·"X 씬에 추가하려면 어디"). 밸런스 수치 작업이면 `BALANCE_MAP.md`도 참고
5. (선택) 최근 일기 `d:\unity\prototyping-kit\journal\` 의 마지막 1~2편 훑어 과정상 미해결 마찰 확인
6. 한 줄 브리핑 후 사용자에게 다음 목표 확인

---

## 8. grep 사용

**코드 파일 (.cs) 수정 시:**
- 수정 전 `grep`으로 관련 클래스/함수의 정확한 위치(라인) 먼저 파악
- 불필요한 전체 파일 cat 금지, 해당 라인만 Read

---

## 9. 세션 종료 시 반드시 할 것

1. `HANDOFF.md` 업데이트 (빌드 상태 / 미해결 이슈 / 다음 할 일)
2. **세션 일기 작성** → `d:\unity\prototyping-kit\journal\YYYY-MM-DD.md`
   (틀: `templates\JOURNAL_ENTRY.md`. 게임이 아니라 *과정*의 회고. 하루 두 번째 세션이면 같은 파일에 `## 세션 N` 추가)
3. git commit + push (변경 파일 전체 스테이징, origin main)

## 10. 프로젝트 종료 루틴

플레이 빌드 배포 후 또는 프로토타입 중단 결정 후:

1. `RETROSPECTIVE.md` 작성 (틀: `templates\RETROSPECTIVE.md`)
   — **철저히 사용자 중심으로 작성.** "구현된 피쳐"·"기술 스택" 같은 사실 나열 섹션만 AI가 채우고, 나머지(What Went Well/Wrong·과정 개선안·게임 개선방안)는 작성 전 플레이테스트 여부/결과부터 묻고 섹션별로 사용자 입장을 질문해서 그 답변으로 채운다. AI가 짚고 싶은 이슈는 먼저 언급하고 사용자 동의를 받아 반영한다.
   — What Went Well/Wrong은 **워크플로 수준**으로. 코드 버그 목록 아님.
   — 과정 개선안 중 Kit에 반영할 것은 **[→ Kit]** 표시 후 실제 반영
2. 노션 등 외부 도구에 공유 (선택, 확정 후에)
3. 다음 프로토타입 SESSION_ZERO 작성 시 이 회고 먼저 읽을 것
