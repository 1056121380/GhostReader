$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $root 'artifacts\verification'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$oldProfile = $env:GHOSTREADER_PROFILE
$oldArtifacts = $env:GHOSTREADER_TEST_ARTIFACTS
try {
    $env:GHOSTREADER_TEST_ARTIFACTS = $evidence
    dotnet build (Join-Path $root 'Tests\GhostReader.Tests.csproj') -c Release --nologo 2>&1 | Tee-Object (Join-Path $evidence 'tests-build-final.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
    dotnet --roll-forward Major (Join-Path $root 'Tests\bin\Release\net8.0-windows\GhostReader.Tests.dll') 2>&1 | Tee-Object (Join-Path $evidence 'tests-final.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed' }
    # Reserve unusual temporary test hotkeys; do not overwrite the user's real configuration.
    $profile = Join-Path $evidence 'selftest-profile'
    New-Item -ItemType Directory -Force -Path $profile | Out-Null
    $env:GHOSTREADER_PROFILE = $profile
    @{ HotkeyToggle='Ctrl+Alt+Shift+F13'; HotkeyNext='Ctrl+Alt+Shift+F14'; HotkeyPrev='Ctrl+Alt+Shift+F15'; HotkeyExit='Ctrl+Alt+Shift+F16'; HotkeySettings='Ctrl+Alt+Shift+F17'; HotkeyThrough='Ctrl+Alt+Shift+F18'; HotkeyAutoScroll='Ctrl+Alt+Shift+F19' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profile 'settings.json') -Encoding utf8
    $exe = Join-Path $root 'release\win-x64\GhostReader.exe'
    if (!(Test-Path -LiteralPath $exe)) { throw 'Run scripts/build.ps1 first' }
    $process = Start-Process -FilePath $exe -ArgumentList '--selftest' -PassThru -Wait -WindowStyle Hidden
    $report = Join-Path $root 'release\win-x64\selftest-report.txt'
    Copy-Item -LiteralPath $report -Destination (Join-Path $evidence 'selftest-release.txt') -Force
    Get-Content -LiteralPath $report -Encoding utf8
    if ($process.ExitCode -ne 0) { throw "Published selftest failed: $($process.ExitCode)" }
    "PublishedSelftestExit=$($process.ExitCode)" | Set-Content -LiteralPath (Join-Path $evidence 'release-exit.txt')
} finally {
    $env:GHOSTREADER_PROFILE = $oldProfile
    $env:GHOSTREADER_TEST_ARTIFACTS = $oldArtifacts
}
