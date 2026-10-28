# Installs a staged package folder into the game's ModsLocal folder and deletes the staged copy.
# The copy is checked against the build's SHA-256 list before it replaces anything. The previous
# package goes to FoodTrucksPackageBackups and comes back if the swap fails. Nothing is staged inside
# ModsLocal, because the game loads every folder there and rejects two copies of the same mod.
# Run by tools/install-remote.sh over ssh, or by hand on the game PC.
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Manifest,
    [string]$ModsLocal = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal')
)
$ErrorActionPreference = 'Stop'

# Check components before traversal/copy: a directory junction is a reparse point too.
function Assert-PlainPath([string]$Path) {
    $cursor = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Package path contains a link/reparse point: $cursor"
            }
        }
        $cursor = Split-Path $cursor -Parent
    }
}

function Get-PackageFiles([string]$Directory) {
    foreach ($item in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Package contains a link/reparse point: $($item.FullName)"
        }
        if ($item.PSIsContainer) { Get-PackageFiles $item.FullName }
        else { $item }
    }
}

function Read-PackageManifest([string]$Path) {
    $entries = @{} # Windows filenames are case-insensitive.
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -cnotmatch '^([a-fA-F0-9]{64})\s+\*?(.+)$') { throw 'Invalid SHA-256 manifest line.' }
        $hash = $Matches[1]
        $name = $Matches[2] -creplace '^\./', ''
        foreach ($part in $name.Split('/')) {
            if (!$part -or $part -in '.', '..' -or $part -match '[\x00-\x1f\\:<>"|?*]' -or
                $part -match '[. ]$' -or $part -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') {
                throw "Unsafe manifest path: $name"
            }
        }
        if ($entries.ContainsKey($name)) { throw "Duplicate manifest path: $name" }
        $entries[$name] = $hash
    }
    if (!$entries.ContainsKey('FoodTrucks.dll')) { throw 'Manifest does not list FoodTrucks.dll.' }
    return $entries
}

function Assert-Package([string]$Directory, [hashtable]$Entries) {
    Assert-PlainPath $Directory
    $files = @(Get-PackageFiles $Directory)
    foreach ($file in $files) {
        $name = $file.FullName.Substring($Directory.Length + 1).Replace('\', '/')
        if (!$Entries.ContainsKey($name)) { throw "Unlisted package file: $name" }
        if ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $Entries[$name]) {
            throw "Hash mismatch for $name."
        }
    }
    if ($files.Count -ne $Entries.Count) { throw 'Package is missing files listed in the SHA-256 manifest.' }
}

. (Join-Path $env:USERPROFILE '.big-ambitions-bridge\Control.ps1')
$controlLease=[BridgeControl.LeaseStore]::ForInstance('normal')
$controlPermit=$controlLease.Begin($env:BA_LOCK_TOKEN,'lifecycle')
try {
# Resolve against PowerShell's location, which can differ from the process cwd.
# Preserve junction components until Assert-PlainPath has checked them.
$Source = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Source).TrimEnd('\', '/')
$Manifest = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Manifest)
$ModsLocal = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ModsLocal).TrimEnd('\', '/')
Assert-PlainPath $Source
Assert-PlainPath $Manifest
Assert-PlainPath $ModsLocal
if (!(Test-Path -LiteralPath $Source -PathType Container)) { throw 'Source must be a directory.' }
if (Get-Process 'Big Ambitions' -ErrorAction SilentlyContinue) { throw 'Big Ambitions is running. Close the game before installing.' }

$gameRoot = Split-Path $ModsLocal -Parent
$target = Join-Path $ModsLocal 'FoodTrucks'
$incoming = Join-Path $gameRoot ('FoodTrucks.incoming-' + [Guid]::NewGuid().ToString('N'))
# Source is deleted after success. Refuse any overlap with the destination tree.
if ($Source -eq $gameRoot -or $Source.StartsWith($gameRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $gameRoot.StartsWith($Source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Source and destination trees must not overlap.'
}
Assert-PlainPath $target
$expected = Read-PackageManifest $Manifest
Assert-Package $Source $expected
$backup = $null
try {
    New-Item -ItemType Directory -Force $gameRoot | Out-Null
    Copy-Item -LiteralPath $Source -Destination $incoming -Recurse -Force
    Assert-Package $incoming $expected

    New-Item -ItemType Directory -Force $ModsLocal | Out-Null
    if (Test-Path -LiteralPath $target) {
        $backups = Join-Path $gameRoot 'FoodTrucksPackageBackups'
        Assert-PlainPath $backups
        New-Item -ItemType Directory -Force $backups | Out-Null
        $backup = Join-Path $backups ('FoodTrucks-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
        Move-Item -LiteralPath $target -Destination $backup
    }
    try {
        Move-Item -LiteralPath $incoming -Destination $target
    } catch {
        if ($backup -and !(Test-Path -LiteralPath $target)) { Move-Item -LiteralPath $backup -Destination $target }
        throw
    }
} finally {
    if (Test-Path -LiteralPath $incoming) { Remove-Item -LiteralPath $incoming -Recurse -Force }
}
Remove-Item -LiteralPath $Source -Recurse -Force
Remove-Item -LiteralPath $Manifest -Force
"Installed and verified $($expected.Count) files in $target"

} finally { $controlLease.End($controlPermit) }
