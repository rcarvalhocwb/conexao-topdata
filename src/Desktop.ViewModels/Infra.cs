using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Desktop.ViewModels;

/// <summary>Base das ViewModels: avisa a tela quando uma propriedade muda.</summary>
public abstract class Notificavel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Muda o campo e avisa, só se o valor mudou.</summary>
    protected bool Definir<T>(ref T campo, T valor, [CallerMemberName] string? propriedade = null)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor))
        {
            return false;
        }

        campo = valor;
        Avisar(propriedade);
        return true;
    }

    protected void Avisar([CallerMemberName] string? propriedade = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propriedade));
}

/// <summary>
/// Botão da tela que chama o serviço. Enquanto roda, fica desabilitado — o operador não
/// consegue mandar a mesma coisa duas vezes clicando rápido.
/// </summary>
public sealed class ComandoAssincrono : ICommand
{
    private readonly Func<Task> _executar;
    private readonly Func<bool>? _podeExecutar;
    private bool _executando;

    public ComandoAssincrono(Func<Task> executar, Func<bool>? podeExecutar = null)
    {
        ArgumentNullException.ThrowIfNull(executar);
        _executar = executar;
        _podeExecutar = podeExecutar;
    }

    public event EventHandler? CanExecuteChanged;

    public bool Executando => _executando;

    public bool CanExecute(object? parameter) => !_executando && (_podeExecutar?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecutarAsync().ConfigureAwait(true);

    /// <summary>Executa aguardando; é o que os testes chamam.</summary>
    public async Task ExecutarAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        _executando = true;
        ReavaliarDisponibilidade();

        try
        {
            await _executar().ConfigureAwait(true);
        }
        finally
        {
            _executando = false;
            ReavaliarDisponibilidade();
        }
    }

    public void ReavaliarDisponibilidade() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Botão de uma linha de lista (o cartão de uma catraca, por exemplo): recebe o item como
/// parâmetro.
/// </summary>
public sealed class ComandoComParametro : ICommand
{
    private readonly Func<object?, Task> _executar;

    public ComandoComParametro(Func<object?, Task> executar)
    {
        ArgumentNullException.ThrowIfNull(executar);
        _executar = executar;
    }

    // Sempre disponível: não há estado que o desabilite.
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter) => await ExecutarAsync(parameter).ConfigureAwait(true);

    /// <summary>Executa aguardando; é o que os testes chamam.</summary>
    public Task ExecutarAsync(object? parameter) => _executar(parameter);
}

/// <summary>Uma tela do menu lateral.</summary>
public interface ITela
{
    /// <summary>Nome no menu.</summary>
    string Titulo { get; }

    /// <summary>Verdadeiro enquanto a tela busca dados (a barra de carregamento aparece).</summary>
    bool Ocupada { get; }

    /// <summary>Busca de novo o que a tela mostra. Nunca lança: falha vira mensagem.</summary>
    Task AtualizarAsync(CancellationToken cancelamento = default);
}

/// <summary>Cores de situação usadas em todas as telas.</summary>
public enum Sinal
{
    Neutro,
    Bom,
    Atencao,
    Problema,
}

/// <summary>Um rótulo e seu valor, numa linha de detalhe da tela.</summary>
/// <remarks>Record, e não tupla: o WPF só liga a propriedades, e tupla tem campos.</remarks>
public sealed record ParDeTexto(string Rotulo, string Valor);
