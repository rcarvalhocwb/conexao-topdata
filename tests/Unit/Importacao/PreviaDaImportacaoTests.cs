using System.Diagnostics;
using System.Text;
using Access.Domain.Credentials;
using Access.Importacao;
using Xunit.Abstractions;
using static Unit.Tests.Importacao.PlanilhasDeTeste;

namespace Unit.Tests.Importacao;

/// <summary>
/// Etapa B.3 do docs/35: leitura de CSV e .xlsx só com a BCL e a prévia da importação
/// (docs/26 §3). Todo arquivo é gerado aqui; todo código é sintético.
/// </summary>
public sealed class PreviaDaImportacaoTests(ITestOutputHelper saida)
{
    private const string Bilheteria = "bilheteria";

    private static readonly TipoConhecido[] TiposDoEvento =
    [
        new("INTEIRA", "Inteira", 1, true),
        new("MEIA", "Meia-entrada", 2, true),
        new("SOCIAL", "Social", 3, true),
        new("CORTESIA", "Cortesia", 4, false),
    ];

    private static ContextoDaPrevia Contexto(
        CredentialNormalization? perfil = null,
        params CartaoExistente[] existentes) =>
        new(Bilheteria, perfil ?? CredentialNormalization.Raw, TiposDoEvento, existentes);

    private static PreviaDaImportacao PreverCsv(string conteudo, ContextoDaPrevia? contexto = null, bool bom = true)
    {
        using var arquivo = Csv(conteudo, bom);
        return PreviaDaImportacao.DeCsv(arquivo, contexto ?? Contexto());
    }

    private static PreviaDaImportacao PreverXlsx(MemoryStream arquivo, ContextoDaPrevia? contexto = null)
    {
        using (arquivo)
        {
            return PreviaDaImportacao.DeXlsx(arquivo, contexto ?? Contexto());
        }
    }

    /// <summary>Nenhuma mensagem da prévia repete um código (LGPD: docs/34 §6).</summary>
    private static void SemCodigoNasMensagens(PreviaDaImportacao previa, params string[] codigos)
    {
        var mensagens = previa.ProblemasDoArquivo
            .Concat(previa.AvisosDoArquivo)
            .Concat(previa.Linhas.SelectMany(l => l.Erros.Concat(l.Avisos)))
            .Concat(previa.Linhas.Select(l => l.ToString()));

        foreach (var mensagem in mensagens)
        {
            foreach (var codigo in codigos)
            {
                Assert.False(mensagem.Contains(codigo, StringComparison.Ordinal), $"Mensagem com o código em claro: {CredentialValue.Mascarar(codigo)}");
            }
        }
    }

    // --------------------------------------------------------------- zeros e números

    [Fact]
    public void Zeros_a_esquerda_sao_preservados_no_csv()
    {
        var previa = PreverCsv(LinhasCrlf(CabecalhoCsv, "0000000101;INTEIRA;;ATIVO;;;;", "00999900000202;MEIA;;ATIVO;;;;"));

        Assert.False(previa.Recusada);
        Assert.Equal(["0000000101", "00999900000202"], previa.Linhas.Select(l => l.CodigoNormalizado));
        Assert.All(previa.Linhas, l => Assert.Equal(ClasseDaLinha.Novo, l.Classe));
    }

    [Fact]
    public void Zeros_a_esquerda_sao_preservados_no_xlsx_em_texto_compartilhado_e_em_linha()
    {
        var previa = PreverXlsx(XlsxDeCartoes(
            ["0000000101", "INTEIRA", "", "ATIVO"],
            [new C("00999900000202", Tipo.EmLinha), new C("MEIA", Tipo.EmLinha), "", new C("ATIVO", Tipo.EmLinha)]));

        Assert.False(previa.Recusada);
        Assert.Equal(["0000000101", "00999900000202"], previa.Linhas.Select(l => l.CodigoNormalizado));
        Assert.Equal(2, previa.Novos);
    }

    [Theory]
    [InlineData("1,23E+13")]
    [InlineData("1.23457E+11")]
    [InlineData("9,99999E+11")]
    [InlineData("1E+15")]
    [InlineData("999900000101,0")]
    public void Notacao_cientifica_e_numero_convertido_sao_recusados(string codigo)
    {
        var previa = PreverCsv(LinhasCrlf(CabecalhoCsv, $"{codigo};INTEIRA;;ATIVO;;;;"));

        var linha = Assert.Single(previa.Linhas);
        Assert.Equal(ClasseDaLinha.Erro, linha.Classe);
        Assert.Contains(linha.Erros, e => e.Contains("notação científica", StringComparison.Ordinal) || e.Contains(",0", StringComparison.Ordinal));
        Assert.Null(linha.CodigoNormalizado);
        SemCodigoNasMensagens(previa, codigo);
    }

    [Fact]
    public void Notacao_cientifica_tambem_e_recusada_como_texto_no_xlsx()
    {
        var previa = PreverXlsx(XlsxDeCartoes(["1,23E+13", "INTEIRA", "", "ATIVO"]));

        Assert.Equal(ClasseDaLinha.Erro, Assert.Single(previa.Linhas).Classe);
    }

    [Theory]
    [InlineData("999900000101")]      // "parece certo", e mesmo assim é erro: não dá para saber
    [InlineData("101")]               // o que o Excel faz com 0000000101
    [InlineData("1.23E+13")]
    public void Celula_numerica_do_xlsx_e_recusada(string valor)
    {
        var previa = PreverXlsx(XlsxDeCartoes([new C(valor, Tipo.Numero), "INTEIRA", "", "ATIVO"]));

        var linha = Assert.Single(previa.Linhas);
        Assert.Equal(ClasseDaLinha.Erro, linha.Classe);
        Assert.Contains(linha.Erros, e => e.Contains("como Número", StringComparison.Ordinal));
        SemCodigoNasMensagens(previa, valor);
    }

    [Fact]
    public void Codigo_de_formula_e_recusado()
    {
        var previa = PreverXlsx(XlsxDeCartoes([new C("0000000101", Tipo.Formula), "INTEIRA", "", "ATIVO"]));

        Assert.Contains(Assert.Single(previa.Linhas).Erros, e => e.Contains("fórmula", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("9999 000101")]
    [InlineData("9999.000101")]
    [InlineData("9999-000101")]
    [InlineData("'0000000101")]
    public void Codigo_com_caractere_que_a_catraca_nao_le_e_recusado(string codigo)
    {
        var previa = PreverCsv(LinhasCrlf(CabecalhoCsv, $"{codigo};INTEIRA;;ATIVO;;;;"));

        Assert.Equal(ClasseDaLinha.Erro, Assert.Single(previa.Linhas).Classe);
    }

    // --------------------------------------------------------------- formato do CSV

    [Fact]
    public void Com_e_sem_bom_o_resultado_e_o_mesmo()
    {
        var conteudo = LinhasCrlf(CabecalhoCsv, "0000000101;INTEIRA;;ATIVO;;;;");

        var comBom = PreverCsv(conteudo, bom: true);
        var semBom = PreverCsv(conteudo, bom: false);

        Assert.False(comBom.Recusada);
        Assert.False(semBom.Recusada);
        Assert.Empty(comBom.AvisosDoArquivo);   // o BOM não vira coluna "﻿codigo" desconhecida
        Assert.Equal(semBom.Linhas.Select(l => (l.Linha, l.Classe, l.CodigoNormalizado)), comBom.Linhas.Select(l => (l.Linha, l.Classe, l.CodigoNormalizado)));
        Assert.NotEqual(comBom.Sha256, semBom.Sha256);
    }

    [Fact]
    public void Linhas_vazias_sao_ignoradas_e_contadas_e_o_numero_da_linha_e_o_fisico()
    {
        var conteudo = CabecalhoCsv + "\r\n" +
                       "0000000101;INTEIRA;;ATIVO;;;;\r\n" +
                       "\r\n" +
                       ";;;;;;;\r\n" +
                       "0000000202;MEIA;;ATIVO;;;;\n" +      // LF sozinho
                       "0000000303;MEIA;;ATIVO;;;;\r" +      // CR sozinho
                       "0000000404;MEIA;;ATIVO;;;;\r\n" +
                       "\r\n";                                 // vazias do fim não contam

        var previa = PreverCsv(conteudo);

        Assert.Equal([2, 5, 6, 7], previa.Linhas.Select(l => l.Linha));
        Assert.Equal(2, previa.LinhasVazias);
        Assert.Equal(4, previa.Novos);
    }

    [Fact]
    public void Aspas_guardam_ponto_e_virgula_aspas_e_quebra_de_linha()
    {
        var conteudo = LinhasCrlf(
            CabecalhoCsv,
            "0000000101;INTEIRA;;ATIVO;;;;\"sala 2; fila B\"",
            "0000000202;MEIA;;ATIVO;;;;\"disse \"\"volto já\"\"\"",
            "0000000303;MEIA;;ATIVO;;;;\"primeira linha\r\nsegunda linha\"",
            "0000000404;SOCIAL;;ATIVO;;;;");

        using var arquivo = Csv(conteudo);
        var lido = LeitorDeCsv.Ler(arquivo);
        var linhas = lido.Cartoes!.Linhas;

        Assert.Equal("sala 2; fila B", linhas[0][7].Texto);
        Assert.Equal("disse \"volto já\"", linhas[1][7].Texto);
        Assert.Equal("primeira linha\r\nsegunda linha", linhas[2][7].Texto);
        Assert.Equal([2, 3, 4, 6], linhas.Select(l => l.Numero));   // a quebra dentro das aspas conta

        var previa = PreverCsv(conteudo);
        Assert.Equal(4, previa.Novos);
    }

    [Fact]
    public void Ultima_linha_sem_quebra_no_fim_tambem_e_lida()
    {
        var previa = PreverCsv(CabecalhoCsv + "\r\n0000000101;INTEIRA;;ATIVO;;;;\r\n0000000202;MEIA;;ATIVO;;;;");

        Assert.Equal([2, 3], previa.Linhas.Select(l => l.Linha));
        Assert.Equal(2, previa.Novos);
    }

    [Fact]
    public void Aspas_sem_fechar_recusam_o_arquivo_com_a_linha()
    {
        var previa = PreverCsv(LinhasCrlf(CabecalhoCsv, "0000000101;INTEIRA;;ATIVO;;;;\"sem fim"));

        Assert.True(previa.Recusada);
        Assert.Contains("linha 2", Assert.Single(previa.ProblemasDoArquivo), StringComparison.Ordinal);
    }

    [Fact]
    public void Arquivo_que_nao_e_utf8_e_recusado_com_instrucao()
    {
        // "Solidário" em Windows-1252: o á vira 0xE1, que não é UTF-8 válido sozinho.
        var ansi = Encoding.Latin1.GetBytes(LinhasCrlf(CabecalhoCsv, "0000000101;INTEIRA;;ATIVO;;;;Solidário"));
        using var arquivo = new MemoryStream(ansi);

        var previa = PreviaDaImportacao.DeCsv(arquivo, Contexto());

        Assert.True(previa.Recusada);
        Assert.Contains("UTF-8", Assert.Single(previa.ProblemasDoArquivo), StringComparison.Ordinal);
    }

    [Fact]
    public void Separador_errado_recusa_o_arquivo()
    {
        var previa = PreverCsv(LinhasCrlf(CabecalhoCsv.Replace(';', ','), "0000000101,INTEIRA,,ATIVO,,,,"));

        Assert.True(previa.Recusada);
        Assert.Contains("ponto e vírgula", Assert.Single(previa.ProblemasDoArquivo), StringComparison.Ordinal);
    }

    [Fact]
    public void Cabecalho_sem_coluna_obrigatoria_ou_com_coluna_repetida_recusa_e_coluna_extra_so_avisa()
    {
        Assert.True(PreverCsv(LinhasCrlf("codigo;titular;situacao", "0000000101;;ATIVO")).Recusada);
        Assert.True(PreverCsv(LinhasCrlf("codigo;tipo;situacao;Código", "0000000101;INTEIRA;ATIVO;x")).Recusada);

        var comExtra = PreverCsv(LinhasCrlf(" CODIGO ;Tipo;Situação;cor", "0000000101;INTEIRA;ATIVO;azul"));
        Assert.False(comExtra.Recusada);
        Assert.Single(comExtra.AvisosDoArquivo);
        Assert.Equal(1, comExtra.Novos);
    }

    // -------------------------------------------------------- repetidos e zeros a mais

    [Fact]
    public void Codigo_repetido_no_arquivo_e_erro_nas_duas_linhas()
    {
        var previa = PreverCsv(LinhasCrlf(
            CabecalhoCsv,
            "0000000101;INTEIRA;;ATIVO;;;;",
            "0000000202;MEIA;;ATIVO;;;;",
            " 0000000101 ;MEIA;;ATIVO;;;;"));   // espaços nas pontas: o mesmo código

        Assert.Equal([ClasseDaLinha.Erro, ClasseDaLinha.Novo, ClasseDaLinha.Erro], previa.Linhas.Select(l => l.Classe));
        Assert.Contains(previa.Linhas[0].Erros, e => e.Contains("linha 4", StringComparison.Ordinal));
        Assert.Contains(previa.Linhas[2].Erros, e => e.Contains("linha 2", StringComparison.Ordinal));
        SemCodigoNasMensagens(previa, "0000000101");
    }

    [Fact]
    public void Mesmo_codigo_com_zeros_a_mais_ou_a_menos_no_arquivo_e_aviso_e_nunca_juncao()
    {
        var previa = PreverCsv(LinhasCrlf(
            CabecalhoCsv,
            "999900000101;SOCIAL;;ATIVO;;;;",
            "00999900000101;SOCIAL;;ATIVO;;;;"));

        Assert.Equal(2, previa.Novos);   // os dois entram como estão: ninguém junta sozinho
        Assert.Contains(previa.Linhas[0].Avisos, a => a.Contains("linha 3", StringComparison.Ordinal) && a.Contains("zeros", StringComparison.Ordinal));
        Assert.Contains(previa.Linhas[1].Avisos, a => a.Contains("linha 2", StringComparison.Ordinal));
        SemCodigoNasMensagens(previa, "999900000101", "00999900000101");
    }

    [Fact]
    public void Mesmo_codigo_com_zeros_a_mais_ou_a_menos_na_base_e_aviso()
    {
        var contexto = Contexto(existentes: new CartaoExistente("00999900000202", Bilheteria, "SOCIAL", SituacaoDoCartao.Valido));

        var previa = PreverCsv(LinhasCrlf(CabecalhoCsv, "999900000202;SOCIAL;;ATIVO;;;;"), contexto);

        var linha = Assert.Single(previa.Linhas);
        Assert.Equal(ClasseDaLinha.Novo, linha.Classe);
        Assert.Contains(linha.Avisos, a => a.Contains("cadastrado", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ perfil e comprimento

    [Fact]
    public void Comprimento_vem_do_perfil_do_provedor_dentro_do_teto()
    {
        var mifare = Contexto(PerfisDeLeitura.MifareCatraca4);

        var previa = PreverCsv(LinhasCrlf(
            CabecalhoCsv,
            "99994567;INTEIRA;;ATIVO;;;;",          // 8 → completado até 10 pelo perfil
            "999900000101;INTEIRA;;ATIVO;;;;"),     // 12: o perfil de 10 não aceita
            mifare);

        Assert.Equal("0099994567", previa.Linhas[0].CodigoNormalizado);
        Assert.Equal(ClasseDaLinha.Novo, previa.Linhas[0].Classe);
        Assert.Contains(previa.Linhas[0].Avisos, a => a.Contains("completado", StringComparison.Ordinal));
        Assert.Equal(ClasseDaLinha.Erro, previa.Linhas[1].Classe);
        Assert.Contains(previa.Linhas[1].Erros, e => e.Contains("mifare-catraca4", StringComparison.Ordinal) && e.Contains("12", StringComparison.Ordinal));
        SemCodigoNasMensagens(previa, "99994567", "0099994567", "999900000101");
    }

    [Theory]
    [InlineData("999", false)]
    [InlineData("9999", true)]
    [InlineData("9999000000000101", true)]
    [InlineData("99990000000001010", false)]
    public void Teto_de_4_a_16_vale_mesmo_no_perfil_sem_limite(string codigo, bool aceito)
    {
        var previa = PreverCsv(LinhasCrlf(CabecalhoCsv, $"{codigo};INTEIRA;;ATIVO;;;;"));

        Assert.Equal(aceito ? ClasseDaLinha.Novo : ClasseDaLinha.Erro, Assert.Single(previa.Linhas).Classe);
    }

    // ------------------------------------------------------------------- LGPD: titular

    [Fact]
    public void Titular_preenchido_e_recusado_e_o_nome_nao_fica_na_previa()
    {
        var previa = PreverCsv(LinhasCrlf(
            CabecalhoCsv,
            "0000000101;INTEIRA;Pessoa Ficticia;ATIVO;;;;",
            "0000000202;INTEIRA;;ATIVO;;;;"));

        Assert.Equal(ClasseDaLinha.Erro, previa.Linhas[0].Classe);
        Assert.Contains(previa.Linhas[0].Erros, e => e.Contains("titular", StringComparison.Ordinal));
        Assert.Equal(ClasseDaLinha.Novo, previa.Linhas[1].Classe);

        var tudo = string.Join("|", previa.Linhas.SelectMany(l => l.Erros.Concat(l.Avisos)));
        Assert.DoesNotContain("Pessoa Ficticia", tudo, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CPF 000.000.000-00")]
    [InlineData("fale com a@b.invalid")]
    [InlineData("(11) 99999-0000")]
    public void Observacao_com_cpf_email_ou_telefone_e_recusada(string observacao)
    {
        var previa = PreverCsv(LinhasCrlf(CabecalhoCsv, $"0000000101;INTEIRA;;ATIVO;;;;{observacao}"));

        Assert.Equal(ClasseDaLinha.Erro, Assert.Single(previa.Linhas).Classe);
    }

    // ------------------------------------------------------- tipo, situação, validade

    [Fact]
    public void Tipo_inexistente_ou_inativo_e_erro_na_linha()
    {
        var previa = PreverCsv(LinhasCrlf(
            CabecalhoCsv,
            "0000000101;VIP;;ATIVO;;;;",
            "0000000202;CORTESIA;;ATIVO;;;;",
            "0000000303;meia;;ATIVO;;;;"));

        Assert.Contains(previa.Linhas[0].Erros, e => e.Contains("VIP não está cadastrado", StringComparison.Ordinal));
        Assert.Contains(previa.Linhas[1].Erros, e => e.Contains("inativo", StringComparison.Ordinal));
        Assert.Equal(ClasseDaLinha.Novo, previa.Linhas[2].Classe);
        Assert.Equal("MEIA", previa.Linhas[2].Dados!.Tipo);
    }

    [Fact]
    public void Tipo_criado_na_aba_tipos_do_mesmo_arquivo_vale_para_os_cartoes()
    {
        var previa = PreverXlsx(Xlsx(
        [
            ("Tipos", [["tipo", "nome_exibido", "ordem", "ativo"], ["SOLIDARIO", "Solidário", new C("5", Tipo.Numero), "SIM"], ["MEIA", "Meia", "2", "SIM"]]),
            ("Cartões", [Cabecalho(), ["0000000101", "SOLIDARIO", "", "ATIVO"]]),
        ]));

        Assert.Equal(ClasseDaLinha.Novo, Assert.Single(previa.Linhas).Classe);
        // SOLIDARIO é novo; MEIA muda o nome exibido. A ordem pode vir como número.
        Assert.Equal([ClasseDaLinha.Novo, ClasseDaLinha.Alterado], previa.Tipos.Select(t => t.Classe));
    }

    [Fact]
    public void Situacao_e_validade_sao_conferidas()
    {
        var previa = PreverCsv(LinhasCrlf(
            CabecalhoCsv,
            "0000000101;INTEIRA;;SUSPENSO;;;;",
            "0000000202;INTEIRA;;ATIVO;31/02/2026 10:00;;;",
            "0000000303;INTEIRA;;ATIVO;05/12/2026 23:00;05/12/2026 18:00;;",
            "0000000404;INTEIRA;;ATIVO;2026-12-05;;;",
            "0000000505;INTEIRA;;bloqueado;05/12/2026 18:00;05/12/2026 23:59;2;"));

        Assert.Equal(
            [ClasseDaLinha.Erro, ClasseDaLinha.Erro, ClasseDaLinha.Erro, ClasseDaLinha.Erro, ClasseDaLinha.Novo],
            previa.Linhas.Select(l => l.Classe));

        // Horário de Brasília (UTC−3) gravado em UTC.
        var dados = previa.Linhas[4].Dados!;
        Assert.True(dados.Bloqueado);
        Assert.Equal(new DateTimeOffset(2026, 12, 5, 21, 0, 0, TimeSpan.Zero), dados.ValidoDe);
        Assert.Equal(2, dados.UsosMaximos);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1,5")]
    [InlineData("dois")]
    public void Usos_maximos_invalidos_sao_recusados(string usos)
    {
        var previa = PreverCsv(LinhasCrlf(CabecalhoCsv, $"0000000101;INTEIRA;;ATIVO;;;{usos};"));

        Assert.Equal(ClasseDaLinha.Erro, Assert.Single(previa.Linhas).Classe);
    }

    [Fact]
    public void Data_e_usos_numericos_do_xlsx_sao_aceitos_nos_dois_sistemas_de_data()
    {
        // 46361,75 = 05/12/2026 18:00 no sistema de 1900; no de 1904 são 1462 dias a menos.
        IReadOnlyList<C> Linha(string serial) =>
            ["0000000101", "INTEIRA", "", "ATIVO", new C(serial, Tipo.Numero), "", new C("3", Tipo.Numero)];

        var de1900 = PreverXlsx(Xlsx([("Cartões", [Cabecalho(), Linha("46361.75")])]));
        var de1904 = PreverXlsx(Xlsx([("Cartões", [Cabecalho(), Linha("44899.75")])], sistema1904: true));

        foreach (var previa in new[] { de1900, de1904 })
        {
            var dados = Assert.Single(previa.Linhas).Dados!;
            Assert.Equal(new DateTimeOffset(2026, 12, 5, 21, 0, 0, TimeSpan.Zero), dados.ValidoDe);
            Assert.Equal(3, dados.UsosMaximos);
        }
    }

    // ------------------------------------------------------ comparação com o cadastro

    [Fact]
    public void Compara_com_o_cadastro_novos_alterados_iguais_e_conflitos()
    {
        var contexto = Contexto(existentes:
        [
            new CartaoExistente("0000000101", Bilheteria, "INTEIRA", SituacaoDoCartao.Valido),
            new CartaoExistente("0000000202", Bilheteria, "INTEIRA", SituacaoDoCartao.Consumido),
            new CartaoExistente("0000000303", Bilheteria, "INTEIRA", SituacaoDoCartao.Bloqueado),
            new CartaoExistente("0000000404", "zet", "INTEIRA", SituacaoDoCartao.Valido),
            new CartaoExistente("0000000505", Bilheteria, "INTEIRA", SituacaoDoCartao.Cancelado),
            new CartaoExistente("0000000606", Bilheteria, "CORTESIA", SituacaoDoCartao.Valido),
        ]);

        var previa = PreverCsv(LinhasCrlf(
            CabecalhoCsv,
            "0000000101;INTEIRA;;ATIVO;;;1;",      // igual
            "0000000202;MEIA;;ATIVO;;;1;",         // tipo muda
            "0000000303;INTEIRA;;ATIVO;;;1;",      // desbloqueia (com aviso)
            "0000000404;INTEIRA;;ATIVO;;;1;",      // de outro provedor
            "0000000505;INTEIRA;;ATIVO;;;1;",      // cancelado
            "0000000606;CORTESIA;;ATIVO;;;;",      // tipo inativo que já é dele; usos passa a sem limite
            "0000000707;INTEIRA;;ATIVO;;;1;"),     // novo
            contexto);

        Assert.Equal(
        [
            ClasseDaLinha.Igual, ClasseDaLinha.Alterado, ClasseDaLinha.Alterado, ClasseDaLinha.Erro,
            ClasseDaLinha.Erro, ClasseDaLinha.Alterado, ClasseDaLinha.Novo,
        ],
            previa.Linhas.Select(l => l.Classe));
        Assert.Equal(["tipo"], previa.Linhas[1].CamposAlterados);
        Assert.Equal(["situacao"], previa.Linhas[2].CamposAlterados);
        Assert.Contains(previa.Linhas[2].Avisos, a => a.Contains("desbloqueia", StringComparison.Ordinal));
        Assert.Contains(previa.Linhas[3].Erros, e => e.Contains("outro provedor", StringComparison.Ordinal));
        Assert.Contains(previa.Linhas[4].Erros, e => e.Contains("cancelado", StringComparison.Ordinal));
        Assert.Equal(["usos_maximos"], previa.Linhas[5].CamposAlterados);
        Assert.Equal((1, 3, 1, 2), (previa.Novos, previa.Alterados, previa.Iguais, previa.ComErro));
    }

    // ---------------------------------------------------------------- pacote .xlsx

    [Fact]
    public void Abas_exemplo_e_instrucoes_nunca_sao_lidas()
    {
        var previa = PreverXlsx(Xlsx(
        [
            ("Instruções", [["Leia antes"]]),
            ("Cartões", [Cabecalho(), ["0000000101", "INTEIRA", "", "ATIVO"]]),
            ("Exemplo", [Cabecalho(), ["999900000001", "INTEIRA", "", "ATIVO"], ["999900000002", "MEIA", "", "ATIVO"]]),
        ]));

        Assert.Equal("0000000101", Assert.Single(previa.Linhas).CodigoNormalizado);
    }

    [Fact]
    public void Linhas_vazias_formatadas_do_modelo_nao_viram_erro()
    {
        // O modelo instalado formata milhares de linhas vazias (<c s="3" t="n"/>).
        IReadOnlyList<C> vazia = [new C("", Tipo.VazioComEstilo), new C("", Tipo.VazioComEstilo)];
        var previa = PreverXlsx(Xlsx(
            [("Cartões", [Cabecalho(), ["0000000101", "INTEIRA", "", "ATIVO"], vazia, null, ["0000000202", "MEIA", "", "ATIVO"], vazia, vazia])]));

        Assert.Equal([2, 5], previa.Linhas.Select(l => l.Linha));
        Assert.Equal(2, previa.LinhasVazias);
        Assert.Equal(2, previa.Novos);
    }

    [Fact]
    public void Pacote_com_macro_ou_que_nao_e_xlsx_e_recusado()
    {
        var comMacro = PreverXlsx(Xlsx([("Cartões", [Cabecalho(), ["0000000101", "INTEIRA", "", "ATIVO"]])], comMacro: true));
        Assert.True(comMacro.Recusada);
        Assert.Contains("macro", Assert.Single(comMacro.ProblemasDoArquivo), StringComparison.Ordinal);

        using var csvDisfarcado = Csv(LinhasCrlf(CabecalhoCsv, "0000000101;INTEIRA;;ATIVO;;;;"));
        Assert.True(PreviaDaImportacao.DeXlsx(csvDisfarcado, Contexto()).Recusada);

        var semCartoes = PreverXlsx(Xlsx([("Tipos", [["tipo"]]), ("Exemplo", [Cabecalho()])]));
        Assert.True(semCartoes.Recusada);
    }

    [Fact]
    public void Pacote_grande_demais_depois_de_descompactado_e_recusado()
    {
        var previa = PreverXlsx(Xlsx(
            [("Cartões", [Cabecalho(), ["0000000101", "INTEIRA", "", "ATIVO"]])],
            bytesDeLixo: LimitesDaImportacao.BytesDescompactados + (1024 * 1024)));

        Assert.True(previa.Recusada);
    }

    [Fact]
    public void Linha_e_cartao_existente_nao_mostram_o_codigo_no_texto()
    {
        var previa = PreverCsv(LinhasCrlf(CabecalhoCsv, "99990000000101;INTEIRA;;ATIVO;;;;"));

        Assert.Equal("Linha 2: Novo cred:****01(14)", previa.Linhas[0].ToString());
        Assert.DoesNotContain("99990000000101", new CartaoExistente("99990000000101", Bilheteria, null, SituacaoDoCartao.Valido).ToString(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- os modelos do repositório

    [Fact]
    public void Os_modelos_instalados_sao_lidos_e_o_exemplo_com_titular_e_recusado()
    {
        var pasta = Path.Combine(RaizDoRepositorio(), "installer", "modelos");

        using (var xlsx = File.OpenRead(Path.Combine(pasta, "modelo-cadastro-de-cartoes.xlsx")))
        {
            // A aba Cartões do modelo vem vazia (docs/26 §5): nada a importar, nada de erro.
            var previa = PreviaDaImportacao.DeXlsx(xlsx, Contexto());
            Assert.False(previa.Recusada, string.Join(" | ", previa.ProblemasDoArquivo));
            Assert.Empty(previa.Linhas);
            Assert.Equal(0, previa.LinhasVazias);
        }

        using var cartoes = File.OpenRead(Path.Combine(pasta, "cartoes-modelo.csv"));
        using var tipos = File.OpenRead(Path.Combine(pasta, "tipos-modelo.csv"));
        var doCsv = PreviaDaImportacao.DeCsv(cartoes, new ContextoDaPrevia(Bilheteria, CredentialNormalization.Raw, [], []), tipos);

        // O exemplo com titular é recusado até existir a cifra do titular (B.9); os outros entram.
        Assert.False(doCsv.Recusada);
        Assert.Equal(4, doCsv.Tipos.Count(t => t.Classe == ClasseDaLinha.Novo));
        Assert.Equal(1, doCsv.ComErro);
        Assert.Contains(doCsv.Linhas.Single(l => l.Classe == ClasseDaLinha.Erro).Erros, e => e.Contains("titular", StringComparison.Ordinal));
        Assert.Equal(3, doCsv.Novos);
    }

    // ------------------------------------------------------------------ 100 mil linhas

    [Fact]
    public void Cem_mil_linhas_de_csv_com_tempo_medido()
    {
        const int Linhas = 100_000;
        var texto = new StringBuilder(CabecalhoCsv).Append("\r\n");
        for (var i = 0; i < Linhas; i++)
        {
            texto.Append("9999").Append(i.ToString("D8", System.Globalization.CultureInfo.InvariantCulture))
                .Append(i % 3 == 0 ? ";MEIA;;ATIVO;;;1;\r\n" : ";INTEIRA;;ATIVO;;;;\r\n");
        }

        // Um terço já está na base, um décimo com outro tipo: a comparação também é medida.
        var existentes = Enumerable.Range(0, Linhas / 3)
            .Select(i => new CartaoExistente($"9999{i * 3:D8}", Bilheteria, i % 10 == 0 ? "INTEIRA" : "MEIA", SituacaoDoCartao.Valido))
            .ToArray();

        using var arquivo = Csv(texto.ToString());
        var cronometro = Stopwatch.StartNew();
        var previa = PreviaDaImportacao.DeCsv(arquivo, Contexto(existentes: existentes));
        cronometro.Stop();

        saida.WriteLine($"CSV de {Linhas} linhas ({arquivo.Length / 1024} KB): prévia em {cronometro.ElapsedMilliseconds} ms");

        Assert.False(previa.Recusada);
        Assert.Equal(Linhas, previa.Linhas.Count);
        Assert.Equal(Linhas - existentes.Length, previa.Novos);
        Assert.Equal(existentes.Length / 10 + (existentes.Length % 10 == 0 ? 0 : 1), previa.Alterados);
        Assert.Equal(0, previa.ComErro);

        // Limite generoso: não depende de máquina rápida, só pega o que for quadrático.
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(60), $"Prévia de 100 mil linhas levou {cronometro.Elapsed}.");
    }

    [Fact]
    public void Cem_mil_linhas_de_xlsx_com_tempo_medido()
    {
        const int Linhas = 100_000;
        var linhas = new List<IReadOnlyList<C>?> { Cabecalho() };
        for (var i = 0; i < Linhas; i++)
        {
            linhas.Add([$"9999{i:D8}", i % 2 == 0 ? "MEIA" : "INTEIRA", "", "ATIVO", "", "", new C("1", Tipo.Numero)]);
        }

        using var arquivo = Xlsx([("Cartões", linhas)]);
        var cronometro = Stopwatch.StartNew();
        var previa = PreviaDaImportacao.DeXlsx(arquivo, Contexto());
        cronometro.Stop();

        saida.WriteLine($"XLSX de {Linhas} linhas ({arquivo.Length / 1024} KB): prévia em {cronometro.ElapsedMilliseconds} ms");

        Assert.False(previa.Recusada, string.Join(" | ", previa.ProblemasDoArquivo));
        Assert.Equal(Linhas, previa.Novos);
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(60), $"Prévia de 100 mil linhas levou {cronometro.Elapsed}.");
    }

    private static string RaizDoRepositorio()
    {
        var pasta = new DirectoryInfo(AppContext.BaseDirectory);
        while (pasta is not null && !File.Exists(Path.Combine(pasta.FullName, "ConexaoTopdata.slnx")))
        {
            pasta = pasta.Parent;
        }

        return pasta?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
