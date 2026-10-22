---
name: sync-app
description: >-
  Sync the installed macOS app to the latest code in one shot: git fetch →
  fast-forward pull → Release build (self-contained osx-arm64 .app) → replace
  /Applications/ImageConverter.app. Use when the user runs /sync-app or asks to
  sync / update / refresh the installed app to the latest ("sync-app",
  "앱 동기화", "앱 최신화", "최신으로 업데이트", or the recurring "fetch/pull
  하고 Release 빌드해서 /Applications 교체" ritual). macOS / Apple Silicon only —
  the deploy target is /Applications. Does NOT launch the app; the user runs it.
  Invoking it IS the approval; it stops only for safety (dirty tree,
  non-fast-forward pull, detached HEAD, wrong platform, build failure).
allowed-tools: Bash
---

# sync-app — update the installed macOS app to the latest

End state: `/Applications/ImageConverter.app` is the current `HEAD`'s Release
build, ready for the user to launch. One shot: fetch → ff pull → Release `.app`
build → replace the installed copy. Invoking /sync-app *is* the approval — do
not ask "shall I pull/deploy?". **Do NOT launch the app** — launching is the
user's; this skill leaves it stopped.

Scope: **macOS on Apple Silicon** (`osx-arm64`). The `/Applications` deploy is
mac-only; on Windows this does not apply (run the full solution via
`/build-all` and use the Release build from `bin\Release`).

This skill only compiles via `dotnet publish`; it does not run `/build-all`.

## 1. Preflight — safety stops

Stop and report (do not continue) if any of these holds:

- Not macOS arm64: `uname -s` ≠ `Darwin` or `uname -m` ≠ `arm64`.
- Detached HEAD: `git symbolic-ref -q HEAD` fails.
- No upstream for the current branch: `git rev-parse --abbrev-ref @{u}` fails.
- Dirty working tree: `git status --short` is non-empty. A sync assumes a clean
  checkout; let the user commit (see `/cnp`) or stash first. Note: the build
  artifacts `ImageConverter*.app/` and `publish/` are gitignored and never make
  the tree dirty.

## 2. Fetch & preview

```sh
git fetch --all --prune
git rev-list --left-right --count @{u}...HEAD   # left=behind right=ahead
git log --oneline HEAD..@{u}                     # incoming commits
git diff --stat HEAD..@{u}                       # files they touch
```

Summarize the incoming commits for the user. `ahead` > 0 (local commits not on
the remote) is fine for a fast-forward *in*, but if the pull in step 3 is not a
fast-forward, that is a safety stop.

## 3. Fast-forward pull

```sh
git merge --ff-only @{u}
```

- Success (or "Already up to date") → continue. Even when already up to date,
  still rebuild and redeploy in the next steps: the installed copy may have been
  built from an older HEAD, and the rebuild is the only thing that guarantees
  `/Applications` matches `HEAD`.
- Fails (non-fast-forward: branches diverged) → **stop.** Never force, rebase,
  or reset. Report and suggest `git pull --rebase`; the user decides.

## 4. Release build → `.app`

Clean last run's artifacts, publish self-contained for `osx-arm64`, assemble the
bundle:

```sh
rm -rf ImageConverter.app publish/osx-arm64
dotnet publish ImageConverter.Avalonia -c Release -r osx-arm64 --self-contained \
    -o ./publish/osx-arm64 --nologo -v minimal

mkdir -p ImageConverter.app/Contents/MacOS ImageConverter.app/Contents/Resources
cp ImageConverter.Avalonia/Info.plist ImageConverter.app/Contents/Info.plist
cp -R ./publish/osx-arm64/. ImageConverter.app/Contents/MacOS/
chmod +x ImageConverter.app/Contents/MacOS/ImageConverter.Avalonia
```

Publish failure (non-zero exit, or no `Build succeeded`) → **stop** and report
the error; do not deploy a broken build. Verify the bundle before deploying:

- `file …/MacOS/ImageConverter.Avalonia` → `Mach-O 64-bit executable arm64`
- native dylibs present at the bundle root: `libSkiaSharp.dylib`,
  `libAvaloniaNative.dylib`, `libHarfBuzzSharp.dylib`, and
  `Magick.Native-Q8-arm64.dll.dylib` (the AVIF encoder — easy to miss)
- `Avalonia.Diagnostics.dll` is **absent** (it is Debug-only; its presence means
  a Debug build slipped in)

(Default `dotnet` is the .NET 10 SDK; the project targets `net9.0` and
publishes fine with it — the self-contained publish pulls the net9.0 runtime
pack. No `global.json` needed.)

## 5. Deploy to `/Applications`

**Quit any running instance first.** macOS keys running apps by
`CFBundleIdentifier`; a bundle cannot be cleanly replaced while running, and a
running process keeps the old code. So:

```sh
if pgrep -f '/Applications/ImageConverter.app.*ImageConverter.Avalonia' >/dev/null; then
  osascript -e 'tell application id "com.seanergyp.imageconverter" to quit'   # graceful
  # wait for exit; if still alive after a few seconds, fall back to: kill <pid>
fi

rm -rf /Applications/ImageConverter.app
ditto ./ImageConverter.app /Applications/ImageConverter.app
chmod +x /Applications/ImageConverter.app/Contents/MacOS/ImageConverter.Avalonia
xattr -dr com.apple.quarantine /Applications/ImageConverter.app 2>/dev/null
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister \
    -f /Applications/ImageConverter.app 2>/dev/null
```

If an instance had to be quit, say so in the report (the user may have been
using it).

## 6. Verify & report

Prove the installed copy is the fresh build — compare the two `ImageConverter.Core.dll`:

```sh
shasum -a 256 /Applications/ImageConverter.app/Contents/MacOS/ImageConverter.Core.dll
shasum -a 256 ./ImageConverter.app/Contents/MacOS/ImageConverter.Core.dll   # must match
```

Report, short:

- pull range (`old..new  branch -> branch`) or "already up to date", with a
  one-line note of what the incoming commits were
- build result (succeeded; bundle verified arm64 + native libs)
- deploy: installed `Core.dll` hash matches the fresh build ✅, exec bit set,
  whether a running instance had to be quit
- **not launched** — the user launches it from Finder

## Notes & gotchas

- **Bundle-id collision:** while an instance with id `com.seanergyp.imageconverter`
  runs, `open <other-copy>.app` just *activates the running one* instead of
  launching the new bundle — so "a process is running" never proves the *new*
  build launched. This skill does not launch at all, but keep it in mind if
  asked to: quit first, then launch, and verify by RSS (~100 MB once the
  runtime loads) and a fresh `etime`, not by `pgrep` alone.
- The Debug bundle (`ImageConverter-Debug.app`, id `…​.debug` via
  `Info.Debug.plist`) is a separate app and is **not** part of sync-app, which
  deploys Release only.
- Everything built here (`ImageConverter.app`, `publish/`) is gitignored, so a
  sync never changes the tree. This skill does not commit or push.
