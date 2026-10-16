# Starts the local server (hidden) if it is not running yet, waits for it, opens the app window.
# ASCII only: Windows PowerShell 5.1 reads BOM-less files in the ANSI code page.
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$port = 4747
function Up { try { (Invoke-WebRequest "http://localhost:$port/api/people" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { $false } }
if (-not (Up)) {
  Start-Process -FilePath node -ArgumentList 'server.mjs' -WorkingDirectory $here -WindowStyle Hidden
  for ($i = 0; $i -lt 160 -and -not (Up); $i++) { Start-Sleep -Milliseconds 250 }
}
# App window: Edge, else Chrome, else Yandex (all Chromium, all support --app); else the default browser.
$url = "http://localhost:$port"
$browsers = @(
  "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
  "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
  "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
  "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
  "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe",
  "$env:LOCALAPPDATA\Yandex\YandexBrowser\Application\browser.exe"
)
$exe = $browsers | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if ($exe) { Start-Process -FilePath $exe -ArgumentList "--app=$url" } else { Start-Process $url }
