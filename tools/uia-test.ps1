# Prueba de punta a punta con UI Automation (lo que usan Narrador y NVDA):
#  1) comprueba que todos los controles tienen nombre accesible,
#  2) añade un juego en «Por jugar», lo marca como completado y guarda con Ctrl+S,
#  3) compara el archivo guardado con lo esperado.
# Uso: pwsh tools/uia-test.ps1 -Exe ...\Checkpoint.exe -List copia.txt
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$List)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
$A = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$C = [System.Windows.Automation.ControlType]

function Find-All($root, $type) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $type)
    $root.FindAll($Scope::Descendants, $cond)
}
function Find-Named($root, $name, $type, [int]$timeout = 10) {
    $end = (Get-Date).AddSeconds($timeout)
    do {
        $c1 = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
        $cond = if ($type) { New-Object System.Windows.Automation.AndCondition($c1, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $type))) } else { $c1 }
        $el = $root.FindFirst($Scope::Descendants, $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $end)
    throw "No se encontró «$name»"
}
function Invoke-El($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 400 }

$before = [IO.File]::ReadAllText($List)
$p = Start-Process $Exe -ArgumentList "`"$List`"" -PassThru
try {
    $end = (Get-Date).AddSeconds(20)
    while ($p.MainWindowHandle -eq 0 -and (Get-Date) -lt $end) { Start-Sleep -Milliseconds 300; $p.Refresh() }
    $win = $A::FromHandle($p.MainWindowHandle)
    $null = Find-Named $win 'Añadir juego' $C::Button 20

    # 1) nombres accesibles
    $unnamed = @()
    foreach ($ctype in @($C::Button, $C::Edit, $C::ComboBox, $C::CheckBox, $C::RadioButton, $C::TabItem)) {
        foreach ($el in (Find-All $win $ctype)) {
            if ([string]::IsNullOrWhiteSpace($el.Current.Name) -and -not $el.Current.IsOffscreen) { $unnamed += "$($ctype.ProgrammaticName) ($($el.Current.AutomationId))" }
        }
    }
    $buttons = (Find-All $win $C::Button).Count
    $tabs = Find-All $win $C::TabItem
    Write-Output "Botones: $buttons · Pestañas: $(($tabs | ForEach-Object { $_.Current.Name }) -join ' | ')"
    if ($unnamed.Count) { Write-Output "SIN NOMBRE: $($unnamed -join ', ')" } else { Write-Output 'OK: todos los controles visibles tienen nombre accesible' }

    # 2) añadir un juego en «Por jugar», marcarlo como completado y guardar
    $tabPor = $tabs | Where-Object { $_.Current.Name -like 'Por jugar*' } | Select-Object -First 1
    $tabPor.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 600
    Invoke-El (Find-Named $win 'Añadir juego' $C::Button)
    # La ficha nueva tarda un momento en llegar al árbol de accesibilidad: esperar a que el título esté vacío.
    # Los valores se escriben con ValuePattern (como un lector de pantalla), sin depender del foco del teclado.
    $vp = [System.Windows.Automation.ValuePattern]::Pattern
    $end = (Get-Date).AddSeconds(10)
    do { Start-Sleep -Milliseconds 300; $title = Find-Named $win 'Título' $C::Edit } while ($title.GetCurrentPattern($vp).Current.Value -ne '' -and (Get-Date) -lt $end)
    $title.SetFocus(); $title.GetCurrentPattern($vp).SetValue('Hollow Knight'); Start-Sleep -Milliseconds 400
    $plat = Find-Named $win 'Plataformas' $C::ComboBox
    $plat.SetFocus(); $plat.GetCurrentPattern($vp).SetValue('PC'); Start-Sleep -Milliseconds 400
    $title.SetFocus(); Start-Sleep -Milliseconds 300
    Invoke-El (Find-Named $win 'Marcar como completado' $C::Button)
    Start-Sleep -Milliseconds 600
    Invoke-El (Find-Named $win 'Guardar' $C::Button); Start-Sleep -Milliseconds 1500
    $winTitle = (Get-Process -Id $p.Id).MainWindowTitle
    Write-Output "Título de la ventana tras guardar: $winTitle"
}
finally {
    Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
}

# 3) comparar
$after = [IO.File]::ReadAllText($List)
$a = $before -split "`r`n"; $b = $after -split "`r`n"
$added = $b | Where-Object { $_ -notin $a }
$removed = $a | Where-Object { $_ -notin $b }
Write-Output "Líneas nuevas: $($added -join ' || ')"
Write-Output "Líneas quitadas: $($removed -join ' || ')"
