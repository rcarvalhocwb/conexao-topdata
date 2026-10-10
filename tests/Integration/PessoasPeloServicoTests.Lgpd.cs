using System.Text.Json;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Desktop.ViewModels;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Integration.Tests;

public sealed partial class PessoasPeloServicoTests
{
    private static async Task<JsonDocument> ExportarTitular(EdgeControl.EdgeControlClient cliente, string id)
    {
        using var chamada = cliente.ExportarDadosDaPessoa(new ObterPessoaRequest { Id = id });
        using var bytes = new MemoryStream();
        var concluido = false;
        while (await chamada.ResponseStream.MoveNext(CancellationToken.None))
        {
            Assert.False(concluido);
            var bloco = chamada.ResponseStream.Current;
            concluido = bloco.Concluido;
            await bytes.WriteAsync(bloco.Conteudo.Memory);
        }
        Assert.True(concluido);
        return JsonDocument.Parse(bytes.ToArray());
    }

    [Fact]
    public async Task Exportar_exige_ver_dados_excluir_exige_permissao_propria_e_confirmacao()
    {
        var admin = await Entrar("adm-lgpd", "administrador");
        var supervisor = await Entrar("sup-lgpd", "supervisor");
        var portaria = await Entrar("port-lgpd", "portaria");
        var id = (await supervisor.GravarPessoaAsync(new GravarPessoaRequest
        {
            Pessoa = new PessoaDoCadastro { PerfilId = "aluno", NomeCompleto = "Titular LGPD fictícia", TipoDoDocumento = "rg", Documento = "RG-FICTICIO-987" },
        })).Id;
        await supervisor.AdicionarCredencialAsync(new AdicionarCredencialRequest { PessoaId = id, Tipo = "cartao", Codigo = "77001906" });
        Assert.Equal(StatusCode.PermissionDenied, await Codigo(async () => { using var dados = await ExportarTitular(portaria, id); }));
        Assert.Equal(StatusCode.PermissionDenied, await Codigo(() => supervisor.ExcluirTitularAsync(new ExcluirTitularRequest { PessoaId = id, Confirmada = true }).ResponseAsync));
        using var exportado = await ExportarTitular(supervisor, id);
        Assert.Equal("RG-FICTICIO-987", exportado.RootElement.GetProperty("pessoa")[0].GetProperty("document").GetString());
        Assert.Equal("77001906", exportado.RootElement.GetProperty("credenciais")[0].GetProperty("value_normalized").GetString());
        Assert.False((await admin.ExcluirTitularAsync(new ExcluirTitularRequest { PessoaId = id })).Gravado);
        Assert.NotNull(_pessoas.Obter(id));
        Assert.True((await admin.ExcluirTitularAsync(new ExcluirTitularRequest { PessoaId = id, Confirmada = true })).Gravado);
        Assert.Null(_pessoas.Obter(id));
        Assert.Equal(StatusCode.NotFound, await Codigo(async () => { using var dados = await ExportarTitular(admin, id); }));
    }

    [Fact]
    public async Task ViewModel_exporta_o_JSON_inteiro_e_so_limpa_a_ficha_apos_exclusao_confirmada()
    {
        var cliente = await Entrar("vm-lgpd", "administrador");
        var id = (await cliente.GravarPessoaAsync(new GravarPessoaRequest
        {
            Pessoa = new PessoaDoCadastro { PerfilId = "aluno", NomeCompleto = "Ficha fictícia para exportação", Telefone = "11 90000-1111" },
        })).Id;
        using var vm = new PessoasViewModel(cliente);
        vm.DefinirPermissoesLgpd(true, true);
        await vm.AtualizarAsync();
        await vm.AbrirAsync(id);
        Assert.True(vm.PodeExportarDados);
        Assert.True(vm.PodeExcluirTitular);
        var pasta = Path.Combine(Path.GetTempPath(), "lgpd-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var caminho = Path.Combine(pasta, "dados.json");
            await vm.ExportarDadosAsync(caminho);
            using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(caminho));
            Assert.Equal("11 90000-1111", json.RootElement.GetProperty("pessoa")[0].GetProperty("phone").GetString());
            Assert.Single(Directory.GetFiles(pasta));
            await vm.ExcluirTitularAsync(false);
            Assert.Equal(id, vm.Id);
            Assert.NotNull(_pessoas.Obter(id));
            await vm.ExcluirTitularAsync(true);
            Assert.Null(_pessoas.Obter(id));
            Assert.False(vm.TemFicha);
            Assert.Empty(vm.NomeCompleto);
            Assert.Empty(vm.Telefone);
            Assert.Empty(vm.Credenciais);
            Assert.DoesNotContain(vm.Pessoas, p => p.Id == id);
            Assert.Contains("Passagens preservadas", vm.Mensagem, StringComparison.Ordinal);
        }
        finally { Directory.Delete(pasta, recursive: true); }
    }

    [Fact]
    public async Task Nova_sessao_limpa_dados_e_erros_de_gravacao_nao_deixam_exportacao_parcial()
    {
        var cliente = await Entrar("vm-privacidade", "administrador");
        var id = (await cliente.GravarPessoaAsync(new GravarPessoaRequest { Pessoa = new PessoaDoCadastro { PerfilId = "aluno", NomeCompleto = "Titular da sessão anterior" } })).Id;
        using var vm = new PessoasViewModel(cliente);
        await vm.AtualizarAsync();
        await vm.AbrirAsync(id);
        var pasta = Path.Combine(Path.GetTempPath(), "lgpd-vm-" + Guid.NewGuid().ToString("N"));
        var destino = Path.Combine(pasta, "diretorio");
        Directory.CreateDirectory(destino);
        try
        {
            await vm.ExportarDadosAsync(destino);
            Assert.Contains("Não foi possível gravar", vm.Mensagem, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(pasta));
            vm.DefinirPermissoesLgpd(false, false);
            Assert.False(vm.MostraExportarDados);
            Assert.False(vm.MostraExcluirTitular);
            Assert.False(vm.TemFicha);
            Assert.Empty(vm.Pessoas);
            Assert.Empty(vm.NomeCompleto);
            var proibido = Path.Combine(pasta, "proibido.json");
            await vm.ExportarDadosAsync(proibido);
            await vm.ExcluirTitularAsync(true);
            Assert.False(File.Exists(proibido));
            Assert.NotNull(_pessoas.Obter(id));
        }
        finally { Directory.Delete(pasta, recursive: true); }
    }

    [Fact]
    public async Task Termo_pelo_servico_exige_parametros_e_aceite_exige_editar_e_confirmacao()
    {
        var supervisor = await Entrar("termo-sup", "supervisor");
        var portaria = await Entrar("termo-port", "portaria");
        var versao = new GravarTermoDeConsentimentoRequest { Versao = "2026-01", Texto = "Termo fictício para teste do serviço" };
        Assert.Equal(StatusCode.PermissionDenied, await Codigo(() => portaria.GravarTermoDeConsentimentoAsync(versao).ResponseAsync));
        Assert.True((await supervisor.GravarTermoDeConsentimentoAsync(versao)).Gravado);
        var id = (await portaria.GravarPessoaAsync(new GravarPessoaRequest { Pessoa = new PessoaDoCadastro { PerfilId = "aluno", NomeCompleto = "Titular do aceite" } })).Id;
        var aceite = new RegistrarAceiteDoTermoRequest { PessoaId = id, Versao = versao.Versao, AceitoEm = Timestamp.FromDateTimeOffset(Agora) };
        Assert.False((await portaria.RegistrarAceiteDoTermoAsync(aceite)).Gravado);
        aceite.Confirmado = true;
        Assert.True((await portaria.RegistrarAceiteDoTermoAsync(aceite)).Gravado);
        using var json = await ExportarTitular(supervisor, id);
        Assert.Equal(versao.Texto, json.RootElement.GetProperty("aceites")[0].GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Historico_grande_chega_inteiro_e_revogar_permissao_interrompe_o_fluxo(bool revogar)
    {
        var adminId = _usuarios.Listar().Single(u => u.Login == UsuariosDoSistema.LoginPadrao).Id;
        var papel = _usuarios.GravarPapel(adminId, null, "Exportador de teste", null, ["pessoas.ver_dados"], Agora).Id!;
        var exportador = await Entrar("exportador-grande", papel);
        var pessoa = _pessoas.Gravar(adminId, new DadosDaPessoa { PerfilId = "aluno", NomeCompleto = "Titular do histórico grande" }, Agora).Id!;
        using (var con = _banco.Fabrica.Abrir())
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = """
                WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i + 1 FROM n WHERE i < 16000)
                INSERT INTO ticket_use_attempt (id, qr_normalized, gate_id, device_id, outcome, reason, at, person_id)
                SELECT 'a-' || i, '77001907', 'p1', 'd1', 'negado', 'PessoaInativa', $em, $id FROM n;
                """;
            cmd.Parameters.AddWithValue("$id", pessoa);
            cmd.Parameters.AddWithValue("$em", Agora.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }
        if (!revogar)
        {
            using var json = await ExportarTitular(exportador, pessoa);
            Assert.Equal(16000, json.RootElement.GetProperty("passagens").GetArrayLength());
            return;
        }
        using var chamada = exportador.ExportarDadosDaPessoa(new ObterPessoaRequest { Id = pessoa });
        Assert.True(await chamada.ResponseStream.MoveNext(CancellationToken.None));
        Assert.Empty(_usuarios.GravarPapel(adminId, papel, "Exportador de teste", null, [], Agora).Problemas);
        var codigo = await Codigo(async () =>
        {
            while (await chamada.ResponseStream.MoveNext(CancellationToken.None)) Assert.False(chamada.ResponseStream.Current.Concluido);
        });
        Assert.Equal(StatusCode.PermissionDenied, codigo);
    }
}
