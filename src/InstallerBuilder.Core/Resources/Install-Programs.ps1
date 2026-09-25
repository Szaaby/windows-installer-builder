<#
.SYNOPSIS
    Programok telepítése a Windows első bejelentkezésekor (Windows Telepítő Készítő).
.DESCRIPTION
    Az autounattend.xml FirstLogonCommands része indítja. Megmutatja a programlistát (pipálható), majd feltelepíti a
    kiválasztottakat: a winget-es programokat az internetről, a sajátokat a telepítőre másolt fájlokból.
    Kézzel is újraindítható: Programok-telepitese.cmd (ugyanebben a mappában).

    Windows PowerShell 5.1-re írva (egy friss Windowson ez van): nincs benne ?? / ?: / -Parallel.
.PARAMETER NoGui
    Tesztekhez: választólista és ablakok nélkül, az alapból bejelölt programokkal.
.PARAMETER DryRun
    Tesztekhez: semmit nem telepít, csak naplózza, mit futtatna.
#>
[CmdletBinding()]
param(
    [string]$ConfigPath,
    [string]$LogPath,
    [switch]$NoGui,
    [switch]$DryRun
)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
$script:ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ConfigPath) { $ConfigPath = Join-Path $script:ScriptDir 'programok.json' }
if (-not $LogPath) { $LogPath = Join-Path $script:ScriptDir 'telepites-naplo.txt' }
$script:OnWindows = ($env:OS -eq 'Windows_NT')
$script:Ui = $null

# A winget "hibakódjai", amik valójában rendben vannak: 0x8A15002B = nincs frissebb verzió, 0x8A150061 = már telepítve.
$script:WingetOkCodes = @(-1978335189, -1978335135)
# Sikeres, de újraindítást kér (MSI / a legtöbb telepítő).
$script:RebootCodes = @(3010, 1641)
# Egy telepítő legfeljebb ennyi ideig futhat.
$script:InstallTimeoutMinutes = 45

function Write-Log {
    param([string]$Message)
    $line = '[{0}] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    try { Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8 } catch { }
    if ($NoGui) { Write-Host $line }
}

function Get-Prop {
    param($Object, [string]$Name, $Default)
    if ($null -ne $Object -and $Object.PSObject.Properties[$Name]) { return $Object.$Name }
    return $Default
}

function Read-ProgramConfig {
    param([string]$Path)
    $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $cfg = $raw | ConvertFrom-Json
    $programs = @()
    foreach ($p in @(Get-Prop $cfg 'programs' @())) {
        if ($null -ne $p) { $programs += $p }
    }
    return [pscustomobject]@{
        AutoStartSeconds = [int](Get-Prop $cfg 'autoStartSeconds' 0)
        RebootWhenDone   = [bool](Get-Prop $cfg 'rebootWhenDone' $false)
        Programs         = $programs
    }
}

function Resolve-InstallerPath {
    param([string]$RelativePath)
    $sep = [IO.Path]::DirectorySeparatorChar
    return [IO.Path]::Combine($script:ScriptDir, ($RelativePath -replace '[\\/]', $sep))
}

# Mit kell futtatni egy programhoz. Kind: winget | local | appx. SourceFile: a saját telepítő fájlja (létezés-ellenőrzéshez).
function Get-InstallCommand {
    param($Program, [string]$WingetPath)
    $type = [string](Get-Prop $Program 'type' 'winget')
    if ($type -eq 'winget') {
        $exe = 'winget.exe'
        if ($WingetPath) { $exe = $WingetPath }
        return [pscustomobject]@{
            Kind       = 'winget'
            FilePath   = $exe
            Arguments  = ('install --id "{0}" --exact --source winget --silent --accept-package-agreements --accept-source-agreements' -f (Get-Prop $Program 'id' ''))
            SourceFile = $null
            Hidden     = $true
        }
    }
    $file = Resolve-InstallerPath ([string](Get-Prop $Program 'file' ''))
    $extra = ([string](Get-Prop $Program 'args' '')).Trim()
    $ext = [IO.Path]::GetExtension($file).ToLowerInvariant()
    $kind = 'local'
    $hidden = $false
    switch ($ext) {
        '.msi' {
            $exe = 'msiexec.exe'
            $arguments = ('/i "{0}" /qn /norestart {1}' -f $file, $extra).Trim()
            $hidden = $true
        }
        { $_ -in @('.msix', '.msixbundle', '.appx', '.appxbundle') } {
            $kind = 'appx'
            $exe = $file
            $arguments = ''
        }
        '.reg' {
            $exe = 'reg.exe'
            $arguments = ('import "{0}"' -f $file)
            $hidden = $true
        }
        { $_ -in @('.cmd', '.bat') } {
            $exe = 'cmd.exe'
            $arguments = ('/c ""{0}" {1}"' -f $file, $extra)
        }
        '.ps1' {
            $exe = 'powershell.exe'
            $arguments = ('-NoProfile -ExecutionPolicy Bypass -File "{0}" {1}' -f $file, $extra).Trim()
        }
        default {
            $exe = $file
            $arguments = $extra
        }
    }
    return [pscustomobject]@{
        Kind       = $kind
        FilePath   = $exe
        Arguments  = $arguments
        SourceFile = $file
        Hidden     = $hidden
    }
}

function Test-InstallSuccess {
    param([string]$Kind, [int]$ExitCode)
    if ($ExitCode -eq 0) { return $true }
    if ($script:RebootCodes -contains $ExitCode) { return $true }
    if ($Kind -eq 'winget' -and ($script:WingetOkCodes -contains $ExitCode)) { return $true }
    return $false
}

function Format-ExitCode {
    param([int]$Code)
    return ('{0} (0x{1})' -f $Code, $Code.ToString('X8'))
}

# ---------------------------------------------------------------- felület

# A felület-függvények külön vannak: a PowerShell egy függvény típusneveit ([System.Windows.Forms....]) a függvény
# első futásakor oldja fel - ha egy közös függvényben lennének, felület nélküli módban is betöltené a WinForms-ot.
$script:DoEvents = $null
function Update-Ui {
    if ($script:Ui -and $script:DoEvents) { & $script:DoEvents }
}

function Wait-Ui {
    param([double]$Seconds)
    $until = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $until) {
        Update-Ui
        Start-Sleep -Milliseconds 100
    }
}

function Initialize-WinForms {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()
    $script:DoEvents = { [System.Windows.Forms.Application]::DoEvents() }
}

function New-UiButton {
    param([string]$Text, [int]$X, [int]$Y, [int]$Width, [bool]$Primary = $false)
    $b = New-Object System.Windows.Forms.Button
    $b.Text = $Text
    $b.Location = New-Object System.Drawing.Point($X, $Y)
    $b.Size = New-Object System.Drawing.Size($Width, 34)
    if ($Primary) {
        $b.FlatStyle = 'Flat'
        $b.FlatAppearance.BorderSize = 0
        $b.BackColor = [System.Drawing.Color]::FromArgb(15, 108, 189)
        $b.ForeColor = [System.Drawing.Color]::White
        $b.Font = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)
    }
    return $b
}

# A választólista. Visszaad: a bejelölt programok indexei (tömb), vagy $null, ha a felhasználó a "Kihagyás"-t választotta.
function Show-ProgramSelection {
    param($Programs, [int]$AutoStartSeconds)
    if ($NoGui) {
        $picked = @()
        for ($i = 0; $i -lt $Programs.Count; $i++) {
            if ([bool](Get-Prop $Programs[$i] 'selected' $true)) { $picked += $i }
        }
        return ,$picked
    }
    return Show-ProgramSelectionForm $Programs $AutoStartSeconds
}

function Show-ProgramSelectionForm {
    param($Programs, [int]$AutoStartSeconds)
    Initialize-WinForms
    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'Programok telepítése'
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.TopMost = $true
    $form.Font = New-Object System.Drawing.Font('Segoe UI', 10)
    $form.ClientSize = New-Object System.Drawing.Size(560, 540)
    $form.BackColor = [System.Drawing.Color]::White

    $title = New-Object System.Windows.Forms.Label
    $title.Text = 'Melyik programok települjenek?'
    $title.Font = New-Object System.Drawing.Font('Segoe UI', 14, [System.Drawing.FontStyle]::Bold)
    $title.Location = New-Object System.Drawing.Point(20, 16)
    $title.Size = New-Object System.Drawing.Size(520, 32)
    $form.Controls.Add($title)

    $info = New-Object System.Windows.Forms.Label
    $info.Text = 'A telepítés a háttérben fut, közben már használhatod a gépet. Ami most kimarad, később a Programok-telepitese.cmd fájllal pótolható.'
    $info.ForeColor = [System.Drawing.Color]::FromArgb(66, 66, 66)
    $info.Location = New-Object System.Drawing.Point(20, 52)
    $info.Size = New-Object System.Drawing.Size(520, 44)
    $form.Controls.Add($info)

    $list = New-Object System.Windows.Forms.CheckedListBox
    $list.CheckOnClick = $true
    $list.IntegralHeight = $false
    $list.Location = New-Object System.Drawing.Point(20, 100)
    $list.Size = New-Object System.Drawing.Size(520, 310)
    foreach ($p in $Programs) {
        $source = 'internetről'
        if ((Get-Prop $p 'type' 'winget') -ne 'winget') { $source = 'a telepítőről' }
        $index = $list.Items.Add(('{0}   ({1})' -f (Get-Prop $p 'name' '?'), $source))
        $list.SetItemChecked($index, [bool](Get-Prop $p 'selected' $true))
    }
    $form.Controls.Add($list)

    $countdown = New-Object System.Windows.Forms.Label
    $countdown.Location = New-Object System.Drawing.Point(20, 420)
    $countdown.Size = New-Object System.Drawing.Size(520, 40)
    $countdown.Padding = New-Object System.Windows.Forms.Padding(8, 4, 8, 4)
    $countdown.ForeColor = [System.Drawing.Color]::FromArgb(107, 61, 0)
    $countdown.BackColor = [System.Drawing.Color]::FromArgb(253, 241, 227)
    $countdown.Visible = ($AutoStartSeconds -gt 0)
    $form.Controls.Add($countdown)

    $btnAll = New-UiButton 'Mind' 20 480 80
    $btnNone = New-UiButton 'Egyik sem' 108 480 100
    $btnSkip = New-UiButton 'Kihagyás' 320 480 100
    $btnInstall = New-UiButton 'Telepítés' 428 480 112 $true
    $btnSkip.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $btnInstall.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $form.Controls.AddRange(@($btnAll, $btnNone, $btnSkip, $btnInstall))
    $form.AcceptButton = $btnInstall
    $form.CancelButton = $btnSkip

    # Az eseménykezelők a script: változókon osztoznak (a PowerShell szkriptblokkok nem zárnak be helyi változót).
    $script:SelList = $list
    $script:SelInstall = $btnInstall
    $script:SelCountdown = $countdown
    $script:SelRemaining = $AutoStartSeconds
    $script:SelForm = $form

    $btnInstall.Text = ('Telepítés ({0})' -f $list.CheckedItems.Count)
    $list.add_ItemCheck({
        param($sender, $e)
        $delta = 0
        if ($e.NewValue -eq [System.Windows.Forms.CheckState]::Checked -and $e.CurrentValue -ne [System.Windows.Forms.CheckState]::Checked) { $delta = 1 }
        if ($e.NewValue -ne [System.Windows.Forms.CheckState]::Checked -and $e.CurrentValue -eq [System.Windows.Forms.CheckState]::Checked) { $delta = -1 }
        $script:SelInstall.Text = ('Telepítés ({0})' -f ($script:SelList.CheckedItems.Count + $delta))
    })

    $timer = New-Object System.Windows.Forms.Timer
    $timer.Interval = 1000
    $script:SelTimer = $timer
    $script:SelStop = {
        if ($script:SelTimer.Enabled) {
            $script:SelTimer.Stop()
            $script:SelCountdown.Text = 'Visszaszámlálás megállítva – válassz, majd kattints a Telepítés gombra.'
        }
    }
    if ($AutoStartSeconds -gt 0) {
        $countdown.Text = ('A telepítés {0} mp múlva magától indul – egy kattintás megállítja a visszaszámlálást.' -f $AutoStartSeconds)
        $timer.add_Tick({
            $script:SelRemaining--
            if ($script:SelRemaining -le 0) {
                $script:SelTimer.Stop()
                $script:SelForm.DialogResult = [System.Windows.Forms.DialogResult]::OK
                $script:SelForm.Close()
            }
            else {
                $script:SelCountdown.Text = ('A telepítés {0} mp múlva magától indul – egy kattintás megállítja a visszaszámlálást.' -f $script:SelRemaining)
            }
        })
        $timer.Start()
        $list.add_MouseDown({ & $script:SelStop })
        $list.add_KeyDown({ & $script:SelStop })
    }
    $btnAll.add_Click({
        & $script:SelStop
        for ($i = 0; $i -lt $script:SelList.Items.Count; $i++) { $script:SelList.SetItemChecked($i, $true) }
    })
    $btnNone.add_Click({
        & $script:SelStop
        for ($i = 0; $i -lt $script:SelList.Items.Count; $i++) { $script:SelList.SetItemChecked($i, $false) }
    })

    $result = $form.ShowDialog()
    $timer.Stop()
    $timer.Dispose()
    $picked = @()
    foreach ($i in $list.CheckedIndices) { $picked += [int]$i }
    $form.Dispose()
    if ($result -ne [System.Windows.Forms.DialogResult]::OK) { return $null }
    return ,$picked
}

function New-ProgressUi {
    param($Programs)
    if ($NoGui) { return }
    New-ProgressUiForm $Programs
}

function New-ProgressUiForm {
    param($Programs)
    Initialize-WinForms
    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'Programok telepítése'
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.Font = New-Object System.Drawing.Font('Segoe UI', 10)
    $form.ClientSize = New-Object System.Drawing.Size(560, 540)
    $form.BackColor = [System.Drawing.Color]::White

    $status = New-Object System.Windows.Forms.Label
    $status.Font = New-Object System.Drawing.Font('Segoe UI', 13, [System.Drawing.FontStyle]::Bold)
    $status.Location = New-Object System.Drawing.Point(20, 16)
    $status.Size = New-Object System.Drawing.Size(520, 30)
    $status.AutoEllipsis = $true
    $status.Text = 'Előkészítés…'
    $form.Controls.Add($status)

    $bar = New-Object System.Windows.Forms.ProgressBar
    $bar.Location = New-Object System.Drawing.Point(20, 54)
    $bar.Size = New-Object System.Drawing.Size(520, 16)
    $bar.Maximum = [Math]::Max(1, $Programs.Count)
    $form.Controls.Add($bar)

    $info = New-Object System.Windows.Forms.Label
    $info.ForeColor = [System.Drawing.Color]::FromArgb(66, 66, 66)
    $info.Location = New-Object System.Drawing.Point(20, 78)
    $info.Size = New-Object System.Drawing.Size(520, 22)
    $form.Controls.Add($info)

    $list = New-Object System.Windows.Forms.ListBox
    $list.IntegralHeight = $false
    $list.Location = New-Object System.Drawing.Point(20, 106)
    $list.Size = New-Object System.Drawing.Size(520, 330)
    foreach ($p in $Programs) { [void]$list.Items.Add(('○  {0}' -f (Get-Prop $p 'name' '?'))) }
    $form.Controls.Add($list)

    $logLabel = New-Object System.Windows.Forms.Label
    $logLabel.ForeColor = [System.Drawing.Color]::FromArgb(92, 92, 92)
    $logLabel.Font = New-Object System.Drawing.Font('Segoe UI', 9)
    $logLabel.Location = New-Object System.Drawing.Point(20, 444)
    $logLabel.Size = New-Object System.Drawing.Size(520, 36)
    $logLabel.Text = ('Napló: {0}' -f $LogPath)
    $form.Controls.Add($logLabel)

    $close = New-UiButton 'Bezárás' 428 490 112 $true
    $close.Enabled = $false
    $close.add_Click({ $script:Ui.Done = $true; $script:Ui.Form.Close() })
    $form.Controls.Add($close)
    $form.add_FormClosing({
        param($sender, $e)
        if (-not $script:Ui.Done) { $e.Cancel = $true }
    })

    $script:Ui = @{ Form = $form; Status = $status; Bar = $bar; Info = $info; List = $list; Close = $close; Done = $false }
    $form.Show()
    Update-Ui
}

function Set-UiStatus {
    param([string]$Text)
    Write-Log $Text
    if ($script:Ui) { $script:Ui.Status.Text = $Text; Update-Ui }
}

function Set-UiInfo {
    param([string]$Text)
    if ($script:Ui) { $script:Ui.Info.Text = $Text; Update-Ui }
}

function Set-UiItem {
    param([int]$Index, [string]$Text, [int]$Completed = -1)
    if (-not $script:Ui) { return }
    $script:Ui.List.Items[$Index] = $Text
    $script:Ui.List.SelectedIndex = $Index
    if ($Completed -ge 0) { $script:Ui.Bar.Value = [Math]::Min($script:Ui.Bar.Maximum, $Completed) }
    Update-Ui
}

function Complete-Ui {
    param([string]$Summary)
    if (-not $script:Ui) { return }
    $script:Ui.Status.Text = $Summary
    $script:Ui.Bar.Value = $script:Ui.Bar.Maximum
    $script:Ui.Close.Enabled = $true
    $script:Ui.Form.Activate()
    while ($script:Ui.Form.Visible) {
        Update-Ui
        Start-Sleep -Milliseconds 100
    }
    $script:Ui.Form.Dispose()
    $script:Ui = $null
}

# ---------------------------------------------------------------- internet és winget

function Test-Internet {
    try {
        $r = Invoke-WebRequest -Uri 'http://www.msftconnecttest.com/connecttest.txt' -UseBasicParsing -TimeoutSec 5
        return ([string]$r.Content -like '*Microsoft Connect Test*')
    }
    catch {
        return $false
    }
}

function Wait-Internet {
    param([int]$TimeoutSeconds = 600)
    if ($DryRun) { return $true }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $opened = $false
    $first = $true
    while ((Get-Date) -lt $deadline) {
        if (Test-Internet) { return $true }
        if ($first) {
            Set-UiStatus 'Internetkapcsolatra várok…'
            Set-UiInfo 'Csatlakozz a WiFi-hez: jobb alsó sarok, hálózat ikon. A telepítés magától folytatódik.'
            $first = $false
        }
        if (-not $opened -and $script:OnWindows) {
            $opened = $true
            try { Start-Process 'ms-availablenetworks:' } catch { }
        }
        Wait-Ui 5
    }
    return $false
}

function Find-Winget {
    $cmd = Get-Command 'winget.exe' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    if ($env:LOCALAPPDATA) {
        $alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe'
        if (Test-Path -LiteralPath $alias) { return $alias }
    }
    if ($env:ProgramFiles) {
        $pkg = Get-ChildItem -Path (Join-Path $env:ProgramFiles 'WindowsApps') -Filter 'Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe' -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending | Select-Object -First 1
        if ($pkg) {
            $exe = Join-Path $pkg.FullName 'winget.exe'
            if (Test-Path -LiteralPath $exe) { return $exe }
        }
    }
    return $null
}

function Install-WingetFromWeb {
    Set-UiStatus 'A winget letöltése a Microsofttól…'
    $tmp = Join-Path $env:TEMP 'ProgramTelepito-winget'
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $files = @(
        @{ Url = 'https://aka.ms/Microsoft.VCLibs.x64.14.00.Desktop.appx'; Name = 'VCLibs.appx' },
        @{ Url = 'https://github.com/microsoft/microsoft-ui-xaml/releases/download/v2.8.6/Microsoft.UI.Xaml.2.8.x64.appx'; Name = 'UIXaml.appx' },
        @{ Url = 'https://aka.ms/getwinget'; Name = 'winget.msixbundle' }
    )
    foreach ($f in $files) {
        $path = Join-Path $tmp $f.Name
        try {
            Invoke-WebRequest -Uri $f.Url -OutFile $path -UseBasicParsing -ErrorAction Stop
            Add-AppxPackage -Path $path -ErrorAction Stop
        }
        catch {
            # Egy már telepített, újabb függőség is hibát ad - ez nem baj, a végén a winget megléte számít.
            Write-Log ('winget előkészítés ({0}): {1}' -f $f.Name, $_.Exception.Message)
        }
        Update-Ui
    }
}

# A friss Windowson a winget (App Installer) néha csak pár perccel a bejelentkezés után áll készen.
function Initialize-Winget {
    if ($DryRun) { return 'winget.exe' }
    $start = Get-Date
    $deadline = $start.AddMinutes(10)
    $registered = $false
    $downloaded = $false
    Set-UiStatus 'A winget előkészítése…'
    while ((Get-Date) -lt $deadline) {
        $w = Find-Winget
        if ($w) {
            try {
                $version = & $w --version 2>$null
                if ($LASTEXITCODE -eq 0) {
                    Write-Log ('winget {0} ({1})' -f $version, $w)
                    return $w
                }
            }
            catch { }
        }
        if (-not $registered) {
            $registered = $true
            try { Add-AppxPackage -RegisterByFamilyName -MainPackage 'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe' -ErrorAction Stop }
            catch { Write-Log ('winget regisztrálás: {0}' -f $_.Exception.Message) }
        }
        elseif (-not $downloaded -and ((Get-Date) - $start).TotalSeconds -gt 90) {
            $downloaded = $true
            Install-WingetFromWeb
        }
        Wait-Ui 5
    }
    return $null
}

# ---------------------------------------------------------------- telepítés

function Invoke-Program {
    param($Command)
    if ($DryRun) {
        Write-Log ('PRÓBA: {0} {1}' -f $Command.FilePath, $Command.Arguments)
        return 0
    }
    if ($Command.Kind -eq 'appx') {
        try {
            Add-AppxPackage -Path $Command.FilePath -ErrorAction Stop
            return 0
        }
        catch {
            Write-Log $_.Exception.Message
            return 1
        }
    }
    $startArgs = @{ FilePath = $Command.FilePath; PassThru = $true }
    if ($Command.Hidden) { $startArgs.WindowStyle = 'Hidden' }
    if ($Command.Arguments) { $startArgs.ArgumentList = $Command.Arguments }
    try {
        $proc = Start-Process @startArgs -ErrorAction Stop
    }
    catch {
        Write-Log ('Nem indítható: {0}' -f $_.Exception.Message)
        return -1
    }
    # A Handle lekérése nélkül a PowerShell a kilépési kódot néha nem kapja meg.
    $null = $proc.Handle
    $deadline = (Get-Date).AddMinutes($script:InstallTimeoutMinutes)
    while (-not $proc.HasExited) {
        Update-Ui
        Start-Sleep -Milliseconds 200
        if ((Get-Date) -gt $deadline) {
            try { $proc.Kill() } catch { }
            Write-Log ('Időtúllépés ({0} perc) - leállítva.' -f $script:InstallTimeoutMinutes)
            return -2
        }
    }
    $proc.WaitForExit()
    return [int]$proc.ExitCode
}

function Format-Elapsed {
    param([TimeSpan]$Span)
    return ('{0}:{1:00}' -f [int][Math]::Floor($Span.TotalMinutes), $Span.Seconds)
}

function Show-RebootQuestion {
    $answer = [System.Windows.Forms.MessageBox]::Show('Egyes programok az újraindítás után lesznek teljesen használhatók. Újraindítod most?', 'Programok telepítése', 'YesNo', 'Question')
    return ($answer -eq 'Yes')
}

function Show-FatalError {
    param([string]$Text)
    Add-Type -AssemblyName System.Windows.Forms
    [void][System.Windows.Forms.MessageBox]::Show($Text, 'Programok telepítése', 'OK', 'Error')
}

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Main {
    Write-Log '=== Programok telepítése indul ==='
    if ($script:OnWindows) {
        # A Windows PowerShell 5.1 régebbi .NET-je magától nem mindig kér TLS 1.2-t.
        try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }
    }
    if ($script:OnWindows -and -not $DryRun -and -not (Test-IsAdmin)) {
        Write-Log 'Rendszergazdai jog kérése…'
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', ('"{0}"' -f $PSCommandPath))
        return
    }
    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        Write-Log ('Nincs programlista: {0}' -f $ConfigPath)
        return
    }
    $cfg = Read-ProgramConfig $ConfigPath
    if ($cfg.Programs.Count -eq 0) {
        Write-Log 'A programlista üres.'
        return
    }

    $selected = Show-ProgramSelection $cfg.Programs $cfg.AutoStartSeconds
    if ($null -eq $selected) {
        Write-Log 'A telepítést a felhasználó kihagyta.'
        return
    }
    $toInstall = @()
    foreach ($i in $selected) { $toInstall += $cfg.Programs[$i] }
    if ($toInstall.Count -eq 0) {
        Write-Log 'Egy program sincs kiválasztva.'
        return
    }
    Write-Log ('Kiválasztva: {0}' -f (($toInstall | ForEach-Object { Get-Prop $_ 'name' '?' }) -join ', '))

    New-ProgressUi $toInstall
    $winget = $null
    $wingetProblem = $null
    $needWinget = @($toInstall | Where-Object { (Get-Prop $_ 'type' 'winget') -eq 'winget' }).Count -gt 0
    if ($needWinget) {
        if (Wait-Internet) {
            Set-UiInfo 'Internet: csatlakozva'
            $winget = Initialize-Winget
            if ($winget) { Set-UiInfo ('Internet: csatlakozva · winget kész') }
            else { $wingetProblem = 'a winget nem érhető el' }
        }
        else {
            $wingetProblem = 'nincs internet'
        }
        if ($wingetProblem) {
            Write-Log ('A winget-es programok kimaradnak: {0}' -f $wingetProblem)
            Set-UiInfo ('A winget-es programok kimaradnak: {0}' -f $wingetProblem)
        }
    }

    $ok = 0
    $failed = @()
    $needsReboot = $false
    for ($i = 0; $i -lt $toInstall.Count; $i++) {
        $p = $toInstall[$i]
        $name = [string](Get-Prop $p 'name' '?')
        $type = [string](Get-Prop $p 'type' 'winget')
        Set-UiStatus ('{0} telepítése…  ({1} / {2})' -f $name, ($i + 1), $toInstall.Count)
        Set-UiItem $i ('›  {0}   – folyamatban' -f $name)
        $started = Get-Date
        $success = $false
        $detail = ''
        if ($type -eq 'winget' -and -not $winget) {
            $detail = $wingetProblem
        }
        else {
            $cmd = Get-InstallCommand $p $winget
            if ($cmd.SourceFile -and -not (Test-Path -LiteralPath $cmd.SourceFile)) {
                $detail = 'a telepítő fájl hiányzik'
            }
            else {
                $code = Invoke-Program $cmd
                $success = Test-InstallSuccess $cmd.Kind $code
                if ($script:RebootCodes -contains $code) { $needsReboot = $true }
                if (-not $success) { $detail = ('hibakód {0}' -f (Format-ExitCode $code)) }
                if ($success -and $cmd.SourceFile -and -not $DryRun) {
                    # A sikeresen feltelepített saját telepítő törölhető (a sikerteleneket a kézi újrapróbáláshoz megtartjuk).
                    Remove-Item -LiteralPath $cmd.SourceFile -Force -ErrorAction SilentlyContinue
                }
            }
        }
        $elapsed = Format-Elapsed ((Get-Date) - $started)
        if ($success) {
            $ok++
            Write-Log ('OK: {0} ({1})' -f $name, $elapsed)
            Set-UiItem $i ('✓  {0}   – {1}' -f $name, $elapsed) ($i + 1)
        }
        else {
            $failed += $name
            Write-Log ('HIBA: {0} - {1}' -f $name, $detail)
            Set-UiItem $i ('✗  {0}   – {1}' -f $name, $detail) ($i + 1)
        }
    }

    if ($failed.Count -eq 0) {
        $summary = ('Kész – mind a(z) {0} program feltelepült.' -f $ok)
    }
    else {
        $summary = ('Kész – {0} sikerült, {1} nem: {2}' -f $ok, $failed.Count, ($failed -join ', '))
        try {
            $desktop = [Environment]::GetFolderPath('Desktop')
            if ($desktop -and -not $DryRun) { Copy-Item -LiteralPath $LogPath -Destination (Join-Path $desktop 'Programok telepítése - napló.txt') -Force }
        }
        catch { }
    }
    Write-Log $summary
    try { Set-Content -LiteralPath (Join-Path $script:ScriptDir 'kesz.txt') -Value (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') -Encoding UTF8 } catch { }
    Complete-Ui $summary

    if (-not $DryRun -and $script:OnWindows) {
        if ($cfg.RebootWhenDone) {
            Write-Log 'Újraindítás 60 mp múlva.'
            & shutdown.exe /r /t 60 /c 'A programok telepítése kész – a gép 60 mp múlva újraindul. (Megszakítás: shutdown /a)'
        }
        elseif ($needsReboot -and -not $NoGui) {
            if (Show-RebootQuestion) { & shutdown.exe /r /t 5 }
        }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        Invoke-Main
    }
    catch {
        Write-Log ('VÁRATLAN HIBA: {0}' -f $_)
        if (-not $NoGui -and $script:OnWindows) {
            try { Show-FatalError ('Váratlan hiba a programok telepítése közben:' + [Environment]::NewLine + $_ + [Environment]::NewLine + 'Napló: ' + $LogPath) }
            catch { }
        }
    }
}
