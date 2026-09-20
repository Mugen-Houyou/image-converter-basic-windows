---
name: build-all
description: >-
  Build this .NET solution the project's canonical way — in BOTH Debug and
  Release configurations. Use whenever the user asks to build, rebuild, or
  compile this project ("빌드", "빌드해", "build", "rebuild", "compile").
  Debug-only is never sufficient here because the user runs the Release build
  (Windows: bin\Release; macOS: /Applications), so a Debug-only build leaves
  their real executable stale. Pick the target per OS: the full .sln on
  Windows, cross-platform.slnf on macOS/Linux (see below).
allowed-tools: Bash, PowerShell
---

# build-all

Build in **both** configurations — Debug first, then Release. **Which target you
build depends on the OS**, because the full solution includes
`ImageConverter.Wpf` (`net9.0-windows`), which only builds on Windows.

## Windows — full solution

```
dotnet build image-converter-basic-windows.sln
dotnet build image-converter-basic-windows.sln -c Release
```

## macOS / Linux — solution filter (WPF excluded)

The full `.sln` fails on non-Windows with:

```
error NETSDK1100: To build a project targeting Windows on this operating
system, set the EnableWindowsTargeting property to true.  [ImageConverter.Wpf]
```

Use the solution filter that ships for exactly this — it contains only
`ImageConverter.Core` + `ImageConverter.Avalonia`:

```
dotnet build cross-platform.slnf
dotnet build cross-platform.slnf -c Release
```

(Forcing WPF with `EnableWindowsTargeting=true` only makes it *compile* — the
result can't run on macOS/Linux anyway, so excluding it via the filter is the
correct move, not a workaround.)

## Success criteria

Each build must end with `Build succeeded.` and **0 Error(s)**. Report both
results (Debug and Release) back to the user — don't report only one.

## Known gotcha: Release fails while the app is running

The user often runs the Release build. If it's open, the Release build can fail
with a file-lock error because the output DLLs are in use.

- **Windows** — `MSB3026`/`MSB3027`:
  ```
  Could not copy "...\ImageConverter.Core.dll" ... because it is being used by
  another process. The file is locked by: "image-converter-basic-windows (PID)".
  ```
  Look for processes `image-converter-basic-windows` (WPF) or
  `ImageConverter.Avalonia`.
- **macOS/Linux** — a plain `dotnet build` writes to `bin/<Config>` and usually
  won't clash with a running self-contained `.app` (it runs from its own
  bundle). But if you rebuild via `dotnet publish` into a path the running app
  uses, quit it first. Running process: `ImageConverter.Avalonia`.

When this happens **do not retry blindly**. Either ask the user to close the
running app, or — if authorized — stop it (`Stop-Process -Id <PID>` on Windows,
`kill <PID>` on macOS/Linux) before rebuilding.

## Notes

- Three projects: `ImageConverter.Core` (shared logic), `ImageConverter.Wpf`,
  `ImageConverter.Avalonia`. The `.sln` covers all three (Windows);
  `cross-platform.slnf` covers Core + Avalonia (macOS/Linux).
- This skill only **compiles**. The runnable macOS `.app` bundle is a separate
  step (`dotnet publish -r osx-arm64 --self-contained` + manual bundling) — not
  part of build-all.
- Output of a plain `dotnet build`:
  - WPF: `ImageConverter.Wpf\bin\<Config>\net9.0-windows\image-converter-basic-windows.exe`
  - Avalonia (Windows): `ImageConverter.Avalonia\bin\<Config>\net9.0\ImageConverter.Avalonia.exe`
  - Avalonia (macOS/Linux): `ImageConverter.Avalonia/bin/<Config>/net9.0/ImageConverter.Avalonia` (apphost, no extension) alongside the `.dll`
- A harmless `LF will be replaced by CRLF` git warning may appear on Windows —
  ignore it; it is not a build error.
