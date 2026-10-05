# Instala (ou atualiza) o IntegracaoSolidesDP como serviço do Windows.
# Rodar como Administrador, de dentro da pasta extraída do zip.
param(
    [string]$ServiceName = "IntegracaoSolidesDP",
    [string]$InstallDir = "C:\Services\IntegracaoSolidesDP"
)
$ErrorActionPreference = "Stop"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Stop-Service -Name $ServiceName -Force
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
# Preserva a configuração já existente do cliente.
Get-ChildItem -Path $PSScriptRoot -Exclude "appsettings.json" | Copy-Item -Destination $InstallDir -Recurse -Force
if (-not (Test-Path (Join-Path $InstallDir "appsettings.json"))) {
    Copy-Item (Join-Path $PSScriptRoot "appsettings.json") $InstallDir
    Write-Host "appsettings.json copiado: edite-o antes de iniciar o serviço (veja INSTALL.md)."
}

$exe = Join-Path $InstallDir "IntegracaoSolidesDP.exe"
if (-not $existing) {
    New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName "Integração RHSenso → Sólides DP" -StartupType Automatic | Out-Null
    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
}

Write-Host "Validando a configuração..."
& $exe --check-config
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Configuração com problemas: o serviço NÃO foi iniciado. Corrija o appsettings.json e rode de novo."
    exit $LASTEXITCODE
}

Start-Service -Name $ServiceName
Write-Host "Serviço $ServiceName iniciado. Logs em $InstallDir\logs, relatórios em $InstallDir\reports."
