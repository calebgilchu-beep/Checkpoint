# Capturas de la app real para revisarla a ojo (vista principal, importar, ajustes, texto, ayuda).
# Uso: powershell -File tools/shots.ps1 -Exe ...\Checkpoint.exe -List copia.txt -OutDir carpeta [-Width 1360 -Height 860]
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$List, [Parameter(Mandatory)][string]$OutDir,
      [int]$Width = 1360, [int]$Height = 860)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W2 { [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }
"@
$A = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
function Find-Named($root, $name, [int]$timeout = 15) {
    $end = (Get-Date).AddSeconds($timeout)
    do {
        $el = $root.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)), (New-Object System.Windows.Automation.PropertyCondition($A::FrameworkIdProperty, "Chrome")), (New-Object System.Windows.Automation.OrCondition((New-Object System.Windows.Automation.PropertyCondition($A::IsInvokePatternAvailableProperty, $true)), (New-Object System.Windows.Automation.PropertyCondition($A::IsTogglePatternAvailableProperty, $true)))))))
        if ($el) { return $el }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $end)
    throw "No se encontró «$name»"
}
function Invoke-Name($root, $name) { $el = Find-Named $root $name; $ip = $null; if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$ip)) { $ip.Invoke() } else { $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }; Start-Sleep -Milliseconds 700 }
$cap = Join-Path $PSScriptRoot 'capture-window.ps1'
function Shot($name) { Start-Sleep -Milliseconds 600; & $cap -ProcessId $p.Id -Out (Join-Path $OutDir "$name.png") | Out-Null; Write-Output $name }

$p = Start-Process $Exe -ArgumentList "`"$List`"" -PassThru
try {
    $end = (Get-Date).AddSeconds(20)
    while ($p.MainWindowHandle -eq 0 -and (Get-Date) -lt $end) { Start-Sleep -Milliseconds 300; $p.Refresh() }
    [W2]::MoveWindow($p.MainWindowHandle, 40, 40, $Width, $Height, $true) | Out-Null
    [W2]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
    $win = $A::FromHandle($p.MainWindowHandle)
    $null = Find-Named $win 'Añadir juego' 20
    Start-Sleep -Seconds 1
    Shot 'main'
    Invoke-Name $win 'Importar'; Shot 'import1'
    Invoke-Name $win 'Buscar juegos'; Start-Sleep -Seconds 3; Shot 'import2'
    Invoke-Name $win 'Cerrar'
    Invoke-Name $win 'Ajustes'; Shot 'settings'
    Invoke-Name $win 'Cerrar'
    Invoke-Name $win 'Texto'; Shot 'text'
    Invoke-Name $win 'Texto'
    Invoke-Name $win 'Vista de lista compacta'; Shot 'list'
    Invoke-Name $win 'Vista de portadas'
    Invoke-Name $win 'Ayuda y atajos de teclado'; Shot 'help'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
}
finally { Stop-Process -Id $p.Id -ErrorAction SilentlyContinue }
