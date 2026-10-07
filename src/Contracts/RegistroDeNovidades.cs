using System.Globalization;

namespace Contracts;

/// <summary>
/// Guarda, por operador, qual edição das "Novidades desta versão" já foi mostrada, para o painel
/// só abrir o aviso quando a atualização tem algo novo a contar.
/// </summary>
/// <remarks>
/// Em <c>%LOCALAPPDATA%\ConexaoTopdata\novidades-vista</c>: o painel roda como o operador e sempre
/// pode gravar ali, ao contrário de <c>%ProgramData%</c>. É de propósito por usuário — cada pessoa
/// que usa o PC vê o aviso uma vez. O arquivo guarda só um número (a edição); nada sensível.
/// </remarks>
public static class RegistroDeNovidades
{
    public static string Arquivo { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ConexaoTopdata",
        "novidades-vista");

    /// <summary>A última edição vista, ou nulo se o operador nunca viu nenhuma (ou não deu para ler).</summary>
    public static int? EdicaoVista(string? arquivo = null)
    {
        var caminho = arquivo ?? Arquivo;

        try
        {
            if (!File.Exists(caminho))
            {
                return null;
            }

            var texto = File.ReadAllText(caminho).Trim();
            return int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var edicao)
                ? edicao
                : null;
        }
        catch (Exception falha) when (falha is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Grava a edição vista; engole falha de I/O (o aviso só reaparece da próxima vez).</summary>
    public static void Gravar(int edicao, string? arquivo = null)
    {
        var caminho = arquivo ?? Arquivo;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
            File.WriteAllText(caminho, edicao.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception falha) when (falha is IOException or UnauthorizedAccessException)
        {
            // Não deu para gravar: na próxima abertura o aviso aparece de novo. Melhor que quebrar.
        }
    }
}
