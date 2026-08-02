# Unity MCP hazard blocker (PreToolUse hook)
#
# Blocks the two classes of mistakes that recurred 2+ times across 22 sessions,
# at the moment of the call rather than as a rule to remember:
#   1) MCP tools that have a reliable replacement (scene-open, console-get-logs)
#   2) Editor APIs that open a modal and kill the whole MCP connection
#   3) OpenScene without Additive - overwrites the scene the user has open
#
# Rationale: Notion "AI Session Guide - 22-session retrospective", Part 1.
#
# NOTE: This file is intentionally ASCII-only. PowerShell 5.1 mangles non-ASCII
#       literals in scripts saved without a UTF-8 BOM, and this environment had
#       no way to verify the BOM was written. Keep it ASCII.
#
# ESCAPE HATCH: put HOOK-OK anywhere in the arguments to bypass every check.
#               Use only after the user has explicitly approved the call.

$ErrorActionPreference = 'Stop'

function Deny([string]$message) {
    # Setting [Console]::OutputEncoding has caused exit 255 under redirection
    # in this environment. Write raw bytes instead to sidestep encoding config.
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($message)
    $err = [Console]::OpenStandardError()
    $err.Write($bytes, 0, $bytes.Length)
    $err.Flush()
    exit 2   # 2 = block the call; stderr is fed back to Claude
}

# If the payload cannot be read, pass silently. A broken hook that blocks all
# work is worse than a hook that misses a case.
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $payload = $raw | ConvertFrom-Json
} catch {
    exit 0
}

$tool = [string]$payload.tool_name
try {
    $body = $payload.tool_input | ConvertTo-Json -Depth 20 -Compress
} catch {
    $body = ''
}
if ($null -eq $body) { $body = '' }

# --- escape hatch -----------------------------------------------------------
if ($body -like '*HOOK-OK*') { exit 0 }

# --- 1) MCP tools with a known-good replacement -----------------------------
if ($tool -like '*scene-open') {
    Deny(@"
BLOCKED: scene-open has repeatedly rejected valid paths as "not valid or not
found" (sessions 4, 5, 21).

Use instead, inside script-execute:
    EditorSceneManager.OpenScene(path, OpenSceneMode.Additive)

If you only need to READ the scene, open it Additive and close it WITHOUT
saving, so the user's open scene is left untouched.
"@)
}

if ($tool -like '*console-get-logs') {
    Deny(@"
BLOCKED: console-get-logs returns the entire accumulated buffer (77KB+) on every
call, which blows the token limit and throws encoding errors on Korean text.

Use instead: return verification values as the RETURN STRING of script-execute
rather than via Debug.Log.

If you genuinely need to check whether an exception occurred, log with a unique
tag and parse the saved file with:
    [System.IO.File]::ReadAllText(path, [System.Text.Encoding]::UTF8)
"@)
}

# --- 2) Editor APIs that open a modal ---------------------------------------
$modalApis = @(
    'SaveCurrentModifiedScenesIfUserWantsTo',
    'DisplayDialogComplex',
    'DisplayDialog',
    'OpenFilePanel',
    'SaveFilePanel',
    'OpenFolderPanel'
)
foreach ($api in $modalApis) {
    if ($body -like "*$api*") {
        Deny(@"
BLOCKED: $api opens a modal dialog. Unity freezes and the ENTIRE MCP connection
dies until the user clicks the dialog by hand - session 20 lost 5+ minutes this
way, with no way to recover from this side.

Use instead:
  * saving a scene    -> EditorSceneManager.SaveScene(scene)
  * needing user input -> ask the user in conversation, not via an editor modal
"@)
    }
}

# --- 3) OpenScene that replaces the user's open scene -----------------------
# OpenScene defaults to Single when the mode argument is omitted, so anything
# that does not explicitly say Additive is treated as destructive.
if (($body -like '*OpenScene(*') -and ($body -notlike '*OpenSceneMode.Additive*')) {
    Deny(@"
BLOCKED: OpenScene called without OpenSceneMode.Additive. Omitting the mode
argument defaults to Single.

Session 15 destroyed the user's UNSAVED Title scene decoration this way. It was
before a commit, so it was unrecoverable and the user had to redo the work.

  * Read-only access -> OpenSceneMode.Additive, then close WITHOUT saving.
  * Genuinely need Single -> ask the user whether the scene is saved FIRST,
    then add HOOK-OK to a comment in the code and call again.
"@)
}

exit 0
