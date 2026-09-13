#!/usr/bin/env bash
# check-msg.sh — deterministic format checks for a commit message file.
#
# Usage:  bash .claude/skills/cnp/scripts/check-msg.sh <message-file>
#
# The file must hold the writer's message only: subject, one blank line,
# body. NO attribution trailers — the main agent appends those after the
# checks pass. Exit 0 = no errors (warnings may still print); exit 1 =
# at least one error. Portable to bash 3.2 (macOS) — no mapfile/assoc.

set -u

# Count characters rather than bytes where a UTF-8 locale is available;
# otherwise non-ASCII characters (e.g. an em dash) over-count slightly.
if locale -a 2>/dev/null | grep -qi '^C\.UTF-8$'; then
  export LC_ALL=C.UTF-8
elif locale -a 2>/dev/null | grep -qi '^en_US\.UTF-8$'; then
  export LC_ALL=en_US.UTF-8
fi

file="${1:-}"
if [ -z "$file" ] || [ ! -f "$file" ]; then
  echo "ERROR: usage: check-msg.sh <message-file>" >&2
  exit 1
fi

errors=0
warns=0
err()  { echo "ERROR: $*"; errors=$((errors + 1)); }
warn() { echo "WARN:  $*"; warns=$((warns + 1)); }

# CR characters would land verbatim in the commit object. Count bytes
# with tr: grep on MSYS/Cygwin treats CRLF as a line end and never sees
# the CR, so a grep-based check silently passes on Windows.
crcount=$(tr -cd '\r' < "$file" | wc -c | tr -d '[:space:]')
if [ "${crcount:-0}" -gt 0 ]; then
  err "file contains $crcount CR (\\r) characters — use LF line endings"
fi

n=0
while IFS= read -r line || [ -n "$line" ]; do
  n=$((n + 1))
  len=${#line}
  if [ "$n" -eq 1 ]; then
    if [ "$len" -eq 0 ]; then
      err "subject (line 1) is empty"
    elif [ "$len" -gt 72 ]; then
      err "subject is $len chars (max 72)"
    elif [ "$len" -gt 50 ]; then
      warn "subject is $len chars (prefer <= 50)"
    fi
    case "$line" in
      *.) warn "subject ends with a period" ;;
    esac
    case "$line" in
      [a-z]*) warn "subject starts lowercase" ;;
    esac
  elif [ "$n" -eq 2 ]; then
    if [ "$len" -ne 0 ]; then
      err "line 2 must be blank (separates subject from body)"
    fi
  else
    if [ "$len" -gt 72 ]; then
      err "line $n is $len chars (max 72)"
    fi
    case "$line" in
      Co-Authored-By:*|Claude-Session:*|Signed-off-by:*|Reviewed-by:*)
        err "line $n looks like a trailer — the main agent appends trailers, not the writer" ;;
    esac
  fi
done < "$file"

if [ "$n" -eq 0 ]; then
  err "message is empty"
fi

# Leftover protocol markers mean the extraction step failed.
if grep -Eq '^(===MESSAGE===|===END===|ATOMICITY:)' "$file"; then
  err "protocol markers (===MESSAGE=== / ===END=== / ATOMICITY:) must not be in the message"
fi

# Cheap heuristic for session references; the verifier does the real check.
pattern='as discussed|earlier in (this|the) (session|conversation)|the conversation|the user asked|in this session'
hits=$(grep -Ein "$pattern" "$file" || true)
if [ -n "$hits" ]; then
  while IFS= read -r hit; do
    warn "possible session reference: $hit"
  done <<EOF
$hits
EOF
fi

echo "errors=$errors warnings=$warns"
if [ "$errors" -gt 0 ]; then
  exit 1
fi
exit 0
