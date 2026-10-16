using System.Security.Cryptography;
using Access.Infrastructure.SQLite;
using Contracts;
using Contracts.Edge.V1;
using Edge.Supervisor;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests.Atualizacoes;

/// <summary>
/// A licença no serviço de verdade (docs/60 §1): sem licença, 15 dias de tolerância; depois, relatórios e
/// mudanças no evento são recusados e o resto segue; instalar a licença libera na hora.
/// </summary>
public sealed class LicencaNoServicoTests : IAsyncLifetime, IDisposable
{
    // A sessão do painel é conferida pelo relógio de verdade (12 h de inatividade): uma data fixa aqui
    // vence sozinha com o passar dos dias.
    private static readonly DateTimeOffset Inicio = DateTimeOffset.UtcNow;
    private static readonly DateOnly Vencimento = DateOnly.FromDateTime(Inicio.AddYears(1).UtcDateTime);

    private readonly BancoTemporario _banco = new();
    private readonly ECDsa _chave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private DateTimeOffset _agora = Inicio;
    private UsuariosDoSistema _usuarios = null!;
    private ControleDeLicenca _licenca = null!;
    private WebApplication? _servidor;
    private string _endereco = string.Empty;
    private string _token = string.Empty;

    public async Task InitializeAsync()
    {
        _banco.Migrar();
        _usuarios = new UsuariosDoSistema(_banco.Fabrica, 100_000);
        _usuarios.GarantirAdministradorPadrao(Inicio);
        var configuracoes = new ConfiguracoesDaBorda(_banco.Fabrica);
        _licenca = new ControleDeLicenca(configuracoes, () => _agora, [Convert.ToBase64String(_chave.ExportSubjectPublicKeyInfo())]);

        var sessoes = new SessoesDoPainel();
        var servico = new EdgeControlService(
            new WorkerSupervisor([]), relogio: () => _agora, semConfiguracao: true, usuarios: _usuarios, sessoes: sessoes,
            configuracoes: configuracoes, consultas: new ConsultasDaOperacao(_banco.Fabrica))
        {
            Licenca = _licenca,
        };

        _token = InterceptadorDeToken.GerarToken();
        _endereco = TransporteLocal.EnderecoPadrao($"licenca-{Guid.NewGuid():N}");
        var construtor = WebApplication.CreateBuilder();
        construtor.WebHost.ConfigureKestrel(o => TransporteLocal.Escutar(o, _endereco));
        construtor.Services.AddSingleton(servico);
        construtor.Services.AddGrpc(o =>
        {
            o.Interceptors.Add<InterceptadorDeToken>(_token);
            o.Interceptors.Add<InterceptadorDeSessao>(
                _usuarios, sessoes, (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow), (Func<bool>)(() => _licenca.Bloqueada));
        });
        _servidor = construtor.Build();
        _servidor.MapGrpcService<EdgeControlService>();
        await _servidor.StartAsync();
    }

    public async Task DisposeAsync()
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
    }

    public void Dispose()
    {
        _chave.Dispose();
        _banco.Dispose();
    }

    private async Task<EdgeControl.EdgeControlClient> EntrarComoAdministrador()
    {
        var admin = _usuarios.Listar().Single(u => u.Login == UsuariosDoSistema.LoginPadrao).Id;
        var (_, problemas) = _usuarios.Gravar(admin, null, "gestora", "Gestora de Teste", true, ["administrador"], "provisoria-1", Inicio);
        Assert.Empty(problemas);
        var sessao = new SessaoDoPainel();
        var cliente = new EdgeControl.EdgeControlClient(TransporteLocal.CriarInvocadorDoPainel(_endereco, _token, sessao));
        sessao.Token = (await cliente.EntrarAsync(new EntrarRequest { Login = "gestora", Senha = "provisoria-1" })).Sessao;
        Assert.True((await cliente.TrocarSenhaAsync(new TrocarSenhaRequest { SenhaAtual = "provisoria-1", SenhaNova = "senha-da-gestora" })).Trocada);
        return cliente;
    }

    private static async Task<RpcException?> Recusa(Func<Task> chamada)
    {
        try
        {
            await chamada();
            return null;
        }
        catch (RpcException erro)
        {
            return erro;
        }
    }

    [Fact]
    public async Task Sem_licenca_bloqueia_o_administrativo_depois_de_15_dias_e_a_licenca_libera()
    {
        var cliente = await EntrarComoAdministrador();
        var relatorios = () => cliente.ObterRelatoriosAsync(new ObterRelatoriosRequest()).ResponseAsync;

        var estado = await cliente.ObterEstadoAsync(new ObterEstadoRequest());
        Assert.Equal((SituacaoDaLicenca.EmTolerancia, 15), (estado.SituacaoDaLicenca, estado.DiasDaLicenca));
        Assert.Null(await Recusa(relatorios));

        // 16 dias depois (a memória do estado é de um minuto: passa).
        _agora = Inicio.AddDays(16);
        var recusa = await Recusa(relatorios);
        Assert.Equal(StatusCode.PermissionDenied, recusa?.StatusCode);
        Assert.Contains("Licença vencida", recusa?.Status.Detail, StringComparison.Ordinal);

        // O que não é administrativo segue: estado, diagnóstico da licença.
        Assert.Equal(SituacaoDaLicenca.Bloqueada, (await cliente.ObterEstadoAsync(new ObterEstadoRequest())).SituacaoDaLicenca);
        var licenca = await cliente.ObterLicencaAsync(new ObterLicencaRequest());
        Assert.Equal(32, licenca.Instalacao.Length);

        // Licença de outro computador: recusada com o motivo.
        var outra = Licencas.Emitir(new DadosDaLicenca("L-9", "Evento de Teste", new string('f', 32), Vencimento, _agora), _chave);
        var recusada = await cliente.InstalarLicencaAsync(new InstalarLicencaRequest { Texto = outra });
        Assert.False(recusada.Gravada);
        Assert.Contains("outro computador", Assert.Single(recusada.Problemas), StringComparison.Ordinal);

        // A licença desta instalação libera na hora.
        var texto = Licencas.Emitir(new DadosDaLicenca("L-10", "Evento de Teste", licenca.Instalacao, Vencimento, _agora), _chave);
        Assert.True((await cliente.InstalarLicencaAsync(new InstalarLicencaRequest { Texto = texto })).Gravada);
        Assert.Null(await Recusa(relatorios));

        var depois = await cliente.ObterLicencaAsync(new ObterLicencaRequest());
        Assert.Equal((SituacaoDaLicenca.Valida, "Evento de Teste", Vencimento.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), (depois.Situacao, depois.Cliente, depois.VenceEm));
    }

    [Fact]
    public async Task Portaria_nao_instala_licenca()
    {
        var admin = _usuarios.Listar().Single(u => u.Login == UsuariosDoSistema.LoginPadrao).Id;
        _usuarios.Gravar(admin, null, "port", "Portaria de Teste", true, ["portaria"], "provisoria-1", Inicio);
        var sessao = new SessaoDoPainel();
        var cliente = new EdgeControl.EdgeControlClient(TransporteLocal.CriarInvocadorDoPainel(_endereco, _token, sessao));
        sessao.Token = (await cliente.EntrarAsync(new EntrarRequest { Login = "port", Senha = "provisoria-1" })).Sessao;
        await cliente.TrocarSenhaAsync(new TrocarSenhaRequest { SenhaAtual = "provisoria-1", SenhaNova = "senha-da-portaria" });

        var recusa = await Recusa(() => cliente.InstalarLicencaAsync(new InstalarLicencaRequest { Texto = "XAL1.x.y" }).ResponseAsync);

        Assert.Equal(StatusCode.PermissionDenied, recusa?.StatusCode);
    }
}
