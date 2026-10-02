using System.ComponentModel;
using System.Runtime.CompilerServices;
using Contracts.Edge.V1;
using Grpc.Core;

namespace Desktop.ViewModels;

/// <summary>
/// ViewModel do painel do evento.
/// </summary>
/// <remarks>
/// <para>
/// Não conhece WPF: tudo aqui roda e é testado em qualquer plataforma. A tela só faz
/// ligação de dados.
/// </para>
/// <para>
/// Nenhuma chamada bloqueia: o aplicativo precisa continuar respondendo mesmo quando o
/// serviço local não responde — que é justamente o momento em que o operador mais
/// precisa dele.
/// </para>
/// </remarks>
public sealed class PainelViewModel : INotifyPropertyChanged
{
    private readonly EdgeControl.EdgeControlClient _cliente;
    private readonly Func<DateTimeOffset> _relogio;
    private EstadoDoPainel _estado = EstadoDoPainel.Carregando();
    private IReadOnlyList<Equipamento> _equipamentos = [];

    public PainelViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        _cliente = cliente;
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public EstadoDoPainel Estado
    {
        get => _estado;
        private set
        {
            _estado = value;
            Avisar();
        }
    }

    public IReadOnlyList<Equipamento> Equipamentos
    {
        get => _equipamentos;
        private set
        {
            _equipamentos = value;
            Avisar();
        }
    }

    /// <summary>Quantas vezes o serviço local não respondeu, em sequência.</summary>
    public int FalhasSeguidas { get; private set; }

    /// <summary>
    /// Busca o estado. Nunca lança: falha vira estado visível, não exceção.
    /// </summary>
    /// <remarks>
    /// Uma exceção não tratada aqui fecharia o aplicativo no pior momento possível.
    /// O operador precisa ver "sem resposta do serviço", não uma tela sumindo.
    /// </remarks>
    public async Task AtualizarAsync(CancellationToken cancelamento = default)
    {
        try
        {
            var resposta = await _cliente
                .ObterEstadoAsync(new ObterEstadoRequest(), cancellationToken: cancelamento)
                .ConfigureAwait(false);

            var lista = await _cliente
                .ListarEquipamentosAsync(new ListarEquipamentosRequest(), cancellationToken: cancelamento)
                .ConfigureAwait(false);

            Estado = EstadoDoPainel.De(resposta, _relogio());
            Equipamentos = [.. lista.Equipamentos];
            FalhasSeguidas = 0;
        }
        catch (RpcException erro)
        {
            FalhasSeguidas++;
            Estado = Estado.ComFalhaDeComunicacao($"{erro.StatusCode}: {erro.Status.Detail}", _relogio());
        }
        catch (OperationCanceledException)
        {
            // Fechar a janela durante uma atualização não é erro.
            throw;
        }
    }

    private void Avisar([CallerMemberName] string? propriedade = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propriedade));
}
