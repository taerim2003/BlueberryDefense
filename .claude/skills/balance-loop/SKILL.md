---
name: balance-loop
description: 봇 플레이테스트 밸런싱 루프의 한 틱 절차 — 종료 조건 확인 → 봇 세션 상태 확인 → 측정 끝나면 분석·가설 판정 → 다음 밸런스 변경(필수) → 보고서 아티팩트 재배포 → 다음 세션 요청. `/loop /balance-loop [최대이터레이션] [마감HH:MM]`으로 부른다. 판단 기준은 전부 balance 스킬.
---

# 밸런싱 루프 — 한 틱

> 🔴 **첫 동작: `balance` 스킬을 연다**(이 컨텍스트에 이미 열려 있지 않다면). 목표·손잡이 선택 규칙·판정 기준은 전부 거기 있다.
> 🔴 **대화하지 않는다. 질문으로 멈추지 않는다**(CLAUDE.md §1). 갈림길은 이터레이션 파일의 `assumptions`에 적고 진행한다.
> 🔴 **커밋·푸시·HANDOFF·노션·게임 로직 코드 수정 금지.** 바꾸는 것은 `KNOBS.md`의 데이터 에셋뿐이다.

## 상태 파일 (컨텍스트가 압축돼도 이것만 읽으면 이어진다)

`BotRuns/loop_state.json`
```json
{ "iteration": 3, "maxIterations": 6, "deadlineLocal": "2026-09-19T07:00", "phase": "campaign|probe|audit",
  "final": false, "reportUrl": "https://claude.ai/...", "note": "" }
```

`BotRuns/iterations/NN.json` — **이터레이션 N = 밸런스 상태 N.** 파일 하나가 "이 상태를 만든 변경 + 그 상태의 측정 + 판정"이다.
```json
{ "iteration": 3,
  "sessions": ["20260919-011200_it03-campaign", "20260919-013900_it03-probe"],
  "target": "G2 — 해변 보통 ≤ 우주 쉬움 관계 미달",
  "symptom": "해변2 D가 우주1보다 0.09 높음. 사망이 해변2 16~20스테이지 콩콩이에 몰림(캐릭터 무관)",
  "axisAnalysis": [ { "axis": "A", "evidence": "…숫자…", "score": 4 }, { "axis": "B", "evidence": "…", "score": 2 },
                    { "axis": "C", "evidence": "…", "score": 1 }, { "axis": "D", "evidence": "…", "score": 1 }, { "axis": "E", "evidence": "…", "score": 1 } ],
  "chosenAxis": "A",
  "rejected": [ { "axis": "B", "reason": "콩콩이 사망 비중이 빌드와 무관" } ],
  "hypothesis": "해변 16~20스테이지 hopperChance −0.15 → 해변2 D −0.05, 해변2 ≤ 우주1 통과",
  "changes": [ { "file": "Assets/Data/StageTable_Coast.asset", "field": "stages[15..19].hopperChance", "before": 0.6, "after": 0.45 } ],
  "prediction": [ { "metric": "probe D 해변 보통", "direction": "down", "expectedDelta": -0.05 } ],
  "passCriterion": "해변2 D ≤ 우주1 D + 0.02 이고 다른 관계가 새로 깨지지 않음",
  "verdict": "confirmed|refuted|inconclusive|pending", "verdictNote": "…숫자 근거…",
  "nextToVerify": "…", "codeSuggestions": [], "assumptions": [] }
```
- 0회차(기준선)는 `changes`가 비어 있다. **1회차부터는 `changes`가 반드시 1곳 이상**이다(사용자 결정).
- 반박된 변경을 되돌릴 때도 되돌림 자체를 `changes`에 적고 새 가설을 함께 둔다.

## 틱 절차

### 0. 초기화 (loop_state.json이 없을 때만)
- 인자 `[최대이터레이션=4] [마감=07:00]`로 `loop_state.json`을 만든다(`iteration: 0`, `phase: "audit"`).
  `BotRuns/report_url.txt`가 있으면 그 주소를 `reportUrl`에 넣는다 — 새 보고서를 만들지 않고 기존 보고서를 이어 쓴다.
- `iterations/00.json`을 `{ "iteration": 0, "sessions": [], "verdict": null }`로 만든다.
- 이미 있으면 **이어서** 한다(지난밤 기록 위에 쌓는다 — 보고서 URL도 그대로).

### 1. 종료 조건 — 무엇보다 먼저
- `iteration > maxIterations` 이거나 **지금 시각 ≥ 마감**이면: 3단계(분석·판정)를 한 번 돌리고 **`loop_state.final = true`로 바꾼 뒤** 보고서를 재배포 → `ScheduleWakeup stop`.
  - 🔴 `final: true`면 보고서 맨 위에 **"루프 종합"** 절이 열린다(사용자 요청 2026-09-18 — 사용자가 읽는 용).
    ① 목표별 변화(기준선 → 지금) ② 난이도 D 추이 9개 목표 × 이터레이션 ③ **이터레이션별로 한 일**(겨냥·변경 수·바꾼 파일·판정) ④ 남은 문제.
    전부 `data.json`에서 자동으로 만든다 — **손으로 쓴 요약을 넣지 말 것**(다음 루프에 낡는다).
    남은 문제는 미달 목표 + `insights`의 `status: "blocker"` 항목에서 온다. 그래서 **멈추기 전에 `insights`를 최신으로 만들어야** ④가 제대로 찬다.
  - 종료 직전 순서: 분석·판정 → `insights` 갱신(§4-B) → `final: true` → `render_check` → 배포 → stop.
- 마감은 기억이 아니라 시각으로 판정한다. 컨텍스트가 날아가도 이것만은 성립한다.

### 2. 세션 상태
- 🚦 **`BotRuns/yield`가 있으면 다른 세션이 Unity를 쓰는 중이다 — 아무것도 요청하지 말고 10~20분 뒤 다시 깬다.**
  봇은 **양보하는 쪽**이다(CLAUDE.md §6-2 — 다른 세션의 작업이 테스트 우선순위가 높다). 이 대기는 반복 횟수·마감을 소모하지 않는 대기가 아니다 — 마감은 그대로 흐른다.
- `BotRuns/request.json` 또는 `BotRuns/active/session.json`이 있으면 **측정 중**이다. 가장 최근 세션 폴더의 `status.json`을 읽는다.
  - `running`이고 `heartbeatUtc`가 5분 이내 → 남은 판 수를 보고 **10~20분 뒤** 다시 깬다(`ScheduleWakeup`).
  - `heartbeatUtc`가 5분 넘게 멈춤 → **Unity 메인 스레드가 막힌 것이다**(2026-09-18에 두 번. BotPilot 안쪽 감시는 `Update()`에 있어서 원리상 못 잡는다).
    → **감시자가 도는지 먼저 본다**: `Tools/BotPlaytest/watchdog.ps1`이 살아 있으면 덤프를 남기고 Unity를 죽였다 되살려 이어 돌린다 — 루프는 멈추지 말고 20분 뒤 다시 깬다.
    감시자가 안 돌고 있으면 `loop_state.note`에 적고 보고서를 재배포한 뒤 **루프를 멈춘다**(루프가 직접 재시작하지 않는다 — 그건 감시자 몫).
    무인 실행을 시작할 땐 감시자를 먼저 띄운다: `powershell -ExecutionPolicy Bypass -File Tools\BotPlaytest\watchdog.ps1 -Deadline <마감HH:MM>`
  - 요청 파일이 2분 넘게 안 사라짐(그리고 `yield`도 없음) → Unity가 꺼졌거나 컴파일 에러. 같은 처리로 멈춘다.
- **`yielded`**(다른 세션에 비켜 줌) → 루프를 멈추지 않는다. `yield` 파일이 **없어지면** 이어 돌리기를 요청한다:
  `{ "label": "<원래 label>-r1", "resumeFrom": "<그 세션 폴더 이름>" }` (`status.json`의 `resumeWith`에 그대로 적혀 있다. 여러 번이면 -r2, -r3).
  런처가 원래 config를 불러오고, 봇은 `resume.json`의 마지막 저장 지점(끝난 판 다음)부터 돈다. 분석기가 `campaignKey`로 여러 세션을 한 캠페인으로 묶는다.
- **`aborted`**(사람이 플레이를 끔 등)도 `resume.json`이 있으면 **한 번만** 같은 방식으로 이어 돌린다. 같은 이터레이션에서 두 번째 `aborted`·`error`면 멈춘다.
- 측정 중이 아니면 `phase`대로 다음 단계:
  - `audit` 끝 → C1 사전 점검(아래) → 통과면 `campaign` 요청
  - `campaign` 끝 → 세션 id를 `iterations/NN.json.sessions`에 추가 → `probe` 요청
  - `probe` 끝 → 세션 id 추가 → 3단계

### 3. 분석 · 판정 — 이 순서로 본다

`node Tools/BotPlaytest/analyze.js` → `BotRuns/report/data.json`. 그다음 **아래 순서를 그대로** 밟는다.
머리로 훑지 말 것 — 오늘 가장 큰 발견 두 개(우주=농장 복사본, 비행선 실효 15%)는 **지표가 아니라 ④·⑥에서** 나왔다.

**① 노이즈 폭부터 잰다.** 이번에 **손대지 않은** 목표들의 D 변동 중 최대치가 노이즈 폭이다.
이걸 모르면 아무 숫자도 해석할 수 없다. 겨냥한 변화가 이보다 작으면 그 가설은 애초에 판정 불가다.

**② 예측 대조표를 본다**(보고서 "이터레이션 기록" 맨 위). `prediction[].key`가 붙어 있으면 직전 이터레이션과 자동으로 대조돼
*예측대로 / 방향만 맞음 / 반대로 움직임*이 나온다. **판정은 여기서 시작한다.**
- 쓸 수 있는 키: `probeD:<목표이름>` · `charD:<Char_*>` · `charSpread` · `g2` · `g4` ·
  `skillPower:<스킬>` · `skillProgress:<스킬>` · `skillOverkill:<스킬>` · `skillsOutOfBand` ·
  `attemptsMean` · `attemptsOf:<목표이름>` · `attemptsInBand` · `picksPerRun` ·
  `clearMinutes:<목표이름>` · `allNodesMinutes` · `playMinutes`
- 🔴 **예측에는 반드시 `key`를 붙인다.** 없으면 대조표에 안 뜨고 판정이 다시 사람 머리로 돌아간다.

**③ "9개 목표" 표의 필요 범위를 본다.** 양옆 목표가 정한 D 구간이 그대로 다음 목표값이다.
**범위가 비어 있으면(하한 > 상한) 그 목표는 건드리지 않는다** — 이웃을 먼저 고쳐야 한다.

**④ 🔴 지표에서 막히면 에셋 숫자를 직접 대조한다.** 같은 종류의 에셋을 나란히 놓고 **숫자를 비교**하는 것이다.
맵 셋의 `enemyHpMultiplier`를 나란히 찍어보고 나서야 "우주 = 농장 복사본"이 보였다 — 어떤 지표도 그걸 말해주지 않았다.
정체성이 있어야 할 것들(맵·캐릭터·스킬)이 **서로 얼마나 다른지**를 숫자로 보는 게 이 단계다.

**⑤ 지표끼리 모순되는 곳이 단서다.** 예: 휘두르기는 **파워 1위인데 진행률 꼴찌**였고, 파고들자 오버킬 78%가 나왔다.
"세 보이는데 성적이 나쁘다" / "약해 보이는데 성적이 좋다"를 먼저 찾는다.

**⑥ 손잡이가 실제로 먹히는지 확인한다.** 값을 바꿔도 코드 구조 때문에 안 먹히는 경우가 있다 —
스폰은 else-if 사슬이라 뒤쪽 적은 지정값의 일부만 나온다(`balance` §5-1). **"바꿨는데 안 변했다"는 대부분 여기다.**

**⑦ `verdict`·`verdictNote`를 쓴다**(숫자 근거 포함) → `analyze.js`를 한 번 더 돌려 보고서에 반영한다.

### 4. 다음 변경 — 필수 (🔴 **작게 바꾸지 말 것**)

> **이터레이션 하나는 측정만 1~1.6시간이다**(campaign 40~53분 + probe 23~50분, 2026-09-18 실측).
> 그 비용을 쓰고 **판정이 안 나오면 통째로 버린 것**이다 — 실제로 1·2회차가 변경 1곳씩이라 둘 다 `inconclusive`였다(측정 4.5시간, 확인된 가설 0개).
> 그래서 이 단계의 기준은 "조심스럽게"가 아니라 **"판정이 나오게"** 다.

1. `balance` §1의 **다룰 순서**(G2 → G3 → G6·G4 → G5 → G1)에서 막힌 층을 고른다.
2. 증상을 숫자로 적는다.
3. **축 A~E 전부**에 증거·점수를 적는다(`balance` §3 표). 맵(A)은 국소화 증거가 있을 때만.
4. 가설 한 문장 · 예측 · 통과 기준 · **반증 조건**을 쓴다.
   - 예측은 `{ metric, key, direction: "up"|"down"|"flat", expectedDelta }`. 🔴 **`key`를 빠뜨리지 말 것** — 없으면 다음 틱의 예측 대조표에 안 뜬다(키 목록은 §3②).
   - **덩어리마다 예측을 최소 하나씩** 단다. 예측이 없는 덩어리는 판정할 수 없고, 판정 못 할 변경은 넣은 의미가 없다.
5. 🔴 **변경 묶기**(`balance` §3):
   - **지표가 갈라지면 한 이터레이션에 여러 덩어리를 넣는다.** 맵·캐릭터·스킬·경제는 서로 다른 칸에서 읽히므로 한 측정으로 각각 판정된다.
   - 같은 지표를 움직이는 변경들은 **한 덩어리 = 하나의 가설**로 묶어 적는다(따로 적으면 판정 불가).
   - **예상 효과 ≥ 노이즈 폭 × 1.5**. 노이즈 폭은 직전 이터레이션에서 **손대지 않은 목표들의 D 변동**으로 잰다(현재 ±0.2).
     이 조건을 못 넘는 변경은 **넣지 않는다.** 크게 밀고 넘치면 다음에 줄이는 게 훨씬 싸다.
5. `iterations/(N+1).json`을 만들고(`sessions: []`, `verdict: "pending"`) **에셋을 수정**한다.
   - `.asset`은 수정 직전에 다시 읽고 Edit 툴로 해당 필드만 바꾼다. YAML 들여쓰기를 보존한다.
   - 바꾼 뒤 `git diff --stat`으로 의도한 파일만 바뀌었는지 본다.
6. `loop_state`: `iteration = N+1`, `phase = "audit"` → `audit` 요청을 쓴다.

### C1 점검 (audit 세션이 끝났을 때)
- `cooldowns.json`에 `rule: "C1"` 위반이 있으면 **`loop_state.note`에 한 줄 적고 그냥 넘어간다.**
  🔴 C1(쿨 15초)은 하드 제약에서 **참고선으로 내려갔다**(사용자 결정 2026-09-18) — **위반을 이유로 변경을 폐기하지 않는다.**

### 4-B. 🔴 `loop_state.json`의 `insights`를 갱신한다 — 판정할 때마다

보고서 **맨 위 "핵심 결론"**이 이 배열을 그대로 그린다(사용자 요청 2026-09-18: 표만 있으면 못 읽는다).
`[{ title, status: "blocker"|"watch"|"good", now, evidence, fix, goal }]` — 중요한 것부터, **첫 항목이 "가장 중요"로 강조된다.**
- 🔴 **세 칸을 반드시 다 채운다: `now`(현재 상황) · `fix`(개선안) · `goal`(기대 목표).**
  `goal`은 **다음 이터레이션에 그대로 대조할 숫자**여야 한다 — "무엇이 얼마에서 얼마로" + **반증 조건**(무엇이 나오면 가설을 버리는가).
  "차이가 난다"로 끝나면 다음 틱에서 달성 여부를 확인할 수 없다.
- `fix`는 **에셋 파일명 · 필드명 · 목표값**을 적는다("맵을 올린다" ✗ / "`StageTable.asset`의 `enemyHpMultiplier`를 ×2.2" ○).
- `title` = 발견을 한 문장으로. **무엇이 무엇보다 어떤지**가 들어가야 한다. 지표 이름이 아니라 **무엇을 알아냈는지**로 쓴다("G2 미달" ✗ / "서열 실패 3개가 한 원인에 모인다" ○).
- `evidence` = 숫자(측정값·에셋값).
- 판정이 끝날 때마다 다시 쓴다. **낡은 항목은 지운다** — 사본이 둘이 되면 한쪽만 낡는다(CLAUDE.md §7).
- 본문에 `**강조**`를 쓰면 굵게 나온다.

### 4-C. 🔴 보고서 작성 기준 (사용자 요청 2026-09-18 — 이 형식을 계속 유지한다)

보고서는 **표만 있으면 못 읽는다.** 표마다 밑에 해설이 붙고, 맨 위에 결론이 있어야 한다.
- **모든 해설은 `현재 상황 → 개선안 → 기대 목표` 세 칸**이다(`Tools/BotPlaytest/report.html`의 `prose()`).
- 🔴 **"차이가 난다"로 끝내지 않는다.** *무엇이 무엇보다 어떻고 · 왜 그게 문제고 · 무엇을 얼마로 바꾸면 · 다음 측정에서 무엇이 어떻게 나와야 하는지*까지 간다.
- **개선안은 에셋 파일명 · 필드명 · 목표값**을 댄다("맵을 올린다" ✗ / "`StageTable.asset`의 `enemyHpMultiplier`를 해변×1.2로" ○).
- **기대 목표에는 반드시 반증 조건**을 넣는다 — *무엇이 나오면 이 가설을 버리는가*. 없으면 다음 틱에서 달성 여부를 판정할 수 없다.
- 해설은 **데이터에서 생성**한다. 특정 이터레이션 숫자를 코드에 박지 말 것 — 다음 이터레이션에서 낡는다.
- 새 지표를 추가하면 그 표에도 해설을 같이 붙인다. 표만 추가하는 건 이 기준 위반이다.

**검증**: 보고서는 문법만 맞아도 런타임 예외로 화면이 통째로 빌 수 있다.
배포 전에 `node Tools/BotPlaytest/render_check.js BotRuns/report/index.html`처럼 **DOM을 흉내 내 실제로 그려 보고**,
`append(null)`·`[object Object]`·`null` 누출이 없는지 본다(둘 다 실제로 났다).

### 5. 보고서 재배포
- `Artifact` 툴로 `BotRuns/report/index.html`을 배포한다. **같은 세션에선 같은 file_path**(URL 유지), 새 Claude 세션이면 `loop_state.reportUrl`을 `url`로 넘긴다.
- 첫 배포만 `favicon: "🫐"`. URL을 `loop_state.reportUrl`에 저장한다.
- 🔴 **이터레이션마다 반드시 한 번 배포한다**(사용자 요청 2026-09-18). 이터레이션이 끝나면 판정과 새 변경이 둘 다 생기므로 보고서는 매번 달라진다.
  측정을 기다리는 중간 틱(진행률만 바뀐 틱)은 배포하지 않는다 — 배포는 "판정 + 다음 변경이 기록된 뒤" 한 번이다.
- 배포 전에 `node Tools/BotPlaytest/render_check.js BotRuns/report/index.html`을 돌린다(§4-C).

### 6. 요청 쓰기
세션 요청은 Write 툴로 `BotRuns/request.json`에 쓴다. 이름 규칙 `itNN-<mode>` — 분석기가 이터레이션에 묶을 때 쓴다.
```json
{ "label": "it03-audit",    "mode": "audit" }
{ "label": "it03-campaign", "mode": "campaign", "campaigns": 2, "maxAttemptsPerGoal": 30, "maxRunsPerCampaign": 200, "runAudit": false, "seed": 303 }
{ "label": "it03-probe",    "mode": "probe", "probeTreeRatios": [0.3, 0.6], "probeRuns": 1, "fullTreeRuns": 2, "runAudit": false, "seed": 313 }
```
- 🔴 **campaign을 건너뛰지 않는다.** 난이도 밸런스의 본체(G3 목표별 판수)와 시간(G1)은 **정주행에서만** 나온다 —
  2회차에서 "겨냥한 건 probe 지표니까"라며 건너뛰었더니 난이도 밸런스 측정이 1회차에 멈춰 버렸다(사용자 지적 2026-09-18).
  campaign은 40~53분이면 끝난다. 그 값을 포기할 만큼 비싸지 않다.
- 시드는 이터레이션마다 바꾼다(같은 시드 반복은 운을 고정한다).
- **판 수는 속도에 맞춘다**: 이터레이션 하나가 실시간 약 2시간을 넘으면 `campaigns`·`probeRuns`를 줄이고 `loop_state.note`에 적는다. 속도는 `runs.jsonl`의 `realTime` 합으로 잰다.
- 판정이 `inconclusive`로 두 번 연속이면 다음 이터레이션은 같은 변경을 유지한 채 **표본만 늘린다**(`campaigns` +1) — 이것도 `changes`가 필요하므로, 그 가설에 묶인 손잡이를 작게 한 번 더 민다.

## 측정을 믿기 전에 볼 것

- **그 세션이 지금 코드로 돌았나** — 세션 시작 시각보다 `Library/ScriptAssemblies/Assembly-CSharp.dll` 수정 시각이 늦고, 그 dll이 모든 `.cs`보다 늦어야 한다.
  세션 도중 저장된 `.cs`는 **플레이가 끝난 뒤에** 컴파일된다(런처 설정) → 그 세션 결과엔 안 들어가 있다. 판정 대상 변경이 코드면 반드시 확인.
- **판이 비정상적으로 느린가** — 판당 `realTime / gameTime`이 평소(0.1~0.3)보다 크게 오르면(예: 0.5 이상) **누수·렉**을 의심한다.
  렉은 실시간뿐 아니라 피해 집계까지 오염시킨다. 그 세션은 판정에 쓰지 말고 `loop_state.note`에 적고, 원인은 사람이 있는 세션에서 고친다.
- **스킬이 실제로 나갔나** — 보고서 스킬 표의 **발동률**(실제 발동 ÷ 쿨만 보면 가능한 발동)이 0.5 아래면 파워 판단보다 **슬롯 굶주림·대상 없음**을 먼저 본다.
- **한두 판으로 결론 내지 않는다** — 벽 스테이지 통과 여부는 빌드 운(대공 스킬 유무)에 크게 갈린다. 캐릭터·빌드별로 3판 이상 모인 뒤 판정한다.
- **기준 트리는 "보유 노드 레벨 비율"**(`probeTreeRatios`) — 가격이 대역마다 지수로 뛰어 비용 비율로 잡으면 거의 풀트리가 된다.
- **설계 변경을 루프에 태우기 전에 작은 확인 세션부터** — `probeGoalFilter`로 해당 맵만, `fullTreeRuns` 1로 짧게 돌려 의도대로 도는지(예: 벽 스테이지에 그 적만 나오는지)를 `stages[].byEnemy`로 본 뒤 본 측정에 넣는다.

## 하지 말 것
- 목표 수치(`targets.json`)를 루프가 바꾸지 않는다 — 사용자 결정이다.
- 한 틱에 세션을 두 개 요청하지 않는다(런처는 하나씩 받는다).
- 다른 세션이 Unity를 쓰려는데 봇이 버티게 하지 않는다 — `yield` 파일을 무시하거나 지우지 않는다(만든 쪽이 지운다).
- 봇 세션이 도는 동안 `.cs`를 고치거나 `assets-refresh`를 부르지 않는다. 런처가 세션 동안 재컴파일을 "플레이 끝난 뒤"로 미루므로 판은 안 깨지지만,
  **MCP `assets-refresh`는 그 미뤄진 컴파일을 기다리다 300초 타임아웃으로 멈춘다**(2026-09-18 실측). 세션 상태는 파일로만 본다.
- 결과가 나쁘다고 봇 정책(무작위·진화 우선·싼 노드부터)을 바꾸지 않는다 — 측정 기준이 흔들린다.
