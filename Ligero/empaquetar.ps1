# Prepara el paquete MSIX de Ligero para la Microsoft Store.
#
#   empaquetar.ps1            Compila, prepara la carpeta del paquete y, si está el Windows SDK, crea el .msix
#   empaquetar.ps1 -Probar    Además instala esa versión en este equipo para probarla (requiere el modo de desarrollador)
#   empaquetar.ps1 -Quitar    Desinstala la versión de prueba
#
# Todo se escribe en tienda\salida. El .msix NO hay que firmarlo: la Microsoft Store lo firma al publicarlo.
param([switch]$Probar, [switch]$Quitar)

$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$salida = Join-Path $raiz 'tienda\salida'
$carpeta = Join-Path $salida 'paquete'

function Fallo($texto) { Write-Host ''; Write-Host $texto -ForegroundColor Red; exit 1 }

# ---- identidad ----
$id = @{}
foreach ($linea in Get-Content (Join-Path $raiz 'tienda\identidad.txt') -Encoding UTF8) {
    if ($linea -match '^\s*#' -or $linea -notmatch '=') { continue }
    $i = $linea.IndexOf('=')
    $id[$linea.Substring(0, $i).Trim()] = $linea.Substring($i + 1).Trim()
}
foreach ($k in 'Nombre', 'Editor', 'NombreEditor', 'NombreApp', 'Version') { if (-not $id[$k]) { Fallo "Falta '$k' en tienda\identidad.txt." } }
if ($id.Version -notmatch '^\d+\.\d+\.\d+\.0$') { Fallo "La versión debe tener cuatro números y terminar en .0 (por ejemplo 1.0.0.0)." }

if ($Quitar) {
    $p = Get-AppxPackage -Name $id.Nombre
    if ($p) { $p | Remove-AppxPackage; Write-Host 'Versión de prueba desinstalada.' } else { Write-Host 'No hay ninguna versión de prueba instalada.' }
    exit 0
}

# ---- carpeta del paquete ----
if (Test-Path $carpeta) { Remove-Item -Recurse -Force $carpeta }
New-Item -ItemType Directory -Force (Join-Path $carpeta 'Assets') | Out-Null

Write-Host 'Compilando Ligero.exe...'
cmd /c "`"$raiz\compilar.bat`" `"$carpeta\Ligero.exe`""
if ($LASTEXITCODE -ne 0) { Fallo 'No se pudo compilar.' }

function Xml($s) { [Security.SecurityElement]::Escape($s) }
$manifiesto = [IO.File]::ReadAllText((Join-Path $raiz 'tienda\AppxManifest.xml'), [Text.Encoding]::UTF8)
$manifiesto = $manifiesto.Replace('{NOMBRE}', (Xml $id.Nombre)).Replace('{EDITOR}', (Xml $id.Editor)).
    Replace('{NOMBRE_EDITOR}', (Xml $id.NombreEditor)).Replace('{NOMBRE_APP}', (Xml $id.NombreApp)).Replace('{VERSION}', (Xml $id.Version))
[IO.File]::WriteAllText((Join-Path $carpeta 'AppxManifest.xml'), $manifiesto, (New-Object Text.UTF8Encoding $true))

# ---- imágenes del paquete (se dibujan aquí, con el mismo diseño que el icono) ----
Add-Type -AssemblyName System.Drawing
function Logo([int]$ancho, [int]$alto, [string]$archivo) {
    $bmp = New-Object Drawing.Bitmap($ancho, $alto)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $lado = [Math]::Min($ancho, $alto) * 0.86
    $x = ($ancho - $lado) / 2; $y = ($alto - $lado) / 2
    $r = $lado * 0.22; $d = 2 * $r
    $ruta = New-Object Drawing.Drawing2D.GraphicsPath
    $ruta.AddArc($x, $y, $d, $d, 180, 90); $ruta.AddArc($x + $lado - $d, $y, $d, $d, 270, 90)
    $ruta.AddArc($x + $lado - $d, $y + $lado - $d, $d, $d, 0, 90); $ruta.AddArc($x, $y + $lado - $d, $d, $d, 90, 90)
    $ruta.CloseFigure()
    $pincel = New-Object Drawing.Drawing2D.LinearGradientBrush((New-Object Drawing.PointF($x, $y)), (New-Object Drawing.PointF($x, ($y + $lado))),
        [Drawing.Color]::FromArgb(40, 140, 235), [Drawing.Color]::FromArgb(0, 85, 170))
    $g.FillPath($pincel, $ruta)
    $s = $lado / 256.0
    $puntos = 150, 30, 70, 140, 120, 140, 100, 226, 186, 108, 134, 108
    $rayo = for ($i = 0; $i -lt 12; $i += 2) { New-Object Drawing.PointF(($x + $puntos[$i] * $s), ($y + $puntos[$i + 1] * $s)) }
    $g.FillPolygon([Drawing.Brushes]::White, [Drawing.PointF[]]$rayo)
    $g.Dispose()
    $bmp.Save((Join-Path $carpeta "Assets\$archivo"), [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
Logo 44 44 'Square44x44Logo.png'
Logo 150 150 'Square150x150Logo.png'
Logo 310 150 'Wide310x150Logo.png'
Logo 50 50 'StoreLogo.png'
Write-Host "Carpeta del paquete lista: $carpeta"

# ---- .msix (necesita MakeAppx: del Windows SDK, o de las herramientas del SDK descargadas de nuget.org) ----
$buscar = @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", (Join-Path $env:LOCALAPPDATA 'Ligero-Herramientas'))
$makeappx = Get-ChildItem $buscar -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' -and (Get-AuthenticodeSignature $_.FullName).Status -eq 'Valid' } |
    Sort-Object FullName -Descending | Select-Object -First 1
if ($makeappx) {
    # En algunos equipos MakeAppx no arranca desde AppData\Local ("configuración en paralelo incorrecta")
    # y sí desde la carpeta temporal: se usa una copia allí.
    $tmp = Join-Path $env:TEMP 'ligero-makeappx'
    New-Item -ItemType Directory -Force $tmp | Out-Null
    Copy-Item (Join-Path $makeappx.DirectoryName '*') $tmp -Force
    $msix = Join-Path $salida ("Ligero-" + $id.Version + ".msix")
    & (Join-Path $tmp 'makeappx.exe') pack /o /d $carpeta /p $msix
    if ($LASTEXITCODE -ne 0) { Fallo 'MakeAppx no pudo crear el paquete (mira el mensaje de arriba).' }
    Write-Host "Paquete para la Store: $msix" -ForegroundColor Green
} else {
    Write-Host ('No se encontró MakeAppx: instala el Windows SDK de Microsoft, o descarga el paquete «Microsoft.Windows.SDK.BuildTools» ' +
                'de nuget.org y descomprímelo en ' + (Join-Path $env:LOCALAPPDATA 'Ligero-Herramientas')) -ForegroundColor Yellow
}

# ---- prueba en este equipo ----
if ($Probar) {
    $dev = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
    if ($dev -ne 1) {
        Fallo ('Para instalar la versión de prueba hay que activar el modo de desarrollador: ' +
               'Configuración > Sistema > Para programadores > Modo de desarrollador.')
    }
    Get-AppxPackage -Name $id.Nombre | Remove-AppxPackage
    Add-AppxPackage -Register (Join-Path $carpeta 'AppxManifest.xml')
    Write-Host 'Versión de prueba instalada: búscala como «Ligero» en el menú Inicio.' -ForegroundColor Green
}
