# 04 — Gêmeo digital e UX de operação da catraca TopFit 4

> Estudo de produto e UX (somente leitura do repositório `conexao-topdata`, 30/09/2026).
> Regras do dono aplicadas em todo o texto: **"Design não pode quebrar operação"** e **"Nunca
> fazer o produto parecer ter uma função que ainda não existe"**. Toda capacidade da catraca
> citada aqui tem arquivo:linha do repositório; o que não tem fonte está marcado
> `A_CONFIRMAR_COM_TOPDATA`. Tudo o que é proposta está marcado **PROPOSTA FUTURA** ou
> **MELHORIA RECOMENDADA**.
>
> Selos usados (os mesmos de `src/Desktop.ViewModels/GemeoDigital/Pecas.cs:49-77`):
> **Disponível** · **Aguardando confirmação** · **Não usada aqui** · **Fora do escopo**.
> Acrescento, só para este estudo, a marca **[NÃO IMPLEMENTADO]** quando a catraca tem a função
> documentada (SDK), mas o XAcess não tem código nem tela para ela — no gêmeo isso vira o selo
> "Aguardando confirmação" ou "Não usada aqui", nunca "Disponível".

Fontes lidas: docs/10, 11, 20, 23, 26, 27, 28, 29, 32, 33, GLOSSARIO; `docs/compatibility-matrix/*.csv`;
`src/Desktop.ViewModels/GemeoDigital/*`, `GerenciarCatraca.cs`, `Telas.cs`;
`src/Desktop.App/Telas/{Gemeo,GerenciarCatraca,Configuracoes,Catracas,PainelAoVivo,Simulador}.xaml`;
`src/Desktop.App/CapturaDeTela.cs`; `src/Rayzer.Design/{Controles.xaml,Componentes.cs,Themes/Generic.xaml}`;
`src/Contracts/Protos/edge_control.proto`; `src/Topdata.EasyInner.Adapter/TopdataInnerAdapter.cs`;
`src/Access.Application/Devices/ITopdataInnerAdapter.cs`; capturas `cap93/*.png`.

---

## 1. Diagnóstico do gêmeo atual

### 1.1 O que ele representa bem

| Ponto forte | Onde está | Por que importa na operação |
|---|---|---|
| Separação clara **Demonstração × Ao vivo**, com selo sempre visível ("DEMONSTRAÇÃO · só no desenho, nada vai para a catraca" / "AO VIVO · CATRACA NN · só observa, não comanda") | `GemeoDigitalViewModel.cs:244-246`; `Gemeo.xaml:42-46` | Evita o pior erro possível: achar que um clique no desenho mexe na catraca |
| O gêmeo **não comanda nada**; a única ponte é "Na simulada", recusada pelo serviço fora do modo simulação | `GemeoDigitalViewModel.cs:634-669`; docs/23:55 | Cumpre "design não quebra operação" |
| **Autorizado ≠ passagem**: giro só conta com origem 6; "liberado sem giro" tem contador e cenário próprio | `CenaDaCatraca.cs:181-182, 278-286`; `Roteiros.cs:75-90`; ADR-0007 | Ensina o operador a ler a prestação de contas do jeito certo |
| Ordem visual **leu → decidiu → liberou → girou**, fila de giros, queda no meio do giro sem "meio giro" | `CenaDaCatraca.cs:115-124, 220-230, 261-276` | O desenho nunca mostra algo fisicamente impossível |
| **Leitor desconhecido não é inventado** (sem origem, não desenha celular nem cartão) | `TraducaoAoVivo.cs:60-72`; docs/33:72 | Coerente com a regra de não fingir |
| Fichas das peças com selo por função e "onde se ajusta" | `Pecas.cs:134-261` | Base certa para a proposta da seção 2 |
| Prévia do display 2×16 com aviso de palavra cortada, >32 caracteres e acento | `Display2x16.cs:39-66` | Evita mensagem ilegível na catraca de verdade |
| Medidas marcadas como a confirmar e aviso na tela | `fit4.json:8`; `GemeoDigitalViewModel.cs:357-361` | Transparência sobre o que é ilustrativo |
| Cores do objeto físico no JSON; cores de situação dos tokens Rayzer | `fit4.json:35-128`; `Gemeo.xaml.cs:541-563, 681` | Cumpre "nenhuma cor solta" |
| Teclado: setas, +/−, lista de peças; estado em texto com `LiveSetting` | `Gemeo.xaml.cs:888-903`; `Gemeo.xaml:85-86, 128-129` | Acessibilidade de base existe |

### 1.2 O que falta representar (lacunas funcionais)

| Falta | Situação real no repositório | Consequência no gêmeo |
|---|---|---|
| **Sentido do giro saída / ambos** | `SentidoDoGiro.Saida` existe na cena (`CenaDaCatraca.cs:43-50`) mas nenhum roteiro usa; liberação de saída/dois sentidos é "Aguardando confirmação" (`Pecas.cs:152-153`; docs/32:252). "A TopFit 4 distingue o sentido do giro?" está em aberto (docs/29:62) | Correto não mostrar como disponível; falta um cenário explicativo com selo |
| **Tempo de liberação** (tempo de acionamento) visível no desenho | Configurável 1–50 s (`Configuracoes.xaml:18-19`); a cena usa `LimiteDaLiberacao` fixo de 15 s na demonstração (`GemeoDigitalViewModel.cs:35, 187`) e 8 s ao vivo (`:641`), e só ao vivo lê `EsperaPeloGiroSegundos` (`:546-549`) | A demonstração ensina um tempo que não é o configurado; não há contagem regressiva visível |
| **Modos on-line/off-line/contingência (T0–T3)** | `NivelDeDegradacao` T0–T3 no contrato (`edge_control.proto:159-165`); `ResultadoDoAcesso.Contingencia` tratado como "liberado" (`TraducaoAoVivo.cs:34-35`) | O desenho não distingue decisão normal de decisão em contingência |
| **Lista gravada na catraca / marcações armazenadas** | SDK documenta (`docs/11-capacidades-do-sdk.md:86-106`; `funcoes-easyinner.csv:34-40`), coleta de bilhetes existe no worker (`DevicePump.cs:316, 475`), **envio de lista não existe** (nenhuma chamada a `EnviarListaAcesso` em `src/`) | Nenhuma peça "memória" no gêmeo; o roteiro "Queda de comunicação" diz "pode operar pela lista local, se tiver" (`Roteiros.cs:146`) sem indicar que o XAcess ainda não grava lista |
| **Relógio da catraca** | Acerto e conferência existem (docs/32:177, 184-190; `edge_control.proto:110-117`) | Não aparece no gêmeo (nenhuma peça/estado) |
| **Parametrização aplicada** (qual configuração a catraca recebeu, e quando) | A tela mostra "Configuração em vigor" lida do serviço (`GemeoDigitalViewModel.cs:551-558`), mas isso é o **salvo no serviço**, não o **aplicado na catraca** (a catraca só recebe ao reconectar — `edge_control.proto:206-207`; docs/32:181) | Risco de o operador achar que o salvo já está na catraca |
| **Bip, relés, teclado, biometria** | Bip e relés "Aguardando confirmação" (docs/32:249-250); teclado "Não usada aqui" (`Pecas.cs:209-210`); biometria sem peça | Bip e relés não têm peça nem ficha no gêmeo; biometria nem aparece |
| **Liberação manual e mensagem temporária ao vivo** | Não passam pelo fluxo de acessos (docs/33:133-134) | Ao vivo, o desenho não mostra o que o próprio operador pediu |
| **Confirmação do giro ao vivo chega junto da decisão** | docs/33:124-128 | Ao vivo, liberação sem giro "volta a travar" em 8 s fixos |

### 1.3 Problemas vistos nas capturas (cap93)

Capturas analisadas: `08-gemeo-digital-{claro,escuro}{,-cenario,-separadas}.png`,
`07-gerenciar-catraca-*`, `02-catracas-*`, `09-configuracoes-*`, `01-painel-ao-vivo-*`.

| # | Gravidade | Problema | Evidência / causa provável |
|---|---|---|---|
| P1 | **ALTO** | **RadioButton "Demonstração" / "Ao vivo" ilegível no tema escuro** (texto cinza-escuro sobre `#111827`) | `08-gemeo-digital-escuro*.png`; `Controles.xaml` não tem estilo para `RadioButton` (só `CheckBox`, linha 424) — o WPF usa a cor padrão do Windows (preta). Falha WCAG 1.4.3 e é exatamente o controle que diz se o operador está vendo simulação ou a catraca real |
| P2 | **ALTO** | **Lista "Peças" com fundo branco no tema escuro** (bloco claro, ofuscante, em sala escura) | `08-gemeo-digital-escuro.png`; `Controles.xaml:619` só tem `Rayzer.Nav.List` com chave; `ListBox` sem estilo padrão |
| P3 | **ALTO** | **Na 1366×768 o operador não vê ao mesmo tempo o desenho e a narração/estado.** O palco tem altura fixa de 460 px (`Gemeo.xaml:68`) abaixo de cabeçalho + selo + barra de opções; o texto de estado, os contadores e a narração do cenário ficam abaixo da dobra | `08-gemeo-digital-*-cenario.png`: cenário rodando e nenhuma frase visível; a catraca aparece cortada embaixo |
| P4 | **ALTO** | **A captura do gêmeo não está em 1366×768**: sai com 1044×768 e o menu recolhido, enquanto as outras saem com 2049×1152 (1366×768 a 150%) | Tamanho dos PNG; `CapturaDeTela.cs:34-35, 120-129`. O CI não prova hoje que o gêmeo cabe em 1366 |
| P5 | MÉDIO | **Selo do modo parece "O DEMONSTRAÇÃO"**: o glifo ○ (neutro) lê como letra O, e a pílula sai como elipse borrada, com o texto encostando nas bordas | `08-*.png`; `Themes/Generic.xaml:20-34` (raio "pílula" + padding 10,3); o mesmo efeito em "Atendendo" e "Aguardando confirmação" em `07-*.png` e `02-*.png` |
| P6 | MÉDIO | **"Testar giro" é o botão principal (azul) da tela** — o único azul forte, colado ao desenho; lê como ação na catraca | `Gemeo.xaml:51`. Regra do DS: azul = ação principal da tela (docs/28:317); aqui a ação é só visual |
| P7 | MÉDIO | **A "pessoa genérica" vira um feixe azul translúcido** que atravessa a cena na vista Frente — lê como raio/sensor, não como pessoa | `08-*-cenario.png`; docs/33:135-136 já registra a limitação |
| P8 | MÉDIO | **Display 3D ilegível** no tamanho padrão (a cabeça ocupa ~12% da largura do palco); o operador não lê a mensagem no desenho, só na prévia 2D | `08-gemeo-digital-*.png` |
| P9 | MÉDIO | **Piso escuro em tema claro**: grande massa `#1C222E` no tema claro; contraste de figura/fundo baixo para a coluna (`#3A3E46`) | `fit4.json:119`; `08-gemeo-digital-claro.png` |
| P10 | MÉDIO | **Selo de "MODO SIMULAÇÃO" na barra lateral recolhida quebra letra a letra** ("MO / DO / SIMU / LAÇÃO…") | `08-*.png` (menu recolhido a 1044 px) |
| P11 | MÉDIO | **Barra superior mostra "✓ MODO SIMULAÇÃO …" em verde (sucesso)**, enquanto o cartão "Modo simulação" e a barra lateral usam laranja (atenção). Mensagem mista: simulação com glifo de "tudo certo" | `01/02/07/08/09-*.png`; docs/27:88 define simulação como tom Atenção |
| P12 | MÉDIO | **Gerenciar catraca: botões desabilitados parecem links clicáveis** ("Bip curto e longo", "Acionar relés avulsos" em azul a 50% de opacidade) e "Liberar um giro" desabilitado parece habilitado no escuro | `07-*.png`; `Controles.xaml:152-154` (desabilitado = só `Opacity 0.5`); `GerenciarCatraca.xaml:97-98`. Viola "nada pode parecer funcionar" |
| P13 | MÉDIO | **Configurações expõe termo técnico no modo guiado**: "Tipo de leitor (técnico — 8 na bancada; 5 se o QR não for lido)" em campo livre numérico | `Configuracoes.xaml:23-24`; GLOSSARIO:1-4 proíbe termo do SDK no guiado; `funcoes-easyinner.csv:14` (0–8) |
| P14 | BAIXO | **Painel ao vivo: métricas dizem 1 autorizado, a tabela diz "Aguardando o primeiro acesso"** (a tabela é só desde que a tela abriu, mas não diz isso) | `01-painel-ao-vivo-escuro.png` |
| P15 | BAIXO | Cartão da catraca não leva ao gêmeo ("Gerenciar · Ver acessos · Diagnóstico") | `02-catracas-*.png` |
| P16 | **ALTO (conteúdo)** | **"Sinal de liberado / bloqueado" aparece como "Disponível"** ("Automático da catraca", `Pecas.cs:213-230`), mas o repositório diz que **LEDs verde e vermelho só existem na Linha 3; na Linha 4 (Fit 4) sinalize por display e bip** (`docs/02-matriz-compatibilidade.md:35-38`; `docs/11-capacidades-do-sdk.md:81-82`). Não há fonte no repositório de que o pictograma da TopFit 4 acenda sozinho ao liberar/negar | O gêmeo acende verde/vermelho em todo cenário. Até a bancada confirmar: selo **Aguardando confirmação** + legenda "no desenho, o sinal mostra o momento; na catraca real, a confirmar"; a narração passa a citar o display como sinal principal. `A_CONFIRMAR_COM_TOPDATA` |

### 1.4 Acessibilidade

| Item | Situação | Proposta |
|---|---|---|
| Contraste | P1 e P2 reprovam; `RayzerDesignTests` calcula pares de tokens, não controles sem estilo | Estilo Rayzer para `RadioButton` e `ListBox`/`ListBoxItem` padrão; teste que **proíbe controle interativo sem estilo Rayzer** nas telas |
| Movimento reduzido | O gêmeo **não consulta** `SystemParameters.ClientAreaAnimation` (só `Componentes.cs:516, 579` consultam) — câmera, giro e separação animam sempre, contra docs/27:122-123 e docs/28:124 | Com animação desligada: câmera salta, giro vai direto ao ângulo final, narração continua |
| Leitor de tela | Estado com `LiveSetting="Polite"` (`Gemeo.xaml:86`); a mudança de **modo** (demonstração/ao vivo) não é anunciada | `LiveSetting="Assertive"` no selo do modo; `AutomationProperties.Name` no RadioButton com o texto completo |
| Teclado | Existe (setas, +/−, lista) | Atalho para "Parar cenário" (Esc) e para alternar modo (Alt+D / Alt+A já existem por `_`) |
| Cor nunca sozinha | Verde tem seta e vermelho tem X (docs/33:120) | Manter; urna cheia hoje é só cor (`Rayzer.Warning`, `Gemeo.xaml.cs:563`) — acrescentar glifo "!" na fenda e texto no estado (o texto já existe: `GemeoDigitalViewModel.cs:712`) |
| Zoom 200% | Coluna direita com largura fixa 360 (`Gemeo.xaml:27`) + palco 460 fixo | Layout em duas colunas que empilha abaixo de 1200 px efetivos |
| Alvo de toque | Botões "Na simulada" e cenários são fantasma, altura ≥40 ok; lista de peças itens ~21 px | Itens da lista com 36–40 px |

---

## 2. Proposta: o gêmeo acompanhando TODAS as funções da catraca

### 2.1 Princípios (valem para todas as famílias)

1. **Três camadas visuais, nunca misturadas no mesmo quadro:**
   - **Real** (ao vivo, vindo do serviço) — contorno sólido, selo "AO VIVO".
   - **Demonstração** (roteiro no desenho) — selo "DEMONSTRAÇÃO".
   - **Pré-visualização** (efeito de uma configuração ainda não aplicada, seção 3) — selo
     "PRÉ-VISUALIZAÇÃO · nada foi enviado", hachura diagonal sobre o palco.
2. **Função não confirmada nunca anima como se funcionasse.** Ela aparece com a peça
   **esmaecida (40%) + ícone de cadeado/relógio + selo "Aguardando confirmação"**, e a narração
   diz o que *aconteceria* e por que ainda não acontece. Ao vivo, função não confirmada
   **não aparece de jeito nenhum** (não há evento real para ela).
3. **Selo por função na ficha da peça continua sendo a fonte da verdade**
   (`CatalogoDaFit4`). Cada nova peça/função entra no catálogo **antes** de entrar no desenho,
   e o teste `O_que_o_sistema_nao_faz_aparece_como_aguardando_confirmacao`
   (`tests/Unit/GemeoDigital/GemeoDigitalTests.cs:96`) cresce junto.
4. **Nenhuma peça nova de geometria sem necessidade.** Onde a função não tem peça física
   visível (bip, relé, memória, relógio), uso **sobreposição 2D ancorada** na peça (onda,
   chip, contador) — mais barato no WPF e não finge hardware que não foi medido.
5. **Ao vivo mostra só o que o contrato `EventoDeAcesso`/`Equipamento` entrega**
   (`edge_control.proto:88-149`). O que precisaria de campo novo está marcado como
   **PROPOSTA FUTURA (contrato)**.

### 2.2 Legenda dos selos propostos

| Selo | Critério objetivo |
|---|---|
| **Disponível** | Há código no XAcess que faz a função, exercitado ao menos no simulador, e fonte primária da Topdata para a TopFit 4 |
| **Aguardando confirmação** | Documentado pela Topdata (manual/SDK) mas não ligado, ou ligado mas sem confirmação de comportamento na TopFit 4 (bancada docs/21) |
| **Não usada aqui** | A catraca tem, o XAcess poderia usar, mas a instalação decidiu não usar |
| **Fora do escopo** | Decisão registrada de não fazer nesta fase (ex.: facial, B9) |

### 2.3 Família por família

> Formato: **Peça 3D** (nome do objeto em `GeometriaFit4`, docs/33:85-89) · **Estado visual** ·
> **Cenário novo (roteiro narrado)** · **Ao vivo** · **Selo** com a fonte.
> Cenários novos são **PROPOSTA** (entram em `Roteiros.Todos`, `Roteiros.cs:169-179`); cada frase
> diz só o que o sistema faz hoje.

#### A. Leitores

**A1. Leitor de QR Code (tampa)**
- **Peça:** `QrMoldura`, `QrJanela` (`PecaDaCatraca.LeitorQr`, `Pecas.cs:29-30`).
- **Estado visual:** janela acende em ciano suave durante `LendoCredencial`; celular (adereço) aproxima. Se o leitor estiver desligado na configuração → janela apagada + cadeado.
- **Cenário novo — "QR com código longo demais":** 0 s celular aproxima; 1,4 s "O QR tem mais de 16 caracteres. A placa da catraca só aceita de 4 a 16 — o ingresso nem chega a ser cadastrado, é recusado na importação, dias antes do evento" (docs/20:15-25, 147-150). Sem sinal verde/vermelho: termina com texto, porque **não há** comportamento confirmado da catraca para QR longo (`A_CONFIRMAR_COM_TOPDATA`, docs/20:163; docs/29:60).
- **Ao vivo:** origem 21 → celular + janela (`TraducaoAoVivo.cs:62`). Hoje o serviço **não preenche** a origem (docs/33:129-131): a leitura aparece sem objeto — manter.
- **Selo:** Ler QR **Disponível** (`Pecas.cs:176-177`; `origens-evento.csv` origem 21). "QR com letras" e "qual tipo de leitor (5 ou 8)" **Aguardando confirmação** (docs/20:163; docs/29:60; `funcoes-easyinner.csv:14`).

**A2. Leitor de proximidade da frente (leitor 1)**
- **Peça:** `ProxPlaca`, `ProxSimbolo` (`PecaDaCatraca.LeitorDeProximidade`).
- **Estado visual:** símbolo de ondas acende ao ler; cartão (adereço) encosta.
- **Cenário existente:** "Cartão da bilheteria na frente" (`Roteiros.cs:110-122`). **Novo — "Cartão de 7 bytes":** narração: "Um cartão com número maior que 10 dígitos pode ser cortado pela catraca, e dois cartões podem virar o mesmo número. Compre cartão de 4 bytes." Termina **sem decisão desenhada** e com selo "Aguardando confirmação" (docs/20:91-110).
- **Ao vivo:** origem 2 → cartão na frente (`TraducaoAoVivo.cs:63`); motivo `ForaDaUrna` também (`:65`).
- **Selo:** **Disponível** (`Pecas.cs:187-188`); formato "ABA 10 dígitos" documentado pela Topdata (docs/20:86-88), comportamento com 7 bytes `A_CONFIRMAR_COM_TOPDATA` (docs/20:98).

**A3. Código de barras**
- **Peça:** a mesma `QrJanela` (o leitor da Topdata lê Code 128, EAN-13 etc., docs/20:40).
- **Estado visual:** só na ficha; o desenho não tem adereço de código de barras.
- **Cenário:** nenhum (não é usado no evento).
- **Ao vivo:** nada distinto (a origem seria a mesma 21 — `A_CONFIRMAR_COM_TOPDATA` se código de barras chega com outra origem).
- **Selo:** **Não usada aqui** — ingressos do evento são QR (docs/20:3-4).

**A4. Leitor da urna (leitor 2)**
- **Peça:** `UrnaMoldura`, `UrnaFenda` (`PecaDaCatraca.Urna`). Leitor da urna desligado na configuração já apaga a urna (`GemeoDigitalViewModel.cs:354-355, 539`).
- **Estado visual:** fenda com luz ciano ao ler; cartão entra até a metade (não some — ver recolhimento).
- **Cenário existente:** "Cartão da bilheteria na urna" (`Roteiros.cs:92-108`) — já diz que não recolhe.
- **Ao vivo:** origem 3 → cartão na urna (`TraducaoAoVivo.cs:64`).
- **Selo:** **Disponível** (`Pecas.cs:196-197`).

**A5. Teclado**
- **Peça:** `TecladoBase`, `Teclas`.
- **Estado visual:** sempre neutro, sem animação; ícone "não usada".
- **Cenário:** nenhum.
- **Ao vivo:** origem 1 existe na tabela (`origens-evento.csv` linha 2), mas o teclado não é habilitado para acesso; se chegar, mostrar "tecla pressionada" sem decisão.
- **Selo:** **Não usada aqui** (`Pecas.cs:209-210`).

**A6. Biometria (digital)**
- **Peça:** **nenhuma** — a variante desenhada não tem sensor (`fit4.json:6, 19-25`). Não criar peça.
- **Estado visual / cenário / ao vivo:** nenhum. Só uma linha na ficha da `Tampa`: "Biometria: não existe nesta variante".
- **Selo:** **Fora do escopo** (B9, docs/29:35); SDK só parcialmente documentado (docs/11:112-116).

**A7. Leitor facial**
- **Peça:** `FacialCorpo`, `FacialTela` — só com "Leitor facial (variante)" marcado (`Gemeo.xaml:63-64`).
- **Estado visual:** sempre esmaecido + selo sobre a peça, **mesmo marcado** (hoje ele aparece normal, sem distinção visual de que é fora do escopo).
- **Cenário:** nenhum (não ensinar operador a esperar reconhecimento facial).
- **Selo:** **Fora do escopo** (`Pecas.cs:236-237`; docs/11:118-134).

#### B. Display, mensagens e sinais

**B1. Display — mensagem padrão**
- **Peça:** `DisplayMoldura`, `DisplayTela` (`PecaDaCatraca.Display`).
- **Estado visual:** texto 2×16 real (`Display2x16.Formatar`). **Melhoria:** botão "Aproximar do display" que leva à vista Painel com zoom de leitura (P8).
- **Cenário:** coberto por todos os cenários (volta à mensagem padrão). Pré-visualização na seção 3.
- **Ao vivo:** mensagem padrão lida da configuração **salva**; rótulo "salva no serviço — vale na próxima conexão da catraca" (docs/32:181; `edge_control.proto:206-207`).
- **Selo:** **Disponível** (`Pecas.cs:163-164`). "Exibir data/hora no display" (`EnviarMensagemPadraoOnLine` com `ExibirData`, `funcoes-easyinner.csv:57`) → **Não usada aqui** (o adaptador envia 0: `TopdataInnerAdapter.cs:186, 205`).

**B2. Display — mensagem temporária**
- **Peça:** `DisplayTela`.
- **Estado visual:** texto temporário + **barra de tempo** fina sob o display (1–60 s) para mostrar que volta sozinho.
- **Cenário novo — "Aviso no display":** 0 s "O operador escreve 'Use a catraca 3' em Gerenciar catraca, por 10 s"; 0,5 s display troca; 10,5 s "Acabou o tempo: volta a mensagem padrão". Narração final: "Com alguém passando, o pedido espera a catraca ficar livre" (docs/32:203-205).
- **Ao vivo:** **não aparece hoje** (docs/33:133-134). **PROPOSTA FUTURA (contrato):** o serviço difundir `ComandoRegistrado` concluído no fluxo, para o gêmeo mostrar "mensagem pedida por Fulano às 19:02".
- **Selo:** **Disponível** (`Pecas.cs:165-166`; docs/32:178). Acento: **Aguardando confirmação** (`Pecas.cs:167-168`; docs/32:265).

**B3. Display — mensagem de negação**
- **Peça:** `DisplayTela`. Texto fixo "Acesso nao autorizado" por 3 s (`Display2x16.cs:22-25`; `DevicePump.cs:545`).
- **Cenário:** já no "Código desconhecido" (`Roteiros.cs:60-73`).
- **Selo:** **Disponível**.

**B4. Luz de fundo (backlight)**
- **Peça:** `DisplayTela` (emissivo).
- **Estado visual hoje:** apaga quando "sem comunicação" (`CenaDaCatraca.cs:420`). **Isso é uma suposição**: não há fonte no repositório de que o display da TopFit 4 apague sem o PC. Propor: sem comunicação, display com **texto cinza "sem notícia do PC"** *no painel lateral*, e o desenho do display em **hachura** (desconhecido), não apagado. `A_CONFIRMAR_COM_TOPDATA`.
- **Selo:** comando de backlight (docs/11:78) → **Não usada aqui**.

**B5. Sinais liberado / bloqueado (pictogramas)**
- **Peça:** `LiberadoFundo`, `LiberadoSeta`, `BloqueadoFundo`, `BloqueadoXis`.
- **Estado visual:** manter seta/X (cor nunca sozinha), **mas** com a legenda de ficha "O desenho acende para marcar o momento; se a TopFit 4 acende sozinha, está a confirmar". Ver P16.
- **Ao vivo:** acende junto de liberado/negado (`CenaDaCatraca.cs:418-419`), com a mesma legenda.
- **Selo:** mudar de Disponível para **Aguardando confirmação** (`docs/02-matriz-compatibilidade.md:38`; `docs/11-capacidades-do-sdk.md:81-82`). `A_CONFIRMAR_COM_TOPDATA`.

**B6. Bip curto / longo**
- **Peça:** nenhuma física; **sobreposição 2D** de ondas sonoras na lateral da `Tampa` + legenda "bip".
- **Estado visual:** ondas **tracejadas e cinza** com cadeado (não animadas em loop; um pulso só, e só com animações do Windows ligadas).
- **Cenário novo — "Bip ao negar (a confirmar)":** 0 s QR desconhecido; 1,4 s negado; 1,5 s ondas tracejadas + narração "A catraca tem bip curto e longo no manual, mas o sistema ainda não usa: aguardando ensaio na bancada (INT-UX-03)". Nenhum som é tocado pelo PC.
- **Ao vivo:** nada.
- **Selo:** **Aguardando confirmação** (docs/32:249; `funcoes-easyinner.csv:49-50`).

#### C. Relés, giro, sentido e tempo

**C1. Relés 1 e 2 (acionamentos)**
- **Peça:** nova peça lógica **`PlacaDeControle`** dentro da `Coluna`, visível só com "Peças separadas" (a ficha da Coluna já diz "por dentro passam a placa de controle e os cabos", `Pecas.cs:249`). Geometria: uma caixa simples, com rótulo "ilustrativa".
- **Estado visual:** dois "bornes" R1/R2 esmaecidos com cadeado.
- **Cenário:** nenhum animado. Ficha explica as funções documentadas (0 não utilizado · registro entrada/saída · sirene · revista · catraca liberada…; tempo 0–50 s, `funcoes-easyinner.csv:17`) e o alerta "relé não serve para acionar giro" (docs/11:62-63).
- **Ao vivo:** nada.
- **Selo:** **Aguardando confirmação** (docs/32:250). Relé 2 = urna (`funcoes-easyinner.csv:48`).

**C2. Giro de entrada**
- **Peça:** `Cubo`, `Braco01..03` (`PecaDaCatraca.Rotor`).
- **Estado visual:** braço solto (liberada) → 1/3 de volta para trás (entrada) → travado; contagem "giro confirmado".
- **Cenário:** existente ("QR válido", "Teste de giro").
- **Ao vivo:** `PassagemConfirmada` → giro (`TraducaoAoVivo.cs:36-40`).
- **Selo:** **Disponível** (`Pecas.cs:148-151`; origem 6, `origens-evento.csv` linha 7).

**C3. Sentido saída / dois sentidos / trocar sentido**
- **Peça:** `Rotor` + **seta de sentido** 2D no piso (verde = entrada; cinza tracejada = saída).
- **Estado visual:** seta de saída sempre tracejada com cadeado.
- **Cenário novo — "Saída e evacuação (a confirmar)":** 0 s "A catraca tem liberação de saída e nos dois sentidos"; 2 s braço **não** gira; seta de saída tracejada; narração "Liberar nos dois sentidos deixa passar mais de uma pessoa por vez (carona). Depende da Topdata e da decisão sobre evacuação (B4)". Se o operador clicar "Testar giro de saída", o gêmeo diz "Só no desenho" e gira **com a peça em hachura** — ou, mais seguro, **não oferecer** o botão até a confirmação. Recomendo **não oferecer**.
- **Ao vivo:** nada (a cena ao vivo só gira em entrada, `TraducaoAoVivo.cs:39`). Se a bancada confirmar que a catraca distingue sentido (docs/29:62), o giro ao vivo passa a usar o sentido do evento (**PROPOSTA FUTURA (contrato)**).
- **Selo:** **Aguardando confirmação** (`Pecas.cs:152-153`; docs/32:252). O adaptador já implementa as variantes (`TopdataInnerAdapter.cs:237-256`), mas nenhuma tela as usa. "Instalação invertida" (perfil físico `SentidoInvertido`, `DeviceConfiguration`) → parâmetro de comissionamento, só no **modo técnico**.

**C4. Queda dos braços (emergência)**
- **Selo:** **Aguardando confirmação** (`Pecas.cs:154-155`). Nenhum cenário, nenhuma animação.

**C5. Tempo de liberação (tempo de acionamento)**
- **Peça:** `Rotor` + **anel de contagem regressiva** 2D em volta do cubo durante `Liberada`.
- **Estado visual:** anel verde que esvazia em N segundos (N = configurado), com o número no centro ("5 s"). Com animações desligadas: só o número, trocando a cada segundo.
- **Cenário:** "Liberada, mas a pessoa desiste" passa a usar **o tempo configurado**, não 15 s fixos (`GemeoDigitalViewModel.cs:35, 187`). Narração: "Passados 5 s (o tempo configurado em Configurações), a catraca volta a travar."
- **Ao vivo:** usa `EsperaPeloGiroSegundos`/`TempoDeAcionamentoSegundos` (`GemeoDigitalViewModel.cs:546-556`); hoje o limite ao vivo é 8 s fixos na troca de catraca (`:641`) — unificar.
- **Selo:** **Disponível** (configuração 1–50 s, `Configuracoes.xaml:18-19`; origem 5 "fim do tempo", `origens-evento.csv` linha 6). Se a catraca realmente manda origem 5: `A_CONFIRMAR` (docs/32:256-259).

#### D. Urna

**D1. Recolher cartão**
- **Peça:** `UrnaFenda`, `UrnaDeposito`.
- **Estado visual:** o cartão **para na fenda** (não cai no depósito). Com "Peças separadas", o depósito aparece **vazio e esmaecido** com cadeado.
- **Cenário novo — "Como seria o recolhimento (a confirmar)":** 0 s cartão na fenda; 1,4 s narração "Na TopFit 4, recolher o cartão depende do relé 2, cuja função não está documentada. O sistema ainda não recolhe" (docs/32:251; docs/29:61). Nenhuma animação do cartão caindo.
- **Selo:** **Aguardando confirmação** (`Pecas.cs:198-199`).

**D2. Urna cheia**
- **Peça:** `UrnaMoldura` (cor `Rayzer.Warning`) + **glifo "!"** na fenda (cor nunca sozinha).
- **Cenário existente:** "Urna cheia (a confirmar)" (`Roteiros.cs:155-166`).
- **Ao vivo:** origem 20 (`TraducaoAoVivo.cs:48-51`), que o serviço ainda não envia (docs/33:132). O campo `capacidade_da_urna_usada_por_cento` existe no `Equipamento` (`edge_control.proto:99-100`) — **se** o serviço preencher, mostrar barra de nível no depósito; enquanto zero/ausente, **não mostrar barra** (não inventar 0%).
- **Selo:** **Aguardando confirmação** (`Pecas.cs:200-201`); capacidade física `A_CONFIRMAR_COM_TOPDATA` (docs/20:165).

#### E. Relógio, modos e memória da catraca

**E1. Relógio da catraca**
- **Peça:** nenhuma (o display não mostra data: `ExibirData = 0`). **Chip 2D** ancorado no `Display`: "Relógio · acertado há 41 s".
- **Estado visual:** chip neutro; **"!" + "diferença de 42 s"** quando `relogio_divergente` (`edge_control.proto:110-117`).
- **Cenário novo — "Relógio conferido":** 0 s "Ao conectar, o sistema acerta o relógio da catraca pelo do PC"; 2 s "1 min depois confere, e depois a cada hora"; 4 s "Se diferir mais de 30 s, aparece um aviso no cartão da catraca — ela continua atendendo" (docs/32:184-190).
- **Ao vivo:** lê `relogio_acertado_em`, `divergencia_do_relogio_segundos`.
- **Selo:** acerto e conferência **Disponível** (docs/32:177, 184-187); acerto automático durante a operação **Aguardando confirmação** (docs/32:188-190).

**E2. Modos: on-line / sem internet / catraca operando sozinha / contingência**
- **Peça:** **faixa de modo** 2D sob a catraca (não no selo de demonstração/ao vivo, que é outra coisa):
  - "On-line — o PC decide" (T0) · "Sem internet — o PC continua decidindo" (T1) — ambos **Disponível** (`EdgeControlService.cs:108`; `EstadoDoPainel.cs:80-84`).
  - "Operando sozinha, sem o PC" (estado `OfflineAutonomo`, `Textos.cs:35`; `DeviceStateMachine.cs:204`) — **Aguardando confirmação**: o que a catraca decide sozinha depende da lista gravada nela, que o XAcess ainda não grava (ver E3).
  - "Decisão pela regra local" (`ResultadoDoAcesso.Contingencia`, `edge_control.proto:156`) — o serviço **não produz** esse resultado hoje (só o gêmeo o trata, `TraducaoAoVivo.cs:34`). **Não mostrar** até existir.
- **Cenário:** "Queda de comunicação" (existente) passa a dizer: "Nesse intervalo, **o XAcess ainda não grava lista na catraca**; o que ela fizer sozinha depende da configuração de fábrica — a confirmar" (corrige `Roteiros.cs:146`).
- **Selo:** conforme acima.

**E3. Lista de acesso gravada na catraca**
- **Peça:** **chip "Memória"** ancorado na `Tampa`: "Lista na catraca: não gravada".
- **Estado visual:** chip neutro esmaecido + cadeado.
- **Cenário:** nenhum animado (não há código).
- **Ao vivo:** nada — o contrato não tem campo.
- **Selo:** **Aguardando confirmação / [NÃO IMPLEMENTADO]**. SDK documenta lista branca/negra, horário 101/102, **reenvio da lista inteira** a cada mudança (docs/11:86-97; `funcoes-easyinner.csv:34-39`), capacidade ~15.000 (`limites-de-capacidade.csv:2-3`): **30 mil ingressos não cabem em lista branca**. Nenhuma chamada `EnviarListaAcesso` existe em `src/`.

**E4. Marcações armazenadas / coleta de bilhetes**
- **Peça:** mesmo chip "Memória": "Marcações a coletar: —".
- **Estado visual:** ícone ↻ enquanto coletando.
- **Cenário novo — "Volta da comunicação":** após "Conectou", passo "O sistema coleta as marcações que a catraca guardou, uma por uma, gravando cada uma antes de pedir a próxima" (docs/11:101-106; `DeviceStateMachine.cs:235-241`).
- **Ao vivo:** **PROPOSTA FUTURA (contrato)** — hoje o `Equipamento` não informa "coletando" nem quantidade; só o texto de estado do worker.
- **Selo:** **Disponível** no programa da catraca (`DevicePump.cs:316, 475`), nunca exercitado com hardware (docs/29:24; CHAOS-REC-01, docs/29:69).

**E5. Parametrização aplicada**
- **Peça:** **chip "Configuração"** no palco: "Salva às 18:40 · na catraca desde 18:41" **ou** "Salva, ainda não aplicada".
- **Estado visual:** ✓ quando aplicada; ! quando salva e não aplicada.
- **Ao vivo:** hoje só dá para saber o **salvo** (`ObterConfiguracao`). **PROPOSTA FUTURA (contrato):** `Equipamento.configuracao_aplicada_em` + `configuracao_versao` (hash). Até lá o chip diz: "Salva no serviço. A catraca recebe ao reconectar (Aplicar agora)".
- **Selo:** envio completo a cada conexão **Disponível** (`TopdataInnerAdapter.cs:135-187`; ADR-0020); indicador de "aplicada" **[NÃO IMPLEMENTADO]**.

### 2.4 Resumo — matriz função × selo

| Família | Função | Selo proposto | Fonte |
|---|---|---|---|
| Leitores | QR | Disponível | Pecas.cs:176-179; docs/20:15-16 |
| | QR com letras / tipo 5 ou 8 | Aguardando confirmação | docs/20:163; docs/29:60 |
| | Proximidade frente (leitor 1) | Disponível | Pecas.cs:187-188 |
| | Código de barras | Não usada aqui | docs/20:40 |
| | Urna (leitor 2) | Disponível | Pecas.cs:196-197 |
| | Teclado | Não usada aqui | Pecas.cs:209-210 |
| | Biometria | Fora do escopo | docs/29:35; docs/11:112-116 |
| | Facial | Fora do escopo | Pecas.cs:236-237 |
| Display | Mensagem padrão / temporária / negação | Disponível | docs/32:178; Display2x16.cs:22 |
| | Acento | Aguardando confirmação | docs/32:265 |
| | Data/hora no display, backlight | Não usada aqui | TopdataInnerAdapter.cs:186; docs/11:78 |
| Sinais | Pictograma verde/vermelho | **Aguardando confirmação** (muda) | docs/02:38; docs/11:81 |
| Bip | Curto/longo | Aguardando confirmação | docs/32:249 |
| Relés | R1/R2 avulsos | Aguardando confirmação | docs/32:250 |
| Giro | Entrada + confirmação origem 6 | Disponível | Pecas.cs:148-151 |
| | Saída / dois sentidos / trocar | Aguardando confirmação | docs/32:252; docs/29:62 |
| | Queda de braços | Aguardando confirmação | Pecas.cs:154-155 |
| Tempo | Tempo de liberação 1–50 s | Disponível | Configuracoes.xaml:18 |
| Urna | Recolher | Aguardando confirmação | docs/32:251 |
| | Urna cheia | Aguardando confirmação | Pecas.cs:200-201 |
| Relógio | Acerto/conferência | Disponível | docs/32:177-187 |
| | Acerto automático em operação | Aguardando confirmação | docs/32:188-190 |
| Modos | On-line / sem internet | Disponível | EdgeControlService.cs:108 |
| | Catraca sozinha (off-line) | Aguardando confirmação | Textos.cs:35; docs/11:86-97 |
| | Contingência (regra local) | não mostrar (não produzido) | edge_control.proto:156 |
| Memória | Lista gravada na catraca | Aguardando confirmação [NÃO IMPLEMENTADO] | funcoes-easyinner.csv:34-39 |
| | Coleta de marcações | Disponível (sem hardware) | DevicePump.cs:475 |
| Config. | Envio completo a cada conexão | Disponível | TopdataInnerAdapter.cs:135-187 |
| | Indicador "aplicada na catraca" | [NÃO IMPLEMENTADO] | edge_control.proto:88-117 |

---

## 3. Pré-visualização de parametrização no gêmeo

> **MELHORIA RECOMENDADA.** Hoje só existe a prévia de texto do display
> (`GemeoDigitalViewModel.cs:327-346`, `Gemeo.xaml:212-235`), desligada no modo ao vivo
> (`:122`). A proposta generaliza essa prévia para toda configuração que tenha efeito visível.

### 3.1 O que pode ser pré-visualizado (e o que não)

| Parâmetro | Onde é editado hoje | Efeito no gêmeo | Pode pré-visualizar? |
|---|---|---|---|
| Mensagem padrão (≤32) | Configurações (`Configuracoes.xaml:15-16`) | Display 2×16 com avisos de corte e acento | **Sim** (já existe a base) |
| Tempo de liberação (1–50 s) | Configurações (`:18-19`) | Anel de contagem regressiva no rotor; cenário "desiste" usa o valor | **Sim** |
| Leitor da urna ligado/desligado | Configurações (`:21`) | Urna acesa/apagada com cadeado; cenário "cartão na urna" muda para "leitor da urna desligado: nada acontece" | **Sim** |
| Leitores habilitados (operação de cada leitor 0–4) | **Não exposto** (só no adaptador, `TopdataInnerAdapter.cs:162-163`) | Leitor desabilitado esmaecido; seta de sentido por leitor | Só no **modo técnico**, e só depois de a tela existir |
| Sentido de giro / instalação invertida | **Não exposto** (`GatePhysicalProfile.SentidoInvertido`) | Seta no piso e sentido do braço | Invertida: **sim, no modo técnico** (é física, comissionamento). Saída/dois sentidos: **não** — função "Aguardando confirmação"; pré-visualizar ensinaria algo que não existe |
| Tipo de leitor (0–8) | Configurações, campo técnico (`:23-24`) | Nenhum efeito visual honesto | **Não** — mostrar só como texto no diff |
| Relés, bip | Não existem | — | **Não** |

### 3.2 Como funciona (fluxo)

1. Em **Parametrização** (seção 4.2), cada grupo com efeito visível tem o botão
   **"Ver no gêmeo"**. Ele abre o gêmeo **em modo Pré-visualização** com a catraca escolhida,
   sem sair do rascunho (painel do gêmeo embutido à direita, ou a tela Gêmeo com o rascunho
   carregado).
2. O gêmeo monta a cena com **o rascunho** (`ConfiguracaoDoEvento` local, não gravada) em vez
   da configuração lida do serviço, e roda um **roteiro curto automático** que exercita o que
   mudou (ex.: mudou o tempo de liberação → roda "Liberada, mas a pessoa desiste" com o novo
   tempo; mudou a mensagem → mostra o display de perto).
3. **Nada é enviado**: a pré-visualização não chama `GravarConfiguracao` nem `EnviarComando`.
   O ViewModel de pré-visualização **não recebe** o cliente gRPC (injeção sem cliente), e há
   teste que garante isso (seção 6.3).
4. Botão **"Comparar"** alterna entre **Atual (aplicado)** e **Novo (rascunho)** no mesmo palco
   (duas faixas de cor/rotulagem, nunca duas catracas lado a lado — em 1366 elas ficariam
   pequenas demais). Atalho: barra de espaço segura mostra o atual.

### 3.3 Como deixar claro o que é real e o que é simulado

| Situação | Selo do palco (sempre visível, `RayzerStatusPill`) | Tom / glifo | Marca no desenho |
|---|---|---|---|
| Demonstração | "DEMONSTRAÇÃO · só no desenho, nada vai para a catraca" | Neutro · ○ → trocar por **◇** (P5) | nenhuma |
| Pré-visualização | "PRÉ-VISUALIZAÇÃO · configuração ainda **não** aplicada" | Info · ↻ | **hachura diagonal** discreta no fundo do palco + etiqueta "RASCUNHO" no canto |
| Ao vivo | "AO VIVO · CATRACA 01 · só observa, não comanda" | Sucesso · ● | nenhuma |
| Ao vivo com configuração salva e **não aplicada** | idem + segunda pílula "Configuração salva às 18:40 — ainda não chegou à catraca" | Atenção · ! | chip "Configuração" com ! |

Regras:
- **Pré-visualização e ao vivo são mutuamente exclusivos.** Entrar em pré-visualização
  suspende o espelho ao vivo com o aviso "O espelho ao vivo fica pausado enquanto você
  pré-visualiza".
- A lista **"Configuração em vigor"** (`Gemeo.xaml:237-252`) passa a ter duas colunas:
  **Salva no serviço** e **Na catraca** (esta última "—" até existir o campo de contrato,
  seção 2.3-E5). Hoje o título "em vigor" é impreciso: é o que está **salvo**.
- Depois de **Aplicar** (seção 4.2), o gêmeo só troca para "aplicada" quando o histórico do
  comando `AplicarConfiguracao` voltar **Concluído** (docs/32:212-213) — nunca no clique.

---

## 4. Telas do módulo catraca (1366 × 768)

### 4.0 Situação real de cada tela proposta

| Tela | Existe hoje? | Base no repositório | Classificação desta proposta |
|---|---|---|---|
| Parametrização da catraca | Parcial: "Configurações do evento", 5 campos, para todas as catracas | `Configuracoes.xaml`; `edge_control.proto:189-208`; `Telas.cs:697-800` | **MELHORIA RECOMENDADA** (diff, validação, confirmação); campos técnicos novos **PROPOSTA FUTURA** |
| Gerenciar catraca (comandos) | Sim | `GerenciarCatraca.xaml`; `GerenciarCatraca.cs`; docs/32 | **MELHORIA RECOMENDADA** |
| Cartões (lista, busca, cadastro, bloqueio, histórico) | **Não** (só "Consultar código") | docs/26; docs/29:73-83; `ConsultarCodigo` (`edge_control.proto:274-288`) | **PROPOSTA FUTURA — fase 3** |
| Importação de cartões | **Não** (só os modelos de planilha) | docs/26; `installer/modelos/`; `ModelosDePlanilhaTests.cs` | **PROPOSTA FUTURA — fase 3** |
| Lista gravada na catraca | **Não** (nenhuma chamada `EnviarListaAcesso`) | docs/11:86-97; `funcoes-easyinner.csv:34-39`; `limites-de-capacidade.csv:2-3` | **PROPOSTA FUTURA**, depende de decisão (lista branca não cabe 30 mil) e de bancada |
| Modo guiado × técnico | **Não** implementado | docs/10:1-8; GLOSSARIO | **MELHORIA RECOMENDADA** |

> Nenhuma dessas telas pode aparecer no menu antes de ter função. Enquanto não existir, o item
> **não aparece** (docs/28:327, regra 12). O que pode aparecer já: a linha "Cartões — em breve
> (fase 3)" **na tela Consultar código**, como texto, sem botão.

### 4.1 Navegação proposta

Hoje o menu tem 10–11 itens soltos. Proposta: agrupar sem esconder (seções com sobretítulo
`Rayzer.Type.Overline`, itens de 44 px):

```
OPERAÇÃO          Painel ao vivo · Acessos · Consultar código
CATRACAS          Catracas ─► detalhe da catraca (abas)  · Gêmeo digital
CARTÕES (fase 3)  Cartões · Importar cartões            ← só quando existir
EVENTO            Configurações do evento · Sincronização · Prestação de contas
SUPORTE           Diagnóstico · Simulador (só em simulação)
```

**Detalhe da catraca** (novo, a partir de "Gerenciar" no cartão da catraca):

```
┌ CATRACA 01 · Entrada 1        ● Atendendo   Relógio acertado há 41 s   [Abrir no gêmeo] ┐
│ Visão · Comandos · Parametrização · Lista na catraca · Histórico                         │
└──────────────────────────────────────────────────────────────────────────────────────────┘
```
- "Comandos" = a tela Gerenciar catraca atual.
- "Parametrização" = hoje, só leitura do que vale para todas as catracas, com link para
  "Configurações do evento" (a configuração **não é por catraca** hoje:
  `Configuracoes.xaml:9`). Parametrização por catraca = **PROPOSTA FUTURA**.
- "Lista na catraca" = seção 4.5; enquanto não existir, a aba **não aparece**.
- O cartão da catraca ganha "Gêmeo" ao lado de "Gerenciar · Ver acessos · Diagnóstico" (P15).

**Modo guiado × técnico** (docs/10:1-8): alternador no rodapé da barra lateral, ao lado do
tema: "Modo técnico" (caixa de marcar). No técnico aparecem: códigos numéricos (tipo de
leitor, origem, retorno nativo), `correlationId`, estado da máquina, diff bruto, campos de
comissionamento. No guiado **nenhum** termo da coluna esquerda do GLOSSARIO aparece sozinho.
Sem login (docs/27:234), o modo técnico é **conveniência, não segurança**: dizer isso na dica.

### 4.2 Parametrização (MELHORIA RECOMENDADA)

**Layout 1366 × 768** (conteúdo útil ≈ 1062 × 640 depois do menu de 248 px e da barra
superior):

```
┌ Configurações do evento ─────────────────────────────── Escopo: todas as 5 catracas ┐
│ Rascunho (não salvo) ●                                                             │
├─ Grupos (240) ─┬─ Campos do grupo (≈480) ───────────────┬─ Resumo (≈320) ──────────┤
│ ▸ Display  ≠   │ Mensagem no visor (até 32)              │ O que muda              │
│   Passagem     │ [Bem-vindo! Aproxime o QR     ]  28/32  │ ≠ Mensagem no visor      │
│   Leitores     │ ┌ 2 × 16 ────────────┐                   │   atual: Aproxime o ing…│
│   Nuvem        │ │Bem-vindo! Aproxi│  ! "Aproxime" corta  │   novo:  Bem-vindo! Ap… │
│   Técnico ⚙    │ │me o QR          │    entre as linhas   │                         │
│                │ └──────────────────┘   [Ver no gêmeo]    │ 1 alteração · 0 erros   │
│                │                                          │ [Revisar e aplicar…]    │
└────────────────┴──────────────────────────────────────────┴─────────────────────────┘
```

- **Grupos:** Display (mensagem padrão) · Passagem (tempo de liberação 1–50 s) · Leitores
  (leitor da urna ligado) · Nuvem (espera pelo giro, só se a nuvem estiver ligada) ·
  **Técnico** (só no modo técnico: tipo de leitor 0–8 como **lista com nomes**, não campo
  livre — `funcoes-easyinner.csv:14`; os demais parâmetros do `DeviceConfiguration` —
  operação dos leitores, relés, dígitos, mudança on/off-line — aparecem **somente leitura**
  com "Definido pelo sistema; ajustar exige confirmação da Topdata", até existir decisão).
- **Validação inline** (antes de salvar, sem esperar o serviço): mensagem ≤ 32 com prévia 2×16
  e avisos de `Display2x16.Avisos`; tempo inteiro 1–50; tipo de leitor da lista. O serviço
  continua validando (`GravarConfiguracaoResponse.problemas`, `edge_control.proto:203-208`) e
  o erro dele aparece **no campo**, não só no rodapé (hoje: lista de `RayzerAlert` no fim da
  página, `Configuracoes.xaml:47-53`).
- **Diff "atual × novo":** coluna Resumo lista só o que muda, com glifo ≠, valor atual e
  novo. "Atual" = **salvo no serviço** e, quando existir o campo, "na catraca".
- **Aplicar com confirmação** — fluxo de dois passos que já existe (Salvar → Aplicar agora,
  `Configuracoes.xaml:36-41`) vira um só assistente:
  1. **Revisar** (diff + impacto): "As 5 catracas vão reconectar para receber a configuração.
     Cada uma fica alguns segundos sem atender (docs/32:260-261). Horário atual: 19:02 —
     evite o pico."
  2. **Quando aplicar:** "Salvar e aplicar agora" · "Só salvar (vale na próxima reconexão)".
     **MELHORIA RECOMENDADA:** "Aplicar uma catraca por vez" (escalonado), para nunca
     derrubar todas as pistas juntas — precisa de mudança no serviço.
  3. **Confirmar:** nome (obrigatório, já existe — `Configuracoes.xaml:33-34`) e botão
     `Rayzer.Button.Primary` com o verbo explícito.
  4. **Resultado:** progresso por catraca (↻ Aguardando a catraca → ✓ Aplicada / × Não voltou
     em 2 min — docs/32:212-213), vindo do histórico de comandos, nunca presumido.
- **Auditoria:** quem, quando, o quê (diff) — o serviço grava o operador junto
  (`edge_control.proto:198-201`). **PROPOSTA FUTURA:** aba "Histórico de configurações" com
  "voltar a esta versão" (rollback, docs/10:309) — hoje não há RPC de histórico de configuração.

### 4.3 Gerenciar catraca (MELHORIA RECOMENDADA sobre o que existe)

```
┌ CATRACA 01 · Entrada 1  ● Atendendo  Relógio: acertado há 41 s · diferença 0 s ───────┐
│ Seu nome (fica registrado)  [Maria Souza      ]                                        │
├─ Liberar um giro (entrada) ─────────────┬─ Mensagem no display ───────────────────────┤
│ Motivo (obrigatório) [Cadeirante, portão]│ [Use a catraca 3        ] 18/32 · [10] s     │
│ Não conta como ingresso.                 │ ┌──────────────────┐                          │
│ [ Liberar 1 giro… ]  (primário, único)   │ │Use a catraca 3  │ prévia 2×16             │
│                                          │ └──────────────────┘ [Mostrar no display]     │
├─ Manutenção ────────────────────────────┴─────────────────────────────────────────────┤
│ [Acertar o relógio agora]   [Refazer a conexão…]   ↻ fica ~segundos sem atender         │
├─ Não disponível nesta versão (texto, sem botões) ───────────────────────────────────────┤
│ ! Bip curto e longo — aguardando ensaio na bancada                                      │
│ ! Relés avulsos — aguardando a Topdata                                                  │
│ ! Recolher cartão na urna — aguardando a Topdata                                        │
│ ! Liberar na saída / nos dois sentidos — aguardando a Topdata e a decisão de evacuação  │
├─ Histórico de pedidos desta catraca (tabela, 36 px por linha) ─────────────────────────┤
```
- **Prévia 2×16** também aqui (hoje só no gêmeo): reutiliza `Display2x16`.
- **Confirmação leve** (dentro do cartão, não modal) para "Liberar 1 giro…" e "Refazer a
  conexão…": "Liberar 1 giro de entrada na Entrada 1 agora? [Confirmar] [Cancelar]" com
  contagem de 10 s. Motivo: em evento lotado, um clique errado solta o giro para qualquer um
  (docs/32:208-210). Sem confirmação para relógio e mensagem (reversíveis e inofensivos).
- **"Não disponível" vira texto**, não botão desabilitado (P12).
- Botão desabilitado ganha **estilo próprio** (fundo `Surface`, texto `Text.Secondary`,
  borda tracejada) em vez de opacidade 0,5 (P12).
- Estado **"catraca ocupada"**: se a catraca está em passagem, a pílula diz "Em passagem —
  o pedido espera ficar livre (até 15 s para liberação)" (docs/32:203-211).

### 4.4 Cartões (PROPOSTA FUTURA — fase 3)

**Lista e busca**
```
┌ Cartões ──────────────────────────────────── 12.480 cartões · 3 tipos ─ [Importar…] [Novo cartão] ┐
│ Buscar [final do código ou código inteiro ] Tipo [Todos ▾] Situação [Todas ▾]  [Buscar]          │
├──────────────────────────────────────────────────────────────────────────────────────────────────┤
│ CÓDIGO          TIPO          SITUAÇÃO      VALIDADE            USOS    ÚLTIMO USO                │
│ ••••••••1234    Inteira       ✓ Ativo       até 05/10 23:59      1/1    19:01 · Entrada 1         │
│ ••••••••0102    Meia          × Bloqueado   —                    0/∞    —                         │
└──────────────────────────────────────────────────────────────────────────────────────────────────┘
```
- **Mascaramento por padrão:** código sempre mascarado na lista (mesma regra do
  `EventoDeAcesso.credencial_mascarada`, `edge_control.proto:131-132`, e do
  `ConsultarCodigoResponse.codigo_mascarado`, `:280`). Código em `Rayzer.Font.Mono`, **sempre
  texto** (zeros à esquerda, docs/28:322).
- **Titular não aparece na lista**; só no detalhe, e nunca em relatório ou log (docs/26:220-223).
- **Busca:** pelos últimos 4+ dígitos ou pelo código inteiro; o código digitado some do campo
  depois da busca (não fica na tela).
- **Detalhe do cartão (painel à direita, 360 px):** tipo, situação, validade, usos, titular
  (se houver, oculto até "Mostrar titular"), **Histórico** = `ConsultarCodigo.historico`
  (`edge_control.proto:287`) — a tela "Consultar código" já faz isso e é a base.
- **Cadastro:** código (lido pelo leitor do balcão ou digitado 2×, para conferir), tipo,
  titular opcional, validade, usos. Validação igual à da importação (docs/26:244-252).
  Aviso quando o código difere de outro só por zeros à esquerda (docs/26:265-268).
- **Bloqueio:** "Bloquear cartão…" com motivo e nome; desbloqueio idem; nunca apagar
  (docs/26:239-240). O efeito vale para a decisão no PC na hora; **na lista gravada na
  catraca só depois de reenviar a lista** (seção 4.5) — dizer isso quando a lista existir.
- **Estados:** vazio ("Nenhum cartão cadastrado. Importe a planilha do modelo ou cadastre um
  cartão." + [Baixar modelo] [Importar…]); busca sem resultado ("Nenhum cartão termina em
  1234. Confira o número no verso do cartão."); carregando (`RayzerFlowBar`); sem serviço
  ("Sem resposta do serviço local — os cartões ficam na base deste computador; abra o
  Assistente de configuração para iniciar o serviço").

### 4.5 Importação de cartões — assistente (PROPOSTA FUTURA — fase 3)

Passos numa faixa horizontal (`Rayzer` "passos", docs/27:158), com **Voltar** sempre
disponível até o passo 4:

| Passo | Tela | Regras (docs/26) |
|---|---|---|
| **1. Arquivo** | Arrastar/escolher `.xlsx` ou `.csv` (`;`, UTF-8). Link "Baixar modelo". | Aceita as duas extensões (docs/26:193-194) |
| **2. Colunas** | Mapeamento automático pelos cabeçalhos do modelo (`codigo`, `tipo`, `titular`, `situacao`, `validade_inicio`, `validade_fim`, `usos_maximos`, `observacao`); só pede mapeamento manual se algum cabeçalho não bater. Mostra 5 linhas de amostra **mascaradas**. | docs/26:207-218 |
| **3. Prévia e erros** | 4 métricas (`RayzerMetricCard`): **Novos · Mudam · Iguais · Com erro**. Tabela de erros: linha, coluna, motivo em português ("Código com 18 caracteres — a catraca aceita de 4 a 16"). Filtro "só erros". Botão "Baixar linhas com erro". | Prévia antes de gravar (docs/26:227-229); erros recusados de propósito (docs/26:244-252); duplicado no arquivo = erro nas duas linhas (docs/26:235-236); aviso de zeros à esquerda (docs/26:265-268) |
| **4. Confirmar** | Resumo: "Gravar 12.480 cartões (12.000 novos, 480 mudam). 37 linhas com erro ficam de fora." Nome obrigatório. Botão primário "Importar 12.480 cartões". Aviso: "Nada é apagado; cartão fora da planilha continua como está." | Tudo ou nada numa transação (docs/26:230-231); nunca apaga (docs/26:239-240) |
| **5. Relatório** | ✓/× por grupo, arquivo de devolução, hora, quem importou. Botões "Desfazer esta importação" (só se nenhum cartão foi usado depois — docs/26:241-242) e "Ir para Cartões". | |

- **Carregando:** prévia de 100 mil linhas (docs/29:80) com `RayzerFlowBar` e contagem
  "Lendo linha 40.000 de 100.000"; botão "Cancelar" até o passo 4.
- **Célula formatada como número no Excel:** erro específico com a correção ("Formate a coluna
  como Texto e cole de novo"), docs/26:248.

### 4.6 Lista gravada na catraca (PROPOSTA FUTURA — depende de decisão e bancada)

**Antes de desenhar a tela, três decisões** (senão a tela promete o que não cumpre):
1. **Lista branca não cabe o evento:** ~15.000 cartões por catraca (`limites-de-capacidade.csv:2-3`)
   contra 30 mil pessoas (docs/14). Opções: lista **negra** (bloqueados) ou lista branca de um
   **subconjunto priorizado** (docs/11:91-94). Decisão de negócio.
2. **Alterar um cartão = reenviar a lista inteira** e a catraca sobrescreve (docs/11:95-97;
   `funcoes-easyinner.csv:36`) — operação cara, planejada, fora do pico.
3. **Não há leitura de volta da lista** no inventário da DLL (`inventario-completo-dll.csv:9, 77, 104, 143`):
   "verificar" só pode ser (a) retorno 0 do envio e (b) **teste amostral** com a catraca
   off-line. `ApagarListaAcesso` tem assinatura ambígua (docs/11:250). `A_CONFIRMAR_COM_TOPDATA`.

**Layout (quando existir):**
```
┌ Lista na catraca · CATRACA 01 ────────────────────────────────────────────────────────┐
│ Tipo de lista: Bloqueados (lista negra)   Na catraca: 212 cartões · enviada 18:10 por Ana │
│ Na base deste PC: 215 bloqueados → 3 diferenças  [Ver diferenças]                        │
│ Capacidade: ████░░░░░░░░ 215 de ~15.000 (a confirmar na bancada)                         │
│ [Enviar lista…]                                                                           │
├─ Envio em andamento ──────────────────────────────────────────────────────────────────────┤
│ ↻ Montando 215 cartões… → Enviando à catraca… → ✓ Catraca confirmou (retorno 0)            │
│ Durante o envio a catraca pode ficar sem atender. (duração: a confirmar na bancada)       │
├─ Verificar ───────────────────────────────────────────────────────────────────────────────┤
│ Teste amostral: com a catraca sem o PC, passe 1 cartão da lista e 1 fora dela.             │
│ [Registrar resultado do teste]                                                            │
└───────────────────────────────────────────────────────────────────────────────────────────┘
```
- Confirmação: "Enviar 215 cartões para a Entrada 1? A lista atual da catraca será
  **substituída**." + nome.
- Progresso **por etapa**, não percentual inventado (a DLL envia a lista numa chamada; não há
  progresso por cartão documentado).
- Estados: nunca enviada ("Esta catraca não tem lista gravada. Sem o PC, ela não tem com que
  decidir — comportamento a confirmar com a Topdata."); falha ("A catraca não confirmou o
  envio. A lista anterior pode ter sido apagada — envie de novo antes de liberar a pista.").

### 4.7 Estados transversais (todas as telas do módulo)

| Estado | Componente | Texto (guiado) |
|---|---|---|
| Vazio | `RayzerEmptyState` | Diz por que está vazio e o que fazer (docs/28:325) |
| Carregando | `RayzerFlowBar` (sem spinner) | — |
| Erro de validação | mensagem **no campo** (`aria-invalid` / `AutomationProperties.HelpText`) | "O tempo vai de 1 a 50 segundos." |
| Sem serviço | `RayzerAlert` Perigo no topo + controles de envio desabilitados | "Sem resposta do serviço local — abra o Assistente de configuração para iniciá-lo" (docs/27:186) |
| Catraca sem notícia | pílula ○ + comandos desabilitados com motivo | "Sem notícia da catraca há 22 s — os pedidos ficam guardados por 60 s" (docs/32:208-211) |
| Modo simulação | pílula ! Atenção (nunca ✓) | "Modo simulação — nenhuma catraca física é acionada" |
| Pedido pendente | linha do histórico ↻ | "Na fila da catraca — espera ela ficar livre" |

---

## 5. Microcópia (pt-BR, modo guiado)

Tom: **o que aconteceu, desde quando, o que fazer** (docs/27:179-191); sem termo do SDK
(GLOSSARIO). Entre parênteses, o texto atual quando muda.

### 5.1 Gêmeo digital

| Lugar | Texto proposto |
|---|---|
| Selo demonstração | "DEMONSTRAÇÃO · só no desenho — nada vai para a catraca" |
| Selo ao vivo | "AO VIVO · CATRACA 01 · só acompanha, não comanda" |
| Selo pré-visualização | "PRÉ-VISUALIZAÇÃO · rascunho — ainda não está na catraca" |
| Opções de modo | "Demonstração (só o desenho)" · "Ao vivo (acompanhar a catraca)" |
| Botão (atual "Testar giro") | "Girar no desenho" — botão **secundário**, não primário (P6) |
| Botão (atual "Na simulada") | "Passar na catraca simulada" (só em modo simulação) |
| Função não confirmada, na ficha | "Ainda não disponível — aguardando confirmação da Topdata. O desenho não simula." |
| Função "Não usada aqui" | "A catraca tem, mas este evento não usa." |
| Sinais (P16) | "O desenho acende o sinal para marcar o momento. Se a TopFit 4 acende sozinha, ainda está sendo confirmado." |
| Ao vivo sem evento | "Nenhuma passagem nesta catraca desde 19:02. O desenho se mexe a cada leitura." |
| Ao vivo, catraca sem notícia | "Sem notícia da Entrada 1 há 22 s. O desenho fica parado até ela voltar." |
| Pausa por pré-visualização | "O acompanhamento ao vivo fica pausado enquanto você vê o rascunho." |
| Config salva e não aplicada | "Configuração salva às 18:40, ainda não chegou à catraca. [Aplicar agora…]" |
| Medidas | "Desenho ilustrativo: as medidas ainda não foram conferidas numa catraca de verdade." |

### 5.2 Parametrização

| Lugar | Texto |
|---|---|
| Faixa do rascunho | "Você tem 2 alterações não salvas." |
| Botão | "Revisar e aplicar…" |
| Revisão — impacto | "As 5 catracas vão reconectar para receber a configuração. Cada uma fica alguns segundos sem atender. Se estiver no pico, prefira só salvar e aplicar depois." |
| Opções | "Salvar e aplicar agora" · "Só salvar (vale na próxima reconexão)" |
| Confirmar | "Aplicar em 5 catracas" |
| Sucesso parcial | "Aplicada em 4 de 5 catracas. A Entrada 3 não voltou em 2 min — veja em Diagnóstico." |
| Erro de campo | "A mensagem tem 35 letras; o visor mostra 32." · "O tempo vai de 1 a 50 segundos." |
| Técnico, só leitura | "Definido pelo sistema. Mudar exige confirmação da Topdata." |

### 5.3 Gerenciar catraca

| Lugar | Texto |
|---|---|
| Botão | "Liberar 1 giro…" |
| Confirmação | "Liberar 1 giro de entrada na Entrada 1 agora? Não conta como ingresso. [Liberar] [Cancelar]" |
| Pendente | "Pedido feito. A catraca executa assim que ficar livre (até 15 s)." |
| Expirado | "Não executado: a catraca ficou ocupada mais de 15 s. Peça de novo se a pessoa ainda estiver lá." |
| Refazer conexão | "Refazer a conexão da Entrada 1? Ela fica alguns segundos sem atender. [Refazer] [Cancelar]" |
| Bloco "não disponível" (atual "Ainda não disponível") | "Não disponível nesta versão" |

### 5.4 Cartões e importação (fase 3)

| Lugar | Texto |
|---|---|
| Vazio | "Nenhum cartão cadastrado. Importe a planilha do modelo ou cadastre um cartão." |
| Máscara | "Números de cartão aparecem escondidos. Busque pelos 4 últimos dígitos." |
| Bloquear | "Bloquear o cartão ••••1234? Ele deixa de entrar na hora. [Bloquear]" |
| Importar — prévia | "12.000 novos · 480 mudam · 9.000 iguais · 37 com erro" |
| Importar — confirmar | "Importar 12.480 cartões. As 37 linhas com erro ficam de fora — baixe o arquivo para corrigir." |
| Importar — Excel | "Linha 42: a coluna 'codigo' está como número no Excel e pode ter perdido zeros à esquerda. Formate a coluna como Texto e cole de novo." |
| Importar — tamanho | "Linha 88: código com 18 caracteres. A catraca aceita de 4 a 16." |
| Desfazer | "Desfazer a importação de 19:05? Os cartões voltam como estavam. (Só é possível porque nenhum foi usado depois.)" |

### 5.5 Lista na catraca (quando existir)

| Lugar | Texto |
|---|---|
| Confirmar envio | "Enviar 215 cartões para a Entrada 1? A lista que está na catraca será substituída." |
| Nunca enviada | "Esta catraca não tem lista gravada. Sem o PC, o que ela faz ainda está sendo confirmado com a Topdata." |
| Falha | "A catraca não confirmou o envio. Envie de novo antes de liberar a pista." |

### 5.6 Barra superior e selo lateral

| Atual | Proposto |
|---|---|
| "✓ MODO SIMULAÇÃO (sem catraca física) — Operando sem internet — nenhuma ação necessária" (verde) | "! Modo simulação — nenhuma catraca física é acionada · Sem internet: nenhuma ação necessária" (tom Atenção) |
| Selo lateral recolhido "MO DO SIMU LAÇÃO…" | Recolhido: só o glifo "!" com dica "Modo simulação"; aberto: texto completo |

---

## 6. Critérios de aceite verificáveis pelo CI

### 6.1 Capturas (job "Instalador MSI" → `capturas-das-telas.zip`, `CapturaDeTela.cs`)

**Pré-condição de todas:** janela com **1366 × 768 lógicos** confirmada no arquivo
(PNG 1366×768 a 100% ou 2049×1152 a 150%); hoje o gêmeo sai com 1044×768 (P4). O
`relatorio.txt` passa a registrar o tamanho e o DPI de cada captura, e o job **falha** se
o tamanho lógico não for 1366×768.

| Captura (claro e escuro) | Estado | O que um revisor/verificação automática confere |
|---|---|---|
| `08-gemeo-digital` | aberto, demonstração | Selo do modo, desenho inteiro (base ao topo) **e** o estado em texto visíveis **sem rolar** |
| `08-gemeo-digital-cenario` | "QR válido" no passo 2 | Selo DEMONSTRAÇÃO + desenho + **frase da narração do passo atual** no mesmo quadro |
| `08-gemeo-digital-separadas` | peças separadas, Rotor escolhido | Ficha com selos por função visível |
| `08-gemeo-digital-aovivo` **(nova)** | ao vivo, catraca 1 simulada, depois de passar `1000000001` | Selo AO VIVO · CATRACA 01; "Último evento" preenchido |
| `08-gemeo-digital-sem-comunicacao` **(nova)** | ao vivo com catraca parada | Estado "Sem comunicação" em texto, desenho em hachura/sem luz |
| `08-gemeo-digital-previa` **(nova, quando existir)** | pré-visualização de mensagem | Selo PRÉ-VISUALIZAÇÃO + hachura |
| `08-gemeo-digital-ficha-aguardando` **(nova)** | ficha da Urna | Pílulas "Aguardando confirmação" legíveis, sem botão de ação |
| `07-gerenciar-catraca` | nome vazio | Botões desabilitados **visivelmente diferentes** dos habilitados; bloco "Não disponível" sem botões |
| `07-gerenciar-catraca-confirmacao` **(nova)** | confirmação de liberação aberta | Texto com nome da catraca e "não conta como ingresso" |
| `09-configuracoes-diff` **(nova, quando existir)** | rascunho com 1 alteração | Coluna "O que muda" com atual e novo |
| `12-cartoes-*`, `13-importacao-passo-3` **(fase 3)** | lista e prévia com erros | Códigos mascarados; nenhum código completo na imagem |

### 6.2 Verificações automáticas sem olho humano (Linux, já cabem nos testes atuais)

1. **Todo controle interativo tem estilo Rayzer**: `RayzerDesignTests` passa a exigir estilo
   implícito para `RadioButton`, `ListBox`, `ListBoxItem`, `ToggleButton` (P1, P2). Teste:
   percorrer os XAML de `Telas/` e listar tipos usados sem estilo em `Controles.xaml`.
2. **Contraste do RadioButton e da ListBox** incluído em
   `Texto_e_situacao_passam_no_contraste_WCAG_AA` (texto sobre `Surface` nos dois temas).
3. **Estado desabilitado** tem par de cor próprio com ≥ 3:1 de diferença de luminância para o
   habilitado (P12), em vez de `Opacity`.
4. **Nenhuma tela do modo guiado contém termo técnico**: varrer `Text=`/`Content=` dos XAML
   contra a coluna esquerda do GLOSSARIO + lista (`TipoLeitor`, "origem", "relé 2",
   "EasyInner", números de função `EI-`). "Tipo de leitor (técnico…)" só pode aparecer em
   bloco visível apenas no modo técnico (P13).
5. **Barra de simulação nunca com tom Sucesso** (P11): teste de ViewModel da janela.
6. **Movimento reduzido:** o gêmeo, com `ClientAreaAnimation = false` simulado, desenha o
   quadro final (ângulo já no fim do giro, câmera sem transição).
7. **Na captura (Windows), amostragem de pixels**: média de luminância do retângulo da lista de
   peças no tema escuro < 30% (não pode ser branca) — verificação barata no job existente.

### 6.3 Testes de ViewModel (Linux, `tests/Unit/GemeoDigital` e `tests/Integration/TelasTests.cs`)

| Teste proposto | O que segura |
|---|---|
| `Toda_funcao_disponivel_tem_fonte` | Cada `FuncaoDaPeca` com `Disponivel` tem campo `Fonte` (arquivo/seção) não vazio — obriga citar a origem |
| `Sinais_luminosos_nao_sao_disponiveis_sem_confirmacao_da_linha_4` | P16: `SinalLiberado`/`SinalBloqueado` = `AguardandoConfirmacao` enquanto a matriz disser "LEDs só Linha 3" |
| `Funcao_aguardando_nao_tem_roteiro_que_a_executa` | Nenhum roteiro aplica sinal de recolher, saída, dois sentidos, bip ou relé como se acontecesse |
| `Ao_vivo_nunca_mostra_funcao_aguardando` | `TraducaoAoVivo` não gera sinal para função não confirmada |
| `Demonstracao_usa_o_tempo_de_liberacao_configurado` | `LimiteDaLiberacao` = `TempoDeAcionamentoSegundos` também na demonstração |
| `Previa_de_parametrizacao_nao_chama_o_servico` | ViewModel de pré-visualização construído com cliente que falha em qualquer chamada; exercitar todos os comandos |
| `Previa_e_ao_vivo_sao_exclusivos` | Entrar em pré-visualização desliga `ModoAoVivo`; o selo muda |
| `Selo_sempre_diz_a_fonte_do_desenho` | Para cada combinação (demonstração, ao vivo, simulada, pré-visualização) o `SeloDoModo` tem o texto e o tom esperados |
| `Configuracao_salva_nao_e_apresentada_como_aplicada` | O resumo usa "Salva no serviço"; "Na catraca" só com o campo de contrato |
| `Liberacao_manual_exige_confirmacao` | `LiberarManualmente` só envia depois de `Confirmar`; cancelar não envia |
| `Refazer_conexao_exige_confirmacao` | idem |
| `Aplicar_mostra_resultado_por_catraca_a_partir_do_historico` | Nunca marca "aplicada" antes de `Concluido` |
| `Codigo_de_cartao_nunca_sai_desmascarado_da_lista` (fase 3) | Nenhuma propriedade de linha expõe o código inteiro |
| `Importacao_prevê_antes_de_gravar` (fase 3) | Nenhuma escrita antes de `Confirmar`; contagens novos/mudam/iguais/erro batem com docs/26 |
| `Lista_na_catraca_nao_mostra_progresso_por_cartao` (quando existir) | Progresso só por etapa |

---

## 7. Prioridade sugerida

| Ordem | Item | Tipo | Esforço |
|---|---|---|---|
| 1 | P1/P2: estilos Rayzer de `RadioButton` e `ListBox` + teste de "controle sem estilo" | Correção | Pequeno |
| 2 | P16: selo dos sinais luminosos para "Aguardando confirmação" + narração | Correção de conteúdo | Pequeno |
| 3 | P12: estado desabilitado próprio; bloco "Não disponível" como texto | Correção | Pequeno |
| 4 | P4/P3: captura real em 1366×768 e layout do gêmeo sem rolagem (palco flexível, narração sobre o palco em faixa inferior) | Correção | Médio |
| 5 | P11/P5/P10: tom da simulação e pílula | Correção | Pequeno |
| 6 | Confirmação leve em liberação manual e refazer conexão | MELHORIA RECOMENDADA | Pequeno |
| 7 | Tempo de liberação configurado na demonstração + anel de contagem | MELHORIA RECOMENDADA | Pequeno |
| 8 | Chips Relógio / Configuração / Memória; "Salva × na catraca" | MELHORIA RECOMENDADA (+ contrato) | Médio |
| 9 | Pré-visualização de parametrização | MELHORIA RECOMENDADA | Médio |
| 10 | Parametrização com diff e assistente de aplicar | MELHORIA RECOMENDADA | Médio |
| 11 | Cartões + Importação | PROPOSTA FUTURA — fase 3 | Grande |
| 12 | Lista na catraca | PROPOSTA FUTURA — após decisão e bancada | Grande |
| 13 | Modo guiado × técnico | MELHORIA RECOMENDADA | Médio |

## 8. Pendências `A_CONFIRMAR_COM_TOPDATA` levantadas neste estudo

1. O pictograma verde/vermelho da TopFit 4 acende sozinho ao liberar/negar? (docs/02:38 diz que LEDs são só Linha 3.)
2. O display da TopFit 4 apaga, ou mostra algo, sem comunicação com o PC?
3. A catraca distingue o sentido do giro na origem 6? (docs/29:62)
4. O que a catraca decide sozinha (off-line) **sem** lista gravada?
5. Duração e efeito na operação de `EnviarListaAcesso`; existe forma de ler a lista de volta?
6. Código de barras chega com a mesma origem 21 do QR?
7. Comportamento com QR > 16 caracteres e com letras (docs/20:163).
