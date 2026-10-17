param(
    [string]$DotNetPath = 'C:\Users\alexa\.dotnet\dotnet.exe',
    [int]$Port = 5097,
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'Src\Presentation\Template.WebApi\Template.WebApi.csproj'
$output = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $repositoryRoot 'docs\refactoring\baseline' } else { $OutputDirectory }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$baseUri = "http://127.0.0.1:$Port"
# Phase 22: no committed file carries JwtSettings:Key, so the Test host gets a per-run key from a cryptographically
# secure source. It lives only in this process environment and is never printed.
$jwtKeyBytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($jwtKeyBytes)
$environment = @{
    ASPNETCORE_ENVIRONMENT = 'Test'
    ASPNETCORE_URLS = $baseUri
    JwtSettings__Key = [Convert]::ToBase64String($jwtKeyBytes)
    # The Test host never contacts a broker, but the validated RabbitMqSettings require a password; it is synthetic.
    RabbitMqSettings__Password = [guid]::NewGuid().ToString('N')
}

# Start-Process -Environment requires PowerShell 7.4+; set and restore process variables so Windows PowerShell 5.1 also works.
$previousEnvironment = @{}
foreach ($name in $environment.Keys) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $environment[$name], 'Process')
}
try {
    $process = Start-Process -FilePath $DotNetPath `
        -ArgumentList @('run', '--project', "`"$project`"", '--no-build', '--no-restore', '--no-launch-profile') `
        -WorkingDirectory $repositoryRoot `
        -WindowStyle Hidden `
        -PassThru
}
finally {
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
}

try {
    $ready = $false
    foreach ($attempt in 1..30) {
        try {
            $health = Invoke-WebRequest -Uri "$baseUri/health" -UseBasicParsing -TimeoutSec 2
            if ($health.StatusCode -eq 200) { $ready = $true; break }
        }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw 'Test host did not become ready.' }

    Invoke-WebRequest -Uri "$baseUri/swagger/v1/swagger.json" -OutFile (Join-Path $output 'openapi-v1.json') -UseBasicParsing
    Invoke-WebRequest -Uri "$baseUri/swagger/v2/swagger.json" -OutFile (Join-Path $output 'openapi-v2.json') -UseBasicParsing
    Invoke-WebRequest -Uri "$baseUri/api/v1/product/get-by-id?id=c9770569-499d-4b25-59af-08dd5e76cb62" -OutFile (Join-Path $output 'product-v1-get.json') -UseBasicParsing
    Invoke-WebRequest -Uri "$baseUri/api/v2/product/c9770569-499d-4b25-59af-08dd5e76cb62" -OutFile (Join-Path $output 'product-v2-get.json') -UseBasicParsing
    Invoke-WebRequest -Uri "$baseUri/health" -OutFile (Join-Path $output 'health.json') -UseBasicParsing
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id }
}

Get-ChildItem $output -File | Sort-Object Name | Select-Object Name, Length
