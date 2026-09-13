---
name: cnp
description: >-
  Commit and push ("cnp") the working tree with a detailed, self-contained
  commit message that a subagent writes from the staged diff plus the main
  session's intent summary. Use when the user runs /cnp or asks to commit
  and push ("cnp", "commit and push", "commit & push", "커밋하고 푸시",
  "커밋 및 푸시", "커밋 푸시"). Does NOT build — run /build-all first if a
  build is wanted. No confirmation gate; it only stops for safety (junk or
  secret files about to be staged, staged changes that should be split into
  several commits, or a rejected push).
allowed-tools: Bash, Agent
---

# cnp — commit and push

One shot: stage → a subagent writes the message → commit → push.
Invoking /cnp *is* the approval; do not ask "shall I commit?".
Do not build here — that is /build-all's job.

## 1. Inspect and stage

- `git status --short`. Clean tree → say "nothing to commit" and stop.
- Detached HEAD → stop and report.
- Check UNTRACKED files. Stop and ask before staging only if something
  clearly should not be versioned and is not gitignored: build output
  (`bin/`, `obj/`), `.claude/settings.local.json`, secrets/env files
  (`.env*`, `*.pem`, `*.key`, credentials), large binaries. Suggest a
  `.gitignore` entry. This is a safety stop, not a confirmation gate.
- `git add -A`
- `git diff --cached --stat` only. Do NOT dump the full diff into this
  context — the subagent reads it itself.

## 2. Write the intent summary

3–6 lines of what THIS session knows and the diff cannot show:

- the problem or need, and what the user actually asked for
- decisions made along the way and alternatives rejected, with reasons
- how it was verified (builds run, manual checks, tests)

If the changes were made outside this session, say exactly that:
"No conversational context available — describe from the diff only."
Never invent motives.

## 3. Spawn the writer subagent

Agent tool, `subagent_type: general-purpose`, foreground
(`run_in_background: false`), and NO cheaper `model` override — writing a
good message requires actually understanding the code. Prompt:

```
You are writing the commit message for the currently STAGED changes in
this repository (cwd is the repo root).

First read `.claude/skills/cnp/writer-brief.md` and follow it exactly —
it defines what to inspect, the required style, and the exact output
format. Do not commit, stage, or modify anything; only inspect and write.

INTENT SUMMARY from the main session (context the diff cannot show):
<intent summary from step 2>
```

## 4. Handle the result

- Take the text between `===MESSAGE===` and `===END===`, and the
  `ATOMICITY:` line after it.
- `ATOMICITY: SPLIT` → do NOT commit. Show the reason and the suggested
  grouping and ask how to proceed. This is the one deliberate stop.
- Sanity check: subject ≤ 72 chars (≤ 50 preferred), exactly one blank
  line after the subject, body lines ≤ 72 columns. Fix trivial overruns
  by re-wrapping; do not rewrite the content.

## 5. Commit

Append the attribution trailer lines that the CURRENT session's
instructions specify (e.g. `Co-Authored-By:` / `Claude-Session:`),
verbatim, after one blank line. Never hardcode or invent them; if the
session specifies none, add none.

Commit via stdin so the formatting survives untouched:

```
git commit -F - <<'EOF'
<subject>

<body>

<trailers>
EOF
```

## 6. Push

- `git push`. If "no upstream branch": `git push -u origin HEAD`.
- Rejected (non-fast-forward, remote has new commits): NEVER force-push,
  and do not auto-pull or auto-rebase. Report the error and suggest
  `git pull --rebase`; the user decides.

## 7. Report

Short: commit hash + subject, number of files changed, push range
(`old..new  branch -> branch`). If anything was skipped or failed, say
exactly what and why.
