[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$environmentPath = Join-Path $repositoryRoot '.env'

if ((Test-Path -LiteralPath $environmentPath) -and -not $Force)
{
    throw '.env already exists. Use -Force only when you intentionally want to replace every local secret.'
}

function New-RandomBase64([int]$byteCount)
{
    $bytes = New-Object byte[] $byteCount
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try
    {
        $generator.GetBytes($bytes)
        return [Convert]::ToBase64String($bytes)
    }
    finally
    {
        $generator.Dispose()
    }
}

$passwordBytes = New-Object byte[] 8
$passwordGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
try
{
    $passwordGenerator.GetBytes($passwordBytes)
}
finally
{
    $passwordGenerator.Dispose()
}
$demoPassword = "Demo!9$(($passwordBytes | ForEach-Object { $_.ToString('x2') }) -join '')"
$lines = @(
    '# Generated for the local academic environment. Never commit this file.'
    'POSTGRES_USER=template_app'
    "POSTGRES_PASSWORD=$(New-RandomBase64 24)"
    'POSTGRES_DB_NAME=TemplateCleanArch'
    'POSTGRES_IDENTITY_DB_NAME=TemplateCleanArchIdentity'
    'RABBITMQ_DEFAULT_USER=template_app'
    "RABBITMQ_DEFAULT_PASS=$(New-RandomBase64 24)"
    "REDIS_PASSWORD=$(New-RandomBase64 24)"
    "REDIS_INSIGHT_ENCRYPTION_KEY=$(New-RandomBase64 32)"
    'AZURITE_ACCOUNT_NAME=templatefiles'
    "AZURITE_ACCOUNT_KEY=$(New-RandomBase64 32)"
    "JwtSettings__Key=$(New-RandomBase64 48)"
    "APPROVAL_WORKFLOW_KEY=$(New-RandomBase64 48)"
    'DEMO_DATA_ENABLED=true'
    "DEMO_USER_PASSWORD=$demoPassword"
    'WEBAPP_PORT=4200'
    'POSTGRES_PORT=5432'
    'RABBITMQ_PORT=5672'
    'RABBITMQ_MANAGEMENT_PORT=15672'
    'ELASTICSEARCH_PORT=9200'
    'KIBANA_PORT=5601'
    'REDIS_PORT=6379'
    'REDIS_INSIGHT_PORT=6380'
    'WEBAPI_HTTP_PORT=8080'
    'WORKER_HEALTH_PORT=8082'
    'WORKFLOW_HTTP_PORT=7071'
    'AZURITE_BLOB_PORT=10000'
    'WORKFLOW_BLOB_PORT=11000'
    'WORKFLOW_QUEUE_PORT=11001'
    'WORKFLOW_TABLE_PORT=11002'
)

Set-Content -LiteralPath $environmentPath -Value $lines -Encoding utf8NoBOM
Write-Host "Created $environmentPath with local random secrets."
Write-Host 'The demo password is stored in DEMO_USER_PASSWORD inside .env; the script does not print it.'
