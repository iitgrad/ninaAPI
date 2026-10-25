#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
dotnet publish src/StarfrontControl.Cli -c Release -o "$HOME/.local/share/starfront-control/cli"
mkdir -p "$HOME/.local/bin"
printf '%s\n' '#!/bin/sh' 'exec dotnet "$HOME/.local/share/starfront-control/cli/starfront-control.dll" "$@"' > "$HOME/.local/bin/starfront-control"
chmod 755 "$HOME/.local/bin/starfront-control"
printf '%s\n' 'Installed starfront-control in ~/.local/bin (requires dotnet on PATH).'
