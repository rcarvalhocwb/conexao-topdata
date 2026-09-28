namespace Contracts;

/// <summary>
/// Onde os aplicativos de tela gravam um erro inesperado antes de avisar o operador.
/// </summary>
/// <remarks>
/// Em <c>%LOCALAPPDATA%\ConexaoTopdata\erros</c>, que o usuário sempre pode gravar: o
/// painel roda como o operador, sem permissão em <c>%ProgramData%</c>. O texto é o da
/// exceção — nenhum aplicativo de tela tem número de cartão para vazar (o contrato não
/// tem esse campo).
/// </remarks>
public static class RegistroDeFalhas
{
    public static string Pasta { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ConexaoTopdata",
        "erros");

    /// <summary>Grava e devolve o caminho do arquivo; nulo se nem gravar foi possível.</summary>
    public static string? Gravar(string aplicativo, Exception erro)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aplicativo);
        ArgumentNullException.ThrowIfNull(erro);

        try
        {
            Directory.CreateDirectory(Pasta);
            var arquivo = Path.Combine(Pasta, $"{aplicativo}-{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(arquivo, $"{DateTime.Now:HH:mm:ss} {erro}{Environment.NewLine}{Environment.NewLine}");
            return arquivo;
        }
        catch (Exception falha) when (falha is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
