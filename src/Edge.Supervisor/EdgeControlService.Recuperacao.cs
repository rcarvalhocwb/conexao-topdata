using System.Collections.Concurrent;
using Access.Domain.Usuarios;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Edge.Supervisor;

/// <summary>
/// Recuperação da senha de um administrador pelo suporte Rayzer (docs/58): o pedido sai daqui, o suporte
/// assina fora, e a resposta assinada libera gravar a senha que a pessoa escolheu. Sem senha mestra.
/// </summary>
public sealed partial class EdgeControlService
{
    // Tentativas erradas de resposta por pedido: depois disso, só com um pedido novo.
    private const int TentativasPorPedido = 5;

    private const string RecusaGenerica =
        "Não foi possível recuperar com esta resposta. Confira o código com o suporte ou peça um novo.";

    // O pedido de cada login, em memória: some se o serviço reiniciar (aí basta pedir de novo).
    private readonly ConcurrentDictionary<string, PedidoPendente> _pedidosDeRecuperacao = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>As chaves públicas que conferem a resposta. Testes passam as deles; o produto usa as da versão.</summary>
    public IReadOnlyList<string> ChavesPublicasDoSuporte { get; init; } = ChavesDoSuporte.Publicas;

    public override Task<PedidoDeRecuperacao> PedirRecuperacaoPeloSuporte(PedirRecuperacaoPeloSuporteRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var login = (request.Login ?? string.Empty).Trim();

        if (_usuarios is null || _configuracoes is null)
        {
            return Task.FromResult(new PedidoDeRecuperacao { Motivo = "O login não está ligado neste serviço." });
        }

        if (ChavesPublicasDoSuporte.Count == 0)
        {
            return Task.FromResult(new PedidoDeRecuperacao
            {
                Motivo = "Esta versão ainda não tem a chave do suporte Rayzer. Peça a outro administrador uma senha provisória (tela Usuários).",
            });
        }

        if (login.Length is < 3 or > 40)
        {
            return Task.FromResult(new PedidoDeRecuperacao { Motivo = "Digite o seu usuário (login) antes de pedir." });
        }

        // O pedido sai mesmo para login que não existe: dizer "não existe" ensinaria quais existem. A
        // confirmação é que recusa.
        var agora = _relogio();
        var pedido = RecuperacaoPeloSuporte.Pedido(_configuracoes.IdDaInstalacao(agora), login, agora);
        _pedidosDeRecuperacao[login] = new PedidoPendente(pedido, 0);

        return Task.FromResult(new PedidoDeRecuperacao
        {
            Disponivel = true,
            Pedido = RecuperacaoPeloSuporte.ParaTexto(pedido),
            ValidoAte = Timestamp.FromDateTimeOffset(RecuperacaoPeloSuporte.ValidoAte(pedido)!.Value),
        });
    }

    public override Task<ResultadoDaGravacao> ConfirmarRecuperacaoPeloSuporte(ConfirmarRecuperacaoPeloSuporteRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var login = (request.Login ?? string.Empty).Trim();

        if (_usuarios is null || _configuracoes is null)
        {
            return Recusada("O login não está ligado neste serviço.");
        }

        if (!_pedidosDeRecuperacao.TryGetValue(login, out var pendente))
        {
            return Recusada("Não há pedido de recuperação para este usuário. Peça um pelo botão \"Gerar código de pedido\".");
        }

        var senhaNova = request.SenhaNova ?? string.Empty;
        var problemasDaSenha = UsuariosDoSistema.ProblemasNaSenha(senhaNova, null);
        if (problemasDaSenha.Count > 0)
        {
            return Task.FromResult(Resultado(problemasDaSenha));
        }

        var agora = _relogio();
        var resposta = RecuperacaoPeloSuporte.DoTexto(request.Resposta, RecuperacaoPeloSuporte.BytesDaResposta);
        var usuario = _usuarios.Listar().FirstOrDefault(u => string.Equals(u.Login, login, StringComparison.OrdinalIgnoreCase));
        var confere = RecuperacaoPeloSuporte.Confere(
            pendente.Pedido, resposta, _configuracoes.IdDaInstalacao(agora), login, agora, ChavesPublicasDoSuporte);

        if (!confere || usuario is not { Ativo: true } || !_usuarios.PermissoesDe(usuario.Id).Contains(Permissoes.UsuariosGerenciar))
        {
            var tentativas = pendente.Tentativas + 1;
            if (tentativas >= TentativasPorPedido || (confere && usuario is not { Ativo: true }))
            {
                _pedidosDeRecuperacao.TryRemove(login, out _);
            }
            else
            {
                _pedidosDeRecuperacao[login] = pendente with { Tentativas = tentativas };
            }

            // Assinatura certa, mas a conta não é de administrador ativo: o suporte não troca senha de
            // operador (isso é do administrador local).
            return Recusada(confere && usuario is { Ativo: true }
                ? "A recuperação pelo suporte é só para administradores. Peça ao administrador uma senha provisória."
                : RecusaGenerica);
        }

        var problemas = _usuarios.GravarSenhaRecuperadaPeloSuporte(usuario.Id, senhaNova, agora);
        if (problemas.Count == 0)
        {
            _pedidosDeRecuperacao.TryRemove(login, out _);
        }

        return Task.FromResult(Resultado(problemas));
    }

    private sealed record PedidoPendente(byte[] Pedido, int Tentativas);
}
