# MANUAL check against the JDownloader that is running on this machine: puts ONE offline test link
# (https://example.com/clickpath-test-file.txt) into the link grabber, package "Clickpath test", without starting a download.
# Delete the package in JDownloader afterwards. Not part of run.ps1 on purpose (it changes the user's JDownloader).
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$exe = Join-Path $root 'Clickpath\bin\Debug\net10.0-windows\Clickpath.exe'
$work = Join-Path $env:TEMP 'clickpath-e2e'
New-Item -ItemType Directory -Force $work | Out-Null

$probe = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
$probe.Start(); $port = $probe.LocalEndpoint.Port; $probe.Stop()
$server = Start-Process powershell -WindowStyle Hidden -PassThru -ArgumentList '-NoProfile', '-File', "`"$(Join-Path $PSScriptRoot 'serve.ps1')`"", '-Root', "`"$(Join-Path $PSScriptRoot 'site')`"", '-Port', $port, '-Seconds', 90
for ($i = 0; $i -lt 20; $i++) { try { Invoke-WebRequest "http://localhost:$port/jd.html" -UseBasicParsing -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep -Milliseconds 300 } }

$template = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'jd-send.plan.template.json'), [Text.Encoding]::UTF8)
$plan = Join-Path $work 'jd-send.json'
[IO.File]::WriteAllText($plan, $template.Replace('{SITE}', "http://localhost:$port"), (New-Object Text.UTF8Encoding($false)))
$log = Join-Path $work 'jd-log.txt'
Remove-Item $log -ErrorAction SilentlyContinue

$p = Start-Process $exe -ArgumentList "`"$plan`"", '--run', '--log', "`"$log`"" -PassThru
$finished = $p.WaitForExit(60000)
Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
if (-not $finished) { $p.Kill(); Write-Host 'TIMEOUT'; exit 1 }
Get-Content $log -Encoding UTF8
exit $p.ExitCode
