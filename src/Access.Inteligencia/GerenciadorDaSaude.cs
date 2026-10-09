using System;
using System.Collections.Generic;
using System.Threading;

namespace Access.Inteligencia;

/// <summary>
/// Gerencia a saúde de todas as catracas, mantendo em memória o estado dos sinais
/// e permitindo consultas por catraca (para o RPC ObterSaudeDasCatracas).
/// </summary>
public class GerenciadorDaSaude : IDisposable
{
    private readonly Dictionary<int, CachedSaude> _saude = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private DateTimeOffset _ultimaAtualizacao = DateTimeOffset.UtcNow;
    private string _versaoParametros = "1.0";

    /// <summary>
    /// Registro em cache de saúde de uma catraca.
    /// </summary>
    public record CachedSaude(
        int Inner,
        NivelDeSinal NivelGeral,
        int Indice0A100,
        IReadOnlyList<SinalDeSaude> Sinais,
        string Recomendacao,
        DateTimeOffset CalculadoEm);

    /// <summary>
    /// Atualiza a saúde de uma catraca com seus sinais.
    /// </summary>
    public void AtualizarSaude(int inner, NivelDeSinal nivel, int indice, IReadOnlyList<SinalDeSaude> sinais, string recomendacao)
    {
        if (inner < 1 || inner > 99)
            throw new ArgumentException($"Inner deve estar entre 1 e 99, recebido: {inner}", nameof(inner));

        var saude = new CachedSaude(inner, nivel, indice, sinais, recomendacao, DateTimeOffset.UtcNow);

        _lock.EnterWriteLock();
        try
        {
            _saude[inner] = saude;
            _ultimaAtualizacao = DateTimeOffset.UtcNow;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Retorna a saúde de todas as catracas conhecidas.
    /// </summary>
    public IReadOnlyDictionary<int, CachedSaude> ObterTodasAsSaudes()
    {
        _lock.EnterReadLock();
        try
        {
            return new Dictionary<int, CachedSaude>(_saude);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Retorna a saúde de uma catraca específica, ou null se não há dados.
    /// </summary>
    public CachedSaude? ObterSaude(int inner)
    {
        _lock.EnterReadLock();
        try
        {
            _saude.TryGetValue(inner, out var saude);
            return saude;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Define a versão dos parâmetros (hash) — vem na resposta do RPC.
    /// </summary>
    public void DefinirVersaoParametros(string versao)
    {
        ArgumentNullException.ThrowIfNull(versao);
        _versaoParametros = versao;
    }

    public string VersaoParametros => _versaoParametros;
    public DateTimeOffset UltimaAtualizacao => _ultimaAtualizacao;

    public void Dispose()
    {
        _lock?.Dispose();
        GC.SuppressFinalize(this);
    }
}
