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
    [string]$Configuracao = 'Release'
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

Write-Host ""
Write-Host "Pronto. Confira o ambiente com .\installer\verificar-ambiente.ps1"
