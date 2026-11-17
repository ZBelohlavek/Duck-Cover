<#
.SYNOPSIS
  Runs Unity from the command line for this project, so Claude (or you) can check work without the editor.

.EXAMPLE
  ./Tools/unity.ps1 check      # compile the project; lists compiler errors, exit code 1 if any
  ./Tools/unity.ps1 capture    # play the Boot scene and save screenshots + log.txt to Screenshots/
  ./Tools/unity.ps1 scenes     # rebuild the generated scenes in Assets/DuckAndCover/Scenes

.NOTES
  Close the Unity editor first: Unity can't open a project that's already open.
  Set UNITY_PATH to Unity.exe if it isn't installed in the default Unity Hub folder.
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet("check", "capture", "scenes")][string]$Command
)

$ErrorActionPreference = "Stop"
$project = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$logs = Join-Path $project "Logs"
New-Item -ItemType Directory -Force -Path $logs | Out-Null

function Find-Unity {
    if ($env:UNITY_PATH -and (Test-Path $env:UNITY_PATH)) { return $env:UNITY_PATH }
    $versionFile = Join-Path $project "ProjectSettings/ProjectVersion.txt"
    if (Test-Path $versionFile) {
        $version = (Select-String -Path $versionFile -Pattern "m_EditorVersion:\s*(\S+)").Matches[0].Groups[1].Value
        $candidate = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
        if (Test-Path $candidate) { return $candidate }
    }
    $any = Get-ChildItem "C:\Program Files\Unity\Hub\Editor" -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($any) { return (Join-Path $any.FullName "Editor\Unity.exe") }
    throw "Unity not found. Set UNITY_PATH to the full path of Unity.exe."
}

$unity = Find-Unity
$log = Join-Path $logs "unity-$Command.log"
Write-Host "Unity: $unity"
Write-Host "Log:   $log"

switch ($Command) {
    "check" {
        $unityArgs = @("-batchmode", "-nographics", "-quit", "-projectPath", $project, "-logFile", $log)
    }
    "capture" {
        # No -nographics: screenshots need a graphics device. No -quit: the capture quits by itself.
        $out = Join-Path $project "Screenshots"
        $unityArgs = @("-batchmode", "-projectPath", $project, "-logFile", $log,
                  "-executeMethod", "DuckAndCover.EditorTools.Automation.CaptureScreenshots", "-captureDir", $out)
    }
    "scenes" {
        $unityArgs = @("-batchmode", "-nographics", "-projectPath", $project, "-logFile", $log,
                  "-executeMethod", "DuckAndCover.EditorTools.Automation.CreateScenes")
    }
}

# Unity.exe is a windowed app, so start it with -Wait (the call operator wouldn't wait),
# quoting any argument that contains spaces.
$argLine = ($unityArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
$process = Start-Process -FilePath $unity -ArgumentList $argLine -Wait -PassThru -NoNewWindow
$code = $process.ExitCode

$errors = @()
if (Test-Path $log) {
    $errors = Select-String -Path $log -Pattern "error CS\d+" | Select-Object -ExpandProperty Line -Unique
} else {
    Write-Host "Unity didn't write a log. Is the project already open in the editor?" -ForegroundColor Yellow
    if ($code -eq 0) { $code = 1 }
}
if ($errors) {
    Write-Host "`nCompiler errors:" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  $_" }
    if ($code -eq 0) { $code = 1 }
}
if ($Command -eq "capture") {
    $report = Join-Path $project "Screenshots/log.txt"
    if (Test-Path $report) { Write-Host ""; Get-Content $report }
}
Write-Host "`nUnity exited with code $code"
exit $code
