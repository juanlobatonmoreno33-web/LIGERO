# Abre Ligero compilando su código en memoria con el compilador que trae Windows.
# Sirve para equipos donde Windows (Smart App Control) no deja abrir Ligero.exe por no estar firmado.
# Es el mismo programa: usa exactamente los archivos de la carpeta src.
param(
    [switch]$SoloCargar,      # para pruebas: carga el código sin abrir la ventana
    [string]$DatosPrueba,     # para pruebas: usa esta carpeta de datos en vez de la real
    [switch]$SegundoPlano     # arranque con Windows: sin mostrar la ventana, con icono junto al reloj
)

$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'src'
$fuentes = foreach ($f in (Get-ChildItem -Path $src -Filter '*.cs' | Sort-Object Name)) {
    [IO.File]::ReadAllText($f.FullName, [Text.Encoding]::UTF8)
}

$opciones = New-Object System.CodeDom.Compiler.CompilerParameters
$opciones.GenerateInMemory = $true
$opciones.GenerateExecutable = $false
$opciones.CompilerOptions = '/optimize+'
$fw = [Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$wm = Join-Path $env:WINDIR 'System32\WinMetadata'
[void]$opciones.ReferencedAssemblies.AddRange([string[]]@(
    'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Management.dll',
    # Para las funciones de la versión de la Store (arranque con Windows)
    (Join-Path $fw 'System.Runtime.dll'), (Join-Path $wm 'Windows.Foundation.winmd'), (Join-Path $wm 'Windows.ApplicationModel.winmd')))

$compilador = New-Object Microsoft.CSharp.CSharpCodeProvider
$resultado = $compilador.CompileAssemblyFromSource($opciones, [string[]]$fuentes)
if ($resultado.Errors.HasErrors) {
    $texto = ($resultado.Errors | ForEach-Object { $_.ToString() }) -join "`r`n"
    Add-Type -AssemblyName System.Windows.Forms
    [void][Windows.Forms.MessageBox]::Show("No se pudo preparar Ligero:`r`n`r`n$texto", 'Ligero')
    exit 1
}
[void]$resultado.CompiledAssembly   # fuerza la carga en memoria

if ($DatosPrueba) { [Ligero.Util]::AppDirOverride = $DatosPrueba }
if (-not $SoloCargar) {
    [Ligero.Program]::ScriptPath = $PSCommandPath   # para poder reabrirse como administrador
    [Ligero.Program]::Background = $SegundoPlano.IsPresent
    [Ligero.Program]::Main()
}
