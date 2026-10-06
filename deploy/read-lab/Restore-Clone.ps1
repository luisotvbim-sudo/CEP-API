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
    throw 'Stop the local read lab API before restoring a clone.'
}
$tableCount = docker exec $container psql -U cep_read_lab -d cep_read_lab -At -c "SELECT count(*) FROM pg_tables WHERE schemaname = 'public'"
if ($LASTEXITCODE -ne 0 -or $tableCount -ne '0') {
    throw 'Restore requires a fresh empty lab database volume.'
}

$start = [Diagnostics.ProcessStartInfo]::new('docker')
foreach ($arg in @('exec', '-i', $container, 'pg_restore', '-U', 'cep_read_lab', '-d', 'cep_read_lab', '--no-owner', '--no-acl')) {
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
    $source = [IO.File]::OpenRead($dump)
    try { $source.CopyTo($process.StandardInput.BaseStream) }
    finally { $source.Dispose(); $process.StandardInput.Close() }
    $process.WaitForExit()
    [void] $stdoutTask.GetAwaiter().GetResult()
    [void] $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw 'Local PostgreSQL restore failed.' }
}
finally { $process.Dispose() }

Write-Output 'Clone restored to the fresh local read lab database.'
