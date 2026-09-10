<#
.SYNOPSIS
    Fetch the full LHDN Step 8 validation result for a submitted document UUID.

.DESCRIPTION
    The service records only the top-level status ("Valid"/"Invalid") from
    GET /api/v1.0/documents/{uuid}/details. That response also carries a
    validationResults block naming every failed validator and the offending
    field - which is what you need to actually fix a CV3xx rejection.

    This script retrieves and prints the whole response, then highlights the
    validation failures.

    Read-only. It does not submit, cancel, or modify anything.

.PARAMETER Uuid
    The MyInvoisUUID from the audit log:
        sqlite3 $db "SELECT InvoiceNumber, MyInvoisUUID FROM AuditLogs
                     WHERE MyInvoisStatus = 'Invalid' ORDER BY Timestamp DESC;"

.PARAMETER ClientId
    Production or pre-prod ClientId. Must match the environment the document
    was submitted to - a production UUID is not visible from pre-prod.

.PARAMETER ClientSecret
    Matching client secret.

.PARAMETER BaseUrl
    Defaults to production. Use https://preprod-api.myinvois.hasil.gov.my for pre-prod.

.PARAMETER OutputDir
    Optional. Writes the raw JSON response here for sharing with LHDN support.

.EXAMPLE
    .\Get-LhdnDocumentDetails.ps1 -Uuid <uuid-from-audit-log> `
        -ClientId <id> -ClientSecret <secret> -OutputDir C:\Temp\lhdn
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Uuid,
    [Parameter(Mandatory)] [string] $ClientId,
    [Parameter(Mandatory)] [string] $ClientSecret,
    [string] $BaseUrl = "https://api.myinvois.hasil.gov.my",
    [string] $OutputDir
)

$ErrorActionPreference = 'Stop'

Write-Host "Requesting access token from $BaseUrl ..." -ForegroundColor Cyan

$tokenBody = @{
    client_id     = $ClientId
    client_secret = $ClientSecret
    grant_type    = 'client_credentials'
    scope         = 'InvoicingAPI'
}

try {
    $token = (Invoke-RestMethod -Method POST -Uri "$BaseUrl/connect/token" `
        -ContentType 'application/x-www-form-urlencoded' -Body $tokenBody).access_token
    Write-Host "Token acquired." -ForegroundColor Green
}
catch {
    Write-Host "Token request failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails.Message) { Write-Host $_.ErrorDetails.Message }
    Write-Host "Check the ClientId/ClientSecret match the environment in -BaseUrl." -ForegroundColor Yellow
    exit 1
}

Write-Host "Fetching details for UUID $Uuid ..." -ForegroundColor Cyan

try {
    $raw = Invoke-WebRequest -Method GET `
        -Uri "$BaseUrl/api/v1.0/documents/$Uuid/details" `
        -Headers @{ Authorization = "Bearer $token" }
    $json = $raw.Content
}
catch {
    Write-Host "Details request failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails.Message) { Write-Host $_.ErrorDetails.Message }
    Write-Host "A UUID submitted to a different environment will 404 here." -ForegroundColor Yellow
    exit 1
}

$doc = $json | ConvertFrom-Json

Write-Host ""
Write-Host "=== DOCUMENT ===" -ForegroundColor Cyan
Write-Host ("  Internal ID : {0}" -f $doc.internalId)
Write-Host ("  Type        : {0} {1}" -f $doc.typeName, $doc.typeVersionName)
Write-Host ("  Status      : {0}" -f $doc.status)
Write-Host ("  Issued      : {0}" -f $doc.dateTimeIssued)
Write-Host ("  Validated   : {0}" -f $doc.dateTimeValidated)

if ($doc.validationResults) {
    Write-Host ""
    Write-Host "=== VALIDATION RESULTS ===" -ForegroundColor Cyan
    Write-Host ("  Overall: {0}" -f $doc.validationResults.status)

    $steps = $doc.validationResults.validationSteps
    $failed = @($steps | Where-Object { $_.status -ne 'Valid' })

    Write-Host ("  Steps: {0} total, {1} failed" -f @($steps).Count, $failed.Count)

    if ($failed.Count -gt 0) {
        Write-Host ""
        Write-Host "=== FAILURES (this is the actionable part) ===" -ForegroundColor Red
        foreach ($s in $failed) {
            Write-Host ""
            Write-Host ("  Validator: {0}" -f $s.name) -ForegroundColor Yellow
            Write-Host ("  Status   : {0}" -f $s.status)
            if ($s.error) {
                if ($s.error.code)          { Write-Host ("  Code     : {0}" -f $s.error.code) -ForegroundColor Red }
                if ($s.error.message)       { Write-Host ("  Message  : {0}" -f $s.error.message) }
                if ($s.error.target)        { Write-Host ("  Target   : {0}" -f $s.error.target) -ForegroundColor Magenta }
                if ($s.error.propertyName)  { Write-Host ("  Property : {0}" -f $s.error.propertyName) -ForegroundColor Magenta }
                if ($s.error.propertyPath)  { Write-Host ("  Path     : {0}" -f $s.error.propertyPath) -ForegroundColor Magenta }
                foreach ($d in $s.error.details) {
                    Write-Host ("    - [{0}] {1}" -f $d.code, $d.message)
                    if ($d.target) { Write-Host ("      target: {0}" -f $d.target) }
                }
            }
        }
    }
    else {
        Write-Host "  No failed validation steps." -ForegroundColor Green
    }
}
else {
    Write-Host ""
    Write-Host "No validationResults block returned." -ForegroundColor Yellow
    Write-Host "If status is Invalid, inspect the raw JSON below." -ForegroundColor Yellow
}

if ($OutputDir) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
    $file = Join-Path $OutputDir "document-details-$Uuid.json"
    $json | Out-File -FilePath $file -Encoding utf8
    Write-Host ""
    Write-Host "Raw response written to: $file" -ForegroundColor Green
    Write-Host "Safe to send to LHDN support - contains no credentials." -ForegroundColor Green
}

Write-Host ""
Write-Host "=== RAW RESPONSE ===" -ForegroundColor Cyan
$json | ConvertFrom-Json | ConvertTo-Json -Depth 20
