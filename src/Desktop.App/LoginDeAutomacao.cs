using Desktop.ViewModels;

namespace Desktop.App;

/// <summary>
/// Login do autoteste e da captura de telas (ADR-0026), que rodam sem ninguém digitando.
/// </summary>
/// <remarks>
/// <para>
/// Usa <c>EDGE_AUTOMACAO_LOGIN</c> e <c>EDGE_AUTOMACAO_SENHA</c> (padrão: o administrador que vem
/// instalado). No primeiro acesso, só troca a senha se <c>EDGE_AUTOMACAO_SENHA_NOVA</c> estiver
/// definida: assim o autoteste rodado numa máquina de evento nunca troca a senha de ninguém por conta
/// própria. O CI define as três; a senha nova é aleatória a cada execução.
/// </para>
/// </remarks>
internal static class LoginDeAutomacao
{
    /// <summary>Entra; chama <paramref name="antesDeTrocar"/> se cair no primeiro acesso, antes de trocar.</summary>
    internal static async Task EntrarAsync(SessaoDoUsuarioViewModel sessao, Func<Task>? antesDeTrocar = null)
    {
        ArgumentNullException.ThrowIfNull(sessao);
        await sessao.IniciarAsync().ConfigureAwait(true);
        if (sessao.Logado)
        {
            return;
        }

        var login = Environment.GetEnvironmentVariable("EDGE_AUTOMACAO_LOGIN") ?? SessaoDoUsuarioViewModel.LoginPadrao;
        var senha = Environment.GetEnvironmentVariable("EDGE_AUTOMACAO_SENHA") ?? "xacess";
        var nova = Environment.GetEnvironmentVariable("EDGE_AUTOMACAO_SENHA_NOVA");

        await TentarAsync(sessao, login, senha).ConfigureAwait(true);
        if (sessao.PedindoLogin && !string.IsNullOrEmpty(nova))
        {
            // Segunda rodada no mesmo serviço (o outro tema da captura): a senha já foi trocada.
            await TentarAsync(sessao, login, nova).ConfigureAwait(true);
        }

        if (sessao.PedindoTroca)
        {
            if (string.IsNullOrEmpty(nova))
            {
                throw new InvalidOperationException(
                    "Primeiro acesso: a automação só troca a senha com EDGE_AUTOMACAO_SENHA_NOVA definida.");
            }

            if (antesDeTrocar is not null)
            {
                await antesDeTrocar().ConfigureAwait(true);
            }

            sessao.Senha = senha;
            sessao.SenhaNova = nova;
            sessao.Confirmacao = nova;
            sessao.NovoLogin = login;
            sessao.NovoNome = "Automação do CI";
            await sessao.TrocarSenha.ExecutarAsync().ConfigureAwait(true);
        }

        if (!sessao.Logado)
        {
            throw new InvalidOperationException($"A automação não conseguiu entrar: {sessao.Mensagem}");
        }
    }

    private static async Task TentarAsync(SessaoDoUsuarioViewModel sessao, string login, string senha)
    {
        sessao.Login = login;
        sessao.Senha = senha;
        await sessao.Entrar.ExecutarAsync().ConfigureAwait(true);
    }
}
