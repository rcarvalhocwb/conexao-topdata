using System.Globalization;
using Contracts.Edge.V1;

namespace Desktop.ViewModels;

/// <summary>A seção "Licença e atualizações" do Diagnóstico (docs/60 §3).</summary>
public sealed partial class DiagnosticoViewModel
{
    private string _licencaResumo = string.Empty;
    private Sinal _licencaSinal = Sinal.Neutro;
    private IReadOnlyList<ParDeTexto> _licenca = [];
    private string _textoDaLicenca = string.Empty;
    private string _idDaInstalacao = string.Empty;
    private string _atualizacaoResumo = string.Empty;
    private Sinal _atualizacaoSinal = Sinal.Neutro;
    private IReadOnlyList<ParDeTexto> _atualizacao = [];

    /// <summary>A licença numa frase.</summary>
    public string LicencaResumo { get => _licencaResumo; private set => Definir(ref _licencaResumo, value); }

    public Sinal LicencaSinal { get => _licencaSinal; private set => Definir(ref _licencaSinal, value); }

    /// <summary>Cliente, número, vencimento e o que fazer.</summary>
    public IReadOnlyList<ParDeTexto> Licenca { get => _licenca; private set => Definir(ref _licenca, value); }

    /// <summary>O identificador desta instalação, para mandar à Rayzer ao pedir a licença.</summary>
    public string IdDaInstalacao { get => _idDaInstalacao; private set => Definir(ref _idDaInstalacao, value); }

    /// <summary>O texto colado da licença nova.</summary>
    public string TextoDaLicenca
    {
        get => _textoDaLicenca;
        set
        {
            if (Definir(ref _textoDaLicenca, value ?? string.Empty))
            {
                InstalarLicenca.ReavaliarDisponibilidade();
            }
        }
    }

    /// <summary>Instala a licença colada (permissão "configuracao.editar").</summary>
    public ComandoAssincrono InstalarLicenca { get; }

    /// <summary>A atualização automática numa frase.</summary>
    public string AtualizacaoResumo { get => _atualizacaoResumo; private set => Definir(ref _atualizacaoResumo, value); }

    public Sinal AtualizacaoSinal { get => _atualizacaoSinal; private set => Definir(ref _atualizacaoSinal, value); }

    /// <summary>Versões, situação, última verificação e o último resultado.</summary>
    public IReadOnlyList<ParDeTexto> Atualizacao { get => _atualizacao; private set => Definir(ref _atualizacao, value); }

    /// <summary>A licença traduzida para a tela: o sinal, a frase e os detalhes.</summary>
    public static (Sinal Sinal, string Resumo, IReadOnlyList<ParDeTexto> Detalhes) DescreverLicenca(LicencaDoSistema l)
    {
        ArgumentNullException.ThrowIfNull(l);

        var (sinal, resumo) = l.Situacao switch
        {
            SituacaoDaLicenca.Valida => (Sinal.Bom, "Válida"),
            SituacaoDaLicenca.VenceEmBreve => (Sinal.Atencao, "Vence em breve"),
            SituacaoDaLicenca.EmTolerancia => (Sinal.Atencao, l.Cliente.Length == 0 ? "Sem licença (em tolerância)" : "Vencida (em tolerância)"),
            SituacaoDaLicenca.Bloqueada => (Sinal.Problema, "Vencida: administrativo bloqueado"),
            _ => (Sinal.Neutro, "Não exigida nesta versão"),
        };

        List<ParDeTexto> detalhes = [new("Situação", l.Mensagem)];
        if (l.Cliente.Length > 0)
        {
            detalhes.Add(new("Cliente", l.Cliente));
        }

        if (l.Numero.Length > 0)
        {
            detalhes.Add(new("Número da licença", l.Numero));
        }

        if (DateOnly.TryParseExact(l.VenceEm, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var vence))
        {
            detalhes.Add(new("Vence em", vence.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)));
        }

        if (l.Problema.Length > 0)
        {
            detalhes.Add(new("Licença instalada", l.Problema));
        }

        return (sinal, resumo, detalhes);
    }

    /// <summary>A atualização traduzida para a tela.</summary>
    public static (Sinal Sinal, string Resumo, IReadOnlyList<ParDeTexto> Detalhes) DescreverAtualizacao(SituacaoDaAtualizacao a, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(a);

        var resumo = !a.Ligada
            ? "Desligada"
            : a.VersaoDisponivel.Length > 0 && a.VersaoDisponivel != a.VersaoInstalada && !a.Situacao.StartsWith("Em dia", StringComparison.Ordinal)
                ? $"Versão {a.VersaoDisponivel} disponível"
                : "Em dia";

        var sinal = !a.Ligada ? Sinal.Neutro : resumo == "Em dia" ? Sinal.Bom : Sinal.Atencao;

        List<ParDeTexto> detalhes =
        [
            new("Canal", a.Canal.Length == 0 ? "—" : a.Canal),
            new("Versão instalada", a.VersaoInstalada),
            new("Última versão estável", a.VersaoDisponivel.Length == 0 ? "—" : a.VersaoDisponivel),
            new("Situação", a.Situacao),
            new("Última verificação", Textos.Ha(a.UltimaVerificacao?.ToDateTimeOffset(), agora)),
        ];

        if (a.UltimoResultado.Length > 0)
        {
            detalhes.Add(new("Última atualização", a.UltimoResultado));
        }

        return (sinal, resumo, detalhes);
    }

    private async Task AtualizarLicencaEAtualizacaoAsync(CancellationToken cancelamento)
    {
        var licenca = await Cliente.ObterLicencaAsync(new ObterLicencaRequest(), cancellationToken: cancelamento);
        (LicencaSinal, LicencaResumo, Licenca) = DescreverLicenca(licenca);
        IdDaInstalacao = licenca.Instalacao;

        var atualizacao = await Cliente.ObterAtualizacaoAsync(new ObterAtualizacaoRequest(), cancellationToken: cancelamento);
        (AtualizacaoSinal, AtualizacaoResumo, Atualizacao) = DescreverAtualizacao(atualizacao, Relogio());
    }

    private async Task InstalarLicencaAsync() =>
        await Tentar(async () =>
        {
            var r = await Cliente.InstalarLicencaAsync(new InstalarLicencaRequest { Texto = TextoDaLicenca.Trim() });
            if (!r.Gravada)
            {
                Mensagem = string.Join(" ", r.Problemas);
                return;
            }

            TextoDaLicenca = string.Empty;
            await AtualizarLicencaEAtualizacaoAsync(default);
            Mensagem = $"Licença instalada. {LicencaResumo}.";
        });
}
