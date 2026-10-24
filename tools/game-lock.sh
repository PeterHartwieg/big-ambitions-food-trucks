# Shared normal-game ownership. Explicit owner tokens replace the old name-prefix bypass.
# Existing game-lock.txt reservations remain blocked until their original holder releases them.
lock_text() { BA_GAME_HOST="$HOST" python3 "$REPO/tools/bridge-lock" status; }
require_lock_free_or_ours() { BA_GAME_HOST="$HOST" python3 "$REPO/tools/bridge-lock" assert; }
take_lock() {
  BA_GAME_HOST="$HOST" python3 "$REPO/tools/bridge-lock" setup
  BA_GAME_HOST="$HOST" python3 "$REPO/tools/bridge-lock" acquire --purpose "${2:-Game test session}"
  echo 'Ownership lasts until explicit release; the historical minutes argument no longer expires it.'
}
drop_lock() { BA_GAME_HOST="$HOST" python3 "$REPO/tools/bridge-lock" release; }
