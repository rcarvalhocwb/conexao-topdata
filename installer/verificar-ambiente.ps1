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
$raiz = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$problemas = @()

function Conferir {
    param([string]$Id, [nullable[bool]]$Atendido, [string]$Mensagem)

    $marca = if ($null -eq $Atendido) { 'conferir' } elseif ($Atendido) { 'ok      ' } else { 'FALHA   ' }
    Write-Host "[$marca] $Id`: $Mensagem"
    if ($Atendido -eq $false) { $script:problemas += $Id }
}

# ---------------------------------------------------------------------------
# ARQUITETURA_X86 — lida no cabeçalho PE do executável publicado.
# Este script roda em PowerShell de 64 bits, então conferir o próprio processo não diria
# nada. O que importa é o worker publicado: se ele sair x64, não carrega a DLL.
# ---------------------------------------------------------------------------
$exeDoWorker = Join-Path $raiz 'artifacts\Edge.Worker.X86\Edge.Worker.X86.exe'
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
        'O worker publicado é de 32 bits, compatível com a EasyInner.dll.'
    } else {
        "O worker publicado tem máquina PE 0x{0:X4}, não 0x014C (I386). A EasyInner.dll é de 32 bits e não carrega num processo de 64. Republique com .\installer\publicar.ps1." -f $maquina
    })
} else {
    Conferir 'ARQUITETURA_X86' $null `
        'Worker ainda não publicado. Rode .\installer\publicar.ps1 e confira de novo.'
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

$dll = Get-ChildItem -Path "$env:SystemRoot\SysWOW64", "$env:SystemRoot\System32" `
    -Filter 'EasyInner.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
Conferir 'DLLS_REGISTRADAS' ($null -ne $dll) `
    $(if ($dll) { "Encontrada em $($dll.FullName)." } else { 'Rode o instalador do SDK Inner Acesso (ver vendor/topdata/README.md).' })

# Cada worker escuta numa porta própria (ADR-0021). Aqui só dá para conferir se a porta
# está livre; se as catracas apontam para ela, só a bancada diz.
$ocupada = Get-NetTCPConnection -LocalPort $Porta -State Listen -ErrorAction SilentlyContinue
if ($ocupada) {
    Conferir 'PORTA_DEDICADA' $false `
        "Porta $Porta ocupada pelo processo $($ocupada.OwningProcess). Feche-o ou use outra porta."
} else {
    Conferir 'PORTA_DEDICADA' $null `
        "Porta $Porta livre. Confirme que as catracas deste grupo apontam para ela."
}

Write-Host ""
if ($problemas.Count -gt 0) {
    Write-Warning "$($problemas.Count) pré-requisito(s) com falha: $($problemas -join ', ')"
    exit 1
}

Write-Host "Nenhum impeditivo. Os itens marcados 'conferir' dependem da bancada."
