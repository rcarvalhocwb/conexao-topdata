namespace Access.Inteligencia;

/// <summary>
/// Regras de alerta (docs/36-anexos/02 §5.2). Cada regra tem seu próprio
/// identificador, dados, conta e limite.
///
/// Etapa I.4: A1–A5 (leitor, comunicação, relógio, configuração, desconhecidos).
/// Etapa I.5+: A6–A15 (outras regras para futuro).
/// </summary>
public enum RegraDeAlerta
{
    /// <summary>Não especificado.</summary>
    NaoEspecificado = 0,

    /// <summary>
    /// A1: Leitor calado (C13 do 01). Nenhuma leitura em ≥3 min, vizinhas leram ≥20.
    /// Probabilidade Poisson: λ ≥ 7, P(0) &lt; 0,1%.
    /// </summary>
    LeitorCalado = 1,

    /// <summary>
    /// A2: Pico de negação. Taxa de negação por motivo crescente.
    /// </summary>
    PicoDeNegacao = 2,

    /// <summary>
    /// A3: Desconhecidos e base atrasada. ≥10 em 5 min + sincronização sem sucesso &gt; 5 min.
    /// </summary>
    Desconhecidos = 3,

    /// <summary>
    /// A4: Reuso ou compartilhamento. Um ingresso em ≥2 catracas.
    /// </summary>
    Reuso = 4,

    /// <summary>
    /// A5: Liberação manual fora do padrão. &gt; max(3, 3 × mediana) por hora.
    /// </summary>
    LiberacaoManualForaPadrao = 5,

    /// <summary>
    /// A6: Comunicação instável (C12 do 01). ≥3 reconexões em 15 min, ou erros de recepção com z ≥ 4.
    /// </summary>
    ComunicacaoInstavel = 6,

    /// <summary>
    /// A7: Relógio derivando. Divergência &gt; 30s ou inclinação &gt; 2s/h.
    /// </summary>
    RelogioDerivando = 7,

    /// <summary>
    /// A8: Configuração não aplicada / equipamento trocado (C9). Salva ≠ aplicada &gt; 2 min.
    /// </summary>
    ConfiguracaoNaoAplicada = 8,

    /// <summary>
    /// A9: Liberou e não girou em série (C4). 3 liberações seguidas sem giro.
    /// </summary>
    LiberouENaoGirou = 9,

    /// <summary>
    /// A10: Giro sem pedido (C3) e correlação (C10). Origem 6 orfã ou ≥2 catracas em 10s.
    /// </summary>
    GiroSemPedido = 10,

    /// <summary>
    /// A11: Urna cheia (C7). Origem 20 ou outro sinal que urna está cheia.
    /// </summary>
    UrnaCheia = 11,

    /// <summary>
    /// A12: Queda simultânea (C10b). ≥2 catracas caíram no mesmo minuto.
    /// </summary>
    QuedaSimultanea = 12,

    /// <summary>
    /// A13: Liberação recusada. A catraca devolveu retorno ≠ 0 em ≥2 em 10 min.
    /// </summary>
    LiberacaoRecusada = 13,

    /// <summary>
    /// A14: Giro no sentido inesperado (com T14 confirmando complemento).
    /// </summary>
    GiroSentidoInesperado = 14,

    /// <summary>
    /// A15: Ocupação alta e redistribuição. ρ ≥ 0,9 com vizinha ≤ 0,6.
    /// </summary>
    OcupacaoAlta = 15,
}

/// <summary>
/// Nível de um sinal ou alerta.
/// </summary>
public enum NivelDeAlerta
{
    /// <summary>Não especificado.</summary>
    NaoEspecificado = 0,

    /// <summary>Dentro do esperado.</summary>
    Normal = 1,

    /// <summary>Alerta: atenção recomendada.</summary>
    Atencao = 2,

    /// <summary>Alerta crítico: ação necessária.</summary>
    Acao = 3,

    /// <summary>Sem dados suficientes.</summary>
    SemDados = 4,

    /// <summary>Aprendendo ainda; mínimo de amostras não atingido.</summary>
    Aprendendo = 5,
}

/// <summary>
/// Um alerta disparado. Ciclo de vida: Aberto → Ciente → Fechado.
/// </summary>
/// <param name="Id">Identificador único (UUIDv7).</param>
/// <param name="Regra">A regra que disparou.</param>
/// <param name="InnerNumber">Número da catraca, ou nulo se alerta de portão.</param>
/// <param name="Portao">Nome do portão, ou nulo se alerta de catraca.</param>
/// <param name="Nivel">Nível do alerta.</param>
/// <param name="Texto">O texto ao operador.</param>
/// <param name="Conta">A conta que disparou, em formato legível (JSON).</param>
/// <param name="AbertaEm">Quando foi disparado.</param>
/// <param name="AtualizadaEm">Última atualização.</param>
/// <param name="FechadaEm">Quando foi fechado (nulo = ainda aberto).</param>
/// <param name="CientePor">Nome do operador que marcou como ciente.</param>
/// <param name="CienteEm">Quando foi marcado como ciente.</param>
/// <param name="VersaoDosParametros">Hash dos parâmetros para reprodutibilidade (I5).</param>
/// <param name="Simulacao">Verdadeiro se veio de catraca simulada.</param>
/// <param name="ElegiavelARele">Verdadeiro se poderia acionar relé 2 no futuro.</param>
public sealed record Alerta(
    string Id,
    RegraDeAlerta Regra,
    int? InnerNumber,
    string? Portao,
    NivelDeAlerta Nivel,
    string Texto,
    string Conta,
    DateTimeOffset AbertaEm,
    DateTimeOffset AtualizadaEm,
    DateTimeOffset? FechadaEm,
    string? CientePor,
    DateTimeOffset? CienteEm,
    string VersaoDosParametros,
    bool Simulacao,
    bool ElegiavelARele)
{
    /// <summary>Verdadeiro se o alerta ainda está aberto (FechadaEm é nulo).</summary>
    public bool EstaAberto => FechadaEm is null;

    /// <summary>Verdadeiro se foi marcado como ciente.</summary>
    public bool EstaAbertoCiente => EstaAberto && CienteEm is not null;
}

/// <summary>
/// Resultado da avaliação de uma regra de alerta (Etapa I.4).
/// </summary>
/// <param name="Nivel">O nível do alerta se disparou.</param>
/// <param name="Texto">O texto ao operador, sem código.</param>
/// <param name="Conta">A conta em formato JSON legível.</param>
public sealed record ResultadoDeRegra(NivelDeAlerta Nivel, string Texto, string Conta);

/// <summary>
/// Métodos avaliadores das 5 regras de alerta da Etapa I.4 (docs/36-anexos/02 §5.2).
/// Funções puras que recebem dados e devolvem resultado.
/// </summary>
public static class RegrasDeAlerta
{
    /// <summary>
    /// A1: Leitor calado (C13 do 01). Nenhuma leitura em ≥3 min, vizinhas leram ≥20.
    /// Probabilidade Poisson: λ ≥ 7, P(0) &lt; 0,1%.
    /// </summary>
    /// <param name="leituras">Contagem de leituras na janela de 3 min.</param>
    /// <param name="leiturasDasVizinhas">Contagem de leituras das vizinhas na mesma janela.</param>
    /// <param name="vizinhasEmOperacao">Quantas vizinhas estão em operação.</param>
    /// <returns>Alerta se dispara; nulo se não dispara.</returns>
    public static ResultadoDeRegra? A1LeitorCalado(int leituras, int leiturasDasVizinhas, int vizinhasEmOperacao)
    {
        const int minVizinhas = 2;
        const int minLeituraDasVizinhas = 20;

        if (vizinhasEmOperacao < minVizinhas)
            return null;

        if (leituras == 0 && leiturasDasVizinhas >= minLeituraDasVizinhas)
        {
            return new ResultadoDeRegra(
                Nivel: NivelDeAlerta.Acao,
                Texto: $"Catraca sem leituras há 3 min; as vizinhas leram {leiturasDasVizinhas}. Confira o leitor e o display.",
                Conta: $"{{\"leituras\": {leituras}, \"vizinhas_leituras\": {leiturasDasVizinhas}, \"vizinhas_operacao\": {vizinhasEmOperacao}}}"
            );
        }

        return null;
    }

    /// <summary>
    /// A2: Comunicação (C12 do 01). ≥3 reconexões em 15 min.
    /// </summary>
    /// <param name="reconexoes">Contagem de reconexões na janela de 15 min.</param>
    /// <returns>Alerta se dispara; nulo se não dispara.</returns>
    public static ResultadoDeRegra? A2Comunicacao(int reconexoes)
    {
        const int limiteReconexoes = 3;

        if (reconexoes >= limiteReconexoes)
        {
            return new ResultadoDeRegra(
                Nivel: NivelDeAlerta.Atencao,
                Texto: $"Catraca reconectou {reconexoes} vezes em 15 min. Confira cabo e porta do switch.",
                Conta: $"{{\"reconexoes_15min\": {reconexoes}}}"
            );
        }

        return null;
    }

    /// <summary>
    /// A3: Relógio. Divergência &gt; 30s ou inclinação &gt; 2s/h.
    /// </summary>
    /// <param name="divergenciaSegundos">Divergência do relógio em segundos.</param>
    /// <param name="inclinacaoSegundosPorHora">Inclinação do relógio em segundos/hora.</param>
    /// <returns>Alerta se dispara; nulo se não dispara.</returns>
    public static ResultadoDeRegra? A3Relogio(int? divergenciaSegundos, double? inclinacaoSegundosPorHora)
    {
        const int limiteDivergencia = 30;
        const double limiteInclinacao = 2.0;

        if (divergenciaSegundos.HasValue && Math.Abs(divergenciaSegundos.Value) > limiteDivergencia)
        {
            var direcao = divergenciaSegundos.Value > 0 ? "adianta" : "atrasa";
            return new ResultadoDeRegra(
                Nivel: NivelDeAlerta.Atencao,
                Texto: $"O relógio da catraca {direcao} cerca de {Math.Abs(divergenciaSegundos.Value)} segundos.",
                Conta: $"{{\"divergencia_segundos\": {divergenciaSegundos}}}"
            );
        }

        if (inclinacaoSegundosPorHora.HasValue && Math.Abs(inclinacaoSegundosPorHora.Value) > limiteInclinacao)
        {
            var direcao = inclinacaoSegundosPorHora.Value > 0 ? "adianta" : "atrasa";
            return new ResultadoDeRegra(
                Nivel: NivelDeAlerta.Atencao,
                Texto: $"O relógio da catraca {direcao} cerca de {Math.Abs(inclinacaoSegundosPorHora.Value):F2} segundos por hora.",
                Conta: $"{{\"inclinacao_s_por_h\": {inclinacaoSegundosPorHora:F2}}}"
            );
        }

        return null;
    }

    /// <summary>
    /// A4: Configuração não aplicada. Salva ≠ aplicada &gt; 2 min.
    /// </summary>
    /// <param name="versaoSalva">Hash da versão salva.</param>
    /// <param name="versaoAplicada">Hash da versão aplicada.</param>
    /// <param name="idadeDasAplicacaoMinutos">Quantos minutos desde a última aplicação.</param>
    /// <returns>Alerta se dispara; nulo se não dispara.</returns>
    public static ResultadoDeRegra? A4Configuracao(string? versaoSalva, string? versaoAplicada, int? idadeDasAplicacaoMinutos)
    {
        const int limiteMinutos = 2;

        if (!string.IsNullOrEmpty(versaoSalva) && versaoSalva != versaoAplicada &&
            idadeDasAplicacaoMinutos.HasValue && idadeDasAplicacaoMinutos.Value > limiteMinutos)
        {
            return new ResultadoDeRegra(
                Nivel: NivelDeAlerta.Atencao,
                Texto: "A catraca não está com a configuração salva.",
                Conta: $"{{\"idade_aplicacao_min\": {idadeDasAplicacaoMinutos}}}"
            );
        }

        return null;
    }

    /// <summary>
    /// A5: Desconhecidos e base atrasada. ≥10 em 5 min + sincronização sem sucesso &gt; 5 min.
    /// </summary>
    /// <param name="desconhecidosEm5Min">Contagem de códigos desconhecidos em 5 min.</param>
    /// <param name="idadeSincronizacaoMinutos">Minutos desde o último sucesso de sincronização.</param>
    /// <returns>Alerta se dispara; nulo se não dispara.</returns>
    public static ResultadoDeRegra? A5Desconhecidos(int desconhecidosEm5Min, int? idadeSincronizacaoMinutos)
    {
        const int limiteDesconhecidos = 10;
        const int limiteSincronizacao = 5;

        if (desconhecidosEm5Min >= limiteDesconhecidos &&
            idadeSincronizacaoMinutos.HasValue && idadeSincronizacaoMinutos.Value > limiteSincronizacao)
        {
            return new ResultadoDeRegra(
                Nivel: NivelDeAlerta.Atencao,
                Texto: $"{desconhecidosEm5Min} códigos desconhecidos em 5 min e a base não atualiza há {idadeSincronizacaoMinutos} min: pode ser lote que não chegou.",
                Conta: $"{{\"desconhecidos_5min\": {desconhecidosEm5Min}, \"idade_sync_min\": {idadeSincronizacaoMinutos}}}"
            );
        }

        return null;
    }
}

/// <summary>
/// Uma regra de alerta de comunicação instável (A6).
/// </summary>
public sealed record RegraA6Comunicacao(
    int Catraca,
    int ReconexoesEm15Min,
    double ZDosErros,
    double P95Latencia,
    double MedianaLatenciaVizinhas)
{
    /// <summary>
    /// A regra dispara se houver ≥3 reconexões em 15 min, ou z dos erros ≥4.
    /// </summary>
    public bool Dispara() => ReconexoesEm15Min >= 3 || ZDosErros >= 4.0;
}

/// <summary>
/// Uma regra de alerta A10: Giro sem pedido (C3) com correlação (C10).
/// </summary>
public sealed record RegraA10GiroSemPedido(
    int Catraca,
    int GirosSemPedidoEm1Hora,
    DateTimeOffset UltimoGiroSemPedido,
    IReadOnlyList<int> CatracasComGiroSemPedidoEm10s)
{
    /// <summary>
    /// A regra dispara se houver ≥3 giros sem pedido na mesma catraca, ou ≥2 catracas em 10s.
    /// </summary>
    public bool Dispara() => GirosSemPedidoEm1Hora >= 3 || CatracasComGiroSemPedidoEm10s.Count >= 2;
}

/// <summary>
/// Uma regra de alerta A12: Queda simultânea (C10b).
/// </summary>
public sealed record RegraA12QuedaSimultanea(
    IReadOnlyList<int> CatracasQueQuairamNoMesmoMinuto)
{
    /// <summary>
    /// A regra dispara se ≥2 catracas caíram no mesmo minuto.
    /// </summary>
    public bool Dispara() => CatracasQueQuairamNoMesmoMinuto.Count >= 2;

    /// <summary>
    /// Possível causa de correlação.
    /// </summary>
    public string TextoParaOperador =>
        CatracasQueQuairamNoMesmoMinuto.Count >= 3
            ? "Catracas caíram juntas: rede ou energia do setor."
            : $"Catracas {string.Join(", ", CatracasQueQuairamNoMesmoMinuto)} caíram juntas: rede ou energia do setor.";
}

/// <summary>
/// Uma regra de alerta A4: Reuso ou compartilhamento de ingresso.
/// </summary>
public sealed record RegraA4Reuso(
    IReadOnlyList<int> Catracas,
    string? TicketIdOuImpressao,
    string Prova,
    string Regra)
{
    /// <summary>
    /// A regra dispara se reuso foi detectado em ≥2 catracas.
    /// </summary>
    public bool Dispara() => Catracas.Count >= 2;

    /// <summary>
    /// Mensagem para o operador: reuso detectado.
    /// </summary>
    public string TextoParaOperador => $"Um ingresso foi usado em múltiplas catracas em tempo suspeito. {Prova}";
}

/// <summary>
/// Uma regra de alerta A13: Liberação recusada pela catraca.
/// </summary>
public sealed record RegraA13LiberacaoRecusada(
    int Catraca,
    int LiberacoesRecusadasEm10Min,
    int UltimoRetornoDaCatraca)
{
    /// <summary>
    /// A regra dispara se ≥2 liberações recusadas em 10 min.
    /// </summary>
    public bool Dispara() => LiberacoesRecusadasEm10Min >= 2;
}
