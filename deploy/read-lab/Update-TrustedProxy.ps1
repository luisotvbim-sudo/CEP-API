param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$envPath = Join-Path $root '.local/read-lab/.env'
$edge = docker inspect cep-front-read-lab-edge-1 | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $edge[0].Config.Labels.'com.docker.compose.project' -ne 'cep-front-read-lab') {
    throw 'The expected local Front edge container was not found.'
}
$address = $edge[0].NetworkSettings.Networks.'cep-real-read-lab_default'.IPAddress
$parsed = $null
if (-not [Net.IPAddress]::TryParse($address, [ref] $parsed) -or
    $parsed.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) {
    throw 'The Front edge is not attached to the local read lab network.'
}
$lines = @([IO.File]::ReadAllLines($envPath))
$current = @($lines | Where-Object { $_ -match '^READ_LAB_PROXY_IP=' })
$api = docker inspect cep-real-read-lab-api-1 2>$null | ConvertFrom-Json
$runningAddress = if ($LASTEXITCODE -eq 0) {
    @($api[0].Config.Env | Where-Object { $_ -like 'ReverseProxy__KnownProxies__0=*' }) -replace '^ReverseProxy__KnownProxies__0=', ''
}
if ($current.Count -eq 1 -and $current[0] -eq "READ_LAB_PROXY_IP=$address" -and $runningAddress -eq $address) {
    Write-Output 'Trusted local proxy is already current.'
    return
}

$updated = @($lines | Where-Object { $_ -notmatch '^READ_LAB_PROXY_IP=' }) + "READ_LAB_PROXY_IP=$address"
[IO.File]::WriteAllLines($envPath, $updated, [Text.UTF8Encoding]::new($false))
Push-Location $root
try {
    docker compose --env-file .local/read-lab/.env -f deploy/read-lab/compose.yaml config -q
    if ($LASTEXITCODE -ne 0) { throw 'Read lab Compose validation failed.' }
    docker compose --env-file .local/read-lab/.env -f deploy/read-lab/compose.yaml up -d --no-deps --force-recreate --wait api
    if ($LASTEXITCODE -ne 0) { throw 'Read lab API recreation failed.' }
}
finally { Pop-Location }
Write-Output 'Trusted local proxy updated; only the read lab API was recreated.'
