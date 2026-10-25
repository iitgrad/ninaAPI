#!/bin/sh
# Build a package only. Never copies into N.I.N.A. or changes configuration.
set -eu
cd "$(dirname "$0")/.."
version=$(sed -n 's/.*AssemblyVersion("\([^"]*\)").*/\1/p' ninaAPI/Properties/AssemblyInfo.cs)
test -n "$version"
output=${1:-"artifacts/StarfrontControl-$version.zip"}
mkdir -p "$(dirname "$output")"
archive="$(cd "$(dirname "$output")" && pwd)/$(basename "$output")"
if test -e "$archive"; then
    printf '%s\n' "Refusing to overwrite existing package: $archive" >&2
    exit 1
fi
staging=$(mktemp -d)
trap 'rm -rf "$staging"' EXIT HUP INT TERM
dotnet build ninaAPI/ninaAPI.csproj -c Release -p:EnableWindowsTargeting=true -p:CopyLocalLockFileAssemblies=true --output "$staging"
(
    cd "$staging"
    zip -q "$archive" StarfrontControl.dll StarfrontControl.Core.dll EmbedIO.dll Swan.Lite.dll StarfrontControl.dll.config
)
printf '%s\n' "Prepared plugin $version: $archive"
