param()
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
$taskGh=(Get-Command gh.exe -ErrorAction SilentlyContinue).Source
if(-not $taskGh){$taskGh=Join-Path $taskRoot '.build\tools\gh\bin\gh.exe'}
if(-not(Test-Path -LiteralPath $taskGh)){throw 'Install GitHub CLI.'}
$taskLogin=& $taskGh api user --jq .login
if($LASTEXITCODE -ne 0){throw 'Authorize GitHub CLI first: gh auth login --web --hostname github.com'}
if($taskLogin.Trim() -ne 'vetallitle1231-sketch'){throw 'Use the authorized project owner account: vetallitle1231-sketch.'}
$taskRepo='vetallitle1231-sketch/Burmalk'
$taskInstaller=Join-Path $taskRoot 'dist\Burmalk-Setup-1.1.3.exe'
if(-not(Test-Path -LiteralPath $taskInstaller)){throw 'Build the release installer first.'}
if(git status --porcelain){throw 'Commit or review local changes before publishing.'}
& $taskGh repo view $taskRepo --json name 2>$null
if($LASTEXITCODE -ne 0){
 & $taskGh repo create $taskRepo --public --source $taskRoot --remote origin --push --description 'Burmalk: Windows PC monitoring, customizable themes, transparent gaming overlay and CPU/memory/disk benchmark.'
 if($LASTEXITCODE -ne 0){throw 'Repository creation failed.'}
}else{
 $taskRemote=git remote get-url origin 2>$null
 if(-not $taskRemote){git remote add origin "https://github.com/$taskRepo.git"}
 elseif($taskRemote -notmatch '^https://github\.com/vetallitle1231-sketch/Burmalk(\.git)?$'){throw 'Unexpected origin; review it before publishing.'}
 git push -u origin main
 if($LASTEXITCODE -ne 0){throw 'Push failed; no force-push is performed.'}
}
& $taskGh repo edit $taskRepo --enable-issues --enable-wiki=false --add-topic windows --add-topic wpf --add-topic monitoring --add-topic gaming-overlay
if($LASTEXITCODE -ne 0){throw 'Repository settings failed.'}
& $taskGh release create v1.1.3 $taskInstaller --repo $taskRepo --target main --title 'Burmalk 1.1.3' --notes-file (Join-Path $taskRoot 'docs\RELEASE-NOTES-1.1.3.md')
if($LASTEXITCODE -ne 0){throw 'Release creation failed; existing releases are not overwritten.'}
Write-Host "Published: https://github.com/$taskRepo"
