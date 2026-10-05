# Development helpers for the GUI: start Offload.exe, press its buttons (UI Automation), take screenshots.
#   . .\scripts\app-drive.ps1
#   $env:OFFLOAD_SETTINGS_DIR = 'D:\tmp\offload-settings'   # isolated settings for the run
#   $p = Start-Offload -Exe .\src\Offload.App\bin\Debug\net10.0-windows\Offload.exe -Arguments '--no-hw-prompt'
#   Invoke-OffloadButton $p 'StartButton'
#   Save-OffloadScreenshot $p 'D:\tmp\shot.png'
#   Stop-Offload $p
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
if (-not ('OffloadWin' -as [type])) {
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class OffloadWin {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
}
if (-not ('OffloadDlg' -as [type])) {
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class OffloadDlg {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
}
'@
}
[OffloadWin]::SetProcessDPIAware() | Out-Null
$AE = [System.Windows.Automation.AutomationElement]

function Start-Offload([string]$Exe, [string]$Arguments = '', [int]$TimeoutSeconds = 30) {
    $p = if ($Arguments) { Start-Process $Exe -ArgumentList $Arguments -PassThru } else { Start-Process $Exe -PassThru }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($p.MainWindowHandle -eq 0 -and -not $p.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200; $p.Refresh() }
    if ($p.HasExited) { throw "Offload exited during start (code $($p.ExitCode))" }
    if ($p.MainWindowHandle -eq 0) { throw 'Offload window did not appear' }
    Start-Sleep -Milliseconds 1000
    $p
}

# Top-level windows of the process. Message boxes are Win32 dialogs (class #32770) titled "Offload" like the
# main window, so they are told apart by class: -Dialog picks message boxes, without it they are skipped.
# A message box with an owner (MessageBox.Show(window, ...)) is not under the desktop in UI Automation but a child
# of its owner window, so -Dialog also looks one level below each top-level window.
function Get-OffloadWindows($Process, [switch]$Dialog) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $Process.Id)
    $top = @($AE::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond))
    if (-not $Dialog) { return $top | Where-Object { $_.Current.ClassName -ne '#32770' } }
    $dlg = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, '#32770')
    $top | Where-Object { $_.Current.ClassName -eq '#32770' }
    foreach ($w in $top) { @($w.FindAll([System.Windows.Automation.TreeScope]::Children, $dlg)) }
}

function Find-OffloadWindow($Process, [string]$TitlePattern = 'Offload', [switch]$Dialog, [int]$TimeoutSeconds = 10) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $w = Get-OffloadWindows $Process -Dialog:$Dialog | Where-Object { $_.Current.Name -like $TitlePattern } | Select-Object -First 1
        if ($w) { return $w }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    throw "window '$TitlePattern' not found"
}

function Find-OffloadElement($Process, [string]$AutomationId, [string]$WindowTitle = 'Offload', [switch]$Dialog, [int]$TimeoutSeconds = 10) {
    $window = Find-OffloadWindow $Process $WindowTitle -Dialog:$Dialog -TimeoutSeconds $TimeoutSeconds
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $AutomationId)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $el = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    throw "element '$AutomationId' not found in '$WindowTitle'"
}

function Invoke-OffloadButton($Process, [string]$AutomationId, [string]$WindowTitle = 'Offload', [switch]$Dialog) {
    $el = Find-OffloadElement $Process $AutomationId $WindowTitle -Dialog:$Dialog
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

# Presses a message-box button: '6' = Ano/Yes, '7' = Ne/No, '2' = OK (the Win32 command ids).
# The buttons of an owned message box show up in UI Automation as panes without InvokePattern, so the dialog gets
# the button's WM_COMMAND directly - exactly what a click sends.
function Invoke-OffloadDialogButton($Process, [string]$AutomationId = '6') {
    $dlg = Find-OffloadWindow $Process '*' -Dialog
    Find-OffloadElement $Process $AutomationId '*' -Dialog | Out-Null   # the button exists
    [OffloadDlg]::PostMessage([IntPtr][int]$dlg.Current.NativeWindowHandle, 0x0111, [IntPtr][int]$AutomationId, [IntPtr]::Zero) | Out-Null
}

# PrintWindow renders the window itself - works even when it is covered by other windows.
function Save-OffloadScreenshot($Process, [string]$Path, [string]$WindowTitle = 'Offload', [switch]$Dialog) {
    $h = [IntPtr]([int](Find-OffloadWindow $Process $WindowTitle -Dialog:$Dialog).Current.NativeWindowHandle)
    $r = New-Object OffloadWin+RECT
    [OffloadWin]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [OffloadWin]::PrintWindow($h, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Stop-Offload($Process) {
    if ($Process.HasExited) { return }
    $Process.CloseMainWindow() | Out-Null
    if (-not $Process.WaitForExit(10000)) { $Process.Kill() }
}
