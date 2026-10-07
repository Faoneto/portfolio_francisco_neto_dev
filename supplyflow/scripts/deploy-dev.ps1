<#
.SYNOPSIS
  Builds SupplyFlow and deploys the code-first parts to a Dataverse DEV environment.

.EXAMPLE
  ./scripts/deploy-dev.ps1 -Url https://orgc3cc5d77.crm2.dynamics.com
  ./scripts/deploy-dev.ps1 -Url https://orgc3cc5d77.crm2.dynamics.com -SkipSeed -SkipPcf

.NOTES
  Run from the supplyflow/ folder. The first Dataverse command opens the browser for login
  (subsequent commands reuse the cached token).
#>
param(
    [Parameter(Mandatory = $true)][string]$Url,
    [switch]$SkipSeed,
    [switch]$SkipPcf
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Step([string]$title, [scriptblock]$action) {
    Write-Host "`n=== $title ===" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) { throw "Falhou: $title (exit code $LASTEXITCODE)" }
}

$deployer = 'src/dataverse/SupplyFlow.Deployer'
$assembly = 'src/dataverse/SupplyFlow.Plugins/bin/Release/net462/SupplyFlow.Plugins.dll'

Step 'Build .NET (plugins + deployer)' { dotnet build SupplyFlow.sln -c Release }
Step 'Validar definicoes' { dotnet run --project $deployer -c Release --no-build -- validate }
Step 'Build web resources' {
    Push-Location src/webresources
    try { npm ci; if ($LASTEXITCODE -eq 0) { npm run build } } finally { Pop-Location }
}

Step '1/4 Schema (tabelas, colunas, chaves, N:N, variaveis de ambiente)' {
    dotnet run --project $deployer -c Release --no-build -- schema --url $Url
}
Step '2/4 Plugins, steps, imagens e Custom API' {
    dotnet run --project $deployer -c Release --no-build -- plugins --url $Url --assembly $assembly
}
Step '3/4 Web resources' {
    dotnet run --project $deployer -c Release --no-build -- webresources --url $Url --folder src/webresources/dist
}
if (-not $SkipSeed) {
    Step '4/4 Dados de exemplo' { dotnet run --project $deployer -c Release --no-build -- seed --url $Url }
}

if (-not $SkipPcf) {
    if (Get-Command pac -ErrorAction SilentlyContinue) {
        Step 'PCF (CnpjInput + RequisitionKanban)' {
            Push-Location src/pcf
            try {
                npm ci
                pac auth create --environment $Url
                pac pcf push --publisher-prefix fno --solution-unique-name SupplyFlow
            } finally { Pop-Location }
        }
    } else {
        Write-Warning 'Power Platform CLI (pac) nao encontrado - PCF ignorado. Instale: dotnet tool install --global Microsoft.PowerApps.CLI.Tool'
    }
}

Write-Host "`nPronto! Abra https://make.powerapps.com > Solucoes > SupplyFlow" -ForegroundColor Green
