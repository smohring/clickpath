# Downloads the latest uBlock Origin release (Chromium build, Manifest V2) from GitHub and unpacks it into
# %LocalAppData%\Clickpath\extensions\ublock-origin, where Clickpath loads it. Run again to update.
# Keep this file ASCII: PowerShell 5.1 reads scripts as ANSI.
$ErrorActionPreference = 'Stop'
$extensions = Join-Path $env:LOCALAPPDATA 'Clickpath\extensions'
$target = Join-Path $extensions 'ublock-origin'

$release = Invoke-RestMethod 'https://api.github.com/repos/gorhill/uBlock/releases/latest' -Headers @{ 'User-Agent' = 'Clickpath' }
$asset = $release.assets | Where-Object { $_.name -like 'uBlock0_*.chromium.zip' } | Select-Object -First 1
if (-not $asset) { throw 'No chromium zip found in the latest uBlock Origin release' }
Write-Host "Downloading $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) MB)"

$work = Join-Path ([IO.Path]::GetTempPath()) "lf-ublock-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $work | Out-Null
try {
    $zip = Join-Path $work $asset.name
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing
    Expand-Archive $zip (Join-Path $work 'x')
    $manifest = Get-ChildItem (Join-Path $work 'x') -Recurse -Filter manifest.json | Select-Object -First 1
    if (-not $manifest) { throw 'The archive holds no manifest.json' }

    New-Item -ItemType Directory -Force $extensions | Out-Null
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Move-Item $manifest.Directory.FullName $target
    Write-Host "Installed $($release.tag_name) into $target"
    Write-Host 'Restart Clickpath and switch the shield button on.'
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
