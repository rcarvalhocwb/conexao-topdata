# 28 — Rayzer UI: a marca e os componentes (brand board → código)

> Rayzer Serviços e Tecnologia LTDA · produto **XAcess** · desenvolvido sob demanda.
> Fonte visual: o brand board da Rayzer (seções 01–11). Este documento transforma o board em
> tokens e componentes reutilizáveis, fiéis ao que ele mostra, sem redesenhar a interface.
> Dois stacks, uma marca: **desktop** WPF (`src/Rayzer.Design`, o produto em operação) e
> **web** React + TypeScript + Tailwind (`web/rayzer-ui`). Visão estratégica e voz: docs/27.

Regras que valem para tudo aqui:

- **Nada de funcionalidade inventada.** Um componente só existe se há função real para ele.
  O que o board sugere e o produto ainda não tem está em §8 como PROPOSTA FUTURA.
- **O design não pode quebrar a operação.** Situação sempre com cor + glifo + texto; nada
  de animação que atrase, esconda ou bloqueie o operador.
- **Uma fonte de verdade.** Cores nos temas (`Temas/*.xaml` ↔ `--rayzer-*`), letreiros em
  `tools/gerar-marca.py`, tempos da abertura em `marca/animacao-abertura.json`. Os testes
  reprovam quando um lado diverge do outro.

---

## 0. Arquitetura de marca: empresa × produto

O board separa duas marcas, e o código também:

```
RAYZER X                        a empresa — Rayzer Serviços e Tecnologia LTDA
  "Tecnologia que conecta pessoas, ambientes e o futuro."
  └─ XAcess  (by RAYZER X)      o produto — controle de acesso inteligente
       "ACCESS IS A FLOW, NOT A DOOR."
```

| | Empresa — `RayzerLogo` | Produto — `XAcessLogo` |
|---|---|---|
| Letreiro | RAYZER + X | X + acess ("Xacess") |
| Complemento | "SERVIÇOS E TECNOLOGIA LTDA" (descritor) | "CONTROLE DE ACESSO INTELIGENTE" (tagline) ou "by RAYZER X" (endosso) |
| Variantes | horizontal (com/sem descritor), vertical, símbolo, monocromático, claro/escuro | principal, com assinatura, compacto, ícone, monocromático, claro/escuro |
| Onde aparece | alto da barra lateral, abertura, documentos da empresa | bloco do produto na barra lateral, barra superior ("XAcess by RAYZER X"), assistente, ícone do executável e do instalador, favicon |

Nunca: juntar os dois num letreiro só ("RAYZER X acess" — era a versão anterior, corrigida),
recolorir o X, girar, contornar, esticar, pôr o produto sem o X.

**Construção.** O X fica numa grade 32 × 32 (`Rayzer.Mark.*.Geometry`): a faixa do fluxo
(\\, Azul Principal, contínua) passa por cima da faixa da passagem (/, ciano → prata).
Os letreiros são vetores gerados da **Sora** com kerning (`tools/gerar-marca.py` →
`Rayzer.Letreiro.*` no XAML e `letreiros.ts` na web): o logo não depende de fonte instalada
e sai idêntico nos dois stacks e nos arquivos. Proporções (largura ÷ altura): horizontal
4,07 · vertical 1,44 · produto principal 3,07 · com assinatura 3,14 · compacto 4,66.

**Respiro e tamanho mínimo.** Respiro: metade da altura do X em volta. Mínimos: símbolo
16 px; horizontal sem descritor 24 px de altura; com descritor ou tagline 40 px (abaixo
disso o texto pequeno some — use a versão sem ele); ícone com "acess" a partir de 96 px,
abaixo disso o favicon só com o X.

**Arquivos (seção 10 do board)** — em `marca/`, gerados por `tools/gerar-marca.py` e
`tools/gerar-marca-imagens.py`:

| Formato | Conteúdo |
|---|---|
| SVG | lockups da empresa e do produto em 4 modos (escuro, claro, mono branco, mono azul-escuro), símbolo, ícones, favicon |
| PNG | os mesmos, transparentes (lockups com 256 px de altura, símbolo 512, ícones 512/256/128, favicon 64/48/32/16) |
| WEBP | os mesmos PNG, sem perda |
| ICO | `xacess.ico` (produto) e `rayzer-x.ico` (empresa), 16–256 px |
| JSON | `animacao-abertura.json`: fases, tempos e curva da abertura |
| Lottie, FIG | **não entregues** (§8): exigem After Effects/Figma; o JSON e os SVG são a base |

**Abertura (seção 08).** `RayzerAbertura` (WPF) e `RayzerSplash` (web): fluxos se
aproximam (0–0,6 s) → interseção e pulso (0,6–1,4 s) → o X se completa (1,4–1,8 s) →
marca e produto (1,8–2,2 s) → some (2,6–2,9 s). Nunca recebe clique nem foco; com as
animações do Windows desligadas ou `prefers-reduced-motion`, não aparece.

---

## PARTE 1 — Design tokens

### 1.1 Cores (paleta do board, seção 06)

| Papel | Token WPF | Variável CSS | Escuro | Claro |
|---|---|---|---|---|
| Rayzer Blue — ação principal | `Rayzer.Brand.Primary` | `--rayzer-brand-blue` | `#0066FF` | `#0066FF` |
| Azul sob o mouse | `Rayzer.Brand.Primary.Hover` | `--rayzer-brand-blue-hover` | `#0058E0` | `#0057DB` |
| Azul como texto/ícone | `Rayzer.Brand.Primary.Foreground` | `--rayzer-brand-blue-fg` | `#5A9BFF` | `#0060F0` |
| Rayzer Cyan — destaque, energia | `Rayzer.Brand.Cyan` | `--rayzer-brand-cyan` | `#00D5FF` | `#00D5FF` |
| Fundo principal | `Rayzer.Background` | `--rayzer-bg` | `#0B1A33` | `#F4F7FB` |
| Superfície elevada (cartão) | `Rayzer.Surface` | `--rayzer-card-bg` | `#111827` | `#FFFFFF` |
| Menus, popups | `Rayzer.Surface.Elevated` | `--rayzer-bg-elevated` | `#172235` | `#FFFFFF` |
| Sob o mouse | `Rayzer.Surface.Hover` | `--rayzer-surface-hover` | `#172236` | `#EAF1FB` |
| Barra lateral | `Rayzer.Nav.Background` | `--rayzer-sidebar-bg` | `#07101F` | `#0B1A33` |
| Borda | `Rayzer.Border` | `--rayzer-border` | `#22304A` | `#D6DFEB` |
| Texto principal (Neutro claro) | `Rayzer.Text.Primary` | `--rayzer-text-primary` | `#F4F7FB` | `#0B1A33` |
| Texto secundário (Cinza) | `Rayzer.Text.Secondary` | `--rayzer-text-secondary` | `#8A9BB0` | `#4A5A72` |
| ✓ Sucesso / operacional | `Rayzer.Success` | `--rayzer-success` | `#32D583` | `#0F7A45` |
| ! Atenção / simulação | `Rayzer.Warning` | `--rayzer-warning` | `#F5A524` | `#8A5300` |
| × Erro / negado | `Rayzer.Danger` | `--rayzer-danger` | `#F05252` | `#C8283A` |
| ↻ Informação / sincronizando | `Rayzer.Info` | `--rayzer-info` | `#4DB8FF` | `#0B63CE` |
| Linha de energia | `Rayzer.Energy` | `--rayzer-energy` | `#0066FF → #00D5FF` | idem |

Cada situação tem o seu fundo tingido (`*.Subtle` / `--rayzer-*-subtle`). No claro, as
cores de situação escurecem para passar em 4,5:1 sobre branco. Tabela completa: 57 chaves
por tema em `src/Rayzer.Design/Temas/*.xaml`; as mesmas em `web/rayzer-ui/src/tokens/rayzer.css`.

### 1.2 Tipografia (seção 07)

| Família | Papel | WPF | Web (Tailwind) |
|---|---|---|---|
| **Sora** | interface, títulos, números de destaque | `Rayzer.Font.Display` | `font-rayzer-display` |
| **Inter** | texto, suporte, sistemas | `Rayzer.Font.Text` | `font-rayzer` |
| **JetBrains Mono** | horas, códigos, logs, dados | `Rayzer.Font.Mono` | `font-rayzer-mono` |

Escala (px / altura de linha): overline 11/14 (versalete, +0,08 em) · caption 12/16 ·
small 13/18 · body 14/20 · section 15/20 semibold · title 20/26 · display 26/32 ·
metric 32/36. Desktop: fontes embutidas (`src/Rayzer.Design/Fontes`, subconjunto latino,
SIL OFL 1.1); web: `@rayzer/ui/fonts.css` (`@fontsource`, sem CDN).

### 1.3 Espaçamento, bordas, raio, sombras, movimento

| Grupo | Valores |
|---|---|
| Espaçamento (grade 4) | 4 · 8 · 12 · 16 · 20 · 24 · 32 · 40 (`Rayzer.Space.1–7`, `rayzer-1–8`) |
| Página / cartão | página 28 × 20; cartão 16 × 14 |
| Alturas | campo e botão 40 (alinham na mesma linha); item de menu 44 (toque); linha de tabela 36 (compacta 32) |
| Borda | 1 px `Border`; campo 1 px `Input.Border` (3:1); foco: anel 2 px `Focus` |
| Raio | 4 (campo) · 8 (botão, selo, item) · 12 (cartão, painel) · pílula |
| Sombra | sm, md, lg (neutras); **glow** azul controlado só na marca, no botão principal e no KPI de destaque |
| Movimento | rápido 120 ms (hover), normal 200 ms (troca), fluxo 1400 ms (barra de carregamento), abertura 2900 ms; curva ease-out; reduzido → 0 |

---

## PARTE 2 — Inventário

| Grupo | Componente | Web (`web/rayzer-ui/src/components`) | Desktop (`Rayzer.Design` / telas) |
|---|---|---|---|
| Marca | RayzerLogo | `brand/RayzerLogo.tsx` | `RayzerLogo` |
| | XAcessLogo | `brand/XAcessLogo.tsx` | `XAcessLogo` |
| | RayzerBrandMark | `brand/RayzerBrandMark.tsx` | `RayzerBrandMark` |
| | RayzerAppIcon | `brand/RayzerAppIcon.tsx` | `RayzerAppIcon` |
| | RayzerSplash (abertura) | `brand/RayzerSplash.tsx` | `RayzerAbertura` |
| Layout | AppShell (+ FlowBar) | `layout/AppShell.tsx` | `JanelaPrincipal` + `RayzerFlowBar` |
| Navegação | Sidebar | `navigation/Sidebar.tsx` | barra lateral da `JanelaPrincipal` |
| | SidebarNavItem | `navigation/SidebarNavItem.tsx` | estilo `Rayzer.Nav.Item` |
| Situação | TopOperationalBar | `status/TopOperationalBar.tsx` | barra superior da `JanelaPrincipal` |
| | StatusPill | `status/StatusPill.tsx` | `RayzerStatusPill` |
| | StatusCard | `status/StatusCard.tsx` | `RayzerStatusCard` |
| | InlineStatus | `status/InlineStatus.tsx` | `RayzerStatus` |
| Painel | PageHeader | `dashboard/PageHeader.tsx` | `RayzerPageHeader` |
| | KpiCard | `dashboard/KpiCard.tsx` | `RayzerMetricCard` |
| | SectionHeader | `dashboard/SectionHeader.tsx` | `RayzerSectionHeader` |
| Dispositivos | TurnstileCard | `devices/TurnstileCard.tsx` | modelo `Cartao.Catraca` (App.xaml) |
| | TurnstileMetaItem | `devices/TurnstileMetaItem.tsx` | `RayzerMetaItem` |
| | TurnstileActionBar | `devices/TurnstileActionBar.tsx` | barra de ações do `Cartao.Catraca` |
| | (ilustração) | `devices/TurnstileIllustration.tsx` | `RayzerTurnstileGlyph` |
| Dados | DataPanel | `data-display/DataPanel.tsx` | `RayzerDataPanel` |
| | DataTable | `data-display/DataTable.tsx` | `DataGrid` + estilos Rayzer |
| | EmptyState | `data-display/EmptyState.tsx` | `RayzerEmptyState` |
| | InfoNotice | `data-display/InfoNotice.tsx` | `RayzerAlert` / estilo `InfoNotice` |
| Formulários | RayzerButton | `forms/RayzerButton.tsx` | estilos `Rayzer.Button.*` |
| | RayzerIconButton | `forms/RayzerIconButton.tsx` | `Rayzer.Button.Icon` |
| | RayzerInput | `forms/RayzerInput.tsx` | `TextBox` + estilo |
| | RayzerSelect | `forms/RayzerSelect.tsx` | `ComboBox` + estilo |
| | RayzerDateInput | `forms/RayzerDateInput.tsx` | `DatePicker` + hora (docs/Periodo) |
| | FilterBar | `forms/FilterBar.tsx` | barra de filtros das telas |

---

## PARTE 3 — Especificação por componente

Convenções da web: toda prop de cor é um **tom** (`success | danger | warning | info |
brand | neutral`), nunca um hex; `className` estende sem substituir.

### RayzerLogo — a empresa
- **Finalidade:** assinar como Rayzer Serviços e Tecnologia.
- **Anatomia:** letreiro RAYZER (vetor) + X (gradientes, bevel) + descritor opcional.
- **Props:** `variant` horizontal | vertical | symbol · `descriptor` (true) · `height` (40) · `glow` · `mono` · `supportColor`.
- **Estados/variantes:** colorido, monocromático (currentColor), com/sem brilho; texto em currentColor (claro/escuro).
- **Comportamento:** a altura define o tamanho; a largura segue a proporção; não encolhe em flex.
- **Acessibilidade:** `role="img"`, `aria-label="Rayzer X — Serviços e Tecnologia"`.
- **Exemplo:** `<RayzerLogo descriptor={false} height={30} glow />` (barra lateral).

### XAcessLogo — o produto
- **Finalidade:** identificar o XAcess, com o endosso da empresa quando cabe.
- **Anatomia:** X + "acess" (vetor) + tagline **ou** "by RAYZER X".
- **Props:** `variant` principal | signature | compact | icon · `height` · `glow` · `mono` · `supportColor`.
- **Comportamento:** `icon` delega ao RayzerAppIcon (com o nome a partir de 96 px).
- **Acessibilidade:** `aria-label` "XAcess — Controle de acesso inteligente" ou "XAcess by Rayzer X".
- **Exemplo:** `<XAcessLogo variant="signature" height={38} />` (barra superior, à direita).

### RayzerBrandMark — o X
- **Props:** `size` (32) · `glow` · `mono` · `title` ("" = decorativo).
- **Regras:** mínimo 16 px; ids de gradiente únicos por instância (testado).

### RayzerAppIcon — ícone do aplicativo
- **Anatomia:** quadro 240/256 com raio 56, fundo azul profundo, borda Azul Principal 6, X; opcional "acess".
- **Props:** `size` · `withName` · `title`.

### RayzerSplash — abertura
- **Props:** `onDone` · `still` (quadro final, sem animar).
- **Comportamento:** 2,9 s, uma vez; `pointer-events: none`; movimento reduzido → `onDone` na hora.
- **Acessibilidade:** `role="status"`, "Abrindo o XAcess".

### AppShell
- **Finalidade:** o esqueleto: barra lateral, barra operacional, linha de carregamento, conteúdo.
- **Props:** `sidebar` · `topbar` · `loading` · `children`.
- **Comportamento:** o conteúdo rola; barra lateral e superior ficam. `loading` liga a FlowBar (`role="progressbar"`).

### Sidebar
- **Anatomia (seção 09):** RAYZER X no alto → bloco do produto (ícone + "XAcess / Controle de acesso") → menu → rodapé (selos, tema, recolher, autoria).
- **Props:** `items[] {id,label,icon,disabled}` · `activeId` · `onNavigate` · `collapsed` · `onToggleCollapse` · `theme` · `onToggleTheme` · `badges` · `footer`.
- **Estados:** aberta 248 px / recolhida 72 px (só ícones, dica com o nome).
- **Acessibilidade:** `<aside aria-label="Navegação principal">`; o bloco do produto é contexto, não botão.

### SidebarNavItem
- **Props:** `icon` · `label` · `active` · `collapsed` · `disabled` · `onClick`.
- **Estados:** normal, hover, ativo (fundo + linha ciano à esquerda + `aria-current="page"`), foco (anel), desabilitado.
- **Alvo:** 44 px de altura.

### TopOperationalBar
- **Finalidade:** STATUS → EXCEÇÃO → CONTEXTO → AÇÃO em qualquer tela.
- **Props:** `blocks` (StatusCards) · `aside` (relógio, produto) · `message` (a frase da situação).
- **Exemplo:** serviço local, catracas, nuvem, modo simulação; à direita o relógio de Brasília e "XAcess by RAYZER X".

### StatusPill / InlineStatus / StatusCard
- **StatusPill:** `tone` · `label` · `glyph?` · `size` sm | md — pílula tingida com glifo.
- **InlineStatus:** `tone` · `label` · `glyph?` · `icon?` — a mesma coisa sem pílula, dentro do texto.
- **StatusCard:** `icon` · `title` · `description` · `tone` — bloco da barra operacional com ícone tingido.
- **Regra:** situação nunca é só cor — glifo (✓ × ! ↻ ● ○) + texto sempre (testado).

### PageHeader / SectionHeader
- **PageHeader:** `title` (Sora 26) · `description` · `actions`.
- **SectionHeader:** `title` · `subtitle` · `action` · `as` h2 | h3 — com a linha de energia vertical.

### KpiCard
- **Anatomia:** rótulo · selo do ícone (fundo tingido + contorno na cor da situação) · número (Sora 32, dígitos tabulares) · contexto · linha de energia.
- **Props:** `label` · `value` · `icon` · `hint` · `tone` · `disabled` · `energyLine`.
- **Estados:** `brand` ganha o glow; `disabled` a 55%.
- **Acessibilidade:** `aria-label="rótulo: valor"`.

### TurnstileCard / TurnstileMetaItem / TurnstileActionBar
- **TurnstileCard:** `code` ("CATRACA 01") · `name` · `status {tone,label,glyph}` · `lastEvent {decision,when}` · `meta[]` · `actions`.
- **TurnstileMetaItem:** `icon` · `label` · `value` (firmware, grupo, porta, reconexões — só o que o serviço informa).
- **TurnstileActionBar:** ações do cartão alinhadas à direita, sobre uma divisória. Hoje: "Ver acessos" e "Diagnóstico" (o que existe).

### DataPanel / DataTable / EmptyState / InfoNotice
- **DataPanel:** `title` · `subtitle` · `toolbar` · `children` — cartão de dados.
- **DataTable:** `columns[] {key,header,width,align,mono,render}` · `rows` · `rowKey` · `compact` · `empty` · `caption`. Cabeçalho `scope="col"`; código e hora em mono; **códigos sempre como texto** (zeros à esquerda preservados — testado); vazia, mostra `empty`.
- **EmptyState:** `icon` · `title` · `description` · `note` · `children` — explica por que está vazio e o que fazer (`role="status"`).
- **InfoNotice:** `tone` · `title` · `children` · `actions` — perigo e atenção são `role="alert"`; o resto, `role="note"`.

### RayzerButton / RayzerIconButton
- **RayzerButton:** `variant` primary | secondary | ghost | danger · `size` sm | md · `icon` · `loading` · atributos de `<button>`. Primary = a ação da tela (uma por tela), com glow sutil. `loading` → `aria-busy` + desabilitado. `type="button"` por padrão.
- **RayzerIconButton:** `label` (obrigatório → `aria-label` e dica) · `icon` · `size`.

### RayzerInput / RayzerSelect / RayzerDateInput / FilterBar
- **Campos:** `label` (sempre visível, ligado ao campo) · `hint` · `error` (→ `aria-invalid` e `aria-describedby`) · 40 px de altura · foco com anel azul.
- **RayzerInput:** + `mono` para códigos.
- **RayzerSelect:** `options[] {value,label}` — seleção nativa (teclado e leitor de tela de graça).
- **RayzerDateInput:** `withTime` → `datetime-local`. Valores no fuso do evento (Brasília); a conversão para UTC é de quem consome. O formato exibido segue o idioma do navegador.
- **FilterBar:** `children` · `onSubmit` · `label` — `role="search"`, campos alinhados pela base, Enter envia.

---

## PARTE 4 — Arquitetura

```
web/rayzer-ui/
  src/
    tokens/        rayzer.css (--rayzer-*, escuro padrão, [data-theme="light"]), fonts.css
    tailwind-preset.ts   cores → var(--rayzer-*), fontes, escala, sombras, alturas
    lib/           cx, tone (Tone, glifos, classes por tom, anel de foco)
    icons/         ícones SVG em linha (currentColor)
    components/
      brand/         RayzerLogo, XAcessLogo, RayzerBrandMark, RayzerAppIcon, RayzerSplash, Lockup, letreiros.ts (gerado)
      layout/        AppShell, FlowBar
      navigation/    Sidebar, SidebarNavItem
      status/        TopOperationalBar, StatusPill, StatusCard, InlineStatus, OperationalClock
      dashboard/     PageHeader, SectionHeader, KpiCard
      devices/       TurnstileCard, TurnstileMetaItem, TurnstileActionBar, TurnstileIllustration
      data-display/  DataPanel, DataTable, EmptyState, InfoNotice
      forms/         RayzerButton, RayzerIconButton, RayzerInput, RayzerSelect, RayzerDateInput, FilterBar
    index.ts       exportações públicas
  tests/           paridade com o desktop, contraste AA, marca, componentes, abertura
  demo/            Vite: prancha da marca, painel escuro e claro, abertura
src/Rayzer.Design/  o mesmo sistema em WPF (Tokens, Temas, Controles, Themes/Generic, Fontes, Marca)
marca/              arquivos de marca (SVG, PNG, WEBP, ICO, JSON)
tools/gerar-marca.py, tools/gerar-marca-imagens.py
```

## PARTE 5 — Código base

```tsx
// main.tsx
import "@rayzer/ui/fonts.css";
import "@rayzer/ui/tokens.css";
import { AppShell, Sidebar, TopOperationalBar, StatusCard, OperationalClock, XAcessLogo, RayzerSplash } from "@rayzer/ui";

// tailwind.config.ts
import rayzerPreset from "@rayzer/ui/tailwind-preset";
export default { presets: [rayzerPreset], content: ["./src/**/*.{ts,tsx}", "./node_modules/@rayzer/ui/src/**/*.{ts,tsx}"] };
```

O `content` do Tailwind **precisa** incluir os `.ts` do pacote (as classes por tom moram em
`lib/tone.ts`) — sem isso, os fundos tingidos somem (achado na validação visual).
Exemplo completo e executável: `web/rayzer-ui/demo/main.tsx` (`npm run demo`).

## PARTE 6 — Variáveis CSS do tema

`web/rayzer-ui/src/tokens/rayzer.css`: `:root` e `[data-theme="dark"]` (padrão) e
`[data-theme="light"]`, com as mesmas chaves — marca (`--rayzer-brand-*`), superfícies
(`--rayzer-bg`, `--rayzer-card-bg`, `--rayzer-bg-elevated`, `--rayzer-surface-*`,
`--rayzer-sidebar-bg`), bordas, texto (`--rayzer-text-*`), situação (`--rayzer-success`,
`-warning`, `-danger`, `-info`, `-neutral` e `-subtle`), campos, foco, navegação
(`--rayzer-nav-*`), brilho e energia (`--rayzer-glow-*`, `--rayzer-energy`), sombras e
movimento (`--rayzer-motion-*`, zerado com movimento reduzido). Trocar o tema é trocar o
atributo `data-theme` no elemento raiz.

## PARTE 7 — Regras de uso

1. **Empresa ≠ produto.** RAYZER X assina a empresa; XAcess é o produto, com o X abrindo o nome e o endosso "by RAYZER X".
2. **Situação = cor + glifo + texto.** Nunca só cor, nunca só ícone.
3. **Azul Principal só na ação principal.** Uma por tela. O resto é secundário ou fantasma.
4. **Ciano é energia, não texto:** linha do item ativo, linha de energia, brilho. Não use ciano em texto corrido.
5. **Glow controlado:** marca, botão principal, KPI de destaque. Nada de neon, nada de glow em texto.
6. **Mono para dados:** hora, código, porta, firmware, registro.
7. **Cartão é string.** Código nunca vira número (zeros à esquerda).
8. **Nenhum hex na tela.** Toda cor vem de token (testado nos dois stacks).
9. **Vazio explica.** Toda lista vazia diz por que e o que fazer.
10. **Movimento serve à operação.** Abertura uma vez e não bloqueia; nada pisca; reduzido desliga.
11. **Contraste AA** em todo par texto/fundo, inclusive sob o mouse (testado).
12. **Não inventar.** Função que não existe não ganha botão, menu nem placeholder.

---

## 8. O que o board sugere e não existe (PROPOSTA FUTURA / MELHORIA RECOMENDADA)

| Item | Situação |
|---|---|
| Seletor de produtos na barra lateral | PROPOSTA FUTURA — hoje há um produto; o bloco é só contexto |
| Perfil do usuário, login | PROPOSTA FUTURA — o painel não tem login (docs/27 §11) |
| Menu "mais opções" do cartão da catraca | PROPOSTA FUTURA — depende da fase 4 (gerenciar catraca) |
| Animação Lottie da abertura | MELHORIA RECOMENDADA — requer After Effects; `animacao-abertura.json` e os SVG são o insumo |
| Arquivo Figma (FIG) do design system | MELHORIA RECOMENDADA — requer Figma; este documento, `marca/svg` e a demo são a fonte |
| Fontes Sora/Inter no Setup (tela do instalador WiX) | Limitação: o tema do WixStdBA usa fontes do sistema |

## 9. Validação

| O quê | Onde |
|---|---|
| Paleta do board, contraste AA (inclusive hover), chaves, cores soltas, empresa × produto, letreiros iguais nos dois stacks, fontes com licença | `tests/Integration/RayzerDesignTests.cs`, `RayzerMarcaTests.cs` |
| Paridade CSS ↔ XAML, contraste, marca, acessibilidade dos componentes, tempos da abertura | `web/rayzer-ui/tests` (Vitest) |
| Marca gerada em dia (o gerador não produz diff) | CI, job "Rayzer UI (web)" |
| Captura real das telas nos dois temas + quadro final da abertura | CI, job "Instalador MSI" → `capturas-das-telas.zip` |
| Demo web construída | CI, artefato `rayzer-ui-demo` |

Limitação conhecida: a renderização das fontes embutidas e da abertura no WPF só é vista
na captura do CI (Windows); o Linux compila e testa o XAML como dado, não desenha.
