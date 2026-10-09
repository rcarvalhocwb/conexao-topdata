using System.Globalization;
using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;

namespace Desktop.ViewModels;

/// <summary>Uma opção de lista (perfil, empresa, sala, horário, situação).</summary>
/// <param name="Codigo">O que vai para o serviço; vazio é "nenhum" ou "todos".</param>
/// <param name="Nome">O que a tela mostra.</param>
public sealed record OpcaoDeLista(string Codigo, string Nome);

/// <summary>Uma pessoa na lista.</summary>
public sealed record LinhaDaPessoa(string Id, string Nome, string Perfil, string Empresa, string Sala, string Situacao, string ValidoAte, int Credenciais);

/// <summary>Uma credencial na ficha: só a máscara do código.</summary>
public sealed record LinhaDaCredencial(string Id, string Tipo, string Codigo, string Situacao, string Validade);

/// <summary>
/// Cadastro local de pessoas (docs/43, ADR-0026): buscar, cadastrar com os campos que o perfil exige,
/// bloquear e desbloquear na hora, e dar ou recolher credenciais.
/// </summary>
/// <remarks>
/// O serviço confere a permissão de cada ação e devolve documento e contato mascarados a quem não pode
/// vê-los; gravar a ficha com a máscara mantém o dado guardado. O código da credencial vai ao serviço e
/// nunca volta: a ficha mostra só a máscara.
/// </remarks>
public sealed class PessoasViewModel : TelaBase
{
    private const string FormatoDaData = "dd/MM/yyyy";

    private ParametrosDoCadastroDePessoas _parametros = new();
    private IReadOnlyList<LinhaDaPessoa> _pessoas = [];
    private LinhaDaPessoa? _selecionada;
    private string _busca = string.Empty;
    private string _filtroPerfil = string.Empty;
    private string _filtroSituacao = string.Empty;
    private IReadOnlyList<OpcaoDeLista> _perfis = [];
    private IReadOnlyList<OpcaoDeLista> _empresas = [];
    private IReadOnlyList<OpcaoDeLista> _salas = [];
    private IReadOnlyList<OpcaoDeLista> _horarios = [];
    private IReadOnlyList<OpcaoDeLista> _anfitrioes = [];
    private IReadOnlyList<LinhaDaCredencial> _credenciais = [];
    private LinhaDaCredencial? _credencialSelecionada;

    private string _id = string.Empty;
    private string _perfilId = "colaborador";
    private string _nomeCompleto = string.Empty;
    private string _nomeSocial = string.Empty;
    private string _tipoDoDocumento = "cpf";
    private string _documento = string.Empty;
    private string _nascimento = string.Empty;
    private string _telefone = string.Empty;
    private string _email = string.Empty;
    private string _veiculo = string.Empty;
    private string _responsavel = string.Empty;
    private string _empresaId = string.Empty;
    private string _salaId = string.Empty;
    private string _anfitriaoId = string.Empty;
    private string _departamento = string.Empty;
    private string _cargo = string.Empty;
    private string _matricula = string.Empty;
    private string _observacao = string.Empty;
    private DateTime? _validoDe;
    private DateTime? _validoAte;
    private string _horarioId = string.Empty;
    private string _limiteDiario = string.Empty;
    private bool _atendimentoPrioritario;
    private string _catracas = string.Empty;
    private string _situacao = string.Empty;
    private string _motivoDaSituacao = string.Empty;
    private bool _dadosCompletos = true;
    private string _motivo = string.Empty;
    private string _novoTipo = "cartao";
    private string _novoCodigo = string.Empty;
    private string _motivoDaCredencial = string.Empty;

    public PessoasViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
        Buscar = new ComandoAssincrono(() => BuscarAsync(CancellationToken.None));
        NovaPessoa = new ComandoAssincrono(() =>
        {
            Selecionada = null;
            Preencher(null);
            return Task.CompletedTask;
        });
        Gravar = new ComandoAssincrono(GravarAsync, () => NomeCompleto.Trim().Length > 0);
        Bloquear = new ComandoAssincrono(() => MudarSituacaoAsync("bloqueado"), () => Id.Length > 0 && Situacao == "ativo" && MotivoValido(Motivo));
        Desbloquear = new ComandoAssincrono(() => MudarSituacaoAsync("ativo"), () => Id.Length > 0 && Situacao is "bloqueado" or "inativo");
        Inativar = new ComandoAssincrono(() => MudarSituacaoAsync("inativo"), () => Id.Length > 0 && Situacao != "inativo" && MotivoValido(Motivo));
        AdicionarCredencial = new ComandoAssincrono(AdicionarCredencialAsync, () => Id.Length > 0 && NovoCodigo.Trim().Length > 0);
        MarcarPerdida = new ComandoAssincrono(() => MudarCredencialAsync("perdida"), PodeMudarCredencial);
        BloquearCredencial = new ComandoAssincrono(() => MudarCredencialAsync("bloqueada"), PodeMudarCredencial);
        DevolverCredencial = new ComandoAssincrono(() => MudarCredencialAsync("devolvida"), PodeMudarCredencial);
        ReativarCredencial = new ComandoAssincrono(() => MudarCredencialAsync("ativa"), () => CredencialSelecionada is { Situacao: not "Ativa" });
    }

    public override string Titulo => "Pessoas";

    public ComandoAssincrono Buscar { get; }

    public ComandoAssincrono NovaPessoa { get; }

    public ComandoAssincrono Gravar { get; }

    public ComandoAssincrono Bloquear { get; }

    public ComandoAssincrono Desbloquear { get; }

    public ComandoAssincrono Inativar { get; }

    public ComandoAssincrono AdicionarCredencial { get; }

    public ComandoAssincrono MarcarPerdida { get; }

    public ComandoAssincrono BloquearCredencial { get; }

    public ComandoAssincrono DevolverCredencial { get; }

    public ComandoAssincrono ReativarCredencial { get; }

    // ---- Busca ----

    /// <summary>Nome (partes com 3 letras ou mais), documento ou código da credencial.</summary>
    public string Busca { get => _busca; set => Definir(ref _busca, value ?? string.Empty); }

    public string FiltroPerfil { get => _filtroPerfil; set => Definir(ref _filtroPerfil, value ?? string.Empty); }

    public string FiltroSituacao { get => _filtroSituacao; set => Definir(ref _filtroSituacao, value ?? string.Empty); }

    /// <summary>Perfis para o filtro, com "Todos".</summary>
    public IReadOnlyList<OpcaoDeLista> PerfisDoFiltro => [new(string.Empty, "Todos os perfis"), .. Perfis];

    public IReadOnlyList<OpcaoDeLista> SituacoesDoFiltro { get; } =
        [new(string.Empty, "Todas"), new("ativo", "Ativas"), new("bloqueado", "Bloqueadas"), new("inativo", "Inativas")];

    public IReadOnlyList<LinhaDaPessoa> Pessoas { get => _pessoas; private set => Definir(ref _pessoas, value); }

    public LinhaDaPessoa? Selecionada
    {
        get => _selecionada;
        set
        {
            if (Definir(ref _selecionada, value) && value is not null)
            {
                _ = AbrirAsync(value.Id);
            }
        }
    }

    // ---- Listas do formulário ----

    public IReadOnlyList<OpcaoDeLista> Perfis
    {
        get => _perfis;
        private set
        {
            if (Definir(ref _perfis, value))
            {
                Avisar(nameof(PerfisDoFiltro));
            }
        }
    }

    public IReadOnlyList<OpcaoDeLista> Empresas { get => _empresas; private set => Definir(ref _empresas, value); }

    public IReadOnlyList<OpcaoDeLista> Salas { get => _salas; private set => Definir(ref _salas, value); }

    public IReadOnlyList<OpcaoDeLista> Horarios { get => _horarios; private set => Definir(ref _horarios, value); }

    /// <summary>Pessoas ativas que podem receber visita.</summary>
    public IReadOnlyList<OpcaoDeLista> Anfitrioes { get => _anfitrioes; private set => Definir(ref _anfitrioes, value); }

    public IReadOnlyList<OpcaoDeLista> TiposDeDocumento { get; } =
        [new("cpf", "CPF"), new("rg", "RG"), new("cnh", "CNH"), new("passaporte", "Passaporte"), new("rne", "RNE / RNM"), new("outro", "Outro")];

    public IReadOnlyList<OpcaoDeLista> TiposDeCredencial { get; } =
        [new("cartao", "Crachá ou cartão de proximidade"), new("qr", "QR code"), new("senha", "Senha de teclado")];

    // ---- Ficha ----

    public string Id
    {
        get => _id;
        private set
        {
            if (Definir(ref _id, value))
            {
                Avisar(nameof(TituloDaFicha));
                Avisar(nameof(TemFicha));
                Reavaliar();
            }
        }
    }

    public bool TemFicha => Id.Length > 0;

    public string TituloDaFicha => Id.Length == 0 ? "Nova pessoa" : $"Ficha: {NomeCompleto}";

    public string PerfilId
    {
        get => _perfilId;
        set
        {
            if (Definir(ref _perfilId, value ?? string.Empty))
            {
                Avisar(nameof(Obrigatorios));
            }
        }
    }

    /// <summary>O que o perfil escolhido exige, para a tela marcar.</summary>
    public string Obrigatorios
    {
        get
        {
            var perfil = _parametros.Perfis.FirstOrDefault(p => p.Id == PerfilId);
            if (perfil is null)
            {
                return string.Empty;
            }

            var rotulos = _parametros.Campos.ToDictionary(c => c.Codigo, c => c.Rotulo, StringComparer.Ordinal);
            var campos = perfil.CamposObrigatorios.Select(c => rotulos.GetValueOrDefault(c, c)).ToList();
            if (perfil.ExigeAnfitriao && !perfil.CamposObrigatorios.Contains("anfitriao"))
            {
                campos.Add(rotulos.GetValueOrDefault("anfitriao", "anfitrião"));
            }

            var validade = perfil.DiasDeValidade switch
            {
                < 0 => "sem validade padrão",
                0 => "vale só no dia do cadastro",
                var d => $"vale {d} dia(s) a partir do cadastro",
            };
            return $"Obrigatório para {perfil.Nome}: {string.Join(", ", campos)}. Validade: {validade}.";
        }
    }

    public string NomeCompleto
    {
        get => _nomeCompleto;
        set
        {
            if (Definir(ref _nomeCompleto, value ?? string.Empty))
            {
                Gravar.ReavaliarDisponibilidade();
            }
        }
    }

    public string NomeSocial { get => _nomeSocial; set => Definir(ref _nomeSocial, value ?? string.Empty); }

    public string TipoDoDocumento { get => _tipoDoDocumento; set => Definir(ref _tipoDoDocumento, value ?? string.Empty); }

    public string Documento { get => _documento; set => Definir(ref _documento, value ?? string.Empty); }

    /// <summary>Data de nascimento como digitada (dd/mm/aaaa).</summary>
    public string Nascimento { get => _nascimento; set => Definir(ref _nascimento, value ?? string.Empty); }

    public string Telefone { get => _telefone; set => Definir(ref _telefone, value ?? string.Empty); }

    public string Email { get => _email; set => Definir(ref _email, value ?? string.Empty); }

    public string Veiculo { get => _veiculo; set => Definir(ref _veiculo, value ?? string.Empty); }

    public string Responsavel { get => _responsavel; set => Definir(ref _responsavel, value ?? string.Empty); }

    public string EmpresaId { get => _empresaId; set => Definir(ref _empresaId, value ?? string.Empty); }

    public string SalaId { get => _salaId; set => Definir(ref _salaId, value ?? string.Empty); }

    public string AnfitriaoId { get => _anfitriaoId; set => Definir(ref _anfitriaoId, value ?? string.Empty); }

    public string Departamento { get => _departamento; set => Definir(ref _departamento, value ?? string.Empty); }

    public string Cargo { get => _cargo; set => Definir(ref _cargo, value ?? string.Empty); }

    public string Matricula { get => _matricula; set => Definir(ref _matricula, value ?? string.Empty); }

    public string Observacao { get => _observacao; set => Definir(ref _observacao, value ?? string.Empty); }

    /// <summary>Primeiro dia que vale (desde 00:00 em Brasília).</summary>
    public DateTime? ValidoDe { get => _validoDe; set => Definir(ref _validoDe, value); }

    /// <summary>Último dia que vale (até 23:59:59 em Brasília). Vazio usa a validade padrão do perfil na criação.</summary>
    public DateTime? ValidoAte { get => _validoAte; set => Definir(ref _validoAte, value); }

    /// <summary>Tabela de horário só desta pessoa; vazio usa a do perfil.</summary>
    public string HorarioId { get => _horarioId; set => Definir(ref _horarioId, value ?? string.Empty); }

    /// <summary>Entradas por dia só desta pessoa; vazio usa o limite do perfil.</summary>
    public string LimiteDiario { get => _limiteDiario; set => Definir(ref _limiteDiario, value ?? string.Empty); }

    public bool AtendimentoPrioritario { get => _atendimentoPrioritario; set => Definir(ref _atendimentoPrioritario, value); }

    /// <summary>Catracas permitidas só para esta pessoa, por número (ex.: "1, 2"); vazio usa as do perfil.</summary>
    public string Catracas { get => _catracas; set => Definir(ref _catracas, value ?? string.Empty); }

    /// <summary>ativo, bloqueado ou inativo.</summary>
    public string Situacao
    {
        get => _situacao;
        private set
        {
            if (Definir(ref _situacao, value))
            {
                Avisar(nameof(TextoDaSituacao));
                Reavaliar();
            }
        }
    }

    public string TextoDaSituacao => Situacao switch
    {
        "ativo" => "Ativa: passa conforme perfil, catracas, horário e validade.",
        "bloqueado" => $"Bloqueada: a catraca nega. {MotivoDaSituacao}",
        "inativo" => $"Inativa: a catraca nega. {MotivoDaSituacao}",
        _ => string.Empty,
    };

    public string MotivoDaSituacao { get => _motivoDaSituacao; private set => Definir(ref _motivoDaSituacao, value); }

    /// <summary>Falso quando o papel do usuário não vê documento e contato (vieram mascarados).</summary>
    public bool DadosCompletos
    {
        get => _dadosCompletos;
        private set
        {
            if (Definir(ref _dadosCompletos, value))
            {
                Avisar(nameof(DadosMascarados));
            }
        }
    }

    public bool DadosMascarados => !DadosCompletos;

    /// <summary>Por que bloquear ou inativar (5 a 200 letras).</summary>
    public string Motivo
    {
        get => _motivo;
        set
        {
            if (Definir(ref _motivo, value ?? string.Empty))
            {
                Reavaliar();
            }
        }
    }

    // ---- Credenciais ----

    public IReadOnlyList<LinhaDaCredencial> Credenciais { get => _credenciais; private set => Definir(ref _credenciais, value); }

    public LinhaDaCredencial? CredencialSelecionada
    {
        get => _credencialSelecionada;
        set
        {
            if (Definir(ref _credencialSelecionada, value))
            {
                Reavaliar();
            }
        }
    }

    public string NovoTipo { get => _novoTipo; set => Definir(ref _novoTipo, value ?? "cartao"); }

    /// <summary>O código como a catraca lê; some da tela depois de gravado.</summary>
    public string NovoCodigo
    {
        get => _novoCodigo;
        set
        {
            if (Definir(ref _novoCodigo, value ?? string.Empty))
            {
                Reavaliar();
            }
        }
    }

    public string MotivoDaCredencial
    {
        get => _motivoDaCredencial;
        set
        {
            if (Definir(ref _motivoDaCredencial, value ?? string.Empty))
            {
                Reavaliar();
            }
        }
    }

    public override async Task AtualizarAsync(CancellationToken cancelamento = default)
    {
        await Tentar(async () =>
        {
            _parametros = await Cliente.ObterParametrosDoCadastroAsync(new ObterParametrosDoCadastroRequest(), cancellationToken: cancelamento);
            Perfis = [.. _parametros.Perfis.Where(p => p.Ativo).Select(p => new OpcaoDeLista(p.Id, p.Nome))];
            Empresas = [new(string.Empty, "(nenhuma)"), .. _parametros.Empresas.Where(e => e.Ativa).Select(e => new OpcaoDeLista(e.Id, e.Nome))];
            var nomesDasEmpresas = _parametros.Empresas.ToDictionary(e => e.Id, e => e.Nome, StringComparer.Ordinal);
            Salas = [new(string.Empty, "(nenhuma)"), .. _parametros.Salas.Where(s => s.Ativa).Select(s => new OpcaoDeLista(
                s.Id, string.IsNullOrEmpty(s.EmpresaId) ? s.Nome : $"{s.Nome} · {nomesDasEmpresas.GetValueOrDefault(s.EmpresaId, "?")}"))];
            Horarios = [new(string.Empty, "(a do perfil)"), .. _parametros.Horarios.Select(h => new OpcaoDeLista(h.Id.ToString(CultureInfo.InvariantCulture), $"{h.Id} · {h.Nome}"))];
            Avisar(nameof(Obrigatorios));
        }).ConfigureAwait(true);

        await BuscarAsync(cancelamento).ConfigureAwait(true);
    }

    private async Task BuscarAsync(CancellationToken cancelamento)
    {
        await Tentar(async () =>
        {
            var resposta = await Cliente.BuscarPessoasAsync(
                new BuscarPessoasRequest { Texto = Busca.Trim(), PerfilId = FiltroPerfil, Situacao = FiltroSituacao, Limite = 500 },
                cancellationToken: cancelamento);
            Pessoas = [.. resposta.Pessoas.Select(p => new LinhaDaPessoa(
                p.Id, p.Nome, p.Perfil, p.Empresa, p.Sala, Situacoes(p.Situacao),
                p.ValidoAte is null ? "—" : FusoDoEvento.NoEvento(p.ValidoAte.ToDateTimeOffset()).ToString(FormatoDaData, CultureInfo.InvariantCulture),
                p.Credenciais))];

            var ativas = await Cliente.BuscarPessoasAsync(new BuscarPessoasRequest { Situacao = "ativo", Limite = 2000 }, cancellationToken: cancelamento);
            Anfitrioes = [new(string.Empty, "(ninguém)"), .. ativas.Pessoas.Where(p => p.Id != Id).Select(p => new OpcaoDeLista(p.Id, p.Nome))];
        }).ConfigureAwait(true);
    }

    private async Task AbrirAsync(string id)
    {
        await Tentar(async () => Preencher(await Cliente.ObterPessoaAsync(new ObterPessoaRequest { Id = id }))).ConfigureAwait(true);
    }

    private void Preencher(FichaDaPessoa? ficha)
    {
        var p = ficha?.Pessoa ?? new PessoaDoCadastro { PerfilId = PerfilId.Length > 0 ? PerfilId : "colaborador", TipoDoDocumento = "cpf" };
        Id = p.Id;
        PerfilId = p.PerfilId;
        NomeCompleto = p.NomeCompleto;
        NomeSocial = p.NomeSocial;
        TipoDoDocumento = p.TipoDoDocumento.Length > 0 ? p.TipoDoDocumento : "cpf";
        Documento = p.Documento;
        Nascimento = DateOnly.TryParseExact(p.Nascimento, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var nascimento)
            ? nascimento.ToString(FormatoDaData, CultureInfo.InvariantCulture)
            : string.Empty;
        Telefone = p.Telefone;
        Email = p.Email;
        Veiculo = p.Veiculo;
        Responsavel = p.Responsavel;
        EmpresaId = p.EmpresaId;
        SalaId = p.SalaId;
        AnfitriaoId = p.AnfitriaoId;
        Departamento = p.Departamento;
        Cargo = p.Cargo;
        Matricula = p.Matricula;
        Observacao = p.Observacao;
        ValidoDe = p.ValidoDe is null ? null : FusoDoEvento.NoEvento(p.ValidoDe.ToDateTimeOffset()).Date;
        ValidoAte = p.ValidoAte is null ? null : FusoDoEvento.NoEvento(p.ValidoAte.ToDateTimeOffset()).Date;
        HorarioId = p.TabelaDeHorario > 0 ? p.TabelaDeHorario.ToString(CultureInfo.InvariantCulture) : string.Empty;
        LimiteDiario = p.LimiteDiario > 0 ? p.LimiteDiario.ToString(CultureInfo.InvariantCulture) : string.Empty;
        AtendimentoPrioritario = p.AtendimentoPrioritario;
        Catracas = string.Join(", ", p.Catracas);
        MotivoDaSituacao = ficha?.MotivoDaSituacao ?? string.Empty;
        Situacao = ficha?.Situacao ?? string.Empty;
        DadosCompletos = ficha?.DadosCompletos ?? true;
        Motivo = string.Empty;
        NovoCodigo = string.Empty;
        MotivoDaCredencial = string.Empty;
        Credenciais = [.. (ficha?.Credenciais ?? []).Select(c => new LinhaDaCredencial(
            c.Id,
            TiposDeCredencial.FirstOrDefault(t => t.Codigo == c.Tipo)?.Nome ?? c.Tipo,
            c.CodigoMascarado,
            SituacaoDaCredencial(c.Situacao) + (c.Motivo.Length > 0 ? $" ({c.Motivo})" : string.Empty),
            Validade(c.ValidoDe, c.ValidoAte)))];
        Avisar(nameof(TituloDaFicha));
    }

    private async Task GravarAsync()
    {
        if (!LerFormulario(out var pessoa))
        {
            return;
        }

        await Tentar(async () =>
        {
            var r = await Cliente.GravarPessoaAsync(new GravarPessoaRequest { Pessoa = pessoa });
            if (!r.Gravado)
            {
                Mensagem = string.Join(" ", r.Problemas);
                return;
            }

            Mensagem = Id.Length == 0
                ? $"{pessoa.NomeCompleto} cadastrada. Agora dê a credencial (crachá, QR ou senha) na ficha."
                : $"Ficha de {pessoa.NomeCompleto} gravada.";
            Preencher(await Cliente.ObterPessoaAsync(new ObterPessoaRequest { Id = r.Id }));
        }).ConfigureAwait(true);

        await BuscarAsync(CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>Monta o pedido a partir do formulário; falso, com a mensagem, se algo não dá para ler.</summary>
    internal bool LerFormulario(out PessoaDoCadastro pessoa)
    {
        pessoa = new PessoaDoCadastro
        {
            Id = Id,
            PerfilId = PerfilId,
            NomeCompleto = NomeCompleto.Trim(),
            NomeSocial = NomeSocial.Trim(),
            TipoDoDocumento = Documento.Trim().Length > 0 ? TipoDoDocumento : string.Empty,
            Documento = Documento.Trim(),
            Telefone = Telefone.Trim(),
            Email = Email.Trim(),
            Veiculo = Veiculo.Trim(),
            Responsavel = Responsavel.Trim(),
            EmpresaId = EmpresaId,
            SalaId = SalaId,
            AnfitriaoId = AnfitriaoId,
            Departamento = Departamento.Trim(),
            Cargo = Cargo.Trim(),
            Matricula = Matricula.Trim(),
            Observacao = Observacao.Trim(),
            AtendimentoPrioritario = AtendimentoPrioritario,
        };

        if (Nascimento.Trim().Length > 0)
        {
            if (!DateOnly.TryParseExact(Nascimento.Trim(), FormatoDaData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
            {
                Mensagem = "Data de nascimento no formato dd/mm/aaaa.";
                return false;
            }

            pessoa.Nascimento = data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (ValidoDe is { } de)
        {
            pessoa.ValidoDe = Timestamp.FromDateTimeOffset(FusoDoEvento.DoEvento(de.Date));
        }

        if (ValidoAte is { } ate)
        {
            pessoa.ValidoAte = Timestamp.FromDateTimeOffset(FusoDoEvento.DoEvento(ate.Date.AddDays(1)).AddSeconds(-1));
        }

        if (HorarioId.Length > 0)
        {
            pessoa.TabelaDeHorario = int.Parse(HorarioId, CultureInfo.InvariantCulture);
        }

        if (LimiteDiario.Trim().Length > 0)
        {
            if (!int.TryParse(LimiteDiario.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var limite) || limite is < 1 or > 100)
            {
                Mensagem = "Entradas por dia: um número de 1 a 100, ou vazio para usar o do perfil.";
                return false;
            }

            pessoa.LimiteDiario = limite;
        }

        if (!LerCatracas(Catracas, out var catracas))
        {
            Mensagem = "Catracas: números de 1 a 99 separados por vírgula (ex.: 1, 2), ou vazio para usar as do perfil.";
            return false;
        }

        pessoa.Catracas.AddRange(catracas);
        return true;
    }

    /// <summary>Lê "1, 2 3" como [1, 2, 3].</summary>
    public static bool LerCatracas(string texto, out List<int> catracas)
    {
        catracas = [];
        foreach (var parte in (texto ?? string.Empty).Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(parte, NumberStyles.None, CultureInfo.InvariantCulture, out var numero) || numero is < 1 or > 99)
            {
                return false;
            }

            if (!catracas.Contains(numero))
            {
                catracas.Add(numero);
            }
        }

        return true;
    }

    private async Task MudarSituacaoAsync(string situacao)
    {
        var id = Id;
        await Tentar(async () =>
        {
            var r = await Cliente.MudarSituacaoDaPessoaAsync(new MudarSituacaoDaPessoaRequest { Id = id, Situacao = situacao, Motivo = Motivo.Trim() });
            if (!r.Gravado)
            {
                Mensagem = string.Join(" ", r.Problemas);
                return;
            }

            Mensagem = situacao switch
            {
                "bloqueado" => $"{NomeCompleto} bloqueada: a próxima leitura já é negada.",
                "inativo" => $"{NomeCompleto} inativada: a próxima leitura já é negada.",
                _ => $"{NomeCompleto} desbloqueada: volta a passar conforme as regras.",
            };
            Preencher(await Cliente.ObterPessoaAsync(new ObterPessoaRequest { Id = id }));
        }).ConfigureAwait(true);

        await BuscarAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private async Task AdicionarCredencialAsync()
    {
        var id = Id;
        await Tentar(async () =>
        {
            var r = await Cliente.AdicionarCredencialAsync(new AdicionarCredencialRequest { PessoaId = id, Tipo = NovoTipo, Codigo = NovoCodigo.Trim() });

            // O código digitado some da tela de qualquer jeito: ele não fica à vista.
            NovoCodigo = string.Empty;
            if (!r.Gravado)
            {
                Mensagem = string.Join(" ", r.Problemas);
                return;
            }

            Mensagem = "Credencial cadastrada: já vale na catraca.";
            Preencher(await Cliente.ObterPessoaAsync(new ObterPessoaRequest { Id = id }));
        }).ConfigureAwait(true);

        await BuscarAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private async Task MudarCredencialAsync(string situacao)
    {
        if (CredencialSelecionada is not { } credencial)
        {
            return;
        }

        var id = Id;
        await Tentar(async () =>
        {
            var r = await Cliente.MudarSituacaoDaCredencialAsync(new MudarSituacaoDaCredencialRequest
            {
                Id = credencial.Id, Situacao = situacao, Motivo = MotivoDaCredencial.Trim(),
            });
            if (!r.Gravado)
            {
                Mensagem = string.Join(" ", r.Problemas);
                return;
            }

            Mensagem = $"Credencial {credencial.Codigo}: {SituacaoDaCredencial(situacao).ToLowerInvariant()}.";
            Preencher(await Cliente.ObterPessoaAsync(new ObterPessoaRequest { Id = id }));
        }).ConfigureAwait(true);
    }

    private bool PodeMudarCredencial() => CredencialSelecionada is not null && MotivoValido(MotivoDaCredencial);

    private static bool MotivoValido(string motivo) => motivo.Trim().Length is >= 5 and <= 200;

    private void Reavaliar()
    {
        Bloquear?.ReavaliarDisponibilidade();
        Desbloquear?.ReavaliarDisponibilidade();
        Inativar?.ReavaliarDisponibilidade();
        AdicionarCredencial?.ReavaliarDisponibilidade();
        MarcarPerdida?.ReavaliarDisponibilidade();
        BloquearCredencial?.ReavaliarDisponibilidade();
        DevolverCredencial?.ReavaliarDisponibilidade();
        ReativarCredencial?.ReavaliarDisponibilidade();
    }

    private static string Situacoes(string situacao) => situacao switch
    {
        "ativo" => "Ativa",
        "bloqueado" => "Bloqueada",
        "inativo" => "Inativa",
        _ => situacao,
    };

    private static string SituacaoDaCredencial(string situacao) => situacao switch
    {
        "ativa" => "Ativa",
        "bloqueada" => "Bloqueada",
        "perdida" => "Perdida",
        "devolvida" => "Devolvida",
        _ => situacao,
    };

    private static string Validade(Timestamp? de, Timestamp? ate) => (de, ate) switch
    {
        (null, null) => "a da pessoa",
        _ => $"{Dia(de) ?? "…"} a {Dia(ate) ?? "…"}",
    };

    private static string? Dia(Timestamp? instante) =>
        instante is null ? null : FusoDoEvento.NoEvento(instante.ToDateTimeOffset()).ToString(FormatoDaData, CultureInfo.InvariantCulture);
}
