param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$secretDir = Join-Path $root '.local/read-lab/secrets'
New-Item -ItemType Directory -Force -Path $secretDir | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)

function Write-IfMissing([string] $name, [string] $value) {
    $path = Join-Path $secretDir $name
    if (-not (Test-Path -LiteralPath $path)) {
        [IO.File]::WriteAllText($path, $value, $utf8)
    }
}

function New-Hex([int] $bytes) {
    return [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes($bytes)).ToLowerInvariant()
}

Write-IfMissing 'postgres_password' (New-Hex 24)
$postgresPassword = [IO.File]::ReadAllText((Join-Path $secretDir 'postgres_password'))
Write-IfMissing 'database_connection' "Host=postgres;Port=5432;Database=cep_read_lab;Username=cep_read_lab;Password=$postgresPassword"
Write-IfMissing 'security-code-hmac' ([Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48)))

$keyPath = Join-Path $secretDir 'jwt-private.pem'
if (-not (Test-Path -LiteralPath $keyPath)) {
    $rsa = [Security.Cryptography.RSA]::Create(3072)
    try { [IO.File]::WriteAllText($keyPath, $rsa.ExportPkcs8PrivateKeyPem(), $utf8) }
    finally { $rsa.Dispose() }
}

Write-Output 'Private lab secrets initialized; contents withheld.'
