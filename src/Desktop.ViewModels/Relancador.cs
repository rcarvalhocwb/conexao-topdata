using System.Text;

namespace Desktop.ViewModels;

/// <summary>
/// O relançador do painel numa atualização de madrugada (docs/60 §2.3): um PowerShell oculto, na conta do
/// operador, que espera a instalação terminar e abre o painel de novo na bandeja.
/// </summary>
/// <remarks>
/// O serviço instala como SYSTEM e não consegue abrir janela na sessão do operador; o próprio painel deixa o
/// relançador antes de fechar. Termina quando o painel muda de versão e o serviço está rodando, ou em 25
/// minutos (aí abre de qualquer jeito: o operador não fica sem o ícone).
/// </remarks>
public static class Relancador
{
    /// <summary>O script, para o painel em <paramref name="executavel"/>.</summary>
    public static string Script(string executavel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executavel);
        var exe = executavel.Replace("'", "''", StringComparison.Ordinal);

        return $$"""
            $exe = '{{exe}}'
            $antes = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
            Start-Sleep -Seconds 60
            $limite = (Get-Date).AddMinutes(25)
            while ((Get-Date) -lt $limite) {
              $agora = $null
              try { $agora = (Get-Item -LiteralPath $exe -ErrorAction Stop).VersionInfo.FileVersion } catch { }
              $servico = Get-Service -Name 'ConexaoTopdataEdge' -ErrorAction SilentlyContinue
              if ($agora -and $agora -ne $antes -and $servico -and $servico.Status -eq 'Running') { break }
              Start-Sleep -Seconds 15
            }
            Start-Sleep -Seconds 5
            if (Test-Path -LiteralPath $exe) { Start-Process -FilePath $exe -ArgumentList '--bandeja' }
            """;
    }

    /// <summary>O script codificado para <c>powershell -EncodedCommand</c> (UTF-16LE em base64).</summary>
    public static string Codificado(string executavel) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(Script(executavel)));
}
