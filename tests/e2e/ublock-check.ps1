# MANUAL check: is uBlock Origin really blocking inside Clickpath's WebView2? Not part of run.ps1 (needs the extension installed
# with tools\get-ublock.ps1 and the network). Asks uBlock's own logger what it decided for three requests; prints the log lines.
# Keep this file ASCII: PowerShell 5.1 reads scripts as ANSI.
param([string]$Exe = (Join-Path $PSScriptRoot '..\..\Clickpath\bin\Debug\net10.0-windows\Clickpath.exe'))
$ErrorActionPreference = 'Stop'
$work = Join-Path ([IO.Path]::GetTempPath()) "lf-ublock-check-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $work | Out-Null
$appLog = Join-Path $env:LOCALAPPDATA 'Clickpath\app.log'

function Invoke-Plan($steps, $name) {
    $plan = Join-Path $work "$name.json"; $log = Join-Path $work "$name.log"
    # -InputObject keeps a one-step plan an array (piping would unwrap it into a single object)
    [IO.File]::WriteAllText($plan, (ConvertTo-Json -InputObject @($steps) -Depth 5), (New-Object Text.UTF8Encoding $false))
    $p = Start-Process $Exe -ArgumentList "`"$plan`" --run --adblock on --log `"$log`"" -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw "Clickpath exited with $($p.ExitCode) (is another instance running? see $log)" }
    Get-Content $log -Encoding UTF8
}

# 1. a first start tells us the extension id (it is derived from the folder path)
Invoke-Plan @(@{ Type = 'Navigate'; Url = 'about:blank' }) 'id' | Out-Null
$loaded = Get-Content $appLog | Select-String 'extension uBlock Origin \(([a-p]{32})\) loaded' | Select-Object -Last 1
if (-not $loaded) { throw 'uBlock Origin was not loaded - run tools\get-ublock.ps1 first and read the "extension" lines in app.log' }
$id = $loaded.Matches[0].Groups[1].Value
$dashboard = "chrome-extension://$id/dashboard.html"

# 2. switch the logger on, request ad hosts from a normal page, read the log from the dashboard (RunScript does not await: park results in window)
$read = "window.__x = 'pending'; vAPI.messaging.send('loggerUI', { what: 'readAll', ownerId: 1, tabIdsToken: '' }).then(function (r) { window.__x = JSON.stringify(r.entries.map(function (e) { return JSON.parse(e); }).filter(function (e) { return /googlesyndication|googletagmanager|example.net/.test(e.url || ''); }).map(function (e) { return [e.type, (e.url || '').slice(0, 60), e.filter ? ('result=' + e.filter.result + ' ' + e.filter.raw.slice(0, 60)) : 'no filter'].join(' | '); })); }, function (e) { window.__x = 'ERR ' + e; }); 'sent'"
$request = "['https://pagead2.googlesyndication.com/pagead/gen_204?id=t', 'https://www.googletagmanager.com/gtag/js?id=G-TEST', 'https://example.net/'].forEach(function (u) { fetch(u, { mode: 'no-cors' }); }); 'started'"
$steps = @(
    @{ Type = 'Navigate'; Url = $dashboard }, @{ Type = 'Delay'; DelayMs = 2500 }, @{ Type = 'RunScript'; Script = $read }, @{ Type = 'Delay'; DelayMs = 800 },
    @{ Type = 'Navigate'; Url = 'https://example.org/' }, @{ Type = 'RunScript'; Script = $request }, @{ Type = 'Delay'; DelayMs = 4000 },
    @{ Type = 'Navigate'; Url = $dashboard }, @{ Type = 'Delay'; DelayMs = 2500 }, @{ Type = 'RunScript'; Script = $read }, @{ Type = 'Delay'; DelayMs = 1000 },
    @{ Type = 'RunScript'; Script = "'LOGGER=' + String(window.__x)" })
$result = Invoke-Plan $steps 'check' | Select-String 'LOGGER=' | Select-Object -Last 1
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
if (-not $result) { throw 'No logger answer' }
$result.Line
# the two ad hosts must show result=1 (blocked), example.net must show no filter
if ($result.Line -match 'googlesyndication[^"]*result=1' -and $result.Line -match 'googletagmanager[^"]*result=1' -and $result.Line -match 'example\.net/ \| no filter') { 'uBlock Origin blocks: ok' }
else { Write-Host 'uBlock Origin does NOT block as expected'; exit 1 }
