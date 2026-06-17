#!/usr/bin/env bash
# Ball2 verification harness.
# Runs Unity EditMode (default) or PlayMode tests headless and prints a structured,
# agent-readable pass/fail summary. Exit 0 = all passed, non-zero = failure.
#
# Usage:
#   ./Tools/run-tests.sh              # EditMode (fast, the default loop signal)
#   ./Tools/run-tests.sh PlayMode     # PlayMode integration tests
#
# Override Unity location with:  UNITY_BIN=/path/to/Unity ./Tools/run-tests.sh
#
# This is the ONLY valid "observe" step for Lane A work. A green compile with no
# passing test is NOT success.

set -uo pipefail

PLATFORM="${1:-EditMode}"
PROJECT_PATH="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RESULTS="${PROJECT_PATH}/test-results-${PLATFORM}.xml"
LOG="${PROJECT_PATH}/test-${PLATFORM}.log"

# --- Locate Unity -----------------------------------------------------------
UNITY_BIN="${UNITY_BIN:-}"
VER="$(grep -oE '6000\.[0-9.]+f[0-9]+' "${PROJECT_PATH}/ProjectSettings/ProjectVersion.txt" 2>/dev/null | head -1)"
if [[ -z "${UNITY_BIN}" ]]; then
  for c in \
    "/Applications/Unity/Hub/Editor/${VER}/Unity.app/Contents/MacOS/Unity" \
    "${HOME}/Unity/Hub/Editor/${VER}/Editor/Unity" \
    "/opt/unity/editors/${VER}/Editor/Unity" \
    "/c/Program Files/Unity/Hub/Editor/${VER}/Editor/Unity.exe" \
    "C:/Program Files/Unity/Hub/Editor/${VER}/Editor/Unity.exe"; do
    if [[ -e "$c" ]]; then UNITY_BIN="$c"; break; fi
  done
fi
if [[ -z "${UNITY_BIN}" ]]; then
  echo "FAIL: Unity binary not found (project version: ${VER:-unknown})." >&2
  echo "      Set UNITY_BIN=/path/to/Unity and retry." >&2
  exit 3
fi

# --- Locate python (for NUnit3 parsing) -------------------------------------
PY_BIN="$(command -v python3 || command -v python || true)"
if [[ -z "${PY_BIN}" ]]; then
  echo "FAIL: python3 (or python) not found; needed to parse test results." >&2
  exit 3
fi

rm -f "${RESULTS}" "${LOG}"

echo ">> Running ${PLATFORM} tests via: ${UNITY_BIN}"
# IMPORTANT: do NOT pass -quit alongside -runTests; the test runner owns its exit.
"${UNITY_BIN}" \
  -batchmode -nographics \
  -projectPath "${PROJECT_PATH}" \
  -runTests -testPlatform "${PLATFORM}" \
  -testResults "${RESULTS}" \
  -logFile "${LOG}"
UNITY_EXIT=$?

if [[ ! -f "${RESULTS}" ]]; then
  echo "FAIL: no results file produced (Unity exit=${UNITY_EXIT}). Last 40 log lines:" >&2
  tail -n 40 "${LOG}" 2>/dev/null >&2 || true
  exit 3
fi

# --- Structured summary (NUnit3 <test-run>) ---------------------------------
"${PY_BIN}" - "${RESULTS}" <<'PY'
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
g = lambda k, d=0: int(root.get(k, d))
total   = g("total", g("testcasecount"))
passed  = g("passed")
failed  = g("failed")
skipped = g("skipped")
dur     = root.get("duration", "?")
print(f"Tests: {total}  Passed: {passed}  Failed: {failed}  Skipped: {skipped}  ({dur}s)")
if failed:
    print("Failures:")
    for tc in root.iter("test-case"):
        if tc.get("result") == "Failed":
            name = tc.get("fullname", tc.get("name"))
            msg_el = tc.find("failure/message")
            msg = " ".join(msg_el.text.split())[:200] if (msg_el is not None and msg_el.text) else ""
            print(f"  - {name}: {msg}")
sys.exit(1 if failed else 0)
PY
PARSE_EXIT=$?

if [[ ${UNITY_EXIT} -ne 0 || ${PARSE_EXIT} -ne 0 ]]; then
  echo "RESULT: FAILED (unity=${UNITY_EXIT}, tests=${PARSE_EXIT})"
  exit 1
fi
echo "RESULT: PASSED"
exit 0