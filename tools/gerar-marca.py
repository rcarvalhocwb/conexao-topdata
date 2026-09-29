#!/usr/bin/env python3
"""
Gera a marca Rayzer a partir do brand board: letreiros em vetor (Sora, com kerning), os
lockups da marca corporativa (RAYZER X) e do produto (XAcess), e os destinos que dependem
deles — uma única fonte de verdade para o desktop, a web e os arquivos de marca.

  src/Rayzer.Design/Tokens.xaml                 geometrias Rayzer.Letreiro.* (entre marcadores)
  src/Rayzer.Design/Themes/Generic.xaml         modelos RayzerLogo e XAcessLogo (entre marcadores)
  web/rayzer-ui/src/components/brand/letreiros.ts   caminhos e lockups para o React
  marca/svg/*.svg                               arquivos vetoriais (escuro, claro, monocromático)

Requer: pip install fonttools uharfbuzz. Fontes em src/Rayzer.Design/Fontes (OFL).
Os PNG, WEBP e ICO saem de tools/gerar-marca-imagens.mjs (renderização no Chromium).
"""
from __future__ import annotations

import json
import re
from pathlib import Path

import uharfbuzz as hb
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

RAIZ = Path(__file__).resolve().parent.parent
FONTES = RAIZ / "src/Rayzer.Design/Fontes"

# Altura de maiúscula da Sora com corpo 100 (sCapHeight 730/1000).
C = 73.0

# ---------------------------------------------------------------- letreiros

LETREIROS = {
    # nome: (arquivo, texto, espaçamento em unidades por 1000)
    "Rayzer": ("Sora-ExtraBold", "RAYZER", 30),
    "Acess": ("Sora-Bold", "acess", -10),
    "Descritor": ("Sora-Regular", "SERVIÇOS E TECNOLOGIA LTDA", 280),
    "Tagline": ("Sora-Regular", "CONTROLE DE ACESSO INTELIGENTE", 280),
    "By": ("Sora-Regular", "by", 0),
}


def letreiro(arquivo: str, texto: str, espaco: int) -> tuple[str, float]:
    """Caminho SVG do texto com corpo 100: x a partir de 0, y = 0 no alto da maiúscula."""
    caminho = FONTES / f"{arquivo}.ttf"
    fonte = TTFont(caminho)
    dados = caminho.read_bytes()
    face = hb.Face(dados)
    hbf = hb.Font(face)
    buf = hb.Buffer()
    buf.add_str(texto)
    buf.guess_segment_properties()
    hb.shape(hbf, buf, {"kern": True, "liga": False})

    glifos = fonte.getGlyphSet()
    ordem = fonte.getGlyphOrder()
    escala = 100 / fonte["head"].unitsPerEm
    pen = SVGPathPen(glifos, ntos=lambda v: f"{v:.2f}".rstrip("0").rstrip("."))
    x = 0.0
    n = len(buf.glyph_infos)
    for i, (info, pos) in enumerate(zip(buf.glyph_infos, buf.glyph_positions)):
        nome = ordem[info.codepoint]
        # y da fonte cresce para cima; no SVG, para baixo. Linha de base em y = C.
        t = TransformPen(pen, (escala, 0, 0, -escala, x + pos.x_offset * escala, C - pos.y_offset * escala))
        glifos[nome].draw(t)
        x += pos.x_advance * escala
        if i < n - 1:
            x += espaco * escala
    return pen.getCommands(), round(x, 2)


GEO = {nome: letreiro(*spec) for nome, spec in LETREIROS.items()}
LARGURA = {nome: w for nome, (_, w) in GEO.items()}

# ---------------------------------------------------------------- lockups

# O X ocupa, na caixa de 32, x 3..29 e y 4..28 (26 × 24).
def marca(esq_visivel: float, topo_visivel: float, altura_visivel: float) -> dict:
    s = altura_visivel / 24
    return {"tipo": "marca", "x": round(esq_visivel - 3 * s, 2), "y": round(topo_visivel - 4 * s, 2), "lado": round(32 * s, 2)}


def texto(nome: str, x: float, y: float, k: float, papel: str) -> dict:
    return {"tipo": "texto", "nome": nome, "x": round(x, 2), "y": round(y, 2), "k": round(k, 4), "papel": papel}


def fechar(itens: list[dict], caixas: list[tuple[float, float, float, float]]) -> dict:
    """Desloca tudo para começar em (0,0) e calcula a caixa."""
    x0 = min(c[0] for c in caixas); y0 = min(c[1] for c in caixas)
    x1 = max(c[2] for c in caixas); y1 = max(c[3] for c in caixas)
    for it in itens:
        it["x"] = round(it["x"] - x0, 2); it["y"] = round(it["y"] - y0, 2)
    return {"w": round(x1 - x0, 2), "h": round(y1 - y0, 2), "itens": itens}


def corp_horizontal(descritor: bool) -> dict:
    wr = LARGURA["Rayzer"]
    hx = 2.0 * C
    cy = C / 2 + 0.12 * C
    xt = cy - hx / 2
    xl = wr + 0.16 * C
    xw = hx * 26 / 24
    itens = [texto("Rayzer", 0, 0, 1, "texto"), marca(xl, xt, hx)]
    caixas = [(0, 0, wr, C), (xl, xt, xl + xw, xt + hx)]
    if descritor:
        k = wr / LARGURA["Descritor"]
        top = C + 0.34 * C
        itens.append(texto("Descritor", 0, top, k, "apoio"))
        caixas.append((0, top, wr, top + C * k))
    return fechar(itens, caixas)


def corp_vertical() -> dict:
    wr = LARGURA["Rayzer"]
    hx = 2.2 * C
    xw = hx * 26 / 24
    xl = (wr - xw) / 2
    xt = -0.3 * C - hx
    k = wr / LARGURA["Descritor"]
    top = C + 0.34 * C
    itens = [marca(xl, xt, hx), texto("Rayzer", 0, 0, 1, "texto"), texto("Descritor", 0, top, k, "apoio")]
    caixas = [(xl, xt, xl + xw, xt + hx), (0, 0, wr, C), (0, top, wr, top + C * k)]
    return fechar(itens, caixas)


def produto(extra: str | None) -> dict:
    hx = 1.12 * C
    xw = hx * 26 / 24
    base = C + 0.02 * C
    xt = base - hx
    xa = xw + 0.03 * C
    wa = LARGURA["Acess"]
    fim = xa + wa
    itens = [marca(0, xt, hx), texto("Acess", xa, 0, 1, "texto")]
    caixas = [(0, xt, xw, base), (xa, C - 53.4, fim, C)]
    if extra == "tagline":
        k = fim / LARGURA["Tagline"]
        top = C + 0.46 * C
        itens.append(texto("Tagline", 0, top, k, "apoio"))
        caixas.append((0, top, fim, top + C * k))
    elif extra == "assinatura":
        k = 0.2
        hxm = 1.5 * C * k
        xwm = hxm * 26 / 24
        wr = LARGURA["Rayzer"] * k
        wb = LARGURA["By"] * k
        top = C + 0.34 * C
        x_mark = fim - xwm
        x_r = x_mark - 0.1 * C - wr
        x_b = x_r - 0.14 * C - wb
        base_m = top + C * k + 0.12 * C * k
        itens += [
            texto("By", x_b, top, k, "apoio"),
            texto("Rayzer", x_r, top, k, "texto"),
            marca(x_mark, base_m - hxm, hxm),
        ]
        caixas.append((x_b, base_m - hxm, fim, base_m))
    return fechar(itens, caixas)


LOCKUPS = {
    # Marca corporativa — Rayzer Serviços e Tecnologia LTDA
    "CorpHorizontal": corp_horizontal(True),
    "CorpHorizontalSimples": corp_horizontal(False),
    "CorpVertical": corp_vertical(),
    # Produto — XAcess
    "ProdutoPrincipal": produto("tagline"),
    "ProdutoAssinatura": produto("assinatura"),
    "ProdutoCompacto": produto(None),
}

# ---------------------------------------------------------------- SVG

GRADIENTES = """
    <linearGradient id="fu" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#4D94FF"/><stop offset="1" stop-color="#0066FF"/></linearGradient>
    <linearGradient id="fl" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#0066FF"/><stop offset="1" stop-color="#003FBF"/></linearGradient>
    <linearGradient id="gu" x1="1" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#8AF0FF"/><stop offset="0.55" stop-color="#00D5FF"/><stop offset="1" stop-color="#009FE0"/></linearGradient>
    <linearGradient id="gl" x1="1" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#F4F7FB"/><stop offset="1" stop-color="#8A9BB0"/></linearGradient>
    <linearGradient id="hl" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFFFFF" stop-opacity="0.4"/><stop offset="0.45" stop-color="#FFFFFF" stop-opacity="0"/></linearGradient>"""

X_PARTES = {
    "gu": "M21,4 L29,4 L20.75,15 L16.75,9.67 Z",
    "gl": "M11,28 L3,28 L11.25,17 L15.25,22.33 Z",
    "fu": "M3,4 L11,4 L20,16 L12,16 Z",
    "fl": "M12,16 L20,16 L29,28 L21,28 Z",
}

MODOS = {
    # sufixo: (texto, apoio, marca monocromática ou None)
    "escuro": ("#F4F7FB", "#AEBBCD", None),
    "claro": ("#0B1A33", "#4A5B75", None),
    "mono-branco": ("#FFFFFF", "#FFFFFF", "#FFFFFF"),
    "mono-azul-escuro": ("#0B1A33", "#0B1A33", "#0B1A33"),
}


def svg_x(mono: str | None) -> str:
    if mono:
        return "".join(f'<path d="{d}" fill="{mono}"/>' for d in X_PARTES.values())
    partes = "".join(f'<path d="{X_PARTES[k]}" fill="url(#{k})"/>' for k in ("gu", "gl", "fu", "fl"))
    return partes + f'<path d="{X_PARTES["fu"]}" fill="url(#hl)"/><path d="{X_PARTES["gu"]}" fill="url(#hl)"/>'


def svg_lockup(l: dict, modo: str, titulo: str) -> str:
    cor, apoio, mono = MODOS[modo]
    corpo = []
    for it in l["itens"]:
        if it["tipo"] == "texto":
            fill = cor if it["papel"] == "texto" else apoio
            corpo.append(f'<path transform="translate({it["x"]} {it["y"]}) scale({it["k"]})" fill="{fill}" d="{GEO[it["nome"]][0]}"/>')
        else:
            s = it["lado"] / 32
            corpo.append(f'<g transform="translate({it["x"]} {it["y"]}) scale({round(s, 4)})">{svg_x(mono)}</g>')
    defs = "" if mono else f"<defs>{GRADIENTES}\n  </defs>"
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {l["w"]} {l["h"]}" role="img" aria-label="{titulo}">'
            f"<title>{titulo}</title>{defs}{''.join(corpo)}</svg>\n")


def svg_simbolo(mono: str | None) -> str:
    defs = "" if mono else f"<defs>{GRADIENTES}\n  </defs>"
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="3 4 26 24" role="img" aria-label="Rayzer X"><title>Rayzer X</title>{defs}{svg_x(mono)}</svg>\n'


def svg_icone(com_nome: bool) -> str:
    """Ícone em quadro arredondado: fundo azul profundo, borda azul e o X (produto: + acess)."""
    lado = 256
    moldura = (f'<defs>{GRADIENTES}\n    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#13264A"/><stop offset="1" stop-color="#0B1A33"/></linearGradient>'
               f'\n    <linearGradient id="bd" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#3D8BFF"/><stop offset="1" stop-color="#0066FF"/></linearGradient>\n  </defs>'
               f'<rect x="8" y="8" width="240" height="240" rx="56" fill="url(#bg)" stroke="url(#bd)" stroke-width="6"/>')
    if com_nome:
        hx = 104; s = hx / 24
        x = (lado - 26 * s) / 2 - 3 * s; y = 44 - 4 * s
        k = 150 / LARGURA["Acess"]
        ta = f'<path transform="translate({round((lado - 150) / 2, 2)} {round(170 - 19.6 * k, 2)}) scale({round(k, 4)})" fill="#F4F7FB" d="{GEO["Acess"][0]}"/>'
    else:
        hx = 132; s = hx / 24
        x = (lado - 26 * s) / 2 - 3 * s; y = (lado - hx) / 2 - 4 * s
        ta = ""
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {lado} {lado}" role="img" aria-label="{"XAcess" if com_nome else "Rayzer X"}">{moldura}'
            f'<g transform="translate({round(x, 2)} {round(y, 2)}) scale({round(s, 4)})">{svg_x(None)}</g>{ta}</svg>\n')


def escrever_svgs() -> None:
    pasta = RAIZ / "marca/svg"
    pasta.mkdir(parents=True, exist_ok=True)
    nomes = {
        "CorpHorizontal": ("rayzer-x-horizontal", "Rayzer X — Serviços e Tecnologia LTDA"),
        "CorpVertical": ("rayzer-x-vertical", "Rayzer X — Serviços e Tecnologia LTDA"),
        "ProdutoPrincipal": ("xacess-principal", "XAcess — Controle de acesso inteligente"),
        "ProdutoAssinatura": ("xacess-assinatura", "XAcess by Rayzer X"),
        "ProdutoCompacto": ("xacess-compacto", "XAcess"),
    }
    for chave, (arquivo, titulo) in nomes.items():
        for modo in MODOS:
            (pasta / f"{arquivo}-{modo}.svg").write_text(svg_lockup(LOCKUPS[chave], modo, titulo), encoding="utf-8")
    (pasta / "rayzer-x-simbolo.svg").write_text(svg_simbolo(None), encoding="utf-8")
    (pasta / "rayzer-x-simbolo-mono-branco.svg").write_text(svg_simbolo("#FFFFFF"), encoding="utf-8")
    (pasta / "rayzer-x-simbolo-mono-azul-escuro.svg").write_text(svg_simbolo("#0B1A33"), encoding="utf-8")
    (pasta / "rayzer-x-icone.svg").write_text(svg_icone(False), encoding="utf-8")
    (pasta / "xacess-icone.svg").write_text(svg_icone(True), encoding="utf-8")
    # Favicon: o X no quadro, sem nome (legível em 16 e 32 px).
    (pasta / "xacess-favicon.svg").write_text(svg_icone(False), encoding="utf-8")


# ---------------------------------------------------------------- XAML

def trocar_regiao(arquivo: Path, inicio: str, fim: str, novo: str) -> None:
    texto_ = arquivo.read_text(encoding="utf-8")
    padrao = re.compile(re.escape(inicio) + r".*?" + re.escape(fim), re.S)
    if not padrao.search(texto_):
        raise SystemExit(f"marcadores não encontrados em {arquivo}")
    texto_ = padrao.sub(lambda _: f"{inicio}\n{novo}\n  {fim}", texto_)
    arquivo.write_text(texto_, encoding="utf-8")


def xaml_geometrias() -> str:
    linhas = []
    for nome, (d, w) in GEO.items():
        linhas.append(f'  <!-- "{LETREIROS[nome][1]}" em {LETREIROS[nome][0]}, corpo 100, largura {w} -->')
        linhas.append(f'  <Geometry x:Key="Rayzer.Letreiro.{nome}">{d}</Geometry>')
    return "\n".join(linhas)


def xaml_canvas(nome_lockup: str, x_name: str, visivel: bool) -> str:
    l = LOCKUPS[nome_lockup]
    vis = "" if visivel else ' Visibility="Collapsed"'
    linhas = [f'          <Viewbox x:Name="{x_name}" Stretch="Uniform"{vis}>',
              f'            <Canvas Width="{l["w"]}" Height="{l["h"]}">']
    for it in l["itens"]:
        if it["tipo"] == "texto":
            fill = "{TemplateBinding Foreground}" if it["papel"] == "texto" else "{TemplateBinding CorDeApoio}"
            linhas.append(
                f'              <Path Canvas.Left="{it["x"]}" Canvas.Top="{it["y"]}" Fill="{fill}" Data="{{DynamicResource Rayzer.Letreiro.{it["nome"]}}}">'
                f'<Path.RenderTransform><ScaleTransform ScaleX="{it["k"]}" ScaleY="{it["k"]}" /></Path.RenderTransform></Path>'
                if it["k"] != 1 else
                f'              <Path Canvas.Left="{it["x"]}" Canvas.Top="{it["y"]}" Fill="{fill}" Data="{{DynamicResource Rayzer.Letreiro.{it["nome"]}}}" />')
        else:
            linhas.append(
                f'              <local:RayzerBrandMark Canvas.Left="{it["x"]}" Canvas.Top="{it["y"]}" Tamanho="{it["lado"]}" '
                'Brilho="{TemplateBinding Brilho}" Monocromatico="{TemplateBinding Monocromatico}" Foreground="{TemplateBinding Foreground}" />')
    linhas += ["            </Canvas>", "          </Viewbox>"]
    return "\n".join(linhas)


def xaml_modelos() -> str:
    corp = f"""  <!-- ===== RayzerLogo: a marca corporativa RAYZER X (Rayzer Serviços e Tecnologia LTDA) ===== -->
  <Style TargetType="{{x:Type local:RayzerLogo}}">
    <Setter Property="Foreground" Value="{{DynamicResource Rayzer.Nav.Text.Active}}" />
    <Setter Property="CorDeApoio" Value="{{DynamicResource Rayzer.Nav.Text.Muted}}" />
    <Setter Property="Height" Value="40" />
    <Setter Property="IsTabStop" Value="False" />
    <Setter Property="Focusable" Value="False" />
    <Setter Property="AutomationProperties.Name" Value="Rayzer X — Serviços e Tecnologia" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{{x:Type local:RayzerLogo}}">
          <Grid>
{xaml_canvas("CorpHorizontal", "Horizontal", True)}
{xaml_canvas("CorpHorizontalSimples", "Simples", False)}
{xaml_canvas("CorpVertical", "Vertical", False)}
          <local:RayzerBrandMark x:Name="Simbolo" Visibility="Collapsed" Tamanho="{{Binding ActualHeight, RelativeSource={{RelativeSource TemplatedParent}}}}"
                                 Brilho="{{TemplateBinding Brilho}}" Monocromatico="{{TemplateBinding Monocromatico}}" Foreground="{{TemplateBinding Foreground}}" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="Descritor" Value="False">
              <Setter TargetName="Horizontal" Property="Visibility" Value="Collapsed" />
              <Setter TargetName="Simples" Property="Visibility" Value="Visible" />
            </Trigger>
            <Trigger Property="Variante" Value="Vertical">
              <Setter TargetName="Horizontal" Property="Visibility" Value="Collapsed" />
              <Setter TargetName="Simples" Property="Visibility" Value="Collapsed" />
              <Setter TargetName="Vertical" Property="Visibility" Value="Visible" />
            </Trigger>
            <Trigger Property="Variante" Value="Simbolo">
              <Setter TargetName="Horizontal" Property="Visibility" Value="Collapsed" />
              <Setter TargetName="Simples" Property="Visibility" Value="Collapsed" />
              <Setter TargetName="Simbolo" Property="Visibility" Value="Visible" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>"""
    prod = f"""  <!-- ===== XAcessLogo: o produto XAcess (endosso "by RAYZER X") ===== -->
  <Style TargetType="{{x:Type local:XAcessLogo}}">
    <Setter Property="Foreground" Value="{{DynamicResource Rayzer.Text.Primary}}" />
    <Setter Property="CorDeApoio" Value="{{DynamicResource Rayzer.Text.Secondary}}" />
    <Setter Property="Height" Value="40" />
    <Setter Property="IsTabStop" Value="False" />
    <Setter Property="Focusable" Value="False" />
    <Setter Property="AutomationProperties.Name" Value="XAcess — Controle de acesso inteligente" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{{x:Type local:XAcessLogo}}">
          <Grid>
{xaml_canvas("ProdutoPrincipal", "Principal", True)}
{xaml_canvas("ProdutoAssinatura", "Assinatura", False)}
{xaml_canvas("ProdutoCompacto", "Compacto", False)}
          <local:RayzerAppIcon x:Name="Icone" Visibility="Collapsed" Width="{{Binding ActualHeight, RelativeSource={{RelativeSource TemplatedParent}}}}" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="Variante" Value="ComAssinatura">
              <Setter TargetName="Principal" Property="Visibility" Value="Collapsed" />
              <Setter TargetName="Assinatura" Property="Visibility" Value="Visible" />
            </Trigger>
            <Trigger Property="Variante" Value="Compacto">
              <Setter TargetName="Principal" Property="Visibility" Value="Collapsed" />
              <Setter TargetName="Compacto" Property="Visibility" Value="Visible" />
            </Trigger>
            <Trigger Property="Variante" Value="Icone">
              <Setter TargetName="Principal" Property="Visibility" Value="Collapsed" />
              <Setter TargetName="Icone" Property="Visibility" Value="Visible" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>"""
    return corp + "\n\n" + prod


# ---------------------------------------------------------------- TypeScript

def escrever_ts() -> None:
    destino = RAIZ / "web/rayzer-ui/src/components/brand/letreiros.ts"
    geo = {nome: {"d": d, "largura": w} for nome, (d, w) in GEO.items()}
    corpo = (
        "// Gerado por tools/gerar-marca.py — não editar à mão.\n"
        "// Letreiros da marca em vetor (Sora, corpo 100, y = 0 no alto da maiúscula) e os\n"
        "// lockups do brand board: marca corporativa RAYZER X e produto XAcess.\n\n"
        "export type LockupItem =\n"
        '  | { tipo: "texto"; nome: LetreiroNome; x: number; y: number; k: number; papel: "texto" | "apoio" }\n'
        '  | { tipo: "marca"; x: number; y: number; lado: number };\n\n'
        "export interface Lockup { w: number; h: number; itens: LockupItem[] }\n\n"
        f"export type LetreiroNome = {' | '.join(json.dumps(n) for n in GEO)};\n\n"
        f"export const LETREIROS: Record<LetreiroNome, {{ d: string; largura: number }}> = {json.dumps(geo, ensure_ascii=False, indent=2)};\n\n"
        f"export const LOCKUPS = {json.dumps(LOCKUPS, ensure_ascii=False, indent=2)} satisfies Record<string, Lockup>;\n"
    )
    destino.write_text(corpo, encoding="utf-8")


def main() -> None:
    trocar_regiao(RAIZ / "src/Rayzer.Design/Tokens.xaml", "<!-- letreiros:inicio -->", "<!-- letreiros:fim -->", xaml_geometrias())
    trocar_regiao(RAIZ / "src/Rayzer.Design/Themes/Generic.xaml", "<!-- marca:inicio -->", "<!-- marca:fim -->", xaml_modelos())
    escrever_ts()
    escrever_svgs()
    for nome, l in LOCKUPS.items():
        print(f"{nome:24} {l['w']:8.2f} × {l['h']:7.2f}  (proporção {l['w'] / l['h']:.2f})")


if __name__ == "__main__":
    main()
