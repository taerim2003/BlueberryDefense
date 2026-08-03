# CLAUDE.md

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- **UI·연출을 만들 땐 "어느 화면의 어디에 붙는가"를 기능만큼 중요한 결정으로 취급할 것.** 요청에 화면이 안 적혀 있으면 **코드를 쓰기 전에 묻는다**. 세션26에 "화면 우측 하단 원티드 포스터"만 보고 맵 선택 화면에 만들었다가 통째로 옮겼다(정답은 스킬트리 창). 기능 스펙은 갈림길마다 물어놓고 배치만 추측한 게 사고였다.
- **사용자 아트를 반영할 땐 파일 수정 시각부터 확인할 것.** 여러 번 다시 그리는 워크플로에서 "내가 배선한 그림"과 "지금 디스크에 있는 그림"이 쉽게 어긋난다(세션26: 애니 작업 1시간 전의 낡은 스케치를 대기 프레임으로 쓰고 있었다).
- 선택지를 낼 땐 **손잡이 이름(`ambushCount` 같은 필드명)이 아니라 화면에서 벌어지는 일로** 먼저 한 줄 설명할 것. 사용자는 코드 필드로 생각하지 않는다 — 세션25에 "게릴라를 늘린다"를 서로 다른 뜻으로 쓰다 선택지 전체가 헛다리를 짚었다(정답은 내가 낸 3안 밖에 있었다).
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
- 새 세션에서 Unity MCP 툴이 안 보이면: Claude Code 재시작해야 `.mcp.json` 변경이 반영됨.
- Unity 에디터가 열려 있어야 로컬 서버가 뜬다. `mcp__ai-game-developer__scene-list-opened`로 연결 확인.

### 🚫 절대 호출 금지 — 모달을 띄우는 API
**모달 다이얼로그가 뜨면 Unity가 멈추고 MCP 연결이 통째로 죽는다.** 사용자가 직접 창을 눌러줄 때까지 아무것도 못 한다(세션20에 5분 이상 날림).
- `EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()` → **`EditorSceneManager.SaveScene(scene)`을 쓸 것**
- `EditorUtility.DisplayDialog` / `DisplayDialogComplex` / `OpenFilePanel` 계열

### 🔒 사용자 작업물을 파괴하지 말 것
에디터는 사용자와 **공유**한다. 미저장 편집을 날리면 git에도 없어 복구가 안 된다(세션15에 실제로 날렸다).
- 씬을 **읽기만 하면 additive로 열고 저장 없이 닫는다.** `OpenScene(..., Single)`은 사용자가 보던 씬을 갈아치운다. Single로 열어야 하면 **저장 여부부터 묻는다**(dirty면 확인 모달 → 위 "MCP 사망"까지 겹친다).
- 사람이 편집하는 에셋(SO·씬)은 **손대기 직전에 다시 읽는다.** 읽은 시점과 판단 시점이 벌어지면 감사·대조 결과가 통째로 틀어진다.
- **커밋 직전 `git status`의 모든 줄을 설명할 수 있어야 한다.** 설명 못 하는 줄이 사고다(내 컴파일 요청이 사용자의 미저장 편집을 디스크로 밀어낸 적 있음).

### 검증은 위에서부터 — 아래로 갈수록 비싸다
① **에디트모드 리플렉션 테스트**(순수 로직. `new GameObject().AddComponent<T>()`는 Awake가 안 돌아 프리팹 참조 없이도 된다 — 조용한 실패가 없어 가장 확실) → ② **`script-execute` 반환값 대조**(에셋 값·커브. 손계산 말고 게임이 실제로 쓰는 경로로) → ③ **플레이모드 스모크**(연출·물리처럼 정말 실행이 필요할 때만) → ④ **사용자에게 물어보기**(버튼 하나 눌러보면 되는 UI 동작은 "눌러보고 알려줘"가 더 빠르고 정확).
⚠️ **에디트모드 `Instantiate`는 Awake를 안 돈다** — 런타임 필드를 에디트모드에서 읽어 검증하려 들지 말 것.
⚠️ **검산 스크립트를 새로 쓰면 대조군부터 돌린다** — 고치기 *전* 데이터처럼 **위반이 나와야 정상인 입력**에 먼저 돌려 실제로 실패가 찍히는지 볼 것. "전부 통과"는 검증이 아니라 **의심 신호**다. (세션25: PowerShell이 변수 대소문자를 안 가려 루프의 `$warn`이 상수 `$WARN`을 덮어 검사가 통째로 무력화됐다. before/after를 둘 다 돌린 덕에 걸렸다.)

### 플레이모드로 측정할 때 체크리스트
1. **시간이 흐르는가** — `Time.timeScale`·`Time.time` 확인. **게임오버·모달이면 `timeScale=0`**이라 `Time.deltaTime` 기반 값이 마지막 값에 굳는다. 이걸 버그로 오판한 적 있음(세션20).
2. **새 코드가 컴파일됐는가** — **플레이 중엔 스크립트가 컴파일되지 않는다.** 코드 수정 → 플레이 종료 → `EditorApplication.isCompiling == false` 확인 → 재진입.
3. **한 프레임에 판정 가능한가** — `script-execute`는 호출마다 **독립 어셈블리**라 static으로 프레임 간 상태를 못 넘긴다. 시계열 샘플링보다 **"어기면 반드시 벗어나는 불변식"**을 세워 한 번에 판정하는 쪽이 낫다.
4. **씬을 임시로 고쳤으면 되돌리고 `git status`로 확인**할 것.
5. **화면을 눈으로 판정하기 전에 대조 실험을 한 번 넣는다** — 색을 불투명으로 바꿔 재촬영, 값을 극단으로 밀어보기. 스크린샷만 보고 "안 그려진다"고 판단했다가 **처음부터 정상이었던** 적이 있다(세션21). 로그로 찍은 상태값이 전부 정상이면 코드가 아니라 관측이 틀린 것이다.

### 에셋을 코드로 만들면 되읽어서 검증할 것
`SpriteRenderer.sprite` 직접 대입이 **조용히 무시된** 적 있다(세션20 — 다른 필드는 다 들어갔는데 스프라이트만 안 들어감). 생성 직후 `AssetDatabase.LoadAssetAtPath`로 되읽어 로그를 찍으면 잡힌다. 프리팹 수정은 `PrefabUtility.LoadPrefabContents` + `SerializedObject`가 가장 확실하다.

### 🪝 위험 호출은 훅이 막는다
`.claude/settings.json`의 `PreToolUse` 훅(`.claude/hooks/block-unity-hazards.ps1`)이 `mcp__ai-game-developer__*` 호출을 검사해 **모달 API · `scene-open` · `console-get-logs` · Additive 없는 `OpenScene`**을 차단하고 대안을 알려준다. 차단되면 stderr 메시지를 읽고 그대로 따를 것.
- 정말 필요하면 인자 어딘가에 `HOOK-OK`를 넣어 통과시킬 수 있다. **사용자 승인을 받은 뒤에만.**
- 훅이 안 도는 것 같으면(위험 호출이 그냥 통과) 스크립트를 직접 실행해 확인할 것 — 훅은 조용히 실패한다. 스크립트는 **ASCII 전용**(no-BOM PowerShell 5.1이 한글을 깨뜨림).

### 🧰 못 미더운 MCP 툴 — 우회법
> `scene-open`·`console-get-logs`는 훅이 막고 대안까지 알려준다(세션25에 실제 차단 확인). 아래는 훅이 안 잡는 것들.
- **씬의 컴포넌트를 코드로 찾기 전에 `SCENE_MAP.md`부터 볼 것.** "프리팹이겠지"라고 넘겨짚고 프리팹 전수 검색을 짰다가 헛돈 적 있다(세션26 — 플레이어는 프리팹이 아니라 **SampleScene의 씬 오브젝트**다. 지도에 적혀 있었다).
- **`gameobject-duplicate`** — 반환값이 원본을 가리킨다. → 복제 후 **부모를 재조회**해 `"(N)"` 접미사로 찾기.
- **`script-execute`** — 관련 동작은 한 호출에 몰되(중간 도메인 리로드로 상태 리셋), **플레이모드 상태 전이만은 한 호출에 하나씩**. 문자열에 이스케이프 따옴표(`\"`) 금지(`"a" + var + "b"`로).

### 📄 씬·프리팹·에셋에 직렬화되는 클래스는 독립 파일로
`MonoBehaviour`·`ScriptableObject`는 **반드시 파일명 = 클래스명인 자기 파일**에 둔다. 다른 .cs에 곁다리로 넣으면 `m_Script: {fileID: 0}`이 되어 **에디터에선 멀쩡한데 빌드에서만** 컴포넌트가 안 붙거나 SO가 null로 로드된다(빌드 데미지 숫자 "0" · 빌드에서 전 스테이지 동일 — 둘 다 이것). 런타임 `AddComponent` 전용이면 합쳐도 된다.
→ **에디터에서 재현이 안 되는 버그는 추측하지 말고 `Player.log`부터.**

---

## 6-1. 노션(Notion) 연동

이 프로젝트의 원본 기획은 Notion에도 있음 — GDD.md/SESSION_ZERO.md는 여기서 옮겨 적은 것.

- **허브: "블루베리 디펜스 POC 문서"** — https://app.notion.com/p/34a6394cd983801991e3cb457c798a0a (`34a6394c-d983-8019-91e3-cb457c798a0a`)
  일정·칸반보드(§6-2)·기획 문서 링크가 전부 여기 달려 있다. **노션에서 뭘 찾든 여기서 출발할 것.**
- 기획 페이지: "🍓 블루베리 디펜스" (위 허브의 "프로토타이핑 문서" 링크) — https://app.notion.com/p/3956394cd98380aba9abf02072b96d6c
- 🔴 **툴은 `mcp__notion__API-*` 하나뿐이다.** `mcp__claude_ai_Notion__*`(=`notion-search`/`notion-fetch`)는 **이 프로젝트에 없다** —
  claude.ai 커넥터 쪽은 별도 OAuth가 필요해 비대화형 세션에서 인증할 수 없다. 이름을 착각해 호출하면 첫 단계에서 막힌다.
- GDD.md와 내용이 어긋나면 — 원본은 Notion이지만, 코딩 중 결정 사항은 GDD.md/SESSION_ZERO.md를 우선 신뢰할 것 (Notion은 초기 기획, 로컬 문서가 최신 결정 반영).

---

## 6-2. 노션 칸반보드 "POC 할일" — 일정·할 일의 단일 원본

### 📌 원칙 (깨지 말 것)

1. **일정과 할 일의 원본은 칸반보드다.** `HANDOFF.md`의 "다음 할 일"은 그 **미러**일 뿐이다.
2. **HANDOFF는 늘 칸반과 일치해 있어야 한다.** 어긋난 걸 발견하면 **그 자리에서** HANDOFF를 맞춘다 — "나중에 정리"는 없다.
3. 둘이 어긋나면 이기는 쪽은 **항상 칸반**. HANDOFF를 근거로 칸반을 고치지 말 것(반대 방향만 허용).
4. **일정을 확인·보고할 때 근거는 칸반이다.** 기억이나 HANDOFF 사본으로 답하지 말고 **매번 조회해서** 답한다.
5. **보드는 사용자와 공동 관리한다.**
   - 사용자는 **세션과 무관하게 아무 때나** 카드를 올린다 → 세션 시작에 반드시 다시 조회(§7-0-1). 세션이 길어지면 중간에도 한 번 더 볼 것.
   - Claude도 관리 주체다. 새 할 일이 생기면 **카드를 만들고**, 착수하면 `진행 중`, 끝나면 `완료`로 옮긴다(§9-1). 사용자가 올린 카드만 기다리지 말 것.

### 🧊 콘텐츠 동결 원칙

POC는 7/31에 끝났고 **게임 구성 변경은 원칙적으로 하지 않는다.** 지금 보드에 있는 콘텐츠성 카드
(새 캐릭터 · 농장맵 확장 · 맵 해금)는 **POC 플레이 피드백을 반영하는 마지막 예외**다.
→ **이후 콘텐츠 추가·변경은 기본적으로 안 하는 것으로 본다.** 하게 된다면 **반드시 칸반 카드로 올라온 것만** —
   Claude가 먼저 "이것도 넣을까요"를 제안하지 말 것. 폴리싱(양념)이 기본값이다.

### 보드 정보

- DB: "POC 할일" — https://app.notion.com/p/3b06394cd983803daf4ac26f924121b7
  - `database_id` = `3b06394c-d983-803d-af4a-c26f924121b7` (부모 페이지 "블루베리 디펜스 POC 문서" = `34a6394c-d983-8019-91e3-cb457c798a0a`)
- 속성: **`상태`**(status: `시작 전`/`진행 중`/`완료`) · `담당자`(people) · `이름`(title)
- **세션 시작에 조회(§7-0-1), 세션 종료에 정리(§9-1).** 끝난 카드는 `완료`로 옮긴다 — 안 옮기면 다음 세션이 끝난 일을 또 한다.

### 조회 방법 — **2단계 고정. 딴 길로 새지 말 것**

```
① API-post-search   filter={"property":"object","value":"page"}
                    sort={"direction":"descending","timestamp":"last_edited_time"}
                    page_size=100
   → 150KB라 한도를 넘고 파일로 떨어진다. 본문은 안 읽어도 된다(그래서 토큰이 거의 안 든다).
② powershell -File .claude/scripts/notion-kanban.ps1 [-Ids]
   → 상태별로 정리된 카드 목록. `-Ids`는 patch/본문조회에 쓸 page_id까지.
```

- 🔴 **왜 이 우회로인가**: MCP 서버가 **구버전 Notion API**를 말하는데 노출된 툴셋은 **신버전(data source 기반)** 이다.
  그래서 `API-query-data-source`·`API-retrieve-a-data-source`는 **항상 400 `invalid_request_url`**, 구버전용 `databases/{id}/query` 툴은 아예 없다.
  (판별법: `API-retrieve-a-database`가 `data_sources` 배열 대신 `properties`를 직접 돌려주면 구버전이다.)
  **서버 설정이 고쳐지면 이 절차는 통째로 폐기하고 `query-data-source` 한 방으로 돌아갈 것.**
- ⚠️ **스크립트는 저장된 덤프 여러 개를 카드 id로 병합한다.** "가장 최신 파일 하나"를 믿으면 안 된다 —
  더 좁은 검색이나 **병렬 세션**이 남긴 부분 덤프가 더 최신일 수 있다(실제로 5장짜리 보드가 2장으로 보인 적 있음).
- ⚠️ **`(older dump)`로 표시된 카드는 십중팔구 삭제된 것**이다(검색은 휴지통 페이지를 안 준다).
  실제 할 일로 취급하기 전에 `API-retrieve-a-page`로 `in_trash`를 확인할 것.
- 스크립트는 **UTF-8 BOM 필수**(PS 5.1이 BOM 없으면 한글 상태명을 깨뜨림). 헤더에 복구 명령이 적혀 있다.
- 카드 본문은 `API-retrieve-page-markdown`(page_id). **카드 대부분이 회의록 요약**이라 액션 아이템 체크박스가 본문에 들어 있다.
- 상태 변경은 `API-patch-page` — `properties: {"상태": {"status": {"name": "완료"}}}`.
- 💡 **토큰이 아까우면 줄일 곳은 검색이 아니라 툴 스키마다.** Notion 툴 스키마 하나가 공통 `$defs` 2KB를 끌고 온다.
  `ToolSearch`로 필요한 것만 골라 부를 것 — 매번 `select:API-post-search,API-retrieve-page-markdown,API-patch-page` 정도면 충분하다.

### 카드를 HANDOFF로 옮길 때
**카드에 적힌 "~해야 함"을 그대로 베끼지 말 것.** 이미 구현됐던 적이 여러 번 있다(§7 경고와 같은 함정).
카드 1건당 **코드 1곳을 grep해 미구현임을 확인**한 뒤 옮기고, 확인한 파일:라인을 HANDOFF에 같이 적는다.

---

## 7. 세션 시작 루틴

0. **코딩 첫 세션이면**: `SESSION_ZERO.md` 완료 여부 확인. 미완성이면 코딩 전에 채울 것.
0-1. **노션 칸반보드 "POC 할일" 조회** — 일정·할 일의 **원본**이다(§6-2). 건너뛰지 말 것.
   - `시작 전`/`진행 중` 카드를 훑고, 이미 끝난 게 있으면 `완료`로 옮긴다.
   - **사용자가 세션 사이에 카드를 새로 올렸다고 전제할 것.** 새 카드는 코드로 미구현을 확인한 뒤 `HANDOFF.md`에 추가한다.
   - HANDOFF와 어긋나면 **칸반이 이긴다** — 브리핑하기 전에 HANDOFF를 먼저 맞춰 놓을 것.
1. `HANDOFF.md` 읽어서 현재 상태 파악
2. 코드 작업이 예상되면 `ARCHITECTURE.md` 함께 읽어 구조 파악 (폴더 책임·매니저 호출관계·"X 추가하려면 어디 손대나" 표)
3. `GDD.md`는 `grep`으로 필요한 섹션만 조각내어 읽을 것 (`cat GDD.md` 금지)
4. 씬 작업이 예상되면 Unity-MCP 툴(`mcp__ai-game-developer__*`)이 로드됐는지 확인. 안 보이면 사용자에게 Claude Code 재시작 요청 (§6 참고). **씬 구조는 `SCENE_MAP.md` 먼저 읽어 파악**(오브젝트 위치·배선·"X 씬에 추가하려면 어디"). 밸런스 수치는 문서가 아니라 **에셋이 정답** — `StageTable*`·`EnemyDefinition`·`Prog_*`·`BalanceConstants.cs`
5. (선택) 최근 일기 `d:\unity\prototyping-kit\journal\` 의 마지막 1~2편 훑어 과정상 미해결 마찰 확인
6. 한 줄 브리핑 후 사용자에게 다음 목표 확인 — 이때 **HANDOFF의 미해결 목록을 같이 훑어 이번 세션 범위를 확정**할 것. 인계 문서에 적힌 항목이 자동으로 작업 범위가 되지는 않아서, 짚지 않으면 통째로 빠뜨린다.
   ⚠️ **HANDOFF는 할 일의 목록이지 코드의 진실이 아니다.** 거기 적힌 "~해야 함"을 사용자에게 말하기 전에 **코드로 1건이라도 확인**할 것 — 이미 해결됐거나 필드명이 틀렸던 적이 여러 번 있다.

---

## 8. grep 사용

**코드 파일 (.cs) 수정 시:**
- 수정 전 `grep`으로 관련 클래스/함수의 정확한 위치(라인) 먼저 파악
- 불필요한 전체 파일 cat 금지, 해당 라인만 Read

---

## 9. 세션 종료 시 반드시 할 것

1. **노션 칸반보드 정리** (§6-2) — 이번 세션에 끝낸 카드를 `완료`로, 손대기 시작한 카드를 `진행 중`으로 옮긴다. 새로 생긴 할 일은 카드로 만든다.
   ⚠️ **"완료"는 코드로 확인한 것만.** 커밋했다고 완료가 아니라 동작을 확인한 것만 옮긴다. 애매하면 `진행 중`으로 두고 남은 것을 카드 본문에 적을 것.
2. `HANDOFF.md` 업데이트 (빌드 상태 / 미해결 이슈 / 다음 할 일) — 칸반을 정리한 **뒤에** 그 결과를 반영한다.
3. **세션 일기 작성** → `d:\unity\prototyping-kit\journal\YYYY-MM-DD.md`
   (틀: `templates\JOURNAL_ENTRY.md`. 게임이 아니라 *과정*의 회고. 하루 두 번째 세션이면 같은 파일에 `## 세션 N` 추가)
4. git commit + push (변경 파일 전체 스테이징, origin main)

## 10. 프로젝트 종료 루틴

플레이 빌드 배포 후 또는 프로토타입 중단 결정 후:

1. `RETROSPECTIVE.md` 작성 (틀: `templates\RETROSPECTIVE.md`)
   — **철저히 사용자 중심으로 작성.** "구현된 피쳐"·"기술 스택" 같은 사실 나열 섹션만 AI가 채우고, 나머지(What Went Well/Wrong·과정 개선안·게임 개선방안)는 작성 전 플레이테스트 여부/결과부터 묻고 섹션별로 사용자 입장을 질문해서 그 답변으로 채운다. AI가 짚고 싶은 이슈는 먼저 언급하고 사용자 동의를 받아 반영한다.
   — What Went Well/Wrong은 **워크플로 수준**으로. 코드 버그 목록 아님.
   — 과정 개선안 중 Kit에 반영할 것은 **[→ Kit]** 표시 후 실제 반영
2. 노션 등 외부 도구에 공유 (선택, 확정 후에)
3. 다음 프로토타입 SESSION_ZERO 작성 시 이 회고 먼저 읽을 것
