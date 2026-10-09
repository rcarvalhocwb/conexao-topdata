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
    private readonly long _limiteDiario;
    private string? _arquivoDoDia;
    private long _bytesDoDia;
    private bool _avisouLimite;

    /// <summary>Teto padrão do arquivo de um dia.</summary>
    /// <remarks>
    /// Achado E1-03 do docs/41: o arquivo do dia não tinha teto e fica no mesmo disco da base local.
    /// Uma falha repetida podia encher o disco, e com o disco cheio a base deixa de gravar acessos.
    /// 50 MB por dia e por processo é muito acima de um dia normal de evento.
    /// </remarks>
    public const long LimiteDiarioPadrao = 50L * 1024 * 1024;

    public RegistroEmArquivo(string pasta, string prefixo, Func<DateTimeOffset>? relogio = null, long limiteDiario = LimiteDiarioPadrao)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pasta);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefixo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limiteDiario);

        _pasta = pasta;
        _prefixo = prefixo;
        _relogio = relogio ?? (() => DateTimeOffset.Now);
        _limiteDiario = limiteDiario;
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
                var arquivo = ArquivoDoDia(agora);
                if (!string.Equals(arquivo, _arquivoDoDia, StringComparison.Ordinal))
                {
                    _arquivoDoDia = arquivo;
                    _bytesDoDia = File.Exists(arquivo) ? new FileInfo(arquivo).Length : 0;
                    _avisouLimite = false;
                }

                var bytes = Encoding.UTF8.GetByteCount(texto);
                if (_bytesDoDia + bytes > _limiteDiario)
                {
                    Perdidas++;
                    if (!_avisouLimite)
                    {
                        // Uma linha só, para quem abrir o arquivo saber por que ele para aqui.
                        var aviso = string.Create(CultureInfo.InvariantCulture,
                            $"{agora:HH:mm:ss.fff} registro do dia atingiu o teto de {_limiteDiario / (1024 * 1024)} MB; as linhas seguintes de hoje só são contadas{Environment.NewLine}");
                        File.AppendAllText(arquivo, aviso, Encoding.UTF8);
                        _avisouLimite = true;
                    }

                    return;
                }

                Directory.CreateDirectory(_pasta);
                File.AppendAllText(arquivo, texto, Encoding.UTF8);
                _bytesDoDia += bytes;
            }
            catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
            {
                Perdidas++;
            }
        }
    }
}
