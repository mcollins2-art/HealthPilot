param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$ApiKey = "",
    [string]$FilePath,
    [int]$PollSeconds = 2
)

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "ApiKey is required."
}

if ([string]::IsNullOrWhiteSpace($FilePath)) {
    throw "FilePath is required."
}

$headers = @{ "X-API-Key" = $ApiKey }
$body = @{
    filePath = $FilePath
    batchSize = 5000
    resumeFromCheckpoint = $true
} | ConvertTo-Json

$started = Get-Date
$create = Invoke-RestMethod -Uri "$BaseUrl/ingestion/jobs" -Method Post -Headers $headers -ContentType "application/json" -Body $body
$jobId = $create.jobId

Write-Host "Queued job $jobId"

while ($true) {
    Start-Sleep -Seconds $PollSeconds
    $job = Invoke-RestMethod -Uri "$BaseUrl/ingestion/jobs/$jobId" -Headers $headers
    if ($job.status -eq "completed" -or $job.status -eq "dead_lettered") {
        $ended = Get-Date
        $seconds = [Math]::Max((New-TimeSpan -Start $started -End $ended).TotalSeconds, 0.001)
        $rows = [int]$job.rowsProcessed
        $rowsPerSecond = [Math]::Round($rows / $seconds, 2)
        [pscustomobject]@{
            jobId = $jobId
            status = $job.status
            rowsProcessed = $rows
            recordsReceived = $job.recordsReceived
            recordsSkipped = $job.recordsSkipped
            durationSeconds = [Math]::Round($seconds, 2)
            rowsPerSecond = $rowsPerSecond
            attemptCount = $job.attemptCount
            fileHashSha256 = $job.fileHashSha256
            parserVersion = $job.parserVersion
        } | Format-List
        break
    }
}
