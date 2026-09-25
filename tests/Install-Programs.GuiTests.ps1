<#
    A telepítéskor megjelenő ablakok tesztje - csak Windowson (a CI futtatja). A programválasztó a visszaszámlálással
    magától bezárul, a folyamat-ablakot a teszt zárja be.
#>
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) ('ipg-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work -Force | Out-Null
Copy-Item (Join-Path $repo 'src/InstallerBuilder.Core/Resources/Install-Programs.ps1') (Join-Path $work 'Install-Programs.ps1')
$log = Join-Path $work 'naplo.txt'
try {
    . (Join-Path $work 'Install-Programs.ps1') -DryRun -LogPath $log
    $programs = @(
        [pscustomobject]@{ name = 'Google Chrome'; type = 'winget'; id = 'Google.Chrome'; selected = $true },
        [pscustomobject]@{ name = 'VLC'; type = 'winget'; id = 'VideoLAN.VLC'; selected = $false },
        [pscustomobject]@{ name = 'Nyomtató driver'; type = 'local'; file = 'telepitok/a.msi'; args = ''; selected = $true }
    )
    $picked = Show-ProgramSelectionForm $programs 2
    if (($picked -join ',') -ne '0,2') { throw "A választó rossz eredményt adott: '$($picked -join ',')'" }
    Write-Host 'OK    programválasztó (visszaszámlálás után magától indult, az alapból bejelöltekkel)'

    New-ProgressUiForm $programs
    Set-UiStatus 'Teszt…'
    Set-UiItem 0 '✓  Google Chrome   – 0:01' 1
    Set-UiItem 1 '✗  VLC   – hibakód 1603' 2
    Set-UiInfo 'Internet: csatlakozva'
    $script:Ui.Done = $true
    $script:Ui.Form.Close()
    $script:Ui.Form.Dispose()
    $script:Ui = $null
    Write-Host 'OK    folyamat-ablak'
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
Write-Host 'Minden felület-teszt sikeres.'
