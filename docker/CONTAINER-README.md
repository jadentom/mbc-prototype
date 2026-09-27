# You are inside the DSH + Godot sandbox container

Read this first. It is the orientation file at `/`, and it is loaded
automatically into every session that starts here: DSH's `agent-instructions`
preset reads `AGENTS.md` from the workspace root, and in this box the workspace
root *is* `/` (see *Layout*). It exists so a fresh session does not spend its
first turns working out where anything is.

`Dockerfile` installs this file at `/AGENTS.md` and symlinks `/README.md` to it.
**Edit the repository copy** — `docker/CONTAINER-README.md` — and rebuild. A
change made to this file inside a running container dies with that container.

## Layout

| path | what it is |
| --- | --- |
| `/workspace` | the MBC Godot project — a host bind mount, read-write |
| `/workspace/AGENTS.md` | **the project guide**: architecture, turn system, conventions. Read it before touching game code. |
| `/workspace/docker/README.md` | how this sandbox is built, run and tested |
| `/workspace/docs/` | camera/minimap design notes |
| `/dsh-home` | `DSH_HOME` — settings, session logs, patch layer (host bind mount) |
| `/opt/godot` | Godot 4.5.1 .NET editor; `godot` is on PATH via `/usr/local/bin/godot` |

Sessions here start at `/`, not at `/workspace`: the Web UI's Workspace is the
container root, which is the point of the box — the agent gets the whole machine,
not only the project. The price is that every path has to be absolute, and the
working directory is not the project.

Toolchain: `godot` 4.5.1 (mono), `dotnet` 8.0, `node` 24, `dsh`. `docker/README.md`
carries the versions and the reason each one is pinned.

## Working here

- Build: `dotnet build /workspace/MbcPrototype.csproj`
- Headless run: `godot --headless --path /workspace --quit-after 60`
- Neither needs an escalation in this box: the workspace is `/`, so the toolchain
  is inside it. A session on the Windows host is the opposite — its copy of the
  project guide warns that `godot` and `dotnet` live outside the workspace.
- There is no Docker CLI in here. Builds, restarts and tests happen on the host:
  `docker\run-sandbox.ps1` and `docker\test-sandbox.ps1`.
- `git push` works from here. `run-sandbox.ps1` copies the host's git author into
  `/root/.gitconfig` and its SSH key into `/root/.ssh` on every start, so commit
  and push as usual. When the box was started with `-NoGitCredentials` there is no
  key and no push — commit anyway and say so, rather than asking for credentials
  that the human already decided not to hand over.
- Don't leave probe scenes or `.gd` scripts in the repo — the project guide's
  *Build & run* explains why (they also leave `.uid` files behind).

## What survives, and what does not

- `/workspace` and `/dsh-home` are **host bind mounts**. Project edits, session
  logs and settings survive `docker stop`, `docker rm`, and a rebuilt image.
- Everything else is the container's own overlay filesystem: `apt-get install`,
  files under `/root` or `/tmp`, extra environment. Those survive
  `docker stop`/`docker start`, but `run-sandbox.ps1 -Reset` — or any rebuild
  that replaces the container — takes them with it. Anything the box should have
  again belongs in `Dockerfile`.
- Session transcripts live in
  `/dsh-home/sessions/--root--/<session-id>/session.jsonl.zstd`. Closing the
  browser, or the container, does not delete them; the sidebar lists a
  workspace's sessions and reopens one. A *new* session, though, starts with no
  memory of an older one — only what is on disk carries across, which is what
  this file and `/workspace/AGENTS.md` are for.
