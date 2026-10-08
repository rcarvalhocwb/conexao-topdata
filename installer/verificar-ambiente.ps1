<#
.SYNOPSIS
  Confere os pré-requisitos que a documentação aponta como causa do retorno 8.
.DESCRIPTION
  Os identificadores aqui são os mesmos de VerificadorDePreRequisitos (src/Edge.Worker).
  Um teste de integração quebra o build se os dois conjuntos divergirem — o instalador e
  o worker precisam reclamar da mesma coisa, com o mesmo nome.

  Descobrir "erro 8" no dia do evento é o pior momento possível. Aqui a checagem sai como
  instrução do que fazer.
#>
[CmdletBinding()]
param(
    [int]$Porta = 3570
)

$ErrorActionPreference = 'Stop'
$pastaDoScript = Split-Path -Parent $MyInvocation.MyCommand.Path
$problemas = @()

# Dois lugares onde este script roda:
#  - instalado: C:\Program Files\Rayzer\XAcess\verificar-ambiente.ps1, com o worker em Worker\;
#  - no repositório: installer\verificar-ambiente.ps1, com o worker em artifacts\Edge.Worker.X86\.
$instalado = Test-Path (Join-Path $pastaDoScript 'Worker')
$pastaDoWorker = if ($instalado) {
    Join-Path $pastaDoScript 'Worker'
} else {
    Join-Path (Split-Path -Parent $pastaDoScript) 'artifacts\Edge.Worker.X86'
}
$comoRefazerOWorker = if ($instalado) {
    'Reinstale o Rayzer XAcess pelo Setup.'
} else {
    'Republique com .\installer\publicar.ps1.'
}

function Conferir {
    param([string]$Id, [nullable[bool]]$Atendido, [string]$Mensagem)

    $marca = if ($null -eq $Atendido) { 'conferir' } elseif ($Atendido) { 'ok      ' } else { 'FALHA   ' }
    Write-Host "[$marca] $Id`: $Mensagem"
    if ($Atendido -eq $false) { $script:problemas += $Id }
}

# ---------------------------------------------------------------------------
# ARQUITETURA_X86 — lida no cabeçalho PE do executável do worker.
# Este script roda em PowerShell de 64 bits, então conferir o próprio processo não diria
# nada. O que importa é o worker: se ele for x64, não carrega a DLL.
# ---------------------------------------------------------------------------
$exeDoWorker = Join-Path $pastaDoWorker 'Edge.Worker.X86.exe'
if (Test-Path $exeDoWorker) {
    $fluxo = [System.IO.File]::OpenRead($exeDoWorker)
    try {
        $leitor = New-Object System.IO.BinaryReader($fluxo)
        $fluxo.Position = 0x3C
        $inicioDoPe = $leitor.ReadInt32()
        $fluxo.Position = $inicioDoPe + 4
        $maquina = $leitor.ReadUInt16()
    } finally {
        $fluxo.Dispose()
    }

    $ehI386 = ($maquina -eq 0x014C)
    Conferir 'ARQUITETURA_X86' $ehI386 $(if ($ehI386) {
        'O programa das catracas é de 32 bits, compatível com a EasyInner.dll.'
    } else {
        "O programa das catracas tem máquina PE 0x{0:X4}, não 0x014C (I386). A EasyInner.dll é de 32 bits e não carrega num processo de 64. $comoRefazerOWorker" -f $maquina
    })
} else {
    Conferir 'ARQUITETURA_X86' $false `
        "Programa das catracas não encontrado em $exeDoWorker. $comoRefazerOWorker"
}

Conferir 'SISTEMA_WINDOWS' ($env:OS -eq 'Windows_NT') `
    'A EasyInner.dll só funciona em Windows.'

# O .NET Framework 3.5 aparece como recurso opcional do Windows.
$net35 = $false
try {
    $chave = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5' -ErrorAction Stop
    $net35 = ($chave.Install -eq 1)
} catch {
    $net35 = $false
}
Conferir 'DOTNET_FRAMEWORK_35' $net35 `
    'Habilite em "Ativar ou desativar recursos do Windows". A ausência é causa documentada de retorno 8.'

# Os mesmos lugares em que o assistente procura (AssistenteDeConfiguracao.VerificarAmbiente): a pasta
# do worker primeiro, que é onde o instalador com o SDK e o botão "Localizar" do assistente a põem.
$dll = @(
    (Join-Path $pastaDoWorker 'EasyInner.dll'),
    (Join-Path $env:SystemRoot 'SysWOW64\EasyInner.dll'),
    (Join-Path $env:SystemRoot 'System32\EasyInner.dll')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
Conferir 'DLLS_REGISTRADAS' ($null -ne $dll) $(if ($dll) {
    "EasyInner.dll encontrada em $dll."
} else {
    "EasyInner.dll não encontrada (procurada em $pastaDoWorker, SysWOW64 e System32). No Assistente de configuração, use o botão Localizar para apontar a DLL do SDK da Topdata."
})

# Cada worker escuta numa porta própria (ADR-0021). Ocupada pelo próprio programa das catracas é o
# normal com o sistema rodando; ocupada por outro programa impede as catracas de conectar.
$ocupada = @(Get-NetTCPConnection -LocalPort $Porta -State Listen -ErrorAction SilentlyContinue)
if ($ocupada.Count -gt 0) {
    $dono = Get-Process -Id $ocupada[0].OwningProcess -ErrorAction SilentlyContinue
    if ($dono -and $dono.ProcessName -eq 'Edge.Worker.X86') {
        Conferir 'PORTA_DEDICADA' $true `
            "Porta $Porta em uso pelo próprio programa das catracas (processo $($dono.Id)), como esperado com o sistema rodando."
    } else {
        $nome = if ($dono) { "$($dono.ProcessName) (processo $($dono.Id))" } else { "processo $($ocupada[0].OwningProcess)" }
        Conferir 'PORTA_DEDICADA' $false `
            "Porta $Porta ocupada por $nome, que não é o programa das catracas. Feche esse programa ou configure outra porta para o grupo no assistente."
    }
} else {
    Conferir 'PORTA_DEDICADA' $null `
        "Porta $Porta livre: o serviço não está escutando nela agora. Confirme que o serviço está rodando e que as catracas deste grupo apontam para esta porta."
}

Write-Host ""
if ($problemas.Count -gt 0) {
    Write-Warning "$($problemas.Count) pré-requisito(s) com falha: $($problemas -join ', ')"
    exit 1
}

Write-Host "Nenhuma FALHA. Os itens marcados 'conferir' pedem uma checagem sua, descrita na própria linha."
