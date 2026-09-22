# Requires PowerShell 7 and Docker Compose. Uses disposable local data and loopback HTTPS only.
[CmdletBinding()]
param([string]$ImageTag = 'security-review')
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$testId = [Guid]::NewGuid().ToString('N').Substring(0, 10)
$projectName = "cep-smoke-$testId"
$testPath = Join-Path $repoPath ".local/$projectName"
New-Item -ItemType Directory -Path $testPath -Force | Out-Null
function Write-TestSecret([string]$Name, [string]$Value) {
    [IO.File]::WriteAllText((Join-Path $testPath $Name), $Value, [Text.UTF8Encoding]::new($false))
}
function New-TestPassword { [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) }
$ownerPassword = New-TestPassword
$runtimePassword = New-TestPassword
$adminPassword = New-TestPassword
Write-TestSecret 'postgres_password' (New-TestPassword)
Write-TestSecret 'owner_password' $ownerPassword
Write-TestSecret 'runtime_password' $runtimePassword
Write-TestSecret 'runtime_connection' "Host=postgres;Database=cep_api;Username=cep_api_runtime;Password=$runtimePassword"
Write-TestSecret 'migration_connection' "Host=postgres;Database=cep_api;Username=cep_api_owner;Password=$ownerPassword"
Write-TestSecret 'bootstrap_email' 'smoke@example.test'
Write-TestSecret 'bootstrap_password' $adminPassword
Write-TestSecret 'smtp_username' ''
Write-TestSecret 'smtp_password' ''
Write-TestSecret 'monday_token' 'disabled-smoke-token'
Write-TestSecret 'vr_mais_token' 'disabled-smoke-token'
Write-TestSecret 'security-code-hmac' ([Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)))
$rsa = [Security.Cryptography.RSA]::Create(3072)
try { Write-TestSecret 'jwt-private.pem' $rsa.ExportRSAPrivateKeyPem() } finally { $rsa.Dispose() }
$tlsRsa = [Security.Cryptography.RSA]::Create(2048)
try {
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=localhost', $tlsRsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-1), [DateTimeOffset]::UtcNow.AddDays(1))
    Write-TestSecret 'origin-certificate.pem' $certificate.ExportCertificatePem()
    Write-TestSecret 'origin-private.key' $tlsRsa.ExportRSAPrivateKeyPem()
    Write-TestSecret 'frontend-origin-certificate.pem' $certificate.ExportCertificatePem()
    Write-TestSecret 'frontend-origin-private.key' $tlsRsa.ExportRSAPrivateKeyPem()
    $certificate.Dispose()
} finally { $tlsRsa.Dispose() }
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$httpsPort = $listener.LocalEndpoint.Port
$listener.Stop()
$relativeSecrets = "./.local/$projectName"
Write-TestSecret 'test.env' @"
API_DOMAIN=localhost
FRONTEND_DOMAIN=cep.lat
API_IMAGE_TAG=$ImageTag
JWT_KEY_ID=smoke-$testId
SMTP_HOST=smtp.invalid
SMTP_PORT=587
SMTP_FROM=no-reply@example.test
SECRETS_DIR=$relativeSecrets
"@
Write-TestSecret 'override.yaml' @"
services:
  front:
    image: nginx:stable-alpine
    command: ["sh", "-c", "printf 'server { listen 8080; location / { return 200 front-smoke-ok; } }\\n' > /etc/nginx/conf.d/default.conf && exec nginx -g 'daemon off;'"]
    networks:
      frontend:
        aliases: [cep-front]
  nginx:
    ports: !override ["127.0.0.1:${httpsPort}:443"]
  migrate:
    secrets:
      - source: bootstrap_email
        target: BootstrapAdmin__Email
      - source: bootstrap_password
        target: BootstrapAdmin__Password
secrets:
  bootstrap_email: {file: '$relativeSecrets/bootstrap_email'}
  bootstrap_password: {file: '$relativeSecrets/bootstrap_password'}
"@
$composeArgs = @('compose', '-p', $projectName, '--env-file', (Join-Path $testPath 'test.env'), '-f', (Join-Path $repoPath 'compose.production.yaml'), '-f', (Join-Path $testPath 'override.yaml'))
function Invoke-TestCompose {
    & docker @composeArgs @args
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose smoke-test command failed.' }
}
try {
    Invoke-TestCompose up -d --no-build --wait postgres
    Invoke-TestCompose run --rm --no-deps migrate migrate
    Invoke-TestCompose exec -T postgres psql -U postgres -d cep_api -v ON_ERROR_STOP=1 -c "INSERT INTO allowed_email_domains (domain) VALUES ('example.test');"
    Invoke-TestCompose run --rm --no-deps migrate bootstrap-admin
    Invoke-TestCompose up -d --no-build --wait --wait-timeout 90 api front nginx
    $baseUrl = "https://localhost:$httpsPort"
    $health = Invoke-WebRequest "$baseUrl/health/ready" -SkipCertificateCheck
    if ($health.StatusCode -ne 200) { throw 'Readiness failed.' }
    $login = @{ email = 'smoke@example.test'; password = $adminPassword; client = @{ type = 'smoke' } } | ConvertTo-Json
    $tokens = Invoke-RestMethod "$baseUrl/api/v1/auth/login" -Method Post -ContentType application/json -Body $login -SkipCertificateCheck
    $headers = @{ Authorization = "Bearer $($tokens.accessToken)" }
    $me = Invoke-RestMethod "$baseUrl/api/v1/me" -Headers $headers -SkipCertificateCheck
    if ($me.email -ne 'smoke@example.test') { throw 'Authentication failed.' }
    $webHeaders = @{ Host = "cep.lat:$httpsPort"; Origin = "https://cep.lat:$httpsPort"; 'X-CEP-Web-Session' = '1'; 'Sec-Fetch-Site' = 'same-origin' }
    $webTokens = Invoke-RestMethod "$baseUrl/api/v1/auth/web/login" -Method Post -ContentType application/json -Body $login -Headers $webHeaders -SessionVariable webSession -SkipCertificateCheck
    if ($webTokens.PSObject.Properties.Name -contains 'refreshToken') { throw 'Browser response exposed refresh token.' }
    $webCookie = @($webSession.Cookies.GetCookies([Uri]$baseUrl) | Where-Object Name -eq '__Host-cep-session')
    if ($webCookie.Count -ne 1 -or !$webCookie[0].HttpOnly -or !$webCookie[0].Secure) { throw 'Secure browser cookie missing.' }
    $frontHeaders = @{ Host = 'cep.lat' }
    $front = Invoke-WebRequest "$baseUrl/" -Headers $frontHeaders -SkipCertificateCheck
    if ($front.StatusCode -ne 200) { throw 'Front virtual host failed.' }
    $frontApiHeaders = @{ Host = 'cep.lat'; Authorization = "Bearer $($tokens.accessToken)" }
    $frontMe = Invoke-RestMethod "$baseUrl/api/v1/me" -Headers $frontApiHeaders -SkipCertificateCheck
    if ($frontMe.email -ne $me.email) { throw 'Same-origin front API proxy failed.' }
    $jwksBefore = Invoke-RestMethod "$baseUrl/.well-known/jwks.json" -SkipCertificateCheck
    Invoke-TestCompose restart api
    # Wait using the same Docker healthcheck used in production.
    Invoke-TestCompose up -d --no-build --wait --wait-timeout 90 api
    $meAfter = Invoke-RestMethod "$baseUrl/api/v1/me" -Headers $headers -SkipCertificateCheck
    $jwksAfter = Invoke-RestMethod "$baseUrl/.well-known/jwks.json" -SkipCertificateCheck
    if ($meAfter.email -ne $me.email -or $jwksBefore.keys[0].n -ne $jwksAfter.keys[0].n) { throw 'Identity or signing key changed on restart.' }
    $webRestored = Invoke-RestMethod "$baseUrl/api/v1/auth/web/refresh" -Method Post -ContentType application/json -Body '{}' -Headers $webHeaders -WebSession $webSession -SkipCertificateCheck
    if ($webRestored.user.email -ne $me.email -or ([DateTimeOffset]$webRestored.sessionExpiresAt - [DateTimeOffset]$webTokens.sessionExpiresAt).Duration().TotalMilliseconds -gt 1) { throw 'Browser session failed to survive restart or extended its deadline.' }
    Invoke-WebRequest "$baseUrl/api/v1/auth/web/logout" -Method Post -ContentType application/json -Body '{}' -Headers $webHeaders -WebSession $webSession -SkipCertificateCheck | Out-Null
    $webRevoked = Invoke-WebRequest "$baseUrl/api/v1/me" -Headers @{ Authorization = "Bearer $($webRestored.accessToken)" } -SkipCertificateCheck -SkipHttpErrorCheck
    if ($webRevoked.StatusCode -ne 401) { throw 'Browser bearer remained valid after logout.' }
    $swagger = Invoke-WebRequest "$baseUrl/swagger/v1/swagger.json" -SkipCertificateCheck -SkipHttpErrorCheck
    if ($swagger.StatusCode -ne 404) { throw 'Swagger must be disabled in Production.' }
    $logout = @{ refreshToken = $tokens.refreshToken } | ConvertTo-Json
    Invoke-WebRequest "$baseUrl/api/v1/auth/logout" -Method Post -ContentType application/json -Body $logout -SkipCertificateCheck | Out-Null
    $revoked = Invoke-WebRequest "$baseUrl/api/v1/me" -Headers $headers -SkipCertificateCheck -SkipHttpErrorCheck
    if ($revoked.StatusCode -ne 401) { throw 'Bearer remained valid after logout.' }
    Write-Output 'PASS: PostgreSQL roles/migrations, non-root API, Nginx HTTPS, native/browser login, durable keys/session after restart, fixed browser deadline, Swagger disabled, logout revocation.'

} finally {
    # Only removes resources in this randomly named test project, including its disposable volumes.
    & docker @composeArgs down --volumes --remove-orphans
    # Remove only the secret files created by this test; never recursively delete a computed path.
    foreach ($name in @('postgres_password','owner_password','runtime_password','runtime_connection','migration_connection',
        'bootstrap_email','bootstrap_password','smtp_username','smtp_password','monday_token','vr_mais_token','security-code-hmac','jwt-private.pem','origin-certificate.pem','origin-private.key','frontend-origin-certificate.pem','frontend-origin-private.key')) {
        Remove-Item -LiteralPath (Join-Path $testPath $name) -ErrorAction SilentlyContinue
    }
}
