param([switch]$Verify, [switch]$Installer)
$ErrorActionPreference = 'Stop'
$taskDotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $taskDotnet) { $taskDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $taskDotnet)) { throw 'Install the .NET 8 SDK to build Burmalk.' }
$taskRoot = $PSScriptRoot
$taskPublish = Join-Path $taskRoot '.build\publish-v1.1.3'
$taskArguments=@('publish',(Join-Path $taskRoot 'src\Burmalk\Burmalk.csproj'),'-c','Release','-r','win-x64','--self-contained','true','-o',$taskPublish,"-p:RestorePackagesPath=$taskRoot\.build\packages",'-p:NuGetAudit=false')
if(Test-Path -LiteralPath (Join-Path $taskRoot '.build\offline-feed')){$taskArguments+=@('--source',(Join-Path $taskRoot '.build\offline-feed'))}
& $taskDotnet @taskArguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
if ($Verify) {
 $taskVerification = Join-Path $taskRoot '.build\verification'
 $taskProcess = Start-Process -FilePath (Join-Path $taskPublish 'Burmalk.exe') -ArgumentList "--selftest-exit --verify-dir ""$taskVerification""" -WindowStyle Hidden -PassThru
 if (-not $taskProcess.WaitForExit(55000)) { throw 'Verification timed out.' }
 $taskReport = Get-Content -LiteralPath (Join-Path $taskVerification 'verification.json') -Raw | ConvertFrom-Json
 if ($taskProcess.ExitCode -ne 0 -or $taskReport.Failures.Count -gt 0) { throw 'Verification failed.' }
}
if ($Installer) {
 $taskCompiler = Join-Path $taskRoot '.build\tools\inno\ISCC.exe'
 if (-not (Test-Path -LiteralPath $taskCompiler)) { $taskCompiler = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
 if (-not $taskCompiler) { throw 'Install Inno Setup 6.7 or newer, and add ISCC.exe to PATH.' }
 & $taskCompiler (Join-Path $taskRoot 'installer\Burmalk.iss')
 if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
 Write-Host "Share: $taskRoot\dist\Burmalk-Setup-1.1.3.exe"
}
