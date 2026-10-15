<#
.SYNOPSIS
  Gera o instalador (RayzerXAcess-<versão>.msi e RayzerXAcess-Setup.exe) numa máquina Windows.
.DESCRIPTION
  O mesmo caminho do CI (job "Instalador MSI (Windows)"), que chama este script por etapa: o que
  sai daqui é o que sai na pré-release. Precisa do .NET SDK do global.json e do WiX 6.0.2 com as
  extensões (o script instala o que faltar com -InstalarWix).

  Sem -SdkDir, o instalador sai SEM o SDK da Topdata (teste/simulação; o assistente localiza a
  EasyInner.dll na máquina). Com -SdkDir, a EasyInner.dll e as DLLs dela vão junto do programa
  das catracas. O SDK é proprietário e nunca é versionado neste repositório.

.EXAMPLE
  # Instalador de teste, versão 0.1.900
  .\installer\gerar-setup.ps1 -Versao 0.1.900 -InstalarWix

.EXAMPLE
  # Instalador com o SDK embarcado (só na máquina que tem o SDK)
  .\installer\gerar-setup.ps1 -Versao 1.0.0 -SdkDir "C:\Topdata\SDK Inner Acesso" -Producao
#>
[CmdletBinding()]
param(
    # Versão que vai nos executáveis, no MSI e no Setup.
    [string]$Versao = '0.1.0',

    # Tudo (padrão) ou uma etapa só: o CI roda o autoteste das telas entre Publicar e Msi.
    [ValidateSet('Tudo', 'Publicar', 'Msi', 'Setup')]
    [string]$Etapa = 'Tudo',

    # Pasta do SDK Inner Acesso (EasyInner.dll e companhia), para embarcar no instalador.
    [string]$SdkDir = $env:TOPDATA_SDK_DIR,

    # Sem este, o instalador sai como "canal de teste" (o mesmo das pré-releases).
    [switch]$Producao,

    # Instala o WiX 6.0.2 e as extensões, se faltarem.
    [switch]$InstalarWix
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Push-Location $raiz
try {
    # O .wxs usa <?ifdef CanalDeTeste?>: em produção a variável não pode nem ser passada.
    $canal = if ($Producao) { @() } else { @('-d', 'CanalDeTeste=1') }

    if ($InstalarWix) {
        if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
            dotnet tool install --global wix --version 6.0.2
            if ($LASTEXITCODE -ne 0) { throw 'Falha ao instalar o WiX.' }
        }
        foreach ($extensao in 'WixToolset.BootstrapperApplications.wixext', 'WixToolset.Util.wixext', 'WixToolset.Firewall.wixext') {
            wix extension add --global "$extensao/6.0.2"
        }
    }

    if ($Etapa -in 'Tudo', 'Publicar') {
        # Self-contained: cada componente leva o seu .NET; o operador não instala runtime nenhum.
        # O worker sai em x86 por obrigação: a EasyInner.dll é de 32 bits.
        $componentes = @(
            @{ Projeto = 'src/Edge.Supervisor';   Rid = 'win-x64'; Saida = 'publicado/Servico' },
            @{ Projeto = 'src/Desktop.App';       Rid = 'win-x64'; Saida = 'publicado/Painel' },
            @{ Projeto = 'src/Edge.Worker.X86';   Rid = 'win-x86'; Saida = 'publicado/Worker' },
            @{ Projeto = 'src/Edge.Configurador'; Rid = 'win-x64'; Saida = 'publicado/Configurador' }
        )
        foreach ($c in $componentes) {
            Write-Host "Publicando $($c.Projeto) [$($c.Rid)]"
            dotnet publish $c.Projeto -c Release -r $c.Rid --self-contained true -p:Version=$Versao -o $c.Saida --nologo
            if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar $($c.Projeto)." }
        }

        if ($SdkDir) {
            $easy = Join-Path $SdkDir 'EasyInner.dll'
            if (-not (Test-Path $easy)) {
                throw "EasyInner.dll não encontrada em $SdkDir. Aponte -SdkDir para a pasta do SDK Inner Acesso."
            }
            Copy-Item (Join-Path $SdkDir '*') 'publicado/Worker' -Recurse -Force
            Write-Host "SDK da Topdata embarcado a partir de $SdkDir"
        }
        else {
            Write-Host 'Sem SDK embarcado (instalador de teste/simulação). Use -SdkDir para embarcar.'
        }
    }

    if ($Etapa -in 'Tudo', 'Msi') {
        # Caminhos ABSOLUTOS: em <Files>, caminho relativo é resolvido a partir da pasta do .wxs,
        # e o MSI saía com 70 KB, sem erro. -arch x64: sem ele, "ProgramFiles64Folder" vira
        # C:\Program Files (x86).
        Write-Host "Construindo o MSI $Versao"
        wix build installer/wix/ConexaoTopdata.wxs -arch x64 -ext WixToolset.Util.wixext -ext WixToolset.Firewall.wixext `
            -d Versao=$Versao `
            -d PastaSupervisor=$((Resolve-Path publicado/Servico).Path) `
            -d PastaPainel=$((Resolve-Path publicado/Painel).Path) `
            -d PastaWorker=$((Resolve-Path publicado/Worker).Path) `
            -d PastaConfigurador=$((Resolve-Path publicado/Configurador).Path) `
            -d PastaModelos=$((Resolve-Path installer/modelos).Path) `
            -d PastaInstalador=$((Resolve-Path installer).Path) `
            @canal `
            -o "RayzerXAcess-$Versao.msi"
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao construir o MSI.' }
    }

    if ($Etapa -in 'Tudo', 'Setup') {
        $msi = (Resolve-Path "RayzerXAcess-$Versao.msi").Path
        Write-Host "Construindo o Setup.exe a partir de $msi"
        wix build installer/wix/Setup.wxs -ext WixToolset.BootstrapperApplications.wixext `
            -d Versao=$Versao -d Msi=$msi @canal -o RayzerXAcess-Setup.exe
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao construir o Setup.exe.' }
        Write-Host ("Pronto: {0}\RayzerXAcess-Setup.exe ({1:N1} MB)" -f $raiz, ((Get-Item RayzerXAcess-Setup.exe).Length / 1MB))
    }
}
finally {
    Pop-Location
}
