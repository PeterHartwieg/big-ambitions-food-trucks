# Starts, stops or reports on Big Ambitions on the game PC, for agent-driven test sessions.
# A process started from an ssh session has no desktop, so start runs the game through a one-off scheduled task
# with an interactive logon: it opens in the signed-in user's session, as if started from the Start menu.
param(
    [Parameter(Mandatory = $true)][ValidateSet('start', 'stop', 'status')][string]$Action,
    [string]$GameArgs = '-console -foodtrucks-test'
)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:USERPROFILE 'AppData\LocalLow\Hovgaard Games\Big Ambitions'
$steam = 'C:\Program Files (x86)\Steam\steam.exe'
$appId = '1331550'
$taskName = 'FoodTrucksTestLaunch'

function Get-Game { Get-Process 'Big Ambitions' -ErrorAction SilentlyContinue }

function Show-Status {
    $game = Get-Game
    if ($game) { "Game: running (pid $($game.Id), started $($game.StartTime.ToString('HH:mm:ss')))." } else { 'Game: not running.' }
    $local = Join-Path $root 'ModsLocal'
    $mods = @(Get-ChildItem $local -Directory -ErrorAction SilentlyContinue | ForEach-Object Name)
    "Local mods: $(if ($mods.Count) { $mods -join ', ' } else { 'none' })."
    if (Test-Path (Join-Path $root 'FoodTrucksParked\FoodTrucks')) { 'Food Trucks: parked in FoodTrucksParked.' }
    elseif ($mods -contains 'FoodTrucks') { 'Food Trucks: installed in ModsLocal.' }
    else { 'Food Trucks: not installed.' }
    $session = (query user 2>$null | Select-String 'console' | Select-Object -First 1)
    if ($session) { "Desktop: $(($session.ToString() -replace '\s+', ' ').Trim())." } else { 'Desktop: nobody signed in at the console.' }
}

switch ($Action) {
    'status' { Show-Status }
    'start' {
        if (Get-Game) { throw 'Big Ambitions is already running. Stop it first (run-remote.sh stop).' }
        if (!(Test-Path $steam)) { throw "Steam not found at $steam." }
        if (!(query user 2>$null | Select-String 'Active')) { throw 'Nobody is signed in on the game PC, so the game has no desktop to open on.' }
        $user = "$env:USERDOMAIN\$env:USERNAME"
        $launch = New-ScheduledTaskAction -Execute $steam -Argument "-applaunch $appId $GameArgs"
        $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
        Register-ScheduledTask -TaskName $taskName -Action $launch -Principal $principal -Force | Out-Null
        try {
            Start-ScheduledTask -TaskName $taskName
            $deadline = (Get-Date).AddSeconds(90)
            while (!(Get-Game) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2 }
        } finally {
            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        }
        $game = Get-Game
        if (!$game) { throw 'The game did not start within 90 seconds. Steam may be showing a dialog on the desktop.' }
        "Game started (pid $($game.Id)) with: $GameArgs"
    }
    'stop' {
        $game = Get-Game
        if (!$game) { 'Game: not running.'; break }
        # The bridge's quit is tried first by run-remote.sh; this is the fallback.
        $game | Stop-Process -Force
        $game | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue
        'Game stopped (killed). Anything not saved in game is lost.'
    }
}
