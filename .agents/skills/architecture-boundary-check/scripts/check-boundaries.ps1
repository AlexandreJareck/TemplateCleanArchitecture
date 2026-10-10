$ErrorActionPreference = 'Stop'
$violations = [System.Collections.Generic.List[string]]::new()

$domainProject = Get-Content -Raw 'Src/Core/Template.Domain/Template.Domain.csproj'
if ($domainProject -match '<PackageReference|<ProjectReference') { $violations.Add('Domain project declares a package or project reference.') }

$applicationProject = [xml](Get-Content -Raw 'Src/Core/Template.Application/Template.Application.csproj')
$allowedTemporaryPackages = @(
    'Elastic.Clients.Elasticsearch', # Phase 10
    'Microsoft.Extensions.Hosting.Abstractions', # Phase 9
    'RabbitMQ.Client' # Phase 9
)
$forbiddenPattern = 'EntityFrameworkCore|StackExchangeRedis|RabbitMQ|Elastic|Serilog|AspNetCore|Hosting.Abstractions'
$actualTemporaryPackages = @($applicationProject.Project.ItemGroup.PackageReference |
    ForEach-Object { [string]$_.Include } |
    Where-Object { $_ -match $forbiddenPattern } |
    Sort-Object)
if (Compare-Object $allowedTemporaryPackages $actualTemporaryPackages) {
    $violations.Add('Application implementation package exceptions differ from the exact Phase 9-10 allowlist.')
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
