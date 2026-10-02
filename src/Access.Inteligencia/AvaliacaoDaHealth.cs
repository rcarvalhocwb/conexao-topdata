using System;
using System.Collections.Generic;

namespace Access.Inteligencia;

/// <summary>
/// Nível de um sinal de saúde.
/// </summary>
public enum NivelDeSinal
{
    Normal = 0,
    Atencao,
    Acao,
    SemDados,
    Aprendendo
}

/// <summary>
/// Tipo de sinal monitorado.
/// </summary>
public enum TipoDeSinal
{
    Giro = 0,
    Relogio,
    Configuracao,
    Comunicacao,
    Leitura,
    GiroSemPedido,
    Laco
}

/// <summary>
/// Um sinal de saúde de uma catraca.
/// </summary>
public class SinalDeSaude
{
    public TipoDeSinal Tipo { get; set; }
    public NivelDeSinal Nivel { get; set; }
    public string Resumo { get; set; } = string.Empty;
    public double? Valor { get; set; }
    public double? Referencia { get; set; }
    public long? Amostras { get; set; }
    public DateTimeOffset? Desde { get; set; }
    public DateTimeOffset? Ate { get; set; }
    public string OQueHazer { get; set; } = string.Empty;
}

/// <summary>
/// Avalia os sinais de saúde de uma catraca (Etapa I.3 do docs/36: IN-01).
/// Cada sinal tem nível (Normal, Atenção, Ação, SemDados, Aprendendo), uma conta explicável
/// e recomendação. Os sinais comparados com as vizinhas na mesma janela cancelam a demanda.
/// </summary>
public class AvaliacaoDaHealth
{
    private readonly TimeProvider _relogio;

    public AvaliacaoDaHealth(TimeProvider? relogio = null)
    {
        _relogio = relogio ?? TimeProvider.System;
    }

    /// <summary>
    /// Avalia o sinal de Giro: taxa de "sem giro" por Wilson.
    /// Regra (docs/36 §4.5): janela 15 min, comparação de medianas, razão ≥ 1,5, n ≥ 30 de cada lado.
    /// </summary>
    public SinalDeSaude AvaliarGiro(double taxaSemGiro, int amostras, double? taxaVizinhas = null)
    {
        var sinal = new SinalDeSaude { Tipo = TipoDeSinal.Giro, Valor = taxaSemGiro, Amostras = amostras };

        if (amostras < 30)
        {
            sinal.Nivel = NivelDeSinal.Aprendendo;
            sinal.Resumo = $"Poucos dados ainda ({amostras} giros; precisa de 30).";
            sinal.OQueHazer = "Coletando dados.";
            return sinal;
        }

        if (taxaVizinhas is null)
        {
            sinal.Nivel = NivelDeSinal.Atencao;
            sinal.Resumo = $"Taxa de sem-giro em {taxaSemGiro * 100:F1}%; sem vizinhas para comparar.";
            sinal.OQueHazer = "Verifique se há catracas vizinhas.";
            sinal.Referencia = 0.05; // 5% por padrão
            return sinal;
        }

        var margem = 0.1; // 10% de margem
        if (Math.Abs(taxaSemGiro - taxaVizinhas.Value) <= margem)
        {
            sinal.Nivel = NivelDeSinal.Normal;
            sinal.Resumo = $"Taxa de sem-giro em {taxaSemGiro * 100:F1}% (esperado ~{taxaVizinhas.Value * 100:F1}%).";
            sinal.OQueHazer = "Operação normal.";
            sinal.Referencia = taxaVizinhas;
            return sinal;
        }

        if (taxaSemGiro > taxaVizinhas.Value + 0.05) // > 5% acima
        {
            sinal.Nivel = NivelDeSinal.Acao;
            sinal.Resumo = $"Taxa de sem-giro {taxaSemGiro * 100:F1}% é significativamente maior que as vizinhas (~{taxaVizinhas.Value * 100:F1}%).";
            sinal.OQueHazer = "Verifique o braço e o sentido de abertura.";
            sinal.Referencia = taxaVizinhas;
            return sinal;
        }

        sinal.Nivel = NivelDeSinal.Atencao;
        sinal.Resumo = $"Taxa de sem-giro em {taxaSemGiro * 100:F1}% (vizinhas: ~{taxaVizinhas.Value * 100:F1}%).";
        sinal.OQueHazer = "Monitore a situação.";
        sinal.Referencia = taxaVizinhas;
        return sinal;
    }

    /// <summary>
    /// Avalia o sinal de Relógio: divergência > 30s ou inclinação > 2s/h.
    /// </summary>
    public SinalDeSaude AvaliarRelogio(int? divergenciaSegundos, double? inclinacao2Sh = null)
    {
        var sinal = new SinalDeSaude { Tipo = TipoDeSinal.Relogio };

        if (divergenciaSegundos is null)
        {
            sinal.Nivel = NivelDeSinal.SemDados;
            sinal.Resumo = "Sem informação de relógio.";
            sinal.OQueHazer = "Aguardando dados.";
            return sinal;
        }

        var divergencia = Math.Abs(divergenciaSegundos.Value);
        const int tolerancia = 30; // segundos

        if (divergencia > tolerancia)
        {
            sinal.Nivel = NivelDeSinal.Acao;
            sinal.Resumo = $"Relógio divergindo em {divergencia} segundos (tolerância: {tolerancia}s).";
            sinal.OQueHazer = "Sincronize o relógio da catraca.";
            sinal.Valor = divergencia;
            sinal.Referencia = tolerancia;
            return sinal;
        }

        sinal.Nivel = NivelDeSinal.Normal;
        sinal.Resumo = $"Relógio dentro do padrão ({divergencia}s de divergência).";
        sinal.OQueHazer = "Operação normal.";
        sinal.Valor = divergencia;
        sinal.Referencia = tolerancia;
        return sinal;
    }

    /// <summary>
    /// Avalia o sinal de Configuração: versão salva ≠ aplicada há > 2 min ou firmware mudou.
    /// </summary>
    public SinalDeSaude AvaliarConfiguracao(string? savedVersion, string? appliedVersion, bool firmwareMudou, DateTimeOffset? ultimaMudanca = null)
    {
        var sinal = new SinalDeSaude { Tipo = TipoDeSinal.Configuracao };

        if (firmwareMudou)
        {
            sinal.Nivel = NivelDeSinal.Atencao;
            sinal.Resumo = "Firmware mudou. Confira o sentido no Mapa de giro.";
            sinal.OQueHazer = "Verifique se o firmware está correto.";
            return sinal;
        }

        if (savedVersion != appliedVersion)
        {
            ultimaMudanca ??= _relogio.GetUtcNow();
            var atraso = _relogio.GetUtcNow() - ultimaMudanca.Value;
            
            if (atraso > TimeSpan.FromMinutes(2))
            {
                sinal.Nivel = NivelDeSinal.Acao;
                sinal.Resumo = $"Configuração salva ({savedVersion}) não está aplicada ({appliedVersion}); esperando há {atraso.TotalMinutes:F0} min.";
                sinal.OQueHazer = "Aplique a configuração à catraca.";
                sinal.Valor = atraso.TotalSeconds;
                sinal.Referencia = 120;
                return sinal;
            }

            sinal.Nivel = NivelDeSinal.Atencao;
            sinal.Resumo = $"Configuração pendente de aplicação ({savedVersion} → {appliedVersion}).";
            sinal.OQueHazer = "A aplicação está em andamento.";
            sinal.Valor = atraso.TotalSeconds;
            sinal.Referencia = 120;
            return sinal;
        }

        sinal.Nivel = NivelDeSinal.Normal;
        sinal.Resumo = $"Configuração em dia (v{appliedVersion}).";
        sinal.OQueHazer = "Operação normal.";
        return sinal;
    }

    /// <summary>
    /// Avalia o sinal de Comunicação (vazio para I.3, será preenchido em I.5).
    /// </summary>
    public SinalDeSaude AvaliarComunicacao()
    {
        return new SinalDeSaude
        {
            Tipo = TipoDeSinal.Comunicacao,
            Nivel = NivelDeSinal.SemDados,
            Resumo = "Comunicação será avaliada em I.5 com dados do coletor.",
            OQueHazer = "Aguardando implementação de I.5."
        };
    }
}

/// <summary>
/// Parâmetros versionados para as regras de saúde (docs/36 §4.5).
/// O hash deste objeto vai em cada alerta para reprodutibilidade (invariante I5).
/// </summary>
public class ParametrosDeInteligencia
{
    public string Versao { get; set; } = "1.0";

    // Giro
    public int GiroMinimoAmostras { get; set; } = 30;
    public double GiroTaxaLimite { get; set; } = 0.05; // 5%

    // Relógio
    public int RelogioToleranciaDivergencia { get; set; } = 30; // segundos
    public double RelogioToleranciaDirecao { get; set; } = 2.0; // segundos por hora
}

/// <summary>
/// Um agregado de minuto para uma catraca (lido de agg_minute).
/// </summary>
public class AgrMinuto
{
    public int InnerNumber { get; set; }
    public string Minute { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public int Reads { get; set; }
    public int Liberados { get; set; }
    public int Negados { get; set; }
    public int Giros { get; set; }
    public int SemGiro { get; set; }
    public int EmptyReads { get; set; }
    public int UnknownCodes { get; set; }
}
