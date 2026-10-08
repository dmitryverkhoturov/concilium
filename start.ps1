# Starts the local server (hidden) if it is not running yet, waits for it, opens the app window.
# ASCII only: Windows PowerShell 5.1 reads BOM-less files in the ANSI code page.
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$port = 4747
function Up { try { (Invoke-WebRequest "http://localhost:$port/api/people" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { $false } }
if (-not (Up)) {
  Start-Process -FilePath node -ArgumentList 'server.mjs' -WorkingDirectory $here -WindowStyle Hidden
  for ($i = 0; $i -lt 160 -and -not (Up); $i++) { Start-Sleep -Milliseconds 250 }
}
Start-Process msedge -ArgumentList "--app=http://localhost:$port"
