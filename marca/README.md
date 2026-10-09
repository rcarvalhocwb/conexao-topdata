# Marca Rayzer — arquivos

Rayzer Serviços e Tecnologia LTDA (**RAYZER X**, a empresa) e **XAcess** (o produto, by
RAYZER X). Tudo aqui é gerado — não edite à mão:

```
python tools/gerar-marca.py          # letreiros (Sora), lockups, SVG, XAML e TypeScript
python tools/gerar-marca-imagens.py  # PNG, WEBP e ICO a partir dos SVG (Chromium + Pillow)
```

| Pasta | Conteúdo |
|---|---|
| `svg/` | `rayzer-x-horizontal`, `rayzer-x-vertical` (empresa) e `xacess-principal`, `xacess-assinatura`, `xacess-compacto` (produto), cada um em `-escuro`, `-claro`, `-mono-branco`, `-mono-azul-escuro`; `rayzer-x-simbolo*`; `rayzer-x-icone`, `xacess-icone`, `xacess-favicon` |
| `png/`, `webp/` | os mesmos, transparentes; ícones em 512/256/128 e favicon em 64/48/32/16 |
| `ico/` | `xacess.ico` (o do executável e do instalador) e `rayzer-x.ico`, 16–256 px |
| `animacao-abertura.json` | fases e tempos da abertura (seção 08 do board) |

Uso, respiro, tamanhos mínimos e o que não fazer: `docs/28-rayzer-ui-componentes.md` §0.
Tipografia: Sora, Inter e JetBrains Mono, SIL Open Font License 1.1 (licenças em
`src/Rayzer.Design/Fontes`).
