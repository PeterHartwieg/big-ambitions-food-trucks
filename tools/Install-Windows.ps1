# Installs a staged package folder into the game's ModsLocal folder and deletes the staged copy.
# Run by tools/install-remote.sh over ssh, or by hand on the game PC.
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$ModsLocal = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal')
)
$ErrorActionPreference = 'Stop'
$Source = (Resolve-Path $Source).Path
if (Get-Process 'Big Ambitions' -ErrorAction SilentlyContinue) { throw 'Big Ambitions is running. Close the game before installing.' }
if (!(Test-Path (Join-Path $Source 'FoodTrucks.dll'))) { throw "No FoodTrucks.dll in $Source." }
$target = Join-Path $ModsLocal 'FoodTrucks'
if (Test-Path $target) {
    $backups = Join-Path (Split-Path $ModsLocal -Parent) 'FoodTrucksPackageBackups'
    New-Item -ItemType Directory -Force $backups | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    Move-Item $target (Join-Path $backups "FoodTrucks-$stamp")
}
New-Item -ItemType Directory -Force $ModsLocal | Out-Null
Copy-Item $Source $target -Recurse
Remove-Item $Source -Recurse -Force
Get-ChildItem $target -Recurse -File | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.FullName.Substring($target.Length + 1) }
"Installed to $target"
