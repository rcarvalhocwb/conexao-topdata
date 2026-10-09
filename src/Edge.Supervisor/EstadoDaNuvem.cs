using Sync.Core;

namespace Edge.Supervisor;

/// <summary>Internet, medida pela última tentativa de falar com a nuvem.</summary>
public enum InternetMedida
{
    /// <summary>A última tentativa recente chegou ao destino (ou o destino respondeu com erro).</summary>
    Disponivel,

    /// <summary>A última tentativa recente não encontrou o destino: falta de rede comprovada.</summary>
    Indisponivel,

    /// <summary>Sem tentativa recente, ou a falha não diz nada sobre a rede. Não se sabe.</summary>
    Desconhecida,
}

/// <summary>Estado da conexão com a nuvem, do ponto de vista deste computador.</summary>
public enum NuvemMedida
{
    /// <summary>Sem configuração: a nuvem não é usada aqui.</summary>
    NaoConfigurada,

    /// <summary>Configurada, mas o segredo não está nesta máquina: nada é tentado.</summary>
    SegredoAusente,

    /// <summary>Configurada, mas ainda não houve tentativa.</summary>
    Conectando,

    /// <summary>A última tentativa recente deu certo.</summary>
    Conectada,

    /// <summary>O destino recusou a credencial desta borda.</summary>
    FalhaDeAutenticacao,

    /// <summary>A última tentativa recente não chegou ao destino, ou o destino está fora.</summary>
    Indisponivel,

    /// <summary>A última tentativa é antiga ou a falha não diz o estado.</summary>
    Desconhecida,
}

/// <summary>Estado da fila de sincronização.</summary>
public enum SincronizacaoMedida
{
    /// <summary>Uma rodada está em andamento agora.</summary>
    Processando,

    /// <summary>A última rodada falhou; a fila segue pendente.</summary>
    Falha,

    /// <summary>Há itens à espera de envio.</summary>
    Pendente,

    /// <summary>Nada pendente e houve sucesso.</summary>
    Sincronizada,

    /// <summary>Nunca houve rodada que permita dizer.</summary>
    Desconhecida,
}

/// <summary>
/// O que o painel precisa saber da sincronização com a nuvem (P0-02).
/// </summary>
/// <remarks>
/// <para>
/// O estado sai da última TENTATIVA, nunca do último sucesso antigo. Um sucesso de dez minutos
/// atrás não prova que a internet está de pé agora, e uma recusa de credencial prova que a
/// internet funciona: o destino respondeu.
/// </para>
/// <para>
/// Sem internet continua sendo o regime normal de um evento (ADR-0017). Estes estados informam;
/// nunca bloqueiam nada: a catraca decide com a lista local.
/// </para>
/// </remarks>
public sealed class EstadoDaNuvem
{
    /// <summary>Tempo mínimo em que uma tentativa vale como "agora", mesmo com sincronização lenta.</summary>
    public static readonly TimeSpan ConsideradaFora = TimeSpan.FromMinutes(2);

    private readonly object _trava = new();
    private DateTimeOffset? _ultimoSucesso;
    private DateTimeOffset? _ultimaTentativa;
    private TipoDeFalha _tipoDaUltimaTentativa = TipoDeFalha.Nenhuma;
    private string? _ultimaFalha;

    /// <summary>Se a nuvem está configurada nesta máquina.</summary>
    public bool Configurada { get; set; }

    /// <summary>Verdadeiro quando o segredo da nuvem falta nesta máquina: nada é tentado.</summary>
    public bool SegredoAusente { get; set; }

    /// <summary>Verdadeiro enquanto uma rodada de sincronização está em andamento.</summary>
    public bool EmAndamento { get; set; }

    /// <summary>De quanto em quanto tempo o laço sincroniza. Define a janela em que uma tentativa vale.</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Última vez que a nuvem respondeu com sucesso (para o painel mostrar "há quanto tempo").</summary>
    public DateTimeOffset? UltimoSucesso
    {
        get { lock (_trava) { return _ultimoSucesso; } }
    }

    /// <summary>Última falha, para o painel explicar. Nunca contém segredo nem corpo de resposta.</summary>
    public string? UltimaFalha
    {
        get { lock (_trava) { return _ultimaFalha; } }
    }

    /// <summary>Quando foi a última tentativa, com sucesso ou não.</summary>
    public DateTimeOffset? UltimaTentativa
    {
        get { lock (_trava) { return _ultimaTentativa; } }
    }

    /// <summary>Tipo da última tentativa; <see cref="TipoDeFalha.Nenhuma"/> quando deu certo.</summary>
    public TipoDeFalha TipoDaUltimaTentativa
    {
        get { lock (_trava) { return _tipoDaUltimaTentativa; } }
    }

    /// <summary>Janela em que a última tentativa ainda diz o estado agora.</summary>
    public TimeSpan JanelaDeValidade => TimeSpan.FromTicks(Math.Max(ConsideradaFora.Ticks, Intervalo.Ticks * 2));

    public void RegistrarSucesso(DateTimeOffset quando)
    {
        lock (_trava)
        {
            _ultimoSucesso = quando;
            _ultimaTentativa = quando;
            _tipoDaUltimaTentativa = TipoDeFalha.Nenhuma;
            _ultimaFalha = null;
        }
    }

    /// <summary>Registra uma falha, com o tipo que diz o que deu errado (ver <see cref="TipoDeFalhaHttp"/>).</summary>
    public void RegistrarFalha(string motivo, TipoDeFalha tipo, DateTimeOffset quando)
    {
        lock (_trava)
        {
            _ultimaTentativa = quando;
            _tipoDaUltimaTentativa = tipo;
            _ultimaFalha = motivo;
        }
    }

    /// <summary>A internet, medida pela última tentativa recente.</summary>
    public InternetMedida Internet(DateTimeOffset agora)
    {
        if (!TemTentativaRecente(agora))
        {
            return InternetMedida.Desconhecida;
        }

        return TipoDaUltimaTentativa switch
        {
            // O destino respondeu (com sucesso, com recusa de credencial ou com erro de servidor):
            // a rede está de pé.
            TipoDeFalha.Nenhuma or TipoDeFalha.Autenticacao or TipoDeFalha.Servidor => InternetMedida.Disponivel,
            TipoDeFalha.Rede => InternetMedida.Indisponivel,
            _ => InternetMedida.Desconhecida,
        };
    }

    /// <summary>O estado da nuvem agora.</summary>
    public NuvemMedida Nuvem(DateTimeOffset agora)
    {
        if (!Configurada)
        {
            return NuvemMedida.NaoConfigurada;
        }

        if (SegredoAusente)
        {
            return NuvemMedida.SegredoAusente;
        }

        if (UltimaTentativa is null)
        {
            return NuvemMedida.Conectando;
        }

        if (!TemTentativaRecente(agora))
        {
            return NuvemMedida.Desconhecida;
        }

        return TipoDaUltimaTentativa switch
        {
            TipoDeFalha.Nenhuma => NuvemMedida.Conectada,
            TipoDeFalha.Autenticacao => NuvemMedida.FalhaDeAutenticacao,
            TipoDeFalha.Rede or TipoDeFalha.Servidor => NuvemMedida.Indisponivel,
            _ => NuvemMedida.Desconhecida,
        };
    }

    /// <summary>A fila de sincronização, dado quantos itens esperam.</summary>
    public SincronizacaoMedida Sincronizacao(long pendentes)
    {
        if (EmAndamento)
        {
            return SincronizacaoMedida.Processando;
        }

        if (!Configurada)
        {
            return SincronizacaoMedida.Desconhecida;
        }

        // Qualquer falha na última rodada conta, mesmo antiga: a fila só volta a "sincronizada" com
        // um sucesso.
        if (UltimaTentativa is not null && TipoDaUltimaTentativa != TipoDeFalha.Nenhuma)
        {
            return SincronizacaoMedida.Falha;
        }

        if (pendentes > 0)
        {
            return SincronizacaoMedida.Pendente;
        }

        return UltimoSucesso is null ? SincronizacaoMedida.Desconhecida : SincronizacaoMedida.Sincronizada;
    }

    private bool TemTentativaRecente(DateTimeOffset agora)
    {
        var tentativa = UltimaTentativa;
        return tentativa is { } t && agora - t <= JanelaDeValidade;
    }
}
