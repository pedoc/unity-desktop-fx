param(
    [Parameter(Mandatory = $true)] [string] $EditorExecutable,
    [Parameter(Mandatory = $true)] [string] $ProjectPath,
    [Parameter(Mandatory = $true)] [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$normalizedProject = [IO.Path]::GetFullPath($ProjectPath).Replace('\', '/')
$openEditor = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
    $_.Name -eq 'Unity.exe' -and
    $_.CommandLine -and
    $_.CommandLine.Replace('\', '/').Contains($normalizedProject, [StringComparison]::OrdinalIgnoreCase) -and
    $_.CommandLine -notmatch '-batchmode'
} | Select-Object -First 1
if ($openEditor) {
    Write-Host "SKIP: Unity project is already open interactively in PID $($openEditor.ProcessId)."
    exit 0
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$results = Join-Path $OutputDirectory 'editmode-results.xml'
$log = Join-Path $OutputDirectory 'editmode.log'
$arguments = @(
    '-batchmode',
    '-nographics',
    '-accept-apiupdate',
    '-projectPath', ('"' + $ProjectPath + '"'),
    '-runTests',
    '-testPlatform', 'EditMode',
    '-testResults', ('"' + $results + '"'),
    '-logFile', ('"' + $log + '"')
)

$process = Start-Process `
    -FilePath $EditorExecutable `
    -ArgumentList $arguments `
    -WindowStyle Hidden `
    -PassThru
if (-not $process.WaitForExit(600000)) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw 'Unity EditMode test process timed out.'
}
$process.Refresh()
if ($process.ExitCode -ne 0) {
    $tail = if (Test-Path $log) { (Get-Content $log -Tail 160 -Encoding utf8) -join "`n" } else { '<no log>' }
    throw "Unity EditMode tests exited with $($process.ExitCode).`n$tail"
}
if (-not (Test-Path -LiteralPath $results)) {
    throw 'Unity EditMode test results XML was not produced.'
}

[xml]$document = Get-Content -LiteralPath $results -Raw -Encoding utf8
$run = $document.'test-run'
if ($run.result -ne 'Passed' -or [int]$run.failed -ne 0 -or [int]$run.passed -lt 1) {
    throw "Unity EditMode tests did not pass: result=$($run.result), passed=$($run.passed), failed=$($run.failed)"
}
Write-Host "Unity EditMode tests passed: $($run.passed)/$($run.total)"
