<#
봇 세션 감시자 — Unity 에디터가 "메인 스레드째" 멈췄을 때 되살린다.

왜 프로세스 밖에 있어야 하나:
  BotPilot의 감시(Update의 lastMainTickReal 검사)는 **Update가 도는 동안에만** 돈다.
  2026-09-18의 두 정지는 메인 스레드가 통째로 막힌 경우라 Update 자체가 안 돌았고,
  그래서 안쪽 감시는 원리상 발동할 수 없었다(측정으로 확인: CPU 3%·전 스레드 Wait·모달 없음).
  안쪽에 무엇을 더 넣어도 같은 이유로 못 잡는다. 그래서 감시는 **Unity 밖**에 있어야 한다.

무엇을 하나:
  status.json의 heartbeatUtc가 -StaleMinutes 넘게 멈췄고 state가 running이면 정지로 보고
    ① 미니덤프 + Editor.log 보존(BotRuns/hang_<시각>/)  ← 원인 규명의 유일한 단서다. 지우지 말 것
    ② Unity 강제 종료 → active/session.json 정리 → resume 요청 작성 → Unity 재시작
  런처(BotLauncher)가 request.json을 보고 알아서 이어 돌린다.

종료 조건(기억 없이 성립하는 상한만 쓴다 — CLAUDE.md §4):
  -Deadline 시각을 넘기거나 -MaxRestarts 회를 다 쓰면 멈춘다.

쓰는 법:
  powershell -ExecutionPolicy Bypass -File Tools\BotPlaytest\watchdog.ps1 -Deadline 23:00
  powershell ... -DryRun          # 죽이지 않고 판정만 찍어 본다
#>
[CmdletBinding()]
param(
    [double] $StaleMinutes = 5,
    [int]    $MaxRestarts  = 5,
    [string] $Deadline     = "",          # "HH:mm" (오늘). 빈 값이면 시간 제한 없음
    [double] $PollSeconds  = 60,
    [string] $UnityExe     = "",
    [switch] $DryRun
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$RunsRoot    = Join-Path $ProjectRoot "BotRuns"
$ActivePath  = Join-Path $RunsRoot "active\session.json"
$RequestPath = Join-Path $RunsRoot "request.json"
$YieldPath   = Join-Path $RunsRoot "yield"
$EditorLog   = Join-Path $env:LOCALAPPDATA "Unity\Editor\Editor.log"

# 🔴 콘솔에만 쓰면 띄우는 방식에 따라 로그가 통째로 사라진다(리다이렉트 없이 start로 떼어낸 경우).
#    파일에도 같이 적어 둔다 — 정지를 사후에 따질 때 이 로그가 유일한 근거다.
$LogPath = Join-Path $RunsRoot "watchdog.log"
function Say($msg) {
    $line = "{0}  {1}" -f (Get-Date -Format "HH:mm:ss"), $msg
    Write-Host $line
    try { Add-Content -Path $LogPath -Value $line -Encoding utf8 } catch { }
}

# JSON은 BOM 없이 쓴다 — analyze.js(Node)의 JSON.parse는 BOM을 못 넘긴다.
# Set-Content -Encoding utf8은 PowerShell 5.1에서 BOM을 붙인다.
function Write-JsonNoBom($path, $obj) {
    [System.IO.File]::WriteAllText($path, ($obj | ConvertTo-Json -Depth 10), (New-Object System.Text.UTF8Encoding $false))
}

if (-not $UnityExe) {
    $UnityExe = Get-ChildItem "$env:ProgramFiles\Unity\Hub\Editor" -Filter "Unity.exe" -Recurse -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $UnityExe -or -not (Test-Path $UnityExe)) { throw "Unity.exe를 못 찾았다. -UnityExe로 직접 줄 것." }

$DeadlineAt = $null
if ($Deadline) {
    $DeadlineAt = [datetime]::ParseExact($Deadline, "HH:mm", $null)
    if ($DeadlineAt -lt (Get-Date)) { $DeadlineAt = $DeadlineAt.AddDays(1) }
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class BotDump {
    [DllImport("dbghelp.dll")]
    public static extern bool MiniDumpWriteDump(IntPtr h, uint pid, IntPtr f, int type, IntPtr a, IntPtr b, IntPtr c);
}
"@

# 정지한 Unity의 스레드 스택을 남긴다. 지금은 읽을 디버거가 없어도 파일은 남겨야 한다 —
# 다음 정지 때 WinDbg/procdump가 생기면 이 덤프가 원인을 가리키는 유일한 물증이다.
function Save-Evidence($proc, $sessionDir, $status) {
    $dir = Join-Path $RunsRoot ("hang_" + (Get-Date -Format "yyyyMMdd-HHmm"))
    New-Item -ItemType Directory -Force $dir | Out-Null
    try {
        $dmp = Join-Path $dir "unity_hang.dmp"
        $fs  = [System.IO.File]::Create($dmp)
        # MiniDumpWithThreadInfo | MiniDumpWithProcessThreadData | MiniDumpWithHandleData
        [void][BotDump]::MiniDumpWriteDump($proc.Handle, $proc.Id, $fs.SafeFileHandle.DangerousGetHandle(), 0x1104,
                                           [IntPtr]::Zero, [IntPtr]::Zero, [IntPtr]::Zero)
        $fs.Close()
    } catch { Say "  덤프 실패(무시): $_" }
    if (Test-Path $EditorLog) { Copy-Item $EditorLog (Join-Path $dir "Editor.log") -Force -ErrorAction SilentlyContinue }
    if ($sessionDir -and (Test-Path $sessionDir)) {
        Copy-Item (Join-Path $sessionDir "status.json") (Join-Path $dir "status.json") -Force -ErrorAction SilentlyContinue
    }
    # 프로세스가 돌던 중인지(무한 루프) 멈춰 있었는지(대기) — 이 한 줄이 다음 조사의 출발점을 가른다
    $c1 = $proc.CPU; Start-Sleep -Seconds 5; $proc.Refresh(); $c2 = $proc.CPU
    @(
        "정지 감지: $(Get-Date -Format o)"
        "세션 폴더: $sessionDir"
        "마지막 heartbeat: $($status.heartbeatUtc)  (state=$($status.state) run=$($status.run) map=$($status.map) asc=$($status.ascension) char=$($status.character) stage=$($status.stage))"
        "Responding: $($proc.Responding)"
        ("CPU 5초간 증가: {0:N2}s  (거의 0이면 '대기', 5에 가까우면 '무한 루프')" -f ($c2 - $c1))
        "Editor.log 마지막 10줄:"
    ) + (Get-Content $EditorLog -Tail 10 -ErrorAction SilentlyContinue) | Set-Content (Join-Path $dir "why.txt") -Encoding utf8
    return $dir
}

Say "감시 시작 — 프로젝트 $ProjectRoot"
Say "  기준: heartbeat $($StaleMinutes)분 정지 · 최대 재시작 $($MaxRestarts)회 · 마감 $(if($DeadlineAt){$DeadlineAt.ToString('yyyy-MM-dd HH:mm')}else{'없음'})$(if($DryRun){' · DRYRUN'})"

$restarts = 0
while ($true) {
    if ($DeadlineAt -and (Get-Date) -ge $DeadlineAt) { Say "마감 도달 — 감시 종료"; break }
    if ($restarts -ge $MaxRestarts)                  { Say "재시작 $($MaxRestarts)회를 다 썼다 — 감시 종료(사람이 볼 차례)"; break }

    Start-Sleep -Seconds $PollSeconds

    # 다른 세션이 Unity를 쓰는 중이면 손대지 않는다(CLAUDE.md §6-2 — 봇이 비켜 주는 쪽)
    if (Test-Path $YieldPath)   { continue }
    if (-not (Test-Path $ActivePath)) { continue }   # 도는 세션이 없다 = 지킬 것도 없다

    try {
        $active     = Get-Content $ActivePath -Raw | ConvertFrom-Json
        $sessionDir = $active.sessionDir
        $statusPath = Join-Path $sessionDir "status.json"
        if (-not (Test-Path $statusPath)) { continue }
        $status = Get-Content $statusPath -Raw | ConvertFrom-Json
    } catch { continue }   # 봇이 쓰는 중이면 반쪽 JSON을 읽을 수 있다 — 다음 폴에서 다시 본다

    if ($status.state -ne "running") { continue }
    if (-not $status.heartbeatUtc)   { continue }

    $age = ((Get-Date).ToUniversalTime() - ([datetime]$status.heartbeatUtc).ToUniversalTime()).TotalMinutes
    if ($age -lt $StaleMinutes) { continue }

    $proc = Get-Process Unity -ErrorAction SilentlyContinue | Sort-Object WorkingSet64 -Descending | Select-Object -First 1
    if (-not $proc) { Say "heartbeat가 $([math]::Round($age,1))분 멈췄는데 Unity 프로세스가 없다 — 사람이 껐다고 보고 넘어간다"; continue }

    Say "🔴 정지 감지 — heartbeat $([math]::Round($age,1))분 정지 (run=$($status.run) $($status.map) asc$($status.ascension) $($status.character))"
    $evidence = Save-Evidence $proc $sessionDir $status
    Say "  증거 보존: $evidence"

    if ($DryRun) { Say "  DRYRUN — 종료·재시작은 건너뛴다"; continue }

    $label = if ($status.label) { $status.label } else { "session" }
    $resumeLabel = "{0}-w{1}" -f $label, ($restarts + 1)
    $resumeFrom  = Split-Path $sessionDir -Leaf

    # 멈춘 세션에 도장을 찍어 둔다 — 분석기와 다음 세션이 "여기서 끊겼다"를 알아야 한다
    try {
        $status | Add-Member -NotePropertyName state -NotePropertyValue "hung" -Force
        $status | Add-Member -NotePropertyName hungAtUtc -NotePropertyValue (Get-Date).ToUniversalTime().ToString("o") -Force
        $status | Add-Member -NotePropertyName evidenceDir -NotePropertyValue (Split-Path $evidence -Leaf) -Force
        Write-JsonNoBom $statusPath $status
    } catch { Say "  status.json 갱신 실패(무시): $_" }

    Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 3
    Say "  Unity 종료"

    # 런처는 active/session.json이 남아 있으면 새 요청을 받지 않는다 — 치워야 이어 돈다
    Remove-Item $ActivePath -Force -ErrorAction SilentlyContinue

    if (Test-Path $RequestPath) { Say "  request.json이 이미 있다 — 그대로 두고 재시작만 한다" }
    else {
        Write-JsonNoBom $RequestPath @{ label = $resumeLabel; resumeFrom = $resumeFrom }
        Say "  이어 돌리기 요청: $resumeLabel (resumeFrom $resumeFrom)"
    }

    # 강제 종료 뒤 Temp\__Backupscenes가 남아 있으면 에디터가 "Recovering Scene Backups" 모달을 띄우고
    # 거기서 멈춘다 — 무인 실행에선 눌러 줄 사람이 없어 런처가 요청을 영영 못 집는다(2026-09-18 실측).
    # 지우지 않고 증거 폴더로 옮긴다.
    $backupScenes = Join-Path $ProjectRoot "Temp\__Backupscenes"
    if (Test-Path $backupScenes) {
        try {
            Move-Item $backupScenes (Join-Path $evidence "__Backupscenes") -Force -ErrorAction Stop
            Say "  백업 씬을 증거 폴더로 옮김 — 복구 모달 차단"
        } catch { Say "  백업 씬 이동 실패(무시): $_" }
    }

    Start-Process $UnityExe -ArgumentList @("-projectPath", $ProjectRoot) | Out-Null
    $restarts++
    Say "  Unity 재시작 ($restarts/$MaxRestarts) — 에디터가 뜨면 런처가 알아서 이어 돈다"

    # 에디터가 올라와 세션을 잡을 때까지는 판정하지 않는다(뜨는 데만 몇 분 걸린다)
    $waitUntil = (Get-Date).AddMinutes(12)
    while ((Get-Date) -lt $waitUntil -and -not (Test-Path $ActivePath)) { Start-Sleep -Seconds 10 }
    if (Test-Path $ActivePath) { Say "  새 세션이 시작됐다" } else { Say "  ⚠ 12분 안에 세션이 안 잡혔다 — 다음 폴에서 다시 본다" }
}
