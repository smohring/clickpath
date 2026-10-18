# Headless end-to-end check: replays plan.template.json against the local test site and verifies the result.
# Expects a Debug build (dotnet build Clickpath.slnx) or -Exe pointing at another build. Exit code 0 = all good.
param([string]$Exe)
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$exe = if ($Exe) { $Exe } else { Join-Path $root 'Clickpath\bin\Debug\net10.0-windows\Clickpath.exe' }
$work = Join-Path $env:TEMP 'clickpath-e2e'
New-Item -ItemType Directory -Force $work | Out-Null

# Serve the test site over HTTP: WebView2 does not start downloads from file:// pages.
$probe = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
$probe.Start(); $port = $probe.LocalEndpoint.Port; $probe.Stop()
$server = Start-Process powershell -WindowStyle Hidden -PassThru -ArgumentList '-NoProfile', '-File', "`"$(Join-Path $PSScriptRoot 'serve.ps1')`"", '-Root', "`"$(Join-Path $PSScriptRoot 'site')`"", '-Port', $port, '-Seconds', 240
for ($i = 0; $i -lt 20; $i++) { try { Invoke-WebRequest "http://localhost:$port/index.html" -UseBasicParsing -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep -Milliseconds 300 } }
$site = "http://localhost:$port"
$template = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'plan.template.json'), [Text.Encoding]::UTF8)
$plan = Join-Path $work 'plan.json'
[IO.File]::WriteAllText($plan, $template.Replace('{SITE}', $site), (New-Object Text.UTF8Encoding($false)))

$log = Join-Path $work 'log.txt'
$csv = Join-Path $work 'results.csv'
Remove-Item $log, $csv -ErrorAction SilentlyContinue

$appLog = Join-Path $env:LOCALAPPDATA 'Clickpath\app.log'
$appLogBefore = if (Test-Path $appLog) { @(Get-Content $appLog -Encoding UTF8).Count } else { 0 }

$p = Start-Process $exe -ArgumentList "`"$plan`"", '--run', '--log', "`"$log`"", '--results', "`"$csv`"" -PassThru
$finished = $p.WaitForExit(180000)
Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
if (-not $finished) { $p.Kill(); Write-Host 'TIMEOUT'; exit 1 }

# lines the app itself wrote during this run (downloads of the sample container end up in the Downloads folder)
$appLines = if (Test-Path $appLog) { @(Get-Content $appLog -Encoding UTF8 | Select-Object -Skip $appLogBefore) } else { @() }
$appLines | ForEach-Object { if ($_ -match 'download started: .* -> (.*lf-e2e-sample.*\.dlc)$') { Remove-Item $Matches[1] -ErrorAction SilentlyContinue } }

$rows = if (Test-Path $csv) { @(Import-Csv $csv) } else { @() }
$lines = if (Test-Path $log) { @(Get-Content $log -Encoding UTF8) } else { @() }
$count = { param($pattern) @($lines | Where-Object { $_ -match $pattern }).Count }
# the texts a loop's child step printed, in order: lines "-> <prefix>: <text>"
$printed = { param($prefix) (@($lines | Where-Object { $_ -match "(^|\s)${prefix}: " } | ForEach-Object { ($_ -split "${prefix}: ", 2)[1] })) -join '|' }
$pagesAll = 'Entry F (featured)|Entry 1.1|Entry 1.2|Entry 1.3|Entry 2.1|Entry 2.2|Entry 2.3|Entry 3.1|Entry 3.2|Entry 3.3'
# how many windows each "close all" step closed (the first one's number depends on what earlier steps left open)
$closedCounts = @($lines | ForEach-Object { if ($_ -match 'closed (\d+) window\(s\); continuing on a blank tab') { [int]$Matches[1] } })

# Result lines of RunScript steps end without a quote (the step's own summary line ends with one).
# expected: 6 extracted links from the loop plus 1 from the page-click tab; if/not branches run once per item, the wrong branch never; {text} expands per item
$checks = [ordered]@{
    'exit code 0'          = ($p.ExitCode -eq 0)
    '7 results'            = ($rows.Count -eq 7)
    'page click opened tab' = (@($rows | Where-Object { $_.Text -eq 'Final link 9 (body)' }).Count -eq 1)
    'new window closed x1'  = ((& $count 'a new window opened and was closed; staying on this tab') -eq 1)
    'new window left open x4' = ((& $count 'a new window opened; staying on this tab') -eq 4)
    'stayed on the page x2' = ((& $count 'Any click$') -eq 2)
    'if-branch x3'         = ((& $count 'if-branch$') -eq 3)
    'not-branch x3'        = ((& $count 'not-branch$') -eq 3)
    'wrong-branch never'   = ((& $count 'wrong-branch$') -eq 0)
    'variable {text} x3'   = ((& $count 'var-check: Item [123]$') -eq 3)
    'handed over x6'       = ((& $count 'handed over \(other app or download\): ms-settings:') -eq 6)
    'container download x3' = ((& $count 'handed over \(other app or download\): lf-e2e-sample') -eq 3)
    'self-closing popup survived' = ((& $count 'after-selfclose$') -eq 1)
    'repeat: ad closed once' = ((& $count 'click 1 opened a window \(closed\); clicking again') -eq 1)
    'repeat: stops after 2'  = ((& $count 'no window opened after click 2: done') -eq 1)
    'repeat: real action'    = ((& $count 'Real action$') -eq 1)
    'plain click jumps'      = (@($lines | Where-Object { $_ -match 'jump-moves:[0-3]$' }).Count -eq 1)
    'human click walks'      = (@($lines | Where-Object { $_ -match 'walk-moves:(\d+)$' -and [int]$Matches[1] -ge 8 }).Count -eq 1)
    'move ends on new page'  = ((& $count 'a new page started loading: continuing with the next step') -eq 1)
    'pause ends on new window' = ((& $count 'a new window opened: continuing with the next step') -eq 1)
    'steps after early end'  = (((& $count 'after-newpage$') -eq 1) -and ((& $count 'after-newwindow$') -eq 1) -and ((& $count 'after-swap$') -eq 1))
    'move ends on content swap' = ((& $count 'the page content was replaced: continuing with the next step') -eq 1)
    'random clicks hit only empty page' = (@($lines | Where-Object { $_ -match 'clicks:(\d+);bad:0$' -and [int]$Matches[1] -ge 2 }).Count -eq 1)
    'pages: all 3 pages, featured once' = ((& $printed 'all') -eq $pagesAll)
    'pages: stops at max pages'         = ((& $printed 'max') -eq 'Entry F (featured)|Entry 1.1|Entry 1.2|Entry 1.3|Entry 2.1|Entry 2.2|Entry 2.3')
    'pages: range across pages'         = ((& $printed 'range') -eq 'Entry 2.2|Entry 2.3|Entry 3.1')
    'pages: button without address'     = ((& $printed 'spa') -eq 'Spa 1.1|Spa 1.2|Spa 1.3|Spa 2.1|Spa 2.2|Spa 2.3')
    'near: picks the row by its logo'   = ((& $printed 'near') -eq 'picked:dd')
    'loop: clicks until the condition holds' = (((& $printed 'loop1') -eq 'picked:dd') -and ((& $count 'left the loop') -eq 2) -and ((& $count 'after-loop$') -eq 1))   # two plan steps leave a loop: this one and the "page found stays open" one
    'loop: no match, runs through'      = (((& $printed 'idx') -eq '1|2') -and ((& $count 'loop2: wrong') -eq 0) -and ((& $count 'after-nodd$') -eq 1))
    'loop: the page found stays open'   = (((& $printed 'kept') -eq '?n=77') -and ((& $printed 'back') -eq '?order=kat,dd,rg&open=1'))
    'until: stops when the text appears' = (((& $printed 'round') -eq '1|2') -and ((& $printed 'until') -eq 'picked:dd') -and ((& $count 'appeared: leaving the loop') -eq 1))
    'until: runs on when it never does'  = ((& $printed 'untilnone') -eq '1|2')
    'link list: three links in order'   = ((& $printed 'listed') -eq '?n=71|?n=72|?n=73')
    'link list: bad line reported'      = ((& $count 'ignored \(not a web address\): not a link') -eq 1)
    'close all in a loop keeps the list' = (((& $printed 'keep') -eq $pagesAll) -and ((& $count 'kept 1 page\(s\) the loop still needs') -eq 10) -and ((& $count 'keepend: List page 3$') -eq 1))
    'close all: 3 windows'   = (($closedCounts.Count -eq 2) -and ($closedCounts[-1] -eq 3))
    'close all: blank start' = ((& $count '^\s*\S+ about:blank$') -eq 1)
    'container not opened x3' = (@($appLines | Where-Object { $_ -match 'container file saved, not opened' }).Count -eq 3)
}
$checks.GetEnumerator() | ForEach-Object { Write-Host ("{0,-22} {1}" -f $_.Key, $(if ($_.Value) { 'ok' } else { 'FAILED' })) }
if ($checks.Values -contains $false) { $lines | Select-Object -Last 15; exit 1 }
