# Funciones compartidas por run-local.ps1 y stop-local.ps1. No se ejecuta directamente.

Set-StrictMode -Version Latest

$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:RunDirectory = Join-Path $script:RepositoryRoot '.run'
$script:PidFilePath = Join-Path $script:RunDirectory 'pids.json'

# Los tres servicios .NET: nombre, proyecto de arranque, ensamblado y puerto fijo.
# El puerto 5212 no es casual: es el unico que el realm permite como redireccion del login.
$script:Services = @(
    @{ Name = 'payment';      ProjectDirectory = 'src\Payment\Payment.Grpc';       Assembly = 'Payment.Grpc';      Port = 5183 },
    @{ Name = 'booking';      ProjectDirectory = 'src\Booking\Booking.Api';        Assembly = 'Booking.Api';       Port = 5088 },
    @{ Name = 'availability'; ProjectDirectory = 'src\Availability\Availability.Web'; Assembly = 'Availability.Web'; Port = 5212 }
)

function Write-Step {
    param([string] $Message)
    Write-Host ("[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $Message) -ForegroundColor Cyan
}

function Stop-WithError {
    param([string] $Message)
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

# Lee un archivo .env (CLAVE=valor, # comentarios, comillas opcionales) a un diccionario.
function Read-EnvironmentFile {
    param([string] $Path)

    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path -Encoding UTF8) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $separatorIndex = $trimmed.IndexOf('=')
        if ($separatorIndex -lt 1) { continue }
        $name = $trimmed.Substring(0, $separatorIndex).Trim()
        $value = $trimmed.Substring($separatorIndex + 1).Trim()
        if ($value.Length -ge 2 -and
            (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'")))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        $values[$name] = $value
    }
    return $values
}

# Ejecuta un programa nativo sin que el stderr de docker/dotnet se convierta en un error de PowerShell.
# Devuelve el codigo de salida; la salida se muestra salvo que se pida lo contrario.
function Invoke-Native {
    param(
        [string] $FilePath,
        [string[]] $Arguments,
        [switch] $Quiet
    )

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $FilePath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousPreference
    }
    if (-not $Quiet -or $exitCode -ne 0) {
        $output | ForEach-Object { Write-Host ("    " + $_.ToString()) -ForegroundColor DarkGray }
    }
    return $exitCode
}

# Igual que Invoke-Native pero devuelve la salida como texto (para consultas).
function Get-NativeOutput {
    param([string] $FilePath, [string[]] $Arguments)

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $FilePath @Arguments 2>&1 | ForEach-Object { $_.ToString() }
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousPreference
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Text = ($output -join "`n") }
}

function Invoke-Compose {
    param([string] $EnvFilePath, [string[]] $Arguments, [switch] $Quiet)

    $composeArguments = @('compose', '--project-directory', $script:RepositoryRoot, '--env-file', $EnvFilePath) + $Arguments
    return Invoke-Native -FilePath 'docker' -Arguments $composeArguments -Quiet:$Quiet
}

function Get-ListeningProcessId {
    param([int] $Port)

    $connection = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($connection) { return [int] $connection.OwningProcess }
    return $null
}

# Lee los PIDs guardados; devuelve solo los que siguen vivos y son el mismo proceso (mismo inicio).
function Get-TrackedProcesses {
    $tracked = @()
    if (-not (Test-Path -LiteralPath $script:PidFilePath)) { return $tracked }

    # Sin @(...) alrededor de la tuberia: en PowerShell 5.1 ConvertFrom-Json entrega el arreglo como un solo objeto.
    $entries = Get-Content -LiteralPath $script:PidFilePath -Raw | ConvertFrom-Json
    foreach ($entry in $entries) {
        $process = Get-Process -Id $entry.pid -ErrorAction SilentlyContinue
        $isSameProcess = $process -and
            ($process.StartTime.ToUniversalTime().ToString('o') -eq $entry.startTime)
        $tracked += [pscustomobject]@{
            Name      = $entry.name
            ProcessId = [int] $entry.pid
            Port      = [int] $entry.port
            IsAlive   = [bool] $isSameProcess
        }
    }
    return $tracked
}

function Save-TrackedProcesses {
    param($Entries)

    New-Item -ItemType Directory -Force -Path $script:RunDirectory | Out-Null
    ConvertTo-Json -InputObject @($Entries) | Set-Content -LiteralPath $script:PidFilePath -Encoding UTF8
}

# Ejecuta un script de shell dentro del contenedor postgres-db. El script viaja en base64 porque PowerShell 5.1
# pierde las comillas dobles de los argumentos nativos y agrega un BOM al stdin.
function Invoke-PostgresShell {
    param([string] $EnvFilePath, [string] $ShellScript)

    $encodedScript = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($ShellScript))
    $composeArguments = @('compose', '--project-directory', $script:RepositoryRoot, '--env-file', $EnvFilePath, 'exec', '-T', 'postgres-db',
        'sh', '-c', "echo $encodedScript | base64 -d | sh")
    return Get-NativeOutput -FilePath 'docker' -Arguments $composeArguments
}

# Copia un .sql al contenedor y lo ejecuta con psql (evita tambien los problemas de codificacion de las tuberias).
function Invoke-DatabaseScript {
    param(
        [string] $EnvFilePath,
        [string] $DatabasePrefix,   # AVAILABILITY | BOOKING | PAYMENT
        [string] $ScriptPath
    )

    $remotePath = "/tmp/$([IO.Path]::GetFileName($ScriptPath))"
    $copyExit = Invoke-Compose -EnvFilePath $EnvFilePath -Arguments @('cp', $ScriptPath, "postgres-db:$remotePath") -Quiet
    if ($copyExit -ne 0) { return $copyExit }

    $shellScript = 'PGPASSWORD="$' + $DatabasePrefix + '_DB_PASSWORD" exec psql -h 127.0.0.1 -U "$' + $DatabasePrefix +
        '_DB_USER" -d "$' + $DatabasePrefix + '_DB_NAME" -v ON_ERROR_STOP=1 -q -f ' + $remotePath
    $result = Invoke-PostgresShell -EnvFilePath $EnvFilePath -ShellScript $shellScript
    if ($result.ExitCode -ne 0) { Write-Host $result.Text -ForegroundColor DarkGray }
    return $result.ExitCode
}
