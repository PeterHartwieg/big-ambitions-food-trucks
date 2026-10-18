"""Shared instance ownership for the bridge clients and offline Windows lifecycle tools."""
import base64
import hashlib
import json
import os
from pathlib import Path
import secrets
import subprocess

TOOLS = Path(__file__).resolve().parents[1]
HOST = os.environ.get('BA_GAME_HOST', 'homepc')

def instance_for_url(url):
    from urllib.parse import urlparse
    port = urlparse(url).port
    if port == 28330: return 'agentmac'
    if port == 18330: return 'agent1'
    if port == 8330: return 'normal'
    return os.environ.get('BA_BRIDGE_INSTANCE', 'normal')

def token(instance, create=False, owner=None):
    direct = os.environ.get('BA_LOCK_TOKEN')
    if direct: return direct
    owner = owner or os.environ.get('BA_LOCK_OWNER')
    if not owner:
        if create: raise RuntimeError('Set BA_LOCK_OWNER to a unique name for this agent/session before acquiring a lock')
        return None
    key = hashlib.sha256(json.dumps(['local-mac' if instance == 'agentmac' else HOST, instance, owner]).encode()).hexdigest()
    directory = Path(os.environ.get('BA_LOCK_TOKEN_DIR', Path.home()/'.big-ambitions-bridge/client-tokens'))
    path = directory/(key+'.json')
    if create:
        directory.mkdir(parents=True, exist_ok=True, mode=0o700)
        try:
            fd = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
        except FileExistsError: pass
        else:
            with os.fdopen(fd, 'w') as output: json.dump({'token':secrets.token_hex(32)}, output)
    return json.loads(path.read_text())['token'] if path.exists() else None

def literal(value): return "'"+str(value).replace("'", "''")+"'"

def remote(script, payload=None, timeout=45):
    encoded = base64.b64encode(script.encode('utf-16le')).decode()
    result = subprocess.run(['ssh','-o','BatchMode=yes','-o','ConnectTimeout=8',HOST,
        'powershell -NoProfile -NonInteractive -EncodedCommand '+encoded],
        input='' if payload is None else json.dumps(payload), text=True, capture_output=True, timeout=timeout)
    if result.returncode: raise RuntimeError(result.stderr.strip() or result.stdout.strip())
    return result.stdout.strip().lstrip('\ufeff')

def provision():
    """Install immutable controller DLLs outside either game. Never change a running game's package."""
    dll = TOOLS/'bridge-locks/bin/Release/netstandard2.0/BridgeControl.dll'
    if not dll.exists(): raise RuntimeError('Build tools/bridge-locks/BridgeControl.csproj first')
    newtonsoft = Path.home()/'.nuget/packages/newtonsoft.json/13.0.2/lib/net45/Newtonsoft.Json.dll'
    digest = hashlib.sha256(dll.read_bytes()+newtonsoft.read_bytes()).hexdigest()[:24]
    directory = '.big-ambitions-bridge/tools/'+digest
    script = "$ErrorActionPreference='Stop'; $dir=Join-Path $env:USERPROFILE "+literal(directory)+"; New-Item -ItemType Directory -Force $dir | Out-Null; Test-Path (Join-Path $dir 'ready')"
    ready = remote(script) == 'True'
    if not ready:
        for file in [dll,newtonsoft]:
            subprocess.run(['scp','-q',str(file),HOST+':'+directory+'/'+file.name],check=True,timeout=60)
        remote("$ErrorActionPreference='Stop'; Set-Content (Join-Path $env:USERPROFILE "+literal(directory+'/ready')+") 'ready'")
    # Stable loader points to an immutable DLL pair. Already running PowerShell callers keep their version.
    loader = "[void][Reflection.Assembly]::LoadFrom((Join-Path $env:USERPROFILE '"+directory+"/Newtonsoft.Json.dll'))\n[void][Reflection.Assembly]::LoadFrom((Join-Path $env:USERPROFILE '"+directory+"/BridgeControl.dll'))\n"
    remote("$ErrorActionPreference='Stop'; [IO.File]::WriteAllText((Join-Path $env:USERPROFILE '.big-ambitions-bridge/Control.ps1'), ([Console]::In.ReadToEnd() | ConvertFrom-Json))", loader)

PREFIX = r"""$ErrorActionPreference='Stop'
# Ownership tokens arrive over stdin, never in command-line arguments or logs.
$inputData=[Console]::In.ReadToEnd() | ConvertFrom-Json
. (Join-Path $env:USERPROFILE '.big-ambitions-bridge\Control.ps1')
"""

def lock_action(instance, action, owner=None, purpose=''):
    credential=token(instance, create=action=='acquire', owner=owner)
    if action != 'status' and not credential: raise RuntimeError('Acquire a lock with BA_LOCK_OWNER set before controlling the game')
    script=PREFIX+r'''
$lease=[BridgeControl.LeaseStore]::ForInstance($inputData.instance)
switch ($inputData.action) {
 'status' { $lease.StatusJson() }
 'acquire' { $lease.AcquireJson($inputData.owner,$inputData.purpose,$inputData.token) }
 'release' { $lease.ReleaseJson($inputData.token) }
 'assert' { $lease.AssertOwner($inputData.token); 'Owner verified' }
}
'''
    return remote(script, {'instance':instance,'action':action,'owner':owner or os.environ.get('BA_LOCK_OWNER'), 'purpose':purpose,'token':credential})

def invoke(script_path, parameters=None, instance='normal', timeout=180, guarded=False):
    credential=token(instance)
    script=PREFIX+r'''
$env:BA_LOCK_TOKEN=$inputData.token
$params=@{}; $inputData.parameters.psobject.Properties | ForEach-Object { $params[$_.Name]=$_.Value }
$scriptPath=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($inputData.script)
'''
    if guarded:
        script += r'''
$lease=[BridgeControl.LeaseStore]::ForInstance($inputData.instance)
$permit=$lease.Begin($inputData.token,'lifecycle')
try { & $scriptPath @params } finally { $lease.End($permit) }
'''
    else: script += '& $scriptPath @params\n'
    return remote(script, {'script':script_path,'parameters':parameters or {},'instance':instance,'token':credential},timeout)
