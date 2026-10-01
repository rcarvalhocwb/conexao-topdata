namespace Access.Application.Devices;

/// <summary>
/// O que o worker envia a uma catraca quando a camada dela não serve: na subida, o padrão
/// <b>dela</b> (fábrica + evento); no "Aplicar agora", nada (o comando falha e a catraca segue
/// com a configuração que já tinha).
/// </summary>
/// <remarks>
/// <para>
/// Etapa A.4 do docs/35. A camada da catraca (<see cref="SobreposicoesDaCatraca"/>, tabela
/// <c>device_config</c> da A.3) foi validada quando gravada, mas contra o evento <b>daquele</b>
/// momento: o evento ou o padrão de fábrica podem mudar depois e tornar a combinação inválida.
/// Quem aplica valida de novo, catraca por catraca, e uma catraca com configuração ruim não
/// derruba as outras nem o worker.
/// </para>
/// <para>
/// Destino dos problemas de <b>leitura</b> da camada (valor ilegível na base, que já volta a
/// herdar do evento, ver <c>ConfiguracoesDasCatracas.Ler</c>):
/// </para>
/// <list type="bullet">
/// <item><b>Na subida, só aviso.</b> O campo ilegível já herda do evento e o resto da camada,
/// se o resultado passar em <see cref="DeviceConfiguration.Validar"/>, vale: parar a catraca ou
/// jogar fora a camada inteira por um campo seria pior do que avisar (configuração ruim não
/// para a catraca, como o evento inválido no hospedeiro).</item>
/// <item><b>No "Aplicar agora", qualquer problema faz o comando falhar.</b> O operador pediu
/// para aplicar o que gravou; aplicar outra coisa em silêncio (o campo herdado) daria como
/// aplicado o que não foi. O desfecho <c>Falhou</c> leva os problemas para o histórico do
/// comando, e a catraca não sai de <c>Polling</c>.</item>
/// </list>
/// <para>
/// Função pura, como o <see cref="MontadorDaConfiguracao"/>: quem lê a base (worker x86, via
/// <c>Access.Infrastructure.SQLite</c>) entrega as três camadas e os problemas de leitura.
/// </para>
/// </remarks>
public static class ConfiguracaoComRecuo
{
    /// <summary>A configuração com que a catraca sobe, e os avisos para o registro do worker.</summary>
    /// <param name="padraoDeFabrica">Primeira camada (ver <see cref="PadroesDeFabrica"/>).</param>
    /// <param name="evento">A camada do evento, já validada por quem leu.</param>
    /// <param name="inner">Número da catraca, para os avisos.</param>
    /// <param name="catraca">A camada desta catraca; <see cref="SobreposicoesDaCatraca.Nenhuma"/> sem linha.</param>
    /// <param name="problemasDaLeitura">Campos ilegíveis da camada (cada um já herda do evento).</param>
    /// <returns>
    /// Sempre uma configuração: a própria da catraca, ou o padrão dela quando a própria é
    /// recusada por <see cref="DeviceConfiguration.Validar"/>.
    /// </returns>
    public static (DeviceConfiguration Configuracao, IReadOnlyList<string> Avisos) NaSubida(
        DeviceConfiguration padraoDeFabrica,
        SobreposicoesDoEvento evento,
        int inner,
        SobreposicoesDaCatraca catraca,
        IReadOnlyList<string> problemasDaLeitura)
    {
        ArgumentNullException.ThrowIfNull(padraoDeFabrica);
        ArgumentNullException.ThrowIfNull(evento);
        ArgumentNullException.ThrowIfNull(catraca);
        ArgumentNullException.ThrowIfNull(problemasDaLeitura);

        var avisos = new List<string>(problemasDaLeitura);
        var propria = MontadorDaConfiguracao.Montar(padraoDeFabrica, evento, catraca);
        var recusas = propria.Validar();

        if (recusas.Count == 0)
        {
            return (propria, avisos);
        }

        avisos.Add(
            $"Catraca {inner}: configuração própria recusada, subindo com o padrão dela (fábrica + evento): " +
            string.Join(" ", recusas));

        return (MontadorDaConfiguracao.Montar(padraoDeFabrica, evento, SobreposicoesDaCatraca.Nenhuma), avisos);
    }

    /// <summary>A configuração que o "Aplicar agora" envia à catraca, ou por que não envia.</summary>
    /// <param name="padraoDeFabrica">Primeira camada (ver <see cref="PadroesDeFabrica"/>).</param>
    /// <param name="evento">A camada do evento, relida e já validada por quem leu.</param>
    /// <param name="inner">Número da catraca, para os problemas.</param>
    /// <param name="catraca">A camada desta catraca, relida.</param>
    /// <param name="problemasDaLeitura">Campos ilegíveis da camada.</param>
    /// <returns>
    /// A configuração completa e válida, ou nula com os problemas (leitura e
    /// <see cref="DeviceConfiguration.Validar"/>), que vão para o desfecho do comando.
    /// </returns>
    public static (DeviceConfiguration? Configuracao, IReadOnlyList<string> Problemas) ParaAplicar(
        DeviceConfiguration padraoDeFabrica,
        SobreposicoesDoEvento evento,
        int inner,
        SobreposicoesDaCatraca catraca,
        IReadOnlyList<string> problemasDaLeitura)
    {
        ArgumentNullException.ThrowIfNull(padraoDeFabrica);
        ArgumentNullException.ThrowIfNull(evento);
        ArgumentNullException.ThrowIfNull(catraca);
        ArgumentNullException.ThrowIfNull(problemasDaLeitura);

        var propria = MontadorDaConfiguracao.Montar(padraoDeFabrica, evento, catraca);
        List<string> problemas =
        [
            .. problemasDaLeitura,
            .. propria.Validar().Select(p => $"Catraca {inner}: {p}"),
        ];

        return problemas.Count > 0 ? (null, problemas) : (propria, []);
    }
}
