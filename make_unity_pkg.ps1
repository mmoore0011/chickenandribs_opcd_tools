<#
.SYNOPSIS
    Creates a .unitypackage from an mm_tools subdirectory.

.DESCRIPTION
    Walks all Unity-relevant files under the specified subdirectory, assigns
    each a GUID and the correct meta importer type, then packs everything into
    a .unitypackage alongside the source folder.

    Package root detection:
      If the directory contains an "Assets/" subfolder the package paths are
      taken from that subfolder's contents directly (e.g. Assets/BirdFlock/...).
      Otherwise every file is placed under Assets/<Dir>/... in the package,
      preserving any subfolders (Editor/, Scripts/, etc.) that exist in source.

    Files skipped automatically:
      - .blend / .blend1 / .fbm directories (Blender source)
      - .mp4 / .mov / .avi (video source)
      - .zip / .unitypackage (archives)
      - .meta (Unity regenerates these on import)
      - Files or folders starting with "decom_"
      - .claude/ directories and settings.local.json
      - MEMORY.md

.PARAMETER Dir
    Name of a subdirectory of this script's parent folder
    (e.g. "bird_flock", "SkyDome", "Fountain").

.EXAMPLE
    .\make_unity_pkg.ps1 bird_flock
    Creates:  bird_flock\bird_flock.unitypackage

.EXAMPLE
    .\make_unity_pkg.ps1 SkyDome
    Creates:  SkyDome\skydome.unitypackage
#>

param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Dir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# UTF-8 without BOM — Unity reads pathname files as raw bytes; a BOM prefix
# produces an invisible U+FEFF character at the start of the path, causing
# Unity to create a phantom "Assets" folder instead of using the existing one.
$utf8 = [System.Text.UTF8Encoding]::new($false)

# ── Resolve paths ─────────────────────────────────────────────────────────────
$toolsRoot = $PSScriptRoot
$toolPath  = Join-Path $toolsRoot $Dir

if (-not (Test-Path $toolPath -PathType Container)) {
    Write-Error "Directory not found: $toolPath"
    exit 1
}

$outPkg = Join-Path $toolPath ($Dir.ToLower() + '.unitypackage')

# ── Temp workspace (auto-cleaned on exit) ─────────────────────────────────────
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) "unity_pkg_$([System.Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force $tmp | Out-Null

# ── Source root and package path prefix ───────────────────────────────────────
$assetsDir = Join-Path $toolPath 'Assets'
if (Test-Path $assetsDir -PathType Container) {
    $sourceRoot = $assetsDir
    $pkgBase    = 'Assets'          # paths already contain the package subfolder name
} else {
    $sourceRoot = $toolPath
    $pkgBase    = "Assets/$Dir"     # wrap everything under Assets/<Dir>/
}

# ── Exclusion rules ───────────────────────────────────────────────────────────
$excludeExts   = [System.Collections.Generic.HashSet[string]]@(
    '.blend', '.blend1', '.mp4', '.mov', '.avi',
    '.zip', '.unitypackage', '.meta'
)
$excludeNames  = [System.Collections.Generic.HashSet[string]]@(
    'MEMORY.md', 'settings.local.json', 'package.json'
)

function Test-Excluded([System.IO.FileInfo]$f) {
    # Extension check
    if ($excludeExts.Contains($f.Extension.ToLower())) { return $true }
    # File name check
    if ($excludeNames.Contains($f.Name))               { return $true }
    # Name prefix check
    if ($f.Name.StartsWith('decom_'))                  { return $true }
    # Directory segment check (handles .claude/ and *.fbm/ at any depth)
    $segments = $f.FullName.Split([System.IO.Path]::DirectorySeparatorChar)
    foreach ($seg in $segments) {
        if ($seg -eq '.claude')     { return $true }
        if ($seg.EndsWith('.fbm'))  { return $true }
        if ($seg.StartsWith('decom_') -and $seg -ne $f.Name) { return $true }
    }
    return $false
}

# ── Meta content by file type ─────────────────────────────────────────────────
function Get-Meta([string]$ext, [string]$guid, [bool]$isFolder) {
    $h = "fileFormatVersion: 2`nguid: $guid`n"
    if ($isFolder) {
        return $h + "folderAsset: yes`nDefaultImporter:`n  externalObjects: {}`n  userData: `n  assetBundleName: `n  assetBundleVariant:`n"
    }
    switch ($ext) {
        '.cs'     { return $h + "MonoImporter:`n  externalObjects: {}`n  serializedVersion: 2`n  defaultReferences: []`n  executionOrder: 0`n  icon: {instanceID: 0}`n  userData: `n  assetBundleName: `n  assetBundleVariant:`n" }
        '.shader' { return $h + "ShaderImporter:`n  externalObjects: {}`n  defaultTextures: []`n  nonModifiableTextures: []`n  userData: `n  assetBundleName: `n  assetBundleVariant:`n" }
        '.json'   { return $h + "TextScriptImporter:`n  userData: `n  assetBundleName: `n  assetBundleVariant:`n" }
        { $_ -in @('.png', '.jpg', '.jpeg') } {
            return $h + "TextureImporter:`n  serializedVersion: 4`n  mipmaps:`n    enableMipmapStreaming: 0`n  bumpmap:`n    convertToNormalMap: 0`n  userData: `n  assetBundleName: `n  assetBundleVariant:`n"
        }
        { $_ -in @('.mp3', '.wav', '.ogg') } {
            return $h + "AudioImporter:`n  externalObjects: {}`n  userData: `n  assetBundleName: `n  assetBundleVariant:`n"
        }
        '.fbx'    { return $h + "ModelImporter:`n  serializedVersion: 19300`n  userData: `n  assetBundleName: `n  assetBundleVariant:`n" }
        default   { return $h + "DefaultImporter:`n  externalObjects: {}`n  userData: `n  assetBundleName: `n  assetBundleVariant:`n" }
    }
}

function New-G { return [System.Guid]::NewGuid().ToString('N') }

function Write-Entry([string]$g, [string]$pkgPath, [string]$metaContent, [byte[]]$assetBytes) {
    $d = Join-Path $tmp $g
    New-Item -ItemType Directory -Force $d | Out-Null
    [System.IO.File]::WriteAllText("$d\pathname",   $pkgPath,     $utf8)
    [System.IO.File]::WriteAllText("$d\asset.meta", $metaContent, $utf8)
    if ($null -ne $assetBytes) {
        [System.IO.File]::WriteAllBytes("$d\asset", $assetBytes)
    }
}

# ── Collect files, derive package paths, gather required folder paths ──────────
$fileList  = [System.Collections.Generic.List[hashtable]]::new()
$folderSet = [System.Collections.Generic.HashSet[string]]::new()

foreach ($f in (Get-ChildItem $sourceRoot -Recurse -File)) {
    if (Test-Excluded $f) { continue }

    $rel     = $f.FullName.Substring($sourceRoot.Length).TrimStart('\', '/').Replace('\', '/')
    $pkgPath = "$pkgBase/$rel"
    $fileList.Add(@{ src = $f.FullName; pkg = $pkgPath; ext = $f.Extension.ToLower() })

    # Register every intermediate folder (skip bare "Assets" root)
    $parts = $pkgPath.Split('/')
    for ($i = 2; $i -lt $parts.Length; $i++) {
        $folderSet.Add(($parts[0..($i - 1)] -join '/')) | Out-Null
    }
}

if ($fileList.Count -eq 0) {
    Write-Warning "No packagable files found under: $sourceRoot"
    exit 0
}

# ── Print plan ────────────────────────────────────────────────────────────────
Write-Output "Source : $sourceRoot"
Write-Output "Base   : $pkgBase"
Write-Output "Output : $outPkg"
Write-Output ''

# ── Write folder entries ──────────────────────────────────────────────────────
foreach ($fp in ($folderSet | Sort-Object)) {
    Write-Entry (New-G) $fp (Get-Meta '' (New-G) $true) $null
}

# ── Write file entries ────────────────────────────────────────────────────────
foreach ($e in $fileList) {
    Write-Entry (New-G) $e.pkg (Get-Meta $e.ext (New-G) $false) ([System.IO.File]::ReadAllBytes($e.src))
    Write-Output "  + $($e.pkg)"
}

# ── Merge embedded .unitypackage files found inside the source root ───────────
# Embedded packages are extracted, and their GUID entries (pathname + asset.meta
# + asset) are copied verbatim into the workspace, preserving the original GUIDs
# so internal cross-references (e.g. prefab → mesh/texture) stay intact.
#
# This folder may also contain THIS TOOL'S OWN previous build output (the
# .unitypackage this script itself produces each run, sitting right next to
# the source files). That file is never a "legitimate embedded resource" —
# it's skipped outright by full path, regardless of what paths it contains,
# so a renamed/moved source file can't resurrect its old stale entry (a pure
# path-collision check wouldn't catch that: the old entry's pathname simply
# no longer matches anything in the fresh scan, so it would otherwise slip
# through untouched forever, re-embedded on every future run).
#
# As a second, independent guard: any pathname the fresh scan already
# claimed is never re-embedded either, no matter which .unitypackage it came
# from — this covers genuinely different embedded packages that happen to
# collide with a freshly-scanned path.
$claimedPaths = [System.Collections.Generic.HashSet[string]]::new()
foreach ($fp in $folderSet)      { $claimedPaths.Add($fp) | Out-Null }
foreach ($e in $fileList)        { $claimedPaths.Add($e.pkg) | Out-Null }

$outPkgFullName = [System.IO.Path]::GetFullPath($outPkg)

$embCount = 0
foreach ($embPkg in (Get-ChildItem $sourceRoot -Recurse -Filter "*.unitypackage")) {
    if ($embPkg.FullName -eq $outPkgFullName) { continue }
    $embTmp = Join-Path ([System.IO.Path]::GetTempPath()) "unity_embed_$([System.Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Force $embTmp | Out-Null
    tar -xzf $embPkg.FullName -C $embTmp 2>$null
    foreach ($guidDir in (Get-ChildItem $embTmp -Directory)) {
        $pnFile = Join-Path $guidDir.FullName "pathname"
        if (-not (Test-Path $pnFile)) { continue }
        $pkgPath = [System.IO.File]::ReadAllText($pnFile, $utf8).Trim()
        if ($claimedPaths.Contains($pkgPath)) { continue }
        $metaFile    = Join-Path $guidDir.FullName "asset.meta"
        $assetFile   = Join-Path $guidDir.FullName "asset"
        if (-not (Test-Path $metaFile)) { continue }
        $metaContent = [System.IO.File]::ReadAllText($metaFile)
        $assetBytes  = if (Test-Path $assetFile) { [System.IO.File]::ReadAllBytes($assetFile) } else { $null }
        Write-Entry $guidDir.Name $pkgPath $metaContent $assetBytes
        Write-Output "  + $pkgPath  [from $($embPkg.Name)]"
        $claimedPaths.Add($pkgPath) | Out-Null
        $embCount++
    }
    Remove-Item -Recurse -Force $embTmp
}

# ── Pack ──────────────────────────────────────────────────────────────────────
$nFolders = $folderSet.Count
$nFiles   = $fileList.Count
Write-Output ''
Write-Output "Packing $($nFolders + $nFiles + $embCount) entries ($nFolders folders, $nFiles files, $embCount embedded)..."
# Pass GUID directory names explicitly so tar omits the leading "./" that
# Unity's package importer does not handle (causing duplicate Assets folders).
Push-Location $tmp
$guidDirs = (Get-ChildItem -Directory | Select-Object -ExpandProperty Name)
tar -czf $outPkg $guidDirs
Pop-Location
$sizeMB = [Math]::Round((Get-Item $outPkg).Length / 1MB, 2)
Write-Output "Done: $outPkg ($sizeMB MB)"
