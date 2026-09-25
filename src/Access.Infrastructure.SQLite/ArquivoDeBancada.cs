using System.Text.Json;
using System.Text.Json.Serialization;
using Access.Domain.Credentials;
using Access.Domain.Ticketing;

namespace Access.Infrastructure.SQLite;

/// <summary>O que uma carga de bancada fez.</summary>
/// <param name="Provedores">Provedores cadastrados.</param>
/// <param name="Ingressos">Ingressos ingeridos.</param>
/// <param name="Cartoes">Cartões vendidos no balcão.</param>
/// <param name="Problemas">O que não entrou, e por quê.</param>
public sealed record ResultadoDaCarga(int Provedores, int Ingressos, int Cartoes, IReadOnlyList<string> Problemas);

/// <summary>
/// Carrega provedores, ingressos e cartões de teste na base local, a partir de um arquivo.
/// </summary>
/// <remarks>
/// <para>
/// Existe porque o teste físico precisa de ingresso na base, e ainda não há tela para pôr
/// ingresso na base. É ferramenta de bancada, não de produção: em produção, os ingressos
/// chegam pela ingestão e os cartões pelo balcão.
/// </para>
/// <para>
/// Aplica as mesmas regras do sistema — o QR fora de 4 a 16 caracteres é recusado aqui
/// também. Um ingresso de teste que a catraca não consegue ler não testa nada, só confunde.
/// Ver docs/21-roteiro-da-bancada.md
/// </para>
/// </remarks>
public static class ArquivoDeBancada
{
    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Lê o arquivo e aplica na base. Pode ser repetido: tudo é idempotente, menos a venda de cartão já usado.</summary>
    public static ResultadoDaCarga Carregar(string json, RepositorioDeIngressos repositorio, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(repositorio);

        var arquivo = JsonSerializer.Deserialize<Arquivo>(json, Opcoes)
            ?? throw new FormatException("Arquivo de bancada vazio.");

        var problemas = new List<string>();

        foreach (var p in arquivo.Provedores)
        {
            repositorio.RegistrarProvedor(
                new ProvedorDeIngresso(
                    p.Id,
                    p.Nome ?? p.Id,
                    "raw",
                    // Sem conector na bancada, de propósito: o teste físico não pode sair
                    // avisando o Zet de que um ingresso de teste foi usado.
                    Conector: "",
                    Reutilizavel: p.Reutilizavel,
                    IntervaloDeReuso: TimeSpan.FromSeconds(p.IntervaloDeReusoSegundos),
                    SomenteNaUrna: p.SomenteNaUrna),
                agora);
        }

        var ingressos = new List<IngressoRecebido>();
        foreach (var i in arquivo.Ingressos)
        {
            var qr = PerfisDeLeitura.QrCatraca4.Apply(i.Qr);
            if (!PerfisDeLeitura.QrCatraca4.IsLengthAccepted(qr))
            {
                problemas.Add($"ingresso {i.Referencia}: QR com {qr.Length} caracteres — a catraca lê de 4 a 16.");
                continue;
            }

            ingressos.Add(new IngressoRecebido(
                i.Provedor, i.Referencia, i.Qr, qr, i.Setor,
                UsosMaximos: Math.Max(1, i.Usos), Categoria: i.Categoria));
        }

        var ingestao = repositorio.Ingerir(ingressos, agora);
        problemas.AddRange(ingestao.Colisoes.Select(c =>
            $"ingresso {c.ReferenciaNova}: QR {c.QrNormalizado} já pertence a {c.ProvedorExistente}/{c.ReferenciaExistente}."));

        if (ingestao.ProvedorDesconhecido > 0)
        {
            problemas.Add($"{ingestao.ProvedorDesconhecido} ingresso(s) de provedor não cadastrado no arquivo.");
        }

        var cartoes = 0;
        foreach (var c in arquivo.Cartoes)
        {
            var codigo = PerfisDeLeitura.MifareCatraca4.Apply(c.Codigo);
            if (!PerfisDeLeitura.MifareCatraca4.IsLengthAccepted(codigo))
            {
                problemas.Add($"cartão {c.Codigo}: não tem 10 dígitos depois de completar os zeros — a catraca entrega 10.");
                continue;
            }

            var venda = repositorio.VenderNoBalcao(c.Provedor, codigo, c.Categoria ?? "inteira", agora);
            if (venda is RepositorioDeIngressos.ResultadoDaVenda.Vendido)
            {
                cartoes++;
            }
            else
            {
                problemas.Add($"cartão {codigo}: {venda}.");
            }
        }

        return new ResultadoDaCarga(
            arquivo.Provedores.Count,
            ingestao.Inseridos + ingestao.Atualizados,
            cartoes,
            problemas);
    }

    private sealed class Arquivo
    {
        public List<ProvedorDoArquivo> Provedores { get; init; } = [];

        public List<IngressoDoArquivo> Ingressos { get; init; } = [];

        public List<CartaoDoArquivo> Cartoes { get; init; } = [];
    }

    private sealed class ProvedorDoArquivo
    {
        public required string Id { get; init; }

        public string? Nome { get; init; }

        public bool Reutilizavel { get; init; }

        [JsonPropertyName("intervaloDeReusoSegundos")]
        public int IntervaloDeReusoSegundos { get; init; }

        public bool SomenteNaUrna { get; init; }
    }

    private sealed class IngressoDoArquivo
    {
        public required string Provedor { get; init; }

        public required string Referencia { get; init; }

        public required string Qr { get; init; }

        public string? Setor { get; init; }

        public string? Categoria { get; init; }

        public int Usos { get; init; } = 1;
    }

    private sealed class CartaoDoArquivo
    {
        public required string Provedor { get; init; }

        public required string Codigo { get; init; }

        public string? Categoria { get; init; }
    }
}
