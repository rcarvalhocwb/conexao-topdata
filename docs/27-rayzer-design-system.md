# 27 — Rayzer Design System e a identidade do Rayzer XAcess

> Pedido do usuário (28/09): implementar no sistema a identidade estratégica, verbal,
> visual e digital da **Rayzer Serviços e Tecnologia LTDA**, com o **Rayzer XAcess** como
> primeiro produto. Não um mockup: o produto real.
>
> Código: `src/Rayzer.Design` (a marca como código) e as telas em `src/Desktop.App` e
> `src/Edge.Configurador`. Validação: `tests/Integration/RayzerDesignTests.cs`,
> `tests/Contract/ArquiteturaTests.cs` e as capturas do CI.

## 1. Estratégia

**Princípio: "Access is a flow, not a door."** Acesso é um fluxo que se atravessa, não uma
porta que se fecha: identificar → validar → decidir → autorizar → registrar → sincronizar
→ analisar. A marca e o produto mostram essa travessia.

**Arquitetura de marca**

```
RAYZER X                       marca corporativa — Rayzer Serviços e Tecnologia LTDA
 └─ XAcess (by RAYZER X)       produto: controle de acesso (catracas, cartões, QR)
 └─ [Produto] (by RAYZER X)    próximos produtos herdam tudo de src/Rayzer.Design
```

Empresa e produto são marcas separadas (brand board, seções 01–04): `RayzerLogo` é só a
empresa; `XAcessLogo` é o produto, com o endosso "by RAYZER X". Um produto novo referencia
`Rayzer.Design`, ganha o seu lockup em `tools/gerar-marca.py` e muda o nome da pasta de preferências em `TemaRayzer.Instalar(app, "Produto")`. Cores, tipo, componentes,
voz e símbolo continuam os mesmos. O teste `O_design_system_nao_depende_de_nenhum_projeto`
garante que a biblioteca não carregue regra de nenhum produto.

**Personalidade:** tecnológica, segura, no controle, confiável. Evita neon, efeito gamer,
cyberpunk, animação decorativa e preto absoluto.

## 2. Auditoria do sistema (antes)

WPF (.NET 10), MVVM com ViewModels sem WPF, serviço local por gRPC em named pipe, SQLite
local e worker x86 para a EasyInner.dll. Não há login (o painel é local e usa um token da
máquina). Não há banco na nuvem acessado pela tela, nem WebSocket: o "tempo real" é um
fluxo gRPC.

| Gravidade | Achado | Tratamento |
|---|---|---|
| CRÍTICO | O botão final do Setup não abria nada: o MSI saía de 32 bits e os arquivos iam para `Program Files (x86)` | MSI de 64 bits (fase 1); teste de instalação real no CI |
| CRÍTICO | O serviço caía ao abrir o canal do painel (`CurrentUserOnly` com ACL própria) | Corrigido; teste de regressão |
| ALTO | Filtro "Até" cortava o dia escolhido | Corrigido (fase 1), com hora opcional e fuso de Brasília |
| ALTO | Cores fixas espalhadas (conversores, código do assistente), sem tema escuro | Tokens + temas; o teste proíbe cor solta |
| ALTO | Situação dependia da cor em partes da tela (bolinha colorida) | Sempre símbolo + texto + cor (RayzerStatus) |
| MÉDIO | Controles com o visual padrão do Windows e alturas diferentes na mesma linha | Estilos Rayzer; campos e botões com 40 px |
| MÉDIO | "Select a date" e calendário em inglês | Cultura pt-BR (fase 1) |
| MÉDIO | Lista vazia parecia sistema quebrado | RayzerEmptyState em toda tabela |
| MÉDIO | Nenhuma indicação de carregamento | RayzerFlowBar |
| BAIXO | Sem logo, sem ícone, nome "Conexão Topdata" (marca de terceiro) | Rayzer XAcess, símbolo e ícone próprios |

## 3. Identidade visual

### 3.1 O símbolo e os logos

> Atualizado pelo brand board (seções 01–05). Especificação completa, com lockups, tamanhos
> mínimos e arquivos, em [docs/28](28-rayzer-ui-componentes.md) §0.

Um **X** vítreo: a faixa do **fluxo** (\\, Azul Principal) é contínua e passa por cima; a da
**passagem** (/, do ciano ao prata) se abre para ela. Grade 32 × 32
(`Rayzer.Mark.*.Geometry`). Os letreiros "RAYZER" e "acess" são vetores gerados da Sora
(`Rayzer.Letreiro.*`, `tools/gerar-marca.py`) — idênticos no desktop, na web e em `marca/`.

- **Empresa:** `RayzerLogo` — RAYZER X, com ou sem "SERVIÇOS E TECNOLOGIA LTDA"; horizontal,
  vertical, símbolo; colorido ou monocromático.
- **Produto:** `XAcessLogo` — Xacess, com a tagline "CONTROLE DE ACESSO INTELIGENTE", com o
  endosso "by RAYZER X", compacto ou ícone (`RayzerAppIcon`).

Não recolorir o X, não girar, não contornar, não juntar empresa e produto num letreiro só.

### 3.2 Cor

Paleta do brand board (seção 06). Toda cor mora num token; a tela nunca escreve um valor
de cor (`Nenhuma_tela_tem_cor_solta`), e o tema escuro é conferido contra o board
(`O_tema_escuro_usa_a_paleta_do_brand_board`).

| Token | Claro | Escuro | Papel |
|---|---|---|---|
| `Rayzer.Brand.Primary` | `#0066FF` | `#0066FF` | **Rayzer Blue** — ação principal |
| `Rayzer.Brand.Cyan` | `#00D5FF` | `#00D5FF` | **Rayzer Cyan** — brilho, linha de energia, indicador |
| `Rayzer.Background` | `#F4F7FB` | `#0B1A33` | fundo (Neutro claro / Fundo principal dark) |
| `Rayzer.Surface` | `#FFFFFF` | `#111827` | cartões e tabelas (Superfície elevada) |
| `Rayzer.Text.Primary` | `#0B1A33` | `#F4F7FB` | texto principal |
| `Rayzer.Text.Secondary` | `#4A5A72` | `#8A9BB0` | texto secundário (Cinza) |
| `Rayzer.Success` = `Access.Granted` = `Device.Online` | `#0F7A45` | `#32D583` | ✓ ● Sucesso / operacional |
| `Rayzer.Warning` = `Device.Warning` | `#8A5300` | `#F5A524` | ! Atenção / simulação |
| `Rayzer.Danger` = `Access.Denied` = `Device.Offline` | `#C8283A` | `#F05252` | × ○ Erro / negado |

No claro, as cores de situação escurecem para passar 4,5:1 sobre branco; no escuro são as
do board. Lista completa em `src/Rayzer.Design/Temas/*.xaml` (e `--rayzer-*` na web).

### 3.3 Tipografia

Brand board, seção 07 — embutidas em `src/Rayzer.Design/Fontes` (SIL OFL 1.1, licença ao
lado), com a fonte do sistema de reserva: **Sora** na interface e nos títulos
(`Rayzer.Font.Display`), **Inter** no texto e nos sistemas (`Rayzer.Font.Text`),
**JetBrains Mono** em horas, códigos e registros (`Rayzer.Font.Mono`, dígitos de largura
fixa).

| Token | px | Uso |
|---|---|---|
| `Rayzer.FontSize.Metric` | 32 | número de métrica |
| `Rayzer.FontSize.Title` | 20 | título de página |
| `Rayzer.FontSize.Section` | 15 | seção, nome da catraca |
| `Rayzer.FontSize.Body` | 14 | texto |
| `Rayzer.FontSize.Small` | 13 | rótulo, tabela |
| `Rayzer.FontSize.Caption` | 12 | legenda, cabeçalho de tabela |

### 3.4 Espaço, forma, elevação e movimento

- **Espaço:** grade de 4 px (`Rayzer.Space.1`–`6` = 4, 8, 12, 16, 24, 32). Página com
  28 × 20; cartão com 16 × 14.
- **Altura:** campos e botões da mesma linha com 40 px, para alinharem; itens do menu com
  44 px, o alvo de toque.
- **Raio:** 4 (campo), 8 (botão, menu), 12 (cartão), pílula (situação).
- **Elevação:** `Rayzer.Shadow.Sm/Md/Lg`, só em superfície flutuante. O resto da
  hierarquia é feito com fundo e borda: sombra custa renderização no WPF.
- **Movimento:** a linguagem é o fluxo, uma travessia. A barra de carregamento é um traço
  que atravessa a linha. Durações de 120 ms, 200 ms e 1,4 s, sem quique. Controles
  respondem na hora, sem animação. Com "mostrar animações" desligado no Windows, a barra
  fica parada.

### 3.5 Ícones

Segoe Fluent Icons (Windows 11), com Segoe MDL2 Assets de reserva (Windows 10), ambos do
sistema. As chaves ficam em `Rayzer.Icon.*` no `Tokens.xaml`.

## 4. Linguagem da situação

Padrão global: **cor + símbolo + texto**, nunca só a cor.

| Símbolo | Situação | Tom |
|---|---|---|
| ✓ | ACESSO AUTORIZADO · SINCRONIZADO | Sucesso |
| × | ACESSO NEGADO | Perigo |
| ● | DISPOSITIVO ONLINE ("Atendendo") | Sucesso |
| ○ | DISPOSITIVO OFFLINE / PARADO | Perigo / Neutro |
| ! | ATENÇÃO | Atenção |
| ↻ | SINCRONIZANDO | Info |

Implementado em `RayzerStatus` (Tom + Texto + Glifo).

## 5. Componentes

| Componente | Onde | Para quê |
|---|---|---|
| `RayzerStatus` | tabelas, cartões, barra superior | situação com símbolo, texto e cor; em pílula |
| `RayzerMetricCard` | painel ao vivo, prestação de contas | um número com rótulo, ícone e contexto |
| `RayzerAlert` | mensagens de tela, problemas | STATUS → EXCEÇÃO → CONTEXTO → AÇÃO |
| `RayzerEmptyState` | toda tabela | explica por que está vazio e o que fazer |
| `RayzerLogo` | barra lateral, abertura | a empresa, RAYZER X; só o X quando recolhido |
| `XAcessLogo` / `RayzerAppIcon` | barra superior, assistente, barra lateral | o produto: Xacess, com tagline, com "by RAYZER X" ou ícone |
| `RayzerAbertura` | ao abrir o painel | fluxos → pulso → X → marca e produto, 2,9 s, nunca bloqueia |
| `RayzerFlowBar` | abaixo da barra superior | carregando |
| Cartão de catraca | painel ao vivo, catracas | CATRACA 01 · situação · nome · último evento · linha técnica · Diagnóstico |
| Estilos | `Controles.xaml` | botão (principal, secundário, fantasma, ícone), campo, senha, lista, data, caixa de marcar, tabela, rolagem, menu lateral, passos, dica |

Os nomes do briefing sem função no produto hoje não foram criados como abstração vazia: `RayzerModal`, `RayzerDrawer`, `RayzerToast`, `RayzerCommandPalette`, `RayzerPagination` e `RayzerAvatar`. Estão na seção 11.

## 6. Onde a marca foi aplicada

- **Janela:** barra lateral em azul profundo com a empresa (RAYZER X) no alto e o produto (XAcess) logo abaixo, os ícones, a linha de energia ciano no item
  ativo, o selo permanente "MODO SIMULAÇÃO", o tema claro/escuro, o menu recolhível e a
  autoria. O menu recolhe sozinho abaixo de 1200 px. A barra superior segue STATUS →
  EXCEÇÃO → CONTEXTO → AÇÃO: situação com símbolo, detalhe da internet, hora de Brasília,
  e o botão do assistente quando falta configuração.
- **Painel ao vivo:** o centro de comando. Métricas (autorizados, passagens, negados,
  fluxo em 5 min, fila da nuvem), cartões de catraca e acessos em tempo real.
- **Demais telas:** cabeçalho com título e contexto, filtros em cartão com campos
  alinhados, tabelas com situação por símbolo, estados vazios e mensagens em RayzerAlert.
- **Assistente:** cabeçalho da marca, passos com a linha do fluxo e verificação do
  ambiente em pílulas ✓/×/!.
- **Instalador:** nome Rayzer XAcess, ícone em "Aplicativos instalados", logo no Setup,
  pasta `C:\Program Files\Rayzer\XAcess`, atalhos e serviço "Rayzer XAcess — …",
  fabricante Rayzer Serviços e Tecnologia.

## 7. Sistema verbal

Mensagens claras, precisas, humanas e operacionais: **o que aconteceu, desde quando, o
que fazer.**

| Evitar | Usar |
|---|---|
| "Erro inesperado." | "Sem resposta do serviço local — abra o Assistente de configuração para iniciá-lo" |
| "Offline" | "Sem notícia do programa da catraca" + "Último evento: … · há 1 min" + [Diagnóstico] |
| "No data" | "Aguardando o primeiro acesso — cada leitura nas catracas aparece aqui na hora" |
| "Error 8" | "Itens com × impedem as catracas de funcionar. Instale o que faltar e clique em Verificar de novo." |

Português de portaria, sem jargão (ver `docs/GLOSSARIO.md` e `Textos.cs`).

## 8. Acessibilidade (WCAG 2.2 AA)

- **Contraste:** cada par texto/fundo tem pelo menos 4,5:1, e borda de campo, foco e
  símbolo da marca pelo menos 3:1, nos temas claro e escuro. O teste
  `Texto_e_situacao_passam_no_contraste_WCAG_AA` calcula cada par.
- **Situação:** nunca só por cor.
- **Foco:** anel de foco visível e próprio em todo controle interativo; navegação por
  teclado no menu e nas telas; teclas de acesso sublinhadas (`_Buscar`).
- **Leitores de tela:** `AutomationProperties.Name` em campos e tabelas, e
  `LiveSetting="Assertive"` na situação da operação.
- **Alvo de toque:** menu com 44 px; campos e botões com 40 px.
- **Alto contraste do Windows:** respeitado, pelo tema AltoContraste.
- **Movimento reduzido:** respeitado.

## 9. Responsividade

É um aplicativo Windows de sala de controle (mínimo de 1366 × 768, docs/10):

- **Desktop XL / desktop:** gestão completa.
- **Notebook / janela estreita:** abaixo de 1200 px, o menu vira só ícones; as grades se
  ajustam.
- **Tablet Windows (toque):** alvos de 40–44 px e rolagem por toque. Operação e gestão.
- **Mobile:** não existe aplicativo móvel. É proposta futura (seção 11): monitoramento,
  alertas, situação e consulta.

## 10. Performance

- Nenhuma biblioteca de interface de terceiros e nenhuma fonte baixada: só a fonte do
  sistema.
- Pincéis congelados (`po:Freeze`).
- Sombra só em superfície flutuante.
- Uma única animação contínua (a barra de carregamento), e só enquanto carrega.
- A troca de tema substitui um dicionário, sem recriar janela.

## 11. Propostas futuras e melhorias recomendadas

Nada abaixo existe no produto hoje (docs/24 §2 e etapa 23 do briefing). Nenhuma tela finge
essas funções.

| Item | Classificação | Observação |
|---|---|---|
| Login e perfis de operador | PROPOSTA FUTURA | Hoje o painel é local e protegido pelo token da máquina e pela ACL do canal. Login pede usuários, papéis e auditoria |
| Aplicativo móvel / web de monitoramento | PROPOSTA FUTURA | Exige API pública segura — nunca o banco na rede das catracas |
| Pessoas e credenciais (foto, titular, tipo, validade) | MELHORIA RECOMENDADA — fase 3 | Modelo de planilha em docs/26 |
| Gerenciar catraca (visor, relógio, relés) | MELHORIA RECOMENDADA — fase 4 | Só o documentado pela Topdata; o resto `A_CONFIRMAR_COM_TOPDATA` |
| Notificações, toasts, som e histórico de alertas | MELHORIA RECOMENDADA — fase 5 | RayzerAlert é a base |
| Relatórios R1–R8 com PDF | MELHORIA RECOMENDADA — fase 6 | docs/25 |
| IP da catraca no cartão do dispositivo | A_CONFIRMAR | O serviço não informa o IP hoje; não se inventa |
| Paleta de comandos, gaveta, paginação | PROPOSTA FUTURA | Só quando houver volume de telas que peça |
| Calendário do seletor de data no tema escuro | LIMITAÇÃO CONHECIDA | O calendário que abre é o do Windows, claro nos dois temas; o campo segue o tema |

## 12. Teste de marca

Sem o logo, a tela ainda é Rayzer?

- A barra lateral em azul profundo com a linha de energia em Ciano.
- O Rayzer Blue (#0066FF) só na ação principal.
- As pílulas de situação ✓ × ● ○ ! ↻.
- O traço que atravessa a linha ao carregar.
- Os cartões de raio 12 sobre fundo cinza-azulado.
- A voz "o que aconteceu, desde quando, o que fazer".

Essas escolhas se repetem no painel, no assistente e no instalador.

## 13. Validação

- **Testes:**
  - `RayzerDesignTests`: temas completos, contraste, chaves existentes e nenhuma cor solta.
  - `LigacoesDasTelasTests`: toda ligação existe, e nenhuma usa `Count` invisível ao WPF.
  - `ArquiteturaTests`: dependências do design system.
- **CI (Windows):**
  - autoteste do painel com o serviço em simulação, passando pelas 9 telas nos dois temas
    e liberando um QR de teste;
  - autoteste do assistente;
  - instalação real, abertura do painel e desinstalação com ele aberto;
  - capturas das telas nos dois temas, publicadas em `capturas-das-telas.zip` no
    pré-lançamento `instalador-de-teste`.
