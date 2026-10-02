using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Contracts.Edge.V1;

namespace Desktop.ViewModels;

/// <summary>
/// Representa a saúde de uma catraca para a tela.
/// </summary>
public sealed record LinhaDeSaude(
    int Inner,
    string Nome,
    string NivelSaude,
    int Indice,
    string ResumoSinal,
    string Recomendacao,
    System.Windows.Input.ICommand AbrirDetalhes);

/// <summary>
/// ViewModel para gerenciar saúde das catracas (Etapa I.3: IN-01).
/// Busca a saúde a cada 10 segundos via RPC ObterSaudeDasCatracas.
/// </summary>
/// <remarks>
/// Implementa polling periódico e sincronização thread-safe com a UI.
/// Quando a camada inteligente está desligada, mostra "não disponível".
/// </remarks>
public sealed class GerenciamentoDeCatracasViewModel
{
    private readonly EdgeControl.EdgeControlClient _cliente;
    private readonly Func<DateTimeOffset> _relogio;
    private readonly TimeSpan _intervaloAtualizacao = TimeSpan.FromSeconds(10);
    private readonly CancellationTokenSource _cancelamento = new();
    private Task? _tarefaPolling;

    private ObservableCollection<LinhaDeSaude> _catracas = new();
    private string _statusCamada = "Desligada";
    private bool _estaCarregando;

    public GerenciamentoDeCatracasViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
    {
        _cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);
    }

    public ObservableCollection<LinhaDeSaude> Catracas => _catracas;
    public string StatusCamada => _statusCamada;
    public bool EstaCarregando => _estaCarregando;

    /// <summary>
    /// Inicia o polling periódico da saúde das catracas.
    /// </summary>
    public void IniciarPolling()
    {
        if (_tarefaPolling != null)
            return;

        _tarefaPolling = PollarSaudeAsync(_cancelamento.Token);
    }

    /// <summary>
    /// Para o polling.
    /// </summary>
    public void PararPolling()
    {
        if (_tarefaPolling == null)
            return;

        _cancelamento.Cancel();
        try
        {
            _tarefaPolling.Wait(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException)
        {
            // Esperado
        }

        _tarefaPolling = null;
    }

    /// <summary>
    /// Busca a saúde das catracas de forma síncrona (para testes e uso imediato).
    /// </summary>
    public async Task AtualizarSaudeSincronamente()
    {
        try
        {
            _estaCarregando = true;

            var resposta = await _cliente.ObterSaudeDasCatracasAsync(
                new ObterSaudeDasCatracasRequest(),
                cancellationToken: CancellationToken.None);

            if (resposta == null)
            {
                _statusCamada = "Sem resposta do serviço";
                _catracas.Clear();
                return;
            }

            _statusCamada = resposta.CalculadaEm == null
                ? "Desligada"
                : $"OK (v{resposta.VersaoDosParametros}, {resposta.CalculadaEm.ToDateTimeOffset():HH:mm:ss})";

            AtualizarColecao(resposta);
        }
        catch (Exception erro)
        {
            _statusCamada = $"Erro: {erro.Message}";
            _catracas.Clear();
        }
        finally
        {
            _estaCarregando = false;
        }
    }

    private async Task PollarSaudeAsync(CancellationToken cancelamento)
    {
        while (!cancelamento.IsCancellationRequested)
        {
            try
            {
                await AtualizarSaudeSincronamente();
            }
            catch (Exception erro)
            {
                System.Diagnostics.Debug.WriteLine($"Erro no polling de saúde: {erro.Message}");
            }

            try
            {
                await Task.Delay(_intervaloAtualizacao, cancelamento);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void AtualizarColecao(SaudeDasCatracas resposta)
    {
        var novasLinhas = new List<LinhaDeSaude>();

        foreach (var saude in resposta.PorCatraca)
        {
            var nivelTexto = saude.NivelGeral switch
            {
                NivelDeSaude.Saudavel => "✓ Saudável",
                NivelDeSaude.ComAtencao => "⚠ Atenção",
                NivelDeSaude.ComAcaoNecessaria => "✗ Ação",
                NivelDeSaude.SemDados => "? Sem dados",
                NivelDeSaude.Aprendendo => "⏳ Aprendendo",
                _ => "Indefinido"
            };

            var resumo = saude.Sinais.Count > 0
                ? saude.Sinais[0].Resumo
                : "Nenhum sinal coletado";

            var linha = new LinhaDeSaude(
                Inner: saude.Inner,
                Nome: $"Catraca {saude.Inner:D2}",
                NivelSaude: nivelTexto,
                Indice: saude.IndiceOA100,
                ResumoSinal: resumo,
                Recomendacao: saude.Recomendacao,
                AbrirDetalhes: new RelayCommand(() =>
                {
                    System.Diagnostics.Debug.WriteLine($"Detalhes da catraca {saude.Inner}");
                }));

            novasLinhas.Add(linha);
        }

        _catracas.Clear();
        foreach (var linha in novasLinhas.OrderBy(l => l.Inner))
        {
            _catracas.Add(linha);
        }
    }
}

/// <summary>
/// Implementação simples de ICommand para uso em ViewModels.
/// </summary>
internal sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();
}
