<#
.SYNOPSIS
  Sobe o ambiente de desenvolvimento e gera o token de sessão do IPC.
.DESCRIPTION
  NÃO é o instalador de produção: não registra serviço do Windows, não assina pacote e
  não configura rollback. Isso vem na Fase 5.
#>
[CmdletBinding()]
param(
    [int]$Porta = 3570
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

$pastaDeDados = Join-Path $env:LOCALAPPDATA 'ConexaoTopdata'
New-Item -ItemType Directory -Force -Path $pastaDeDados | Out-Null

# Token de sessão: além da ACL do named pipe, que protege por identidade de usuário e
# não separa processos da mesma conta.
$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$token = [Convert]::ToBase64String($bytes)

$arquivoDoToken = Join-Path $pastaDeDados 'token'
Set-Content -Path $arquivoDoToken -Value $token -NoNewline -Encoding ASCII

# Só o usuário atual lê o token.
$acl = Get-Acl $arquivoDoToken
$acl.SetAccessRuleProtection($true, $false)
$regra = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "$env:USERDOMAIN\$env:USERNAME", 'Read,Write', 'Allow')
$acl.SetAccessRule($regra)
Set-Acl -Path $arquivoDoToken -AclObject $acl

Write-Host "Token de sessão gravado em $arquivoDoToken (somente o usuário atual lê)."
Write-Host ""
Write-Host "Para executar:"
Write-Host "  `$env:EDGE_TOKEN = Get-Content '$arquivoDoToken'"
Write-Host "  .\artifacts\Edge.Supervisor\Edge.Supervisor.exe"
Write-Host "  .\artifacts\Desktop.App\Desktop.App.exe"
Write-Host ""
Write-Host "O worker x86 escuta na porta $Porta. Aponte as catracas deste grupo para ela."
Write-Host "Cada worker adicional usa a porta seguinte."
