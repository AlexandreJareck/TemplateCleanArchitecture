param([Parameter(Mandatory)][string[]]$ChangedPath)

$checks = [System.Collections.Generic.HashSet[string]]::new()
foreach ($path in $ChangedPath) {
    $normalized = $path -replace '\\', '/'
    if ($normalized -match '\.(csproj|props|targets)$|global\.json$') { [void]$checks.Add('restore') }
    if ($normalized -match '^Src/Core/') { [void]$checks.Add('unit'); [void]$checks.Add('webapi-build') }
    if ($normalized -match '^Src/Infrastructure/') { [void]$checks.Add('integration'); [void]$checks.Add('webapi-build') }
    if ($normalized -match '^Src/Presentation/') { [void]$checks.Add('functional'); [void]$checks.Add('webapi-build') }
    if ($normalized -match '^Tests/') { [void]$checks.Add('affected-tests') }
    if ($normalized -match '^docker-compose|^init-db\.sh$') { [void]$checks.Add('compose-config') }
}
$checks | Sort-Object
