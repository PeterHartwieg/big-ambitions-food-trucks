# The game PC's lock file, shared by every session that tests in game (sourced by run-remote.sh and install-remote.sh).
# C:\Users\Peter\game-lock.txt: no file means free. The holder writes owner, since, until and what is running, and
# deletes it when done. While someone else holds it: no install, start, stop, quit or save load.
# This session's owner text comes from BA_LOCK_OWNER (default "Food Trucks session").

LOCK_OWNER="${BA_LOCK_OWNER:-Food Trucks session}"

lock_text() {
  ssh "$HOST" 'if (Test-Path $env:USERPROFILE\game-lock.txt) { Get-Content $env:USERPROFILE\game-lock.txt }' < /dev/null 2>/dev/null \
    | { grep -v '^\*\*' || true; } | tr -d '\r'
}

# Refuses (exit 1) when someone else holds the lock.
require_lock_free_or_ours() {
  local text owner
  text="$(lock_text)"
  [ -z "$text" ] && return 0
  owner="$(sed -n 's/^owner: //p' <<<"$text" | head -1)"
  case "$owner" in
    "$LOCK_OWNER"*) return 0 ;;
  esac
  echo "The game PC is locked by another session; not touching the game:" >&2
  sed 's/^/  /' <<<"$text" >&2
  echo "Ask the owner for it, or wait until they delete game-lock.txt." >&2
  exit 1
}

take_lock() {
  local minutes="${1:-60}" what="${2:-Food Trucks test session}"
  require_lock_free_or_ours
  local since until
  since="$(date '+%Y-%m-%d %H:%M')"
  until="$(date -v+"${minutes}"M '+%H:%M')"
  ssh "$HOST" "Set-Content -Path \$env:USERPROFILE\\game-lock.txt -Value @('owner: $LOCK_OWNER', 'since: $since', 'until: about $until', 'game: $what', 'rules: no install, restart, load or quit while held; message the owner to ask for it')" < /dev/null
  echo "Lock taken: $LOCK_OWNER until about $until."
}

drop_lock() {
  require_lock_free_or_ours
  ssh "$HOST" 'Remove-Item -ErrorAction SilentlyContinue $env:USERPROFILE\game-lock.txt' < /dev/null
  echo "Lock released."
}
