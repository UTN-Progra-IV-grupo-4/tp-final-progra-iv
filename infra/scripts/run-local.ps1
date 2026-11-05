<#
.SYNOPSIS
    Levanta todo el sistema en local con un solo comando: PostgreSQL y Keycloak en Docker,
    migraciones, datos de demo y los tres servicios .NET en segundo plano.

.DESCRIPTION
    Lee un archivo .env, valida las variables obligatorias, levanta postgres-db y keycloak con
    docker compose, aplica las migraciones de Availability, Booking y Payment, carga el catalogo de
    demo y arranca Payment, Booking y Availability con la autenticacion entre servicios activada.
    Guarda los PIDs en .run/pids.json; stop-local.ps1 los usa para detenerlos.
    Se puede ejecutar varias veces: lo que ya esta corriendo no se vuelve a levantar.

.PARAMETER EnvFile
    Ruta del .env. Por defecto, el .env de la raiz del repositorio.

.PARAMETER Reset
    Detiene los servicios, vacia el estado de reservas (conserva el catalogo) y los vuelve a levantar.
    No vacia el broker: revisa que la suscripcion de Azure Service Bus este en cero.

.EXAMPLE
    ./infra/scripts/run-local.ps1
    ./infra/scripts/run-local.ps1 -EnvFile C:\ruta\mi.env -Reset
#>
[CmdletBinding()]
param(
    [string] $EnvFile,
    [switch] $Reset
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'local-common.ps1')

$totalStopwatch = [Diagnostics.Stopwatch]::StartNew()
$root = $script:RepositoryRoot
if (-not $EnvFile) { $EnvFile = Join-Path $root '.env' }

# 1. Validacion del .env ------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $EnvFile)) {
    Stop-WithError "No existe el archivo '$EnvFile'. Crealo con: Copy-Item .env.example .env y completa los valores (ver docs/running-locally.md, seccion 2)."
}
$EnvFile = (Resolve-Path -LiteralPath $EnvFile).Path
$environment = Read-EnvironmentFile -Path $EnvFile

$requiredVariables = @(
    'KEYCLOAK_ADMIN', 'KEYCLOAK_ADMIN_PASSWORD',
    'KEYCLOAK_AVAILABILITY_CLIENT_SECRET', 'KEYCLOAK_BOOKING_CLIENT_SECRET', 'KEYCLOAK_PAYMENT_CLIENT_SECRET',
    'AVAILABILITY_DB_NAME', 'AVAILABILITY_DB_USER', 'AVAILABILITY_DB_PASSWORD',
    'BOOKING_DB_NAME', 'BOOKING_DB_USER', 'BOOKING_DB_PASSWORD',
    'PAYMENT_DB_NAME', 'PAYMENT_DB_USER', 'PAYMENT_DB_PASSWORD',
    'AZURE_SERVICE_BUS_CONNECTION_STRING'
)
$missingVariables = @($requiredVariables | Where-Object { -not $environment.ContainsKey($_) -or [string]::IsNullOrWhiteSpace($environment[$_]) })
if ($missingVariables.Count -gt 0) {
    Write-Host "ERROR: faltan variables obligatorias en '$EnvFile':" -ForegroundColor Red
    $missingVariables | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host "Completalas (hay una plantilla en .env.example) y volve a ejecutar. No se hizo ningun cambio." -ForegroundColor Red
    exit 1
}

function Get-EnvironmentValue {
    param([string] $Name, [string] $Default = '')
    if ($environment.ContainsKey($Name) -and -not [string]::IsNullOrWhiteSpace($environment[$Name])) { return $environment[$Name] }
    return $Default
}

$postgresPort = Get-EnvironmentValue 'POSTGRES_PORT' '5432'
$keycloakPort = Get-EnvironmentValue 'KEYCLOAK_PORT' '8080'
foreach ($portVariable in @(@('POSTGRES_PORT', $postgresPort), @('KEYCLOAK_PORT', $keycloakPort))) {
    if ($portVariable[1] -notmatch '^\d{1,5}$') { Stop-WithError "$($portVariable[0]) debe ser un numero de puerto (valor actual: '$($portVariable[1])')." }
}
$keycloakRealmUrl = "http://localhost:$keycloakPort/realms/seat-reservation"

# 2. Requisitos de la maquina -------------------------------------------------------------------
Write-Step 'Comprobando requisitos (Docker, .NET SDK, puertos)'
$dockerCheck = Get-NativeOutput -FilePath 'docker' -Arguments @('info', '--format', '{{.ServerVersion}}')
if ($dockerCheck.ExitCode -ne 0) { Stop-WithError 'Docker no responde. Abri Docker Desktop, espera a que termine de iniciar y volve a ejecutar.' }
$dotnetCheck = Get-NativeOutput -FilePath 'dotnet' -Arguments @('--version')
if ($dotnetCheck.ExitCode -ne 0) { Stop-WithError 'No se encontro el SDK de .NET. Instala el SDK indicado en global.json (10.x).' }

$tracked = @(Get-TrackedProcesses)
if ($Reset) {
    Write-Step 'Reset: deteniendo los servicios antes de vaciar el estado'
    foreach ($entry in $tracked | Where-Object { $_.IsAlive }) {
        Stop-Process -Id $entry.ProcessId -Force
        Write-Host "    detenido $($entry.Name) (PID $($entry.ProcessId))"
    }
    if (Test-Path -LiteralPath $script:PidFilePath) { Remove-Item -LiteralPath $script:PidFilePath }
    $tracked = @()
}

$trackedByName = @{}
foreach ($entry in $tracked | Where-Object { $_.IsAlive }) { $trackedByName[$entry.Name] = $entry }
foreach ($service in $script:Services) {
    if ($trackedByName.ContainsKey($service.Name)) { continue }
    $ownerId = Get-ListeningProcessId -Port $service.Port
    if ($ownerId) {
        $ownerName = (Get-Process -Id $ownerId -ErrorAction SilentlyContinue).ProcessName
        Stop-WithError "El puerto $($service.Port) ($($service.Name)) esta ocupado por el proceso $ownerName (PID $ownerId), que no es de este script. Cerralo vos y volve a ejecutar; el script no toca procesos ajenos."
    }
}

# 3. Infraestructura ----------------------------------------------------------------------------
Write-Step 'Levantando PostgreSQL y Keycloak (docker compose)'
$composeExit = Invoke-Compose -EnvFilePath $EnvFile -Arguments @('up', '-d', 'postgres-db', 'keycloak') -Quiet
if ($composeExit -ne 0) { Stop-WithError 'docker compose up fallo (arriba esta su salida). Causas comunes: puerto 5432/8080 ocupado (cambia POSTGRES_PORT/KEYCLOAK_PORT) o volumen viejo con otras credenciales (docker compose down -v).' }

Write-Step 'Esperando a PostgreSQL'
$postgresReady = $false
for ($attempt = 0; $attempt -lt 60 -and -not $postgresReady; $attempt++) {
    # Con -h 127.0.0.1 solo responde el servidor definitivo (el temporal de inicializacion escucha unicamente por socket).
    $probe = Invoke-PostgresShell -EnvFilePath $EnvFile -ShellScript 'PGPASSWORD="$AVAILABILITY_DB_PASSWORD" psql -h 127.0.0.1 -U "$AVAILABILITY_DB_USER" -d "$AVAILABILITY_DB_NAME" -tAc "select 1"'
    if ($probe.ExitCode -eq 0 -and $probe.Text.Trim() -eq '1') { $postgresReady = $true } else { Start-Sleep -Seconds 1 }
}
if (-not $postgresReady) { Stop-WithError 'PostgreSQL no quedo listo en 60 s. Mira: docker compose logs postgres-db' }

Write-Step 'Esperando a Keycloak (importa el realm seat-reservation)'
$keycloakReady = $false
for ($attempt = 0; $attempt -lt 120 -and -not $keycloakReady; $attempt++) {
    try {
        $discovery = Invoke-WebRequest -UseBasicParsing -Uri "$keycloakRealmUrl/.well-known/openid-configuration" -TimeoutSec 3
        if ($discovery.StatusCode -eq 200) { $keycloakReady = $true }
    } catch { Start-Sleep -Seconds 1 }
}
if (-not $keycloakReady) { Stop-WithError "Keycloak no publico el realm en 120 s ($keycloakRealmUrl). Mira: docker compose logs keycloak" }

# 4. Proveedor Google (solo si hay credenciales) ------------------------------------------------
if (-not [string]::IsNullOrWhiteSpace((Get-EnvironmentValue 'GOOGLE_CLIENT_ID'))) {
    Write-Step 'Habilitando el proveedor de identidad Google en Keycloak'
    try {
        $tokenResponse = Invoke-RestMethod -Method Post -Uri "http://localhost:$keycloakPort/realms/master/protocol/openid-connect/token" -Body @{
            grant_type = 'password'; client_id = 'admin-cli'
            username   = $environment['KEYCLOAK_ADMIN']; password = $environment['KEYCLOAK_ADMIN_PASSWORD']
        }
        $adminHeaders = @{ Authorization = "Bearer $($tokenResponse.access_token)" }
        $providerUrl = "http://localhost:$keycloakPort/admin/realms/seat-reservation/identity-provider/instances/google"
        $provider = Invoke-RestMethod -Method Get -Uri $providerUrl -Headers $adminHeaders
        if ($provider.enabled) {
            Write-Host '    Google ya estaba habilitado.'
        } else {
            # Keycloak devuelve el secreto enmascarado y lo conserva al guardar, asi que no se toca ni se imprime.
            $provider.enabled = $true
            Invoke-RestMethod -Method Put -Uri $providerUrl -Headers $adminHeaders -ContentType 'application/json' `
                -Body ([Text.Encoding]::UTF8.GetBytes(($provider | ConvertTo-Json -Depth 10))) | Out-Null
            Write-Host '    Google habilitado.'
        }
    } catch {
        Write-Host "    AVISO: no se pudo habilitar Google ($($_.Exception.Message)). El sistema sigue; el login con usuario y contrasena funciona." -ForegroundColor Yellow
    }
}

# 5. Compilacion, migraciones y datos de demo ---------------------------------------------------
Write-Step 'Compilando los servicios (dotnet build)'
foreach ($service in $script:Services) {
    $projectFile = Join-Path $root "$($service.ProjectDirectory)\$($service.Assembly).csproj"
    $buildExit = Invoke-Native -FilePath 'dotnet' -Arguments @('build', $projectFile, '--nologo', '-v', 'q', '-clp:ErrorsOnly') -Quiet
    if ($buildExit -ne 0) { Stop-WithError "No compilo $($service.Assembly) (arriba esta el error)." }
}

Write-Step 'Aplicando migraciones (dotnet ef)'
Push-Location $root
try {
    $toolExit = Invoke-Native -FilePath 'dotnet' -Arguments @('tool', 'restore') -Quiet
    if ($toolExit -ne 0) { Stop-WithError 'dotnet tool restore fallo (necesita acceso a NuGet la primera vez).' }

    $connectionStrings = @{}
    foreach ($prefix in @('AVAILABILITY', 'BOOKING', 'PAYMENT')) {
        $connectionStrings[$prefix] = "Host=localhost;Port=$postgresPort;Database=$($environment["${prefix}_DB_NAME"]);Username=$($environment["${prefix}_DB_USER"]);Password=$($environment["${prefix}_DB_PASSWORD"])"
    }

    $migrations = @(
        @{ Variable = 'ConnectionStrings__Availability'; Prefix = 'AVAILABILITY'; Arguments = @('--project', 'src/Availability/Availability.Infrastructure') },
        @{ Variable = 'ConnectionStrings__Booking';      Prefix = 'BOOKING';      Arguments = @('--project', 'src/Booking/Booking.Data', '--startup-project', 'src/Booking/Booking.Api') },
        @{ Variable = 'ConnectionStrings__Payment';      Prefix = 'PAYMENT';      Arguments = @('--project', 'src/Payment/Payment.Data', '--startup-project', 'src/Payment/Payment.Grpc') }
    )
    foreach ($migration in $migrations) {
        Set-Item -Path "Env:$($migration.Variable)" -Value $connectionStrings[$migration.Prefix]
        try {
            $migrationExit = Invoke-Native -FilePath 'dotnet' -Arguments (@('ef', 'database', 'update', '--no-build') + $migration.Arguments) -Quiet
        } finally {
            Remove-Item -Path "Env:$($migration.Variable)"
        }
        if ($migrationExit -ne 0) { Stop-WithError "Fallo la migracion de $($migration.Prefix.ToLower()) (arriba esta el error)." }
        Write-Host "    migracion $($migration.Prefix.ToLower()) aplicada"
    }
} finally {
    Pop-Location
}

if ($Reset) {
    Write-Step 'Reset: vaciando el estado de reservas (se conserva el catalogo)'
    foreach ($resetTarget in @(@('PAYMENT', 'payment.sql'), @('BOOKING', 'booking.sql'), @('AVAILABILITY', 'availability.sql'))) {
        $resetExit = Invoke-DatabaseScript -EnvFilePath $EnvFile -DatabasePrefix $resetTarget[0] -ScriptPath (Join-Path $root "infra\scripts\reset-demo-state\$($resetTarget[1])")
        if ($resetExit -ne 0) { Stop-WithError "Fallo el reset de $($resetTarget[0].ToLower())." }
    }
    Write-Host '    AVISO: el broker no se vacia. Verifica que la suscripcion de Azure Service Bus tenga 0 mensajes activos.' -ForegroundColor Yellow
}

Write-Step 'Cargando el catalogo de demo (se puede repetir sin duplicar)'
$seedExit = Invoke-DatabaseScript -EnvFilePath $EnvFile -DatabasePrefix 'AVAILABILITY' -ScriptPath (Join-Path $root 'src\Availability\Availability.Infrastructure\Persistence\Scripts\demo-data.sql')
if ($seedExit -ne 0) { Stop-WithError 'Fallo la carga de demo-data.sql.' }

# 6. Servicios en segundo plano -----------------------------------------------------------------
$brokerConnectionString = $environment['AZURE_SERVICE_BUS_CONNECTION_STRING']
$commonVariables = @{ ASPNETCORE_ENVIRONMENT = 'Development' }
$brokerVariables = @{
    Broker__Provider                          = 'AzureServiceBus'
    Broker__AzureServiceBus__Enabled          = 'true'
    Broker__AzureServiceBus__TopicName        = 'grupo4-booking-topic'
    Broker__AzureServiceBus__ConnectionString = $brokerConnectionString
}
$serviceVariables = @{
    payment      = @{
        ConnectionStrings__Payment           = $connectionStrings['PAYMENT']
        Authentication__Authority            = $keycloakRealmUrl
        Authentication__RequireHttpsMetadata = 'false'
    }
    booking      = @{
        ConnectionStrings__Booking           = $connectionStrings['BOOKING']
        Authentication__Authority            = $keycloakRealmUrl
        Authentication__RequireHttpsMetadata = 'false'
        Payment__Enabled                     = 'true'
        Payment__Address                     = 'http://localhost:5183'
        ServiceCredentials__Authority        = $keycloakRealmUrl
        ServiceCredentials__ClientId         = 'booking-service'
        ServiceCredentials__ClientSecret     = $environment['KEYCLOAK_BOOKING_CLIENT_SECRET']
        Broker__AzureServiceBus__SubscriptionName = 'booking'
    }
    availability = @{
        ConnectionStrings__Availability      = $connectionStrings['AVAILABILITY']
        Keycloak__Authority                  = $keycloakRealmUrl
        EventQueries__Source                 = 'Database'
        DevelopmentAuthentication__Enabled   = 'false'
        Booking__BaseUrl                     = 'http://localhost:5088'
        ServiceCredentials__Authority        = $keycloakRealmUrl
        ServiceCredentials__ClientId         = 'availability-service'
        ServiceCredentials__ClientSecret     = $environment['KEYCLOAK_AVAILABILITY_CLIENT_SECRET']
    }
}
$serviceVariables['booking'] += $brokerVariables
$serviceVariables['availability'] += $brokerVariables
foreach ($serviceName in @($serviceVariables.Keys)) { $serviceVariables[$serviceName] += $commonVariables }

New-Item -ItemType Directory -Force -Path $script:RunDirectory | Out-Null
$entries = @()
foreach ($service in $script:Services) {
    $existing = $trackedByName[$service.Name]
    if ($existing -and (Get-ListeningProcessId -Port $service.Port) -eq $existing.ProcessId) {
        Write-Step "$($service.Name) ya esta corriendo (PID $($existing.ProcessId)); no se vuelve a levantar"
        $entries += @{ name = $service.Name; pid = $existing.ProcessId; port = $service.Port
            startTime = (Get-Process -Id $existing.ProcessId).StartTime.ToUniversalTime().ToString('o') }
        continue
    }

    Write-Step "Arrancando $($service.Name) en el puerto $($service.Port)"
    $projectDirectory = Join-Path $root $service.ProjectDirectory
    $assemblyPath = Join-Path $projectDirectory "bin\Debug\net10.0\$($service.Assembly).dll"
    $outputLog = Join-Path $script:RunDirectory "$($service.Name).out.log"
    $errorLog = Join-Path $script:RunDirectory "$($service.Name).err.log"

    # Las variables se pasan por entorno (no por argumentos) para que los secretos no queden en la linea de comandos.
    $variables = $serviceVariables[$service.Name].Clone()
    $variables['ASPNETCORE_URLS'] = "http://localhost:$($service.Port)"
    $previousValues = @{}
    foreach ($name in $variables.Keys) {
        $previousValues[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $variables[$name], 'Process')
    }
    try {
        $process = Start-Process -FilePath 'dotnet' -ArgumentList ('"' + $assemblyPath + '"') -WorkingDirectory $projectDirectory `
            -RedirectStandardOutput $outputLog -RedirectStandardError $errorLog -WindowStyle Hidden -PassThru
    } finally {
        foreach ($name in $variables.Keys) { [Environment]::SetEnvironmentVariable($name, $previousValues[$name], 'Process') }
    }
    $entries += @{ name = $service.Name; pid = $process.Id; port = $service.Port
        startTime = $process.StartTime.ToUniversalTime().ToString('o') }
    Save-TrackedProcesses -Entries $entries

    $listening = $false
    for ($attempt = 0; $attempt -lt 180 -and -not $listening; $attempt++) {
        if ($process.HasExited) {
            Write-Host "    $($service.Name) termino con codigo $($process.ExitCode). Ultimas lineas de su log:" -ForegroundColor Red
            Get-Content -LiteralPath $outputLog, $errorLog -Tail 15 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
            Stop-WithError "$($service.Name) no pudo arrancar. Logs completos en $script:RunDirectory"
        }
        $logText = Get-Content -LiteralPath $outputLog -Raw -ErrorAction SilentlyContinue
        if ($logText -and $logText.Contains('Now listening on')) { $listening = $true } else { Start-Sleep -Milliseconds 500 }
    }
    if (-not $listening) { Stop-WithError "$($service.Name) no dijo 'Now listening on' en 90 s. Logs en $script:RunDirectory" }
}
Save-TrackedProcesses -Entries $entries

# 7. Resumen ------------------------------------------------------------------------------------
$totalStopwatch.Stop()
Write-Host ''
Write-Host ("Sistema listo en {0:N1} s" -f $totalStopwatch.Elapsed.TotalSeconds) -ForegroundColor Green
Write-Host '  Availability (web)  http://localhost:5212   <- entra por aca'
Write-Host '  Booking (REST)      http://localhost:5088'
Write-Host '  Payment (gRPC)      http://localhost:5183'
Write-Host "  Keycloak (admin)    http://localhost:$keycloakPort   (usuario: $($environment['KEYCLOAK_ADMIN']))"
Write-Host '  Usuarios de demo    user1 / User123!   y   user2 / User123!'
Write-Host '  PIDs                ' -NoNewline
Write-Host (($entries | ForEach-Object { "$($_.name)=$($_.pid)" }) -join '  ')
Write-Host "  Logs                $script:RunDirectory"
Write-Host '  Para apagar todo    ./infra/scripts/stop-local.ps1'
