<#
.SYNOPSIS
  Publica os componentes em artifacts\.
.DESCRIPTION
  O worker sai em x86 por obrigação: a EasyInner.dll é de 32 bits, e um processo de 64
  falha no carregamento. Os demais saem em x64.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuracao = 'Release',

    # Pasta com o SDK da Topdata (EasyInner.dll e as DLLs que ela usa), para embarcar no
    # instalador. Fica ao lado do worker x86, onde o P/Invoke a encontra e o assistente a
    # detecta sozinho. Vazio (padrão) = instalador de teste/simulação, sem o SDK.
    # O SDK é proprietário da Topdata e NUNCA é versionado neste repositório público.
    [string]$SdkDir = $env:TOPDATA_SDK_DIR
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$saida = Join-Path $raiz 'artifacts'

Write-Host "Publicando em $saida ($Configuracao)"

$componentes = @(
    @{ Nome = 'Edge.Worker.X86'; Projeto = 'src/Edge.Worker.X86'; Rid = 'win-x86' },
    @{ Nome = 'Edge.Supervisor'; Projeto = 'src/Edge.Supervisor'; Rid = 'win-x64' },
    @{ Nome = 'Desktop.App';     Projeto = 'src/Desktop.App';     Rid = 'win-x64' }
)

foreach ($c in $componentes) {
    $destino = Join-Path $saida $c.Nome
    Write-Host "  $($c.Nome) [$($c.Rid)]"

    dotnet publish (Join-Path $raiz $c.Projeto) `
        --configuration $Configuracao `
        --runtime $c.Rid `
        --self-contained false `
        --output $destino `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao publicar $($c.Nome)."
    }
}

# O arquivo de exemplo do ensaio de bancada vai junto do worker (docs/21).
Copy-Item (Join-Path $raiz 'installer/bancada.exemplo.json') (Join-Path $saida 'Edge.Worker.X86') -Force

# SDK da Topdata embarcado: copia a EasyInner.dll e companhia para a pasta do worker, onde
# o worker x86 a carrega por P/Invoke e o assistente a detecta sozinho. Só quando -SdkDir
# (ou TOPDATA_SDK_DIR) aponta para uma pasta — nunca vem do repositório.
if ($SdkDir) {
    if (-not (Test-Path $SdkDir)) {
        throw "SdkDir nao encontrado: $SdkDir"
    }
    $easy = Join-Path $SdkDir 'EasyInner.dll'
    if (-not (Test-Path $easy)) {
        throw "EasyInner.dll nao encontrada em $SdkDir. Aponte -SdkDir para a pasta do SDK Inner Acesso."
    }
    $destinoWorker = Join-Path $saida 'Edge.Worker.X86'
    Copy-Item (Join-Path $SdkDir '*') $destinoWorker -Recurse -Force
    Write-Host "SDK da Topdata embarcado a partir de $SdkDir"
} else {
    Write-Host "Sem SDK embarcado (instalador de teste/simulacao). Use -SdkDir para embarcar."
}

Write-Host ""
Write-Host "Pronto. Confira o ambiente com .\installer\verificar-ambiente.ps1"
