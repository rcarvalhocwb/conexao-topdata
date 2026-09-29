#!/usr/bin/env python3
"""
Rasteriza os SVGs de marca/svg (gerados por tools/gerar-marca.py) em PNG, WEBP e ICO, e
atualiza os arquivos que o aplicativo e o instalador usam (src/Rayzer.Design/Marca).

Requer: Chromium (headless) e Pillow. Caminho do Chromium: variável CHROMIUM ou o do
Playwright em /opt/pw-browsers.
"""
from __future__ import annotations

import glob
import os
import shutil
import subprocess
import tempfile
from pathlib import Path

from PIL import Image

RAIZ = Path(__file__).resolve().parent.parent
SVG = RAIZ / "marca/svg"
PNG = RAIZ / "marca/png"
WEBP = RAIZ / "marca/webp"
ICO = RAIZ / "marca/ico"
APP = RAIZ / "src/Rayzer.Design/Marca"


def chromium() -> str:
    if os.environ.get("CHROMIUM"):
        return os.environ["CHROMIUM"]
    achados = sorted(glob.glob("/opt/pw-browsers/chromium-*/chrome-linux*/chrome"))
    if not achados:
        raise SystemExit("Chromium não encontrado (defina CHROMIUM).")
    return achados[-1]


def rasterizar(svg: Path, destino: Path, largura: int, altura: int) -> None:
    """SVG → PNG transparente, centralizado na caixa pedida."""
    with tempfile.TemporaryDirectory() as tmp:
        html = Path(tmp) / "p.html"
        html.write_text(
            "<html><body style='margin:0;background:transparent'>"
            f"<div style='width:{largura}px;height:{altura}px;display:flex;align-items:center;justify-content:center'>"
            f"<img src='file://{svg}' style='width:{largura}px;height:{altura}px;object-fit:contain'></div>"
            "</body></html>",
            encoding="utf-8",
        )
        # O headless desconta parte da janela e tem largura mínima: janela folgada e recorte.
        bruto = Path(tmp) / "bruto.png"
        subprocess.run(
            [chromium(), "--headless", "--no-sandbox", "--disable-gpu", "--hide-scrollbars", "--allow-file-access-from-files",
             "--default-background-color=00000000", f"--window-size={max(largura, 600)},{altura + 400}",
             f"--screenshot={bruto}", f"file://{html}"],
            check=True, capture_output=True,
        )
        Image.open(bruto).crop((0, 0, largura, altura)).save(destino)


def main() -> None:
    for pasta in (PNG, WEBP, ICO):
        pasta.mkdir(parents=True, exist_ok=True)

    # Lockups: altura 256 (a largura segue a proporção do SVG, com respiro de 12%).
    for svg in sorted(SVG.glob("*.svg")):
        nome = svg.stem
        if nome.endswith("-icone") or nome == "xacess-favicon":
            continue
        caixa = svg.read_text(encoding="utf-8").split('viewBox="', 1)[1].split('"', 1)[0].split()
        w, h = float(caixa[2]), float(caixa[3])
        altura = 512 if "simbolo" in nome else 256
        largura = round(altura * w / h)
        margem = round(altura * 0.12)
        rasterizar(svg, PNG / f"{nome}.png", largura + 2 * margem, altura + 2 * margem)

    # Ícones e favicon.
    for nome, tamanhos in (("rayzer-x-icone", (512, 256, 128, 64, 48, 32, 16)), ("xacess-icone", (512, 256, 128)), ("xacess-favicon", (64, 48, 32, 16))):
        for t in tamanhos:
            rasterizar(SVG / f"{nome}.svg", PNG / f"{nome}-{t}.png", t, t)

    for png in PNG.glob("*.png"):
        Image.open(png).save(WEBP / f"{png.stem}.webp", "WEBP", lossless=True, method=6)

    # ICO multi-tamanho: o X no quadro até 64 px; com o nome do produto em 128 e 256.
    def ico(destino: Path, pequeno: str, grande: str) -> None:
        imagens = [Image.open(PNG / f"{pequeno}-{t}.png").convert("RGBA") for t in (16, 32, 48, 64)]
        imagens += [Image.open(PNG / f"{grande}-{t}.png").convert("RGBA") for t in (128, 256)]
        imagens[-1].save(destino, format="ICO", sizes=[i.size for i in imagens], append_images=imagens[:-1])

    ico(ICO / "xacess.ico", "xacess-favicon", "xacess-icone")
    ico(ICO / "rayzer-x.ico", "rayzer-x-icone", "rayzer-x-icone")

    # O que o aplicativo e o instalador usam.
    shutil.copy(ICO / "xacess.ico", APP / "rayzer-xacess.ico")
    shutil.copy(PNG / "xacess-favicon-64.png", APP / "rayzer-xacess-64.png")
    print("ok:", len(list(PNG.glob("*.png"))), "png,", len(list(WEBP.glob("*.webp"))), "webp, 2 ico")


if __name__ == "__main__":
    main()
