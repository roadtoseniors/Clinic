#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
clinic_dotnet="$(command -v dotnet || true)"
if [ -x "$HOME/.dotnet/dotnet" ]; then clinic_dotnet="$HOME/.dotnet/dotnet"; fi
if [ -z "$clinic_dotnet" ]; then echo 'Установите .NET SDK 10.' >&2; exit 1; fi
export TZ=Europe/Moscow
exec "$clinic_dotnet" run --project Clinic.Tests -p:UsedAvaloniaProducts= -- --seed-api
