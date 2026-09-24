using System.Globalization;
using System.Text.Json;

namespace Shared.Observability;

/// <summary>Severidade de um registro de log.</summary>
public enum NivelDeLog
{
    Depuracao,
    Informacao,
    Aviso,
    Erro,
    Critico,
}

/// <summary>Destino de um registro de log já redigido.</summary>
public interface IDestinoDeLog
{
    void Escrever(string linhaJson);
}

/// <summary>Destino que escreve na saída padrão.</summary>
public sealed class DestinoDeConsole : IDestinoDeLog
{
    public void Escrever(string linhaJson) => Console.Out.WriteLine(linhaJson);
}

/// <summary>Destino em memória, para teste e para o pacote de diagnóstico.</summary>
public sealed class DestinoEmMemoria : IDestinoDeLog
{
    private readonly List<string> _linhas = [];
    private readonly Lock _porteiro = new();

    public IReadOnlyList<string> Linhas
    {
        get
        {
            lock (_porteiro)
            {
                return [.. _linhas];
            }
        }
    }

    public void Escrever(string linhaJson)
    {
        lock (_porteiro)
        {
            _linhas.Add(linhaJson);
        }
    }
}

/// <summary>
/// Log estruturado em JSON, com redação aplicada <b>na escrita</b>.
/// </summary>
/// <remarks>
/// Não existe caminho que escreva sem passar pelo redator: é a escolha de desenho que
/// torna o log seguro por construção, em vez de por disciplina.
/// Ver docs/03-arquitetura.md, seção 10.
/// </remarks>
public sealed class LogEstruturado
{
    private static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = false };

    private readonly IDestinoDeLog _destino;
    private readonly Func<DateTimeOffset> _relogio;
    private readonly string _componente;

    public LogEstruturado(IDestinoDeLog destino, string componente, Func<DateTimeOffset>? relogio = null)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentException.ThrowIfNullOrWhiteSpace(componente);

        _destino = destino;
        _componente = componente;
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);
    }

    public void Depuracao(string mensagem, string correlationId, IReadOnlyDictionary<string, object?>? campos = null) =>
        Registrar(NivelDeLog.Depuracao, mensagem, correlationId, campos);

    public void Informacao(string mensagem, string correlationId, IReadOnlyDictionary<string, object?>? campos = null) =>
        Registrar(NivelDeLog.Informacao, mensagem, correlationId, campos);

    public void Aviso(string mensagem, string correlationId, IReadOnlyDictionary<string, object?>? campos = null) =>
        Registrar(NivelDeLog.Aviso, mensagem, correlationId, campos);

    public void Erro(string mensagem, string correlationId, IReadOnlyDictionary<string, object?>? campos = null) =>
        Registrar(NivelDeLog.Erro, mensagem, correlationId, campos);

    public void Critico(string mensagem, string correlationId, IReadOnlyDictionary<string, object?>? campos = null) =>
        Registrar(NivelDeLog.Critico, mensagem, correlationId, campos);

    private void Registrar(
        NivelDeLog nivel,
        string mensagem,
        string correlationId,
        IReadOnlyDictionary<string, object?>? campos)
    {
        ArgumentNullException.ThrowIfNull(mensagem);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var registro = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["t"] = _relogio().ToString("O", CultureInfo.InvariantCulture),
            ["nivel"] = nivel.ToString(),
            ["componente"] = _componente,
            ["correlationId"] = correlationId,

            // Toda mensagem passa pelo redator, sem exceção.
            ["mensagem"] = RedatorDeDadoSensivel.Redigir(mensagem),
        };

        if (campos is not null)
        {
            foreach (var (nome, valor) in campos)
            {
                // E todo campo também, considerando o nome dele.
                registro[nome] = RedatorDeDadoSensivel.RedigirCampo(nome, valor);
            }
        }

        _destino.Escrever(JsonSerializer.Serialize(registro, Opcoes));
    }
}
