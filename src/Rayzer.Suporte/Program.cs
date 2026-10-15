using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Rayzer.Suporte;

// Ferramenta do suporte Rayzer (docs/58). Uso:
//   Rayzer.Suporte                          menu (dois cliques no .exe): gerar as chaves ou assinar
//   Rayzer.Suporte gerar-chaves [pasta]     cria suporte-rayzer-privada.pem e suporte-rayzer-publica.txt
//   Rayzer.Suporte assinar [arquivo.pem]    pede o código do cliente e mostra a resposta
Console.OutputEncoding = Encoding.UTF8;

// A pasta padrão é Documentos: quem abre com dois cliques não escolhe pasta.
// Nunca a pasta atual: aberta de dentro de um clone do repositório, a chave privada cairia nele.
var documentos = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
var pastaPadrao = Path.Combine(
    string.IsNullOrEmpty(documentos) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : documentos,
    "Rayzer Suporte");
var chavePadrao = Path.Combine(pastaPadrao, "suporte-rayzer-privada.pem");

switch (args.FirstOrDefault())
{
    case "gerar-chaves":
        return GerarChaves(args.Length > 1 ? args[1] : pastaPadrao);
    case "assinar":
        return Assinar(args.Length > 1 ? args[1] : chavePadrao);
    case null:
        return Menu(pastaPadrao, chavePadrao);
    default:
        Console.WriteLine("Rayzer.Suporte — recuperação de senha de administrador (docs/58)");
        Console.WriteLine("  (sem nada)               menu");
        Console.WriteLine("  gerar-chaves [pasta]     cria a chave privada (protegida por senha) e a pública");
        Console.WriteLine("  assinar [arquivo.pem]    assina o código de pedido que o cliente ditar");
        return 1;
}

static int Menu(string pasta, string chave)
{
    Console.WriteLine("Rayzer.Suporte — recuperação de senha de administrador do XAcess");
    Console.WriteLine();
    Console.WriteLine(File.Exists(chave)
        ? $"Chave privada encontrada: {chave}"
        : "Ainda não há chave privada neste computador.");
    Console.WriteLine();
    Console.WriteLine("  1  Gerar as chaves (uma vez só)");
    Console.WriteLine("  2  Responder a um pedido de recuperação");
    Console.WriteLine("  0  Sair");
    Console.Write("Escolha: ");

    var codigo = (Console.ReadLine() ?? string.Empty).Trim() switch
    {
        "1" => GerarChaves(pasta),
        "2" => Assinar(chave),
        _ => 0,
    };

    if (!Console.IsInputRedirected)
    {
        // Aberto com dois cliques, a janela fecharia antes de dar para ler (e copiar) a resposta.
        Console.WriteLine();
        Console.Write("Tecle Enter para fechar.");
        Console.ReadLine();
    }

    return codigo;
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
    Directory.CreateDirectory(pasta);
    File.WriteAllText(arquivo, privada);
    var arquivoPublico = Path.Combine(pasta, "suporte-rayzer-publica.txt");
    File.WriteAllText(arquivoPublico, publica + Environment.NewLine);
    Console.WriteLine();
    Console.WriteLine($"Chave PRIVADA: {arquivo}");
    Console.WriteLine("  NÃO mande este arquivo a ninguém. Faça uma cópia de segurança (pen drive guardado).");
    Console.WriteLine("  Quem tiver o arquivo e a senha recupera administradores.");
    Console.WriteLine();
    Console.WriteLine($"Chave PÚBLICA: {arquivoPublico}");
    Console.WriteLine("  Esta pode ser enviada: mande o arquivo .txt (ou o texto abaixo) para quem mantém o código.");
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
