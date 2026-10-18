param([string]$Root, [int]$Port = 8765, [int]$Seconds = 120)
$l = New-Object System.Net.HttpListener
$l.Prefixes.Add("http://localhost:$Port/")
$l.Start()
$end = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $end) {
  $task = $l.GetContextAsync()
  while (-not $task.AsyncWaitHandle.WaitOne(500)) { if ((Get-Date) -ge $end) { break } }
  if (-not $task.IsCompleted) { break }
  $ctx = $task.Result
  $name = [IO.Path]::GetFileName($ctx.Request.Url.AbsolutePath)
  $file = Join-Path $Root $name
  if (Test-Path $file) {
    $bytes = [IO.File]::ReadAllBytes($file)
    if ($name -like '*.dlc') { $ctx.Response.ContentType = 'application/octet-stream'; $ctx.Response.AddHeader('Content-Disposition', "attachment; filename=$name") }
    else { $ctx.Response.ContentType = 'text/html; charset=utf-8' }
    $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
  } else { $ctx.Response.StatusCode = 404 }
  $ctx.Response.Close()
}
$l.Stop()
