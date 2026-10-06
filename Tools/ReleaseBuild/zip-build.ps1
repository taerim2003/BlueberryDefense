# Zip a build folder so the archive holds one top-level "Build/" folder (same layout as past test builds).
# usage: powershell -NoProfile -ExecutionPolicy Bypass -File zip-build.ps1 -BuildDir <...\Build> -Dst <out.zip>
# Entry names are written with "/" on purpose: PS 5.1 ZipFile.CreateFromDirectory writes "\" (some unzip tools
# then flatten the folders), and Windows tar.exe crashed midway on this build. ASCII only (PS 5.1 reads no-BOM as ANSI).
param([string]$BuildDir, [string]$Dst)
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$BuildDir = (Resolve-Path $BuildDir).Path.TrimEnd('\')
$parent = Split-Path $BuildDir -Parent
$top = Split-Path $BuildDir -Leaf
if ($top -ne 'Build') { throw "BuildDir must be a folder named Build: $BuildDir" }
if (Test-Path $Dst) { Remove-Item $Dst -Confirm:$false }
$fs = [System.IO.File]::Open($Dst, [System.IO.FileMode]::CreateNew)
$zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
$n = 0
try {
  Get-ChildItem -Path $BuildDir -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($parent.Length + 1).Replace('\', '/')
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $rel, [System.IO.Compression.CompressionLevel]::Optimal)
    $n++
  }
} finally { $zip.Dispose(); $fs.Dispose() }
"entries=$n size=" + (Get-Item $Dst).Length
