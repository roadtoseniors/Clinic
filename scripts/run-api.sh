#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
clinic_dotnet="$(command -v dotnet || true)"
if [ -x "$HOME/.dotnet/dotnet" ]; then clinic_dotnet="$HOME/.dotnet/dotnet"; fi
if [ -z "$clinic_dotnet" ]; then echo 'Установите .NET SDK 10.' >&2; exit 1; fi
export TZ=Europe/Moscow
"$clinic_dotnet" build Clinic.API -p:UsedAvaloniaProducts=
cd Clinic.API
exec "$clinic_dotnet" bin/Debug/net10.0/Clinic.API.dll --urls "${CLINIC_API_URL:-http://127.0.0.1:5278}"
