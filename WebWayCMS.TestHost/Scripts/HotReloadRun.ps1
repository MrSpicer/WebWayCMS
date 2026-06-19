# Starts dotnet watch with hot reload for the Integration-Host example (C# + Razor).
$ErrorActionPreference = 'Stop'
Set-Location "$PSScriptRoot/.."
$env:ASPNETCORE_ENVIRONMENT = 'Development'

# Note: Integration-Host has no .scss of its own — wwwroot/css/site.css is static and the
# CMS admin UI styles ship compiled inside the WebWayCMS package — so there are
# no Sass watchers to start here.
dotnet watch run --project Integration-Host.csproj
