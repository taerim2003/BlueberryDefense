# notion-kanban.ps1 -- extract the "POC 할일" kanban rows from a saved MCP search result.
#
# WHY THIS EXISTS
#   The Notion MCP server speaks the OLD Notion API but exposes the NEW data-source tool set,
#   so API-query-data-source / API-retrieve-a-data-source both return 400 invalid_request_url.
#   The only working discovery path is API-post-search over the whole workspace, whose response
#   is ~150KB. The harness spills that to a file instead of the context window; this script
#   reads that file and prints just the board.
#
# USAGE (2 steps, always in this order)
#   1) Call the MCP tool:
#        API-post-search  filter={"property":"object","value":"page"}
#                         sort={"direction":"descending","timestamp":"last_edited_time"}
#                         page_size=100
#      It will overflow and report a saved file path. Ignore the body.
#   2) powershell -File .claude/scripts/notion-kanban.ps1
#      (no args = newest saved search result for this project)
#
#   -Path <file>   parse only this file instead of merging recent ones
#   -Ids           also print page ids (needed for API-patch-page / retrieve-page-markdown)
#   -WithinHours N how far back to merge saved dumps (default 24)
#
# WHY IT MERGES INSTEAD OF TAKING THE NEWEST FILE
#   Saved dumps are not all full-board dumps -- a narrower search (or a parallel Claude Code
#   session on this project, since the glob spans every session dir) leaves a partial file that
#   is newer. Taking "the newest file" showed 2 of 5 cards once. So: read every dump in the
#   window, group rows by page id, and keep each card's most recently edited sighting.
#
# ENCODING: this file MUST stay UTF-8 *with BOM*. PowerShell 5.1 reads BOM-less files as ANSI
#       and would mangle the Korean status names in $order below. If you rewrite it with a tool
#       that strips the BOM, restore it:
#         $r=Get-Content $f -Raw -Encoding UTF8
#         [IO.File]::WriteAllText($f,$r,(New-Object Text.UTF8Encoding($true)))
#       Page properties are still looked up by TYPE ('title'/'status'), never by their Korean
#       names -- that part stays encoding-proof, and it also survives a property rename in Notion.

[CmdletBinding()]
param(
    [string]$Path,
    [string]$DatabaseId = '3b06394c-d983-803d-af4a-c26f924121b7',
    [switch]$Ids,
    [int]$WithinHours = 24
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

if ($Path) {
    $files = @(Get-Item -LiteralPath $Path)
} else {
    $glob = Join-Path $env:USERPROFILE '.claude\projects\d--unity-BlueberryDefense\*\tool-results\mcp-notion-API-post-search-*.txt'
    $cutoff = (Get-Date).AddHours(-$WithinHours)
    $files = @(Get-ChildItem -Path $glob -ErrorAction SilentlyContinue |
               Where-Object { $_.LastWriteTime -ge $cutoff } | Sort-Object LastWriteTime)
    if ($files.Count -eq 0) {
        Write-Error ("No API-post-search result saved in the last {0}h. Run step 1 (see header) first." -f $WithinHours)
    }
}

$seen = @{}          # page id -> row (most recently edited sighting wins)
$truncated = $false
foreach ($f in $files) {
    $doc = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($doc.has_more) { $truncated = $true }
    foreach ($r in $doc.results) {
        if ($r.parent.database_id -ne $DatabaseId) { continue }

        $title = '(untitled)'
        $state = '(no status)'
        foreach ($p in $r.properties.PSObject.Properties) {
            switch ($p.Value.type) {
                'title'  { $t = ($p.Value.title | ForEach-Object { $_.plain_text }) -join ''
                           if ($t) { $title = $t } }
                'status' { if ($p.Value.status) { $state = $p.Value.status.name } }
            }
        }
        # Files are walked oldest -> newest, so ties must go to the LATER file (-gt, not -ge):
        # an unchanged card has the same last_edited_time in every dump, and we want From to
        # name the freshest dump that still contains it.
        $prev = $seen[$r.id]
        if ($prev -and $prev.Edited -gt $r.last_edited_time) { continue }
        $seen[$r.id] = [pscustomobject]@{
            State = $state; Title = $title; Id = $r.id
            Edited = $r.last_edited_time; From = $f.Name
        }
    }
}
$rows = @($seen.Values)

$newest = ($files | Select-Object -Last 1)
$age = [int]((Get-Date) - $newest.LastWriteTime).TotalMinutes
Write-Output ("Dumps : {0} file(s), newest {1} min old" -f $files.Count, $age)
Write-Output ("Cards : {0}" -f $rows.Count)
if ($rows.Count -eq 0) {
    Write-Output 'No rows matched. Wrong DatabaseId, or the dumps predate the cards.'
    return
}
# A card marked "(older dump)" is missing from the freshest dump. Search never returns trashed
# pages, so the usual cause is that the card was DELETED -- merging would otherwise resurrect it.
# (Observed once: a test card deleted minutes after creation kept showing up.) Confirm with
# API-retrieve-a-page and check in_trash before treating such a card as real work.
$stale = @($rows | Where-Object { $_.From -ne $newest.Name })
if ($stale.Count -gt 0) {
    Write-Output ("Note  : {0} card(s) missing from the newest dump -- likely DELETED." -f $stale.Count)
    Write-Output '        Verify with API-retrieve-a-page (in_trash) before acting on them.'
}

# Fixed board order; anything unexpected falls to the end so a renamed status is visible, not hidden.
$order = @('시작 전', '진행 중', '완료')
foreach ($s in ($order + ($rows.State | Where-Object { $order -notcontains $_ } | Select-Object -Unique))) {
    $group = @($rows | Where-Object { $_.State -eq $s })
    if ($group.Count -eq 0) { continue }
    Write-Output ''
    Write-Output ("== {0} ({1})" -f $s, $group.Count)
    foreach ($c in ($group | Sort-Object Edited -Descending)) {
        $mark = if ($c.From -ne $newest.Name) { ' (older dump)' } else { '' }
        if ($Ids) { Write-Output ("  - {0}{1}  [{2}]" -f $c.Title, $mark, $c.Id) }
        else      { Write-Output ("  - {0}{1}" -f $c.Title, $mark) }
    }
}

if ($truncated) {
    Write-Output ''
    Write-Output 'WARNING: at least one dump was truncated (has_more=true). A card not edited recently'
    Write-Output '         can fall outside the 100 most-recent pages and be missing entirely.'
}
