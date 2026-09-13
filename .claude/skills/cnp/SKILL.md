---
name: cnp
description: >-
  Commit and push ("cnp") the working tree with a detailed, self-contained
  commit message. A writer subagent drafts it from the staged diff plus the
  main session's intent summary, a script checks the format, and an
  independent verifier subagent fact-checks every claim against the diff
  and the summary before anything is committed. Use when the user runs
  /cnp or asks to commit and push ("cnp", "commit and push",
  "commit & push", "커밋하고 푸시", "커밋 및 푸시", "커밋 푸시"). Does NOT
  build — run /build-all first if a build is wanted. No confirmation gate;
  it only stops for safety (junk or secret files about to be staged,
  changes that should be split into several commits, a blocking verifier
  finding that one revision did not fix, or a rejected push).
allowed-tools: Bash, Agent, SendMessage, Write
---

# cnp — commit and push

One shot: stage → writer drafts → script checks format → verifier
fact-checks → (one revision if needed) → main reality-check → commit →
push. Invoking /cnp *is* the approval; do not ask "shall I commit?".
Do not build here — that is /build-all's job.

Files in this skill:

- `writer-brief.md` — read by the writer subagent
- `verifier-brief.md` — read by the verifier subagent
- `scripts/check-msg.sh` — deterministic format checks (bash 3.2+)

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
  context — the subagents read it themselves.

## 2. Write the intent summary

3–6 lines of what THIS session knows and the diff cannot show:

- the problem or need, and what the user actually asked for
- decisions made along the way and alternatives rejected, with reasons
- how it was verified (builds run, manual checks, tests)

If the changes were made outside this session, say exactly that:
"No conversational context available — describe from the diff only."

This summary is also the verifier's contract: the verifier treats it as
a trusted source, so anything wrong here becomes a wrong commit message.
State only what actually happened. Never invent motives or verification.

## 3. Spawn the writer subagent

Agent tool, `subagent_type: general-purpose`, foreground
(`run_in_background: false`), and NO cheaper `model` override — writing
a good message requires actually understanding the code. Keep this
agent's id: if the verifier asks for a revision you message the SAME
agent so it keeps the diff in context. Prompt:

```
You are writing the commit message for the currently STAGED changes in
this repository (cwd is the repo root).

First read `.claude/skills/cnp/writer-brief.md` and follow it exactly —
it defines what to inspect, the required style, and the exact output
format. Do not commit, stage, or modify anything; only inspect and write.

INTENT SUMMARY from the main session (context the diff cannot show):
<intent summary from step 2>
```

## 4. Extract and format-check

- `ATOMICITY: SPLIT` → do NOT commit. Show the reason and the suggested
  grouping and ask how to proceed. This is a deliberate stop.
- Save the text between `===MESSAGE===` and `===END===` (subject +
  body, NO trailers) to `<scratchpad>/commit-msg.txt` with the Write
  tool. The scratchpad directory is named in your system prompt.
- Run `bash .claude/skills/cnp/scripts/check-msg.sh <that file>`.
- `ERROR` lines: fix trivial ones yourself (re-wrap a long line, add the
  blank line) and re-run; anything else goes back to the writer using
  the step 6 mechanics. `WARN` lines: use judgement.

## 5. Spawn the verifier subagent

A FRESH `general-purpose` agent (not the writer), foreground, no cheaper
`model` override. It gets the message and the intent summary, and reads
the staged diff itself. Prompt:

```
You are fact-checking a proposed commit message for the currently
STAGED changes in this repository (cwd is the repo root).

First read `.claude/skills/cnp/verifier-brief.md` and follow it
exactly. Do not commit, stage, or modify anything.

INTENT SUMMARY (trusted source, from the main session):
<intent summary from step 2>

PROPOSED MESSAGE:
===MESSAGE===
<subject + body, no trailers>
===END===
```

## 6. Act on the verdict

- `PASS` → step 7.
- `NOTES` (only MINOR findings) → apply a finding yourself when the fix
  is an obvious rewording that keeps the meaning; otherwise leave it.
  → step 7.
- `REVISE` (any BLOCKING finding) → send the findings to the SAME
  writer agent with SendMessage: "Revise the message to fix exactly
  these findings; keep everything else; output the complete message
  again in the ===MESSAGE=== format." Re-run the format script on the
  revision. **One revision round only.** Do not re-run the verifier;
  instead, in step 7, check each flagged item yourself. If a BLOCKING
  item is still wrong after the revision, stop and report the findings
  to the user.

## 7. Main reality-check

Read the final message once against what this session actually did.
This is cheap — you must read it to commit it anyway.

- every verification claim ("built Debug and Release", "tested by
  hand") really happened here; delete or soften any that did not
- the intent summary you wrote was accurate; if you now see it was not,
  correct the message accordingly
- nothing beyond what is staged is described
- no secrets, tokens, emails, internal IDs, or absolute personal paths

## 8. Commit

Append the attribution trailer lines that the CURRENT session's
instructions specify (e.g. `Co-Authored-By:` / `Claude-Session:`),
verbatim, after one blank line, to the scratchpad file. Never hardcode
or invent them; if the session specifies none, add none. Then:

```
git commit -F <scratchpad>/commit-msg.txt
```

Committing from the file keeps the wrapping exactly as checked.

## 9. Push

- `git push`. If "no upstream branch": `git push -u origin HEAD`.
- Rejected (non-fast-forward, remote has new commits): NEVER force-push,
  and do not auto-pull or auto-rebase. Report the error and suggest
  `git pull --rebase`; the user decides.

## 10. Report

Short: commit hash + subject, number of files changed, push range
(`old..new  branch -> branch`), the verifier verdict, and whether a
revision round happened. If anything was skipped or failed, say exactly
what and why.
