# Builds the release files into dist/release:
#   Concilium-<v>-setup.exe      installer (per user, no admin), data in %APPDATA%\Concilium
#   Concilium-<v>-portable.zip   unpack anywhere and run; data stays next to the exe
#   concilium-markdown-<v>.zip   the folder template without the app
# Version comes from <Version> in windows/Concilium.csproj.
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$v = ([xml](Get-Content "$root\windows\Concilium.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$out = "$root\dist\release"
$app = "$out\Concilium"
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 is not installed" }

if (Test-Path $app) { Remove-Item $app -Recurse -Force }
& $dotnet publish "$root\windows" -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true -o $app
if ($LASTEXITCODE) { throw "publish failed" }
Copy-Item "$root\LICENSE" "$app\LICENSE.txt"
Copy-Item "$root\windows\installer\THIRD-PARTY-NOTICES.txt" "$app\THIRD-PARTY-NOTICES.txt"

& $iscc /Q "/DVersion=$v" "/DSource=$app" "$root\windows\installer\Concilium.iss"
if ($LASTEXITCODE) { throw "installer failed" }

# tar.exe writes zip paths with "/" (Compress-Archive in PowerShell 5.1 writes "\").
"Concilium keeps its data next to this program (the folder 'Concilium data')." | Set-Content "$app\portable.txt" -Encoding utf8
Push-Location $out
tar.exe -a -c -f "Concilium-$v-portable.zip" Concilium
Pop-Location
Remove-Item "$app\portable.txt"

Push-Location $root
tar.exe -a -c -f "$out\concilium-markdown-$v.zip" concilium-markdown LICENSE
Pop-Location
Get-ChildItem $out -Filter "*$v*" | Select-Object Name, @{n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
