# Isolated DSH + Godot sandbox

Run DeepSeek Harness sessions inside a container instead of on the host: the
agent gets this project, a matching Godot .NET editor and the .NET SDK, and
nothing else on this machine.

## Quick start

```powershell
.\docker\run-sandbox.ps1          # build if needed, start, open the Web UI
.\docker\test-sandbox.ps1         # assert the whole flow still works
.\docker\run-sandbox.ps1 -Reset   # drop the container and build cache
```

A shortcut named **DeepSeek Godot Sandbox** can be written into this folder and
copied to the Desktop (or anywhere) to launch the sandbox by double-click. It
targets `run-sandbox.ps1` by absolute path, so it keeps working after being
moved — which also means a committed `.lnk` would only be valid on the machine
that made it, so it is generated rather than tracked:

```powershell
.\docker\run-sandbox.ps1 -CreateShortcut -NoPause                              # to the Desktop
.\docker\run-sandbox.ps1 -CreateShortcut -NoPause -ShortcutPath .\docker\out.lnk
```

The Web UI comes up on <http://localhost:3081>. It is 3081 and not 3080 on
purpose: 3080 is where a harness running on the host itself listens, so the
container would collide with it.

## What is in the box

| | version | why that one |
| --- | --- | --- |
| Node | 24 (from `node:24-bookworm`) | the same major this host already runs dsh on |
| Godot | 4.5.1 **.NET/mono** | `MbcPrototype.csproj` pins `Godot.NET.Sdk/4.5.1`; a non-.NET build cannot open a C# project, and 4.3 refuses a 4.5 project outright |
| .NET SDK | 8.0 | the project targets `net8.0` |
| dsh | `0.1.1-rc.2` (`ARG DSH_VERSION`) | the version this host runs, so a session in the box behaves like one outside it; its dependency tree is pinned to the day that release was current (`ARG DSH_BEFORE`), because unpinned caret ranges resolve to cordis releases this dsh cannot boot with |

The project is mounted at `/workspace`, which is also the container's working
directory — but *not* the session's workspace root: the Workspace this box's Web
UI was set up with is the container root `/`, which is what gives a session the
whole machine instead of only the project. That choice lives in `DSH_HOME`
(`storages/workspace.json`), so it survives rebuilds but not a deleted
`~/.dsh-docker`.

The build does not merely assemble the image: the last step boots `dsh web`
inside it, with the same patch layer the run script installs, and requires it to
still be answering 20 seconds later. A patch that fails to compose — or an npm
tree that resolves into an unloadable plugin — therefore fails `docker build`
instead of failing silently at container start. That makes `-Rebuild` the check
to run after touching `Dockerfile` or `cordis.patch.yml`, before any of the
runtime assertions in `test-sandbox.ps1` get a chance to run.

## The root orientation file

A session here starts at `/`, and `/` is also the filesystem root, so a file
there is the one thing every session sees without searching for it. The image
therefore installs `CONTAINER-README.md` at `/AGENTS.md`, with `/README.md` as a
symlink to it, so a new agent lands on the layout of the box instead of spending
its first turns rediscovering it — the project guide is two levels down
(`/workspace/AGENTS.md`) and the sandbox's own guide three
(`/workspace/docker/README.md`).

The **name** is what does the work, not the location: DSH's `agent-instructions`
preset discovers `AGENTS.md` (then `CLAUDE.md`) in the workspace root and its
ancestors, so for a session whose cwd is `/` the file arrives as baseline
workspace instructions — nothing has to be opened. That covers the Workspace
described above; a session created against `/workspace` instead sees the project
guide there, exactly as a host session does, and `/AGENTS.md` is then only what
`ls /` turns up. Both files are checked by `test-sandbox.ps1`.

Edit `CONTAINER-README.md` here and rebuild: the file inside a container is
replaced wholesale on every build, so a change made in a running container dies
with that container.

## Committing and pushing from inside the box

Sessions are where the work happens, so sessions are what needs the credentials:
`run-sandbox.ps1` hands the container this machine's git author and its SSH
material on every run, and then checks that GitHub accepts the result.

* The author is read from this machine's **effective** config —
  `git -C <repo> config --get user.name`, so a repository-local identity wins
  exactly as it would here — and written into the container's own
  `/root/.gitconfig`. Never into `.git/config` on the share: the box is
  disposable and this machine's clone is not.
* `%USERPROFILE%\.ssh` is copied to `/root/.ssh` with `docker cp`, then chmodded
  to 700/600.
* `ssh -T git@github.com` is run inside the container, because a key that is
  present but not registered there behaves exactly like a working one until a
  push fails — and a push is the expensive place to discover that.

**Copied in, not mounted**, and ssh is what forces the distinction: it refuses a
private key anyone else can read,

```
Permissions 0777 for '/root/.ssh/id_ed25519' are too open.
```

and that check reads the *file's* own mode, so no `StrictModes` setting talks it
out of anything. An NTFS share reports every file as `777` — the same property
that makes the harness refuse a credentials file from `/dsh-home`, and why the
model key travels as an environment variable instead — so a bind mount would hand
ssh a key it will not use, and every push would fail with a message about
permissions rather than about credentials. `docker cp` puts the files on the
container's own filesystem, where mode 600 sticks.

Two switches shape it:

* `-NoGitCredentials` skips the whole step: the box commits but cannot push, and
  `test-sandbox.ps1`'s key check drops to a warning.
* `-SshDirectory` chooses what is copied (default `%USERPROFILE%\.ssh`). Point it
  at a directory holding a **deploy key** scoped to this repository, with its own
  `config` naming it for github.com, when the box should be able to push and do
  nothing else with your GitHub account.

## Mounts

| host | container | notes |
| --- | --- | --- |
| this repository | `/workspace` | read-write; the agent edits the real project |
| `%USERPROFILE%\.dsh-docker` | `/dsh-home` | `DSH_HOME`: settings, session history, patch layer. Delete for a factory-fresh harness. |
| named volume `…-obj` | `/workspace/obj` | |
| named volume `…-bin` | `/workspace/bin` | |
| named volume `…-godot` | `/workspace/.godot` | |

**Credentials are never a file in there.** An NTFS share cannot express POSIX
ownership — every file reads back as mode `777` — and the harness refuses to
load a secrets file any wider than its owner, failing the boot with
`credentials-local: … is readable beyond its owner (mode 777)`. `chmod` inside
the container does not stick on that mount. The key is passed as
`DEEPSEEK_API_KEY` instead, which the harness documents as its highest
precedence source ("inherited process environment (read-only, wins)"). The run
script reads it from the host's `~/.dsh/.credentials.yaml`, or from your own
environment if set, and deletes any stale `.credentials.yaml` it finds — a
leftover from an earlier run would still trip the check.

The three volumes exist so the container's build output cannot collide with the
output of a Windows build — `obj/project.assets.json` and `.godot/mono` are
toolchain-specific, and sharing them between a Linux container and a Windows
editor produces confusing, intermittent failures. `-Reset` recreates them.

## Why the Web server binds `0.0.0.0` inside the container

`dsh web` binds `127.0.0.1` by default, and Docker publishes a port by
forwarding to the **container's interface address, never its loopback**, so a
default-bound server in a container is unreachable from the host. The obvious
fix is refused on purpose:

```
$ dsh web --host 0.0.0.0
error: --host 0.0.0.0 is intentionally not supported yet for safety:
it would expose remote code execution to the network; use 127.0.0.1 instead
```

So the bind is overridden through configuration instead:
`docker/cordis.patch.yml` is copied into the container's `DSH_HOME` on every
run, where it patches the `webserver` row:

```yaml
- id: webserver
  config:
    host: '0.0.0.0'
    port: 3080
```

Two things to keep in mind if you edit that file:

* a patch **replaces a row's whole `config`** — there is no deep merge — so
  every field the row keeps has to be restated;
* a patch whose `id` matches no row is skipped with a warning, so a typo is
  silent. `docker exec <container> dsh web --dump-config` prints the composed
  tree to confirm the override landed.

## Security boundary

The Harness has **no authentication layer**: whoever can reach the Web UI can
run code on the box. Reachability is therefore the entire boundary, and it is
kept narrow on purpose:

* the port is published as `127.0.0.1:<port>:3080`, so only this machine can
  connect — do not widen it without putting a real auth layer in front;
* the `/api` browser-trust fence additionally refuses any request whose `Host`
  is neither loopback nor a declared trusted authority, which is why the UI is
  opened as `http://localhost:<port>`. `test-sandbox.ps1` asserts both halves of
  that fence.

Inside the container the agent runs as `root` and can `apt-get install`
anything; that is the point of the box, and it is why the box is disposable.

It also holds credentials on purpose, and that is worth stating plainly in a
section about boundaries: the model key arrives as an environment variable, and
`run-sandbox.ps1` copies your git author and the SSH key described in
*Committing and pushing from inside the box* into `/root/.ssh`. Any session — that
is, any agent, and anyone who can reach the Web UI — can read them and push with
them. `-NoGitCredentials` keeps the box free of your key, and `-SshDirectory` with
a deploy key limits what a leak would be worth.

## Troubleshooting

**`docker-credential-desktop: executable file not found in %PATH%`** — the
Docker CLI invokes its credential helper as a sibling *by name*, so the CLI's
directory has to be on `PATH`. Both scripts prepend it; if you see this from a
hand-written command, use a shell started after Docker Desktop was installed.

**`permission denied … npipe:////./pipe/dockerDesktopLinuxEngine`** — the
engine is reachable but this process may not open the pipe (a DSH session can
deny named pipes to its own child processes; Docker Desktop starting up looks
the same). Run the flow from a normal PowerShell window.

**`plugin(s) failed to load: @deepseek-ai/dsh-sandbox-local` with a nearly empty
log** — npm 11.19+ skips dependency install scripts unless they are named on
the command line, and one of them builds a native binding the plugin tree
imports at module scope: `dsh-sandbox-local` imports `dsh-sandbox-windows-acl`,
which does `import koffi from 'koffi'`. The `Dockerfile` therefore passes
`--allow-scripts=…` and installs `build-essential`/`python3` for the compile.
If you add a dependency with its own install script, add it to that list.

**`user patch-layer watching requires the Cordis HMR service`, or
`TypeError: hmr.registerConfig is not a function`** — both are the *same* failure
wearing two masks, and neither is about the patch layer: the dependency tree has
drifted forward of the dsh release it belongs to. The release is pinned exactly;
its dependencies are caret ranges, and `cordis-plugin-hmr` **1.0.19** dropped the
`registerConfig` method that `dsh-app-boot@0.1.1-rc.2` calls by name. The service
itself is real — its constructor is `Hmr`, and only the method is missing — so
nothing declares the incompatibility and npm installs a tree that cannot boot.
The image therefore installs with `--before` (`ARG DSH_BEFORE`), which restores
the tree as it stood when the release was current, and the build asserts that
`registerConfig` is actually there. With the `hmr` row disabled you get the first
message, with it enabled the second; the versions the pin produced are printed by
every build. If this returns, compare those lines against `cordis` 4.0.1,
`cordis-plugin-hmr` 1.0.16, `cordis-plugin-timer` 1.1.3.

**The Web UI does not answer** — `docker logs <container>` is printed
automatically on failure. The usual causes are a bad patch layer (see above) or
missing credentials.

**The game does not boot headlessly** — reported as a warning rather than a
failure by `test-sandbox.ps1`, because a prototype can fail to boot for reasons
unrelated to the sandbox. Pass `-Strict` to make it fatal.

## A/B: the community-image variant

`Dockerfile.smanx` is a second, deliberately throwaway comparison: the same
Godot toolchain layered on `smanx/deepseek-harness:devtools-0.1.5-rc.3` instead
of on `node:24-bookworm` plus our own `npm install`.

It exists because of the failure mode this project hit: our dsh *release* is
pinned exactly while its dependencies are caret ranges, so an install can resolve
cordis packages the release cannot boot with (see the `DSH_BEFORE` comment in
`Dockerfile`, and the HMR entry in *Troubleshooting*). That community image
sidesteps the whole class by construction — it installs `@deepseek-ai/dsh@next`
at build time and tags the result with the version it got, so dsh and its cordis
dependencies are always resolved together and published as one immutable tag.

One command builds it, starts it and runs the same end-to-end checks:

```powershell
.\docker\test-sandbox.ps1 -DockerFile Dockerfile.smanx `
    -PatchFile cordis.patch.smanx.yml -Rebuild `
    -ImageName godot-dsh-sandbox-smanx -ContainerName deepseek-godot-sandbox-smanx
```

Both images can coexist; the variant gets its own image name, container name and
build-cache volumes. What to expect from the result:

| check | variant expectation |
| --- | --- |
| toolchain, `dotnet build`, headless boot, root orientation file | should behave exactly as ours — that layer is identical |
| `Web UI answers`, `dsh CLI responds` | pass, but through *their* entrypoint (`dsh web --port 3079` + a proxy on 3080) |
| `fence refuses a non-loopback Host` | **expected to fail** — see below. Reported as the finding, not as a broken test |
| `fence accepts a loopback Host` | passes either way |

The fence is the real difference between the two variants. Their proxy is created
with `changeOrigin: true` and rewrites `Origin` too, so DSH always sees
`127.0.0.1:3079` — a loopback authority — no matter what the client sent, and the
browser-trust fence has nothing left to refuse. **For this variant the boundary is
therefore the published host port plus whatever the proxy enforces**, and the
proxy enforces nothing unless you set both `PROXY_USERNAME` and `PROXY_PASSWORD`
(its `checkAuth` returns true when either is empty). Our own variant keeps the
fence where the test can see it. The proxy also rewrites response bodies — a
`crypto.randomUUID` polyfill in HTML, `isLoopbackHostname(...)` → `true` in JS —
which is why it is popular for LAN access and beside the point on loopback.

Two things worth checking by hand before trusting it:

- **the terminal binding**: their build passes no `--allow-scripts`, unlike ours,
  so their `node-pty` may be unbuilt —
  `docker run --rm --entrypoint node godot-dsh-sandbox-smanx -e "require('node-pty')"`;
- **the dsh version**: `0.1.5-rc.3` is theirs, not the `0.1.1-rc.2` this host runs,
  so box sessions are no longer version-identical to host sessions. Set
  `SMANX_TAG=devtools-0.1.1-rc.2` to match the version and compare their older
  tree instead.

## Files

| file | role |
| --- | --- |
| `Dockerfile` | the image: Node, Godot 4.5.1 .NET, .NET 8 SDK, dsh — and the build-time boot smoke test |
| `Dockerfile.smanx` | the A/B variant: the same toolchain on the community DSH image |
| `CONTAINER-README.md` | the orientation file both images install at `/AGENTS.md` (with `/README.md` symlinked to it) — the one a session finds without looking; this is the copy to edit |
| `cordis.patch.yml` | the two `DSH_HOME` rows this deployment needs: the `webserver` bind override and the re-enabled `hmr` row, both commented with why |
| `cordis.patch.smanx.yml` | the same minus the `webserver` row, which that image's entrypoint proxy replaces |
| `run-sandbox.ps1` | build, start, wait for readiness, hand the container its git credentials, open the browser, desktop shortcut |
| `test-sandbox.ps1` | end-to-end assertions about the running sandbox; `-DockerFile`/`-PatchFile`/`-ImageName`/`-ContainerName` score either variant |

Scripts target **Windows PowerShell 5.1**, which is what this machine ships
(`pwsh` is not installed).
