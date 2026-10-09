using System.Globalization;
using Access.Domain.Credentials;
using Access.Domain.Usuarios;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Edge.Supervisor;

/// <summary>Cadastro local de pessoas, credenciais, parâmetros e catraca fechada (docs/43 P3, ADR-0026).</summary>
/// <remarks>
/// <para>
/// O código de uma credencial entra pelo pedido e nunca volta em claro: a resposta traz a máscara de
/// <see cref="CredentialValue.Mascarar"/>, como em todo o painel (ADR-0008, ADR-0014).
/// </para>
/// <para>
/// Documento, telefone, e-mail e veículo voltam mascarados, e nascimento e responsável vazios, para quem
/// não tem <see cref="Permissoes.PessoasVerDados"/>. Ao gravar, um desses campos que volta vazio ou igual à
/// máscara mantém o valor guardado: quem não vê o dado não o apaga sem querer.
/// </para>
/// </remarks>
public sealed partial class EdgeControlService
{
    private readonly CadastroDePessoas? _pessoas;
    private readonly ParametrosDoCadastro? _parametrosDoCadastro;

    /// <summary>A máscara de um dado pessoal: só os dois últimos caracteres.</summary>
    public static string MascararDado(string? valor) =>
        string.IsNullOrEmpty(valor) ? string.Empty
        : valor.Length <= 4 ? "****"
        : new string('*', valor.Length - 2) + valor[^2..];

    public override Task<BuscarPessoasResponse> BuscarPessoas(BuscarPessoasRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new BuscarPessoasResponse();
        foreach (var p in Pessoas().Buscar(Vazio(request.Texto), Vazio(request.PerfilId), Vazio(request.EmpresaId), Vazio(request.Situacao), request.Limite > 0 ? request.Limite : 200))
        {
            var linha = new LinhaDePessoa
            {
                Id = p.Id,
                Nome = p.Nome,
                Perfil = p.Perfil,
                Empresa = p.Empresa ?? string.Empty,
                Sala = p.Sala ?? string.Empty,
                Situacao = p.Situacao,
                Credenciais = p.Credenciais,
            };
            if (p.ValidoAte is { } ate)
            {
                linha.ValidoAte = Timestamp.FromDateTimeOffset(ate);
            }

            resposta.Pessoas.Add(linha);
        }

        return Task.FromResult(resposta);
    }

    public override Task<FichaDaPessoa> ObterPessoa(ObterPessoaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pessoa = Pessoas().Obter(request.Id)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Pessoa não encontrada."));
        var completos = PodeVerDados(context);
        var d = pessoa.Dados;

        var msg = new PessoaDoCadastro
        {
            Id = d.Id ?? string.Empty,
            PerfilId = d.PerfilId,
            NomeCompleto = d.NomeCompleto,
            NomeSocial = d.NomeSocial ?? string.Empty,
            TipoDoDocumento = d.TipoDoDocumento ?? string.Empty,
            Documento = completos ? d.Documento ?? string.Empty : MascararDado(d.Documento),
            Nascimento = completos ? d.Nascimento?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty : string.Empty,
            Telefone = completos ? d.Telefone ?? string.Empty : MascararDado(d.Telefone),
            Email = completos ? d.Email ?? string.Empty : MascararDado(d.Email),
            Veiculo = completos ? d.Veiculo ?? string.Empty : MascararDado(d.Veiculo),
            Responsavel = completos ? d.Responsavel ?? string.Empty : string.Empty,
            EmpresaId = d.EmpresaId ?? string.Empty,
            SalaId = d.SalaId ?? string.Empty,
            AnfitriaoId = d.AnfitriaoId ?? string.Empty,
            Departamento = d.Departamento ?? string.Empty,
            Cargo = d.Cargo ?? string.Empty,
            Matricula = d.Matricula ?? string.Empty,
            Observacao = d.Observacao ?? string.Empty,
            TabelaDeHorario = d.TabelaDeHorario ?? 0,
            LimiteDiario = d.LimiteDiario ?? 0,
            AtendimentoPrioritario = d.AtendimentoPrioritario,
        };
        msg.Catracas.AddRange(d.Catracas);
        if (d.ValidoDe is { } de)
        {
            msg.ValidoDe = Timestamp.FromDateTimeOffset(de);
        }

        if (d.ValidoAte is { } ate)
        {
            msg.ValidoAte = Timestamp.FromDateTimeOffset(ate);
        }

        var ficha = new FichaDaPessoa
        {
            Pessoa = msg,
            Situacao = pessoa.Situacao,
            MotivoDaSituacao = pessoa.MotivoDaSituacao ?? string.Empty,
            CriadaEm = Timestamp.FromDateTimeOffset(pessoa.CriadaEm),
            AtualizadaEm = Timestamp.FromDateTimeOffset(pessoa.AtualizadaEm),
            DadosCompletos = completos,
        };

        foreach (var c in pessoa.Credenciais)
        {
            var credencial = new CredencialDaPessoa
            {
                Id = c.Id,
                Tipo = c.Tipo,
                CodigoMascarado = CredentialValue.Mascarar(c.Valor),
                Situacao = c.Situacao,
                Motivo = c.Motivo ?? string.Empty,
                CriadaEm = Timestamp.FromDateTimeOffset(c.CriadaEm),
            };
            if (c.ValidoDe is { } cde)
            {
                credencial.ValidoDe = Timestamp.FromDateTimeOffset(cde);
            }

            if (c.ValidoAte is { } cate)
            {
                credencial.ValidoAte = Timestamp.FromDateTimeOffset(cate);
            }

            ficha.Credenciais.Add(credencial);
        }

        return Task.FromResult(ficha);
    }

    public override Task<ResultadoDoCadastroDePessoas> GravarPessoa(GravarPessoaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var p = request.Pessoa ?? new PessoaDoCadastro();
        var cadastro = Pessoas();

        DateOnly? nascimento = null;
        if (!string.IsNullOrWhiteSpace(p.Nascimento))
        {
            if (!DateOnly.TryParseExact(p.Nascimento, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
            {
                return Resultado(ResultadoDoCadastro.Recusado("Data de nascimento inválida (use AAAA-MM-DD)."));
            }

            nascimento = data;
        }

        var dados = new DadosDaPessoa
        {
            Id = Vazio(p.Id),
            PerfilId = p.PerfilId,
            NomeCompleto = p.NomeCompleto,
            NomeSocial = Vazio(p.NomeSocial),
            TipoDoDocumento = Vazio(p.TipoDoDocumento),
            Documento = Vazio(p.Documento),
            Nascimento = nascimento,
            Telefone = Vazio(p.Telefone),
            Email = Vazio(p.Email),
            Veiculo = Vazio(p.Veiculo),
            Responsavel = Vazio(p.Responsavel),
            EmpresaId = Vazio(p.EmpresaId),
            SalaId = Vazio(p.SalaId),
            AnfitriaoId = Vazio(p.AnfitriaoId),
            Departamento = Vazio(p.Departamento),
            Cargo = Vazio(p.Cargo),
            Matricula = Vazio(p.Matricula),
            Observacao = Vazio(p.Observacao),
            ValidoDe = p.ValidoDe?.ToDateTimeOffset(),
            ValidoAte = p.ValidoAte?.ToDateTimeOffset(),
            TabelaDeHorario = p.TabelaDeHorario > 0 ? p.TabelaDeHorario : null,
            LimiteDiario = p.LimiteDiario > 0 ? p.LimiteDiario : null,
            AtendimentoPrioritario = p.AtendimentoPrioritario,
            Catracas = [.. p.Catracas],
        };

        // Quem não vê o dado completo recebeu a máscara: o que volta igual a ela, ou vazio, fica como está.
        if (dados.Id is { } id && !PodeVerDados(context) && cadastro.Obter(id) is { } atual)
        {
            var a = atual.Dados;
            dados = dados with
            {
                TipoDoDocumento = Mantido(dados.Documento, a.Documento) ? a.TipoDoDocumento : dados.TipoDoDocumento,
                Documento = Mantido(dados.Documento, a.Documento) ? a.Documento : dados.Documento,
                Telefone = Mantido(dados.Telefone, a.Telefone) ? a.Telefone : dados.Telefone,
                Email = Mantido(dados.Email, a.Email) ? a.Email : dados.Email,
                Veiculo = Mantido(dados.Veiculo, a.Veiculo) ? a.Veiculo : dados.Veiculo,
                Nascimento = dados.Nascimento ?? a.Nascimento,
                Responsavel = dados.Responsavel ?? a.Responsavel,
            };
        }

        return Resultado(cadastro.Gravar(QuemId(context), dados, _relogio()));
    }

    public override Task<ResultadoDoCadastroDePessoas> MudarSituacaoDaPessoa(MudarSituacaoDaPessoaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Resultado(Pessoas().MudarSituacao(QuemId(context), request.Id, request.Situacao, Vazio(request.Motivo), _relogio()));
    }

    public override Task<ResultadoDoCadastroDePessoas> AdicionarCredencial(AdicionarCredencialRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Resultado(Pessoas().AdicionarCredencial(
            QuemId(context), request.PessoaId, request.Tipo, request.Codigo,
            request.ValidoDe?.ToDateTimeOffset(), request.ValidoAte?.ToDateTimeOffset(), _relogio()));
    }

    public override Task<ResultadoDoCadastroDePessoas> MudarSituacaoDaCredencial(MudarSituacaoDaCredencialRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Resultado(Pessoas().MudarCredencial(QuemId(context), request.Id, request.Situacao, Vazio(request.Motivo), _relogio()));
    }

    public override Task<ParametrosDoCadastroDePessoas> ObterParametrosDoCadastro(ObterParametrosDoCadastroRequest request, ServerCallContext context)
    {
        var parametros = Parametros();
        var resposta = new ParametrosDoCadastroDePessoas();

        foreach (var perfil in parametros.Perfis())
        {
            var msg = new PerfilDoCadastro
            {
                Id = perfil.Id,
                Nome = perfil.Nome,
                Pronto = perfil.Pronto,
                ExigeAnfitriao = perfil.ExigeAnfitriao,
                DiasDeValidade = perfil.DiasDeValidade ?? -1,
                TabelaDeHorario = perfil.TabelaDeHorario ?? 0,
                LimiteDiario = perfil.LimiteDiario ?? 0,
                DiasDeRetencao = perfil.DiasDeRetencao,
                Ativo = perfil.Ativo,
            };
            msg.CamposObrigatorios.AddRange(perfil.CamposObrigatorios);
            msg.Catracas.AddRange(perfil.Catracas);
            resposta.Perfis.Add(msg);
        }

        resposta.Empresas.AddRange(parametros.Empresas().Select(e => new EmpresaDoCadastro
        {
            Id = e.Id ?? string.Empty, Nome = e.Nome, Cnpj = e.Cnpj ?? string.Empty, Ativa = e.Ativa,
        }));
        resposta.Salas.AddRange(parametros.Salas().Select(s => new SalaDoCadastro
        {
            Id = s.Id ?? string.Empty, Nome = s.Nome, EmpresaId = s.EmpresaId ?? string.Empty,
            Andar = s.Andar ?? string.Empty, Bloco = s.Bloco ?? string.Empty, Ativa = s.Ativa,
        }));
        foreach (var tabela in parametros.Horarios())
        {
            var msg = new HorarioDoCadastro { Id = tabela.Id, Nome = tabela.Nome };
            msg.Faixas.AddRange(tabela.Faixas.Select(f => new FaixaDoHorario { Dia = f.Dia, Inicio = f.Inicio, Fim = f.Fim }));
            resposta.Horarios.Add(msg);
        }

        resposta.Feriados.AddRange(parametros.Feriados().Select(f => new FeriadoDoCadastro
        {
            Dia = f.Dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Nome = f.Nome,
        }));
        resposta.Campos.AddRange(CamposDaPessoa.Todos.Select(c => new CampoDoFormulario { Codigo = c.Codigo, Rotulo = c.Rotulo }));
        resposta.TiposDeDocumento.AddRange(CadastroDePessoas.TiposDeDocumento);
        resposta.TiposDeCodigo.AddRange(CadastroDePessoas.TiposDeCredencial);
        return Task.FromResult(resposta);
    }

    public override Task<ResultadoDoCadastroDePessoas> GravarEmpresa(GravarEmpresaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var e = request.Empresa ?? new EmpresaDoCadastro();
        return Resultado(Parametros().GravarEmpresa(QuemId(context), new Empresa(Vazio(e.Id), e.Nome, Vazio(e.Cnpj), e.Ativa), _relogio()));
    }

    public override Task<ResultadoDoCadastroDePessoas> GravarSala(GravarSalaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var s = request.Sala ?? new SalaDoCadastro();
        return Resultado(Parametros().GravarSala(
            QuemId(context), new Sala(Vazio(s.Id), s.Nome, Vazio(s.EmpresaId), Vazio(s.Andar), Vazio(s.Bloco), s.Ativa), _relogio()));
    }

    public override Task<ResultadoDoCadastroDePessoas> GravarHorario(GravarHorarioRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var h = request.Horario ?? new HorarioDoCadastro();
        return Resultado(Parametros().GravarHorario(
            QuemId(context), new TabelaDeHorario(h.Id, h.Nome, [.. h.Faixas.Select(f => new FaixaDeHorario(f.Dia, f.Inicio, f.Fim))]), _relogio()));
    }

    public override Task<ResultadoDoCadastroDePessoas> ExcluirHorario(ExcluirHorarioRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Resultado(Parametros().ExcluirHorario(QuemId(context), request.Id, _relogio()));
    }

    public override Task<ResultadoDoCadastroDePessoas> GravarFeriado(GravarFeriadoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var f = request.Feriado ?? new FeriadoDoCadastro();
        return DateOnly.TryParseExact(f.Dia, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)
            ? Resultado(Parametros().GravarFeriado(QuemId(context), new Feriado(dia, f.Nome), _relogio()))
            : Resultado(ResultadoDoCadastro.Recusado("Data do feriado inválida (use AAAA-MM-DD)."));
    }

    public override Task<ResultadoDoCadastroDePessoas> ExcluirFeriado(ExcluirFeriadoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DateOnly.TryParseExact(request.Dia, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)
            ? Resultado(Parametros().ExcluirFeriado(QuemId(context), dia, _relogio()))
            : Resultado(ResultadoDoCadastro.Recusado("Data do feriado inválida (use AAAA-MM-DD)."));
    }

    public override Task<ResultadoDoCadastroDePessoas> GravarPerfil(GravarPerfilRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var p = request.Perfil ?? new PerfilDoCadastro();
        var perfil = new PerfilDePessoa(
            p.Id, p.Nome, p.Pronto, [.. p.CamposObrigatorios], p.ExigeAnfitriao,
            p.DiasDeValidade >= 0 ? p.DiasDeValidade : null,
            p.TabelaDeHorario > 0 ? p.TabelaDeHorario : null,
            p.LimiteDiario > 0 ? p.LimiteDiario : null,
            p.DiasDeRetencao > 0 ? p.DiasDeRetencao : 180,
            p.Ativo,
            [.. p.Catracas]);
        return Resultado(Parametros().GravarPerfil(QuemId(context), perfil, _relogio()));
    }

    public override Task<ListarCatracasFechadasResponse> ListarCatracasFechadas(ListarCatracasFechadasRequest request, ServerCallContext context)
    {
        var resposta = new ListarCatracasFechadasResponse();
        if (_pessoas is null)
        {
            return Task.FromResult(resposta);
        }

        resposta.Catracas.AddRange(_pessoas.CatracasFechadas().Select(c => new CatracaFechadaNoCadastro
        {
            Inner = c.Inner, Em = Timestamp.FromDateTimeOffset(c.Em), Por = c.Por, Motivo = c.Motivo,
        }));
        return Task.FromResult(resposta);
    }

    public override Task<ResultadoDoCadastroDePessoas> FecharCatraca(FecharCatracaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Resultado(Pessoas().FecharCatraca(QuemNome(context), request.Inner, Vazio(request.Motivo), _relogio()));
    }

    public override Task<ResultadoDoCadastroDePessoas> AbrirCatraca(AbrirCatracaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Resultado(Pessoas().AbrirCatraca(QuemNome(context), request.Inner, Vazio(request.Motivo), _relogio()));
    }

    private CadastroDePessoas Pessoas() => _pessoas
        ?? throw new RpcException(new Status(StatusCode.FailedPrecondition,
            "O cadastro de pessoas não está disponível: a chave dos dados pessoais não pôde ser lida do cofre desta máquina. Veja o log do serviço."));

    private ParametrosDoCadastro Parametros() => _parametrosDoCadastro
        ?? throw new RpcException(new Status(StatusCode.FailedPrecondition, "O cadastro de pessoas não está disponível neste serviço."));

    // Sem base de usuários (ferramentas e testes) o login está desligado e tudo é visível, como antes do login.
    private bool PodeVerDados(ServerCallContext context) =>
        _usuarios is null || Chamador(context)?.Permissoes.Contains(Permissoes.PessoasVerDados) == true;

    private static string? QuemId(ServerCallContext context) => Chamador(context)?.Usuario.Id;

    private static string QuemNome(ServerCallContext context) =>
        Chamador(context) is { } c ? $"{c.Usuario.Nome} ({c.Usuario.Login})" : "painel";

    private static bool Mantido(string? recebido, string? guardado) =>
        recebido is null || (guardado is not null && recebido == MascararDado(guardado));

    private static string? Vazio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor;

    private static Task<ResultadoDoCadastroDePessoas> Resultado(ResultadoDoCadastro resultado)
    {
        var resposta = new ResultadoDoCadastroDePessoas { Gravado = resultado.Gravado, Id = resultado.Id ?? string.Empty };
        resposta.Problemas.AddRange(resultado.Problemas);
        resposta.Avisos.AddRange(resultado.Avisos);
        return Task.FromResult(resposta);
    }
}
