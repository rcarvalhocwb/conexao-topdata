using Access.Infrastructure.SQLite;

namespace Edge.Supervisor;

/// <summary>
/// A licença deste computador, para o serviço (docs/60 §1): o estado de agora, o bloqueio do administrativo
/// e a instalação de uma licença nova. As catracas nunca passam por aqui.
/// </summary>
public sealed class ControleDeLicenca
{
    private readonly ConfiguracoesDaBorda _configuracoes;
    private readonly Func<DateTimeOffset> _relogio;
    private readonly IReadOnlyList<string> _chaves;
    private readonly object _trava = new();
    private (DateTimeOffset Quando, EstadoDaLicenca Estado)? _ultimo;

    /// <param name="configuracoes">Onde a licença fica gravada.</param>
    /// <param name="relogio">Relógio.</param>
    /// <param name="chavesPublicas">As chaves que assinam licenças; padrão, as do suporte.</param>
    public ControleDeLicenca(ConfiguracoesDaBorda configuracoes, Func<DateTimeOffset>? relogio = null, IReadOnlyList<string>? chavesPublicas = null)
    {
        ArgumentNullException.ThrowIfNull(configuracoes);
        _configuracoes = configuracoes;
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);
        _chaves = chavesPublicas ?? ChavesDoSuporte.Publicas;
    }

    /// <summary>Se a licença é exigida nesta versão (há chave pública).</summary>
    public bool Exigida => _chaves.Count > 0;

    /// <summary>O identificador desta instalação, para mandar à Rayzer.</summary>
    public string Instalacao => _configuracoes.IdDaInstalacao(_relogio());

    /// <summary>
    /// O estado agora. Guardado por um minuto: o painel pergunta a cada poucos segundos, e a licença só muda
    /// por <see cref="Instalar"/> (que limpa a memória) ou pela virada do dia.
    /// </summary>
    public EstadoDaLicenca Estado()
    {
        var agora = _relogio();
        lock (_trava)
        {
            if (_ultimo is { } ultimo && agora - ultimo.Quando < TimeSpan.FromMinutes(1) && agora >= ultimo.Quando)
            {
                return ultimo.Estado;
            }
        }

        var estado = Exigida
            ? Licencas.Avaliar(_configuracoes.LerLicenca(), Instalacao, agora, _configuracoes.PrimeiraPartidaDaLicenca(agora), _chaves)
            : Licencas.Avaliar(null, string.Empty, agora, agora, _chaves);

        lock (_trava)
        {
            _ultimo = (agora, estado);
        }

        return estado;
    }

    /// <summary>Verdadeiro quando relatórios, atualizações e mudanças no evento devem ser recusados.</summary>
    public bool Bloqueada => Estado().Bloqueada;

    /// <summary>Confere e grava uma licença nova. Devolve os problemas; vazio se gravou.</summary>
    public IReadOnlyList<string> Instalar(string? texto, string quem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quem);

        if (!Exigida)
        {
            return ["Esta versão não exige licença; nada a instalar."];
        }

        if (string.IsNullOrWhiteSpace(texto))
        {
            return ["Cole o texto da licença que a Rayzer mandou."];
        }

        if (Licencas.Ler(texto, Instalacao, _chaves, out var problema) is null)
        {
            return [problema ?? "Licença inválida."];
        }

        _configuracoes.GravarLicenca(texto, _relogio(), quem);
        lock (_trava)
        {
            _ultimo = null;
        }

        return [];
    }
}
