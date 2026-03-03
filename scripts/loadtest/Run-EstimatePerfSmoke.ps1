param(
	[string]$BaseUrl = "http://localhost:5000",
	[string]$ApiKey = "",
	[int]$WarmupRequests = 5,
	[int]$TotalRequests = 200,
	[int]$Concurrency = 20,
	[int]$P95ThresholdMs = 500,
	[double]$FailureRateThreshold = 0.01,
	[string]$OutFile = ".\scripts\loadtest\last-estimate-perf.json"
)

$ErrorActionPreference = "Stop"

if ($TotalRequests -lt 1) {
	throw "TotalRequests must be >= 1"
}

if ($Concurrency -lt 1) {
	throw "Concurrency must be >= 1"
}

if ($WarmupRequests -lt 0) {
	throw "WarmupRequests must be >= 0"
}

$requestBody = @{
	zipCode = "10001"
	insurer = "Insurer1"
	cptCode = "70001"
	deductibleRemaining = 1200
	coinsurancePercent = 20
	copay = 50
	oopMaxRemaining = 3000
	copayAppliesBeforeDeductible = $true
} | ConvertTo-Json

$headers = @{
	"Content-Type" = "application/json"
}

if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
	$headers["X-API-Key"] = $ApiKey
}

for ($warmup = 0; $warmup -lt $WarmupRequests; $warmup++) {
	try {
		Invoke-RestMethod -Uri "$BaseUrl/estimate" -Method Post -Headers $headers -Body $requestBody | Out-Null
	}
	catch {
		# Warm-up failures are ignored and excluded from measured thresholds.
	}
}

$durations = New-Object System.Collections.Generic.List[double]
$failures = 0
$completed = 0

$jobs = @()
$jobStarter = if (Get-Command Start-ThreadJob -ErrorAction SilentlyContinue) { "Start-ThreadJob" } else { "Start-Job" }

for ($worker = 0; $worker -lt $Concurrency; $worker++) {
	$jobs += & $jobStarter -ScriptBlock {
		param($BaseUrl, $headers, $requestBody, $workerId, $concurrency, $totalRequests)

		$localDurations = New-Object System.Collections.Generic.List[double]
		$localFailures = 0
		$localCompleted = 0

		for ($i = $workerId; $i -lt $totalRequests; $i += $concurrency) {
			$sw = [System.Diagnostics.Stopwatch]::StartNew()
			try {
				$resp = Invoke-RestMethod -Uri "$BaseUrl/estimate" -Method Post -Headers $headers -Body $requestBody
				$sw.Stop()
				if ($null -eq $resp.negotiatedRateRange) {
					$localFailures++
				}
				else {
					$localDurations.Add($sw.Elapsed.TotalMilliseconds)
				}
			}
			catch {
				$sw.Stop()
				$localFailures++
			}
			$localCompleted++
		}

		return [PSCustomObject]@{
			Durations = $localDurations
			Failures = $localFailures
			Completed = $localCompleted
		}
	} -ArgumentList $BaseUrl, $headers, $requestBody, $worker, $Concurrency, $TotalRequests
}

foreach ($job in $jobs) {
	$result = Receive-Job -Job $job -Wait
	Remove-Job -Job $job | Out-Null

	foreach ($d in $result.Durations) {
		$durations.Add([double]$d)
	}

	$failures += [int]$result.Failures
	$completed += [int]$result.Completed
}

if ($completed -eq 0) {
	throw "No requests were executed."
}

$sorted = $durations | Sort-Object
$count = $sorted.Count

function Get-Percentile([double[]]$values, [double]$percentile) {
	if ($values.Length -eq 0) { return [double]::NaN }

	$rank = [Math]::Ceiling(($percentile / 100.0) * $values.Length)
	$index = [Math]::Max([Math]::Min($rank - 1, $values.Length - 1), 0)
	return $values[$index]
}

$avg = if ($count -gt 0) { ($sorted | Measure-Object -Average).Average } else { [double]::NaN }
$p50 = Get-Percentile -values ([double[]]$sorted) -percentile 50
$p95 = Get-Percentile -values ([double[]]$sorted) -percentile 95
$p99 = Get-Percentile -values ([double[]]$sorted) -percentile 99

$failureRate = [double]$failures / [double]$completed

$summary = [PSCustomObject]@{
	timestampUtc = [DateTime]::UtcNow.ToString("o")
	baseUrl = $BaseUrl
	warmupRequests = $WarmupRequests
	totalRequests = $completed
	successfulRequests = $count
	failedRequests = $failures
	failureRate = $failureRate
	avgMs = [Math]::Round($avg, 2)
	p50Ms = [Math]::Round($p50, 2)
	p95Ms = [Math]::Round($p95, 2)
	p99Ms = [Math]::Round($p99, 2)
	thresholds = [PSCustomObject]@{
		p95Ms = $P95ThresholdMs
		failureRate = $FailureRateThreshold
	}
	pass = ($p95 -le $P95ThresholdMs) -and ($failureRate -le $FailureRateThreshold)
}

$summary | ConvertTo-Json -Depth 5 | Set-Content -Path $OutFile
$summary | Format-List

if (-not $summary.pass) {
	Write-Error "Performance smoke test failed threshold checks."
	exit 1
}

Write-Host "Performance smoke test passed."
