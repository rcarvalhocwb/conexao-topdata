using System.Globalization;
using Contracts.Edge.V1;
using Grpc.Core;

namespace Desktop.ViewModels;

/// <summary>
/// O painel lateral "Por quê?" de uma negação (Etapa I.2 do docs/36, IN-06; desenho em
/// docs/36-anexos/03 §2.3, T4), usado pela lista de Acessos e pelo Painel ao vivo.
/// </summary>
/// <remarks>
/// <para>
/// Mostra o que aconteceu, o que dizer à pessoa e o que fazer, como o serviço explicou. Nenhum
/// código, nem mascarado: a linha da lista já mostra a máscara, e a explicação não precisa dela.
/// </para>
/// <para>
/// Funciona com a camada inteligente desligada: o texto é determinístico e vem do que a decisão
/// já gravou. Falha de comunicação vira texto no próprio painel, nunca exceção.
/// </para>
/// </remarks>
public sealed class PainelPorQue : Notificavel
{
    private readonly EdgeControl.EdgeControlClient _cliente;
    private bool _aberto;
    private string _cabecalho = string.Empty;
    private string _oQueAconteceu = string.Empty;
    private string _oQueDizer = string.Empty;
    private string _oQueFazer = string.Empty;
    private Sinal _sinal = Sinal.Neutro;

    public PainelPorQue(EdgeControl.EdgeControlClient cliente)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        _cliente = cliente;
        Abrir = new ComandoComParametro(p => p is LinhaDeAcesso linha ? ExplicarAsync(linha) : Task.CompletedTask);
        Fechar = new ComandoAssincrono(() =>
        {
            Aberto = false;
            return Task.CompletedTask;
        });
    }

    /// <summary>O botão "Por quê?" da linha (parâmetro: a <see cref="LinhaDeAcesso"/>).</summary>
    public ComandoComParametro Abrir { get; }

    /// <summary>Fecha o painel; a lista volta a ocupar a largura toda.</summary>
    public ComandoAssincrono Fechar { get; }

    /// <summary>O painel está à mostra (360 px, empurrando a lista — docs/36-anexos/03 §2.1, item 7).</summary>
    public bool Aberto { get => _aberto; private set => Definir(ref _aberto, value); }

    /// <summary>"× Negado às 19:44:07 · Catraca 02 · leitor da frente".</summary>
    public string Cabecalho { get => _cabecalho; private set => Definir(ref _cabecalho, value); }

    public string OQueAconteceu { get => _oQueAconteceu; private set => Definir(ref _oQueAconteceu, value); }

    public string OQueDizer { get => _oQueDizer; private set => Definir(ref _oQueDizer, value); }

    public string OQueFazer { get => _oQueFazer; private set => Definir(ref _oQueFazer, value); }

    /// <summary>A cor do cabeçalho: negado (problema) ou liberado sem giro (atenção).</summary>
    public Sinal Sinal { get => _sinal; private set => Definir(ref _sinal, value); }

    /// <summary>Pede a explicação de uma linha ao serviço e abre o painel com ela.</summary>
    public async Task ExplicarAsync(LinhaDeAcesso linha)
    {
        ArgumentNullException.ThrowIfNull(linha);

        try
        {
            var r = await _cliente.ExplicarNegativaAsync(new ExplicarNegativaRequest { EventoId = linha.EventoId });

            if (!r.Encontrada)
            {
                Mostrar(Sinal.Neutro, "Tentativa não encontrada",
                    "Esta tentativa não está na base deste PC.", string.Empty, "Atualize a lista e tente de novo.");
                return;
            }

            var quando = r.Em is null ? string.Empty : $" às {Textos.Hora(r.Em.ToDateTimeOffset())}";
            var onde = string.IsNullOrEmpty(r.OndeFoiLido) ? string.Empty : $" · {r.OndeFoiLido}";
            Mostrar(
                r.Negada ? Sinal.Problema : Sinal.Atencao,
                string.Create(CultureInfo.InvariantCulture, $"{(r.Negada ? "× Negado" : "Liberado")}{quando} · Catraca {r.Inner:D2}{onde}"),
                r.OQueAconteceu,
                r.OQueDizer,
                r.OQueFazer);
        }
        catch (RpcException)
        {
            Mostrar(Sinal.Neutro, "Sem resposta do serviço local",
                "Não foi possível buscar a explicação agora. Tente de novo em instantes.", string.Empty,
                "Confira se o serviço está iniciado e tente de novo.");
        }
    }

    private void Mostrar(Sinal sinal, string cabecalho, string aconteceu, string dizer, string fazer)
    {
        Sinal = sinal;
        Cabecalho = cabecalho;
        OQueAconteceu = aconteceu;
        OQueDizer = dizer;
        OQueFazer = fazer;
        Aberto = true;
    }
}
