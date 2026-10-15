#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
clinic_url="${CLINIC_API_URL:-http://127.0.0.1:5278}"
clinic_api_pid=""
cleanup() { if [ -n "$clinic_api_pid" ]; then kill "$clinic_api_pid" 2>/dev/null || true; fi; }
trap cleanup EXIT INT TERM
if ! curl --silent --fail "$clinic_url/health" >/dev/null; then
  ./scripts/run-api.sh > /tmp/clinic-api-start.log 2>&1 &
  clinic_api_pid=$!
  clinic_ready=0
  for clinic_attempt in $(seq 1 30); do
    if curl --silent --fail "$clinic_url/health" >/dev/null; then clinic_ready=1; break; fi
    if ! kill -0 "$clinic_api_pid" 2>/dev/null; then break; fi
    sleep 1
  done
  if [ "$clinic_ready" -ne 1 ]; then cat /tmp/clinic-api-start.log >&2; exit 1; fi
fi
export CLINIC_API_URL="$clinic_url"
./scripts/run-desktop.sh
