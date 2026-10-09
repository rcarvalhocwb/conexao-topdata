using System.Security.Cryptography;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Contracts;
using Contracts.Edge.V1;
using Desktop.ViewModels;
using Edge.Supervisor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests;

/// <summary>Tarefa #8: nomes somente no painel autorizado; a nuvem e o difusor continuam sem nomes.</summary>
public sealed class NomesNasPassagensTests
{
    private const string Nome = "Beatriz Lima";
    private const string Cartao = "77001234";
    private const string Ingresso = "1000000001";
    private static readonly DateTimeOffset Agora = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class Montagem : IAsyncDisposable
    {
        private WebApplication? _servidor;
        private string _endereco = string.Empty;
        private readonly string _token = InterceptadorDeToken.GerarToken();
        private readonly UsuariosDoSistema _usuarios;
        private readonly SessoesDoPainel _sessoes = new();

        public Montagem(string cofre = "disponivel")
        {
            Banco.Migrar();
            var cifra = new CifraDeDadosPessoais(RandomNumberGenerator.GetBytes(CifraDeDadosPessoais.TamanhoDaChave));
            Pessoas = new CadastroDePessoas(Banco.Fabrica, cifra);
            var criada = Pessoas.Gravar("teste", new DadosDaPessoa { NomeCompleto = Nome, PerfilId = "aluno" }, Agora);
            Assert.True(criada.Gravado, string.Join(" ", criada.Problemas));
            PessoaId = criada.Id!;
            Assert.True(Pessoas.AdicionarCredencial("teste", PessoaId, "cartao", Cartao, null, null, Agora).Gravado);

            Repositorio = new RepositorioDeIngressos(Banco.Fabrica, new EspelhoDeTentativas("nuvem", TimeSpan.Zero));
            Repositorio.RegistrarProvedor(new ProvedorDeIngresso("zet", "Zet", "qr-catraca4", ""), Agora);
            Repositorio.Ingerir([new IngressoRecebido("zet", "T1", Ingresso, Ingresso, Categoria: "meia")], Agora);
            Operacao = new Operacao(Banco.Fabrica);
            Consultas = new ConsultasDaOperacao(Banco.Fabrica);
            _usuarios = new UsuariosDoSistema(Banco.Fabrica, 100_000);
            _usuarios.GarantirAdministradorPadrao(Agora);
            Servico = new EdgeControlService(new WorkerSupervisor([]), relogio: () => Agora,
                operacao: Operacao, consultas: Consultas, usuarios: _usuarios, sessoes: _sessoes,
                pessoas: cofre switch
                {
                    "ausente" => null,
                    "outra_chave" => new CadastroDePessoas(Banco.Fabrica,
                        new CifraDeDadosPessoais(RandomNumberGenerator.GetBytes(CifraDeDadosPessoais.TamanhoDaChave))),
                    _ => Pessoas,
                });
        }

        public BancoTemporario Banco { get; } = new();
        public CadastroDePessoas Pessoas { get; }
        public string PessoaId { get; }
        public RepositorioDeIngressos Repositorio { get; }
        public Operacao Operacao { get; }
        public ConsultasDaOperacao Consultas { get; }
        public EdgeControlService Servico { get; }

        public void Passagens()
        {
            Assert.True(Repositorio.TentarUsar(Cartao, "portao-1", "inner-1", Agora).Resultado.Liberou);
            Assert.True(Repositorio.TentarUsar(Ingresso, "portao-1", "inner-1", Agora.AddSeconds(1)).Resultado.Liberou);
            Assert.True(Pessoas.MudarSituacao("teste", PessoaId, "bloqueado", "Crachá retido", Agora.AddSeconds(2)).Gravado);
            Assert.Equal(MotivoDoUso.PessoaBloqueada,
                Repositorio.TentarUsar(Cartao, "portao-1", "inner-1", Agora.AddSeconds(3)).Resultado.Motivo);
        }

        public async Task IniciarAsync()
        {
            _endereco = TransporteLocal.EnderecoPadrao($"nomes-{Guid.NewGuid():N}");
            var construtor = WebApplication.CreateBuilder();
            construtor.WebHost.ConfigureKestrel(o => TransporteLocal.Escutar(o, _endereco));
            construtor.Services.AddSingleton(Servico);
            construtor.Services.AddGrpc(o =>
            {
                o.Interceptors.Add<InterceptadorDeToken>(_token);
                o.Interceptors.Add<InterceptadorDeSessao>(_usuarios, _sessoes);
            });
            _servidor = construtor.Build();
            _servidor.MapGrpcService<EdgeControlService>();
            await _servidor.StartAsync();
        }

        public async Task<EdgeControl.EdgeControlClient> Entrar(string login, string papel)
        {
            var admin = _usuarios.Listar().Single(u => u.Login == UsuariosDoSistema.LoginPadrao).Id;
            var (_, problemas) = _usuarios.Gravar(admin, null, login, "Usuária " + login, true, [papel], "provisoria-1", Agora);
            Assert.Empty(problemas);
            var sessao = new SessaoDoPainel();
            var cliente = new EdgeControl.EdgeControlClient(TransporteLocal.CriarInvocadorDoPainel(_endereco, _token, sessao));
            sessao.Token = (await cliente.EntrarAsync(new EntrarRequest { Login = login, Senha = "provisoria-1" })).Sessao;
            Assert.True((await cliente.TrocarSenhaAsync(new TrocarSenhaRequest
            {
                SenhaAtual = "provisoria-1", SenhaNova = "senha-de-" + login,
            })).Trocada);
            return cliente;
        }

        public async ValueTask DisposeAsync()
        {
            if (_servidor is not null)
            {
                await _servidor.StopAsync();
                await _servidor.DisposeAsync();
            }

            if (!TransporteLocal.UsaNamedPipe && File.Exists(_endereco))
            {
                File.Delete(_endereco);
            }

            Banco.Dispose();
        }
    }

    [Fact]
    public async Task Consulta_junta_a_pessoa_sem_decifrar_e_o_difusor_e_a_nuvem_nao_recebem_nome()
    {
        await using var m = new Montagem();
        m.Passagens();
        var (linhas, haMais) = m.Consultas.ListarTentativas(new FiltroDeTentativas());
        Assert.False(haMais);
        Assert.Equal(3, linhas.Count);
        Assert.Null(linhas[1].Pessoa);
        foreach (var linha in new[] { linhas[0], linhas[2] })
        {
            Assert.Equal(m.PessoaId, linha.Pessoa!.PessoaId);
            Assert.DoesNotContain(Nome, System.Text.Encoding.UTF8.GetString(linha.Pessoa.Cifrado!), StringComparison.Ordinal);
            Assert.Equal(Nome, m.Pessoas.NomeParaPassagem(linha.Pessoa));
            Assert.Equal(Nome, m.Pessoas.NomeParaPassagem(m.Consultas.PessoaDaTentativa(linha.Id)!));
            Assert.DoesNotContain(Cartao, linha.CodigoMascarado, StringComparison.Ordinal);
        }

        Assert.Null(m.Consultas.PessoaDaTentativa(linhas[1].Id));
        Assert.Null(m.Consultas.PessoaDaTentativa(Guid.NewGuid()));
        Assert.True(m.Consultas.ListarTentativas(new FiltroDeTentativas(Limite: 1)).HaMais);
        using var assinatura = m.Servico.Eventos.Assinar();
        Assert.Equal(3, new AcompanhamentoDaOperacao(m.Operacao, m.Servico.Eventos, TimeSpan.FromSeconds(1)).UmaLeitura());
        while (assinatura.Leitor.TryRead(out var evento))
        {
            Assert.Empty(evento.NomeDaPessoa);
        }

        var fila = await new FilaDeSaidaSqlite(m.Banco.Fabrica).ProximosAsync("nuvem", 100, Agora.AddMinutes(1), default);
        Assert.NotEmpty(fila);
        Assert.All(fila, item =>
        {
            Assert.DoesNotContain(Nome, item.PayloadJson, StringComparison.Ordinal);
            Assert.DoesNotContain(m.PessoaId, item.PayloadJson, StringComparison.Ordinal);
            Assert.DoesNotContain(Cartao, item.PayloadJson, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("disponivel", Nome)]
    [InlineData("ausente", "(nome ilegível)")]
    [InlineData("outra_chave", "(nome ilegível)")]
    public async Task Nome_e_motivo_chegam_as_duas_telas_e_so_a_sessao_com_pessoas_ver(string cofre, string esperado)
    {
        await using var m = new Montagem(cofre);
        m.Passagens();
        await m.IniciarAsync();
        // A portaria tem pessoas.ver, mas não pessoas.ver_dados: nome não exige os demais dados pessoais.
        var portaria = await m.Entrar("port", "portaria");
        var leitura = await m.Entrar("leitor", "somente_leitura");
        var comNome = await portaria.ListarAcessosAsync(new ListarAcessosRequest());
        var semNome = await leitura.ListarAcessosAsync(new ListarAcessosRequest());
        Assert.Equal(3, comNome.Acessos.Count);
        Assert.Equal(esperado, comNome.Acessos[0].NomeDaPessoa);
        Assert.Equal("Negado · pessoa bloqueada", comNome.Acessos[0].MensagemAoOperador);
        Assert.Empty(comNome.Acessos[1].NomeDaPessoa);
        Assert.Equal(esperado, comNome.Acessos[2].NomeDaPessoa);
        Assert.All(semNome.Acessos, e => Assert.Empty(e.NomeDaPessoa));

        var acessos = new AcessosViewModel(portaria, () => Agora);
        await acessos.AtualizarAsync();
        Assert.Equal(esperado, acessos.Linhas[0].NomeDaPessoa);
        Assert.Equal("Negado · pessoa bloqueada", acessos.Linhas[0].Mensagem);
        var painel = new PainelAoVivoViewModel(portaria, () => Agora);
        await painel.AtualizarAsync();
        Assert.Equal(2, painel.UltimosAcessos.Count(l => l.NomeDaPessoa == esperado));

        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var fluxoComNome = portaria.AcompanharEventos(new AcompanharEventosRequest(), cancellationToken: prazo.Token);
        using var fluxoSemNome = leitura.AcompanharEventos(new AcompanharEventosRequest(), cancellationToken: prazo.Token);
        new AcompanhamentoDaOperacao(m.Operacao, m.Servico.Eventos, TimeSpan.FromSeconds(1)).UmaLeitura();
        for (var i = 0; i < 3; i++)
        {
            Assert.True(await fluxoComNome.ResponseStream.MoveNext(prazo.Token));
            Assert.True(await fluxoSemNome.ResponseStream.MoveNext(prazo.Token));
            var evento = fluxoComNome.ResponseStream.Current;
            Assert.Equal(i == 1 ? string.Empty : esperado, evento.NomeDaPessoa);
            Assert.Empty(fluxoSemNome.ResponseStream.Current.NomeDaPessoa);
            Assert.Equal(evento.EventoId, fluxoSemNome.ResponseStream.Current.EventoId);
            Assert.Equal(evento.NomeDaPessoa, LinhaDeAcesso.De(evento).NomeDaPessoa);
        }

        // Ler com e sem permissão nunca altera os eventos guardados para o próximo assinante.
        using var atrasado = m.Servico.Eventos.Assinar();
        while (atrasado.Leitor.TryRead(out var evento))
        {
            Assert.Empty(evento.NomeDaPessoa);
        }
    }

    [Fact]
    public async Task Nome_adulterado_nao_interrompe_a_consulta_e_contexto_sem_permissao_nao_recebe_nome()
    {
        await using var m = new Montagem();
        m.Passagens();
        var linha = m.Consultas.ListarTentativas(new FiltroDeTentativas()).Tentativas[0];
        var cifrado = linha.Pessoa!.Cifrado!.ToArray();
        cifrado[^1] ^= 1;
        Assert.Equal("(nome ilegível)", m.Pessoas.NomeParaPassagem(linha.Pessoa with { Cifrado = cifrado }));
        var resposta = await m.Servico.ListarAcessos(new ListarAcessosRequest(), null!);
        Assert.Equal(3, resposta.Acessos.Count);
        Assert.All(resposta.Acessos, e => Assert.Empty(e.NomeDaPessoa));
    }
}
