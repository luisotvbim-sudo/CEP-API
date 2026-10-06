param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$secretDir = Join-Path $root '.local/read-lab/secrets'
New-Item -ItemType Directory -Force -Path $secretDir | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)

function Get-LocalPassword([string] $name) {
    $path = Join-Path $secretDir $name
    if (-not (Test-Path -LiteralPath $path)) {
        $value = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)).ToLowerInvariant()
        [IO.File]::WriteAllText($path, $value, $utf8)
    }
    return [IO.File]::ReadAllText($path)
}

function New-IdentityHash([string] $password) {
    $salt = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
    $subkey = [Security.Cryptography.Rfc2898DeriveBytes]::Pbkdf2(
        $password, $salt, 100000, [Security.Cryptography.HashAlgorithmName]::SHA512, 32)
    $bytes = [byte[]]::new(61)
    $bytes[0] = 1
    [Array]::Copy([byte[]](0, 0, 0, 2, 0, 1, 134, 160, 0, 0, 0, 16), 0, $bytes, 1, 12)
    [Array]::Copy($salt, 0, $bytes, 13, 16)
    [Array]::Copy($subkey, 0, $bytes, 29, 32)
    return [Convert]::ToBase64String($bytes)
}

$adminHash = New-IdentityHash (Get-LocalPassword 'admin-password')
$memberHash = New-IdentityHash (Get-LocalPassword 'member-password')
$sql = @'
BEGIN;
DO $check$ BEGIN
  IF (SELECT COUNT(*) FROM users WHERE "Role" = 'OrganizationAdmin') <> 1
     OR (SELECT COUNT(*) FROM users WHERE "Role" = 'User') <> 1
     OR (SELECT COUNT(*) FROM users) <> 2 THEN
    RAISE EXCEPTION 'Lab account cardinality differs from reviewed clone';
  END IF;
END $check$;
UPDATE users SET
  "Email" = 'admin@lab.invalid',
  "NormalizedEmail" = 'ADMIN@LAB.INVALID',
  "UserName" = 'admin@lab.invalid',
  "NormalizedUserName" = 'ADMIN@LAB.INVALID',
  "DisplayName" = 'Lab Admin',
  "PasswordHash" = '__ADMIN_HASH__',
  "SecurityStamp" = gen_random_uuid()::text,
  "Status" = 'Active',
  "EmailConfirmed" = true
WHERE "Role" = 'OrganizationAdmin';
UPDATE users SET
  "Email" = 'member@lab.invalid',
  "NormalizedEmail" = 'MEMBER@LAB.INVALID',
  "UserName" = 'member@lab.invalid',
  "NormalizedUserName" = 'MEMBER@LAB.INVALID',
  "DisplayName" = 'Lab Member',
  "PasswordHash" = '__MEMBER_HASH__',
  "SecurityStamp" = gen_random_uuid()::text,
  "Status" = 'Active',
  "EmailConfirmed" = true
WHERE "Role" = 'User';
COMMIT;
'@
$sql = $sql.Replace('__ADMIN_HASH__', $adminHash).Replace('__MEMBER_HASH__', $memberHash)

$start = [Diagnostics.ProcessStartInfo]::new('docker')
foreach ($arg in @('exec', '-i', 'cep-real-read-lab-postgres-1', 'psql', '-X', '-q', '-v', 'ON_ERROR_STOP=1', '-U', 'cep_read_lab', '-d', 'cep_read_lab')) {
    [void] $start.ArgumentList.Add($arg)
}
$start.UseShellExecute = $false
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($start)
try {
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.StandardInput.Write($sql)
    $process.StandardInput.Close()
    $process.WaitForExit()
    [void] $stdoutTask.GetAwaiter().GetResult()
    [void] $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw 'Local lab account setup failed.' }
}
finally { $process.Dispose() }

Write-Output 'Local lab accounts configured; passwords remain in ignored private files.'
