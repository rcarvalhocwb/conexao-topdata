namespace Desktop.ViewModels.GemeoDigital;

/// <summary>Pontos de vista prontos da câmera.</summary>
public enum VistaDaCamera
{
    /// <summary>Três quartos, pela frente e pelo lado dos braços.</summary>
    Inicial,

    /// <summary>De frente, como quem chega à catraca.</summary>
    Frente,

    /// <summary>De cima do painel, como quem olha o display.</summary>
    Painel,

    /// <summary>Do lado dos braços, ao longo da passagem.</summary>
    Bracos,

    /// <summary>Por trás, como quem sai.</summary>
    Tras,

    /// <summary>De cima.</summary>
    Cima,
}

/// <summary>Uma linha da narração do cenário.</summary>
/// <param name="Quando">"1,4 s".</param>
/// <param name="Texto">A frase.</param>
/// <param name="Atual">É o passo de agora.</param>
/// <param name="Aconteceu">Já passou (inclui o atual).</param>
public sealed record LinhaDaNarracao(string Quando, string Texto, bool Atual, bool Aconteceu);
