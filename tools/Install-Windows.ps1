# Installs a staged package folder into the game's ModsLocal folder and deletes the staged copy.
# The previous package is kept in FoodTrucksPackageBackups and restored if the install fails.
# Run by tools/install-remote.sh over ssh, or by hand on the game PC.
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$ModsLocal = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal')
)
$ErrorActionPreference = 'Stop'
$Source = (Resolve-Path $Source).Path
if (Get-Process 'Big Ambitions' -ErrorAction SilentlyContinue) { throw 'Big Ambitions is running. Close the game before installing.' }
$required = @('FoodTrucks.dll', 'Dependencies\0Harmony.dll', 'Dependencies\FoodTrucks.Core.dll', 'AssetBundles\Windows\foodtrucks.unity3d', 'Locales\en.json')
foreach ($file in $required) { if (!(Test-Path (Join-Path $Source $file))) { throw "Staged package is missing $file." } }

New-Item -ItemType Directory -Force $ModsLocal | Out-Null
$target = Join-Path $ModsLocal 'FoodTrucks'
$incoming = Join-Path $ModsLocal 'FoodTrucks.incoming'
if (Test-Path $incoming) { Remove-Item $incoming -Recurse -Force }
Copy-Item $Source $incoming -Recurse

$backup = $null
if (Test-Path $target) {
    $backups = Join-Path (Split-Path $ModsLocal -Parent) 'FoodTrucksPackageBackups'
    New-Item -ItemType Directory -Force $backups | Out-Null
    $backup = Join-Path $backups ('FoodTrucks-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Move-Item $target $backup
}
try {
    Move-Item $incoming $target
} catch {
    if ($backup) { Move-Item $backup $target }
    throw
}
Remove-Item $Source -Recurse -Force
Get-ChildItem $target -Recurse -File | Sort-Object FullName | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.FullName.Substring($target.Length + 1) }
"Installed to $target"
