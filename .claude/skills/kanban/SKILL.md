---
name: kanban
description: 노션 연동 + 칸반보드 "POC 할일" 조회·상태변경 절차. 세션 시작/종료 루틴에서 할 일·일정을 확인·정리할 때, 노션에서 기획 문서를 찾을 때 사용.
---

# 노션 · 칸반보드 운영

## 노션(Notion) 연동

이 프로젝트의 원본 기획은 Notion에도 있음 — GDD.md/SESSION_ZERO.md는 여기서 옮겨 적은 것.

- **허브: "블루베리 디펜스 POC 문서"** — https://app.notion.com/p/34a6394cd983801991e3cb457c798a0a (`34a6394c-d983-8019-91e3-cb457c798a0a`)
  일정·칸반보드·기획 문서 링크가 전부 여기 달려 있다. **노션에서 뭘 찾든 여기서 출발할 것.**
- 기획 페이지: "🍓 블루베리 디펜스" (위 허브의 "프로토타이핑 문서" 링크) — https://app.notion.com/p/3956394cd98380aba9abf02072b96d6c
- 🔴 **툴은 `mcp__notion__API-*` 하나뿐이다.** `mcp__claude_ai_Notion__*`(=`notion-search`/`notion-fetch`)는 **이 프로젝트에 없다** —
  claude.ai 커넥터 쪽은 별도 OAuth가 필요해 비대화형 세션에서 인증할 수 없다. 이름을 착각해 호출하면 첫 단계에서 막힌다.
- GDD.md와 내용이 어긋나면 — 원본은 Notion이지만, 코딩 중 결정 사항은 GDD.md/SESSION_ZERO.md를 우선 신뢰할 것 (Notion은 초기 기획, 로컬 문서가 최신 결정 반영).

---

## 칸반보드 "POC 할일" — 일정·할 일의 단일 원본

### 📌 원칙 (깨지 말 것)

1. **일정과 할 일의 원본은 칸반보드다.** `HANDOFF.md`의 "다음 할 일"은 그 **미러**일 뿐이다.
2. **HANDOFF는 늘 칸반과 일치해 있어야 한다.** 어긋난 걸 발견하면 **그 자리에서** HANDOFF를 맞춘다 — "나중에 정리"는 없다.
3. 둘이 어긋나면 이기는 쪽은 **항상 칸반**. HANDOFF를 근거로 칸반을 고치지 말 것(반대 방향만 허용).
4. **일정을 확인·보고할 때 근거는 칸반이다.** 기억이나 HANDOFF 사본으로 답하지 말고 **매번 조회해서** 답한다.
5. **보드는 사용자와 공동 관리한다.**
   - 사용자는 **세션과 무관하게 아무 때나** 카드를 올린다 → 세션 시작에 반드시 다시 조회(`brief` 스킬 1단계). 세션이 길어지면 중간에도 한 번 더 볼 것.
   - Claude도 관리 주체다. 새 할 일이 생기면 **카드를 만들고**, 착수하면 `진행 중`, 끝나면 `완료`로 옮긴다(세션 종료 루틴 = `wrap` 스킬 1단계). 사용자가 올린 카드만 기다리지 말 것.

### 🧊 콘텐츠 동결 원칙

POC는 7/31에 끝났고 **게임 구성 변경은 원칙적으로 하지 않는다.** 지금 보드에 있는 콘텐츠성 카드
(새 캐릭터 · 농장맵 확장 · 맵 해금)는 **POC 플레이 피드백을 반영하는 마지막 예외**다.
→ **이후 콘텐츠 추가·변경은 기본적으로 안 하는 것으로 본다.** 하게 된다면 **반드시 칸반 카드로 올라온 것만** —
   Claude가 먼저 "이것도 넣을까요"를 제안하지 말 것. 폴리싱(양념)이 기본값이다.

### 보드 정보

- DB: "POC 할일" — https://app.notion.com/p/3b06394cd983803daf4ac26f924121b7
  - `database_id` = `3b06394c-d983-803d-af4a-c26f924121b7` (부모 페이지 "블루베리 디펜스 POC 문서" = `34a6394c-d983-8019-91e3-cb457c798a0a`)
- 속성: **`상태`**(status: `시작 전`/`진행 중`/`완료`) · `담당자`(people) · `이름`(title)
- **세션 시작에 조회(`brief` 스킬), 세션 종료에 정리(`wrap` 스킬).** 끝난 카드는 `완료`로 옮긴다 — 안 옮기면 다음 세션이 끝난 일을 또 한다.

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
**카드에 적힌 "~해야 함"을 그대로 베끼지 말 것.** 이미 구현됐던 적이 여러 번 있다(CLAUDE.md §7 경고와 같은 함정).
카드 1건당 **코드 1곳을 grep해 미구현임을 확인**한 뒤 옮기고, 확인한 파일:라인을 HANDOFF에 같이 적는다.
