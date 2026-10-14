using System.Text;
using Access.Domain.Devices;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;

namespace Integration.Tests;

/// <summary>
/// Carga inicial dos cartões já validados (docs/57 §3): os três formatos de exportação do sistema
/// anterior, juntados pelo número, gravados pela mesma regra da sincronização com a nuvem. Todos os
/// números aqui são fictícios.
/// </summary>
public sealed class CargaInicialDeCartoesTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 14, 18, 0, 0, TimeSpan.Zero);

    // Os cabeçalhos são os dos arquivos de exportação; os números e os nomes são inventados.
    private const string Cartoes =
        "﻿numero_cartao;codigo_barras;tipo_cartao;status;total_usos;ultimo_uso;observacoes;criado_em;atualizado_em\r\n" +
        "900000000001;;INTEIRA;available;0;;;2025-12-09T11:47:15-03:00;2025-12-09T11:47:15-03:00\r\n" +
        "00009000000002;;MEIA;available;0;;;2025-12-09T11:47:15-03:00;2025-12-09T11:47:15-03:00\r\n" +
        "900000000003;;AUTO;available;0;;;2025-12-09T11:47:15-03:00;2025-12-09T11:47:15-03:00\r\n" +
        "900000000004;;SOCIAL;inactive;0;;;2025-12-09T11:47:15-03:00;2025-12-09T11:47:15-03:00\r\n";

    private const string Autorizados =
        "﻿numero_cartao;tipo_ingresso;titular;ativo;vezes_usado;valido_de;valido_ate;origem;criado_em;atualizado_em\r\n" +
        "900000000001;INTEIRA;Pessoa Ficticia;SIM;0;;;;2025-11-17T10:06:01-03:00;2025-11-17T10:06:01-03:00\r\n" +
        "00009000000002;MEIA;MEIA;SIM;0;;;bulk_import;2025-11-17T10:06:01-03:00;2025-11-17T10:06:01-03:00\r\n" +
        "900000000003;INTEIRA;INTEIRA;SIM;0;;;bulk_import;2025-11-17T10:06:01-03:00;2025-11-17T10:06:01-03:00\r\n" +
        "900000000004;SOCIAL;SOCIAL;SIM;0;;;bulk_import;2025-11-17T10:06:01-03:00;2025-11-17T10:06:01-03:00\r\n" +
        "9000000002;MEIA;MEIA;SIM;0;;;bulk_import;2025-11-17T10:06:01-03:00;2025-11-17T10:06:01-03:00\r\n";

    private const string Autorizacoes =
        "﻿id;numero_cartao;dispositivo;autorizado;tipo_ingresso;valido_de;valido_ate;titular;origem;criado_em;atualizado_em\r\n" +
        "00000000-0000-0000-0000-000000000001;900000000001;all-devices;SIM;INTEIRA;;;INTEIRA;authorized_cards;2025-12-09T00:10:47-03:00;2025-12-09T16:17:45-03:00\r\n" +
        "00000000-0000-0000-0000-000000000005;900000000005;all-devices;NAO;SOCIAL;;;SOCIAL;authorized_cards;2025-12-09T00:10:47-03:00;2025-12-09T16:17:45-03:00\r\n";

    private readonly BancoTemporario _banco = new();

    public CargaInicialDeCartoesTests() => _banco.Migrar();

    public void Dispose() => _banco.Dispose();

    private static ArquivoDeCarga Arquivo(string nome, string conteudo) => new(nome, Encoding.UTF8.GetBytes(conteudo));

    private ResultadoDaCargaInicial CarregarOsTres() =>
        CargaInicialDeCartoes.Carregar(
            [Arquivo("cartoes.csv", Cartoes), Arquivo("autorizados.csv", Autorizados), Arquivo("autorizacoes.csv", Autorizacoes)],
            _banco.Fabrica, Agora, "teste@maquina");

    private string Texto(string sql)
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        return Convert.ToString(comando.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    [Fact]
    public void Junta_os_tres_arquivos_pelo_numero_e_grava_cada_cartao_uma_vez()
    {
        var resultado = CarregarOsTres();

        Assert.Equal(3, resultado.Arquivos);
        Assert.Equal(6, resultado.Cartoes);
        Assert.Equal(6, resultado.Inseridos);
        Assert.Equal("6", Texto("SELECT COUNT(*) FROM ticket WHERE provider_id = 'bilheteria-local';"));
        Assert.Empty(resultado.Problemas);
    }

    [Fact]
    public void O_numero_fica_como_veio_com_os_zeros_e_sem_misturar_com_o_mesmo_numero_sem_zeros()
    {
        CarregarOsTres();

        Assert.Equal("1", Texto("SELECT COUNT(*) FROM ticket WHERE qr_normalized = '00009000000002';"));
        Assert.Equal("1", Texto("SELECT COUNT(*) FROM ticket WHERE qr_normalized = '9000000002';"));
    }

    [Fact]
    public void O_tipo_do_ingresso_vale_mais_que_o_do_plastico()
    {
        var resultado = CarregarOsTres();

        // "AUTO" no arquivo de cartões; INTEIRA nos autorizados.
        Assert.Equal("INTEIRA", Texto("SELECT category FROM ticket WHERE qr_normalized = '900000000003';"));
        Assert.False(resultado.PorTipo.ContainsKey("AUTO"));
        Assert.Equal(2, resultado.PorTipo["INTEIRA"]);
        Assert.Equal(2, resultado.PorTipo["MEIA"]);
        Assert.Equal(2, resultado.PorTipo["SOCIAL"]);
    }

    [Fact]
    public void Inativo_em_qualquer_arquivo_entra_desativado()
    {
        var resultado = CarregarOsTres();

        Assert.Equal(2, resultado.Cancelados);
        Assert.Equal("cancelado", Texto("SELECT status FROM ticket WHERE qr_normalized = '900000000004';"));
        Assert.Equal("cancelado", Texto("SELECT status FROM ticket WHERE qr_normalized = '900000000005';"));
        Assert.Equal("valido", Texto("SELECT status FROM ticket WHERE qr_normalized = '900000000001';"));
    }

    [Fact]
    public void Cartao_carregado_libera_na_urna_e_respeita_o_intervalo_de_reuso()
    {
        CarregarOsTres();
        var repositorio = new RepositorioDeIngressos(_banco.Fabrica);

        var primeira = repositorio.TentarUsar("900000000001", "portao-1", "catraca-01", Agora, leitor: KnownEventOrigin.Leitor2);
        Assert.True(primeira.Resultado.Liberou);
        Assert.Equal("INTEIRA", primeira.Resultado.Categoria);

        var naFrente = repositorio.TentarUsar("00009000000002", "portao-1", "catraca-01", Agora, leitor: KnownEventOrigin.Leitor1);
        Assert.Equal(MotivoDoUso.ForaDaUrna, naFrente.Resultado.Motivo);

        var desativado = repositorio.TentarUsar("900000000004", "portao-1", "catraca-01", Agora, leitor: KnownEventOrigin.Leitor2);
        Assert.False(desativado.Resultado.Liberou);
    }

    [Fact]
    public void Cria_o_provedor_local_e_os_tipos_sem_guardar_o_nome_do_titular()
    {
        var resultado = CarregarOsTres();

        Assert.Equal(3, resultado.TiposCriados);
        Assert.Equal("Meia-entrada", Texto("SELECT display_name FROM ticket_type WHERE code = 'MEIA';"));
        Assert.Equal("1", Texto("SELECT reusable FROM ticket_provider WHERE id = 'bilheteria-local';"));
        Assert.Equal("1", Texto("SELECT urn_only FROM ticket_provider WHERE id = 'bilheteria-local';"));

        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT * FROM ticket;";
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            for (var i = 0; i < leitor.FieldCount; i++)
            {
                Assert.NotEqual("Pessoa Ficticia", leitor.IsDBNull(i) ? null : Convert.ToString(leitor.GetValue(i), System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }

    [Fact]
    public void Repetir_os_mesmos_arquivos_nao_faz_nada_e_fica_registrado_uma_vez()
    {
        CarregarOsTres();
        var segunda = CarregarOsTres();

        Assert.True(segunda.JaCarregada);
        Assert.Equal("1", Texto("SELECT COUNT(*) FROM import_batch WHERE kind = 'cartoes' AND status = 'aplicada';"));
        Assert.Equal("6", Texto("SELECT COUNT(*) FROM ticket;"));
    }

    [Fact]
    public void Provedor_que_ja_existe_fica_como_esta()
    {
        var repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        repositorio.RegistrarProvedor(
            new ProvedorDeIngresso("bilheteria-local", "Bilheteria (painel na nuvem)", "raw", "", Reutilizavel: true,
                IntervaloDeReuso: TimeSpan.FromSeconds(300), SomenteNaUrna: false),
            Agora);

        CarregarOsTres();

        Assert.Equal("300", Texto("SELECT reuse_interval_seconds FROM ticket_provider WHERE id = 'bilheteria-local';"));
        Assert.Equal("0", Texto("SELECT urn_only FROM ticket_provider WHERE id = 'bilheteria-local';"));
    }

    [Fact]
    public void Arquivo_sem_a_coluna_do_numero_nao_e_lido_e_o_problema_nao_mostra_numero()
    {
        var resultado = CargaInicialDeCartoes.Carregar(
            [Arquivo("outro.csv", "codigo_errado;tipo\r\n900000000001;INTEIRA\r\n")], _banco.Fabrica, Agora, "teste@maquina");

        Assert.Equal(0, resultado.Cartoes);
        Assert.Contains(resultado.Problemas, p => p.Contains("numero_cartao", StringComparison.Ordinal));
        Assert.DoesNotContain(resultado.Problemas, p => p.Contains("900000000001", StringComparison.Ordinal));
    }

    [Fact]
    public void Na_pasta_carrega_e_move_para_importados()
    {
        var pastaDeDados = Path.Combine(Path.GetTempPath(), "carga-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pasta = Path.Combine(pastaDeDados, CargaInicialDeCartoes.NomeDaPasta);
            Directory.CreateDirectory(pasta);
            File.WriteAllText(Path.Combine(pasta, "autorizados.csv"), Autorizados, Encoding.UTF8);
            var linhas = new List<string>();

            var resultado = CargaInicialDeCartoes.CarregarDaPasta(pastaDeDados, _banco.Fabrica, Agora, linhas.Add);

            Assert.NotNull(resultado);
            Assert.Equal(5, resultado.Inseridos);
            Assert.Empty(Directory.GetFiles(pasta, "*.csv"));
            Assert.Single(Directory.GetFiles(Path.Combine(pasta, "importados")));
            Assert.Contains(linhas, l => l.StartsWith("carga inicial: 1 arquivo(s), 5 cartão(ões)", StringComparison.Ordinal));
            Assert.DoesNotContain(linhas, l => l.Contains("900000000001", StringComparison.Ordinal));

            // Sem arquivo esperando, nada acontece.
            Assert.Null(CargaInicialDeCartoes.CarregarDaPasta(pastaDeDados, _banco.Fabrica, Agora, linhas.Add));
        }
        finally
        {
            Directory.Delete(pastaDeDados, recursive: true);
        }
    }
}
