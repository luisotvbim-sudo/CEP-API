param(
    [string] $DumpPath = (Join-Path $PSScriptRoot '../../.local/read-lab/production-clone.dump')
)

$ErrorActionPreference = 'Stop'
$dump = (Resolve-Path -LiteralPath $DumpPath).Path
$container = 'cep-real-read-lab-postgres-1'
$inspect = docker inspect $container | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $inspect[0].Config.Labels.'com.docker.compose.project' -ne 'cep-real-read-lab') {
    throw 'The local read lab PostgreSQL container was not found.'
}
$apiState = docker inspect --format '{{.State.Running}}' cep-real-read-lab-api-1 2>$null
if ($LASTEXITCODE -eq 0 -and $apiState -eq 'true') {
    throw 'Stop the local read lab API before restoring original login fields.'
}

function Invoke-PrivateProcess([string] $program, [string[]] $arguments, [string] $inputText) {
    $start = [Diagnostics.ProcessStartInfo]::new($program)
    foreach ($arg in $arguments) { [void] $start.ArgumentList.Add($arg) }
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        if ($null -ne $inputText) { $process.StandardInput.Write($inputText) }
        $process.StandardInput.Close()
        $process.WaitForExit()
        $output = $outputTask.GetAwaiter().GetResult()
        [void] $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw 'Private lab database operation failed.' }
        return $output
    }
    finally { $process.Dispose() }
}

$dumpSql = Invoke-PrivateProcess 'docker' @(
    'run', '--rm', '--network', 'none', '--mount', "type=bind,source=$dump,target=/backup,readonly",
    'postgres:17-alpine', 'pg_restore', '--data-only', '--table=users',
    '--no-owner', '--no-acl', '-f', '-', '/backup') $null
$blocks = [regex]::Matches($dumpSql, '(?ms)^COPY public\.users \([^\r\n]+\) FROM stdin;\r?\n.*?^\\\.\r?\n')
if ($blocks.Count -ne 1) { throw 'The private dump does not contain one users COPY block.' }
$copy = [regex]::Replace($blocks[0].Value, '^COPY public\.users ', 'COPY original_users ')
$dumpSql = $null

$prefix = @'
BEGIN;
CREATE TEMP TABLE original_users AS SELECT * FROM users WITH NO DATA;
'@
$suffix = @'
DO $check$ BEGIN
  IF (SELECT count(*) FROM original_users) <> 2
     OR (SELECT count(*) FROM users) <> 2
     OR EXISTS (SELECT 1 FROM original_users WHERE "PasswordHash" IS NULL)
     OR EXISTS (
       SELECT 1 FROM users u FULL JOIN original_users b ON u."Id" = b."Id"
       WHERE u."Id" IS NULL OR b."Id" IS NULL OR u."Role" <> b."Role"
     )
     OR EXISTS (SELECT 1 FROM users WHERE "PasswordHash" IS NOT NULL)
     OR EXISTS (SELECT 1 FROM refresh_sessions)
     OR EXISTS (SELECT 1 FROM email_outbox)
     OR EXISTS (SELECT 1 FROM invitations)
     OR EXISTS (SELECT 1 FROM password_resets)
     OR EXISTS (SELECT 1 FROM time_control.power_pin_configuration)
     OR EXISTS (SELECT 1 FROM time_control.power_action_overrides)
     OR EXISTS (SELECT 1 FROM time_control.notification_dispatches)
     OR EXISTS (SELECT 1 FROM time_control.app_settings WHERE "AutomaticEnabled")
     OR EXISTS (
       SELECT 1 FROM workforce_people p
       WHERE p."UserId" IS NOT NULL
         AND NOT EXISTS (SELECT 1 FROM original_users b WHERE b."Id" = p."UserId")
     ) THEN
    RAISE EXCEPTION 'Clone account identity or neutralization check failed';
  END IF;
END $check$;

DO $restore$ DECLARE updated integer; BEGIN
  UPDATE users u SET
    "Email" = b."Email",
    "NormalizedEmail" = b."NormalizedEmail",
    "UserName" = b."UserName",
    "NormalizedUserName" = b."NormalizedUserName",
    "DisplayName" = b."DisplayName",
    "PasswordHash" = b."PasswordHash"
  FROM original_users b
  WHERE u."Id" = b."Id" AND u."Role" = b."Role";
  GET DIAGNOSTICS updated = ROW_COUNT;
  IF updated <> 2 THEN RAISE EXCEPTION 'Unexpected account update count'; END IF;
END $restore$;

DO $verify$ BEGIN
  IF EXISTS (
    SELECT 1 FROM users u JOIN original_users b ON u."Id" = b."Id"
    WHERE u."Email" IS DISTINCT FROM b."Email"
       OR u."NormalizedEmail" IS DISTINCT FROM b."NormalizedEmail"
       OR u."UserName" IS DISTINCT FROM b."UserName"
       OR u."NormalizedUserName" IS DISTINCT FROM b."NormalizedUserName"
       OR u."DisplayName" IS DISTINCT FROM b."DisplayName"
       OR u."PasswordHash" IS DISTINCT FROM b."PasswordHash"
       OR u."SecurityStamp" IS NOT DISTINCT FROM b."SecurityStamp"
  ) OR EXISTS (
    SELECT 1 FROM workforce_people p
    WHERE p."UserId" IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM users u WHERE u."Id" = p."UserId")
  ) THEN
    RAISE EXCEPTION 'Clone login comparison or workforce link check failed';
  END IF;
END $verify$;
COMMIT;
'@

$sql = $prefix + "`n" + $copy + "`n" + $suffix
[void] (Invoke-PrivateProcess 'docker' @(
    'exec', '-i', $container, 'psql', '-X', '-q', '-v', 'ON_ERROR_STOP=1',
    '-U', 'cep_read_lab', '-d', 'cep_read_lab') $sql)
$sql = $null
$copy = $null
Write-Output 'Two original login records matched by ID and role; six fields restored. Session and workforce checks passed.'
