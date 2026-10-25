#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
bun install --cwd "$PWD/web" --frozen-lockfile
bun build web/monitor.ts --target browser --outfile src/StarfrontControl.Service/wwwroot/app.js
bunx --package typescript tsc --noEmit --strict --target ES2022 --moduleResolution bundler --module esnext --resolveJsonModule --allowSyntheticDefaultImports --lib DOM,ES2022 web/monitor.ts
dotnet test tests/StarfrontControl.Core.Tests -c Release
bun test tests/integration/observation-analysis.test.ts web/target-visibility.test.ts web/sequence-presentation.test.ts
dotnet build ninaAPI/ninaAPI.csproj -c Release -p:EnableWindowsTargeting=true -p:CopyLocalLockFileAssemblies=true
dotnet build src/StarfrontControl.Service -c Release
dotnet build src/StarfrontControl.Cli -c Release
bun tests/integration/cli-smoke.ts
bun tests/integration/service-lifecycle.ts "$PWD/src/StarfrontControl.Service/bin/Release/net8.0/StarfrontControl.Service.dll"
