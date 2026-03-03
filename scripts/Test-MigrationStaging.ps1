param(
    [string]$Project = ".\src\HealthPilot.Api\HealthPilot.Api.csproj",
    [string]$StartupProject = ".\src\HealthPilot.Api\HealthPilot.Api.csproj",
    [string]$OutSql = ".\tmp\staging-migration.sql"
)

$ErrorActionPreference = "Stop"

Set-Location (Resolve-Path "$PSScriptRoot\..")

New-Item -ItemType Directory -Force -Path ".\tmp" | Out-Null

Write-Host "Generating idempotent migration SQL script..."
dotnet ef migrations script --idempotent --project $Project --startup-project $StartupProject --output $OutSql

Write-Host "Applying migrations to configured database..."
dotnet ef database update --project $Project --startup-project $StartupProject

Write-Host "Checking for pending migrations..."
$pending = dotnet ef migrations list --project $Project --startup-project $StartupProject | Select-String "(Pending)"

if ($pending) {
    Write-Error "Pending migrations still exist after update."
    exit 1
}

Write-Host "Migration staging check completed successfully."
Write-Host "SQL artifact:" (Resolve-Path $OutSql)
