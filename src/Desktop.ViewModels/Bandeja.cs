
namespace Desktop.ViewModels;

/// <summary>Um aviso que sobe da bandeja (notificação do Windows).</summary>
/// <param name="Titulo">Título curto.</param>
/// <param name="Texto">O que aconteceu e o que fazer.</param>
/// <param name="Problema">Verdadeiro para problema; falso para "voltou ao normal".</param>
public sealed record AvisoDaBandeja(string Titulo, string Texto, bool Problema);

/// <summary>
/// O que o ícone perto do relógio mostra: o texto ao passar o mouse, e os avisos quando uma
/// catraca para de atender ou o serviço some.
/// </summary>
/// <remarks>
/// Fica aqui, e não no WPF, para ser testado sem Windows. Só avisa <b>mudança</b>: uma
/// catraca parada há horas não gera um aviso a cada 2 segundos.
/// </remarks>
public sealed class ResumoDaBandeja
{
    /// <summary>O Windows corta o texto do ícone da bandeja em 127 caracteres.</summary>
    public const int LimiteDaDica = 127;

    private HashSet<int>? _atendendo;
    private bool? _servicoOk;

    /// <summary>O texto ao passar o mouse sobre o ícone.</summary>
    public static string Dica(PainelAoVivoViewModel painel)
    {
        ArgumentNullException.ThrowIfNull(painel);

        var texto = painel.ServicoSinal is Sinal.Problema
            ? $"Rayzer XAcess · serviço local: {painel.ServicoResumo}"
            : $"Rayzer XAcess · catracas {painel.CatracasResumo} · serviço {painel.ServicoResumo.ToLowerInvariant()}";

        return texto.Length <= LimiteDaDica ? texto : texto[..(LimiteDaDica - 1)] + "…";
    }

    /// <summary>
    /// Olha o painel depois de uma atualização e devolve o aviso a mostrar, se algo mudou
    /// para pior (ou o serviço voltou). A primeira observação só registra.
    /// </summary>
    public AvisoDaBandeja? Observar(PainelAoVivoViewModel painel)
    {
        ArgumentNullException.ThrowIfNull(painel);

        var servicoOk = painel.ServicoSinal is not Sinal.Problema;
        var atendendo = painel.Catracas.Where(c => c.Sinal is Sinal.Bom).Select(c => c.Inner).ToHashSet();
        var anteriorServico = _servicoOk;
        var anteriores = _atendendo;
        _servicoOk = servicoOk;

        // Sem serviço não há lista de catracas confiável: guarda a última boa para comparar
        // quando ele voltar.
        if (servicoOk)
        {
            _atendendo = atendendo;
        }

        if (anteriorServico is true && !servicoOk)
        {
            return new AvisoDaBandeja(
                "Serviço local sem resposta",
                "As catracas podem ter parado de atender. Abra o painel ou inicie a operação pelo ícone perto do relógio.",
                Problema: true);
        }

        if (anteriorServico is false && servicoOk)
        {
            return new AvisoDaBandeja("Serviço local de volta", $"Catracas {painel.CatracasResumo}.", Problema: false);
        }

        if (!servicoOk || anteriores is null)
        {
            return null;
        }

        var pararam = painel.Catracas.Where(c => anteriores.Contains(c.Inner) && !atendendo.Contains(c.Inner)).ToList();
        if (pararam.Count > 0)
        {
            var nomes = string.Join(", ", pararam.Select(c => c.Nome));
            return new AvisoDaBandeja(
                pararam.Count == 1 ? "Catraca parou de atender" : $"{pararam.Count} catracas pararam de atender",
                $"{nomes}: {pararam[0].Situacao}.",
                Problema: true);
        }

        return null;
    }

    /// <summary>A mensagem depois de pedir para parar ou iniciar a operação.</summary>
    /// <param name="parar">Verdadeiro: pediu para parar.</param>
    /// <param name="codigo">Código de saída do assistente; nulo se o operador recusou a permissão.</param>
    public static string ResultadoDoControle(bool parar, int? codigo) => codigo switch
    {
        null => "Nada mudou: a permissão de administrador não foi dada.",
        0 when parar => "Operação encerrada. As catracas não estão mais sendo atendidas pelo sistema.",
        0 => "Operação iniciada. As catracas voltam a ser atendidas em alguns segundos.",
        2 => "O serviço do Rayzer XAcess não está instalado neste computador. Reinstale pelo Setup.",
        _ => parar
            ? "Não foi possível encerrar a operação. Tente de novo ou reinicie o computador."
            : "Não foi possível iniciar a operação. Abra o assistente de configuração e confira o ambiente.",
    };
}
