# 54. Auditoria de tema e estados (E4, parte A5)

**Entregável:** parte "Tema e estados" do E4 do `docs/46-prompt-analise-front-end.md` (§4, papel A5; critérios §7 e §8),
feita pelo agente A5. É a peça que o `docs/50` aguarda para a linha "Tema e estados".

**Base:** branch `tarefa/prompt-analise-front-end`, commit `41d2e65`. Só leitura de código: nenhum `.cs`, `.xaml`
ou `.proto` foi alterado. Nada foi executado: não rodei o aplicativo, não há captura e não há resultado de teste.

**Árvore usada:** a aprovada do `docs/46` §6 (20 itens folha). Onde a tela de hoje ainda não é a da árvore, a tabela
diz qual tela atual a cobre.

**Classificação** (docs/46 §7), com o sentido que tem aqui:

- **APROVADO:** verificado lendo o código; o arquivo e a linha estão citados.
- **PENDENTE:** depende de ver a tela no Windows (cor real, contraste, quebra de linha, duração do estado).
- **BLOQUEADO:** a tela não existe ou depende de decisão do dono.
- **REPROVADO:** verificado no código e não atende ao P8, P5 ou P10.

**Critério do P10 usado nesta auditoria** (escrito aqui para ser contestado): o texto do próprio estado vazio precisa dizer
o que o operador faz a seguir (uma ação, um lugar ou "aguarde" com o motivo). Texto que só descreve o que vai aparecer
("O histórico aparece aqui") não tem próximo passo. Um cabeçalho de página que explica a tela não substitui o estado vazio.

---

## 1. Resumo em números

| Item | Valor | Situação |
|---|---|---|
| Cores literais (`#RGB`, `#RRGGBB`, `#AARRGGBB`) em `.xaml` e `.cs` de `src/Desktop.App` | **0** | APROVADO |
| Nomes de cor (`Red`, `Gray`...), `Brushes.*`, `Colors.*`, `Color.FromRgb` em `src/Desktop.App` | **0** | APROVADO |
| Cores em `Desktop.App` que vêm de texto hexadecimal fora dos tokens (cena 3D, `fit4.json`) | **1 tela** (Configuração da catraca, hoje `Gemeo`), 42 valores | REPROVADO (P8) |
| Fontes literais (`FontFamily` com nome fixo) | **1** (`Consolas`, `Gemeo.xaml.cs:609`) | REPROVADO (P8) |
| Tamanhos de letra numéricos fixos (`FontSize="15"`...) | **10** em 6 arquivos | PENDENTE (decisão, §2.3) |
| Chaves `Rayzer.*` usadas por `Desktop.App` e não definidas | **0** de 82 | APROVADO |
| Chaves de cor iguais nos 3 temas (`Claro`, `Escuro`, `AltoContraste`) | 58 = 58 = 58, conjuntos idênticos | APROVADO |
| Telas com tema que não troca por inteiro | **1** (Configuração da catraca, a cena 3D) | REPROVADO (P8) |
| Itens da árvore sem estado vazio com próximo passo | **8 de 20** (9 telas ou painéis de hoje); 2 sem texto algum, 6 com texto sem passo | REPROVADO (P10) |
| Itens da árvore que ainda não existem como tela | **5 de 20** | BLOQUEADO |
| Estados "simulação" e "sem internet" no mesmo lugar em todas as telas | Lugar: sim. Tom do "sem internet": não | Simulação APROVADO; sem internet REPROVADO (P5) |

---

## 2. Cores e fontes literais (P8)

### 2.1 Tokens do `Rayzer.Design`

Fonte: `src/Rayzer.Design/Temas/Escuro.xaml`, `Claro.xaml`, `AltoContraste.xaml` e `Tokens.xaml`.

| Valor esperado no briefing | Onde está de fato | Situação |
|---|---|---|
| `#0066FF` | `Rayzer.Brand.Primary` em Claro e Escuro (`Claro.xaml:51`, `Escuro.xaml:52`) | APROVADO |
| `#00D5FF` | `Rayzer.Brand.Cyan` em Claro e Escuro (`Claro.xaml:65`, `Escuro.xaml:66`) | APROVADO |
| `#081220` | **Não existe em nenhum arquivo do repositório.** O fundo do tema escuro é `#0B1A33` (`Escuro.xaml:14`); a barra lateral é `#07101F` (`Escuro.xaml:104`) | divergência a esclarecer (§2.4) |
| `#111827` | `Rayzer.Surface` no escuro (`Escuro.xaml:16`) e `Rayzer.Graphite` nos dois (`Claro.xaml:67`, `Escuro.xaml:68`) | APROVADO |
| `#32D583` | `Rayzer.Success` e `Access.Granted` no escuro (`Escuro.xaml:72`) | APROVADO (só no escuro) |
| `#F5A524` | `Rayzer.Warning` no escuro (`Escuro.xaml:76`) | APROVADO (só no escuro) |
| `#F05252` | `Rayzer.Danger` no escuro (`Escuro.xaml:80`) | APROVADO (só no escuro) |

No tema claro, sucesso, atenção e perigo são outros valores, escurecidos para dar contraste sobre branco:
`#0F7A45`, `#8A5300` e `#C8283A` (`Claro.xaml:71,75,79`). No alto contraste, `Success`, `Warning` e `Danger` valem todos
`SystemColors.WindowTextColor` (`AltoContraste.xaml:70,74,78`): a diferença entre eles passa a ser só o símbolo (✓, !, ×) e o
texto. O código já prevê isso (`Conversores.cs:9-11`: "o símbolo e o texto vão juntos").

**Outros tokens, além dos pedidos:** `Escuro.xaml` tem 41 valores hexadecimais distintos e `Claro.xaml` 38, em 58 chaves por
tema. Além dos sete acima, os que se destacam: `#0B1A33` (fundo escuro, azul institucional), `#F4F7FB` (fundo claro),
`#8A9BB0` (texto de apoio e neutro), `#33A0FF` (anel de foco no escuro), `#4DB8FF` (informação e "sincronizando"),
`#172235`, `#0D1526`, `#22304A` (superfícies e bordas). `Tokens.xaml` guarda mais 16 valores fixos que **não trocam com o
tema** (gradientes da marca e do ícone, sombras `DropShadowEffect`, brilho azul): são da marca, ficam dentro do
`Rayzer.Design` e portanto não violam o P8, mas valem como observação para o alto contraste (§3).

### 2.2 Ocorrências em `src/Desktop.App`

Busca feita em todos os `.xaml` e `.cs` (fora de `obj/` e `bin/`).

| Busca | Resultado | Situação |
|---|---|---|
| `#` seguido de 3 a 8 dígitos hexadecimais | 0 ocorrências | APROVADO |
| Atributos `Foreground`, `Background`, `Fill`, `Stroke`, `BorderBrush`, `Color`, `Brush` com valor que não seja `{DynamicResource ...}`, `{StaticResource ...}`, `{Binding ...}` ou `{TemplateBinding ...}` | 0 ocorrências | APROVADO |
| Nome de cor em XAML (`Red`, `Blue`, `Gray`...) | 0 ocorrências | APROVADO |
| `Brushes.`, `Colors.`, `Color.FromRgb`, `SystemColors.` em `.cs` | 0 ocorrências | APROVADO |
| `SolidColorBrush` e `Color.FromArgb` criados em código | 13 pontos, **todos em `Gemeo.xaml.cs`** (abaixo) | REPROVADO (P8) |
| `FontFamily` com nome fixo | 1 ocorrência (abaixo) | REPROVADO (P8) |

**Cor que entra pelo código (REPROVADO, P8).** O arquivo `src/Desktop.App/Telas/Gemeo.xaml.cs` desenha a catraca em 3D
com cores lidas como texto hexadecimal de `src/Desktop.ViewModels/GemeoDigital/fit4.json` (42 valores, linhas 39 a 126:
acabamentos da carcaça, luzes, piso, sombra `#90000000`, display aceso `#104EA8`, QR `#FFFFFF`/`#000000`). A conversão é
`Cor(string hex)` em `Gemeo.xaml.cs:382-386`; o uso, nas linhas abaixo. Nenhum desses valores é token do `Rayzer.Design`.

| Arquivo:linha | O que cria |
|---|---|
| `Gemeo.xaml.cs:295-298` | luz ambiente e as 3 luzes direcionais da cena |
| `Gemeo.xaml.cs:300` | cor do piso |
| `Gemeo.xaml.cs:305-306` | sombra de contato (a mesma cor com transparência 0) |
| `Gemeo.xaml.cs:382-386` | `Cor(...)`: texto hexadecimal para `Color` |
| `Gemeo.xaml.cs:397-399` | cor e brilho de cada acabamento da catraca e da pessoa |
| `Gemeo.xaml.cs:409-410` | material fosco (difuso e especular) |
| `Gemeo.xaml.cs:417-418` | fundo e tinta do QR da tela do celular |
| `Gemeo.xaml.cs:590-591` e `610-625` | fundo e tinta do display de 2 linhas, mais a transparência fixa `0x22` da célula |
| `Gemeo.xaml.cs:804-805` e `812-815` | material aceso e realce |

O que **já usa o tema** na mesma cena: `CorDoTema(...)` (`Gemeo.xaml.cs:389-390`) busca no tema as cores de realce
(`Rayzer.Brand.Cyan`, `Rayzer.Warning`, `Rayzer.Info`, `Rayzer.Access.Granted`, `Rayzer.Access.Denied`), nas linhas 526, 565,
651, 659, 673 e 791. Ou seja, o realce das peças segue o tema; o corpo da catraca, o piso, as luzes e o display não.

**Decisão que cabe ao dono (BLOQUEADO):** a cena 3D representa um objeto físico (a catraca TopFit 4) com acabamentos reais.
O P8 diz "nenhuma cor literal", sem exceção escrita. Ou a cena passa a derivar das cores do tema, ou o P8 ganha uma exceção
escrita para a cena 3D. Enquanto isso não for decidido, a cena conta como REPROVADA.

**Fonte literal (REPROVADO, P8).** `Gemeo.xaml.cs:609`:
`new Typeface(new FontFamily("Consolas"), ...)`. É o texto do display de 2 linhas que vira imagem da cena. O token
`Rayzer.Font.Mono` existe (`Tokens.xaml:24`: JetBrains Mono, depois Cascadia Mono, depois Consolas) e é o que as demais telas usam.

**Fontes por token, conferidas (APROVADO).** Nenhum outro `FontFamily` com nome fixo. Os usos de `FontFamily` em XAML apontam
para `Rayzer.Font.Display` (`JanelaPrincipal.xaml:42`) e `Rayzer.Font.Mono` (`Simulador.xaml:18`, `Consulta.xaml:12`,
`Gemeo.xaml:342-343`). `CapturaDeTela.cs:553` usa `SetResourceReference` para `Rayzer.Font.Text`.

### 2.3 Tamanhos de letra fixos (PENDENTE, decisão)

O P8 fala em cor e em fonte. Tamanho numérico fixo não é citado, mas escapa da escala `Rayzer.FontSize.*`
(`Tokens.xaml:29-37`: 32, 20, 26, 20, 15, 14, 13, 12, 11). São 10 ocorrências:

| Arquivo:linha | Valor | Token equivalente |
|---|---|---|
| `JanelaPrincipal.xaml:42` | 15 | `Rayzer.FontSize.Section` |
| `JanelaPrincipal.xaml:147` | 18 (hora do evento) | **nenhum** |
| `JanelaPrincipal.xaml:165` | 13 (frase de estado) | `Rayzer.FontSize.Small` |
| `Telas/Contas.xaml:34` | 14 (ícone) | `Rayzer.FontSize.Body` |
| `Telas/Sincronizacao.xaml:15` | 15 | `Rayzer.FontSize.Section` |
| `Telas/Gemeo.xaml:342`, `343` | 16 (prévia do display) | **nenhum** |
| `App.xaml:162`, `171`, `179` | 13 (ícones dos botões do cartão da catraca) | `Rayzer.FontSize.Small` |

Oito dos dez têm token igual. Dois (18 e 16) não têm. Classificação: PENDENTE, até o dono dizer se o P8 cobre tamanho.

### 2.4 Divergência do briefing

O briefing citava `#081220` como um dos tokens. Esse valor não aparece em `src/`, em `docs/` nem em arquivos `.json`.
O escuro usa `#0B1A33` como fundo e `#07101F` na barra lateral. Provável engano de digitação do briefing; a lista de
tokens acima é a do código. Se `#081220` for um valor novo de marca, ele ainda não existe no `Rayzer.Design`.

---

## 3. Claro, escuro e alto contraste

### 3.1 Como o tema troca (verificado no código)

| Verificação | Evidência | Situação |
|---|---|---|
| O tema é um dicionário de recursos trocado em tempo de execução | `TemaRayzer.Reaplicar()` substitui o dicionário em `MergedDictionaries` (`TemaRayzer.cs:115-132`) | APROVADO |
| Alto contraste do Windows tem prioridade sobre a preferência | `Resolver()` devolve `AltoContraste` se `SystemParameters.HighContrast` (`TemaRayzer.cs:136-141`) | APROVADO |
| A mudança de alto contraste em execução é ouvida | `TemaRayzer.cs:82` trata `PropertyChanged` de `HighContrast` | APROVADO |
| Os três temas definem as mesmas chaves | 58 chaves em cada; `diff` das listas sem diferença | APROVADO |
| Toda cor de `Desktop.App` é recurso dinâmico | 0 `StaticResource` apontando para chave de cor; as únicas referências estáticas são estilos, margens, raios e fontes (busca por `StaticResource Rayzer.(Background\|Surface\|Border\|Text\|Brand\|Success\|Warning\|Danger...)` só acha o estilo `Rayzer.Nav.List`, que não é cor) | APROVADO |
| O estilo dos controles do `Rayzer.Design` também usa cor dinâmica | busca em `Controles.xaml` e `Themes/Generic.xaml`: nenhum `StaticResource` de cor, nenhum hexadecimal | APROVADO |
| Toda chave `Rayzer.*` referenciada existe | 82 chaves distintas em `Desktop.App`, 0 faltando | APROVADO |
| O rótulo do botão de tema acompanha | `JanelaPrincipal.xaml.cs:216-221` (`MostrarTema`) troca texto e ícone | APROVADO |

Uma chave de cor inexistente em `DynamicResource` não dá erro: o elemento fica sem pincel, invisível. A checagem das 82
chaves fecha esse risco no código.

### 3.2 Tela a tela: o tema troca?

Critério: a tela usa só `DynamicResource` de token, sem valor fixo. "Troca" aqui quer dizer *pelo código*; a aparência de
cada tema continua PENDENTE até ver no Windows.

| Item da árvore | Tela de hoje | Usa só token dinâmico? | Onde o tema não se aplica |
|---|---|---|---|
| Barra superior e lateral (todas as telas) | `JanelaPrincipal.xaml` | sim (25 usos de `DynamicResource Rayzer.*`) | nenhum no XAML |
| Painel ao vivo | `PainelAoVivo.xaml` | sim | nenhum |
| Acessos | `Acessos.xaml` | sim | nenhum |
| Consultar código | `Consulta.xaml` | sim | nenhum |
| Catracas | `Catracas.xaml` | sim | nenhum |
| Configuração (desenho) | `Gemeo.xaml` + `Gemeo.xaml.cs` | **não por inteiro** | **a cena 3D**: corpo, piso, luzes, sombra, display e QR vêm do `fit4.json`; a cena é montada uma vez em `Gemeo.xaml.cs` (a tela não ouve `TemaRayzer.Mudou`), então trocar o tema com a tela aberta não repinta o corpo da catraca. O palco em volta (`Rayzer.Surface.Sunken`, `Gemeo.xaml:88`) troca. O piso `#1C222E` e a sombra `#90000000` ficam escuros também no tema claro |
| Configuração (lista) | `Parametrizacao.xaml`, `CampoDaCatraca.xaml` | sim | nenhum |
| Giro | `MapaDeGiro.xaml` | sim (9 usos) | nenhum |
| Gerenciar | `GerenciarCatraca.xaml` | sim | nenhum |
| Pessoas | `Pessoas.xaml` | sim | nenhum |
| Cadastro | `ParametrosDoCadastro.xaml` | sim | nenhum |
| Prestação de contas | `Contas.xaml` | sim | nenhum |
| Sincronização | `Sincronizacao.xaml` | sim | nenhum |
| Configurações do evento | `Configuracoes.xaml` | sim | nenhum |
| Diagnóstico | `Diagnostico.xaml` | sim | nenhum |
| Usuários e papéis | `Usuarios.xaml` | sim | nenhum |
| Simulador | `Simulador.xaml` | sim | nenhum |
| Novidades | `JanelaDeNovidades.xaml` | sim | nenhum |
| Entrar / trocar senha | bloco `Login` de `JanelaPrincipal.xaml:181-221` | sim | nenhum |
| Leituras recusadas, Visão geral, Cartões, Trocar minha senha, Ajuda | não existem como tela | n/a | n/a |

**Total de telas cujo tema não troca por inteiro: 1** (Configuração da catraca, na parte da cena 3D). Situação: REPROVADO (P8).
O restante das telas existentes: APROVADO pelo código, aparência PENDENTE.

Dois limites que não são violação do P8, mas pedem olho no Windows (PENDENTE):

1. **Alto contraste e ícones da marca.** `Tokens.xaml` mantém gradientes e brilho da marca (16 valores fixos) que não
   viram `SystemColors`. O logotipo e o ícone do produto continuam coloridos em alto contraste. É padrão de marca, mas confira
   se o contorno e o texto ao lado continuam legíveis.
2. **Sucesso, atenção e perigo iguais no alto contraste.** As três cores viram a mesma (`WindowText`). A leitura depende do
   símbolo e do texto. Os componentes `RayzerStatus` trazem os dois (`Componentes.cs:79`); os textos coloridos soltos
   (`Rayzer.Danger` em `CampoDaCatraca.xaml:42` e `MapaDeGiro.xaml:58`; `Rayzer.Warning` em `MapaDeGiro.xaml:72` e
   `App.xaml:139`, relógio divergente) dependem do texto da frase, que existe, mas sem símbolo.

### 3.3 Verificação que falta (PENDENTE)

Contraste real de cada par texto/fundo nos três temas, aparência do alto contraste com o tema do Windows ativo, e o
comportamento da cena 3D ao trocar de tema com a tela aberta. Tudo depende de abrir o aplicativo no Windows (CI ou dono).

---

## 4. Estados de cada tela

Os seis estados e o que existe no código:

| Estado | Mecanismo geral | Onde | Situação |
|---|---|---|---|
| **Carregando** | `TelaBase.Tentar` liga `Ocupada` (`Telas.cs:46-65`); a barra `RayzerFlowBar` mostra um traço enquanto `TelaAtual.Ocupada` (`JanelaPrincipal.xaml:172`); sem animação, fica parada e cheia (`Componentes.cs:516-520`). Antes da primeira resposta: "Conectando ao serviço local…" (`EstadoDoPainel.cs:65-69`) | todas as telas, no mesmo lugar | APROVADO (código); duração e aparência PENDENTE |
| **Vazio** | `RayzerEmptyState` com `ZeroParaVisivel` (`Conversores.cs:104-123`), em 8 pontos de 7 telas; as demais listas não têm | por tela, §4.1 | ver tabela |
| **Erro** | `TelaBase.Mensagem` mostrada num `RayzerAlert` no alto de cada tela; `Tentar` pega só `RpcException` e traduz em `MensagemDeFalha.Para` (`MensagemDeFalha.cs:36-54`). Na barra superior, a frase `Sem resposta do serviço local…` com tom Perigo (`EstadoDoPainel.cs:150-166`) | por tela e na barra | ver §4.2 |
| **Sem permissão** | O menu esconde a tela que o papel não abre (`JanelaViewModel.Permitida`, `Telas.cs:1525-1528`). Ação negada volta como `Seu papel não permite: {nome}.` (`InterceptadorDeSessao.cs:190`) dentro do alerta da tela. Conta do Windows fora do grupo: texto próprio, que diz quem libera (`MensagemDeFalha.cs:24-26`) | menu, alerta da tela, barra | ver §4.3 |
| **Simulação** | Ver §5 | barra superior, barra lateral, cartão da catraca | ver §5 |
| **Sem internet** | Ver §5 | barra superior | ver §5 |

### 4.1 Estado vazio, tela a tela (P10)

Cada linha é um item da árvore do `docs/46` §6. "Existe estado vazio" é o elemento na tela; "próximo passo" segue o
critério escrito no início deste documento.

| # | Item da árvore | Tela de hoje | Existe estado vazio? | Texto no código | Próximo passo? | Situação |
|---|---|---|---|---|---|---|
| 1 | Painel ao vivo | `PainelAoVivo.xaml:53,96` | sim, 2 | Sem catraca: título `Nenhuma catraca configurada`, texto `Abra o Assistente de configuração e informe o número do Inner de cada catraca.` (`Textos.cs:194-199`). Com o serviço parado: `As catracas aparecem quando o serviço local responder. Veja o aviso no topo da janela.` Sem acesso: `Aguardando o primeiro acesso` + `Cada leitura nas catracas aparece aqui na hora...` + observação sobre o Simulador | sim: ação, lugar ou "aguarde" com o motivo | APROVADO |
| 2 | Acessos | `Acessos.xaml:130` | sim | `Nenhum acesso para mostrar` / `Ajuste os filtros e clique em Buscar. Sem filtro, aparecem os 500 mais recentes.` O alerta `Nenhum acesso com esses filtros.` (`Telas.cs:553`) aparece ao mesmo tempo, repetindo | sim | APROVADO (texto duplicado) |
| 3 | Leituras recusadas | não existe | n/a | nenhuma tela com esse nome (busca por "recusad", "reconhecid" só acha textos de outras telas) | n/a | BLOQUEADO |
| 4 | Consultar código | `Consulta.xaml:55` | sim | `Nenhuma consulta ainda` / `O histórico do código consultado aparece aqui.` O passo está só no cabeçalho da página (`Consulta.xaml:8`), não no estado | **não** | **REPROVADO (P10)** |
| 5 | Catracas | `Catracas.xaml:21` | sim | O mesmo texto do Painel (`SemCatracas`); o alerta `Nenhuma catraca cadastrada na instalação.` (`Telas.cs:445`) repete ao mesmo tempo | sim | APROVADO (texto duplicado) |
| 6 | Visão geral (por catraca) | não existe | n/a | n/a | n/a | BLOQUEADO |
| 7 | Configuração (por catraca) | `Gemeo.xaml` e `Parametrizacao.xaml` | só mensagem, sem elemento de estado | Gêmeo sem catraca: `Nenhuma catraca cadastrada: a demonstração funciona; o modo ao vivo precisa de uma catraca.` (`GemeoDigitalViewModel.cs:668`). Parametrização sem catraca: `Nenhuma catraca cadastrada na instalação.` (`Parametrizacao.cs:882`) e formulário vazio. Os dois não dizem como cadastrar uma catraca (o caminho é o Assistente). Já `Nenhuma alteração. Clique numa peça e mude um parâmetro...` (`CentralDaCatraca.cs:246`) e `Nenhuma alteração. Mude um campo...` (`Parametrizacao.cs:667`) têm passo | **não** (na falta de catraca) | **REPROVADO (P10)** |
| 8 | Giro | `MapaDeGiro.xaml` (painel dentro de 7) | só frase de resumo | `Nenhuma alteração no giro.` (`MapaDeGiro.cs:406`), sem dizer o que mudar; sem catraca, o painel mostra só o texto de introdução | **não** | **REPROVADO (P10)**, leve |
| 9 | Gerenciar | `GerenciarCatraca.xaml:185` | sim, no histórico; mensagem para "sem catraca" | Histórico: `Nenhum pedido para esta catraca` / `O que for pedido aqui aparece nesta lista, com quem pediu e o que a catraca respondeu.` Sem catraca: `Nenhuma catraca cadastrada na instalação.` (`GerenciarCatraca.cs:373`) | **não** nos dois | **REPROVADO (P10)** |
| 10 | Pessoas | `Pessoas.xaml:40,199,244` | **não**: a lista de pessoas, a de credenciais e a de importações são `DataGrid` sem estado vazio | Busca sem resultado deixa a tabela em branco, sem texto algum (`Pessoas.cs:566-576` não escreve mensagem). Instalação sem ninguém cadastrado também fica em branco | **não** | **REPROVADO (P10)**, sem estado |
| 11 | Cartões (lotes) | não existe | n/a | n/a | n/a | BLOQUEADO |
| 12 | Cadastro (empresas, salas, horários, feriados, perfis) | `ParametrosDoCadastro.xaml:28,100,112,173,203` | **não**: 5 `DataGrid` sem estado vazio | Nenhum texto quando não há empresa, sala, horário ou feriado cadastrado | **não** | **REPROVADO (P10)**, sem estado |
| 13 | Prestação de contas | `Contas.xaml` | só mensagem; 4 tabelas sem estado | `Nenhuma tentativa no período.` (`Telas.cs:844`) em alerta neutro; as quatro tabelas (categoria, catraca, hora, motivos) ficam só com cabeçalho. Sem sugestão de ampliar o período | **não** | **REPROVADO (P10)**, sem estado |
| 14 | Sincronização | `Sincronizacao.xaml:56` | sim | `Nenhuma origem cadastrada` / `As origens aparecem quando a nuvem envia os primeiros cartões ou ingressos.` Descreve a condição, não diz o que fazer (por exemplo, configurar a nuvem) | **não** | **REPROVADO (P10)** |
| 15 | Configurações do evento | `Configuracoes.xaml` | n/a: é formulário, sem lista | `Problemas` e `Mensagem` cobrem erro e resultado | n/a | não se aplica |
| 16 | Diagnóstico | `Diagnostico.xaml:55` | não, mas a lista só fica vazia com o serviço parado | Nesse caso o alerta de erro traz o passo (`chame o suporte`, `MensagemDeFalha.cs:29-30`) | sim, pelo alerta | APROVADO (ressalva) |
| 17 | Usuários e papéis | `Usuarios.xaml:29,85` | não | As listas nunca são vazias na prática: o administrador instalado e os papéis do sistema existem | n/a | não se aplica na prática |
| 18 | Trocar minha senha | não existe como tela (só a troca obrigatória no login) | n/a | n/a | n/a | BLOQUEADO |
| 19 | Simulador | `Simulador.xaml:57` | sim | `Nenhuma passagem simulada` / `Escolha um código de teste ao lado e clique em Passar.` | sim | APROVADO |
| 20 | Novidades e ajuda | `JanelaDeNovidades.xaml` (janela que abre uma vez) | n/a | a "Ajuda" não existe | n/a | BLOQUEADO (conteúdo) |

**Contagem:**

- Existem como tela e têm estado vazio com próximo passo: 4 itens (1, 2, 5, 19), mais o 16 com ressalva.
- **Sem estado vazio com próximo passo: 8 itens da árvore** (4, 7, 8, 9, 10, 12, 13, 14), que somam **9 telas ou painéis de
  hoje** (Gêmeo e Parametrização são as duas telas do item 7). Desses, **2 não têm texto algum** (10 e 12) e
  **6 têm texto sem passo** (4, 7, 8, 9, 13, 14).
- Não existem como tela: 5 itens (3, 6, 11, 18, 20), BLOQUEADOS pela decisão do dono (`docs/48` §5) ou pela construção.
- Não se aplicam: 15 e 17.

**Limites verificados em todas as listas.** `ZeroParaVisivel` só olha a contagem da lista (`Conversores.cs:104-123`). Não
existe uma bandeira "carregando" que segure o estado vazio. Logo, nos primeiros instantes de cada tela (antes da resposta do
serviço), o texto de vazio aparece mesmo havendo dados a chegar: por exemplo, "Nenhuma catraca configurada" na tela Catracas,
porque `ServicoRespondeu` começa verdadeiro (`Telas.cs:418`). Quanto tempo isso dura é PENDENTE (depende de ver no Windows);
que o código não o impede está verificado.

### 4.2 Erro

| Verificação | Evidência | Situação |
|---|---|---|
| Falha de chamada vira texto na tela, nunca exceção | `TelaBase.Tentar` (`Telas.cs:46-65`); `PainelViewModel.AtualizarAsync` (`PainelViewModel.cs:75-92`) | APROVADO |
| Erro tem texto com próximo passo no caso mais comum | `MensagemDeFalha.SemResposta` (`:29-30`): reiniciar sozinho, "chame o suporte em um minuto" | APROVADO |
| Erro de outro código mostra o nome do código gRPC | `$"O serviço recusou o pedido ({codigo})."` (`MensagemDeFalha.cs:53`), sem próximo passo e com termo técnico (isto é do A3) | REPROVADO (P10) |
| Só `RpcException` é capturada | qualquer outra exceção dentro de `Tentar` não vira mensagem; fora de `RpcException` o código não trata. Quanto isso acontece de fato é PENDENTE | PENDENTE |
| O erro é mostrado com cor de erro | O alerta de mensagem de todas as telas usa `Tom="Neutro"` (`App.xaml:53-56`); Simulador, Gerenciar, Parametrização, Giro e Gêmeo trocam para `Tom="Marca"`. Erro e resultado de ação ("Gravado.", "Pedido à catraca...") usam o **mesmo** alerta e o mesmo tom. Só a barra superior usa Perigo | **REPROVADO (P5)**: o erro de uma tela aparece com tom neutro ou de marca, igual a uma confirmação |
| Mensagem e alerta ficam no mesmo lugar | `RayzerAlert` com `Pagina.Mensagem` logo abaixo do cabeçalho da página em 11 telas | APROVADO |

### 4.3 Sem permissão (ligação com o A6 e o P9)

| Verificação | Evidência | Situação |
|---|---|---|
| O menu não mostra a tela fora do papel | `TelasDoMenu` filtra por `Permitida` (`Telas.cs:1522-1528`) | APROVADO |
| Dentro da tela, ação negada mostra texto do serviço | `Seu papel não permite: {nome}.` com nome amigável da permissão (`InterceptadorDeSessao.cs:188-190`), exibido no alerta neutro da tela | APROVADO (o texto existe) |
| O texto diz **quem libera** | A frase para na permissão que falta; não diz "peça ao administrador". O P9 pede "a tela diz quem libera" | **REPROVADO (P9)** |
| Conta do Windows sem acesso ao serviço | `MensagemDeFalha.SemPermissao`: "Peça ao administrador do computador para incluir você no grupo..." (`MensagemDeFalha.cs:24-26`) | APROVADO |
| Dado mascarado por papel tem estado próprio | `Pessoas.xaml:59-60` mostra o alerta "Documento e contato aparecem mascarados: o seu papel não permite vê-los. Gravar a ficha mantém o que está guardado." É a **única** tela com estado de permissão explícito | APROVADO (só em Pessoas) |
| Há caminho que abre uma tela sem passar pelo filtro do menu | Os botões do cartão da catraca chamam `Gerenciar`, `VerAcessos`, `AbrirDiagnostico`, `Parametrizar` e `AbrirNoGemeo` e trocam `TelaAtual` direto (`Telas.cs:1392-1480`). `AbrirDiagnostico` abre o Diagnóstico sem conferir `DiagnosticoVer`; `Parametrizacao` não está em `PermissaoDaTela` (`Telas.cs:1312-1327`: "tela fora do mapa: basta estar logado"). Nesses casos a tela abre e a negação aparece depois, como alerta neutro | **REPROVADO (P9)**: o menu esconde, mas o atalho não. A confirmação com papel real é PENDENTE (A6/A8) |

---

## 5. Simulação e sem internet: aparecem no mesmo lugar? (P5)

### 5.1 A barra superior é a mesma em todas as telas

A barra (`JanelaPrincipal.xaml:115-169`) e a barra lateral ficam **fora** do `ContentControl` que troca de tela
(`JanelaPrincipal.xaml:174`). Valem igualmente para os 14 itens do menu, para a Parametrização (aberta fora do menu) e
para qualquer tela futura. Só o bloco de login a cobre (`JanelaPrincipal.xaml:181`).

| Verificação | Situação |
|---|---|
| Os blocos "Serviço local", "Catracas", "Nuvem" e "Modo simulação" são os mesmos em toda tela | APROVADO (código) |
| A frase de situação (`RayzerStatus`, `JanelaPrincipal.xaml:165-167`) é a mesma em toda tela | APROVADO (código) |
| Nenhuma tela desenha uma segunda barra de estado própria | APROVADO (busca por `RayzerStatusCard` só acha a barra) |
| Posição exata quando a janela é estreita | os blocos ficam num `WrapPanel` e podem quebrar de linha em 1366 px; PENDENTE (A4) |

### 5.2 Simulação

| Onde aparece | Texto | Tom | Arquivo:linha |
|---|---|---|---|
| Barra lateral, rodapé (selo) | `! MODO SIMULAÇÃO — sem catraca física` | Atenção (`Rayzer.Warning.Subtle` + `Rayzer.Warning`) | `JanelaPrincipal.xaml:54-61` |
| Barra superior, 4º bloco | `Modo simulação` / `sem catraca física` | Atenção | `JanelaPrincipal.xaml:137-139` |
| Frase de situação | prefixo `MODO SIMULAÇÃO (sem catraca física) — ` e a saúde nunca fica "normal" | Atenção | `EstadoDoPainel.cs:111-124` |
| Cartão de cada catraca simulada | pílula `Simulação` | Atenção | `App.xaml:116` (`Cartao.Catraca`) |
| Tela Gêmeo ao vivo | título `AO VIVO · CATRACA nn (SIMULADA)` | texto simples | `GemeoDigitalViewModel.cs:275` |
| Tela Simulador | aviso `Como a simulação funciona` | Info | `Simulador.xaml:37-38` |

Conclusão: **mesmo lugar e mesma cor em todas as telas** (barra superior, barra lateral e frase, todas fora do conteúdo,
tom Atenção). APROVADO pelo código; visual PENDENTE. Observações:

1. São **quatro frases diferentes** para a mesma situação ("MODO SIMULAÇÃO — sem catraca física", "Modo simulação / sem catraca
   física", "MODO SIMULAÇÃO (sem catraca física) —", "Simulação"). Padronizar é com o A3 (glossário); não é violação de lugar nem de cor.
2. A **bandeja do Windows não informa** a simulação (`Bandeja.cs` e `BandejaDoSistema.cs` não citam "simul"): quem olha só o
   ícone perto do relógio não vê. Fora do escopo da barra, mas é um lugar de estado. PENDENTE (decisão).
3. Com o menu recolhido (72 px), o selo da barra lateral continua com o texto inteiro, que quebra em coluna estreita
   (`JanelaPrincipal.xaml.cs:180-184` só ajusta a margem). PENDENTE (A4).

### 5.3 Sem internet

| Onde aparece | Texto | Tom | Arquivo:linha |
|---|---|---|---|
| Barra superior, bloco "Nuvem" | `Offline — catracas seguem` (já sincronizou antes) ou `Sem sincronização` (nunca sincronizou) | **Atenção** no primeiro, **Neutro** no segundo | `Telas.cs:206-211` |
| Frase de situação (nível T1) | `Operando sem internet — nenhuma ação necessária` | **Normal (verde, ✓)** | `EstadoDoPainel.cs:80-82` |
| Tela Sincronização, cabeçalho | `Sem internet, o acesso segue pela lista local deste computador...` | texto fixo | `Sincronizacao.xaml:8` |
| Tela Sincronização, bloco de situação | pílula "Situação da nuvem" | Neutro, Atenção ou Bom | `Telas.cs:700-714` |
| Cartão "Aguardando envio à nuvem" do Painel | quantidade na fila | Info ou Sucesso | `PainelAoVivo.xaml:33-36` |
| `PainelAoVivoViewModel.Internet` | `Nuvem: sem internet desde ... — o acesso segue pela lista local deste computador` | n/a | `Telas.cs:195`; **nenhuma tela exibe a propriedade** (busca por `Internet` em `*.xaml` não acha binding) |

Conclusão: o **lugar** é o mesmo em todas as telas (barra superior). A **cor não é**: na mesma barra, o bloco "Nuvem" mostra
sem internet em Atenção (laranja) e a frase de situação mostra o mesmo fato em Normal (verde com ✓). Há duas leituras
opostas lado a lado. A razão está escrita no código ("Sem internet é o regime NORMAL de um evento", `EstadoDoPainel.cs:78`),
e o bloco "Nuvem" não seguiu. Além disso, a frase mais clara para o operador ("o acesso segue pela lista local deste
computador", em `Telas.cs:195`) foi escrita e nunca aparece.

- **Sem internet na barra superior com a mesma cor:** REPROVADO (P5).
- A cor correta (verde, laranja ou neutra) depende do dono: BLOQUEADO (decisão). O dado a decidir é este: o código afirma
  que sem internet é normal em evento; o bloco "Nuvem" afirma que é atenção.
- Quando a simulação está ligada e a internet cai, a frase fica em Atenção pela simulação (`EstadoDoPainel.cs:119-122`),
  e o "sem internet" some da frase; fica só no bloco "Nuvem". Efeito colateral do ponto acima.

### 5.4 Outros estados citados no P5 (só registro)

- **Sem login:** o bloco de login cobre a janela inteira (`JanelaPrincipal.xaml:181-182`): barra, menu e conteúdo ficam
  atrás. É o mesmo lugar e a mesma aparência, antes de qualquer tela. APROVADO (código).
- **Catraca fechada:** não faz parte desta auditoria (A8/A2). A tela `Gerenciar` tem a lista de fechadas
  (`GerenciarCatraca.cs`, `LerFechadasAsync`); o cartão da catraca no Painel não foi conferido para esse estado. PENDENTE.

---

## 6. Resumo de classificação

| # | Conclusão | Situação |
|---|---|---|
| 1 | Nenhuma cor literal em XAML de `Desktop.App`; todas as cores são recursos dinâmicos de token | APROVADO |
| 2 | As 82 chaves `Rayzer.*` usadas existem; os 3 temas têm as mesmas 58 chaves | APROVADO |
| 3 | Cena 3D da Configuração da catraca usa 42 cores do `fit4.json` e não troca com o tema | REPROVADO (P8); exceção possível BLOQUEADA (dono) |
| 4 | Fonte literal `Consolas` em `Gemeo.xaml.cs:609` | REPROVADO (P8) |
| 5 | 10 tamanhos de letra numéricos (8 com token equivalente, 2 sem) | PENDENTE (decisão) |
| 6 | Aparência dos 3 temas, contraste e alto contraste no Windows | PENDENTE |
| 7 | 8 itens da árvore (9 telas) sem estado vazio com próximo passo | REPROVADO (P10) |
| 8 | 5 itens da árvore sem tela hoje (Leituras recusadas, Visão geral, Cartões, Trocar minha senha, Ajuda) | BLOQUEADO |
| 9 | Estado vazio aparece também durante o carregamento (sem bandeira de carregando) | PENDENTE (duração) |
| 10 | Erro de tela usa alerta neutro ou de marca, igual à confirmação | REPROVADO (P5) |
| 11 | Erro de código desconhecido mostra o código gRPC e não dá passo | REPROVADO (P10) |
| 12 | Mensagem de permissão diz o que falta, não quem libera; atalhos do cartão da catraca abrem tela sem conferir permissão | REPROVADO (P9, com A6) |
| 13 | Simulação no mesmo lugar e na mesma cor em todas as telas (barra superior e lateral) | APROVADO (código); visual PENDENTE |
| 14 | Sem internet: mesmo lugar, mas verde na frase e laranja no bloco "Nuvem" | REPROVADO (P5); cor correta BLOQUEADA (dono) |
| 15 | Bandeja do Windows não mostra simulação nem sem internet | PENDENTE (decisão) |

## 7. Decisões que cabem ao dono

1. A cena 3D da catraca pode ter paleta própria (exceção escrita ao P8) ou deve derivar do tema?
2. O P8 cobre tamanho de letra? Se sim, criar tokens para 16 e 18 ou trocar por 15 e 20.
3. Sem internet é "normal" (verde, como a frase) ou "atenção" (laranja, como o bloco "Nuvem")? Qual tom a barra mostra?
4. O estado vazio deve levar um botão (ação direta), ou o texto com o passo basta? Hoje o `RayzerEmptyState` aceita
   conteúdo (é um `ContentControl`), mas nenhuma tela o usa para pôr botão.
5. A bandeja do Windows deve avisar simulação e queda de internet?
6. O valor `#081220` do briefing é um token novo ou engano de digitação?

## 8. Como foi feito

Todas as buscas foram por leitura de texto (padrões de cor, `FontFamily`, `FontSize`, `StaticResource` e `DynamicResource` de
chaves de cor, `RayzerEmptyState`, `ZeroParaVisivel`, `VazioSome`) em `src/Desktop.App`, `src/Desktop.ViewModels` e
`src/Rayzer.Design`, mais a leitura inteira da `JanelaPrincipal.xaml`, de `App.xaml` e das telas. A checagem das 82 chaves
foi um cruzamento mecânico entre as referências de `Desktop.App` e as definições de `Rayzer.Design` e `Desktop.App`.
`src/Edge.Configurador` (o assistente) não faz parte do escopo; uma busca rápida de cor literal nele também deu 0.
Nenhuma captura de tela foi feita e nenhum teste foi executado.
