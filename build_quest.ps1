# Builds CrystalLeather.Quest.dll, the Quest version of the mod, from the same source files.
#   .\build_quest.ps1                              uses ..\ATT Quest Dlls\questdlls
#   .\build_quest.ps1 -QuestDlls "D:\quest\questdlls"
# The Quest reference DLLs come from https://github.com/TheRavenSeb/ATT-Quest-Modding-dlls (the questdlls folder).
[CmdletBinding()]
param([string]$QuestDlls = '')
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $QuestDlls) { $QuestDlls = Join-Path (Split-Path -Parent $here) 'ATT Quest Dlls\questdlls' }
if (-not (Test-Path (Join-Path $QuestDlls 'Il2CppRoot.Township.dll'))) { throw "Can't find the Quest DLLs at '$QuestDlls'. Pass -QuestDlls with the folder that holds Il2CppRoot.Township.dll." }

& dotnet build (Join-Path $here 'Quest\CrystalLeather.Quest.csproj') -c Release -nologo -v q "-p:QuestDlls=$QuestDlls" "-p:OutDir=$(Join-Path $here 'Quest\bin\')"
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
Copy-Item (Join-Path $here 'Quest\bin\CrystalLeather.Quest.dll') (Join-Path $here 'CrystalLeather.Quest.dll') -Force
Write-Host "Built $(Join-Path $here 'CrystalLeather.Quest.dll')"
