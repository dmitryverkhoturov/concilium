# The remember skill runs `sleep`, the general memory skill, kept here unchanged.
# Copies the maintained version of sleep into the template when it differs.
param([string]$From = "$env:USERPROFILE\.claude\skills\sleep\SKILL.md")
$to = Join-Path $PSScriptRoot "..\concilium-markdown\.claude\skills\sleep\SKILL.md"
if (-not (Test-Path $From)) { Write-Error "Not found: $From"; exit 1 }
if ((Get-FileHash $From).Hash -eq (Get-FileHash $to).Hash) { "sleep is up to date"; exit 0 }
Copy-Item $From $to -Force
"sleep updated from $From"
