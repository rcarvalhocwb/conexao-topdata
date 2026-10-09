using System.Collections.Frozen;

namespace Access.Application.Devices;

/// <summary>Um retorno que a matriz documenta para uma função específica.</summary>
/// <param name="Status">Como o retorno é tratado.</param>
/// <param name="Significado">O que ele quer dizer, com as palavras da matriz.</param>
/// <param name="Fonte">O id da linha em <c>docs/compatibility-matrix/funcoes-easyinner.csv</c>.</param>
public sealed record RetornoDocumentado(AdapterStatus Status, string Significado, string Fonte);

/// <summary>
/// Retornos que só têm sentido junto com a função que os devolveu.
/// </summary>
/// <remarks>
/// <para>
/// O mesmo número muda de sentido de uma função para outra: 3 é "porta já aberta" em
/// <c>AbrirPortaComunicacao</c>, 129 é "ecoar inválido" em <c>HabilitarTeclado</c> e
/// "tempo inválido" em <c>HabilitarMudancaOnLineOffLine</c>. Sem a função, todos caíam em
/// <see cref="AdapterStatus.RetornoDesconhecido"/> (defeito F6, docs/34 §2; ADR-0018).
/// </para>
/// <para>
/// <b>Só entra aqui o que está na coluna <c>retornos_documentados</c> da matriz FUN</b>
/// (<c>funcoes-easyinner.csv</c>), com as palavras dela; um teste de contrato confere as
/// duas direções. A leitura geral "128/129/130 = 1º/2º/3º parâmetro inválido" do anexo 01
/// §1.16 não é aplicada por dedução: <c>ConfigurarLeitor2</c>, que tem um parâmetro só,
/// documenta 129 para "operação inválida".
/// </para>
/// </remarks>
public static class RetornosDocumentados
{
    private static readonly FrozenDictionary<(string Funcao, int Retorno), RetornoDocumentado> Tabela = Montar();

    /// <summary>Todos os pares (função, retorno) documentados.</summary>
    public static IReadOnlyCollection<(string Funcao, int Retorno)> Pares => Tabela.Keys;

    /// <summary>O retorno documentado para a função, ou <c>null</c>.</summary>
    public static RetornoDocumentado? Consultar(string? funcao, int retorno) =>
        funcao is not null && Tabela.TryGetValue((funcao, retorno), out var documentado) ? documentado : null;

    private static FrozenDictionary<(string, int), RetornoDocumentado> Montar()
    {
        var t = new Dictionary<(string, int), RetornoDocumentado>();

        void Recusa(string funcao, int retorno, string motivo, string fonte) =>
            t.Add((funcao, retorno), new RetornoDocumentado(
                AdapterStatus.ConfiguracaoRecusada,
                $"configuração recusada: {funcao} devolveu {retorno} ({motivo})",
                fonte));

        // Comunicação.
        Recusa("DefinirTipoConexao", 9, "tipo de conexão inválido", "EI-001");
        t.Add(("AbrirPortaComunicacao", 2), new(AdapterStatus.ErroDeComunicacao, "porta não aberta", "EI-002"));
        t.Add(("AbrirPortaComunicacao", 3), new(AdapterStatus.PortaJaAberta, "porta já aberta", "EI-002"));
        for (var r = 4; r <= 6; r++)
        {
            // DLL de apoio ausente: como o 8, insistir não adianta; é instalação.
            t.Add(("AbrirPortaComunicacao", r), new(AdapterStatus.FalhaDeDependencia, "DLL de apoio ausente", "EI-002"));
        }

        // Montagem da configuração (buffer global; só vale com EnviarConfiguracoes).
        Recusa("DefinirPadraoCartao", 128, "padrão inválido", "EI-010");
        Recusa("DefinirQuantidadeDigitosCartao", 128, "quantidade inválida", "EI-011");
        Recusa("ConfigurarLeitor1", 128, "operação inválida", "EI-014");
        Recusa("ConfigurarLeitor2", 129, "operação inválida", "EI-015");
        Recusa("HabilitarTeclado", 128, "habilita inválido", "EI-020");
        Recusa("HabilitarTeclado", 129, "ecoar inválido", "EI-020");
        Recusa("RegistrarAcessoNegado", 128, "tipo inválido", "EI-021");
        Recusa("DefinirFuncaoDefaultLeitoresProximidade", 128, "função inválida", "EI-022");
        Recusa("DefinirNumeroCartaoMaster", 128, "número inválido", "EI-023");
        Recusa("HabilitarMudancaOnLineOffLine", 128, "habilita inválido", "EI-028");
        Recusa("HabilitarMudancaOnLineOffLine", 129, "tempo inválido", "EI-028");
        Recusa("DefinirTipoListaAcesso", 128, "tipo inválido", "EI-033");
        Recusa("InserirUsuarioListaAcesso", 128, "padrão ou quantidade do cartão inválido", "EI-034");
        Recusa("InserirUsuarioListaAcesso", 129, "dígitos inválido", "EI-034");
        Recusa("InserirUsuarioListaAcesso", 130, "horário inválido", "EI-034");

        return t.ToFrozenDictionary();
    }
}
