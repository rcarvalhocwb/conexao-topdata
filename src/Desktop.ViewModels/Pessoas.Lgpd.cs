using Contracts.Edge.V1;
using Grpc.Core;

namespace Desktop.ViewModels;

public sealed partial class PessoasViewModel
{
    private bool _podeVerDados = true;
    private bool _podeExcluir = true;
    private long _geracaoDaSessao;
    private long _geracaoDaFicha;
    private CancellationTokenSource _cancelamentoLgpd = new();
    public bool PodeExportarDados => TemFicha && _podeVerDados && !Ocupada;
    public bool PodeExcluirTitular => TemFicha && _podeExcluir && !Ocupada;
    public bool MostraExportarDados => _podeVerDados;
    public bool MostraExcluirTitular => _podeExcluir;

    /// <summary>Uma nova sessão invalida a ficha e os pedidos pendentes, mesmo que tenha os mesmos papéis.</summary>
    public void DefinirPermissoesLgpd(bool verDados, bool excluir)
    {
        _geracaoDaSessao++;
        _cancelamentoLgpd.Cancel();
        _cancelamentoLgpd.Dispose();
        _cancelamentoLgpd = new CancellationTokenSource();
        _podeVerDados = verDados;
        _podeExcluir = excluir;
        Selecionada = null;
        Preencher(null);
        Pessoas = [];
        Anfitrioes = [];
        Busca = string.Empty;
        _arquivo = null;
        _nomeDoArquivo = string.Empty;
        ImportacaoAplicavel = false;
        ResumoDaImportacao = string.Empty;
        ProblemasDaImportacao = [];
        Lotes = [];
        LoteSelecionado = null;
        Mensagem = string.Empty;
        Avisar(nameof(MostraExportarDados));
        Avisar(nameof(MostraExcluirTitular));
        AvisarLgpd();
    }

    public async Task ExportarDadosAsync(string caminho, CancellationToken cancelamento = default)
    {
        if (!PodeExportarDados) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(caminho);
        var id = Id;
        var sessao = _geracaoDaSessao;
        var ficha = _geracaoDaFicha;
        using var ligado = CancellationTokenSource.CreateLinkedTokenSource(cancelamento, _cancelamentoLgpd.Token);
        string? temporario = null;
        try
        {
            await Tentar(async () =>
            {
                var destino = Path.GetFullPath(caminho);
                temporario = Path.Combine(Path.GetDirectoryName(destino)!, $".{Path.GetFileName(destino)}.{Guid.NewGuid():N}.tmp");
                using var chamada = Cliente.ExportarDadosDaPessoa(new ObterPessoaRequest { Id = id }, cancellationToken: ligado.Token);
                var concluido = false;
                await using (var arquivo = new FileStream(temporario, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                {
                    while (await chamada.ResponseStream.MoveNext(ligado.Token).ConfigureAwait(true))
                    {
                        if (sessao != _geracaoDaSessao || ficha != _geracaoDaFicha || Id != id || !_podeVerDados)
                            throw new OperationCanceledException(ligado.Token);
                        var bloco = chamada.ResponseStream.Current;
                        if (concluido || (bloco.Concluido && !bloco.Conteudo.IsEmpty))
                            throw new IOException("Exportação incompleta.");
                        if (bloco.Concluido) concluido = true;
                        else await arquivo.WriteAsync(bloco.Conteudo.Memory, ligado.Token).ConfigureAwait(true);
                    }
                }
                ligado.Token.ThrowIfCancellationRequested();
                if (!concluido) throw new IOException("Exportação incompleta.");
                if (sessao != _geracaoDaSessao || ficha != _geracaoDaFicha || Id != id || !_podeVerDados)
                    throw new OperationCanceledException(ligado.Token);
                // O destino só aparece (ou é substituído) quando o JSON está inteiro.
                File.Move(temporario, destino, overwrite: true);
                temporario = null;
                Mensagem = "Dados do titular exportados.";
            }).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { if (sessao == _geracaoDaSessao) Mensagem = "Exportação cancelada."; }
        catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
        { if (sessao == _geracaoDaSessao) Mensagem = "Não foi possível gravar o arquivo de dados do titular."; }
        finally
        {
            if (temporario is not null)
            {
                try { File.Delete(temporario); }
                catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
                { if (sessao == _geracaoDaSessao) Mensagem = "Não foi possível remover o arquivo temporário da exportação."; }
            }
        }
    }

    public async Task ExcluirTitularAsync(bool confirmada)
    {
        if (!confirmada || !PodeExcluirTitular) return;
        var id = Id;
        var sessao = _geracaoDaSessao;
        await Tentar(async () =>
        {
            var r = await Cliente.ExcluirTitularAsync(new ExcluirTitularRequest { PessoaId = id, Confirmada = true }, cancellationToken: _cancelamentoLgpd.Token);
            if (sessao != _geracaoDaSessao) return;
            if (!r.Gravado) { Mensagem = string.Join(" ", r.Problemas); return; }
            if (Id == id) { Selecionada = null; Preencher(null); }
            Pessoas = [.. Pessoas.Where(p => p.Id != id)];
            Anfitrioes = [.. Anfitrioes.Where(p => p.Codigo != id)];
            Mensagem = "Titular excluído. Passagens preservadas sem identificação pessoal.";
        }).ConfigureAwait(true);
    }

    private void AvisarLgpd()
    {
        Avisar(nameof(PodeExportarDados));
        Avisar(nameof(PodeExcluirTitular));
    }

    public void Dispose()
    {
        _cancelamentoLgpd.Cancel();
        _cancelamentoLgpd.Dispose();
        GC.SuppressFinalize(this);
    }
}
