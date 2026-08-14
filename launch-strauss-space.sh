#!/usr/bin/env bash
set -euo pipefail

PROJECT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
EDITOR="$PROJECT_DIR/.tools/unity-editors/6000.3.22f1/Editor/Unity"

if [[ ! -x "$EDITOR" ]]; then
    echo "Unity Editor not found: $EDITOR" >&2
    exit 1
fi

# Keep Unity Hub and the Editor on the same project-local license profile.
export HOME="$PROJECT_DIR/.hub-home"
export XDG_CONFIG_HOME="$HOME/.config"

exec "$EDITOR" -projectPath "$PROJECT_DIR" "$@"
