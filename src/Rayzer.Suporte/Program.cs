using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Rayzer.Suporte;

// Ferramenta do suporte Rayzer (docs/58). Uso:
//   Rayzer.Suporte gerar-chaves [pasta]     cria suporte-rayzer-privada.pem e mostra a chave pública
//   Rayzer.Suporte assinar <arquivo.pem>     pede o código do cliente e mostra a resposta
Console.OutputEncoding = Encoding.UTF8;

switch (args.FirstOrDefault())
{
    case "gerar-chaves":
        return GerarChaves(args.Length > 1 ? args[1] : Directory.GetCurrentDirectory());
    case "assinar" when args.Length > 1:
        return Assinar(args[1]);
    default:
        Console.WriteLine("Rayzer.Suporte — recuperação de senha de administrador (docs/58)");
        Console.WriteLine("  gerar-chaves [pasta]   cria a chave privada (protegida por senha) e mostra a pública");
        Console.WriteLine("  assinar <arquivo.pem>  assina o código de pedido que o cliente ditar");
        return 1;
}

static int GerarChaves(string pasta)
{
    var arquivo = Path.Combine(pasta, "suporte-rayzer-privada.pem");
    if (File.Exists(arquivo))
    {
        Console.Error.WriteLine($"Já existe {arquivo}. Não sobrescrevo uma chave privada.");
        return 2;
    }

    var senha = LerSenha("Senha para proteger a chave privada (mínimo 12): ");
    if (senha.Length < 12 || senha != LerSenha("Repita a senha: "))
    {
        Console.Error.WriteLine("Senhas diferentes ou curtas demais. Nada foi criado.");
        return 2;
    }

    var (privada, publica) = FerramentaDoSuporte.GerarChaves(senha);
    File.WriteAllText(arquivo, privada);
    Console.WriteLine();
    Console.WriteLine($"Chave privada: {arquivo}");
    Console.WriteLine("Guarde-a fora do repositório, com cópia de segurança. Quem tiver o arquivo e a senha recupera administradores.");
    Console.WriteLine();
    Console.WriteLine("Chave PÚBLICA (pode ir para o repositório, em src/Access.Infrastructure.SQLite/ChavesDoSuporte.cs):");
    Console.WriteLine(publica);
    return 0;
}

static int Assinar(string arquivo)
{
    if (!File.Exists(arquivo))
    {
        Console.Error.WriteLine($"Não achei {arquivo}.");
        return 2;
    }

    Console.Write("Código de pedido que o cliente ditou: ");
    var pedido = Console.ReadLine() ?? string.Empty;
    var senha = LerSenha("Senha da chave privada: ");

    try
    {
        if (FerramentaDoSuporte.Assinar(File.ReadAllText(arquivo), senha, pedido) is not { } resultado)
        {
            Console.Error.WriteLine("Esse código não é um pedido de recuperação válido. Confira com o cliente.");
            return 2;
        }

        if (resultado.ValidoAte < DateTimeOffset.UtcNow)
        {
            Console.Error.WriteLine("Esse pedido já venceu. Peça ao cliente um código novo.");
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Pedido válido até {resultado.ValidoAte.ToLocalTime():dd/MM/yyyy HH:mm}."));
        Console.WriteLine("Resposta para o cliente colar na tela de login:");
        Console.WriteLine(resultado.Resposta);
        return 0;
    }
    catch (CryptographicException)
    {
        Console.Error.WriteLine("Senha errada ou arquivo de chave inválido.");
        return 2;
    }
}

static string LerSenha(string pergunta)
{
    Console.Write(pergunta);
    if (Console.IsInputRedirected)
    {
        return Console.ReadLine() ?? string.Empty;
    }

    var senha = new StringBuilder();
    for (var tecla = Console.ReadKey(intercept: true); tecla.Key != ConsoleKey.Enter; tecla = Console.ReadKey(intercept: true))
    {
        if (tecla.Key == ConsoleKey.Backspace && senha.Length > 0)
        {
            senha.Length--;
        }
        else if (!char.IsControl(tecla.KeyChar))
        {
            senha.Append(tecla.KeyChar);
        }
    }

    Console.WriteLine();
    return senha.ToString();
}
