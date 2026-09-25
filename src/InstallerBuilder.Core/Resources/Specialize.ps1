<#
    Windows Telepítő Készítő - a Windows telepítés "specialize" fázisában fut (autounattend.xml RunSynchronous),
    RENDSZER (SYSTEM) jogokkal, még a kezdeti kérdések (OOBE) előtt.

    Létrehozza a helyi felhasználót a felhasznalo.json alapján, és a rendszergazdák közé teszi - a csoportot a
    nyelvtől független SID-jével (S-1-5-32-544) adja meg, mert a neve magyar Windowson "Rendszergazdák", angolon
    "Administrators". A felhasznalo.json-t (benne a jelszóval) utána azonnal törli.
#>
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$log = Join-Path $dir 'specialize-naplo.txt'
$userFile = Join-Path $dir 'felhasznalo.json'

function Write-Log([string]$Message) {
    try { Add-Content -LiteralPath $log -Value ('[{0}] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message) -Encoding UTF8 } catch { }
}

try {
    if (-not (Test-Path -LiteralPath $userFile)) {
        Write-Log 'Nincs felhasznalo.json - nincs teendő.'
        return
    }
    $u = Get-Content -LiteralPath $userFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $name = [string]$u.name
    $existing = Get-LocalUser -Name $name -ErrorAction SilentlyContinue
    if (-not $existing) {
        $params = @{ Name = $name; AccountNeverExpires = $true }
        if ($u.fullName) { $params.FullName = [string]$u.fullName }
        if ([string]$u.password) {
            $params.Password = ConvertTo-SecureString -String ([string]$u.password) -AsPlainText -Force
            $params.PasswordNeverExpires = $true
        }
        else {
            $params.NoPassword = $true
        }
        New-LocalUser @params | Out-Null
        Write-Log ('Felhasználó létrehozva: {0}' -f $name)
    }
    else {
        Write-Log ('A felhasználó már létezik: {0}' -f $name)
    }
    try {
        Add-LocalGroupMember -SID 'S-1-5-32-544' -Member $name -ErrorAction Stop
        Write-Log 'Rendszergazdák csoportba téve.'
    }
    catch {
        # Már tag - nem hiba.
        Write-Log ('Csoport: {0}' -f $_.Exception.Message)
    }
    if (-not [string]$u.password) {
        # Jelszó nélküli fióknál a "jelszó soha nem jár le" nem értelmezhető, de a lejáró jelszó kérését kikapcsoljuk.
        Set-LocalUser -Name $name -PasswordNeverExpires $true -ErrorAction SilentlyContinue
    }
}
catch {
    Write-Log ('HIBA: {0}' -f $_)
}
finally {
    Remove-Item -LiteralPath $userFile -Force -ErrorAction SilentlyContinue
}
