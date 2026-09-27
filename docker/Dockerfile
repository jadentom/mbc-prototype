# syntax=docker/dockerfile:1
#
# Isolated DeepSeek Harness + Godot sandbox.
#
# An agent session runs *inside* this box against a mounted Godot C# project,
# so the container needs three things the host provides today: Node (for dsh),
# the .NET SDK (the project builds C#), and a Godot .NET editor binary.
#
# Choices that differ from a generic "godot in docker" recipe, and why:
#
#   * Node 24, matching the host that already runs dsh, so the container is
#     not the place a Node-major difference shows up.
#   * Godot 4.5.1 **mono/.NET**, matching `Godot.NET.Sdk/4.5.1` in
#     MbcPrototype.csproj. The upstream non-.NET build (and any 4.3 build)
#     cannot open this project: a C# project needs the .NET editor, and a
#     4.3 editor refuses a 4.5 project outright.
#   * `GodotSharp/` must stay a sibling of the executable, so the whole
#     download is unpacked into /opt/godot and PATH points at that directory
#     instead of copying the binary out on its own.
#   * .NET 8 SDK: the project targets net8.0.
#   * xvfb + libGL: `godot --headless` covers most automated checks, but a
#     real (virtual) display is available when something needs rendering.

ARG NODE_IMAGE=node:24-bookworm
FROM ${NODE_IMAGE}

ARG GODOT_VERSION=4.5.1
# Pinned to the version this project's host already runs, rather than `latest`.
#
# `latest` (0.1.5-rc.3 at the time of writing) installs a conflicted tree: npm
# emits hundreds of "ERESOLVE overriding peer dependency" warnings and ends up
# with duplicate nested copies of packages (dsh-sandbox-local appears under both
# dsh-base/node_modules and dsh-sdk-minimal/node_modules). A nested copy
# resolves its peer dependencies from its own location, and when npm has
# hoisted one of those elsewhere the plugin cannot be imported -- the whole boot
# then dies with "plugin(s) failed to load: @deepseek-ai/dsh-sandbox-local;
# ... could not be resolved", and DSH prints no underlying reason.
#
# Pinning also keeps the sandbox on the same version as the host, so a session
# in here behaves like a session out there.
ARG DSH_VERSION=0.1.1-rc.2

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_RUNNING_IN_CONTAINER=true

# Base utilities plus the shared libraries the Godot Linux editor links
# against. They are needed even for --headless runs: the binary links them at
# load time. build-essential/python3 are there for the native npm install
# scripts below (koffi compiles through cnoke, node-pty through node-gyp) --
# neither ships a usable prebuild for this platform.
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        git \
        wget \
        unzip \
        xz-utils \
        libfontconfig1 \
        libgl1 \
        libxcursor1 \
        libxinerama1 \
        libxrandr2 \
        libxi6 \
        libxkbcommon0 \
        libasound2 \
        libpulse0 \
        libudev1 \
        xvfb \
        build-essential \
        python3 \
    && rm -rf /var/lib/apt/lists/*

# .NET 8 SDK from Microsoft's Debian 12 feed.
RUN wget -q https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O /tmp/ms-prod.deb \
    && dpkg -i /tmp/ms-prod.deb \
    && rm /tmp/ms-prod.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends dotnet-sdk-8.0 \
    && rm -rf /var/lib/apt/lists/*

# Godot 4.5.1 .NET editor. The release zip unpacks to a version-named
# directory holding both the executable and GodotSharp/, which must travel
# together.
RUN set -eux; \
    wget -q -O /tmp/godot.zip \
        "https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}-stable/Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64.zip"; \
    unzip -q /tmp/godot.zip -d /tmp/godot; \
    src="$(find /tmp/godot -mindepth 1 -maxdepth 1 -type d -name 'Godot_v*' | head -n1)"; \
    [ -n "$src" ] || src=/tmp/godot; \
    mkdir -p /opt/godot; \
    mv "$src/GodotSharp" /opt/godot/GodotSharp; \
    mv "$src"/Godot_v*_mono_linux.x86_64 /opt/godot/godot; \
    chmod +x /opt/godot/godot; \
    rm -rf /tmp/godot /tmp/godot.zip

ENV PATH="/opt/godot:${PATH}"

# The harness CLI. Its profile bundles (base, web app, frontend dist) ship as
# ordinary dependencies, so a global install is the whole product.
#
# --allow-scripts is load-bearing, not an optimization. npm 11.19+ skips
# dependency install scripts unless they are named, and two of them build
# native bindings that sit in the plugin tree's import graph:
# @deepseek-ai/dsh-sandbox-local imports @deepseek-ai/dsh-sandbox-windows-acl,
# which does `import koffi from 'koffi'` at module scope, and node-pty is the
# terminal binding. npm prints the list it skipped at the end of the install;
# build-essential/python3 above exist so those compiles can actually run.
#
# (This was originally blamed for the boot failure below. It was not the cause
# -- with the bindings built, the boot failed in exactly the same way -- but it
# is still required, and the failure it would cause is just as opaque.)
# Pin the dependency *tree*, not just the release.
#
# The dsh release is pinned exactly, but its dependencies are ordinary caret
# ranges, so a global install resolves whatever is current on the day it runs --
# and a later cordis release moved the API that this dsh release calls:
#
#   dsh-app-boot/lib/index.js:767   hmr.registerConfig(filename, refresh)
#   cordis-plugin-hmr 1.0.19        no such method (now getLinked/analyzeChanges)
#
# Nothing declares that incompatibility -- no peer range, no engine check -- so
# npm happily installs a tree in which `dsh web` cannot boot at all:
#
#   TypeError: hmr.registerConfig is not a function
#
# This is not hypothetical: it is exactly what broke this image. The image had
# resolved cordis 4.0.4 / cordis-plugin-hmr 1.0.19 / cordis-plugin-timer 1.1.6,
# while the Windows host this project is developed on -- installed from the same
# dsh release a month earlier -- holds 4.0.1 / 1.0.16 / 1.1.3 and boots. The
# service is real in both (the type name is `Hmr`); only the method is missing.
#
# --before restores the tree as it stood when this release was current, which is
# the only snapshot of "the versions it was written against" we have. The image
# build prints the versions it actually got, so a drift shows up in the log. Move
# this date only together with DSH_VERSION.
ARG DSH_BEFORE=2026-08-23T03:55:00Z

RUN npm install -g "@deepseek-ai/dsh@${DSH_VERSION}" \
        --before "${DSH_BEFORE}" \
        --allow-scripts=koffi,node-pty,protobufjs,@google/genai,@deepseek-ai/dsh-subprocess-local \
    && npm cache clean --force

# Check the one dependency invariant this image actually depends on, and record
# the surrounding facts while doing it.
#
# `dsh-app-boot` calls `hmr.registerConfig`; `cordis-plugin-hmr` only has it in
# the older releases the `--before` pin selects. Asserting it here turns a
# future drift -- a bumped DSH_BEFORE, a yanked version, a registry hiccup --
# into one named line instead of a stack trace from inside the smoke test.
RUN set -eux; \
    D=/usr/local/lib/node_modules/@deepseek-ai/dsh/node_modules/@deepseek-ai; \
    echo '--- the tree the pin produced ---'; \
    for p in cordis cordis-plugin-hmr cordis-plugin-timer cordis-plugin-loader; do \
        printf '%-24s' "$p"; \
        grep -m1 '"version"' "$D/$p/package.json" || echo ' ABSENT'; \
    done; \
    printf '%-24s' 'registerConfig hits'; \
    grep -c registerConfig "$D/cordis-plugin-hmr/lib/index.js" || true; \
    if ! grep -q registerConfig "$D/cordis-plugin-hmr/lib/index.js"; then \
        echo 'cordis-plugin-hmr has no registerConfig: dsh-app-boot would die in'; \
        echo 'watchUserPatches with "hmr.registerConfig is not a function". Check'; \
        echo 'DSH_BEFORE against the versions this dsh release was built with.'; \
        exit 1; \
    fi; \
    echo '--- node-addon-require-builtin packages ---'; \
    ls -d "$D"/node-addon-require-builtin* || true; \
    node -e "const p = '$D/node-addon-require-builtin'; let r; try { r = require(p).requireBuiltin('internal/modules/esm/loader') } catch (e) { console.log('addon route threw:', e.code || e.message) } console.log('addon route:', r ? 'resolves the ESM loader' : 'UNAVAILABLE')"; \
    node --expose-internals -e "let l; try { l = require('internal/modules/esm/loader').getOrInitializeCascadedLoader() } catch (e) { console.log('flag route threw:', e.code || e.message) } console.log('flag route:', l ? 'resolves the ESM loader' : 'UNAVAILABLE')"

# Boot the harness here, in the image, and require it to still be answering
# 20 seconds later.
#
# Every failure this catches looks identical from outside -- the process starts,
# prints its URL, then dies seconds later -- and DSH's own diagnostics are
# misleading or absent. All three of these really happened, and each cost a
# round of manual debugging:
#
#   * npm nested a plugin outside Cordis's search path; the boot dies with
#     "plugin(s) failed to load: ... could not be resolved" and no reason;
#   * a secrets file the host share reports as world-readable;
#   * the dependency tree drifting forward of the release it belongs to, so the
#     HMR service exists but lacks the method `dsh-app-boot` calls -- reported as
#     "user patch-layer watching requires the Cordis HMR service" with the `hmr`
#     row disabled, and as "hmr.registerConfig is not a function" with it enabled.
#
# The probe is an HTTP request rather than a process check on purpose: a crashed
# background child can still be seen as a live (zombie) pid by `kill -0`, but it
# cannot answer a request.
#
# The smoke home is seeded with the same patch layer the run script installs, so
# a patch that fails to compose -- or that leaves a row unloadable -- fails the
# build too. That patch owns the bind, so the probe uses its port and takes no
# --port flag. Its `hmr` row is not what makes HMR work (the `DSH_BEFORE` pin
# above is); it is here because a mounted row fails loudly where a plugin created
# after the tree settles fails silently.
COPY cordis.patch.yml /tmp/dsh-smoke-seed/cordis.patch.yml

RUN set -eux; \
    cd /tmp; \
    mkdir -p /tmp/dsh-smoke; \
    cp /tmp/dsh-smoke-seed/cordis.patch.yml /tmp/dsh-smoke/cordis.patch.yml; \
    DSH_HOME=/tmp/dsh-smoke dsh web --no-open > /tmp/dsh-smoke.log 2>&1 & \
    pid=$!; \
    sleep 20; \
    if ! curl -sf -o /dev/null http://127.0.0.1:3080/; then \
        echo '--- the harness is not answering 20s after boot ---'; \
        cat /tmp/dsh-smoke.log; \
        kill "$pid" 2>/dev/null || true; \
        exit 1; \
    fi; \
    kill "$pid" 2>/dev/null || true; \
    sleep 1; \
    head -n 5 /tmp/dsh-smoke.log; \
    rm -rf /tmp/dsh-smoke /tmp/dsh-smoke-seed /tmp/dsh-smoke.log

# Make `godot` reachable from a login shell.
#
# `ENV PATH="/opt/godot:..."` above is inherited by `docker run` and by non-login
# shells, but Debian's /etc/profile *assigns* PATH outright before sourcing
# /etc/profile.d, so it survives neither `bash -l` nor `bash -lc`. `test-sandbox.ps1`
# probes that way, and a shell inside an agent session starts the same way, so the
# toolchain that answers becomes apt's `dotnet` (/usr/bin) and the global `dsh`
# (/usr/local/bin) while `godot` reports "command not found" -- an image that looks
# half-installed for reasons that have nothing to do with Godot.
#
# A wrapper rather than a symlink, deliberately: GodotSharp/ has to stay a sibling
# of the *binary*, and exec'ing the real path keeps the running executable at
# /opt/godot/godot, where the runtime looks for it. A symlink in /usr/local/bin
# would make the executable's own directory ambiguous instead.
#
# Deliberately below the dependency install rather than beside the PATH it amends:
# up there it would invalidate that layer and cost a full npm re-resolve for one
# file.
RUN printf '%s\n' '#!/bin/sh' 'exec /opt/godot/godot "$@"' > /usr/local/bin/godot \
    && chmod +x /usr/local/bin/godot \
    && ls -l /usr/local/bin/godot

# The project is mounted here, so this is also the session workspace root.
WORKDIR /workspace

# Documentation only: publishing a port is what actually exposes it, and the
# run script publishes it on the host loopback.
EXPOSE 3080

# `--no-open` because there is no browser in the container; the host script
# opens the page. The bind address is NOT a flag: `dsh web --host 0.0.0.0` is
# refused by the CLI on purpose, and the default 127.0.0.1 bind is unreachable
# through a published Docker port. docker/cordis.patch.yml overrides the
# webserver row's config instead, and DSH_HOME is set by the run script.
CMD ["dsh", "web", "--no-open"]
