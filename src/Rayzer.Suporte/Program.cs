using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Rayzer.Suporte;

// Ferramenta do suporte Rayzer (docs/58 e docs/60). Uso:
//   Rayzer.Suporte                          menu (dois cliques no .exe)
//   Rayzer.Suporte gerar-chaves [pasta]     cria suporte-rayzer-privada.pem e suporte-rayzer-publica.txt
//   Rayzer.Suporte assinar [arquivo.pem]    pede o código do cliente e mostra a resposta
//   Rayzer.Suporte emitir-licenca [arquivo.pem]   emite uma licença (docs/60 §1)
//   Rayzer.Suporte gerar-chave-de-atualizacao [pasta]   a chave que assina as versões estáveis (docs/60 §2.5)
//   Rayzer.Suporte assinar-arquivo <arquivo>   (CI) assina com CHAVE_DE_ATUALIZACAO e SENHA_DA_CHAVE_DE_ATUALIZACAO
//   Rayzer.Suporte publica-do-segredo          (CI) mostra a chave pública do segredo de atualização
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
    case "emitir-licenca":
        return EmitirLicenca(args.Length > 1 ? args[1] : chavePadrao);
    case "gerar-chave-de-atualizacao":
        return GerarChaves(args.Length > 1 ? args[1] : pastaPadrao, chaveDeAtualizacao: true);
    case "assinar-arquivo" when args.Length > 1:
        return AssinarArquivo(args[1]);
    case "publica-do-segredo":
        return PublicaDoSegredo();
    case null:
        return Menu(pastaPadrao, chavePadrao);
    default:
        Console.WriteLine("Rayzer.Suporte — recuperação de senha de administrador (docs/58)");
        Console.WriteLine("  (sem nada)               menu");
        Console.WriteLine("  gerar-chaves [pasta]     cria a chave privada (protegida por senha) e a pública");
        Console.WriteLine("  assinar [arquivo.pem]    assina o código de pedido que o cliente ditar");
        Console.WriteLine("  emitir-licenca [arquivo.pem]   emite uma licença do XAcess");
        Console.WriteLine("  gerar-chave-de-atualizacao [pasta]   a chave que assina as versões estáveis");
        return 1;
}

static int Menu(string pasta, string chave)
{
    Console.WriteLine("Rayzer.Suporte — chaves, licenças e recuperação de senha do XAcess");
    Console.WriteLine();
    Console.WriteLine("Digite o número da opção e tecle Enter. Ao digitar uma senha, aparece um * por letra.");
    Console.WriteLine();
    Console.WriteLine(File.Exists(chave)
        ? $"Chave privada encontrada: {chave}"
        : "Ainda não há chave privada neste computador.");
    Console.WriteLine();
    Console.WriteLine("  1  Gerar as chaves do suporte (uma vez só)");
    Console.WriteLine("  2  Responder a um pedido de recuperação de senha");
    Console.WriteLine("  3  Emitir uma licença");
    Console.WriteLine("  4  Gerar a chave de atualização (uma vez só, vai para o GitHub)");
    Console.WriteLine("  0  Sair");
    Console.Write("Escolha: ");

    var escolha = (Console.ReadLine() ?? string.Empty).Trim();
    var codigo = escolha switch
    {
        "1" => GerarChaves(pasta),
        "2" => Assinar(chave),
        "3" => EmitirLicenca(chave),
        "4" => GerarChaves(pasta, chaveDeAtualizacao: true),
        _ => 0,
    };

    if (codigo == 0 && escolha is "1" or "3" or "4" && OperatingSystem.IsWindows() && !Console.IsInputRedirected)
    {
        // Mostra os arquivos criados: o .txt público é o que vai por e-mail ou mensagem.
        try
        {
            using var _ = System.Diagnostics.Process.Start("explorer.exe", $"\"{pasta}\"");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Sem o Explorer: o caminho já está escrito na tela.
        }
    }

    if (!Console.IsInputRedirected)
    {
        // Aberto com dois cliques, a janela fecharia antes de dar para ler (e copiar) a resposta.
        Console.WriteLine();
        Console.Write("Tecle Enter para fechar.");
        Console.ReadLine();
    }

    return codigo;
}

static int GerarChaves(string pasta, bool chaveDeAtualizacao = false)
{
    var prefixo = chaveDeAtualizacao ? "atualizacao" : "suporte-rayzer";
    var arquivo = Path.Combine(pasta, $"{prefixo}-privada.pem");
    if (File.Exists(arquivo))
    {
        Console.Error.WriteLine($"Já existe {arquivo}. Não sobrescrevo uma chave privada.");
        return 2;
    }

    Console.WriteLine("Crie uma senha para proteger a chave (12 letras ou mais; anote num lugar seguro).");
    var senha = LerSenha("Senha: ");
    if (senha.Length < 12)
    {
        Console.Error.WriteLine($"A senha tem {senha.Length} letra(s); precisa de 12 ou mais. Nada foi criado: escolha a opção de novo.");
        return 2;
    }

    if (senha != LerSenha("Repita a mesma senha: "))
    {
        Console.Error.WriteLine("As duas senhas ficaram diferentes. Nada foi criado: escolha a opção de novo.");
        return 2;
    }

    var (privada, publica) = FerramentaDoSuporte.GerarChaves(senha);
    Directory.CreateDirectory(pasta);
    File.WriteAllText(arquivo, privada);
    var arquivoPublico = Path.Combine(pasta, $"{prefixo}-publica.txt");
    File.WriteAllText(arquivoPublico, publica + Environment.NewLine);
    Console.WriteLine();
    Console.WriteLine($"Chave PRIVADA: {arquivo}");
    if (chaveDeAtualizacao)
    {
        Console.WriteLine("  Vai para os segredos do repositório no GitHub (Settings > Secrets and variables > Actions):");
        Console.WriteLine("    CHAVE_DE_ATUALIZACAO = o conteúdo inteiro deste arquivo .pem");
        Console.WriteLine("    SENHA_DA_CHAVE_DE_ATUALIZACAO = a senha que você acabou de digitar");
        Console.WriteLine("  Depois disso, guarde o .pem num pen drive e não mande por e-mail nem mensagem.");
    }
    else
    {
        Console.WriteLine("  NÃO mande este arquivo a ninguém. Faça uma cópia de segurança (pen drive guardado).");
        Console.WriteLine("  Quem tiver o arquivo e a senha recupera administradores e emite licenças.");
    }

    Console.WriteLine();
    Console.WriteLine($"Chave PÚBLICA: {arquivoPublico}");
    Console.WriteLine("  Esta pode ser enviada: mande o arquivo .txt (ou o texto abaixo) para quem mantém o código.");
    Console.WriteLine(publica);
    return 0;
}

static int EmitirLicenca(string arquivo)
{
    if (!File.Exists(arquivo))
    {
        Console.Error.WriteLine($"Não achei {arquivo}. A licença é assinada com a chave do suporte (opção 1).");
        return 2;
    }

    Console.Write("Cliente (aparece no painel): ");
    var cliente = (Console.ReadLine() ?? string.Empty).Trim();
    Console.Write("Identificador da instalação (tela Diagnóstico; * para qualquer computador): ");
    var instalacao = (Console.ReadLine() ?? string.Empty).Trim();
    Console.Write("Vale até (dd/mm/aaaa): ");
    var data = (Console.ReadLine() ?? string.Empty).Trim();

    if (cliente.Length == 0 || instalacao.Length == 0
        || !DateOnly.TryParseExact(data, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var vence))
    {
        Console.Error.WriteLine("Faltou o cliente, a instalação, ou a data não está no formato dd/mm/aaaa. Nada foi emitido.");
        return 2;
    }

    var senha = LerSenha("Senha da chave privada: ");
    try
    {
        var licenca = FerramentaDoSuporte.EmitirLicenca(File.ReadAllText(arquivo), senha, cliente, instalacao, vence, DateTimeOffset.UtcNow);
        var pasta = Path.GetDirectoryName(Path.GetFullPath(arquivo))!;
        var nome = string.Concat(cliente.Split(Path.GetInvalidFileNameChars())).Replace(' ', '-');
        var saida = Path.Combine(pasta, $"licenca-{nome}-{vence:yyyy-MM-dd}.txt");
        File.WriteAllText(saida, licenca + Environment.NewLine);
        Console.WriteLine();
        Console.WriteLine($"Licença de {cliente}, válida até {vence:dd/MM/yyyy}. Mande o texto abaixo (ou o arquivo {saida}):");
        Console.WriteLine("No XAcess: Diagnóstico > Licença > colar > Instalar licença.");
        Console.WriteLine();
        Console.WriteLine(licenca);
        return 0;
    }
    catch (CryptographicException)
    {
        Console.Error.WriteLine("Senha errada ou arquivo de chave inválido.");
        return 2;
    }
}

// Para o CI (workflow "Promover versão estável"): a chave e a senha vêm dos segredos do repositório.
static int AssinarArquivo(string arquivo)
{
    var (pem, senha) = (Environment.GetEnvironmentVariable("CHAVE_DE_ATUALIZACAO"), Environment.GetEnvironmentVariable("SENHA_DA_CHAVE_DE_ATUALIZACAO"));
    if (string.IsNullOrWhiteSpace(pem) || senha is null || !File.Exists(arquivo))
    {
        Console.Error.WriteLine("Faltam os segredos CHAVE_DE_ATUALIZACAO e SENHA_DA_CHAVE_DE_ATUALIZACAO, ou o arquivo.");
        return 2;
    }

    try
    {
        File.WriteAllText(arquivo + ".sig", FerramentaDoSuporte.AssinarArquivo(pem, senha, File.ReadAllBytes(arquivo)));
        Console.WriteLine($"Assinado: {arquivo}.sig");
        return 0;
    }
    catch (CryptographicException)
    {
        Console.Error.WriteLine("A chave ou a senha de atualização não abrem.");
        return 2;
    }
}

static int PublicaDoSegredo()
{
    var (pem, senha) = (Environment.GetEnvironmentVariable("CHAVE_DE_ATUALIZACAO"), Environment.GetEnvironmentVariable("SENHA_DA_CHAVE_DE_ATUALIZACAO"));
    if (string.IsNullOrWhiteSpace(pem) || senha is null)
    {
        Console.Error.WriteLine("Faltam os segredos CHAVE_DE_ATUALIZACAO e SENHA_DA_CHAVE_DE_ATUALIZACAO.");
        return 2;
    }

    try
    {
        Console.WriteLine(FerramentaDoSuporte.PublicaDe(pem, senha));
        return 0;
    }
    catch (CryptographicException)
    {
        Console.Error.WriteLine("A chave ou a senha de atualização não abrem.");
        return 2;
    }
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
        // Um asterisco por letra: sem eco nenhum, quem digita acha que a janela travou.
        if (tecla.Key == ConsoleKey.Backspace && senha.Length > 0)
        {
            senha.Length--;
            Console.Write("\b \b");
        }
        else if (!char.IsControl(tecla.KeyChar))
        {
            senha.Append(tecla.KeyChar);
            Console.Write('*');
        }
    }

    Console.WriteLine();
    return senha.ToString();
}
