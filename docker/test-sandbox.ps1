<#
.SYNOPSIS
    Validates the containerised DeepSeek Harness + Godot sandbox end to end.

.DESCRIPTION
    Starts the sandbox through run-sandbox.ps1 (building the image when it is
    missing), then asserts the things the flow actually promises:

      * the container stays up and serves the Web UI on the published port;
      * the Web UI answers on the host loopback;
      * the /api browser-trust fence refuses a non-loopback authority and
        accepts a loopback one (the reason publishing on 127.0.0.1 is enough);
      * godot, dotnet and dsh are installed inside the container at the
        versions this project needs;
      * the orientation file a session reads first is installed at the container
        root, under the name the harness loads;
      * a session can commit and push: the machine's git author is configured in
        the container and its SSH key is installed with a mode ssh accepts;
      * the project's C# assembly builds inside the container;
      * the game boots headlessly.

    Every check is fatal except the two that depend on something outside the
    sandbox: the headless game boot, because a prototype can legitimately fail
    to boot for reasons that have nothing to do with the box, and the installed
    SSH key, because a machine may simply have none. -Strict makes both fatal.

.PARAMETER Port
    Host port the Web UI is published on. Must match run-sandbox.ps1's.

.PARAMETER ImageName
    Image to validate. Defaults to the project's own Dockerfile image; point it
    (with -ContainerName and -DockerFile) at another variant to score that one.

.PARAMETER ContainerName
    Container to validate, and the one run-sandbox.ps1 starts.

.PARAMETER DockerFile
    Dockerfile run-sandbox.ps1 should build; forwarded unchanged.

.PARAMETER PatchFile
    Patch layer run-sandbox.ps1 should install; forwarded unchanged.

.PARAMETER Strict
    Treat the two checks that are warnings by default -- the headless game boot
    and the installed SSH key -- as fatal too.

.EXAMPLE
    .\test-sandbox.ps1

.EXAMPLE
    .\test-sandbox.ps1 -DockerFile Dockerfile.smanx -PatchFile cordis.patch.smanx.yml -Rebuild `
        -ImageName godot-dsh-sandbox-smanx -ContainerName deepseek-godot-sandbox-smanx
    Build and score the community-image variant (see docker/README.md).
#>
[CmdletBinding()]
param(
    [int]$Port = 3081,
    [string]$ImageName = 'godot-dsh-sandbox',
    [string]$ContainerName = 'deepseek-godot-sandbox',
    [string]$DockerFile = 'Dockerfile',
    [string]$PatchFile = 'cordis.patch.yml',
    [switch]$SkipStart,
    [switch]$Rebuild,
    [switch]$Reset,
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$results = New-Object System.Collections.ArrayList

function Add-Result([string]$Name, [bool]$Passed, [string]$Detail, [bool]$Fatal = $true) {
    $null = $results.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail; Fatal = $Fatal })
    $tag = if ($Passed) { 'PASS' } else { if ($Fatal) { 'FAIL' } else { 'WARN' } }
    $colour = if ($Passed) { 'Green' } elseif ($Fatal) { 'Red' } else { 'Yellow' }
    Write-Host ("[{0}] {1}" -f $tag, $Name) -ForegroundColor $colour
    if ($Detail) { Write-Host "       $Detail" -ForegroundColor DarkGray }
}

# Mirrors run-sandbox.ps1's resolution: the installer's user-scoped location
# first, then the older machine-scoped ones, then PATH.
function Resolve-DockerPath {
    foreach ($candidate in @(
            (Join-Path $env:LOCALAPPDATA 'Programs\DockerDesktop\resources\bin\docker.exe'),
            (Join-Path $env:ProgramFiles 'Docker\Docker\Resources\bin\docker.exe'),
            (Join-Path $env:ProgramData 'DockerDesktop\version-bin\docker.exe'))) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    $command = Get-Command 'docker' -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    return $null
}

function Invoke-Captured([string]$File, [string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $raw = @(& $File @Arguments 2>&1)
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    # 'Continue', not 'SilentlyContinue': a silently continued native stderr
    # line never reaches the pipeline, and docker sends everything worth
    # reading (build progress, container stderr, a crash trace) there.
    # ErrorRecords render as their message, not their exception type name, and
    # the @() keeps single-line output an array for .Count below.
    $rendered = foreach ($item in $raw) {
        if ($item -is [System.Management.Automation.ErrorRecord]) { $item.Exception.Message }
        else { "$item" }
    }
    return [pscustomobject]@{ ExitCode = $code; Lines = @($rendered) }
}

function Get-LastLine([object]$Captured) {
    if ($Captured.Lines.Count -eq 0) { return '' }
    return ($Captured.Lines | Where-Object { "$_".Trim() } | Select-Object -Last 1)
}

<#
Runs one command inside the container.

A timeout has to wrap a *shell*, not the command text. `timeout 600 cd /workspace
&& dotnet build` asks `timeout` to execute a program named `cd` -- which does not
exist, because `cd` is a shell builtin -- and the check fails with exit 127 and
"timeout: failed to run command 'cd': No such file or directory", which says
nothing about the project. Timed commands are therefore handed to `bash -c`, with
single quotes in them escaped so they survive the nested quoting.
#>
function Invoke-InContainer([string]$Docker, [string]$Command, [int]$TimeoutSeconds = 0) {
    $inner = if ($TimeoutSeconds -gt 0) {
        "timeout $TimeoutSeconds bash -c '" + ($Command -replace "'", "'\''") + "'"
    } else {
        $Command
    }
    return Invoke-Captured $Docker @('exec', $ContainerName, 'bash', '-lc', $inner)
}

Write-Host ''
Write-Host '=== DSH + Godot sandbox validation ===' -ForegroundColor Cyan

$docker = Resolve-DockerPath
if (-not $docker) { Write-Host 'docker.exe not found.' -ForegroundColor Red; exit 1 }
Write-Host "docker: $docker" -ForegroundColor DarkGray

# The CLI finds docker-credential-desktop and buildx as siblings by name, so
# its own directory has to be on PATH even though the CLI itself is invoked by
# absolute path. Mirrors run-sandbox.ps1.
$dockerDirectory = Split-Path -Parent $docker
if (@($env:PATH -split ';') -notcontains $dockerDirectory) {
    $env:PATH = "$dockerDirectory;$env:PATH"
}

if (-not $SkipStart) {
    Write-Host ''
    Write-Host '--- starting sandbox (run-sandbox.ps1) ---' -ForegroundColor Cyan
    $startArgs = @{
        Port          = $Port
        ImageName     = $ImageName
        ContainerName = $ContainerName
        DockerFile    = $DockerFile
        PatchFile     = $PatchFile
        NoBrowser     = $true
        NoPause       = $true
    }
    if ($Rebuild) { $startArgs['Rebuild'] = $true }
    if ($Reset) { $startArgs['Reset'] = $true }
    & (Join-Path $ScriptDir 'run-sandbox.ps1') @startArgs
    if ($LASTEXITCODE -ne 0) { Write-Host 'run-sandbox.ps1 failed; cannot continue.' -ForegroundColor Red; exit 1 }
    Write-Host ''
}

# ── 1-2. container + published Web UI ────────────────────────────────────────

$running = (Invoke-Captured $docker @('ps', '--format', '{{.Names}}')).Lines -contains $ContainerName
Add-Result 'container is running' $running $ContainerName

# The published port belongs to the container, and is not necessarily the one
# requested: run-sandbox.ps1 falls back to the next free port when the requested
# one is busy, and an existing container keeps the mapping it was created with.
# Asking the container is the only answer that is always right -- probing the
# requested port instead reports HTTP 0 and blames the sandbox for whatever else
# happens to be listening there (a previous run's container, in the run that
# found this).
$published = (Invoke-Captured $docker @('port', $ContainerName, '3080')).Lines |
    Where-Object { "$_".Trim() } | Select-Object -First 1
$hostPort = $Port
if ($published -match ':(?<port>\d+)\s*$') { $hostPort = [int]$Matches['port'] }
if ($hostPort -ne $Port) {
    Write-Host "published port is $hostPort (run-sandbox.ps1 was asked for $Port)" -ForegroundColor DarkGray
}

$url = "http://localhost:$hostPort/"
$status = 0
try {
    $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 10
    $status = $response.StatusCode
} catch {
    $status = 0
}
Add-Result 'Web UI answers on the host loopback' ($status -eq 200) "$url -> HTTP $status"

# ── 3-4. the browser-trust fence ─────────────────────────────────────────────

$foreign = Invoke-InContainer $docker "curl -s -o /dev/null -w '%{http_code}' -H 'Host: evil.example' http://127.0.0.1:3080/api"
$foreignCode = Get-LastLine $foreign
Add-Result 'fence refuses a non-loopback Host' ($foreignCode -eq '403') "Host: evil.example -> HTTP $foreignCode (expected 403)"

$loopback = Invoke-InContainer $docker "curl -s -o /dev/null -w '%{http_code}' -H 'Host: localhost:3080' http://127.0.0.1:3080/api"
$loopbackCode = Get-LastLine $loopback
Add-Result 'fence accepts a loopback Host' ($loopbackCode -ne '403') "Host: localhost:3080 -> HTTP $loopbackCode (expected anything but 403)"

# ── 5-6. toolchain ───────────────────────────────────────────────────────────

$godot = Invoke-InContainer $docker 'godot --version'
$godotLine = Get-LastLine $godot
Add-Result 'godot is the 4.5.1 .NET build' ($godot.ExitCode -eq 0 -and $godotLine -like '4.5.1*mono*') $godotLine

$dotnet = Invoke-InContainer $docker 'dotnet --version'
$dotnetLine = Get-LastLine $dotnet
Add-Result 'dotnet SDK 8 is present' ($dotnet.ExitCode -eq 0 -and $dotnetLine -like '8.*') "dotnet $dotnetLine"

$dsh = Invoke-InContainer $docker 'dsh --version'
$dshLine = Get-LastLine $dsh
Add-Result 'dsh CLI responds' ($dsh.ExitCode -eq 0) $dshLine

# ── 7. the root orientation file ─────────────────────────────────────────────

# A session in this box starts at `/`, so this is the one document a fresh agent
# cannot miss -- and it is spelled `AGENTS.md` because that is the name DSH's
# agent-instructions preset discovers in the workspace root and its ancestors,
# which is what makes it load without anything opening it. Both halves are the
# contract: the file has content, and the reader-facing `README.md` spelling of
# it resolves too (a symlink, so a build that lost either one shows up here
# rather than in a session that then has to go looking for the project).
$orientation = Invoke-InContainer $docker 'test -s /AGENTS.md && test -s /README.md'
Add-Result 'the root orientation file is installed' ($orientation.ExitCode -eq 0) '/AGENTS.md non-empty; /README.md symlink'

# ── 8. the credentials a session pushes with ─────────────────────────────────

# Private keys are identified by their content, not by their name or their mode:
# `id_*` would miss a key named after the host it belongs to, and known_hosts and
# config are legitimately world-readable -- a name- or mode-only rule reports
# them as the very thing it is looking for. (Which is not hypothetical: the first
# draft of this check counted the known_hosts file that `git ls-remote` leaves
# behind.)
$keys = Invoke-InContainer $docker 'find /root/.ssh -maxdepth 1 -type f -exec grep -l "PRIVATE KEY" {} + 2>/dev/null | wc -l'
$keyCount = Get-LastLine $keys
# Warning by default: no key on this machine is a working sandbox that cannot
# push, which run-sandbox.ps1 already says out loud at start. -Strict asks for it
# to be a failure instead.
Add-Result 'an SSH key is installed in the box' ($keyCount -ne '0') "private keys in /root/.ssh: $keyCount" ([bool]$Strict)

# Fatal, and the half worth asserting on its own: ssh refuses a private key
# anyone else can read, and that is exactly what a bind mount of the host's .ssh
# would produce -- an NTFS share reports every file as 777 -- so this is the
# check that fails if the copy in run-sandbox.ps1 is ever replaced by a mount.
#
# `-print` is load-bearing: find adds its implicit print only when the expression
# holds no other action, so with `-exec` in it find prints nothing at all and the
# count is zero whatever the modes are. Without it this check passes on a key
# that ssh would refuse.
$loose = Invoke-InContainer $docker 'find /root/.ssh -maxdepth 1 -type f -exec grep -q "PRIVATE KEY" {} \; -perm /077 -print 2>/dev/null | wc -l'
$looseCount = Get-LastLine $loose
Add-Result 'no private key is readable beyond root' ($looseCount -eq '0') "private keys with group/other bits: $looseCount"

# ── 9. the project builds ────────────────────────────────────────────────────

Write-Host ''
Write-Host '--- building the project inside the container (this is the slow one) ---' -ForegroundColor Cyan
$build = Invoke-InContainer $docker 'cd /workspace && dotnet build MbcPrototype.csproj -v minimal' 600
if ($build.ExitCode -ne 0) {
    Write-Host ($build.Lines | Select-Object -Last 25 | Out-String) -ForegroundColor DarkGray
}
Add-Result 'MbcPrototype.csproj builds in the container' ($build.ExitCode -eq 0) "dotnet build -> exit $($build.ExitCode)"

$assembly = Invoke-InContainer $docker 'ls -1 /workspace/.godot/mono/temp/bin/Debug/MbcPrototype.dll'
Add-Result 'the built assembly is where Godot loads it from' ($assembly.ExitCode -eq 0) (Get-LastLine $assembly)

# ── 10. the game boots headlessly ────────────────────────────────────────────

# Import first. Godot cannot run a project it has never imported -- the main
# scene is a UID and every texture comes from .godot/imported -- so against a
# fresh -godot volume (a new box, or any -Reset) the run below aborted with
# "Unrecognized UID" and exit 1 without booting anything, which made this check
# a warning about a healthy deployment. run-sandbox.ps1 imports when the box
# starts; repeating it here keeps the check meaningful for a container that was
# already running (-SkipStart), and names the cause if it ever fails again.
$import = Invoke-InContainer $docker 'cd /workspace && godot --headless --path /workspace --import' 600
if ($import.ExitCode -ne 0) {
    Write-Host ($import.Lines | Select-Object -Last 25 | Out-String) -ForegroundColor DarkGray
}
Add-Result 'the project imports in the container' ($import.ExitCode -eq 0) "godot --headless --import -> exit $($import.ExitCode)"

$boot = Invoke-InContainer $docker 'cd /workspace && godot --headless --path /workspace --quit-after 60' 300
if ($boot.ExitCode -ne 0) {
    Write-Host ($boot.Lines | Select-Object -Last 25 | Out-String) -ForegroundColor DarkGray
}
# Fatal only with -Strict, which is what the help promises: a prototype can fail
# to boot for reasons that have nothing to do with the sandbox, so by default this
# is a warning. The flag was inverted here -- `-not $Strict` -- which made the
# least sandbox-specific check the one that failed an otherwise healthy run.
Add-Result 'the game boots headlessly' ($boot.ExitCode -eq 0) "godot --headless --quit-after 60 -> exit $($boot.ExitCode)" ([bool]$Strict)

# ── container state, whenever anything failed ────────────────────────────────
# A container that dies *after* serving is invisible to the checks above: every
# docker exec just reports "is not running", and the log that explains why is
# never printed. Dump it once, here.

if (@($results | Where-Object { -not $_.Passed }).Count -gt 0) {
    Write-Host ''
    Write-Host '--- container state and log ---' -ForegroundColor Cyan
    $state = Invoke-Captured $docker @('inspect', '--format',
        'status={{.State.Status}} exit={{.State.ExitCode}} oom={{.State.OOMKilled}} started={{.State.StartedAt}} finished={{.State.FinishedAt}} error={{.State.Error}}', $ContainerName)
    foreach ($line in $state.Lines) { Write-Host "  $line" -ForegroundColor Yellow }
    $log = Invoke-Captured $docker @('logs', '--tail', '60', $ContainerName)
    foreach ($line in $log.Lines) { Write-Host "  $line" -ForegroundColor DarkGray }
}

# ── summary ──────────────────────────────────────────────────────────────────

$fatalFailures = @($results | Where-Object { -not $_.Passed -and $_.Fatal })
$warnings      = @($results | Where-Object { -not $_.Passed -and -not $_.Fatal })

Write-Host ''
Write-Host '=== summary ===' -ForegroundColor Cyan
Write-Host ("  {0} checks, {1} failed, {2} warnings" -f $results.Count, $fatalFailures.Count, $warnings.Count)
Write-Host ''
Write-Host "  Web UI: $url" -ForegroundColor Green
Write-Host ''

if ($fatalFailures.Count -gt 0) { exit 1 }
exit 0
