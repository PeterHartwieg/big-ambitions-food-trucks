# Windows entry for build.py: builds AgentRigInput.dll against an explicit Managed folder; installs nothing.
param(
    [Parameter(Mandatory=$true)][string]$GameManaged,
    [Parameter(Mandatory=$true)][string]$Output,
    [string]$Dotnet='dotnet'
)
$ErrorActionPreference='Stop'
& python (Join-Path $PSScriptRoot 'build.py') --game-managed $GameManaged --output $Output --dotnet $Dotnet
if ($LASTEXITCODE -ne 0) { throw "AgentRigInput build failed ($LASTEXITCODE)" }
