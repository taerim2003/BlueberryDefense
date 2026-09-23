#!/usr/bin/env bash
# Unity 에디터를 건드리지 않고 Assembly-CSharp을 진짜로 컴파일해 본다.
# 병렬 세션·봇 플레이테스트가 에디터를 쓰는 중에도 안전하다(도메인 리로드를 강요하지 않는다).
#
# 쓰는 법: 프로젝트 루트에서  bash Tools/compile-check.sh [editor|qa|release]
#   editor(기본) = 에디터 컴파일 그대로
#   qa           = UNITY_EDITOR* define을 빼고 BOT_QA를 넣는다 → QA 빌드(Assets/Editor/QABuild.cs) 구성
#   release      = UNITY_EDITOR* define을 뺀다 → 릴리스 빌드 구성(봇 코드가 빠져도 컴파일되는지)
#   ⚠️ qa/release도 참조 DLL은 에디터 것 그대로라 "빌드에 없는 API"는 못 잡는다 — define 분기만 검사한다.
#
# ⚠️ rsp의 소스 목록은 Unity가 마지막으로 컴파일한 시점 것이다.
#    `.cs` 파일을 **추가·삭제**했으면 이 검사는 무효다(내용 수정만 유효) — 그땐 에디터 컴파일이 필요하다.
set -u

UNITY="/c/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor/Data"
DOTNET="$UNITY/NetCoreRuntime/dotnet.exe"
# ⚠️ csc에 넘기는 경로는 Windows 형식(C:/...)이어야 한다. Git Bash의 /c/... 는 D:\c\... 로 해석된다.
CSC="C:/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor/Data/DotNetSdkRoslyn/csc.dll"

OUT_DIR="${TMPDIR:-/tmp}/bbd-compile-check"
mkdir -p "$OUT_DIR"
OUT_WIN=$(cd "$OUT_DIR" && pwd -W 2>/dev/null || echo "$OUT_DIR")

# 🔴 **에디터 rsp만 고른다.** 플레이어 빌드(QA 빌드 포함)도 같은 폴더에 rsp를 남기는데(`*P.dag`·`*PDevDbg.dag`),
#    그게 최신이 되면 "에디터 검사"가 조용히 플레이어 구성을 검사한다. UNITY_EDITOR define 줄이 있는 것만 후보다.
RSP=$(grep -l '^-define:UNITY_EDITOR$' Library/Bee/artifacts/*.dag/Assembly-CSharp.rsp 2>/dev/null | xargs -r ls -t 2>/dev/null | head -1)
if [ -z "$RSP" ]; then
  echo "FAIL: Assembly-CSharp.rsp를 못 찾았다. Unity가 한 번은 컴파일한 적이 있어야 한다."
  exit 2
fi

CHECK_RSP="$OUT_DIR/check.rsp"
DLL="$OUT_WIN/check.dll"
rm -f "$OUT_DIR/check.dll"
# Unity 아티팩트를 덮지 않도록 -out을 옮기고 -refout은 지운다.
sed -e "s|^-out:.*|-out:\"$DLL\"|" -e "s|^-refout:.*||" "$RSP" > "$CHECK_RSP"
# 🔴 원본 rsp는 **마지막 줄에 개행이 없다** — 그냥 append하면 마지막 옵션에 들러붙어 조용히 무시된다.
printf '\n' >> "$CHECK_RSP"

MODE="${1:-editor}"
if [ "$MODE" = "qa" ] || [ "$MODE" = "release" ]; then
  BEFORE=$(grep -c '^-define:UNITY_EDITOR' "$CHECK_RSP")
  sed -i '/^-define:UNITY_EDITOR/d' "$CHECK_RSP"
  [ "$MODE" = "qa" ] && echo "-define:BOT_QA" >> "$CHECK_RSP"
  # 🔴 뺀 개수를 센다 — 0이면 rsp 형식이 바뀐 것이고, 이 모드는 에디터 구성을 한 번 더 검사한 것뿐이다.
  echo "mode=$MODE: UNITY_EDITOR* define ${BEFORE}개 제거$( [ "$MODE" = "qa" ] && echo ', BOT_QA 추가')"
  if [ "$BEFORE" -lt 1 ]; then echo "FAIL: rsp에서 UNITY_EDITOR define을 못 찾았다"; exit 2; fi
fi

# rsp의 소스 목록은 Unity가 마지막으로 컴파일한 시점 것이라 **새로 만든 .cs가 빠져 있다.**
# 그대로 두면 새 타입을 참조하는 파일이 "없는 타입"으로 실패하거나, 반대로 새 파일의 오류를 놓친다.
# → Assets/Scripts 아래 .cs 중 rsp에 없는 것을 찾아 붙인다(Editor 스크립트는 다른 어셈블리라 제외).
ADDED=0
while IFS= read -r f; do
  # rsp는 따옴표 친 슬래시 경로로 적는다: "Assets/Scripts/Foo.cs"
  win="${f#./}"
  if ! grep -qF "\"$win\"" "$CHECK_RSP"; then
    echo "\"$win\"" >> "$CHECK_RSP"
    ADDED=$((ADDED+1))
    echo "  + rsp에 없던 새 파일 추가: $win"
  fi
done < <(find Assets/Scripts -name '*.cs' -not -path '*/Editor/*')

# 🔴 입력이 비지 않았는지 먼저 센다 — 빈 입력은 "오류 0"으로 통과처럼 보인다.
SRC_COUNT=$(grep -cE '\.cs"?$' "$CHECK_RSP")
echo "rsp: $RSP  (소스 $SRC_COUNT개, 새로 추가 $ADDED개)"
if [ "$SRC_COUNT" -lt 2 ]; then
  echo "FAIL: 소스 목록이 비었다 — rsp 치환이 깨졌다."
  exit 2
fi

"$DOTNET" "$CSC" "@$CHECK_RSP" > "$OUT_DIR/out.txt" 2>&1
CSC_EXIT=$?

grep -E ": error |: warning CS" "$OUT_DIR/out.txt" | head -40

# 🔴 종료코드와 산출물을 **둘 다** 본다. 파이프 뒤의 $?는 grep/head의 것이라 믿으면 안 된다.
if [ "$CSC_EXIT" -ne 0 ] || [ ! -f "$OUT_DIR/check.dll" ]; then
  echo "FAIL: csc exit=$CSC_EXIT · dll=$( [ -f "$OUT_DIR/check.dll" ] && echo 있음 || echo 없음 )"
  echo "--- 전체 출력 앞 30줄 ---"
  head -30 "$OUT_DIR/out.txt"
  exit 1
fi

echo "OK: 컴파일 통과 (오류 0 · dll $(stat -c%s "$OUT_DIR/check.dll") bytes)"
