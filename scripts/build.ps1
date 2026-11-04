param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $root 'release\win-x64'
dotnet publish (Join-Path $root 'GhostReader.csproj') -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $destination --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $destination '使用说明.md')
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $destination
Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $destination
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $destination -Recurse -Force
@'
@echo off
start "" "%~dp0GhostReader.exe" --settings
'@ | Set-Content -LiteralPath (Join-Path $destination '打开设置.cmd') -Encoding ascii
Write-Output "Release: $destination"
