<#
.SYNOPSIS
  Carrega no Rayzer XAcess os cartões já validados (arquivos .csv exportados do sistema anterior).
.DESCRIPTION
  Copia os arquivos para a pasta da carga inicial do serviço
  (%ProgramData%\ConexaoTopdata\carga-inicial) e reinicia o serviço, que carrega os cartões ao
  subir e move os arquivos para carga-inicial\importados. Depois mostra o resumo da carga.

  Aceita os formatos de exportação conhecidos (cartões, cartões autorizados e autorizações da
  catraca), juntos ou separados: os arquivos são juntados pelo número do cartão. O nome do
  titular não é lido. Repetir a carga com os mesmos arquivos não muda nada.

  Rodar como administrador, na máquina do evento. Os números dos cartões abrem a catraca: não
  deixe cópias dos arquivos em pastas públicas, no e-mail ou no repositório.
.EXAMPLE
  .\carregar-cartoes.ps1 -Arquivo 'C:\Users\ana\Downloads\cartoes-autorizados-backup.csv'
.EXAMPLE
  .\carregar-cartoes.ps1 -Arquivo 'D:\backup-cartoes'
#>
[CmdletBinding()]
param(
    # Arquivos .csv, ou pastas com eles.
    [Parameter(Mandatory = $true)]
    [string[]]$Arquivo,

    [string]$Servico = 'ConexaoTopdataEdge'
)

$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    throw 'Rode este script como administrador (botão direito, Executar como administrador).'
}

$csvs = foreach ($item in $Arquivo) {
    if (Test-Path -LiteralPath $item -PathType Container) {
        Get-ChildItem -LiteralPath $item -Filter '*.csv' -File
    } elseif (Test-Path -LiteralPath $item -PathType Leaf) {
        Get-Item -LiteralPath $item
    } else {
        throw "Não achei: $item"
    }
}
$csvs = @($csvs | Where-Object { $_.Extension -ieq '.csv' })
if ($csvs.Count -eq 0) { throw 'Nenhum arquivo .csv para carregar.' }

$dados = Join-Path $env:ProgramData 'ConexaoTopdata'
$pasta = Join-Path $dados 'carga-inicial'
New-Item -ItemType Directory -Force -Path $pasta | Out-Null
foreach ($csv in $csvs) {
    Copy-Item -LiteralPath $csv.FullName -Destination (Join-Path $pasta $csv.Name) -Force
    Write-Host "Copiado: $($csv.Name)"
}

$inicio = Get-Date
if (Get-Service -Name $Servico -ErrorAction SilentlyContinue) {
    Write-Host 'Reiniciando o serviço para carregar os cartões...'
    Restart-Service -Name $Servico -Force
} else {
    Write-Host "O serviço $Servico não está instalado: a carga acontece quando ele subir pela primeira vez."
    return
}

# O serviço move os arquivos para "importados" quando termina. Espera até 2 minutos.
$limite = (Get-Date).AddMinutes(2)
while ((Get-ChildItem -LiteralPath $pasta -Filter '*.csv' -File).Count -gt 0 -and (Get-Date) -lt $limite) {
    Start-Sleep -Seconds 2
}

$registros = Join-Path $dados 'registros'
$linhas = Get-ChildItem -LiteralPath $registros -Filter 'servico-*.log' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -ge $inicio.AddMinutes(-1) } |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Encoding UTF8 } |
    Where-Object { $_ -match 'carga inicial' }

if ((Get-ChildItem -LiteralPath $pasta -Filter '*.csv' -File).Count -gt 0) {
    Write-Warning 'Os arquivos ainda estão na pasta: a carga não terminou. Veja o registro do serviço.'
}
if ($linhas) {
    $linhas | Select-Object -Last 20 | ForEach-Object { Write-Host $_ }
} else {
    Write-Host "Resumo não encontrado no registro. Veja $registros."
}
