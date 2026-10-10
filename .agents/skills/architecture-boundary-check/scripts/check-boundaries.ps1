$ErrorActionPreference = 'Stop'
$violations = [System.Collections.Generic.List[string]]::new()

$domainProject = Get-Content -Raw 'Src/Core/Template.Domain/Template.Domain.csproj'
if ($domainProject -match '<PackageReference|<ProjectReference') { $violations.Add('Domain project declares a package or project reference.') }

$applicationProject = [xml](Get-Content -Raw 'Src/Core/Template.Application/Template.Application.csproj')
$forbiddenPattern = 'EntityFrameworkCore|StackExchangeRedis|RabbitMQ|Elastic|Serilog|AspNetCore|Hosting.Abstractions'
$forbiddenPackages = @($applicationProject.Project.ItemGroup.PackageReference |
    ForEach-Object { [string]$_.Include } |
    Where-Object { $_ -match $forbiddenPattern })
if ($forbiddenPackages.Count -gt 0) {
    $violations.Add("Application references implementation packages: $($forbiddenPackages -join ', ').")
}

$coreFiles = Get-ChildItem 'Src/Core' -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
foreach ($file in $coreFiles) {
    $source = Get-Content -Raw -LiteralPath $file.FullName
    if ($source -match 'RabbitMQ|BackgroundService|IHostedService|ConnectionFactory|CreateChannel|QueueDeclare|ExchangeDeclare|RabbitMqSettings') {
        $violations.Add("Core contains messaging plumbing in $($file.FullName).")
    }
    if ($source -match 'Elastic\.|ElasticsearchClient|ElasticSearchSettings|Elasticsearch|Serilog|HealthChecks') {
        $violations.Add("Core contains search, logging or health-check plumbing in $($file.FullName).")
    }
}

$sourceFiles = Get-ChildItem 'Src' -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
foreach ($file in $sourceFiles) {
    $source = Get-Content -Raw -LiteralPath $file.FullName
    $relative = $file.FullName.Substring((Get-Location).Path.Length + 1) -replace '\\', '/'
    if ($source -match 'using Elastic\.Clients|ElasticsearchClient' -and $relative -notlike 'Src/Infrastructure/Template.Infrastructure.Search/*') {
        $violations.Add("Elasticsearch client used outside Infrastructure.Search in $relative.")
    }
    if ($source -match 'BuildServiceProvider\(' -and $relative -ne 'Src/Presentation/Template.WebApi/Infrastructure/Extensions/LocalizationExtensions.cs') { # Phase 13: localization
        $violations.Add("Registration builds an intermediate service provider in $relative.")
    }
}

$applicationPorts = Get-ChildItem 'Src/Core/Template.Application/Interfaces' -Recurse -Filter *.cs
foreach ($file in $applicationPorts) {
    $source = Get-Content -Raw -LiteralPath $file.FullName
    if ($source -match 'IQueryable\s*<') { $violations.Add("Application port exposes IQueryable in $($file.FullName).") }
}

$presentationFiles = Get-ChildItem 'Src/Presentation' -Recurse -Filter *.cs
foreach ($file in $presentationFiles) {
    $source = Get-Content -Raw -LiteralPath $file.FullName
    if ($source -match '\b(DbContext|IGenericRepository|IProductRepository)\b') { $violations.Add("Presentation directly references persistence in $($file.FullName).") }
}

$controllerFiles = Get-ChildItem 'Src/Presentation/Template.WebApi/Controllers' -Recurse -Filter *.cs
foreach ($file in $controllerFiles) {
    $source = Get-Content -Raw -LiteralPath $file.FullName
    if ($source -match 'HttpContext\.RequestServices|RequestServices\.Get(Service|RequiredService)') {
        $violations.Add("Controller uses service location in $($file.FullName).")
    }
}

if ($violations.Count -gt 0) { $violations; exit 1 }
'Architecture boundary scan passed.'
