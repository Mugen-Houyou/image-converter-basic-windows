---
name: build-all
description: >-
  Build this .NET solution the project's canonical way — the FULL solution in
  BOTH Debug and Release configurations. Use whenever the user asks to build,
  rebuild, or compile this project ("빌드", "빌드해", "build", "rebuild",
  "compile"). Debug-only is never sufficient here because the user runs the app
  from bin\Release, so a Debug-only build leaves their real executable stale.
allowed-tools: Bash, PowerShell
---

# build-all

Build the whole solution in **both** configurations. Order: Debug first, then Release.

```
dotnet build image-converter-basic-windows.sln
dotnet build image-converter-basic-windows.sln -c Release
```

## Success criteria

Each build must end with `Build succeeded.` and **0 Error(s)**. Report both
results (Debug and Release) back to the user — don't report only one.

## Known gotcha: Release fails while the app is running

The user often runs the app from `bin\Release`. If it's open, the Release build
fails with `MSB3026`/`MSB3027` file-lock errors like:

```
Could not copy "...\ImageConverter.Core.dll" ... because it is being used by
another process. The file is locked by: "image-converter-basic-windows (PID)".
```

When this happens **do not retry blindly**. Either:

- Ask the user to close the running app, then rebuild, **or**
- If they've authorized it, stop the process (e.g. `Stop-Process -Id <PID>`)
  before rebuilding.

To check first: look for processes `image-converter-basic-windows` (WPF) or
`ImageConverter.Avalonia`.

## Notes

- The solution has three projects: `ImageConverter.Core` (shared logic),
  `ImageConverter.Wpf`, and `ImageConverter.Avalonia`. Building the `.sln`
  covers all three.
- Output executables land in:
  - WPF: `ImageConverter.Wpf\bin\<Config>\net9.0-windows\image-converter-basic-windows.exe`
  - Avalonia: `ImageConverter.Avalonia\bin\<Config>\net9.0\ImageConverter.Avalonia.exe`
- A harmless `LF will be replaced by CRLF` warning may appear from git on
  Windows — ignore it; it is not a build error.
