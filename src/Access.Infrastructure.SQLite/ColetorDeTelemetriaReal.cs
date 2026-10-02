using System.Globalization;
using Edge.Worker;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// Implementação real do coletor com anel limitado. Nunca bloqueia nem espera por arquivo.
/// Gravação em <c>telemetria.db</c> (Etapa I.1 dos docs/36).
/// </summary>
public sealed class ColetorDeTelemetriaReal : ColetorDeTelemetria
{
    /// <summary>Tamanho do anel em memória para eventos.</summary>
    private const int TamanhodoAnel = 4096;

    private readonly FabricaDaTelemetria _fabrica;
    private readonly Func<DateTimeOffset> _relogio;
    private readonly SinalDaOperacao?[] _anel = new SinalDaOperacao?[TamanhodoAnel];
    private int _posicao;
    private long _descartados;

    // Contadores por minuto (resetados a cada descarregamento)
    private readonly Dictionary<int, long> _errosDeRecepcao = new();
    private readonly Dictionary<int, long> _leiturasVazias = new();
    private readonly Dictionary<int, long> _origensDesconhecidas = new();
    private readonly Dictionary<int, HistogramaDeBaldes> _latenciaDecisao = new();
    private readonly Dictionary<int, HistogramaDeBaldes> _latenciaRecepcao = new();
    private readonly Dictionary<int, HistogramaDeBaldes> _latenciaVolta = new();
    private readonly Dictionary<int, long> _voltasDoLaco = new();
    private readonly Dictionary<int, long> _decisoes = new();
    private readonly Dictionary<int, long> _desviosDoRelogio = new();
    private readonly Dictionary<int, long> _reconexoes = new();
    private readonly Dictionary<int, long> _segundosEmOperacao = new();

    public ColetorDeTelemetriaReal(FabricaDaTelemetria fabrica, Func<DateTimeOffset>? relogio = null)
    {
        ArgumentNullException.ThrowIfNull(fabrica);

        _fabrica = fabrica;
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);
    }

    public override void Enfileirar(SinalDaOperacao sinal)
    {
        ArgumentNullException.ThrowIfNull(sinal);

        int pos = Interlocked.Increment(ref _posicao) & (TamanhodoAnel - 1);
        var anterior = Interlocked.Exchange(ref _anel[pos], sinal);

        if (anterior is not null)
        {
            Interlocked.Increment(ref _descartados);
        }
    }

    public override void ContarErroDeRecepcao(int inner)
    {
        lock (_errosDeRecepcao)
        {
            if (!_errosDeRecepcao.TryGetValue(inner, out var valor))
            {
                _errosDeRecepcao[inner] = 1;
            }
            else
            {
                _errosDeRecepcao[inner] = valor + 1;
            }
        }
    }

    public override void ContarLeituraVazia(int inner)
    {
        lock (_leiturasVazias)
        {
            if (!_leiturasVazias.TryGetValue(inner, out var valor))
            {
                _leiturasVazias[inner] = 1;
            }
            else
            {
                _leiturasVazias[inner] = valor + 1;
            }
        }
    }

    public override void ContarOrigemDesconhecida(int inner)
    {
        lock (_origensDesconhecidas)
        {
            if (!_origensDesconhecidas.TryGetValue(inner, out var valor))
            {
                _origensDesconhecidas[inner] = 1;
            }
            else
            {
                _origensDesconhecidas[inner] = valor + 1;
            }
        }
    }

    public override void RegistrarLatenciaDecisao(int inner, long milissegundos)
    {
        lock (_latenciaDecisao)
        {
            if (!_latenciaDecisao.TryGetValue(inner, out var histo))
            {
                histo = new HistogramaDeBaldes();
                _latenciaDecisao[inner] = histo;
            }

            histo.Registrar(milissegundos);
        }
    }

    public override void RegistrarLatenciaRecepcao(int inner, long milissegundos)
    {
        lock (_latenciaRecepcao)
        {
            if (!_latenciaRecepcao.TryGetValue(inner, out var histo))
            {
                histo = new HistogramaDeBaldes();
                _latenciaRecepcao[inner] = histo;
            }

            histo.Registrar(milissegundos);
        }
    }

    public override void RegistrarLatenciaVolta(int inner, long milissegundos)
    {
        lock (_latenciaVolta)
        {
            if (!_latenciaVolta.TryGetValue(inner, out var histo))
            {
                histo = new HistogramaDeBaldes();
                _latenciaVolta[inner] = histo;
            }

            histo.Registrar(milissegundos);
        }
    }

    public override void ContarVoltaDoLaco(int inner)
    {
        lock (_voltasDoLaco)
        {
            if (!_voltasDoLaco.TryGetValue(inner, out var valor))
            {
                _voltasDoLaco[inner] = 1;
            }
            else
            {
                _voltasDoLaco[inner] = valor + 1;
            }
        }
    }

    public override void ContarDecisao(int inner)
    {
        lock (_decisoes)
        {
            if (!_decisoes.TryGetValue(inner, out var valor))
            {
                _decisoes[inner] = 1;
            }
            else
            {
                _decisoes[inner] = valor + 1;
            }
        }
    }

    public override void RegistrarDesvioDoRelogio(int inner, long segundos)
    {
        lock (_desviosDoRelogio)
        {
            _desviosDoRelogio[inner] = segundos;
        }
    }

    public override void ContarReconexao(int inner)
    {
        lock (_reconexoes)
        {
            if (!_reconexoes.TryGetValue(inner, out var valor))
            {
                _reconexoes[inner] = 1;
            }
            else
            {
                _reconexoes[inner] = valor + 1;
            }
        }
    }

    public override void RegistrarSegundoEmOperacao(int inner)
    {
        lock (_segundosEmOperacao)
        {
            if (!_segundosEmOperacao.TryGetValue(inner, out var valor))
            {
                _segundosEmOperacao[inner] = 1;
            }
            else
            {
                _segundosEmOperacao[inner] = valor + 1;
            }
        }
    }

    public override (int Descarregados, int Descartados) Descarregar(string? sessionId, string worker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worker);

        int descarregados = 0;
        int descartados = (int)Interlocked.Exchange(ref _descartados, 0);

        var sinais = new List<SinalDaOperacao>();
        for (int i = 0; i < TamanhodoAnel; i++)
        {
            var sinal = Interlocked.Exchange(ref _anel[i], null);
            if (sinal is not null)
            {
                sinais.Add(sinal);
            }
        }

        descarregados = sinais.Count;

        // Obter snapshots dos contadores
        Dictionary<int, long> erros, vazias, desconhecidas, desvios, reconexoes, segundos, voltas, decisoes;
        Dictionary<int, HistogramaDeBaldes> latenciaDecisao, latenciaRecepcao, latenciaVolta;

        lock (_errosDeRecepcao)
        {
            erros = new Dictionary<int, long>(_errosDeRecepcao);
            _errosDeRecepcao.Clear();
        }

        lock (_leiturasVazias)
        {
            vazias = new Dictionary<int, long>(_leiturasVazias);
            _leiturasVazias.Clear();
        }

        lock (_origensDesconhecidas)
        {
            desconhecidas = new Dictionary<int, long>(_origensDesconhecidas);
            _origensDesconhecidas.Clear();
        }

        lock (_latenciaDecisao)
        {
            latenciaDecisao = new Dictionary<int, HistogramaDeBaldes>(_latenciaDecisao);
            _latenciaDecisao.Clear();
        }

        lock (_latenciaRecepcao)
        {
            latenciaRecepcao = new Dictionary<int, HistogramaDeBaldes>(_latenciaRecepcao);
            _latenciaRecepcao.Clear();
        }

        lock (_latenciaVolta)
        {
            latenciaVolta = new Dictionary<int, HistogramaDeBaldes>(_latenciaVolta);
            _latenciaVolta.Clear();
        }

        lock (_voltasDoLaco)
        {
            voltas = new Dictionary<int, long>(_voltasDoLaco);
            _voltasDoLaco.Clear();
        }

        lock (_decisoes)
        {
            decisoes = new Dictionary<int, long>(_decisoes);
            _decisoes.Clear();
        }

        lock (_desviosDoRelogio)
        {
            desvios = new Dictionary<int, long>(_desviosDoRelogio);
            _desviosDoRelogio.Clear();
        }

        lock (_reconexoes)
        {
            reconexoes = new Dictionary<int, long>(_reconexoes);
            _reconexoes.Clear();
        }

        lock (_segundosEmOperacao)
        {
            segundos = new Dictionary<int, long>(_segundosEmOperacao);
            _segundosEmOperacao.Clear();
        }

        // Descarregar sinais
        if (sinais.Count > 0 || erros.Count > 0 || vazias.Count > 0 || desconhecidas.Count > 0 || voltas.Count > 0 || decisoes.Count > 0)
        {
            try
            {
                using var conexao = _fabrica.Abrir();
                using var transacao = conexao.BeginTransaction();

                // Descarregar sinais
                if (sinais.Count > 0)
                {
                    using (var comando = conexao.CreateCommand())
                    {
                        comando.Transaction = transacao;
                        comando.CommandText =
                            """
                            INSERT INTO device_signal
                            (id, session_id, inner_number, kind, origin_raw, complement, state_from, state_to, trigger,
                             native_return, firmware, pending_attempt_id, device_time, received_at)
                            VALUES ($id, $sessao, $inner, $tipo, $origem, $compl, $est_ant, $est_novo, $mot,
                                    $ret, $fw, $tent, $hora_eq, $recebido);
                            """;

                        foreach (var sinal in sinais)
                        {
                            comando.Parameters.Clear();
                            comando.Parameters.AddWithValue("$id", sinal.Id);
                            comando.Parameters.AddWithValue("$sessao", (object?)sessionId ?? DBNull.Value);
                            comando.Parameters.AddWithValue("$inner", sinal.Inner);
                            comando.Parameters.AddWithValue("$tipo", sinal.Tipo.ToString().ToLowerInvariant());
                            comando.Parameters.AddWithValue("$origem", sinal.OrigemBruta ?? DBNull.Value);
                            comando.Parameters.AddWithValue("$compl", sinal.Complemento ?? DBNull.Value);
                            comando.Parameters.AddWithValue("$est_ant", sinal.EstadoAnterior ?? DBNull.Value);
                            comando.Parameters.AddWithValue("$est_novo", sinal.EstadoNovo ?? DBNull.Value);
                            comando.Parameters.AddWithValue("$mot", sinal.MotivoTransicao ?? DBNull.Value);
                            comando.Parameters.AddWithValue("$ret", sinal.RetornoNativo ?? DBNull.Value);
                            comando.Parameters.AddWithValue("$fw", sinal.Firmware ?? DBNull.Value);
                            comando.Parameters.AddWithValue("$tent", sinal.IdTentativaPendente ?? DBNull.Value);
                            comando.Parameters.AddWithValue("$hora_eq", sinal.HoraDoEquipamento ?? DBNull.Value);
                            comando.Parameters.AddWithValue("$recebido", sinal.RecebidoEm?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
                            comando.ExecuteNonQuery();
                        }
                    }
                }

                // Descarregar health_minute
                var minutoAtual = _relogio().ToUniversalTime().ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
                var todasAsCatracas = new HashSet<int>();
                todasAsCatracas.UnionWith(erros.Keys);
                todasAsCatracas.UnionWith(vazias.Keys);
                todasAsCatracas.UnionWith(desconhecidas.Keys);
                todasAsCatracas.UnionWith(latenciaDecisao.Keys);
                todasAsCatracas.UnionWith(latenciaRecepcao.Keys);
                todasAsCatracas.UnionWith(latenciaVolta.Keys);
                todasAsCatracas.UnionWith(voltas.Keys);
                todasAsCatracas.UnionWith(decisoes.Keys);
                todasAsCatracas.UnionWith(desvios.Keys);
                todasAsCatracas.UnionWith(reconexoes.Keys);
                todasAsCatracas.UnionWith(segundos.Keys);

                if (todasAsCatracas.Count > 0)
                {
                    using (var comando = conexao.CreateCommand())
                    {
                        comando.Transaction = transacao;
                        comando.CommandText =
                            """
                            INSERT OR REPLACE INTO health_minute
                            (inner_number, minute, session_id, worker, seconds_in_operation, reconnects,
                             recv_errors, empty_reads, unknown_origins, loop_turns, decisions,
                             decision_ms_hist, recv_ms_hist, loop_ms_hist, clock_drift_s, dropped)
                            VALUES ($inner, $min, $sessao, $worker, $seg_op, $recon,
                                    $erros, $vazias, $descon, $voltas, $decisoes,
                                    $lat_dec, $lat_rec, $lat_volta, $desvio, $desc);
                            """;

                        foreach (int inner in todasAsCatracas)
                        {
                            comando.Parameters.Clear();
                            comando.Parameters.AddWithValue("$inner", inner);
                            comando.Parameters.AddWithValue("$min", minutoAtual);
                            comando.Parameters.AddWithValue("$sessao", (object?)sessionId ?? DBNull.Value);
                            comando.Parameters.AddWithValue("$worker", worker);
                            comando.Parameters.AddWithValue("$seg_op", segundos.TryGetValue(inner, out var seg) ? seg : 0);
                            comando.Parameters.AddWithValue("$recon", reconexoes.TryGetValue(inner, out var rec) ? rec : 0);
                            comando.Parameters.AddWithValue("$erros", erros.TryGetValue(inner, out var err) ? err : 0);
                            comando.Parameters.AddWithValue("$vazias", vazias.TryGetValue(inner, out var vaz) ? vaz : 0);
                            comando.Parameters.AddWithValue("$descon", desconhecidas.TryGetValue(inner, out var dsc) ? dsc : 0);
                            comando.Parameters.AddWithValue("$voltas", voltas.TryGetValue(inner, out var vol) ? vol : 0);
                            comando.Parameters.AddWithValue("$decisoes", decisoes.TryGetValue(inner, out var dec) ? dec : 0);
                            comando.Parameters.AddWithValue("$lat_dec", latenciaDecisao.TryGetValue(inner, out var ld) ? ld.SerializarParaJson() : "{}");
                            comando.Parameters.AddWithValue("$lat_rec", latenciaRecepcao.TryGetValue(inner, out var lr) ? lr.SerializarParaJson() : "{}");
                            comando.Parameters.AddWithValue("$lat_volta", latenciaVolta.TryGetValue(inner, out var lv) ? lv.SerializarParaJson() : "{}");
                            comando.Parameters.AddWithValue("$desvio", desvios.TryGetValue(inner, out var dev) ? dev : (long?)DBNull.Value);
                            comando.Parameters.AddWithValue("$desc", descartados);
                            comando.ExecuteNonQuery();
                        }
                    }
                }

                transacao.Commit();
            }
            catch (Exception ex)
            {
                // Falha de disco: nada a fazer senão contar. Invariante I3: a camada parada, lenta,
                // com exceção ou travada nunca muda a sequência nativa.
                System.Diagnostics.Debug.WriteLine($"Falha ao descarregar telemetria: {ex.Message}");
            }
        }

        return (descarregados, descartados);
    }
}
