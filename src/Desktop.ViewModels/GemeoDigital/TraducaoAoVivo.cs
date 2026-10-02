using Contracts.Edge.V1;

namespace Desktop.ViewModels.GemeoDigital;

/// <summary>
/// Traduz o que o serviço conta sobre a catraca real em acontecimentos da cena.
/// </summary>
/// <remarks>
/// <para>
/// O fluxo ao vivo (<c>AcompanharEventos</c>) manda <b>uma tentativa por evento</b>, no
/// momento em que ela é gravada: com a decisão, e com o giro só se ele já tiver acontecido.
/// Por isso, ao vivo, uma liberação sem giro informado volta a travar sozinha depois do
/// tempo de espera pelo giro — e o giro de verdade aparece em Acessos. Está registrado em
/// docs/33-gemeo-digital.md, seção 7, com a mudança de contrato que resolveria.
/// </para>
/// <para>
/// A origem da leitura (QR, leitor da frente, urna) só é usada quando o serviço a informa.
/// Sem ela, a cena mostra a leitura sem desenhar celular nem cartão: melhor nada do que o
/// leitor errado.
/// </para>
/// </remarks>
public static class TraducaoAoVivo
{
    /// <summary>Os acontecimentos que um evento de acesso produz, em ordem.</summary>
    public static IReadOnlyList<SinalDaCena> Sinais(EventoDeAcesso evento)
    {
        ArgumentNullException.ThrowIfNull(evento);

        var sinais = new List<SinalDaCena> { SinalDaCena.Credencial(Leitor(evento)) };

        switch (evento.Resultado)
        {
            case ResultadoDoAcesso.Permitido:
            case ResultadoDoAcesso.Contingencia:
                sinais.Add(SinalDaCena.Liberado());
                if (evento.PassagemConfirmada)
                {
                    sinais.Add(SinalDaCena.Giro());
                }

                break;

            case ResultadoDoAcesso.Negado:
                sinais.Add(SinalDaCena.Negado());
                break;

            default:
                // Em revisão ou sem resultado: a leitura aconteceu; a cena não inventa desfecho.
                break;
        }

        // Evento da própria catraca (origem 20), quando o serviço informar a origem.
        if (evento.OrigemBruta == 20)
        {
            sinais.Add(SinalDaCena.UrnaCheia());
        }

        return sinais;
    }

    /// <summary>Leitor pela origem da Topdata; pelo motivo, quando ele não deixa dúvida.</summary>
    public static LeitorDaCena Leitor(EventoDeAcesso evento)
    {
        ArgumentNullException.ThrowIfNull(evento);

        return evento.OrigemBruta switch
        {
            21 => LeitorDaCena.Qr,
            2 => LeitorDaCena.CartaoNaFrente,
            3 => LeitorDaCena.CartaoNaUrna,
            _ when string.Equals(evento.Motivo, "ForaDaUrna", StringComparison.Ordinal) => LeitorDaCena.CartaoNaFrente,
            _ => LeitorDaCena.Nenhum,
        };
    }

    /// <summary>Conectada ou não, pela situação que o serviço informa.</summary>
    public static SinalDaCena Saude(Equipamento equipamento)
    {
        ArgumentNullException.ThrowIfNull(equipamento);
        return equipamento.EmOperacao ? SinalDaCena.Conectou() : SinalDaCena.PerdeuComunicacao();
    }
}
