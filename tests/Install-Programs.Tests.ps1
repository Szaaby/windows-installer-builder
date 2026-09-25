<#
    Az Install-Programs.ps1 tesztje - felület nélkül, próbaüzemben (semmit nem telepít).
    Windows PowerShell 5.1-gyel és PowerShell 7-tel is futtatható (a CI mindkettővel futtatja):
        powershell -File tests/Install-Programs.Tests.ps1
        pwsh -File tests/Install-Programs.Tests.ps1
#>
$ErrorActionPreference = 'Stop'
$script:Failures = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if ($Condition) { Write-Host "OK    $Message" } else { Write-Host "HIBA  $Message" -ForegroundColor Red; $script:Failures++ }
}

$repo = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repo 'src/InstallerBuilder.Core/Resources/Install-Programs.ps1'
$work = Join-Path ([IO.Path]::GetTempPath()) ('ipt-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path (Join-Path $work 'telepitok') -Force | Out-Null
Copy-Item $source (Join-Path $work 'Install-Programs.ps1')
Set-Content -Path (Join-Path $work 'telepitok/02-driver.msi') -Value 'x'
Set-Content -Path (Join-Path $work 'telepitok/03-setup.exe') -Value 'x'
Set-Content -Path (Join-Path $work 'telepitok/05-beallitas.reg') -Value 'x'
$config = @'
{
  "autoStartSeconds": 30,
  "rebootWhenDone": false,
  "programs": [
    { "name": "Google Chrome", "type": "winget", "id": "Google.Chrome", "selected": true },
    { "name": "Nyomtató driver", "type": "local", "file": "telepitok/02-driver.msi", "args": "", "selected": true },
    { "name": "Intel driver", "type": "local", "file": "telepitok/03-setup.exe", "args": "-s", "selected": true },
    { "name": "VLC", "type": "winget", "id": "VideoLAN.VLC", "selected": false },
    { "name": "Registry", "type": "local", "file": "telepitok/05-beallitas.reg", "args": "", "selected": true },
    { "name": "Hiányzó", "type": "local", "file": "telepitok/99-nincs.exe", "args": "/S", "selected": true }
  ]
}
'@
[IO.File]::WriteAllText((Join-Path $work 'programok.json'), $config, (New-Object System.Text.UTF8Encoding($false)))
$log = Join-Path $work 'naplo.txt'

try {
    # 1) Teljes futás próbaüzemben.
    $psExe = (Get-Process -Id $PID).Path
    & $psExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $work 'Install-Programs.ps1') -NoGui -DryRun -LogPath $log | Out-Null
    $text = [IO.File]::ReadAllText($log, [Text.Encoding]::UTF8)

    Assert-True ($text -match 'PRÓBA: winget\.exe install --id "Google\.Chrome" --exact --source winget --silent --accept-package-agreements --accept-source-agreements') 'winget parancs'
    Assert-True ($text -match 'PRÓBA: msiexec\.exe /i ".*02-driver\.msi" /qn /norestart') 'msi parancs'
    Assert-True ($text -match 'PRÓBA: .*03-setup\.exe -s') 'exe parancs a kapcsolóval'
    Assert-True ($text -match 'PRÓBA: reg\.exe import ".*05-beallitas\.reg"') 'reg import'
    Assert-True ($text -notmatch 'VideoLAN\.VLC') 'az alapból ki nem jelölt program kimarad'
    Assert-True ($text -match 'HIBA: Hiányzó - a telepítő fájl hiányzik') 'hiányzó telepítő fájl jelzése'
    Assert-True ($text -match 'Kész – 4 sikerült, 1 nem: Hiányzó') 'összegzés'
    Assert-True (Test-Path (Join-Path $work 'telepitok/03-setup.exe')) 'próbaüzemben semmi nem törlődik'
    Assert-True (Test-Path (Join-Path $work 'kesz.txt')) 'kész-jelző fájl'

    # 2) Függvények közvetlenül (dot-source: a fő rész ilyenkor nem fut le).
    . (Join-Path $work 'Install-Programs.ps1') -NoGui -DryRun -LogPath $log -ConfigPath (Join-Path $work 'programok.json')
    Assert-True (Test-InstallSuccess 'winget' 0) 'kód 0 = siker'
    Assert-True (Test-InstallSuccess 'local' 3010) '3010 = siker, újraindítással'
    Assert-True (Test-InstallSuccess 'winget' -1978335135) 'winget: már telepítve = siker'
    Assert-True (-not (Test-InstallSuccess 'local' -1978335135)) 'a winget-kód saját telepítőnél hiba'
    Assert-True (-not (Test-InstallSuccess 'local' 1603)) '1603 = hiba'
    Assert-True ((Format-ExitCode -1978335189) -eq '-1978335189 (0x8A15002B)') 'hibakód hexában'
    $cfg = Read-ProgramConfig (Join-Path $work 'programok.json')
    Assert-True ($cfg.AutoStartSeconds -eq 30 -and $cfg.Programs.Count -eq 6) 'programlista beolvasása'
    Assert-True ($cfg.Programs[1].name -eq 'Nyomtató driver') 'ékezetes név (UTF-8)'
    $sel = Show-ProgramSelection $cfg.Programs 0
    Assert-True (($sel -join ',') -eq '0,1,2,4,5') 'alapból bejelöltek (felület nélkül)'
    $cmd = Get-InstallCommand ([pscustomobject]@{ type = 'local'; file = 'telepitok/x.cmd'; args = '/q' }) $null
    Assert-True ($cmd.FilePath -eq 'cmd.exe' -and $cmd.Arguments -match '^/c "".*x\.cmd" /q"$') 'cmd parancs idézőjelezése'
    $ps = Get-InstallCommand ([pscustomobject]@{ type = 'local'; file = 'telepitok/x.ps1'; args = '' }) $null
    Assert-True ($ps.Arguments -match '^-NoProfile -ExecutionPolicy Bypass -File ".*x\.ps1"$') 'ps1 parancs'
    $appx = Get-InstallCommand ([pscustomobject]@{ type = 'local'; file = 'telepitok/x.msixbundle'; args = '' }) $null
    Assert-True ($appx.Kind -eq 'appx') 'msix csomag'
    $w = Get-InstallCommand ([pscustomobject]@{ type = 'winget'; id = 'A.B' }) 'C:\w\winget.exe'
    Assert-True ($w.FilePath -eq 'C:\w\winget.exe' -and $w.Hidden) 'a megtalált winget útvonala'
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

if ($script:Failures -gt 0) {
    Write-Host "$($script:Failures) teszt sikertelen" -ForegroundColor Red
    exit 1
}
Write-Host 'Minden teszt sikeres.'
