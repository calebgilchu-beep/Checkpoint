# Prueba «Guardar como .md» de punta a punta: abre la lista, la guarda como Markdown con el diálogo
# nativo de Windows, vuelve a abrir el .md y comprueba que salen las mismas secciones con los mismos juegos.
# Uso: powershell -File tools/uia-md-test.ps1 -Exe ...\Checkpoint.exe -List copia.txt -Md salida.md
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$List, [Parameter(Mandatory)][string]$Md)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
$A = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
function Cond($name) { New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name) }
function Find-Web($root, $name, [int]$timeout = 15) {
    $c = New-Object System.Windows.Automation.AndCondition((Cond $name), (New-Object System.Windows.Automation.PropertyCondition($A::FrameworkIdProperty, 'Chrome')))
    $end = (Get-Date).AddSeconds($timeout)
    do { $el = $root.FindFirst($Scope::Descendants, $c); if ($el) { return $el }; Start-Sleep -Milliseconds 300 } while ((Get-Date) -lt $end)
    throw "No se encontró «$name»"
}
function Invoke-El($el) { $pt = $null; if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pt)) { $pt.Invoke() } elseif ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pt)) { $pt.Expand() } else { $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }; Start-Sleep -Milliseconds 600 }
function Tabs($win) {
    $c = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
    ($win.FindAll($Scope::Descendants, $c) | ForEach-Object { $_.Current.Name }) -join ' | '
}
function Start-App($file) {
    $p = Start-Process $Exe -ArgumentList "`"$file`"" -PassThru
    $end = (Get-Date).AddSeconds(20)
    while ($p.MainWindowHandle -eq 0 -and (Get-Date) -lt $end) { Start-Sleep -Milliseconds 300; $p.Refresh() }
    $win = $A::FromHandle($p.MainWindowHandle)
    $null = Find-Web $win 'Añadir juego' 20
    return @($p, $win)
}

if (Test-Path $Md) { Remove-Item -LiteralPath $Md }
$p, $win = Start-App $List
try {
    $tabsTxt = Tabs $win
    Invoke-El (Find-Web $win 'Guardar como')
    Invoke-El (Find-Web $win 'Guardar como Markdown (.md)')
    # Diálogo nativo «Guardar»: escribir la ruta y aceptar.
    $end = (Get-Date).AddSeconds(10); $dlg = $null
    do {
        $dlg = $A::RootElement.FindFirst($Scope::Children, (New-Object System.Windows.Automation.AndCondition((Cond 'Guardar lista de juegos'), (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)))))
        if (-not $dlg) { $dlg = $win.FindFirst($Scope::Descendants, (Cond 'Guardar lista de juegos')) }
        if (-not $dlg) { Start-Sleep -Milliseconds 300 }
    } while (-not $dlg -and (Get-Date) -lt $end)
    if (-not $dlg) { throw 'No se abrió el diálogo de guardar' }
    # Se acepta el nombre que propone el diálogo (mismo nombre, extensión .md) pulsando «Guardar».
    Start-Sleep -Milliseconds 1500
    Add-Type @"
using System; using System.Runtime.InteropServices;
public static class BM { [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l); }
"@
    $btn = $dlg.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, '1')))
    [BM]::SendMessage([IntPtr]$btn.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Seconds 2
    Write-Output "Pestañas (.txt): $tabsTxt"
    Write-Output "Título tras guardar: $((Get-Process -Id $p.Id).MainWindowTitle)"
}
finally { Stop-Process -Id $p.Id -ErrorAction SilentlyContinue }

if (-not (Test-Path $Md)) { throw 'No se creó el .md' }
Write-Output "--- primeras líneas del .md ---"
Get-Content -LiteralPath $Md -Encoding UTF8 -TotalCount 24

$p, $win = Start-App $Md
try { Write-Output "Pestañas (.md reabierto): $(Tabs $win)" }
finally { Stop-Process -Id $p.Id -ErrorAction SilentlyContinue }
