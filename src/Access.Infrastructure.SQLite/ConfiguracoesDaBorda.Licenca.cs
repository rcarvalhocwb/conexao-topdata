using System.Globalization;

namespace Access.Infrastructure.SQLite;

/// <summary>A licença instalada e o que a atualização automática precisa lembrar (docs/60).</summary>
public sealed partial class ConfiguracoesDaBorda
{
    /// <summary>O texto da licença instalada.</summary>
    public const string ChaveLicenca = "licenca.texto";

    /// <summary>Desde quando a licença é exigida aqui: conta a tolerância de quem ainda não tem licença.</summary>
    public const string ChavePrimeiraPartidaDaLicenca = "licenca.primeira_partida";

    /// <summary>A versão que a atualização automática mandou instalar, com o instante (<c>versao|instante</c>).</summary>
    public const string ChaveAtualizacaoEmCurso = "atualizacao.em_curso";

    /// <summary>O resultado da última atualização automática, para o Diagnóstico.</summary>
    public const string ChaveAtualizacaoResultado = "atualizacao.resultado";

    /// <summary>Tentativas de instalar cada versão (<c>atualizacao.tentativas.0.1.N</c>).</summary>
    public const string PrefixoTentativasDeAtualizacao = "atualizacao.tentativas.";

    /// <summary>A licença instalada; nulo se não há.</summary>
    public string? LerLicenca() => Todos().GetValueOrDefault(ChaveLicenca) is { Length: > 0 } texto ? texto : null;

    /// <summary>Grava a licença nova, com quem instalou.</summary>
    public void GravarLicenca(string texto, DateTimeOffset agora, string quem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texto);
        GravarPares(new Dictionary<string, string> { [ChaveLicenca] = texto.Trim() }, agora, quem);
    }

    /// <summary>A primeira partida com a licença exigida; gravada na primeira vez e nunca mudada.</summary>
    public DateTimeOffset PrimeiraPartidaDaLicenca(DateTimeOffset agora)
    {
        using (var conexao = _fabrica.Abrir())
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText = "INSERT OR IGNORE INTO edge_setting (key, value, updated_at, updated_by) VALUES ($k, $v, $v, 'servico');";
            comando.Parameters.AddWithValue("$k", ChavePrimeiraPartidaDaLicenca);
            comando.Parameters.AddWithValue("$v", agora.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            comando.ExecuteNonQuery();
        }

        return DateTimeOffset.TryParse(Todos()[ChavePrimeiraPartidaDaLicenca], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var quando)
            ? quando
            : agora;
    }

    /// <summary>Um valor livre da atualização automática; nulo se não existe.</summary>
    public string? LerValor(string chave)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chave);
        return Todos().GetValueOrDefault(chave);
    }

    /// <summary>Grava um valor livre da atualização automática (o autor é o serviço).</summary>
    public void GravarValor(string chave, string valor, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chave);
        ArgumentNullException.ThrowIfNull(valor);
        GravarPares(new Dictionary<string, string> { [chave] = valor }, agora, "servico");
    }
}
