param([string]$Solution = 'TemplateCleanArchitecture.sln')

dotnet list $Solution package --include-transitive
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet list $Solution package --vulnerable --include-transitive
exit $LASTEXITCODE
