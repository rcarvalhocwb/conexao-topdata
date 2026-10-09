using Access.Application.Devices;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// Lê da base o que o worker envia a cada catraca: fábrica → evento (<c>edge_setting</c>) →
/// camada da catraca (<c>device_config</c>, migração 012), uma catraca por vez.
/// </summary>
/// <remarks>
/// <para>
/// Etapa A.4 do docs/35. Serviço e worker conversam pela base (ADR-0024): o painel grava a
/// camada com <see cref="ConfiguracoesDasCatracas.Gravar"/>, e o worker lê aqui, na subida e no
/// "Aplicar agora" daquela catraca. A decisão do que fazer com configuração ruim é a de
/// <see cref="ConfiguracaoComRecuo"/>; esta classe só lê e entrega.
/// </para>
/// <para>
/// Fica fora do hospedeiro x86 (que não carrega em teste) para que a ponta a ponta — gravar a
/// camada, aplicar, ver o que a catraca simulada recebeu — seja testada com a mesma leitura que
/// o worker usa. Nenhuma leitura daqui acontece no caminho do giro: a subida é antes do laço, e
/// o "Aplicar agora" é lido junto com a fila de comandos, como antes (<c>SessaoDeOperacao</c>).
/// </para>
/// </remarks>
public sealed class ConfiguracaoPorCatraca
{
    private readonly SqliteConnectionFactory _fabrica;
    private readonly Func<DeviceConfiguration> _padraoDeFabrica;
    private readonly ConfiguracoesDasCatracas _catracas;
    private readonly MapasDeGiro _mapas;

    /// <param name="fabrica">Conexões com a base local.</param>
    /// <param name="padraoDeFabrica">
    /// A primeira camada. Nulo = <see cref="PadroesDeFabrica.TopFit4"/>, a mesma da validação da
    /// gravação (<see cref="ConfiguracoesDasCatracas"/>).
    /// </param>
    public ConfiguracaoPorCatraca(SqliteConnectionFactory fabrica, Func<DeviceConfiguration>? padraoDeFabrica = null)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
        _padraoDeFabrica = padraoDeFabrica ?? (() => PadroesDeFabrica.TopFit4);
        _catracas = new ConfiguracoesDasCatracas(fabrica, _padraoDeFabrica);
        _mapas = new MapasDeGiro(fabrica);
    }

    /// <summary>
    /// A configuração com que a catraca sobe. Nunca falha: camada ilegível ou recusada vira
    /// aviso e a catraca sobe com o padrão dela (ver <c>ConfiguracaoComRecuo.NaSubida</c>). O mapa
    /// de giro da catraca (migração 017) entra no perfil físico; ilegível, é aviso e aquela origem
    /// segue o padrão.
    /// </summary>
    /// <param name="inner">Número da catraca.</param>
    /// <param name="evento">
    /// A configuração do evento já lida e validada pelo hospedeiro (que cai no padrão quando o
    /// evento é inválido): a mesma para todas as catracas da subida.
    /// </param>
    public (DeviceConfiguration Configuracao, IReadOnlyList<string> Avisos) NaSubida(int inner, ConfiguracaoDaOperacao evento)
    {
        ArgumentNullException.ThrowIfNull(evento);

        SobreposicoesDaCatraca camada;
        MapaDeGiro mapa;
        IReadOnlyList<string> problemas;

        try
        {
            (camada, var daCamada) = _catracas.Ler(inner);
            var gravado = _mapas.Ler(inner);
            mapa = gravado.Mapa;
            problemas = [.. daCamada, .. gravado.Problemas];
        }
        catch (SqliteException erro)
        {
            // Base ocupada na subida: a catraca não fica parada por causa da camada dela.
            camada = SobreposicoesDaCatraca.Nenhuma;
            mapa = MapaDeGiro.Vazio;
            problemas = [$"Catraca {inner}: configuração própria não pôde ser lida ({erro.GetType().Name}); herda do evento."];
        }

        return ConfiguracaoComRecuo.NaSubida(_padraoDeFabrica(), evento.ParaACatraca(), inner, camada, mapa, problemas);
    }

    /// <summary>
    /// Relê o evento e a camada <b>desta</b> catraca para o "Aplicar agora". Nula com os
    /// problemas quando não pode aplicar: o comando falha e a catraca segue com o que tinha.
    /// </summary>
    /// <param name="inner">A catraca do comando.</param>
    public (DeviceConfiguration? Configuracao, IReadOnlyList<string> Problemas) ParaAplicar(int inner)
    {
        try
        {
            // O evento, como antes da A.4: chave ilegível ou valor recusado impede aplicar.
            var (evento, ilegiveis) = new ConfiguracoesDaBorda(_fabrica).Ler();
            List<string> doEvento = [.. ilegiveis.Select(c => $"'{c}' ilegível."), .. evento.Validar()];
            if (doEvento.Count > 0)
            {
                return (null, doEvento);
            }

            var (camada, problemas) = _catracas.Ler(inner);
            var mapa = _mapas.Ler(inner);
            return ConfiguracaoComRecuo.ParaAplicar(
                _padraoDeFabrica(), evento.ParaACatraca(), inner, camada, mapa.Mapa, [.. problemas, .. mapa.Problemas]);
        }
        catch (SqliteException erro)
        {
            // Base ocupada: o comando falha com o motivo, em vez de ficar recebido sem desfecho.
            return (null, [$"a configuração não pôde ser lida agora ({erro.GetType().Name}); peça de novo."]);
        }
    }
}
