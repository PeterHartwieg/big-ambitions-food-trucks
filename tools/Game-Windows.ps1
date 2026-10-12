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

# True when the running game was started with the test flag, so it is ours to stop.
function Test-TestGame {
    $proc = Get-CimInstance Win32_Process -Filter "Name='Big Ambitions.exe'" -ErrorAction SilentlyContinue | Select-Object -First 1
    return [bool]($proc -and $proc.CommandLine -match '-foodtrucks-test')
}

function Show-Status {
    $game = Get-Game
    if ($game) {
        $kind = if (Test-TestGame) { 'with -foodtrucks-test' } else { 'without the test flag' }
        "Game: running (pid $($game.Id), started $($game.StartTime.ToString('HH:mm:ss')), $kind)."
    } else { 'Game: not running.' }
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
        $user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
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
        # Only run-remote.sh stop --force gets here, after the bridge's quit didn't close the game.
        $game = Get-Game
        if (!$game) { 'Game: not running.'; break }
        if (!(Test-TestGame)) { throw 'The running game was started without -foodtrucks-test, so it is not a test session. Not stopped.' }
        $game | Stop-Process -Force
        $game | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue
        'Game stopped (killed). Anything not saved in game is lost, and a save being written may be damaged.'
    }
}
