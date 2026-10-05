---
name: build
description: 테스트용 릴리스 빌드를 뽑아 zip으로 묶고 노션 「프로토타입 빌드」 페이지에 올린다. 지금 작업 폴더(다른 세션의 커밋 안 된 변경 포함)를 그대로 빌드한다. Steam 업로드가 아니다. 사용자가 /build를 칠 때만 실행.
disable-model-invocation: true
---

# /build — 빌드 뽑아서 노션에 올리기

사용자가 받아서 테스트할 빌드를 만든다. **Steam(steamcmd · `D:\SteamScripts`)에는 올리지 않는다** — 그건 사용자가 따로 말할 때만.
질문으로 멈추지 않는다(CLAUDE.md §1). 막히면 그 단계에서 멈추고 이유를 보고한다.

| 단계 | 하는 일 | 끝난 판정 |
|---|---|---|
| 0 | Unity·다른 세션 상태 확인 | MCP 응답 · 플레이 중 아님 · 봇 비켜 줌 |
| 1 | 릴리스 컴파일 확인 | `compile-check.sh release` 통과 |
| 2 | 빌드 | `Builds/Release/status.txt` 마지막 줄 `DONE` |
| 3 | zip | 항목 경로가 `Build/…`(슬래시) · `unzip -t` 오류 없음 |
| 4 | 노션 업로드 | 스크립트가 `VERIFIED` 출력 |
| 5 | 정리·보고 | 내가 쓴 `yield` 삭제 · 보고 |

## 0. 상태 확인

- **Unity**: `script-execute`로 `EditorApplication.isPlaying`·`isCompiling`과 열린 씬의 `isDirty`를 찍는다(`unity-mcp` 스킬 수칙대로 결과는 반환값으로).
  - `ECONNREFUSED`면 Unity가 꺼진 것 → `Start-Process "C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe" -ArgumentList '-projectPath "D:\unity\BlueberryDefense"'`로 띄우고,
    `curl -s -m 2 http://localhost:23269`가 응답할 때까지 기다린 뒤 MCP 툴을 다시 불러 연결을 확인한다(첫 임포트에 수십 초).
    뜬 직후엔 빈 씬만 열려 있다 — 빌드는 빌드 설정의 씬 파일을 쓰므로 그대로 둬도 된다.
  - **플레이 중이면 멈추고 보고한다.** 사용자가 테스트 중일 수 있다 — 내가 플레이모드를 끄지 않는다.
  - 열린 씬이 dirty면 **저장하지 않는다**(사용자 작업물). 빌드는 디스크의 씬을 쓰므로 그 편집은 안 들어간다 — 보고에 적는다.
- **봇 세션**: `BotRuns/active/session.json`이 있으면 봇이 Unity를 쓰는 중이다(CLAUDE.md §6-2). `BotRuns/yield`에 `build · 테스트 빌드`를 쓰고
  `status.json`의 `state`가 `yielded`가 되고 `active/`가 빌 때까지 기다린다.
- **QA 빌드**: `QARuns/build.status.json`의 `state`가 `building`이면 끝날 때까지 기다린다(둘이 동시에 에디터를 잡으면 안 된다).

## 1. 컴파일 확인

`bash Tools/compile-check.sh release` — 오류가 있으면 **어느 파일인지** 보고하고 멈춘다. 다른 세션이 작업 중인 파일일 가능성이 높다 — 내가 고치지 않는다.

## 2. 빌드

1. `assets-refresh`를 부르고, `isCompiling == false`가 될 때까지 확인한다. 에디터에 포커스가 없으면 자동 임포트가 안 돌아서
   **다른 세션이 디스크에 쓴 변경이 옛 임포트 결과로 들어간다** — 이 단계를 빼면 "변경사항 적용"이 안 된다.
2. `script-execute`로 `return ReleaseBuild.Queue();` — `queued`가 오면 시작된 것이다. `busy`면 그 이유를 보고 1.로.
3. 끝날 때까지 백그라운드로 기다린다(보통 3분 안팎):
   `F=Builds/Release/status.txt; for i in $(seq 1 300); do grep -q "DONE\|FAILED" $F && break; sleep 5; done; cat $F` (`run_in_background`).
   `FAILED`면 그 줄을 그대로 보고하고 멈춘다.

## 3. zip

```
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ReleaseBuild/zip-build.ps1 -BuildDir Builds/Release/Build -Dst Builds/Release/BlueberryDefense.zip
unzip -l Builds/Release/BlueberryDefense.zip | awk '{print $4}' | cut -d/ -f1-2 | sort -u | head
unzip -tq Builds/Release/BlueberryDefense.zip
```
- 경로가 `Build/BlueberryDefense.exe`처럼 **슬래시**로 나와야 한다. `Build\…`면 압축 해제 도구에 따라 폴더가 깨진다.
- 셸 인자에 경로·백슬래시를 넣는 코드를 직접 쓰지 말 것(CLAUDE.md §8-1) — 위 스크립트만 쓴다.

## 4. 노션 업로드

`node Tools/ReleaseBuild/notion-upload.js Builds/Release/BlueberryDefense.zip`
- 「프로토타입 빌드」 페이지(허브 하단 링크)의 **마지막 파일 바로 뒤**에 붙는다. 이름은 `BlueberryDefense_YYYYMMDD.zip`, 같은 이름이 있으면 `_HHmm`이 붙는다.
- 마지막 줄이 `VERIFIED`여야 끝이다(스크립트가 페이지를 다시 읽어 확인한다). `ERROR`면 그 메시지를 보고한다.
- 노션 MCP 툴엔 파일 업로드가 없어서 스크립트가 MCP 서버의 토큰으로 REST API를 직접 부른다. 토큰을 출력하거나 다른 곳에 옮기지 말 것.

## 5. 정리·보고

- 0단계에서 내가 쓴 `BotRuns/yield`(내용이 `build ·`로 시작)는 지운다 — 안 지우면 봇이 영원히 안 돈다.
- `git rev-parse --short HEAD`와 `git status --porcelain | wc -l`로 **무엇이 들어갔는지**를 적는다: HEAD + 커밋 안 된 변경 N개.
- 보고: 노션에 올라간 파일 이름 · 크기 · 들어간 변경(HEAD + 미커밋 N개) · dirty 씬 때문에 빠진 편집이 있으면 그것.
  **실행해 보지는 않았다**는 것도 적는다(압축 검사만 했다). 커밋하지 않는다.
- `HANDOFF.md`의 `확인 대기`에 **"화면 캡처로 확인하지 않았다"고 적힌 변경이 이 빌드에 들어 있으면 보고 맨 위에 적는다.**
  받는 사람이 보는 건 그 빌드다 — 사용자가 확인하기 전에 나가면 되돌려도 올라간 파일은 그대로 남는다.
