---
name: qa-loop
description: QA 빌드 봇 루프의 한 틱 절차 — 종료 조건 확인 → 러너·빌드 상태 확인 → 분석 → 새 오류 분류(게임/봇/환경) → 확실한 게임 버그는 .cs 수정 → 재빌드 요청 → 고친 오류가 새 빌드에서 사라졌는지 판정 → 대시보드 재배포. `/loop /qa-loop [마감HH:MM]`으로 부른다. 밸런스 수치는 건드리지 않는다(사용자가 대시보드를 보고 정한다).
---

# QA 루프 — 한 틱

> 🔴 **대화하지 않는다. 질문으로 멈추지 않는다**(CLAUDE.md §1). 갈림길은 `triage.json`의 `note`와 HANDOFF `## ❓ 확인 대기`에 적고 진행한다.
> 🔴 **금지: 커밋·푸시 · 밸런스 에셋(`Assets/Data/**`·`MainSkillTree.asset`·`BalanceConstants.cs`) 수정 · 봇의 balance 정책(`BotPilot` 레벨업 우선순위) 변경 · 노션.**
>    밸런스 데이터는 **쌓기만 한다** — 결정은 사용자가 대시보드의 밸런스 탭을 보고 한다.
> 🔴 **에디터가 필요한 일은 빌드뿐이다.** 빌드는 `QARuns/build.request`로만 요청한다(`QABuild`가 봇 세션을 비키게 하고 뽑는다). 플레이모드·`assets-refresh`를 직접 부르지 않는다.

## 구성 (어디에 무엇이 있나)

| 무엇 | 어디 |
|---|---|
| 빌드 | `Assets/Editor/QABuild.cs` — `QARuns/build.request` → `QARuns/builds/<buildId>/`, 상태 `QARuns/build.status.json`, 최신 `builds/latest.txt` |
| 러너 | `Tools/QA/qa-runner.js`(실행은 `Tools/QA/qa-runner.cmd`) — 상태 `QARuns/runner.json`, 로그 `runner.log`, 세션 결과 `outcomes.jsonl`, 멈추기 `QARuns/runner.stop` |
| 세션 | `QARuns/sessions/<시각>_<buildId>_<kind><slot>/` — `runs.jsonl`·`errors.jsonl`·`perf.jsonl`·`trace.log`·`player.log`·`status.json`·`qa.json`(세이브 시나리오), 죽었으면 `crash.json`/`hang.json`, 오류 첫 화면 `err_<sig>.png` |
| 봇 | `Assets/Scripts/Bot/` — `BotPilot`(판 진행) · `BotChaos`(엣지 케이스 행동) · `QAErrorLog`(오류 수집) · `QAInvariants`(불변식·렉) |
| 분석 | `node Tools/QA/qa-analyze.js` → `QARuns/report/{data.json,index.html,shots/}` |
| 분류 | `QARuns/errors/triage.json` — **이 루프가 쓰는 유일한 판단 기록** |
| 대시보드 URL | `QARuns/report_url.txt` |

`triage.json` (sig → 판단)
```json
{ "3fa9c01b2e": { "status": "fixing", "cause": "LevelUpUI.OnReroll이 닫힌 뒤에도 불림 — rerollButton이 패널과 같이 안 꺼짐",
                   "note": "chaos reroll_all 직후에만. 사람도 연타로 재현 가능", "fixedIn": "20260923-0110_18b89f",
                   "files": ["Assets/Scripts/LevelUpUI.cs"], "utc": "…" } }
```
- `status`: `bug`(게임 버그 확인·아직 안 고침) · `fixing`(고쳤고 `fixedIn` 빌드부터 확인 중) · `fixed`(확인 끝) · `bot`(봇 쪽 문제) · `env`(환경) · `wontfix`(사람이 못 하는 조작 등 — 이유 필수)
- `new`·`regressed`는 **분석기가 붙인다**. 적지 않는다. `regressed` = `fixedIn` 이후 빌드에서 또 나왔다.

## 틱 절차

**0. 종료 조건 — 기억 없이 판정한다.** 인자의 마감 시각이 지났으면 `QARuns/runner.stop`을 만들고 → 6번(대시보드)만 하고 → `ScheduleWakeup(stop)`.
   `runner.json`이 없거나 `finishedUtc`가 있으면 러너가 끝난 것이다 → 같은 처리.

**1. 러너가 살아 있는가.** `runner.json`의 `heartbeatUtc`가 2분 넘게 안 바뀌었으면 러너가 죽은 것이다 → `cmd //c "Tools\\QA\\qa-runner.cmd <runner.json.args 그대로>"`로 다시 띄운다.
   기본 구성: `--deadline <마감> --instances 3 --chaos 1 --headlessBalance --maxSessions 400`.
   🔴 **QA 빌드는 켜지면 무조건 음소거다(`QAMute`) — 그래도 exe를 러너 밖에서 손으로 띄우지 말 것.** 설정 파일은 `JSON.stringify`로만 쓴다(셸로 쓰면 경로 이스케이프가 깨져 봇이 안 뜬다).
   🔴 실행 경로는 `QARuns/player/` 하나다(방화벽이 경로마다 "허용"을 묻는다). 빌드 폴더에서 직접 띄우지 않는다.

**2. 빌드 상태.** `build.status.json`이 `failed`면 `note`의 컴파일 오류를 고치고 다시 요청한다. `waiting`이 30분 넘게 이어지면 봇 세션이 비키지 않는 것이다 → `BotRuns/yield`의 내용을 보고 HANDOFF에 적는다(억지로 지우지 않는다).

**3. 분석.** `node Tools/QA/qa-analyze.js`. 출력의 `[new]`·`[regressed]` 줄이 이번 틱의 할 일이다.

**4. 새 오류 분류** — sig 하나씩, `data.json`의 `errors.rows[]`에서 그 줄을 본다(`stack`·`frame`·`sample.lastActions`·`screenshot`·`kinds`·`perBuild`).
   - **봇 문제**(`source: "bot"`, `[Bot]` 메시지 · 리플렉션 필드명 · `EnterRun` 대기 초과가 chaos 뒤에만): 원인이 봇 코드면 `Assets/Scripts/Bot/`을 고친다 — **balance 정책은 건드리지 않는다.** `status: bot`.
   - **사람이 못 하는 조작의 산물**(chaos의 치트성 행동 `skip_stage`·`levelup_burst` 직후에만 나고, 정상 경로로는 도달 불가): `wontfix` + 그 근거. 도달 가능하면 게임 버그다.
   - **게임 버그**: 스택의 첫 게임 프레임을 열고 원인을 **한 줄로** 쓴다. 쓸 수 있고 고치는 곳이 명확하면 고친다(`status: fixing`, `files`). 한 줄로 못 쓰면 `bug`로 두고 가설을 `note`에.
     - 🔴 **증상을 막는 null 체크를 넣기 전에 왜 null이 되는지를 쓴다**(CLAUDE.md §1 "증상의 크기를 줄이지 말고 구조를 바꾼다"). 가드만 넣으면 오류는 사라지고 버그는 남는다.
     - 🔴 고친 `.cs`는 `bash Tools/compile-check.sh qa`와 `bash Tools/compile-check.sh release`를 **둘 다** 통과해야 빌드를 요청한다.
     - UI·연출이 바뀌는 수정은 하지 않는다(손맛은 사용자 몫) → `bug` + HANDOFF.
   - **환경**(디스크·그래픽 장치·-nographics에서만): `env`.
   - **불변식**(`[QA-INV]`): 불변식이 틀렸을 수도 있다. 사람 눈에 실제로 보이는 증상인지 스크린샷으로 먼저 확인하고, 불변식이 틀렸으면 `QAInvariants.cs`를 고친다.

**5. 재빌드와 확인.**
   - 이번 틱에 `fixing`이 새로 생겼으면 `QARuns/build.request`를 쓴다. 러너가 새 빌드로 알아서 갈아탄다(판 경계).
   - 새 빌드가 나오면 이번 틱에 고친 sig의 `fixedIn`을 그 `buildId`로 채운다.
   - `fixing`인 sig는 `fixedIn` 빌드에서 **그 오류가 났던 kind의 판이 50판 넘게 돌았는데 0번**이면 `fixed`로 바꾼다. 판이 모자라면 기다린다.
   - `regressed`가 떴으면 그 sig를 다시 연다(원인 재분석 — 첫 수정이 증상만 막았을 가능성부터).

**6. 대시보드 재배포.** `QARuns/report/index.html`을 Artifact로 배포한다.
   - 처음이면 새로 만들고 URL을 `QARuns/report_url.txt`에 적는다. 다음부터는 **그 URL로 갱신**한다.
   - 스크린샷은 `files`로 `shots/<sig>.png`를 같이 올린다(바뀐 것만).

**7. 다음 틱 예약.** 오류를 고치는 중이거나 빌드를 기다리면 20분, 조용하면 40분(`ScheduleWakeup`).

## 🔴 판단 기준

- 🔴 **렉은 `frametimes.jsonl`(FrameTimingManager)로만 판단한다 — `perf.jsonl`의 벽시계 렉은 근거가 아니다.**
  창이 가려지면 화면 제출이 초당 1회로 억제돼 벽시계 프레임이 0.5초씩 끊긴다(내용과 무관, 간격 1.03초, 분해하면 전부
  `Semaphore.WaitForSignal`). 같은 판이 headless에선 250ms 넘는 프레임 0개였다(9/23 실측).
  `frametimes.jsonl`은 CPU·GPU·제출 대기를 갈라 주므로 **대기를 빼고** 본다. 창을 띄운 인스턴스(`kind: render`, 1080p)가 그 값을 만든다.
  ⚠️ `timingsValid`가 false면 "부하 없음"이 아니라 **안 재진 것**이다(빌드에 Frame Timing Stats가 꺼진 경우).
- **빈도가 아니라 영향으로 순서를 매긴다.** 크래시·멈춤·`timescale-stuck`(게임이 얼어 보임) > 판이 이상하게 끝남 > 로그만 남는 오류. 1000번 나는 로그 경고보다 한 번의 크래시가 먼저다.
- **chaos에서만 나는 오류**도 사람이 같은 순서로 누를 수 있으면 게임 버그다. `lastActions`로 재현 순서를 적어 둔다.
- 같은 sig가 여러 원인에서 날 수 있다(첫 프레임이 공용 유틸일 때). `stack` 전체가 서로 다르면 sig를 믿지 말고 따로 적는다.
- 밸런스 탭의 숫자를 보고 판단이 떠올라도 **고치지 않는다.** 사용자에게 보고할 것이 있으면 HANDOFF에 한 줄.
