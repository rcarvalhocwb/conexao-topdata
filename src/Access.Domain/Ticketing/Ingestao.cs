namespace Access.Domain.Ticketing;

/// <summary>Um QR que dois provedores diferentes reivindicam.</summary>
/// <param name="QrNormalizado">O valor em conflito.</param>
/// <param name="ProvedorExistente">Quem já tinha o QR.</param>
/// <param name="ReferenciaExistente">Referência do ingresso já gravado.</param>
/// <param name="ProvedorNovo">Quem tentou gravar por cima.</param>
/// <param name="ReferenciaNova">Referência do ingresso recusado.</param>
public sealed record ColisaoDeQr(
    string QrNormalizado,
    string ProvedorExistente,
    string ReferenciaExistente,
    string ProvedorNovo,
    string ReferenciaNova);

/// <summary>O que uma ingestão fez.</summary>
/// <param name="Inseridos">Ingressos novos.</param>
/// <param name="Atualizados">Ingressos que já existiam e foram reescritos.</param>
/// <param name="Colisoes">Recusados por conflito de QR com outro provedor.</param>
/// <param name="ProvedorDesconhecido">Recusados porque o provedor não está cadastrado.</param>
public sealed record ResultadoDaIngestao(
    int Inseridos,
    int Atualizados,
    IReadOnlyList<ColisaoDeQr> Colisoes,
    int ProvedorDesconhecido)
{
    /// <summary>Nada entrou nem foi alterado.</summary>
    public bool Vazia => Inseridos == 0 && Atualizados == 0;
}

