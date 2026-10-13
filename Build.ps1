param([switch]$Verify, [switch]$Installer, [switch]$PerformanceVerify)
$ErrorActionPreference = 'Stop'
$taskDotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $taskDotnet) { $taskDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $taskDotnet)) { throw 'Install the .NET 8 SDK to build Burmalk.' }
$taskRoot = $PSScriptRoot
$taskVersion = ([xml](Get-Content -LiteralPath (Join-Path $taskRoot 'src\Burmalk\Burmalk.csproj') -Raw)).Project.PropertyGroup.Version
$taskPublish = Join-Path $taskRoot ('.build\publish-v' + $taskVersion)
$taskArguments=@('publish',(Join-Path $taskRoot 'src\Burmalk\Burmalk.csproj'),'-c','Release','-r','win-x64','--self-contained','true','-o',$taskPublish,"-p:RestorePackagesPath=$taskRoot\.build\packages",'-p:NuGetAudit=false')
if(Test-Path -LiteralPath (Join-Path $taskRoot '.build\offline-feed')){$taskArguments+=@('--source',(Join-Path $taskRoot '.build\offline-feed'))}
& $taskDotnet @taskArguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
if ($Verify -or $PerformanceVerify) {
 $taskVerification = Join-Path $taskRoot ('.build\verification-publish-' + $taskVersion)
 $taskVerifyArgs = "--selftest-exit --profile-experimental --verify-dir ""$taskVerification"""
 if ($PerformanceVerify) { $taskVerifyArgs += ' --verify-performance' }
 $taskProcess = Start-Process -FilePath (Join-Path $taskPublish 'Burmalk.exe') -ArgumentList $taskVerifyArgs -WindowStyle Hidden -PassThru
 $taskTimeout = if ($PerformanceVerify) { 300000 } else { 80000 }
 if (-not $taskProcess.WaitForExit($taskTimeout)) { throw 'Verification timed out.' }
 $taskReport = Get-Content -LiteralPath (Join-Path $taskVerification 'verification.json') -Raw | ConvertFrom-Json
 if ($taskProcess.ExitCode -ne 0 -or $taskReport.Failures.Count -gt 0) { throw 'Verification failed.' }
}
if ($Installer) {
 $taskCompiler = Join-Path $taskRoot '.build\tools\inno\ISCC.exe'
 if (-not (Test-Path -LiteralPath $taskCompiler)) { $taskCompiler = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
 if (-not $taskCompiler) { throw 'Install Inno Setup 6.7 or newer, and add ISCC.exe to PATH.' }
 & $taskCompiler "/DAppVersion=$taskVersion" "/DPublishDir=$taskPublish" (Join-Path $taskRoot 'installer\Burmalk.iss')
 if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
 Write-Host "Share: $taskRoot\dist\Burmalk-Setup-$taskVersion.exe"
}
