"""Gera o fundo do Setup.exe (installer/wix/fundo-setup.png) a partir da arte da marca.

A janela do Setup tem 820 x 461 (a proporção 16:9 da arte). O fundo é pintado pela
própria janela (Theme/@ImageFile + Window/@SourceX/SourceY no tema), sempre por baixo dos
textos e botões. O WiX exige a imagem estritamente MAIOR que o recorte, por isso 822 x 463
(o recorte usa 0,0–820,461). Em telas com zoom, o Setup estica o recorte.

Não use ImageControl de fundo: ele é um controle como os outros e fica por cima dos
botões (foi o que escondeu o botão Instalar na 0.1.77/0.1.78).

Os textos e os botões do Setup ficam na faixa da direita (tema-rayzer.xml, X = 470). Por
isso a arte ganha ali uma faixa escura, na cor de fundo da marca, que começa transparente
e escurece: a marca, o slogan e as catracas da esquerda ficam intactos, e o texto branco
fica legível por cima.

Uso: python tools/gerar-fundo-do-setup.py
"""

from pathlib import Path

from PIL import Image, ImageDraw

RAIZ = Path(__file__).resolve().parent.parent
ARTE = RAIZ / "marca" / "arte-rayzer-x.jpg"
SAIDA = RAIZ / "installer" / "wix" / "fundo-setup.png"

LARGURA, ALTURA = 822, 463   # a janela (820 x 461) + 2 px: o WiX exige imagem maior que o recorte
INICIO_DA_FAIXA = 0.54       # onde a faixa fica opaca (fração da largura) — X = 443
DEGRADE = 0.10               # largura do degradê antes da faixa
COR_DA_FAIXA = (8, 16, 34)   # próximo de #0B1A33, o fundo da marca
OPACIDADE = 228              # de 255: a arte ainda aparece levemente por trás
AZUL_DA_MARCA = (47, 128, 255)


def gerar() -> None:
    arte = Image.open(ARTE).convert("RGB").resize((LARGURA, ALTURA), Image.LANCZOS).convert("RGBA")

    faixa = Image.new("RGBA", (LARGURA, ALTURA), (0, 0, 0, 0))
    pincel = ImageDraw.Draw(faixa)
    x_faixa = int(LARGURA * INICIO_DA_FAIXA)
    x_degrade = x_faixa - int(LARGURA * DEGRADE)

    for x in range(x_degrade, LARGURA):
        t = min(1.0, (x - x_degrade) / (x_faixa - x_degrade))
        pincel.line([(x, 0), (x, ALTURA)], fill=COR_DA_FAIXA + (int(OPACIDADE * t),))

    arte.alpha_composite(faixa)

    # Um fio azul da marca na borda da faixa.
    ImageDraw.Draw(arte).line(
        [(x_faixa, int(ALTURA * 0.06)), (x_faixa, int(ALTURA * 0.94))],
        fill=AZUL_DA_MARCA + (160,),
        width=1,
    )

    arte.convert("RGB").save(SAIDA, optimize=True)
    print(f"{SAIDA.relative_to(RAIZ)}: {SAIDA.stat().st_size // 1024} KB")


if __name__ == "__main__":
    gerar()
