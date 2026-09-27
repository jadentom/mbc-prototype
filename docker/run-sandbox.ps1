<#
.SYNOPSIS
    Starts the isolated DeepSeek Harness + Godot sandbox and opens its Web UI.

.DESCRIPTION
    Builds (when needed) the image from the Dockerfile next to this script,
    starts the container with the project mounted at /workspace, waits until the
    Harness Web UI answers, and opens it in the default browser.

    Three things about this box are deliberate and worth knowing before editing:

      * The Web server binds 0.0.0.0 *inside* the container, because Docker
        publishes a port by forwarding to the container's interface address,
        never its loopback. That override lives in docker/cordis.patch.yml,
        which is copied into the container's DSH home on every run; the CLI's
        own `--host 0.0.0.0` is refused by design.
      * The published port is bound to the host loopback only
        (127.0.0.1:<port>). The Harness has no authentication layer, so
        reachability is the whole security boundary. Do not widen this.
      * This machine's git author, and the SSH key in %USERPROFILE%\.ssh, are
        copied into the container on every run, so a session can commit and push
        by itself. That widens what the box holds: -NoGitCredentials skips it,
        and a deploy key scoped to this repository is the tighter answer if the
        box should be able to push and nothing else.

    Written for Windows PowerShell 5.1 (the version this machine ships).

.PARAMETER Port
    Host port for the Web UI. Defaults to 3081 so it cannot collide with a
    harness already serving on 3080 on this machine. If the requested port is
    busy the next free one is used.

.PARAMETER ProjectPath
    Project directory mounted at /workspace inside the container. Defaults to
    this repository (the parent of this script's folder).

.PARAMETER DshHome
    Host directory mounted at /dsh-home: the container's DSH home, holding
    settings, session history, credentials and the patch layer. Defaults to
    %USERPROFILE%\.dsh-docker. Delete it for a factory-fresh harness.

.PARAMETER DockerFile
    Dockerfile to build, by name in this folder. Defaults to Dockerfile.
    Dockerfile.smanx builds the community-image variant, which needs its own
    ImageName/ContainerName so both images can coexist (see docker/README.md).

.PARAMETER PatchFile
    Patch layer copied into the container's DSH home as cordis.patch.yml.
    Defaults to cordis.patch.yml. The smanx variant needs
    cordis.patch.smanx.yml: that image's entrypoint puts a reverse proxy on
    3080, so DSH must not also bind it.

.PARAMETER Rebuild
    Rebuild the image even when it already exists.

.PARAMETER Recreate
    Delete and recreate the container even when it is current.

.PARAMETER Reset
    Delete the container and its build-cache volumes (obj, bin, .godot), then
    recreate. Use when a build behaves oddly.

.PARAMETER NoBrowser
    Do not open a browser window.

.PARAMETER NoGitCredentials
    Do not copy this machine's git author and SSH material into the container.
    Use it when the box should hold no key of yours: sessions can still commit,
    but a push has to happen from this machine. By default the key in
    -SshDirectory is copied in (mode 600) on every run, because a container is
    where the harness sessions live and they are the ones that push.

.PARAMETER SshDirectory
    Directory whose contents become the container's /root/.ssh. Defaults to
    %USERPROFILE%\.ssh. Point it at a directory holding a deploy key scoped to
    this repository -- with its own `config` naming it for github.com -- when the
    box should be able to push and do nothing else with your GitHub account.

.PARAMETER NoPause
    Do not wait for Enter before the window closes.

.PARAMETER CreateShortcut
    Create (or refresh) a shortcut that runs this script, then exit. It is
    written to -ShortcutPath, which defaults to the Desktop.

.PARAMETER ShortcutPath
    Where -CreateShortcut writes the .lnk. Point it somewhere writable (for
    example this folder) when the Desktop is not, then copy the file out. The
    shortcut targets this script by absolute path, so it keeps working after
    being moved.

.EXAMPLE
    .\run-sandbox.ps1
    Build if needed, start the container, open the Web UI.

.EXAMPLE
    .\run-sandbox.ps1 -Rebuild -Reset -Port 3082
    Rebuild the image and start from a clean build cache on port 3082.
#>
[CmdletBinding()]
param(
    [int]$Port = 3081,
    [string]$ImageName = 'godot-dsh-sandbox',
    [string]$ContainerName = 'deepseek-godot-sandbox',
    [string]$ProjectPath,
    [string]$DshHome,
    [string]$DockerFile = 'Dockerfile',
    [string]$PatchFile = 'cordis.patch.yml',
    [switch]$Rebuild,
    [switch]$Recreate,
    [switch]$Reset,
    [switch]$NoBrowser,
    [switch]$NoGitCredentials,
    [switch]$NoPause,
    [switch]$CreateShortcut,
    [string]$ShortcutPath,
    [string]$SshDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$ScriptPath = $MyInvocation.MyCommand.Path
if (-not $ProjectPath) { $ProjectPath = Split-Path -Parent $ScriptDir }
if (-not $DshHome)     { $DshHome = Join-Path $env:USERPROFILE '.dsh-docker' }
if (-not $SshDirectory) { $SshDirectory = Join-Path $env:USERPROFILE '.ssh' }

function Write-Head([string]$Text) {
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}
function Write-Note([string]$Text) { Write-Host "    $Text" -ForegroundColor Gray }
function Write-Warn2([string]$Text) { Write-Host "    ! $Text" -ForegroundColor Yellow }

<#
Renders one captured pipeline item as text. PowerShell 5.1 turns a native
command's stderr into ErrorRecords, whose string form is often just the
exception type name; the message is the part worth showing.
#>
function ConvertTo-Text($Item) {
    if ($Item -is [System.Management.Automation.ErrorRecord]) { return $Item.Exception.Message }
    return "$Item"
}

<#
Runs a native command and returns its exit code, leaving its output on the
console.

PowerShell 5.1 turns a native command's stderr into terminating errors while
$ErrorActionPreference is 'Stop' -- and docker writes ordinary build progress to
stderr -- so the preference is relaxed for the duration of the call.
#>
function Invoke-Streaming([string]$File, [string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $File @Arguments 2>&1 | ForEach-Object { Write-Host (ConvertTo-Text $_) }
        return $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
}

<#
Runs a native command capturing output, never throwing. Used for probes whose
failure is expected and meaningful (a stopped daemon, a missing image).

Two traps this avoids, both of which cost real debugging time here:
  * the preference must be 'Continue', not 'SilentlyContinue' -- a silently
    continued native stderr line never reaches the pipeline, and docker sends
    the interesting half (container stderr, including a crash trace) there, so
    probes came back empty exactly when they mattered;
  * the pipeline is wrapped in @() as a whole, because `@(...) | ForEach-Object`
    unrolls the array and leaves a bare string for single-line output, which
    then blows up on .Count under Set-StrictMode.
#>
function Invoke-Captured([string]$File, [string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $raw = @(& $File @Arguments 2>&1)
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    $rendered = foreach ($item in $raw) { ConvertTo-Text $item }
    return [pscustomobject]@{ ExitCode = $code; Lines = @($rendered) }
}

<#
Docker Desktop 4.x installs per-user on recent versions and machine-wide on
older ones, and this machine has carried both, so every known location is tried
before falling back to PATH. docker/test-sandbox.ps1 mirrors this list.
#>
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

<#
The model key to hand the container, mirroring the harness's own precedence:
the inherited environment wins over the managed credentials document.

That document has a small fixed shape (`version` / `refs` / `records`, with
POSIX-identifier keys), so the one key is read with a regex rather than pulling
a YAML parser into a PowerShell 5.1 script for it.
#>
function Get-HostApiKey {
    $fromEnvironment = [Environment]::GetEnvironmentVariable('DEEPSEEK_API_KEY')
    if ($fromEnvironment) { return $fromEnvironment }
    $document = Join-Path $env:USERPROFILE '.dsh\.credentials.yaml'
    if (-not (Test-Path -LiteralPath $document)) { return $null }
    foreach ($line in (Get-Content -LiteralPath $document)) {
        if ($line -match '^\s*DEEPSEEK_API_KEY\s*:\s*(\S+)\s*$') { return $Matches[1] }
    }
    return $null
}

<#
Hands the container this machine's git author, and its SSH material when there is
any, so that commits and `git push` work from inside a session and not only from
here.

Copied in, rather than mounted, and ssh's own key check is what forces that: a
private key anyone else can read is refused outright ("Permissions 0777 ... are
too open"), and that check reads the *file's* mode, so no StrictModes setting
talks it out of anything. An NTFS share cannot express POSIX ownership -- every
file on it reads back as 777, which is the same property that makes the harness
refuse a credentials file from this share (see the DEEPSEEK_API_KEY comment in
Invoke-Main) -- so a bind mount of %USERPROFILE%\.ssh would hand ssh a key it will
not use, and every push would fail with a message about permissions rather than
about credentials. `docker cp` lands the files on the container's own
filesystem, where 600 sticks.

The whole directory travels, not just the key: known_hosts is what stops ssh from
asking whether github.com really is github.com, and a session has no console to
answer that prompt on.

Nothing in here throws. Credentials are a convenience the box is given, not a
promise the deployment makes -- a machine with no key still gets a sandbox that
runs the game, builds the project and serves the Web UI -- so every failure here
is a warning and a note in the closing summary.
#>
function Sync-GitCredentials([string]$Docker, [string]$Name, [string]$ProjectPath, [string]$SshDirectory) {
    $identity = $null
    $gitCommand = Get-Command 'git' -ErrorAction SilentlyContinue
    if ($gitCommand) {
        # No --global and no --local: the question is which identity this
        # machine's commits already carry, and that is the merged answer.
        $authorName  = (Invoke-Captured $gitCommand.Source @('-C', $ProjectPath, 'config', '--get', 'user.name')).Lines  | Select-Object -First 1
        $authorEmail = (Invoke-Captured $gitCommand.Source @('-C', $ProjectPath, 'config', '--get', 'user.email')).Lines | Select-Object -First 1
        if ($authorName -and $authorEmail) {
            # --global writes the container's own /root/.gitconfig, never the
            # repository's .git/config on the share: the box is disposable and
            # this machine's clone is not.
            $null = Invoke-Captured $Docker @('exec', $Name, 'git', 'config', '--global', 'user.name', $authorName)
            $null = Invoke-Captured $Docker @('exec', $Name, 'git', 'config', '--global', 'user.email', $authorEmail)
            $identity = "$authorName <$authorEmail>"
            Write-Note "the box commits as $identity"
        }
    }
    if (-not $identity) {
        Write-Warn2 'no git author found for this machine, so the box has none either:'
        Write-Warn2 "  docker exec $Name git config --global user.name `"Your Name`""
        Write-Warn2 "  docker exec $Name git config --global user.email you@example.com"
    }

    if (-not (Test-Path -LiteralPath $SshDirectory)) {
        Write-Warn2 "no $SshDirectory on this machine, so the box cannot push:"
        Write-Warn2 'push from this machine, or create a key and run this script again'
        return 'no SSH key to install'
    }

    # Replace rather than merge: `docker cp` copies a directory *into* one that
    # already exists, so a second run against the same container would leave the
    # keys at /root/.ssh/.ssh and ssh would never look at them.
    $null = Invoke-Captured $Docker @('exec', $Name, 'rm', '-rf', '/root/.ssh')
    $copy = Invoke-Captured $Docker @('cp', $SshDirectory, "${Name}:/root/.ssh")
    if ($copy.ExitCode -ne 0) {
        Write-Warn2 "could not copy $SshDirectory into the container: $(($copy.Lines | Select-Object -Last 1))"
        return 'SSH key not copied'
    }
    # -f because a directory holding only a .pub file, or only known_hosts, makes
    # these globs match nothing, and that is not a failure worth reporting.
    $null = Invoke-Captured $Docker @('exec', $Name, 'sh', '-c',
        'chmod 700 /root/.ssh && chmod -f 600 /root/.ssh/* && chmod -f 644 /root/.ssh/*.pub')
    Write-Note "SSH material copied in from $SshDirectory, private keys at mode 600"

    # Verify instead of assuming. A key that is present but not registered, or
    # not the one GitHub knows, behaves exactly like a working key until a push
    # fails -- and a push is the expensive place to find that out. `ssh -T` exits
    # 1 whether or not it authenticated (GitHub offers no shell), so the greeting
    # is the signal and the exit code is not. accept-new records github.com's
    # host key on first use rather than prompting for it: a session has no
    # console, so a prompt there is a hang, and a hang is indistinguishable from
    # a slow push.
    $probe = Invoke-Captured $Docker @('exec', $Name, 'ssh', '-o', 'BatchMode=yes',
        '-o', 'StrictHostKeyChecking=accept-new', '-o', 'ConnectTimeout=10', '-T', 'git@github.com')
    $greeting = $probe.Lines | Where-Object { $_ -match 'successfully authenticated' } | Select-Object -First 1
    if ($greeting) {
        Write-Note "github.com says: $greeting"
        if ($identity) { return "ready to push as $identity" }
        return 'ready to push (no git author set)'
    }
    Write-Warn2 "github.com did not authenticate: $(($probe.Lines | Select-Object -Last 1))"
    Write-Warn2 'the key may not be registered there, this machine may be offline,'
    Write-Warn2 'or a known_hosts entry for github.com may be stale'
    return 'SSH key installed, github.com unverified'
}

<#
Imports the project once, so the box can run the game from the moment it starts.

Godot cannot run a project it has never imported. `project.godot` names its main
scene by UID and every texture is loaded out of `.godot/imported`; both are
products of the import pass, which only the editor used to trigger. Against a
fresh `-godot` volume -- a new box, or any `-Reset` -- the headless run the root
orientation file hands a session therefore dies before it boots anything:

    ERROR: Unrecognized UID: "uid://...".
    Couldn't detect whether to run the editor ... Aborting.

exit code 1, with nothing in it to say that the project was never imported. That
made test-sandbox.ps1's boot check a warning about a healthy deployment, and it
is what a session hits on its first `godot --headless --path /workspace ...`.

The pass is idempotent and takes a couple of seconds; the editor writes the same
data when a human opens the project. Like Sync-GitCredentials this reports
rather than throws -- a box that cannot import still serves the Web UI -- and its
verdict is repeated in the closing summary.
#>
function Import-GodotProject([string]$Docker, [string]$Name) {
    # The timeout wraps bash, not the command text: `timeout 600 cd /workspace
    # && ...` would ask timeout to execute a program named `cd`.
    $import = Invoke-Captured $Docker @('exec', $Name, 'timeout', '600', 'bash', '-lc',
        'cd /workspace && godot --headless --path /workspace --import')
    if ($import.ExitCode -eq 0) {
        Write-Note 'the project is imported, so headless runs work from here'
        return 'imported (headless runs ready)'
    }
    Write-Warn2 "the project did not import: $(($import.Lines | Select-Object -Last 1))"
    Write-Warn2 'a headless run aborts with "Unrecognized UID" until it does:'
    Write-Warn2 "  docker exec $Name godot --headless --path /workspace --import"
    return 'NOT imported (headless runs will fail)'
}

<# The Docker Desktop launcher, in either install layout. #>
function Resolve-DesktopPath {
    foreach ($candidate in @(
            (Join-Path $env:LOCALAPPDATA 'Programs\DockerDesktop\Docker Desktop.exe'),
            (Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'))) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

<#
Puts the resolved CLI's own directory on PATH.

docker.exe is invoked by absolute path, but the CLI shells out to sibling
programs by name -- docker-credential-desktop for registry auth (and it is
called even for anonymous pulls, whenever ~/.docker/config.json names a
credsStore), plus buildx and the compose plugin. A shell that started before
Docker Desktop was installed has a PATH without that directory, and the build
then dies with "exec: docker-credential-desktop: executable file not found in
%PATH%". Prepending the directory makes the script independent of how old the
calling shell's PATH is.
#>
function Initialize-DockerEnvironment([string]$DockerExe) {
    $directory = Split-Path -Parent $DockerExe
    if (@($env:PATH -split ';') -notcontains $directory) {
        $env:PATH = "$directory;$env:PATH"
        Write-Note "added $directory to PATH for this run"
    }
}

function Test-Daemon([string]$Docker) {
    return ((Invoke-Captured $Docker @('info', '--format', '{{.ServerVersion}}')).ExitCode -eq 0)
}

function Wait-Daemon([string]$Docker) {
    if (Test-Daemon $Docker) { return }
    Write-Note 'Docker daemon is not answering; starting Docker Desktop...'
    $desktop = Resolve-DesktopPath
    if ($desktop) { Start-Process -FilePath $desktop | Out-Null }
    for ($i = 1; $i -le 60; $i++) {
        Start-Sleep -Seconds 5
        if (Test-Daemon $Docker) { Write-Note 'Docker daemon is up.'; return }
        if ($i % 6 -eq 0) { Write-Note "still waiting for the Docker daemon ($($i * 5)s)" }
    }
    throw 'Docker daemon did not start within 5 minutes. Start Docker Desktop and re-run.'
}

function Test-PortFree([int]$Candidate) {
    $listener = $null
    try {
        $listener = New-Object -TypeName System.Net.Sockets.TcpListener -ArgumentList @([System.Net.IPAddress]::Loopback, $Candidate)
        $listener.Start()
        return $true
    } catch {
        return $false
    } finally {
        if ($listener -ne $null) { $listener.Stop() }
    }
}

function Get-FreePort([int]$Start) {
    for ($p = $Start; $p -lt ($Start + 40); $p++) {
        if (Test-PortFree $p) { return $p }
    }
    throw "No free host port between $Start and $($Start + 39)."
}

function Get-Names([string]$Docker, [string[]]$Arguments) {
    return @((Invoke-Captured $Docker $Arguments).Lines)
}

function Show-ContainerLog([string]$Docker, [string]$Name) {
    Write-Warn2 'last 40 lines of the container log:'
    foreach ($line in (Invoke-Captured $Docker @('logs', '--tail', '40', $Name)).Lines) {
        Write-Host "      $line" -ForegroundColor DarkGray
    }
}

<#
A container that dies before writing a single log line leaves nothing to read,
so the state is reported alongside the log: the exit code separates "the
command was not found" from "the process ran and crashed", and State.Error
carries the runtime's own message when the process never started at all.
#>
function Show-ContainerState([string]$Docker, [string]$Name) {
    $probe = Invoke-Captured $Docker @('inspect', '--format',
        'status={{.State.Status}} exit={{.State.ExitCode}} oom={{.State.OOMKilled}} started={{.State.StartedAt}} error={{.State.Error}}', $Name)
    if ($probe.ExitCode -ne 0) { return }
    foreach ($line in $probe.Lines) { Write-Warn2 "state: $line" }
}

function New-Shortcut([string]$Destination) {
    $directory = Split-Path -Parent $Destination
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    $shell = New-Object -ComObject WScript.Shell
    $link  = $shell.CreateShortcut($Destination)
    $link.TargetPath       = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $link.Arguments        = '-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $ScriptPath
    $link.WorkingDirectory = $ScriptDir
    $link.Description      = 'Start the isolated DeepSeek Harness + Godot sandbox and open its Web UI'
    $link.WindowStyle      = 1
    $dockerExe = Resolve-DockerPath
    if ($dockerExe) { $link.IconLocation = "$dockerExe,0" }
    try {
        $link.Save()
    } catch {
        throw ("could not write $Destination ($($_.Exception.Message)). " +
               'Use -ShortcutPath to write the shortcut somewhere writable, then copy it out.')
    }
    Write-Host "Shortcut written to $Destination"
}

function Invoke-Main {
    if ($CreateShortcut) {
        $destination = $ShortcutPath
        if (-not $destination) {
            $destination = Join-Path ([Environment]::GetFolderPath('Desktop')) 'DeepSeek Godot Sandbox.lnk'
        }
        New-Shortcut $destination
        return
    }

    Write-Head 'Docker'
    $docker = Resolve-DockerPath
    if (-not $docker) { throw 'docker.exe not found. Install Docker Desktop first.' }
    Write-Note "cli: $docker"
    Initialize-DockerEnvironment $docker
    Wait-Daemon $docker

    if (-not (Test-Path -LiteralPath (Join-Path $ProjectPath 'project.godot'))) {
        Write-Warn2 "no project.godot in $ProjectPath - continuing, but that is not a Godot project."
    }
    Write-Note "project: $ProjectPath -> /workspace"

    Write-Head 'Image'
    $imageId = $null
    $probe = Invoke-Captured $docker @('inspect', '--format', '{{.Id}}', $ImageName)
    if ($probe.ExitCode -eq 0) { $imageId = ($probe.Lines | Select-Object -First 1) }
    if ($Rebuild -or -not $imageId) {
        Write-Note "building $ImageName from $DockerFile (a few minutes the first time)"
        $code = Invoke-Streaming $docker @('build', '--tag', $ImageName, '--file', (Join-Path $ScriptDir $DockerFile), $ScriptDir)
        if ($code -ne 0) { throw "docker build failed with exit code $code" }
        $probe = Invoke-Captured $docker @('inspect', '--format', '{{.Id}}', $ImageName)
        if ($probe.ExitCode -ne 0) { throw 'the image was built but cannot be inspected' }
        $imageId = ($probe.Lines | Select-Object -First 1)
    } else {
        Write-Note 'image present; pass -Rebuild to refresh it'
    }

    Write-Head 'Harness home'
    if (-not (Test-Path -LiteralPath $DshHome)) { New-Item -ItemType Directory -Path $DshHome -Force | Out-Null }
    # The patch layer is copied in under DSH's fixed name regardless of which
    # file it came from: that name is what the harness reads, and a missing
    # source file is a mistake worth failing on rather than booting without it.
    $patchSource = Join-Path $ScriptDir $PatchFile
    if (-not (Test-Path -LiteralPath $patchSource)) { throw "patch layer not found: $patchSource" }
    Copy-Item -LiteralPath $patchSource -Destination (Join-Path $DshHome 'cordis.patch.yml') -Force
    Write-Note "$PatchFile -> $(Join-Path $DshHome 'cordis.patch.yml')"

    # Credentials travel as an environment variable, never as a file in here.
    #
    # This share cannot express POSIX ownership: NTFS reports every file as mode
    # 777, and the harness refuses to read a secrets file any wider than its
    # owner -- it fails the boot with
    #   credentials-local: /dsh-home/.credentials.yaml is readable beyond its
    #   owner (mode 777); run "chmod 600 ..." before starting again
    # and chmod inside the container does not stick on this mount. The inherited
    # environment is the documented higher-precedence source ("inherited process
    # environment (read-only, wins)"), so the key is passed that way instead.
    # A stale file from an earlier run would still trip the check, so it is
    # removed.
    $extraRunArgs = @()
    $staleCredentials = Join-Path $DshHome '.credentials.yaml'
    if (Test-Path -LiteralPath $staleCredentials) {
        Remove-Item -LiteralPath $staleCredentials -Force
        Write-Note 'removed a stale .credentials.yaml (unusable on this share)'
    }
    $apiKey = Get-HostApiKey
    if ($apiKey) {
        $extraRunArgs += @('--env', "DEEPSEEK_API_KEY=$apiKey")
        Write-Note 'credentials passed as DEEPSEEK_API_KEY'
    } else {
        Write-Warn2 'no credentials found; set the model key in the Web UI (Settings -> Models), or export DEEPSEEK_API_KEY.'
    }

    Write-Head 'Container'
    $volumes = @("$ContainerName-obj", "$ContainerName-bin", "$ContainerName-godot")
    $exists = (Get-Names $docker @('ps', '-a', '--format', '{{.Names}}')) -contains $ContainerName

    if ($Reset) {
        if ($exists) { $null = Invoke-Captured $docker @('rm', '--force', $ContainerName) }
        foreach ($volume in $volumes) { $null = Invoke-Captured $docker @('volume', 'rm', $volume) }
        Write-Note 'container and build-cache volumes removed'
        $exists = $false
    } elseif ($exists) {
        $containerImage = ((Invoke-Captured $docker @('inspect', '--format', '{{.Image}}', $ContainerName)).Lines | Select-Object -First 1)
        $stale = $Recreate -or $containerImage -ne $imageId
        if (-not $stale) {
            # A container's environment is fixed at creation, so one built before
            # the key was available cannot pick it up by restarting.
            $containerEnv = @((Invoke-Captured $docker @('inspect', '--format', '{{range .Config.Env}}{{println .}}{{end}}', $ContainerName)).Lines)
            $containerHasKey = @($containerEnv | Where-Object { $_ -like 'DEEPSEEK_API_KEY=*' }).Count -gt 0
            $stale = $containerHasKey -ne [bool]$apiKey
        }
        if ($stale) {
            Write-Note 'container is stale or recreation was requested; replacing it'
            $null = Invoke-Captured $docker @('rm', '--force', $ContainerName)
            $exists = $false
        }
    }

    # Decide the published port only now, after any stale container is gone.
    #
    # Choosing it earlier was wrong twice over. A container that is about to be
    # replaced still holds the port it was created with, so the "free" port search
    # skipped past it and the new container came up on a *different* port than the
    # one this script reported -- which is exactly how a healthy run reported
    # "host port 3081 is busy; using 3082 instead" and then failed a check aimed
    # at 3081. And a container that is being kept cannot move at all: its port is
    # fixed at creation, so it is read back from the runtime rather than guessed.
    $hostPort = $null
    if ($exists) {
        $published = (Invoke-Captured $docker @('port', $ContainerName, '3080')).Lines |
            Where-Object { "$_".Trim() } | Select-Object -First 1
        if ($published -match ':(?<port>\d+)\s*$') {
            $hostPort = [int]$Matches['port']
            Write-Note "reusing the existing container's published port $hostPort"
        }
    }
    if (-not $hostPort) {
        $hostPort = Get-FreePort $Port
        if ($hostPort -ne $Port) { Write-Note "host port $Port is busy; using $hostPort instead" }
    }

    if ($exists) {
        if ((Get-Names $docker @('ps', '--format', '{{.Names}}')) -contains $ContainerName) {
            Write-Note 'container already running'
        } else {
            Write-Note 'starting the existing container'
            $code = Invoke-Streaming $docker @('start', $ContainerName)
            if ($code -ne 0) { throw "docker start failed with exit code $code" }
        }
    } else {
        Write-Note 'creating the container'
        $runArgs = @(
            'run', '--detach', '--init',
            '--name', $ContainerName,
            '--env', 'DSH_HOME=/dsh-home',
            '--publish', "127.0.0.1:${hostPort}:3080",
            '--volume', "${ProjectPath}:/workspace",
            '--volume', "${DshHome}:/dsh-home",
            '--volume', "$($volumes[0]):/workspace/obj",
            '--volume', "$($volumes[1]):/workspace/bin",
            '--volume', "$($volumes[2]):/workspace/.godot"
        ) + $extraRunArgs + @($ImageName)
        # Bare $runArgs, never @runArgs: in argument position '@name' is
        # PowerShell splatting, which would spread the array across parameters
        # and hand docker run no arguments at all.
        $code = Invoke-Streaming $docker $runArgs
        if ($code -ne 0) { throw "docker run failed with exit code $code" }
    }

    # The container is up, so the credentials can go in. This is not part of what
    # the deployment promises -- the box is worth having without it -- which is
    # why Sync-GitCredentials reports rather than throws, and why its verdict is
    # repeated in the closing summary instead of only scrolling past here.
    Write-Head 'Git credentials'
    if ($NoGitCredentials) {
        $gitStatus = 'skipped (-NoGitCredentials)'
        Write-Note 'the box holds no key: it can commit, and you push from here'
    } else {
        $gitStatus = Sync-GitCredentials -Docker $docker -Name $ContainerName -ProjectPath $ProjectPath -SshDirectory $SshDirectory
    }

    Write-Head 'Web UI'
    $url = "http://localhost:$hostPort/"
    $ready = $false
    for ($i = 1; $i -le 60; $i++) {
        Start-Sleep -Seconds 1
        if (-not ((Get-Names $docker @('ps', '--format', '{{.Names}}')) -contains $ContainerName)) {
            Show-ContainerState $docker $ContainerName
            Show-ContainerLog $docker $ContainerName
            throw 'the container exited during startup (state and log above)'
        }
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 4
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        } catch {
            # the server is not serving yet
        }
    }
    if (-not $ready) {
        Show-ContainerState $docker $ContainerName
        Show-ContainerLog $docker $ContainerName
        throw "the Web UI did not answer on $url within 60s (state and log above)"
    }
    Write-Note "ready: $url"

    # The box is up, but it cannot run the game until the project has been
    # imported once -- see Import-GodotProject. Reported, not thrown: the Web UI
    # is serving either way, and the verdict is repeated in the summary.
    Write-Head 'Project import'
    $godotStatus = Import-GodotProject -Docker $docker -Name $ContainerName

    if (-not $NoBrowser) {
        Start-Process $url | Out-Null
        Write-Note 'opening the default browser'
    }

    Write-Host ''
    Write-Host "  DeepSeek Harness is running: $url" -ForegroundColor Green
    Write-Host "  container : $ContainerName"
    Write-Host "  image     : $ImageName"
    Write-Host "  project   : $ProjectPath  (mounted at /workspace)"
    Write-Host "  harness   : $DshHome  (mounted at /dsh-home)"
    Write-Host "  git       : $gitStatus"
    Write-Host "  godot     : $godotStatus"
    Write-Host ''
    Write-Host "  logs  : docker logs -f $ContainerName"
    Write-Host "  shell : docker exec -it $ContainerName bash"
    Write-Host "  stop  : docker stop $ContainerName"
    Write-Host '  clean : .\run-sandbox.ps1 -Reset'
    Write-Host ''
}

try {
    Invoke-Main
} catch {
    Write-Host ''
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    if (-not $NoPause) { Write-Host 'Press Enter to close...' -ForegroundColor Gray; $null = Read-Host }
    exit 1
}

if (-not $NoPause) { Write-Host 'Press Enter to close this window...' -ForegroundColor Gray; $null = Read-Host }
