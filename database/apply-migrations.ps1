<#
.SYNOPSIS
  Aplica las migraciones V*.sql en orden a una base Postgres gestionada
  (Neon, Supabase, etc.) que no corre el mount initdb del docker-compose.

.DESCRIPTION
  Útil para provisionar la DB del demo: corre V001..V006 en orden con
  ON_ERROR_STOP, así que se corta al primer error. Requiere `psql` en el PATH.

.EXAMPLE
  ./database/apply-migrations.ps1 -ConnectionString "postgresql://user:pass@ep-xxx.neon.tech/db?sslmode=require"

.NOTES
  psql acepta la URL postgresql:// directamente (a diferencia de Npgsql, que en
  appsettings/Render usa el formato keyword Host=...;Username=...;...).
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString
)

$ErrorActionPreference = "Stop"

$migrationsDir = Join-Path $PSScriptRoot "migrations"
$files = Get-ChildItem -Path $migrationsDir -Filter "V*.sql" | Sort-Object Name

if (-not $files) {
    throw "No se encontraron migraciones V*.sql en $migrationsDir"
}

foreach ($file in $files) {
    Write-Host "==> aplicando $($file.Name)" -ForegroundColor Cyan
    psql $ConnectionString -v ON_ERROR_STOP=1 -f $file.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "Falló $($file.Name) (exit $LASTEXITCODE). Se aborta."
    }
}

Write-Host "Listo: $($files.Count) migraciones aplicadas." -ForegroundColor Green
