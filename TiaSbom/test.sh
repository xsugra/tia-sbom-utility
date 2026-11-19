#!/usr/bin/env bash
# Runs the automated tests on Linux or WSL (net8.0). No TIA Portal needed.
#   ./test.sh                      unit tests, error messages, CycloneDX schema
#   ./test.sh --gitea              plus integration tests against a real Gitea, see docs/10-testovanie.md
set -euo pipefail
cd "$(dirname "$0")"
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
# WSL without root: libicu extracted with "apt-get download libicu78 && dpkg-deb -x" into ~/.local/icu.
if [[ -d "$HOME/.local/icu/usr/lib/x86_64-linux-gnu" ]]; then
  export LD_LIBRARY_PATH="$HOME/.local/icu/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
fi

if [[ "${1:-}" == "--gitea" ]]; then
  : "${TIASBOM_TEST_GITEA_URL:?set TIASBOM_TEST_GITEA_URL, e.g. http://127.0.0.1:3000}"
  : "${TIASBOM_TEST_TOKEN:?set TIASBOM_TEST_TOKEN to a token of the test account}"
else
  unset TIASBOM_TEST_GITEA_URL TIASBOM_TEST_TOKEN
fi

"$DOTNET" test tests/TiaSbom.Tests -f net8.0 --logger "console;verbosity=normal" --logger "trx;LogFileName=results.trx"
