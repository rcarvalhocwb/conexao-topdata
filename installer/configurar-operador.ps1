<#
.SYNOPSIS
  Cria o grupo "ConexaoTopdata Operadores" e põe nele as contas dos operadores do evento.
.DESCRIPTION
  O painel e o token do serviço só funcionam para quem está nesse grupo (ou para administradores).
  Sem isso, o painel dos operadores não conecta ao serviço (auditoria de 07/10: S03).

  Rodar como administrador, na máquina do evento, uma vez por instalação. Depois, cada operador
  precisa sair e entrar de novo no Windows para a nova permissão valer.

  Não lê nem imprime o token do serviço.
.EXAMPLE
  .\configurar-operador.ps1 -Operador 'PORTARIA\ana','PORTARIA\bruno'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Operador,

    [string]$Grupo = 'ConexaoTopdata Operadores',
    [string]$Servico = 'ConexaoTopdataEdge'
)

$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    throw 'Rode este script como administrador (botão direito, Executar como administrador).'
}

if (-not (Get-LocalGroup -Name $Grupo -ErrorAction SilentlyContinue)) {
    New-LocalGroup -Name $Grupo -Description 'Operadores do Rayzer XAcess: podem abrir o painel e falar com o serviço.' | Out-Null
    Write-Host "Grupo criado: $Grupo"
} else {
    Write-Host "Grupo já existe: $Grupo"
}

foreach ($conta in $Operador) {
    $ja = Get-LocalGroupMember -Group $Grupo -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ieq $conta }
    if ($ja) {
        Write-Host "Já é operador: $conta"
        continue
    }
    try {
        Add-LocalGroupMember -Group $Grupo -Member $conta
        Write-Host "Adicionado: $conta"
    } catch {
        Write-Warning "Não foi possível adicionar '$conta': $($_.Exception.Message)"
    }
}

# O serviço reaplica a permissão do canal e do token na partida.
if (Get-Service -Name $Servico -ErrorAction SilentlyContinue) {
    Restart-Service -Name $Servico
    Write-Host "Serviço $Servico reiniciado para aplicar a permissão."
} else {
    Write-Warning "Serviço $Servico não encontrado. Instale o sistema antes de configurar os operadores."
}

Write-Host ''
Write-Host 'Membros atuais do grupo:'
Get-LocalGroupMember -Group $Grupo | ForEach-Object { Write-Host "  $($_.Name)" }
Write-Host ''
Write-Host 'Cada operador deve sair e entrar de novo no Windows para a permissão valer.'
