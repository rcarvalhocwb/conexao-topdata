using Contracts.Edge.V1;
using Desktop.ViewModels;
using Google.Protobuf;
using Grpc.Core;

namespace Integration.Tests;

/// <summary>Respostas atrasadas não restauram PII nem publicam arquivos de outra ficha/sessão.</summary>
public sealed class LgpdNaViewModelTests
{
    private sealed class Invocador : CallInvoker
    {
        public TaskCompletionSource<FichaDaPessoa>? FichaPendente { get; set; }
        public Leitor Leitor { get; } = new();
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            var id = ((ObterPessoaRequest)(object)request).Id;
            var resposta = FichaPendente?.Task ?? Task.FromResult(new FichaDaPessoa
            {
                Pessoa = new PessoaDoCadastro { Id = id, PerfilId = "aluno", NomeCompleto = "Titular " + id, Telefone = "telefone pessoal" },
                Situacao = "ativo", DadosCompletos = true,
            });
            return new(Converter<TResponse>(resposta), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
        }
        private static async Task<T> Converter<T>(Task<FichaDaPessoa> r) => (T)(object)await r.ConfigureAwait(false);
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
            new((IAsyncStreamReader<TResponse>)(object)Leitor, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }
    private sealed class Leitor : IAsyncStreamReader<BlocoDosDadosDaPessoa>
    {
        public BlocoDosDadosDaPessoa Current { get; private set; } = new();
        public TaskCompletionSource<bool> Bloqueou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Liberar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _indice;
        public bool Falhar { get; set; }
        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (_indice++ == 0) { Current = new() { Conteudo = ByteString.CopyFromUtf8("{\"dado\":\"pessoal\"}") }; return true; }
            if (_indice == 2)
            {
                Bloqueou.TrySetResult(true);
                // Até um transporte que ignora o cancelamento não pode publicar a resposta atrasada.
                await Liberar.Task.ConfigureAwait(false);
                if (Falhar) throw new RpcException(new Status(StatusCode.Unavailable, "interrompido"));
                Current = new() { Concluido = true };
                return true;
            }
            return false;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resposta_de_ficha_atrasada_nao_atravessa_saida_ou_mudanca_de_ficha(bool sair)
    {
        var inv = new Invocador { FichaPendente = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var vm = new PessoasViewModel(new EdgeControl.EdgeControlClient(inv));
        var antiga = vm.AbrirAsync("antiga");
        var pendente = inv.FichaPendente;
        if (sair) vm.DefinirPermissoesLgpd(false, false);
        else { inv.FichaPendente = null; await vm.AbrirAsync("nova"); }
        pendente.SetResult(new FichaDaPessoa { Pessoa = new PessoaDoCadastro { Id = "antiga", NomeCompleto = "Nome antigo secreto", Telefone = "Contato antigo" } });
        await antiga;
        Assert.DoesNotContain("antigo", vm.NomeCompleto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("antigo", vm.Telefone, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(sair ? string.Empty : "nova", vm.Id);
    }

    [Theory]
    [InlineData("sair")]
    [InlineData("ficha")]
    [InlineData("falha")]
    public async Task Exportacao_interrompida_remove_temporario_e_preserva_arquivo_anterior(string causa)
    {
        var inv = new Invocador();
        using var vm = new PessoasViewModel(new EdgeControl.EdgeControlClient(inv));
        await vm.AbrirAsync("antiga");
        var pasta = Path.Combine(Path.GetTempPath(), "lgpd-pendente-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        var caminho = Path.Combine(pasta, "dados.json");
        await File.WriteAllTextAsync(caminho, "arquivo anterior");
        try
        {
            var exportacao = vm.ExportarDadosAsync(caminho);
            await inv.Leitor.Bloqueou.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (causa == "sair") vm.DefinirPermissoesLgpd(false, false);
            else if (causa == "ficha") await vm.AbrirAsync("nova");
            else inv.Leitor.Falhar = true;
            inv.Leitor.Liberar.SetResult(true);
            await exportacao;
            Assert.Equal("arquivo anterior", await File.ReadAllTextAsync(caminho));
            Assert.Single(Directory.GetFiles(pasta));
        }
        finally { Directory.Delete(pasta, recursive: true); }
    }
}
