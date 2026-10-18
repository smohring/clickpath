param([string]$Version = '1.0.0')

# Publishes the app (framework-dependent, win-x64) and zips it to dist/Clickpath-<version>-win-x64.zip.
# PathMap keeps the local build folder out of the binaries.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'dist\publish'
$zip = Join-Path $root "dist\Clickpath-$Version-win-x64.zip"

if (Test-Path (Join-Path $root 'dist')) { Remove-Item (Join-Path $root 'dist') -Recurse -Force }
dotnet publish (Join-Path $root 'Clickpath\Clickpath.csproj') -c Release -r win-x64 --self-contained false -o $publish "-p:PathMap=$root=/src"
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

Remove-Item (Join-Path $publish '*.pdb') -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE'), (Join-Path $root 'CHANGELOG.md') $publish
Copy-Item (Join-Path $root 'examples') $publish -Recurse
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip
$zip
