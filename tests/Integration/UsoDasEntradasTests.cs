using System.Security.Cryptography;
using Access.Application.Devices;
using Access.Domain.Devices;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;

namespace Integration.Tests;

/// <summary>
/// Uso das entradas da catraca (docs/61), pedido da bancada de 15/10/2026: "o leitor da frente só para
/// cartões administrativos ou de funcionários; a urna para os cartões do evento; o teclado desligado ou só
/// para uma situação específica". Base de verdade; dados sintéticos.
/// </summary>
public sealed class UsoDasEntradasTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private const string CartaoDoEvento = "0000777001";
    private const string Cracha = "0000555001";

    private readonly BancoTemporario _banco = new();
    private readonly RepositorioDeIngressos _ingressos;
    private readonly ConfiguracoesDaBorda _configuracoes;

    public UsoDasEntradasTests()
    {
        _banco.Migrar();
        _ingressos = new RepositorioDeIngressos(_banco.Fabrica);
        _configuracoes = new ConfiguracoesDaBorda(_banco.Fabrica);

        _ingressos.RegistrarProvedor(new ProvedorDeIngresso("evento", "Cartões do evento", "raw", "", SomenteNaUrna: true), Agora.AddDays(-1));
        _ingressos.RegistrarProvedor(new ProvedorDeIngresso("site", "Ingressos em QR", "raw", ""), Agora.AddDays(-1));
        _ingressos.Ingerir(
            [
                new IngressoRecebido("evento", "C1", CartaoDoEvento, CartaoDoEvento, Categoria: "inteira"),
                new IngressoRecebido("site", "Q1", "QR-SITE-1", "QR-SITE-1", Categoria: "inteira"),
            ],
            Agora.AddDays(-1));

        var cifra = new CifraDeDadosPessoais(RandomNumberGenerator.GetBytes(CifraDeDadosPessoais.TamanhoDaChave));
        var pessoas = new CadastroDePessoas(_banco.Fabrica, cifra);
        var empresa = new ParametrosDoCadastro(_banco.Fabrica).GravarEmpresa("admin", new Empresa(null, "Acme", null), Agora).Id;
        var pessoa = pessoas.Gravar("admin", new DadosDaPessoa { PerfilId = "colaborador", NomeCompleto = "Pessoa Sintetica", EmpresaId = empresa }, Agora);
        Assert.True(pessoa.Gravado, string.Join(" ", pessoa.Problemas));
        Assert.True(pessoas.AdicionarCredencial("admin", pessoa.Id!, "cartao", Cracha, null, null, Agora).Gravado);
    }

    public void Dispose() => _banco.Dispose();

    private void Usar(string frente, string teclado)
    {
        var (atual, _) = _configuracoes.Ler();
        _configuracoes.Gravar(atual with { UsoDaFrente = frente, UsoDoTeclado = teclado }, Agora);
    }

    private MotivoDoUso Ler(string codigo, KnownEventOrigin origem) =>
        _ingressos.TentarUsar(
            codigo, "portao-1", "inner-1", Agora,
            leitor: origem is KnownEventOrigin.Leitor1 or KnownEventOrigin.Leitor2 ? origem : null,
            origemBruta: (int)origem).Resultado.Motivo;

    [Fact]
    public void Padrao_frente_com_equipe_e_qr_cartao_do_evento_so_na_urna_e_teclado_desligado()
    {
        Assert.Equal(MotivoDoUso.Consumido, Ler(Cracha, KnownEventOrigin.Leitor1));
        Assert.Equal(MotivoDoUso.Consumido, Ler("QR-SITE-1", KnownEventOrigin.QrCode));
        Assert.Equal(MotivoDoUso.ForaDaUrna, Ler(CartaoDoEvento, KnownEventOrigin.Leitor1));
        Assert.Equal(MotivoDoUso.EntradaDesligada, Ler(Cracha, KnownEventOrigin.Teclado));
        Assert.Equal(MotivoDoUso.Consumido, Ler(CartaoDoEvento, KnownEventOrigin.Leitor2));
    }

    [Fact]
    public void Frente_so_da_equipe_recusa_ingresso_e_aceita_cracha()
    {
        Usar(UsoDasEntradas.SoEquipe, UsoDasEntradas.TecladoDesligado);

        Assert.Equal(MotivoDoUso.SomenteEquipe, Ler("QR-SITE-1", KnownEventOrigin.QrCode));
        Assert.Equal(MotivoDoUso.SomenteEquipe, Ler(CartaoDoEvento, KnownEventOrigin.Leitor1));
        Assert.Equal(MotivoDoUso.Consumido, Ler(Cracha, KnownEventOrigin.Leitor1));

        // Negar não consome: o QR continua valendo.
        Usar(UsoDasEntradas.EquipeEIngressos, UsoDasEntradas.TecladoDesligado);
        Assert.Equal(MotivoDoUso.Consumido, Ler("QR-SITE-1", KnownEventOrigin.QrCode));
    }

    [Fact]
    public void Frente_desligada_recusa_tudo_e_vai_a_catraca_como_leitor_1_desabilitado()
    {
        Usar(UsoDasEntradas.Desligada, UsoDasEntradas.TecladoDesligado);

        Assert.Equal(MotivoDoUso.EntradaDesligada, Ler(Cracha, KnownEventOrigin.Leitor1));
        Assert.Equal(MotivoDoUso.EntradaDesligada, Ler("QR-SITE-1", KnownEventOrigin.QrCode));

        var (evento, _) = _configuracoes.Ler();
        var catraca = MontadorDaConfiguracao.Montar(PadroesDeFabrica.TopFit4, evento.ParaACatraca(), SobreposicoesDaCatraca.Nenhuma);
        Assert.Equal(MontadorDaConfiguracao.LeitorDesabilitado, catraca.OperacaoDoLeitor1);
        Assert.Equal(MontadorDaConfiguracao.LeitorSomenteEntrada, catraca.OperacaoDoLeitor2);
        Assert.Empty(catraca.Validar());
    }

    [Fact]
    public void Teclado_so_da_equipe_aceita_codigo_de_pessoa_e_recusa_ingresso_digitado()
    {
        Usar(UsoDasEntradas.EquipeEIngressos, UsoDasEntradas.SoEquipe);

        Assert.Equal(MotivoDoUso.SomenteEquipe, Ler("QR-SITE-1", KnownEventOrigin.Teclado));
        Assert.Equal(MotivoDoUso.Consumido, Ler(Cracha, KnownEventOrigin.Teclado));
    }

    [Fact]
    public void Cracha_na_urna_e_recusado_para_nao_ser_recolhido()
    {
        Assert.Equal(MotivoDoUso.CredencialNaUrna, Ler(Cracha, KnownEventOrigin.Leitor2));

        // E continua valendo na frente.
        Assert.Equal(MotivoDoUso.Consumido, Ler(Cracha, KnownEventOrigin.Leitor1));
    }

    [Fact]
    public void Uso_ilegivel_na_base_volta_ao_padrao_e_e_avisado()
    {
        _configuracoes.GravarValor(ConfiguracoesDaBorda.ChaveUsoDaFrente, "qualquer", Agora);

        var (configuracao, ilegiveis) = _configuracoes.Ler();

        Assert.Equal(UsoDasEntradas.EquipeEIngressos, configuracao.UsoDaFrente);
        Assert.Contains(ConfiguracoesDaBorda.ChaveUsoDaFrente, ilegiveis);
    }
}
