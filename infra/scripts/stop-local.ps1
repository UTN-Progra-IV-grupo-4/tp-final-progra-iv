<#
.SYNOPSIS
    Detiene lo que levanto run-local.ps1: los tres servicios .NET (solo por PID) y los contenedores.

.PARAMETER EnvFile
    Ruta del .env que se uso para levantar. Por defecto, el .env de la raiz del repositorio.

.PARAMETER RemoveData
    Ademas borra los volumenes (bases de datos y Keycloak). La proxima vez arranca desde cero.
#>
[CmdletBinding()]
param(
    [string] $EnvFile,
    [switch] $RemoveData
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'local-common.ps1')

if (-not $EnvFile) { $EnvFile = Join-Path $script:RepositoryRoot '.env' }

Write-Step 'Deteniendo los servicios .NET (solo los PIDs guardados por run-local.ps1)'
foreach ($entry in @(Get-TrackedProcesses)) {
    if ($entry.IsAlive) {
        Stop-Process -Id $entry.ProcessId -Force
        Write-Host "    detenido $($entry.Name) (PID $($entry.ProcessId))"
    } else {
        Write-Host "    $($entry.Name) (PID $($entry.ProcessId)) ya no estaba corriendo"
    }
}
if (Test-Path -LiteralPath $script:PidFilePath) { Remove-Item -LiteralPath $script:PidFilePath }

Write-Step 'Bajando los contenedores'
if (-not (Test-Path -LiteralPath $EnvFile)) {
    Write-Host "    AVISO: no existe '$EnvFile'; docker compose lo necesita para resolver variables. Pasa -EnvFile con el .env que usaste." -ForegroundColor Yellow
    exit 1
}
$composeArguments = @('down')
if ($RemoveData) { $composeArguments += '-v' }
$exitCode = Invoke-Compose -EnvFilePath (Resolve-Path -LiteralPath $EnvFile).Path -Arguments $composeArguments -Quiet
if ($exitCode -ne 0) { Stop-WithError 'docker compose down fallo.' }
Write-Host 'Todo apagado.' -ForegroundColor Green
