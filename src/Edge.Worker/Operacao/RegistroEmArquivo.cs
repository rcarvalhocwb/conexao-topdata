using System.Globalization;
using System.Text;

namespace Edge.Worker.Operacao;

/// <summary>
/// Registro de operação em arquivo, um por dia, com hora em cada linha.
/// </summary>
/// <remarks>
/// <para>
/// Recebe só texto já mascarado — quem escreve aqui é a <see cref="SessaoDeOperacao"/>,
/// que nunca passa código inteiro. O arquivo é aberto e fechado a cada linha: um worker
/// morto por <c>Kill</c> não deixa linha perdida em buffer, e o arquivo pode ser lido
/// pelo painel a qualquer momento.
/// </para>
/// <para>
/// Falha ao escrever <b>não</b> derruba nada: a catraca é mais importante que o log.
/// </para>
/// </remarks>
public sealed class RegistroEmArquivo
{
    private readonly string _pasta;
    private readonly string _prefixo;
    private readonly Func<DateTimeOffset> _relogio;
    private readonly Lock _trava = new();

    public RegistroEmArquivo(string pasta, string prefixo, Func<DateTimeOffset>? relogio = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pasta);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefixo);

        _pasta = pasta;
        _prefixo = prefixo;
        _relogio = relogio ?? (() => DateTimeOffset.Now);
    }

    /// <summary>Linhas que não puderam ser gravadas.</summary>
    public long Perdidas { get; private set; }

    /// <summary>Caminho do arquivo do dia.</summary>
    public string ArquivoDoDia(DateTimeOffset quando) =>
        Path.Combine(_pasta, string.Create(CultureInfo.InvariantCulture, $"{_prefixo}-{quando:yyyy-MM-dd}.log"));

    /// <summary>Acrescenta uma linha.</summary>
    public void Escrever(string linha)
    {
        ArgumentNullException.ThrowIfNull(linha);

        var agora = _relogio();
        var texto = string.Create(CultureInfo.InvariantCulture, $"{agora:HH:mm:ss.fff} {linha}{Environment.NewLine}");

        lock (_trava)
        {
            try
            {
                Directory.CreateDirectory(_pasta);
                File.AppendAllText(ArquivoDoDia(agora), texto, Encoding.UTF8);
            }
            catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
            {
                Perdidas++;
            }
        }
    }
}
