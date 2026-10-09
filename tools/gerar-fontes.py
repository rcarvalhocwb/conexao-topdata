#!/usr/bin/env python3
"""
Gera as fontes embutidas em src/Rayzer.Design/Fontes a partir das variáveis do Google Fonts
(SIL OFL 1.1). O WPF não lê eixos de fonte variável: saem instâncias estáticas, com o nome
da família unificado (Sora, Inter, JetBrains Mono) e o subconjunto latino.

  B=https://raw.githubusercontent.com/google/fonts/main/ofl
  curl -sSLo Sora-VF.ttf  "$B/sora/Sora%5Bwght%5D.ttf"
  curl -sSLo Inter-VF.ttf "$B/inter/Inter%5Bopsz,wght%5D.ttf"
  curl -sSLo JBM-VF.ttf   "$B/jetbrainsmono/JetBrainsMono%5Bwght%5D.ttf"
  pip install fonttools && python tools/gerar-fontes.py   # na pasta dos .ttf; saída em ./saida
"""
from fontTools.varLib.instancer import instantiateVariableFont
from fontTools import subset

UNI = "U+0020-007E,U+00A0-017F,U+0192,U+02C6,U+02DA,U+02DC,U+2013-2014,U+2018-201E,U+2022,U+2026,U+2030,U+2039-203A,U+20AC,U+2122,U+2190-2193,U+2212"
PLANO = [
    ("Sora-VF.ttf", "Sora", {"wght": 400}, "Regular"),
    ("Sora-VF.ttf", "Sora", {"wght": 600}, "SemiBold"),
    ("Sora-VF.ttf", "Sora", {"wght": 700}, "Bold"),
    ("Sora-VF.ttf", "Sora", {"wght": 800}, "ExtraBold"),
    ("Inter-VF.ttf", "Inter", {"wght": 400, "opsz": 14}, "Regular"),
    ("Inter-VF.ttf", "Inter", {"wght": 500, "opsz": 14}, "Medium"),
    ("Inter-VF.ttf", "Inter", {"wght": 600, "opsz": 14}, "SemiBold"),
    ("JBM-VF.ttf", "JetBrains Mono", {"wght": 400}, "Regular"),
    ("JBM-VF.ttf", "JetBrains Mono", {"wght": 600}, "SemiBold"),
]
PESO = {"Regular": 400, "Medium": 500, "SemiBold": 600, "Bold": 700, "ExtraBold": 800}
import os
os.makedirs("saida", exist_ok=True)
for src, fam, loc, estilo in PLANO:
    f = TTFont(src)
    inst = instantiateVariableFont(f, loc, updateFontNames=False)
    opts = subset.Options(); opts.layout_features = ["*"]; opts.name_IDs = ["*"]; opts.name_languages = ["*"]; opts.notdef_outline = True
    s = subset.Subsetter(opts); s.populate(unicodes=subset.parse_unicodes(UNI)); s.subset(inst)
    name = inst["name"]
    for rec in list(name.names):
        if rec.nameID in (16, 17, 21, 22, 25): name.removeNames(nameID=rec.nameID)
    arquivo = f"{fam.replace(' ', '')}-{estilo}"
    for pid, eid, lid in ((3, 1, 0x409), (1, 0, 0)):
        name.setName(fam, 1, pid, eid, lid)
        name.setName(estilo, 2, pid, eid, lid)
        name.setName(f"{fam} {estilo}", 4, pid, eid, lid)
        name.setName(arquivo, 6, pid, eid, lid)
        name.setName(fam, 16, pid, eid, lid)
        name.setName(estilo, 17, pid, eid, lid)
    inst["OS/2"].usWeightClass = PESO[estilo]
    sel = inst["OS/2"].fsSelection & ~0b1100001  # limpa italic, bold, regular
    sel |= 0b100000 if estilo == "Bold" else (0b1000000 if estilo == "Regular" else 0)
    inst["OS/2"].fsSelection = sel
    inst["head"].macStyle = 1 if estilo == "Bold" else 0
    for t in ("STAT", "fvar", "gvar", "avar", "HVAR", "MVAR"):
        if t in inst: del inst[t]
    inst.save(f"saida/{arquivo}.ttf")
    print(arquivo, os.path.getsize(f"saida/{arquivo}.ttf"))
