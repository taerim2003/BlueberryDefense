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
- `iterations/00.json`을 `{ "iteration": 0, "sessions": [], "verdict": null }`로 만든다.
- 이미 있으면 **이어서** 한다(지난밤 기록 위에 쌓는다 — 보고서 URL도 그대로).

### 1. 종료 조건 — 무엇보다 먼저
- `iteration > maxIterations` 이거나 **지금 시각 ≥ 마감**이면: 3·5단계(분석·보고서)를 한 번 돌리고 `final: true`로 재배포 → `ScheduleWakeup stop`.
- 마감은 기억이 아니라 시각으로 판정한다. 컨텍스트가 날아가도 이것만은 성립한다.

### 2. 세션 상태
- `BotRuns/request.json` 또는 `BotRuns/active/session.json`이 있으면 **측정 중**이다. 가장 최근 세션 폴더의 `status.json`을 읽는다.
  - `running`이고 `heartbeatUtc`가 5분 이내 → 남은 판 수를 보고 **10~20분 뒤** 다시 깬다(`ScheduleWakeup`).
  - `heartbeatUtc`가 5분 넘게 멈춤, 또는 `error`·`aborted` → `loop_state.note`에 사유·세션 id를 적고 보고서를 재배포한 뒤 **루프를 멈춘다**(무인 재시작 시도 금지 — Unity가 멈췄을 수 있다).
  - 요청 파일이 2분 넘게 안 사라짐 → Unity가 꺼졌거나 컴파일 에러. 같은 처리로 멈춘다.
- 측정 중이 아니면 `phase`대로 다음 단계:
  - `audit` 끝 → C1 사전 점검(아래) → 통과면 `campaign` 요청
  - `campaign` 끝 → 세션 id를 `iterations/NN.json.sessions`에 추가 → `probe` 요청
  - `probe` 끝 → 세션 id 추가 → 3단계

### 3. 분석 · 판정
1. `node Tools/BotPlaytest/analyze.js`
2. `BotRuns/report/data.json`에서 이번 이터레이션(`iteration N`)의 스코어보드·세부를 읽는다.
3. N ≥ 1이면 **N.json의 가설을 판정**한다 — `prediction`·`passCriterion` vs 측정값, 노이즈 폭(`balance` §4). `verdict`·`verdictNote`를 채운다.
4. `analyze.js`를 한 번 더 돌려 판정이 보고서에 들어가게 한다.

### 4. 다음 변경 — 필수
1. `balance` §1 우선순위로 **겨냥할 목표 하나**를 고른다(가장 먼 것).
2. 증상을 숫자로 적는다.
3. **축 A~E 전부**에 증거·점수를 적는다(`balance` §3 표). 맵(A)은 국소화 증거가 있을 때만.
4. 손잡이 1~3곳 · 가설 한 문장 · 예측(지표·방향·크기) · 통과 기준을 쓴다.
5. `iterations/(N+1).json`을 만들고(`sessions: []`, `verdict: "pending"`) **에셋을 수정**한다.
   - `.asset`은 수정 직전에 다시 읽고 Edit 툴로 해당 필드만 바꾼다. YAML 들여쓰기를 보존한다.
   - 바꾼 뒤 `git diff --stat`으로 의도한 파일만 바뀌었는지 본다.
6. `loop_state`: `iteration = N+1`, `phase = "audit"` → `audit` 요청을 쓴다.

### C1 사전 점검 (audit 세션이 끝났을 때)
- 그 세션의 `cooldowns.json` `violations`에 `rule: "C1"`이 있으면 → **이번 변경은 무효**: 해당 에셋을 되돌리고, `iterations/N.json`에 `verdict: "refuted"`, `verdictNote: "C1 위반으로 측정 전 폐기"`를 적은 뒤 4단계를 다시 한다(같은 N 번호를 재사용하지 말고 N+1로).

### 5. 보고서 재배포
- `Artifact` 툴로 `BotRuns/report/index.html`을 배포한다. **같은 세션에선 같은 file_path**(URL 유지), 새 Claude 세션이면 `loop_state.reportUrl`을 `url`로 넘긴다.
- 첫 배포만 `favicon: "🫐"`. URL을 `loop_state.reportUrl`에 저장한다.
- 틱마다 배포할 필요는 없다 — **판정이나 변경이 생긴 틱**과 **마지막 틱**에만.

### 6. 요청 쓰기
세션 요청은 Write 툴로 `BotRuns/request.json`에 쓴다. 이름 규칙 `itNN-<mode>` — 분석기가 이터레이션에 묶을 때 쓴다.
```json
{ "label": "it03-audit",    "mode": "audit" }
{ "label": "it03-campaign", "mode": "campaign", "campaigns": 2, "maxAttemptsPerGoal": 30, "maxRunsPerCampaign": 200, "runAudit": false, "seed": 303 }
{ "label": "it03-probe",    "mode": "probe", "probeTreeRatios": [0.05, 0.3], "probeRuns": 1, "fullTreeRuns": 2, "runAudit": false, "seed": 313 }
```
- 시드는 이터레이션마다 바꾼다(같은 시드 반복은 운을 고정한다).
- **판 수는 속도에 맞춘다**: 이터레이션 하나가 실시간 약 2시간을 넘으면 `campaigns`·`probeRuns`를 줄이고 `loop_state.note`에 적는다. 속도는 `runs.jsonl`의 `realTime` 합으로 잰다.
- 판정이 `inconclusive`로 두 번 연속이면 다음 이터레이션은 같은 변경을 유지한 채 **표본만 늘린다**(`campaigns` +1) — 이것도 `changes`가 필요하므로, 그 가설에 묶인 손잡이를 작게 한 번 더 민다.

## 하지 말 것
- 목표 수치(`targets.json`)를 루프가 바꾸지 않는다 — 사용자 결정이다.
- 한 틱에 세션을 두 개 요청하지 않는다(런처는 하나씩 받는다).
- 봇 세션이 도는 동안 `.cs`를 고치거나 `assets-refresh`를 부르지 않는다. 런처가 세션 동안 재컴파일을 "플레이 끝난 뒤"로 미루므로 판은 안 깨지지만,
  **MCP `assets-refresh`는 그 미뤄진 컴파일을 기다리다 300초 타임아웃으로 멈춘다**(2026-09-18 실측). 세션 상태는 파일로만 본다.
- 결과가 나쁘다고 봇 정책(무작위·진화 우선·싼 노드부터)을 바꾸지 않는다 — 측정 기준이 흔들린다.
