using System.Globalization;
using System.Text.RegularExpressions;
using Contracts.Edge.V1;

namespace Desktop.ViewModels;

/// <summary>Uma empresa na lista.</summary>
public sealed record LinhaDeEmpresa(string Id, string Nome, string Cnpj, string Situacao);

/// <summary>Uma sala na lista.</summary>
public sealed record LinhaDeSala(string Id, string Nome, string Empresa, string Local, string Situacao);

/// <summary>Uma tabela de horário na lista.</summary>
public sealed record LinhaDeHorario(int Id, string Nome, string Resumo);

/// <summary>Um feriado na lista.</summary>
public sealed record LinhaDeFeriado(string Dia, string Nome);

/// <summary>Um perfil na lista.</summary>
public sealed record LinhaDePerfil(string Id, string Nome, string Origem, string Regras);

/// <summary>As faixas de um dia da tabela, como texto ("08:00-12:00, 13:00-18:00").</summary>
public sealed class DiaDoHorario(int dia, string nome, string faixas) : Notificavel
{
    private string _faixas = faixas;

    public int Dia { get; } = dia;

    public string Nome { get; } = nome;

    public string Faixas { get => _faixas; set => Definir(ref _faixas, value ?? string.Empty); }
}

/// <summary>
/// Os parâmetros do cadastro de pessoas (docs/43 §5): empresas e salas, tabelas de horário e feriados,
/// e os perfis com os campos obrigatórios e as regras padrão.
/// </summary>
public sealed partial class ParametrosDoCadastroViewModel : TelaBase
{
    private static readonly string[] NomesDosDias = ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado", "Feriado"];

    private ParametrosDoCadastroDePessoas _parametros = new();
    private IReadOnlyList<LinhaDeEmpresa> _empresas = [];
    private IReadOnlyList<LinhaDeSala> _salas = [];
    private IReadOnlyList<LinhaDeHorario> _horarios = [];
    private IReadOnlyList<LinhaDeFeriado> _feriados = [];
    private IReadOnlyList<LinhaDePerfil> _perfis = [];
    private IReadOnlyList<OpcaoDeLista> _opcoesDeEmpresa = [];
    private IReadOnlyList<OpcaoDeLista> _opcoesDeHorario = [];
    private IReadOnlyList<OpcaoMarcavel> _camposDoPerfil = [];
    private LinhaDeEmpresa? _empresaSelecionada;
    private LinhaDeSala? _salaSelecionada;
    private LinhaDeHorario? _horarioSelecionado;
    private LinhaDeFeriado? _feriadoSelecionado;
    private LinhaDePerfil? _perfilSelecionado;

    private string _empresaId = string.Empty;
    private string _empresaNome = string.Empty;
    private string _empresaCnpj = string.Empty;
    private bool _empresaAtiva = true;
    private string _salaId = string.Empty;
    private string _salaNome = string.Empty;
    private string _salaEmpresaId = string.Empty;
    private string _salaAndar = string.Empty;
    private string _salaBloco = string.Empty;
    private bool _salaAtiva = true;
    private int _horarioId;
    private string _horarioNome = string.Empty;
    private IReadOnlyList<DiaDoHorario> _dias = [];
    private DateTime? _feriadoDia;
    private string _feriadoNome = string.Empty;
    private string _perfilId = string.Empty;
    private string _perfilNome = string.Empty;
    private bool _perfilExigeAnfitriao;
    private string _perfilDiasDeValidade = string.Empty;
    private string _perfilHorarioId = string.Empty;
    private string _perfilLimite = string.Empty;
    private string _perfilRetencao = "180";
    private string _perfilCatracas = string.Empty;
    private bool _perfilAtivo = true;

    public ParametrosDoCadastroViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
        NovaEmpresa = new ComandoAssincrono(() => { EmpresaSelecionada = null; PreencherEmpresa(null); return Task.CompletedTask; });
        GravarEmpresa = new ComandoAssincrono(GravarEmpresaAsync);
        NovaSala = new ComandoAssincrono(() => { SalaSelecionada = null; PreencherSala(null); return Task.CompletedTask; });
        GravarSala = new ComandoAssincrono(GravarSalaAsync);
        NovoHorario = new ComandoAssincrono(() => { HorarioSelecionado = null; PreencherHorario(null); return Task.CompletedTask; });
        GravarHorario = new ComandoAssincrono(GravarHorarioAsync);
        ExcluirHorario = new ComandoAssincrono(ExcluirHorarioAsync, () => HorarioId > 0);
        GravarFeriado = new ComandoAssincrono(GravarFeriadoAsync, () => FeriadoDia is not null);
        ExcluirFeriado = new ComandoAssincrono(ExcluirFeriadoAsync, () => FeriadoSelecionado is not null);
        NovoPerfil = new ComandoAssincrono(() => { PerfilSelecionado = null; PreencherPerfil(null); return Task.CompletedTask; });
        GravarPerfil = new ComandoAssincrono(GravarPerfilAsync);
        PreencherHorario(null);
    }

    public override string Titulo => "Perfis e horários";

    public ComandoAssincrono NovaEmpresa { get; }

    public ComandoAssincrono GravarEmpresa { get; }

    public ComandoAssincrono NovaSala { get; }

    public ComandoAssincrono GravarSala { get; }

    public ComandoAssincrono NovoHorario { get; }

    public ComandoAssincrono GravarHorario { get; }

    public ComandoAssincrono ExcluirHorario { get; }

    public ComandoAssincrono GravarFeriado { get; }

    public ComandoAssincrono ExcluirFeriado { get; }

    public ComandoAssincrono NovoPerfil { get; }

    public ComandoAssincrono GravarPerfil { get; }

    public IReadOnlyList<LinhaDeEmpresa> Empresas { get => _empresas; private set => Definir(ref _empresas, value); }

    public IReadOnlyList<LinhaDeSala> Salas { get => _salas; private set => Definir(ref _salas, value); }

    public IReadOnlyList<LinhaDeHorario> Horarios { get => _horarios; private set => Definir(ref _horarios, value); }

    public IReadOnlyList<LinhaDeFeriado> Feriados { get => _feriados; private set => Definir(ref _feriados, value); }

    public IReadOnlyList<LinhaDePerfil> Perfis { get => _perfis; private set => Definir(ref _perfis, value); }

    public IReadOnlyList<OpcaoDeLista> OpcoesDeEmpresa { get => _opcoesDeEmpresa; private set => Definir(ref _opcoesDeEmpresa, value); }

    public IReadOnlyList<OpcaoDeLista> OpcoesDeHorario { get => _opcoesDeHorario; private set => Definir(ref _opcoesDeHorario, value); }

    // ---- Empresa ----

    public LinhaDeEmpresa? EmpresaSelecionada
    {
        get => _empresaSelecionada;
        set
        {
            if (Definir(ref _empresaSelecionada, value) && value is not null)
            {
                PreencherEmpresa(_parametros.Empresas.FirstOrDefault(e => e.Id == value.Id));
            }
        }
    }

    public string TituloDaEmpresa => _empresaId.Length == 0 ? "Nova empresa" : $"Empresa: {EmpresaNome}";

    public string EmpresaNome { get => _empresaNome; set => Definir(ref _empresaNome, value ?? string.Empty); }

    public string EmpresaCnpj { get => _empresaCnpj; set => Definir(ref _empresaCnpj, value ?? string.Empty); }

    public bool EmpresaAtiva { get => _empresaAtiva; set => Definir(ref _empresaAtiva, value); }

    // ---- Sala ----

    public LinhaDeSala? SalaSelecionada
    {
        get => _salaSelecionada;
        set
        {
            if (Definir(ref _salaSelecionada, value) && value is not null)
            {
                PreencherSala(_parametros.Salas.FirstOrDefault(s => s.Id == value.Id));
            }
        }
    }

    public string TituloDaSala => _salaId.Length == 0 ? "Nova sala ou unidade" : $"Sala: {SalaNome}";

    public string SalaNome { get => _salaNome; set => Definir(ref _salaNome, value ?? string.Empty); }

    public string SalaEmpresaId { get => _salaEmpresaId; set => Definir(ref _salaEmpresaId, value ?? string.Empty); }

    public string SalaAndar { get => _salaAndar; set => Definir(ref _salaAndar, value ?? string.Empty); }

    public string SalaBloco { get => _salaBloco; set => Definir(ref _salaBloco, value ?? string.Empty); }

    public bool SalaAtiva { get => _salaAtiva; set => Definir(ref _salaAtiva, value); }

    // ---- Horário ----

    public LinhaDeHorario? HorarioSelecionado
    {
        get => _horarioSelecionado;
        set
        {
            if (Definir(ref _horarioSelecionado, value) && value is not null)
            {
                PreencherHorario(_parametros.Horarios.FirstOrDefault(h => h.Id == value.Id));
            }
        }
    }

    public int HorarioId
    {
        get => _horarioId;
        private set
        {
            if (Definir(ref _horarioId, value))
            {
                Avisar(nameof(TituloDoHorario));
                ExcluirHorario.ReavaliarDisponibilidade();
            }
        }
    }

    public string TituloDoHorario => HorarioId == 0 ? "Nova tabela de horário" : $"Tabela {HorarioId}: {HorarioNome}";

    public string HorarioNome { get => _horarioNome; set => Definir(ref _horarioNome, value ?? string.Empty); }

    /// <summary>Os oito dias (domingo a sábado e feriado), cada um com as faixas em texto.</summary>
    public IReadOnlyList<DiaDoHorario> Dias { get => _dias; private set => Definir(ref _dias, value); }

    // ---- Feriado ----

    public LinhaDeFeriado? FeriadoSelecionado
    {
        get => _feriadoSelecionado;
        set
        {
            if (Definir(ref _feriadoSelecionado, value))
            {
                ExcluirFeriado.ReavaliarDisponibilidade();
            }
        }
    }

    public DateTime? FeriadoDia
    {
        get => _feriadoDia;
        set
        {
            if (Definir(ref _feriadoDia, value))
            {
                GravarFeriado.ReavaliarDisponibilidade();
            }
        }
    }

    public string FeriadoNome { get => _feriadoNome; set => Definir(ref _feriadoNome, value ?? string.Empty); }

    // ---- Perfil ----

    public LinhaDePerfil? PerfilSelecionado
    {
        get => _perfilSelecionado;
        set
        {
            if (Definir(ref _perfilSelecionado, value) && value is not null)
            {
                PreencherPerfil(_parametros.Perfis.FirstOrDefault(p => p.Id == value.Id));
            }
        }
    }

    public string TituloDoPerfil => _perfilId.Length == 0 ? "Novo perfil" : $"Perfil: {PerfilNome}";

    public string PerfilNome { get => _perfilNome; set => Definir(ref _perfilNome, value ?? string.Empty); }

    /// <summary>Os campos do formulário de pessoa; marcado é obrigatório para este perfil.</summary>
    public IReadOnlyList<OpcaoMarcavel> CamposDoPerfil { get => _camposDoPerfil; private set => Definir(ref _camposDoPerfil, value); }

    public bool PerfilExigeAnfitriao { get => _perfilExigeAnfitriao; set => Definir(ref _perfilExigeAnfitriao, value); }

    /// <summary>Dias de validade a partir do cadastro (0 = só o dia); vazio = sem validade padrão.</summary>
    public string PerfilDiasDeValidade { get => _perfilDiasDeValidade; set => Definir(ref _perfilDiasDeValidade, value ?? string.Empty); }

    public string PerfilHorarioId { get => _perfilHorarioId; set => Definir(ref _perfilHorarioId, value ?? string.Empty); }

    /// <summary>Entradas por dia; vazio = sem limite.</summary>
    public string PerfilLimite { get => _perfilLimite; set => Definir(ref _perfilLimite, value ?? string.Empty); }

    /// <summary>Por quantos dias guardar os dados depois de inativar (LGPD, docs/43 P7).</summary>
    public string PerfilRetencao { get => _perfilRetencao; set => Definir(ref _perfilRetencao, value ?? string.Empty); }

    /// <summary>Catracas permitidas (ex.: "1, 2"); vazio = todas.</summary>
    public string PerfilCatracas { get => _perfilCatracas; set => Definir(ref _perfilCatracas, value ?? string.Empty); }

    public bool PerfilAtivo { get => _perfilAtivo; set => Definir(ref _perfilAtivo, value); }

    public override Task AtualizarAsync(CancellationToken cancelamento = default) =>
        Tentar(async () =>
        {
            _parametros = await Cliente.ObterParametrosDoCadastroAsync(new ObterParametrosDoCadastroRequest(), cancellationToken: cancelamento);
            var empresas = _parametros.Empresas.ToDictionary(e => e.Id, e => e.Nome, StringComparer.Ordinal);
            var horarios = _parametros.Horarios.ToDictionary(h => h.Id, h => h.Nome);
            var campos = _parametros.Campos.ToDictionary(c => c.Codigo, c => c.Rotulo, StringComparer.Ordinal);

            Empresas = [.. _parametros.Empresas.Select(e => new LinhaDeEmpresa(e.Id, e.Nome, FormatarCnpj(e.Cnpj), e.Ativa ? "Ativa" : "Inativa"))];
            Salas = [.. _parametros.Salas.Select(s => new LinhaDeSala(
                s.Id, s.Nome, empresas.GetValueOrDefault(s.EmpresaId, "—"),
                string.Join(" · ", new[] { s.Andar.Length > 0 ? $"andar {s.Andar}" : null, s.Bloco.Length > 0 ? $"bloco {s.Bloco}" : null }.OfType<string>()),
                s.Ativa ? "Ativa" : "Inativa"))];
            Horarios = [.. _parametros.Horarios.Select(h => new LinhaDeHorario(h.Id, h.Nome, Resumo(h)))];
            Feriados = [.. _parametros.Feriados.Select(f => new LinhaDeFeriado(Data(f.Dia), f.Nome))];
            Perfis = [.. _parametros.Perfis.Select(p => new LinhaDePerfil(
                p.Id, p.Nome, p.Pronto ? "Pronto" : "Criado aqui",
                Regras(p, campos, horarios)))];
            OpcoesDeEmpresa = [new(string.Empty, "(do prédio, sem empresa)"), .. _parametros.Empresas.Where(e => e.Ativa).Select(e => new OpcaoDeLista(e.Id, e.Nome))];
            OpcoesDeHorario = [new(string.Empty, "(sem restrição)"), .. _parametros.Horarios.Select(h => new OpcaoDeLista(h.Id.ToString(CultureInfo.InvariantCulture), $"{h.Id} · {h.Nome}"))];

            if (CamposDoPerfil.Count == 0 && _perfilId.Length == 0)
            {
                PreencherPerfil(null);
            }
        });

    private void PreencherEmpresa(EmpresaDoCadastro? e)
    {
        _empresaId = e?.Id ?? string.Empty;
        EmpresaNome = e?.Nome ?? string.Empty;
        EmpresaCnpj = FormatarCnpj(e?.Cnpj ?? string.Empty);
        EmpresaAtiva = e?.Ativa ?? true;
        Avisar(nameof(TituloDaEmpresa));
    }

    private void PreencherSala(SalaDoCadastro? s)
    {
        _salaId = s?.Id ?? string.Empty;
        SalaNome = s?.Nome ?? string.Empty;
        SalaEmpresaId = s?.EmpresaId ?? string.Empty;
        SalaAndar = s?.Andar ?? string.Empty;
        SalaBloco = s?.Bloco ?? string.Empty;
        SalaAtiva = s?.Ativa ?? true;
        Avisar(nameof(TituloDaSala));
    }

    private void PreencherHorario(HorarioDoCadastro? h)
    {
        HorarioId = h?.Id ?? 0;
        HorarioNome = h?.Nome ?? string.Empty;
        Dias = [.. Enumerable.Range(0, 8).Select(dia => new DiaDoHorario(
            dia, NomesDosDias[dia],
            string.Join(", ", (h?.Faixas ?? []).Where(f => f.Dia == dia).OrderBy(f => f.Inicio).Select(f => $"{Hora(f.Inicio)}-{Hora(f.Fim)}"))))];
        Avisar(nameof(TituloDoHorario));
    }

    private void PreencherPerfil(PerfilDoCadastro? p)
    {
        _perfilId = p?.Id ?? string.Empty;
        PerfilNome = p?.Nome ?? string.Empty;
        var obrigatorios = p?.CamposObrigatorios.ToHashSet(StringComparer.Ordinal) ?? ["nome"];
        CamposDoPerfil = [.. _parametros.Campos.Select(c => new OpcaoMarcavel(c.Codigo, c.Rotulo, string.Empty, obrigatorios.Contains(c.Codigo)))];
        PerfilExigeAnfitriao = p?.ExigeAnfitriao ?? false;
        PerfilDiasDeValidade = p is null || p.DiasDeValidade < 0 ? string.Empty : p.DiasDeValidade.ToString(CultureInfo.InvariantCulture);
        PerfilHorarioId = p is { TabelaDeHorario: > 0 } ? p.TabelaDeHorario.ToString(CultureInfo.InvariantCulture) : string.Empty;
        PerfilLimite = p is { LimiteDiario: > 0 } ? p.LimiteDiario.ToString(CultureInfo.InvariantCulture) : string.Empty;
        PerfilRetencao = (p?.DiasDeRetencao ?? 180).ToString(CultureInfo.InvariantCulture);
        PerfilCatracas = string.Join(", ", p?.Catracas ?? []);
        PerfilAtivo = p?.Ativo ?? true;
        Avisar(nameof(TituloDoPerfil));
    }

    private async Task GravarEmpresaAsync()
    {
        var empresa = new EmpresaDoCadastro { Id = _empresaId, Nome = EmpresaNome.Trim(), Cnpj = EmpresaCnpj.Trim(), Ativa = EmpresaAtiva };
        await Gravar(() => Cliente.GravarEmpresaAsync(new GravarEmpresaRequest { Empresa = empresa }).ResponseAsync, $"Empresa {empresa.Nome} gravada.", id => _empresaId = id).ConfigureAwait(true);
    }

    private async Task GravarSalaAsync()
    {
        var sala = new SalaDoCadastro { Id = _salaId, Nome = SalaNome.Trim(), EmpresaId = SalaEmpresaId, Andar = SalaAndar.Trim(), Bloco = SalaBloco.Trim(), Ativa = SalaAtiva };
        await Gravar(() => Cliente.GravarSalaAsync(new GravarSalaRequest { Sala = sala }).ResponseAsync, $"Sala {sala.Nome} gravada.", id => _salaId = id).ConfigureAwait(true);
    }

    private async Task GravarHorarioAsync()
    {
        var horario = new HorarioDoCadastro { Id = HorarioId, Nome = HorarioNome.Trim() };
        foreach (var dia in Dias)
        {
            if (!LerFaixas(dia.Faixas, out var faixas))
            {
                Mensagem = $"{dia.Nome}: escreva as faixas como 08:00-12:00, 13:00-18:00 (até 24:00), ou deixe vazio para não liberar no dia.";
                return;
            }

            horario.Faixas.AddRange(faixas.Select(f => new FaixaDoHorario { Dia = dia.Dia, Inicio = f.Inicio, Fim = f.Fim }));
        }

        await Gravar(
            () => Cliente.GravarHorarioAsync(new GravarHorarioRequest { Horario = horario }).ResponseAsync,
            $"Tabela de horário {horario.Nome} gravada.",
            id => HorarioId = int.Parse(id, CultureInfo.InvariantCulture)).ConfigureAwait(true);
    }

    private async Task ExcluirHorarioAsync()
    {
        var id = HorarioId;
        await Gravar(() => Cliente.ExcluirHorarioAsync(new ExcluirHorarioRequest { Id = id }).ResponseAsync, $"Tabela {id} excluída.", _ => PreencherHorario(null)).ConfigureAwait(true);
    }

    private async Task GravarFeriadoAsync()
    {
        if (FeriadoDia is not { } dia)
        {
            return;
        }

        var feriado = new FeriadoDoCadastro { Dia = dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Nome = FeriadoNome.Trim() };
        await Gravar(() => Cliente.GravarFeriadoAsync(new GravarFeriadoRequest { Feriado = feriado }).ResponseAsync, $"Feriado de {Data(feriado.Dia)} gravado.", _ =>
        {
            FeriadoDia = null;
            FeriadoNome = string.Empty;
        }).ConfigureAwait(true);
    }

    private async Task ExcluirFeriadoAsync()
    {
        if (FeriadoSelecionado is not { } feriado)
        {
            return;
        }

        var dia = DateOnly.ParseExact(feriado.Dia, "dd/MM/yyyy", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await Gravar(() => Cliente.ExcluirFeriadoAsync(new ExcluirFeriadoRequest { Dia = dia }).ResponseAsync, $"Feriado de {feriado.Dia} desmarcado.", _ => { }).ConfigureAwait(true);
    }

    private async Task GravarPerfilAsync()
    {
        var perfil = new PerfilDoCadastro { Id = _perfilId, Nome = PerfilNome.Trim(), ExigeAnfitriao = PerfilExigeAnfitriao, Ativo = PerfilAtivo };
        perfil.CamposObrigatorios.AddRange(CamposDoPerfil.Where(c => c.Marcado).Select(c => c.Codigo));

        if (!Numero(PerfilDiasDeValidade, 0, 3650, out var dias) || !Numero(PerfilLimite, 1, 100, out var limite)
            || !Numero(PerfilRetencao, 1, 3650, out var retencao) || retencao is null)
        {
            Mensagem = "Validade: 0 a 3650 dias ou vazio. Entradas por dia: 1 a 100 ou vazio. Retenção: 1 a 3650 dias.";
            return;
        }

        if (!PessoasViewModel.LerCatracas(PerfilCatracas, out var catracas))
        {
            Mensagem = "Catracas: números de 1 a 99 separados por vírgula, ou vazio para todas.";
            return;
        }

        perfil.DiasDeValidade = dias ?? -1;
        perfil.LimiteDiario = limite ?? 0;
        perfil.DiasDeRetencao = retencao.Value;
        perfil.TabelaDeHorario = PerfilHorarioId.Length > 0 ? int.Parse(PerfilHorarioId, CultureInfo.InvariantCulture) : 0;
        perfil.Catracas.AddRange(catracas);

        await Gravar(() => Cliente.GravarPerfilAsync(new GravarPerfilRequest { Perfil = perfil }).ResponseAsync, $"Perfil {perfil.Nome} gravado.", id => _perfilId = id).ConfigureAwait(true);
    }

    private async Task Gravar(Func<Task<ResultadoDoCadastroDePessoas>> chamada, string sucesso, Action<string> depois)
    {
        await Tentar(async () =>
        {
            var r = await chamada();
            if (!r.Gravado)
            {
                Mensagem = string.Join(" ", r.Problemas);
                return;
            }

            Mensagem = string.Join(" ", new[] { sucesso }.Concat(r.Avisos));
            depois(r.Id);
        }).ConfigureAwait(true);

        await AtualizarAsync().ConfigureAwait(true);
    }

    /// <summary>Lê "08:00-12:00, 13:00-18:00" em minutos; vazio é o dia sem faixa (não libera).</summary>
    public static bool LerFaixas(string texto, out List<(int Inicio, int Fim)> faixas)
    {
        faixas = [];
        foreach (var parte in (texto ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = Faixa().Match(parte);
            if (!m.Success)
            {
                return false;
            }

            var inicio = (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60) + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var fim = (int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) * 60) + int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            if (inicio >= 1440 || fim > 1440 || fim <= inicio || int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) > 59 || int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) > 59)
            {
                return false;
            }

            faixas.Add((inicio, fim));
        }

        return true;
    }

    [GeneratedRegex(@"^(\d{1,2}):(\d{2})\s*(?:-|–|a|às)\s*(\d{1,2}):(\d{2})$")]
    private static partial Regex Faixa();

    private static bool Numero(string texto, int minimo, int maximo, out int? valor)
    {
        valor = null;
        if (string.IsNullOrWhiteSpace(texto))
        {
            return true;
        }

        if (!int.TryParse(texto.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < minimo || n > maximo)
        {
            return false;
        }

        valor = n;
        return true;
    }

    private static string Hora(int minutos) => $"{minutos / 60:00}:{minutos % 60:00}";

    private static string Data(string iso) =>
        DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : iso;

    private static string FormatarCnpj(string cnpj) =>
        cnpj.Length == 14 && cnpj.All(char.IsAsciiDigit)
            ? $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}"
            : cnpj;

    private static string Resumo(HorarioDoCadastro h) =>
        h.Faixas.Count == 0
            ? "nenhuma faixa: não libera"
            : string.Join("; ", h.Faixas.GroupBy(f => f.Dia).OrderBy(g => g.Key)
                .Select(g => $"{NomesDosDias[g.Key][..3]} {string.Join(", ", g.OrderBy(f => f.Inicio).Select(f => $"{Hora(f.Inicio)}-{Hora(f.Fim)}"))}"));

    private static string Regras(PerfilDoCadastro p, Dictionary<string, string> campos, Dictionary<int, string> horarios)
    {
        var partes = new List<string>
        {
            "obrigatório: " + string.Join(", ", p.CamposObrigatorios.Select(c => campos.GetValueOrDefault(c, c))),
            p.DiasDeValidade switch { < 0 => "sem validade padrão", 0 => "vale no dia", var d => $"vale {d} dia(s)" },
        };
        if (p.TabelaDeHorario > 0)
        {
            partes.Add("horário " + horarios.GetValueOrDefault(p.TabelaDeHorario, p.TabelaDeHorario.ToString(CultureInfo.InvariantCulture)));
        }

        if (p.LimiteDiario > 0)
        {
            partes.Add($"{p.LimiteDiario} entrada(s) por dia");
        }

        if (p.Catracas.Count > 0)
        {
            partes.Add("catracas " + string.Join(", ", p.Catracas));
        }

        if (!p.Ativo)
        {
            partes.Add("inativo");
        }

        return string.Join(" · ", partes);
    }
}
