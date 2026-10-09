<#
.SYNOPSIS
  Baixa os SDKs e manuais oficiais da Topdata para vendor\topdata\sdk\.
.DESCRIPTION
  Os arquivos sao proprietarios da Topdata e NAO sao versionados (ver .gitignore).
  Esta e a maquina onde eles importam: a EasyInner.dll so roda em Windows x86.
.PARAMETER Filtro
  'todos' (padrao), 'inner' ou 'facial'.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File vendor\topdata\fetch-sdk.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File vendor\topdata\fetch-sdk.ps1 -Filtro inner
#>
[CmdletBinding()]
param(
    [ValidateSet('todos','inner','facial')]
    [string]$Filtro = 'todos'
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $dir 'sdk'
$origem = Join-Path $dir 'sources.tsv'

if (-not (Test-Path $origem)) { throw "Arquivo de origens nao encontrado: $origem" }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$ok = 0; $falhou = 0
Import-Csv -Path $origem -Delimiter "`t" | ForEach-Object {
    if ($Filtro -ne 'todos' -and $_.categoria -ne $Filtro) { return }

    $destino = Join-Path $out $_.nome
    if ((Test-Path $destino) -and (Get-Item $destino).Length -gt 0) {
        Write-Host "  ja existe: $($_.nome)"
        $script:ok++; return
    }

    Write-Host "  baixando: $($_.nome)"
    $parcial = "$destino.parcial"
    try {
        Invoke-WebRequest -Uri $_.url -OutFile $parcial -UseBasicParsing -TimeoutSec 300
    } catch {
        Write-Warning "    FALHOU: $($_.Exception.Message)"
        Remove-Item $parcial -ErrorAction SilentlyContinue
        $script:falhou++; return
    }

    # Confere a assinatura do arquivo: PK.. para ZIP, %PDF para PDF.
    # Sem isso, uma pagina de erro HTML seria salva como se fosse o SDK.
    $bytes = [IO.File]::ReadAllBytes($parcial) | Select-Object -First 4
    $assinatura = -join ($bytes | ForEach-Object { '{0:X2}' -f $_ })
    if ($assinatura -like '504B*' -or $assinatura -eq '25504446') {
        Move-Item $parcial $destino -Force
        $tam = [math]::Round((Get-Item $destino).Length / 1MB, 2)
        Write-Host "    ok ($tam MB)"
        $script:ok++
    } else {
        Write-Warning "    FALHOU: conteudo nao e ZIP nem PDF (assinatura $assinatura) - link provavelmente expirou"
        Remove-Item $parcial -ErrorAction SilentlyContinue
        $script:falhou++
    }
}

if ($ok -gt 0) {
    Get-ChildItem $out -File -Exclude 'CHECKSUMS.txt' |
        Get-FileHash -Algorithm SHA256 |
        ForEach-Object { "{0}  {1}" -f $_.Hash.ToLower(), (Split-Path $_.Path -Leaf) } |
        Set-Content (Join-Path $out 'CHECKSUMS.txt') -Encoding ASCII
    Write-Host ""
    Write-Host "SHA-256 gravado em $out\CHECKSUMS.txt"
    Write-Host "Guarde esse arquivo: e a linha de base para detectar troca de DLL em campo."
}

Write-Host ""
Write-Host "Concluido: $ok arquivo(s), $falhou falha(s). Destino: $out"
if ($falhou -gt 0) {
    Write-Host "Links oficiais em: https://integrador.topdata.com.br/"
    exit 1
}
