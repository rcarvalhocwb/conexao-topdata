# 51. Roteiros do operador (E5)

**Status:** entregável E5 do plano [docs/46](46-prompt-analise-front-end.md), escrito pelo agente A2 (analista de
tarefas do operador). Não altera código. Os nove roteiros do §4 (A2) foram seguidos tela a tela no código de
`src/Desktop.ViewModels` e `src/Desktop.App`.

## 0. Como ler este documento

**O que foi feito.** Cada roteiro foi reconstituído lendo o código: títulos de menu (`Telas.cs`), textos e botões
(`*.xaml`), regras de habilitar botão e mensagens (ViewModels), permissões (`Telas.cs`, `Login.cs`,
`InterceptadorDeSessao.cs`, migrações 020 e 022). Nada foi executado: o painel é WPF e não roda neste ambiente.

**Cliques.** Contei cada clique distinto (menu, botão, caixa de texto que precisa de foco, caixa de seleção).
Digitar não conta como clique; o texto digitado aparece à parte. Os botões com `_` no nome (por exemplo `_Gravar`)
têm atalho Alt+letra e o `Tab` troca de campo, então o operador de teclado faz menos cliques que o número abaixo.

**Tempo.** O docs/46 §8 pede tempo medido. Tempo exige o painel aberto no Windows com o serviço e catraca (ou
simulação). **Todo tempo fica PENDENTE** e aparece como `PENDENTE`. Não foi estimado nem inventado.

**Classificação** (docs/46 §7):

| Situação | Quando usei |
|---|---|
| APROVADO | Verificado no código, com arquivo:linha |
| PENDENTE | Depende de ver na tela (Windows/CI) ou de medir tempo |
| BLOQUEADO | Depende de decisão do dono ou de hardware, ou a função não existe e não cabe a mim criar |
| REPROVADO | Verificado no código e não atende ao princípio P1–P10 citado |

**Exemplos** usam só dados fictícios: operadora "Ana Souza" (login `ana.souza`), catraca "Entrada Norte".

### 0.1 O que o código tem hoje (conferido contra o docs/46 §3)

| O que o docs/46 diz | O que o código mostra | Situação |
|---|---|---|
| 15 telas no menu | **14** entradas: Painel ao vivo, Catracas, Acessos, Consultar código, Sincronização, Prestação de contas, Gerenciar catraca, Configuração da catraca, Configurações, Diagnóstico, Simulador (só em simulação), Pessoas, Perfis e horários, Usuários (`Telas.cs:1342-1358`) | APROVADO |
| Existe "Cartões não reconhecidos" | **Não existe** tela, ViewModel nem botão com esse nome ou função (busca em `src/` sem resultado; o único trecho é `edge_control.proto:254`, campo `origem_desconhecida`) | APROVADO (verificação da ausência) |
| Menu ainda mostra "Gêmeo digital" | Na fonte o título já é "Configuração da catraca" (`GemeoDigitalViewModel.cs:161`, `Gemeo.xaml:28`). O resíduo "Gêmeo" só pode estar no instalador | PENDENTE (conferir o build instalado) |
| Título "Configuração desta catraca" | Não existe com esse texto. Há "Configuração da catraca" (menu e tela do desenho), "Configurar a catraca" (botão em Gerenciar, `GerenciarCatraca.xaml:39`) e "Parametrização da catraca" (`Parametrizacao.xaml:26`; o menu interno usa "Parametrização", `Parametrizacao.cs:546`) | REPROVADO (P1: a mesma função em três nomes, ver F-52) |
| "Cartões" / lote de cartões em GESTÃO | Não existe. O cadastro de cartões é a fase 3, marcada "não existe" em `docs/34-anexos/02-arquiteto.md:222` | APROVADO (verificação da ausência) |

## 1. Quadro-resumo

| # | Roteiro | Cliques até a função | Cliques até concluir | Tempo | Situação |
|---|---|---|---|---|---|
| 1 | Abrir o evento e ver se está tudo funcionando | 0 depois do login | 0 | PENDENTE | APROVADO no alcance; REPROVADO na leitura (faixa e termos) |
| 2 | Descobrir que uma catraca parou | 0 (topo e cartões) | 3 para refazer a conexão | PENDENTE | REPROVADO (faixa diz "Tudo funcionando") |
| 3 | Liberar uma pessoa sem cartão, com motivo | 1 | 5 | PENDENTE | APROVADO no alcance e na confirmação; REPROVADO no "Seu nome" |
| 4 | Cadastrar um lote de cartões inteiros pela urna | não existe | — | PENDENTE | BLOQUEADO |
| 5 | Cadastrar um cartão recusado | não existe | pelo menos 9 pelo desvio | PENDENTE | BLOQUEADO (função) |
| 6 | Fechar uma catraca e reabri-la | 1 | 3 para fechar; 2 para reabrir | PENDENTE | REPROVADO (estado invisível fora de Gerenciar) |
| 7 | Fechar o dia e gerar a prestação de contas | 1 | 3 | PENDENTE | BLOQUEADO ("fechar o dia"); REPROVADO ("De" fixo) |
| 8 | Trocar a própria senha | não existe | só com administrador | PENDENTE | REPROVADO |
| 9 | Criar um operador de portaria que não vê o restante | 1 | 6 | PENDENTE | APROVADO no alcance; REPROVADO em P9 |

Critério de aceite do docs/46 §8 ("em até dois cliques, ou com justificativa"): cumprido em 1, 2, 3, 6, 7 e 9;
sem função para 4, 5 e 8.

---

## 2. Roteiro 1: abrir o evento e ver se está tudo funcionando

**Objetivo do operador.** Ligar o computador da portaria, entrar e saber em poucos segundos se as catracas e a
internet estão bem.

**Passos reais**

1. Abrir o programa "XAcess — Painel do evento · Rayzer" (`JanelaPrincipal.xaml:5`). Uma cobertura de login esconde o
   painel até entrar (`JanelaPrincipal.xaml:181-221`).
2. Digitar **Usuário** (o cursor já cai nele, `JanelaPrincipal.xaml.cs:105`) e **Senha**; Enter aciona **`_Entrar`**
   (`IsDefault`, `JanelaPrincipal.xaml:193`). **0 cliques de mouse.**
3. Após uma atualização do programa, aparece a janela "Novidades desta versão"; botão **Entendi**
   (`JanelaDeNovidades.xaml:54-57`). 1 clique, só uma vez por versão.
4. O painel abre em **Painel ao vivo** (`Telas.cs:1359`). Ler, de cima para baixo: os cartões do topo **Serviço local**,
   **Catracas**, **Nuvem** (`JanelaPrincipal.xaml:128-136`), a faixa de estado (`:165-167`), os cinco números
   (`PainelAoVivo.xaml:21-36`) e a lista **Catracas** (`:47-56`).

**Cliques até a função:** 0 depois do login. **Tempo:** PENDENTE.

| ID | Atrito | Onde | Princípio | Situação |
|---|---|---|---|---|
| F-01 | Não existe "abrir o evento": nenhuma tela ou lista de conferência; o operador interpreta três cartões, uma faixa e cinco números por conta própria | `Telas.cs:1359`; sem VM de abertura | P3, P10 | BLOQUEADO (decisão do dono: criar ou não um passo de abertura) |
| F-02 | A faixa "Tudo funcionando" não olha catraca parada: ver F-08 | `EstadoDoPainel.cs:84-87,104` | P5 | REPROVADO |
| F-03 | Termos técnicos no caminho principal: "Serviço local", "Firmware", "Grupo" (que mostra o nome do programa da catraca, `e.Worker`), "Porta", "Reconexões" | `JanelaPrincipal.xaml:128`; `App.xaml:130-133`; `Telas.cs:395` | P2 | REPROVADO |
| F-04 | Os cartões do topo são só leitura: nenhum leva a lugar nenhum (sem `Click`/`Command`), nem o que está em amarelo ou vermelho | `JanelaPrincipal.xaml:128-139` | P10 | REPROVADO |
| F-05 | Estado vazio sem catraca manda "Abra o Assistente de configuração e informe o número do Inner de cada catraca". "Inner" é nome de engenharia, e o botão para abrir o assistente só existe no topo quando o serviço diz que falta configuração | `Textos.cs:197`; `JanelaPrincipal.xaml:143-145` | P2, P10 | REPROVADO |
| F-06 | Menu com 14 itens em lista plana, sem grupos | `Telas.cs:1342-1358`; `JanelaPrincipal.xaml:87-98` | P3 | REPROVADO |
| F-07 | Menu cortado em janelas pequenas (issue #9). O código tem `MinHeight=600` e `MinWidth=1000`, mas só se confirma vendo na tela | `JanelaPrincipal.xaml:7-8,27-100` | P7 | PENDENTE |

**O que está certo e foi verificado:** abre direto no Painel ao vivo; o selo de simulação aparece em três lugares
(barra lateral `JanelaPrincipal.xaml:55-61`, cartão `:137-139`, prefixo da faixa `EstadoDoPainel.cs:113`);
"sem internet" é tratado como normal e não como alarme (`EstadoDoPainel.cs:80-82`). APROVADO.

---

## 3. Roteiro 2: descobrir que uma catraca parou

**Objetivo.** Perceber, sem ficar olhando para o painel, que a catraca "Entrada Norte" deixou de atender, e saber o
que fazer.

**Como o sistema avisa (três caminhos reais)**

- **Com a janela aberta:** o cartão **Catracas** do topo muda para amarelo ("1/2 online", `Telas.cs:205`) ou vermelho
  ("0/2 online", `Telas.cs:204`); o cartão da catraca na lista passa de "Atendendo" para a situação nova
  (`Textos.cs:12-44`: "Programa da catraca parou", "Programa da catraca não responde", "Com problema — veja o
  diagnóstico", "Sem notícia do programa da catraca").
- **Com a janela escondida na bandeja:** balão do Windows "Catraca parou de atender", com o nome e a situação
  (`Bandeja.cs:77-84`).
- **Não existe:** som, nem alerta dentro do painel. A chamada `ListarAlertas` existe no contrato
  (`edge_control.proto:142`), mas nenhum código do Desktop a usa (busca por "Alerta" em `Desktop.*` sem resultado).

**Passos para agir** (a partir do cartão da catraca no Painel ao vivo):

1. Clicar em **Gerenciar** no cartão (`App.xaml:157-165`). A tela abre já na catraca certa (`Telas.cs:1452-1470`). 1 clique.
2. Digitar o nome em "Seu nome (fica registrado em cada pedido)" (`GerenciarCatraca.xaml:24-25`), 1 clique no campo.
   Sem isso o botão fica desabilitado (`GerenciarCatraca.cs:99,377`).
3. Em **Manutenção**, clicar em **Refazer a _conexão…** (`GerenciarCatraca.xaml:112`). 1 clique.
4. Clicar em **Confirmar e refazer** (`:121`). 1 clique.

**Cliques:** 0 para perceber (topo/cartões), 1 para chegar à função, 4 no total com o nome. **Tempo:** PENDENTE.

| ID | Atrito | Onde | Princípio | Situação |
|---|---|---|---|---|
| F-08 | **A faixa de estado fica "Tudo funcionando" (verde) com uma catraca de duas parada.** O texto de alerta só troca quando **nenhuma** catraca está conectada. O serviço só emite os níveis T0 e T1 (nunca T2/T3), então "Catraca sem comunicação…" não aparece nesse caso. A única pista no topo é o cartão amarelo "1/2 online" | `EstadoDoPainel.cs:84-87,99-108`; `EdgeControlService.cs:273` (`Nivel = internet ? T0 : T1`) | P5 | REPROVADO |
| F-09 | A situação "Programa da catraca parou" não diz o próximo passo. Os botões do cartão são Gerenciar, Ver acessos e Diagnóstico; "Diagnóstico" abre uma tela escrita "Para o suporte" sem selecionar a catraca | `Textos.cs:38-40`; `App.xaml:157-182`; `Diagnostico.xaml:8`; `Telas.cs:1447-1451` | P10 | REPROVADO |
| F-10 | A conexão só pode ser refeita depois de digitar "Seu nome", e o botão fica desabilitado sem explicar por quê | `GerenciarCatraca.cs:93-99`; ver F-15 | P10 | REPROVADO |
| F-11 | A mesma lista de cartões aparece em duas telas: seção "Catracas" do Painel ao vivo e o item de menu "Catracas" (mesmo modelo `Cartao.Catraca`) | `PainelAoVivo.xaml:47-56`; `Catracas.xaml:13`; `App.xaml:92` | P1 | REPROVADO |
| F-12 | Um estado que o código não conhece sai cru na tela (`_ => (estado, Atencao)`), por exemplo o nome interno do estado da máquina | `Textos.cs:42` | P2 | REPROVADO |
| F-13 | O balão da bandeja só avisa **mudança**. Catraca que já estava parada quando o painel abriu não gera aviso (a primeira observação só registra) | `Bandeja.cs:72` | P5 | REPROVADO |
| F-14 | Alertas do serviço (`ListarAlertas`) sem tela no painel | `edge_control.proto:142` | P5 | BLOQUEADO (decisão: expor ou não) |

**O que está certo:** o relógio da catraca fora do horário vira texto em amarelo no cartão com o caminho
("Acerte em Gerenciar", `App.xaml:137-150`); com o serviço fora, os cartões não mostram "Atendendo" falso
(`Telas.cs:232-243`, `Textos.cs:204-205`). APROVADO.

---

## 4. Roteiro 3: liberar uma pessoa que não tem cartão (com motivo)

**Objetivo.** A pessoa chegou sem cartão nem ingresso, mas pode entrar (autorização do responsável). O operador
libera uma passagem na catraca e deixa o motivo registrado.

**Passos reais**

1. Clicar em **Gerenciar** no cartão da catraca (Painel ao vivo), ou no menu **Gerenciar catraca**
   (`Telas.cs:1350`, ícone `Conversores.cs:117`). 1 clique. Pelo cartão, a catraca já vem escolhida. Pelo menu, a tela abre na
   primeira catraca da lista (`GerenciarCatraca.cs:355-358`); se for outra, trocar no campo **Catraca**
   (`GerenciarCatraca.xaml:19-22`, 2 cliques).
2. Campo **Seu nome (fica registrado em cada pedido)**: clicar e digitar "Ana Souza" (2 letras ou mais,
   `GerenciarCatraca.cs:377`). 1 clique.
3. Cartão **Liberação manual**: campo **Motivo (obrigatório)**: clicar e digitar, por exemplo, "visitante autorizado
   pela coordenação" (5 letras ou mais, `GerenciarCatraca.cs:265`). 1 clique.
4. Clicar em **`_Liberar um giro…`** (`GerenciarCatraca.xaml:69-71`). 1 clique. Aparece o aviso amarelo
   "Confirmar: liberar um giro na Entrada Norte, sem ingresso? O motivo "…" e o nome de quem confirma ficam
   registrados." (`GerenciarCatraca.cs:261-263`).
5. Clicar em **`_Confirmar a liberação`** (`GerenciarCatraca.xaml:80`). 1 clique.
6. Ler o resultado: aviso no alto da página e linha nova em **Histórico de pedidos desta catraca** (colunas PEDIDO
   EM, O QUÊ, DETALHE, QUEM, SITUAÇÃO, RESULTADO, `GerenciarCatraca.xaml:169-183`).

**Cliques até a função:** 1. **Cliques até concluir:** 5 (1 + nome + motivo + liberar + confirmar). **Tempo:** PENDENTE.

| ID | Atrito | Onde | Princípio | Situação |
|---|---|---|---|---|
| F-15 | **"Seu nome" é digitado à mão em toda tela de ação, embora o operador já esteja logado.** O mesmo pedido de nome se repete em Gerenciar, Configurações, Sincronização, estorno em Acessos, Configuração da catraca e Parametrização. O painel nunca preenche esse campo com o usuário da sessão (o comentário ainda diz "Não há login"). Pior: o serviço grava o nome que vem no pedido de liberação, enquanto Fechar/Reabrir usam o usuário da sessão. Quem está logado como Ana pode registrar "Bruno" | `GerenciarCatraca.cs:290-301,403`; `GerenciarCatraca.xaml:24-25`; `Telas.cs:676,1033`; `Acessos.xaml:68-70`; `Gemeo.xaml:178`; `Parametrizacao.xaml:50`; serviço: `EdgeControlService.cs:874-882` x `EdgeControlService.Pessoas.cs:354,376` | P6, P10 | REPROVADO |
| F-16 | O botão **Liberar um giro…** fica desabilitado enquanto faltam nome ou motivo, sem dizer o que falta | `GerenciarCatraca.cs:70-76,265,377` | P10 | REPROVADO |
| F-17 | O texto do cartão usa "giro", "ingresso" e "mapa de giro", mas a pessoa na portaria só sabe que "não tem cartão". Diz também que a catraca ocupada por mais de 15 s não executa | `GerenciarCatraca.xaml:65-66` | P2 | REPROVADO |
| F-18 | O sentido da liberação não é escolhido nem mostrado no ato: segue o "mapa de giro" da catraca ("entrada, no padrão") | `GerenciarCatraca.xaml:65-66` | P2, P10 | PENDENTE (depende de ver o mapa e de hardware) |
| F-19 | A liberação manual não identifica a pessoa (só motivo livre) e **não entra nos números da prestação de contas** ("aparece só aqui", "ainda não entram") | `GerenciarCatraca.xaml:65-66`; `Telas.cs:820` | P6 | BLOQUEADO (relatório R6, fase 6) |
| F-20 | No ponto de partida mais comum, a linha negada do Painel ao vivo ou de Acessos, o painel "Por quê?" explica "O que fazer", mas não há botão para liberar dali: o operador troca de tela e repete catraca e nome | `PainelAoVivo.xaml:59-100`; `App.xaml:73-84` | P3 | REPROVADO |
| F-52 | A configuração da catraca tem quatro nomes e três entradas: item de menu "Configuração da catraca", botões "Configurar a catraca" e "Parametrização (em lista)" em Gerenciar, tela "Parametrização da catraca", e o item de menu "Configurações" (do evento) ao lado | `GerenciarCatraca.xaml:39-46`; `Parametrizacao.xaml:26`; `GemeoDigitalViewModel.cs:161`; `Telas.cs:1028` | P1, P2 | REPROVADO |
| F-21 | O título da tela é fixo ("Gerenciar catraca"); a catraca em uso só aparece no campo de seleção | `GerenciarCatraca.xaml:12`; `GerenciarCatraca.cs:228` | P4 | REPROVADO |

**O que está certo e foi verificado:** liberar exige motivo e dois passos, com o nome da catraca e do motivo no
aviso de confirmação, e muda o motivo cancela a confirmação anterior (`GerenciarCatraca.cs:70-90,324-330`); o
histórico guarda quem pediu, quando e o desfecho, sem presumir sucesso (`GerenciarCatraca.cs:421-428`). O papel
Portaria tem `catraca.comandar` (migração 020, linhas 93-95). APROVADO. O desfecho na catraca real é PENDENTE
(hardware).

---

## 5. Roteiro 4: cadastrar um lote de cartões inteiros pela urna

**Objetivo (como o docs/46 descreve).** Passar vários cartões novos pela urna e cadastrá-los de uma vez.

**Situação encontrada: a função não existe no painel.** Verificado:

- Nenhuma ViewModel ou tela de cartões ou lotes de cartões em `src/Desktop.ViewModels` e `src/Desktop.App/Telas`.
- Nenhum RPC de lote de cartões em `edge_control.proto` (os RPCs de lote são **só de pessoas**, linhas 50-53).
- O estudo do módulo já diz "Cadastro de cartões e importação (fase 3 — não existe)"
  (`docs/34-anexos/02-arquiteto.md:222`; a tela B7 também não foi feita).
- A urna aparece em quatro lugares, todos de outro assunto: a caixa "Leitor da urna ligado" em Configurações
  (`Configuracoes.xaml:21`), a coluna "SÓ NA URNA" de Sincronização (`Sincronizacao.xaml:53`), a caixa "Na fenda da
  urna" do Simulador (`Simulador.xaml:26`, só em simulação) e o item "Recolher cartão na urna", **desabilitado** com
  o motivo "Aguardando confirmação da Topdata" (`GerenciarCatraca.cs:343`).

**O que existe de mais próximo:** **Pessoas** › cartão **Importar planilha** › **`Escolher arquivo e _conferir…`** →
**`Apli_car importação`** (`Pessoas.xaml:231-237`; `Pessoas.cs:153-199`). Importa **pessoas com credenciais**
(modelo `modelo-pessoas.csv`), com prévia, "tudo ou nada" e **`_Desfazer o lote escolhido`**
(`Pessoas.xaml:253-254`). Não lê cartões pela urna, e só o papel com `pessoas.importar` enxerga o resultado: o
papel Portaria não tem essa permissão (migração 022, linhas 14-17).

| ID | Atrito | Onde | Princípio | Situação |
|---|---|---|---|---|
| F-22 | Lote de cartões: sem tela, sem RPC, sem menu. Não dá para medir cliques nem atrito de uma tela que não existe | `edge_control.proto:50-53`; `docs/34-anexos/02-arquiteto.md:222` | P3 | BLOQUEADO (decisão do dono sobre "GESTÃO › Cartões" + ensaio da urna na bancada) |
| F-23 | A importação de pessoas fica no fim da tela de Pessoas (último cartão da coluna direita, com rolagem) e usa os termos "prévia", "tudo ou nada", "lote" e "credencial" | `Pessoas.xaml:228-256` | P2, P10 | REPROVADO |

**Conclusão do roteiro 4: BLOQUEADO.** Depende de (a) decisão do dono sobre o fluxo de cadastro por lote e de
(b) hardware: a função do relé da urna na TopFit 4 ainda não foi confirmada pela Topdata (`GerenciarCatraca.cs:343`).

---

## 6. Roteiro 5: cadastrar um cartão recusado

**Objetivo.** Um cartão foi recusado na catraca; o operador quer cadastrá-lo para a próxima leitura passar.

**Situação encontrada.** A tela "Leituras recusadas" (docs/46 §6) não existe, nem a antiga "Cartões não reconhecidos"
(ver §0.1). O desvio que existe hoje:

1. Ver a recusa no **Painel ao vivo** ou em **Acessos**: linha em vermelho "× …". 0–1 clique.
2. Clicar em **Por quê?** na linha (`App.xaml:73-84`). 1 clique. O painel lateral mostra "O que aconteceu", "O que
   dizer à pessoa" e "O que fazer".
3. Menu **Consultar código** (`ConsultaViewModel.Titulo`, `Telas.cs:573`). 1 clique. Digitar o código outra vez (a lista
   só mostra o código mascarado: `Telas.cs:263`; no campo, passar no leitor do balcão) e **Enter** ou **`_Consultar`**
   (`Consulta.xaml:13-18`). 1 clique. Resultado: "Código não cadastrado. A catraca negaria este código."
   (`Telas.cs:617`). **Fim: não há botão para cadastrar.**
4. Para cadastrar: menu **Pessoas** (1) › **`_Nova pessoa`** (1) › preencher Nome completo (1) › **`_Gravar a ficha`**
   (1) › campo "Nova credencial" (1) digitar o código de novo › **`_Adicionar`** (1) (`Pessoas.xaml:25,174,218-224`).

**Total pelo desvio: pelo menos 9 cliques** (1 Por quê? + 1 Consultar + 1 botão Consultar + 6 do cadastro), com o código
digitado duas vezes. **Tempo:** PENDENTE.

| ID | Atrito | Onde | Princípio | Situação |
|---|---|---|---|---|
| F-24 | Não existe lugar para tratar leituras recusadas nem botão "cadastrar este código" em Consultar código; o código é apagado do campo assim que a consulta sai | `Telas.cs:604,617` | P3, P10 | BLOQUEADO (a tela proposta não foi construída; decisão do dono) |
| F-25 | O único caminho de cadastro de código amarra o cartão a uma **pessoa** (credencial exige ficha gravada); para cartão de bilheteria (sem pessoa) não há caminho | `Pessoas.cs:101,329` | P3 | BLOQUEADO (decisão: cartão avulso x cartão de pessoa) |

**Conclusão do roteiro 5: BLOQUEADO** (a função proposta não existe). Pelo desvio, REPROVADO no critério de dois
cliques do docs/46 §8.

---

## 7. Roteiro 6: fechar uma catraca e reabri-la

**Objetivo.** Impedir que qualquer pessoa passe pela catraca "Entrada Norte" (obra, evacuação parcial, defeito) e
depois voltar ao normal.

**Passos reais para fechar**

1. Menu **Gerenciar catraca** (1) ou **Gerenciar** no cartão (1). 1 clique.
2. **Rolar a página até o fim:** o cartão **Fechar a catraca** vem depois de Liberação manual,
   Mensagem no display e Manutenção (`GerenciarCatraca.xaml:61-144`). O texto atual do estado aparece
   ("Aberta: a catraca decide normalmente.", `GerenciarCatraca.cs:146`).
3. Clicar no campo **Motivo (fica registrado com o seu usuário)** e digitar 5 letras ou mais, por exemplo "troca de
   catraca" (`GerenciarCatraca.xaml:136-139`; regra `GerenciarCatraca.cs:152`). 1 clique.
4. Clicar em **`_Fechar: ninguém passa`** (`:140`). 1 clique. **Sem confirmação.** O aviso "Catraca 1 fechada: a próxima
   leitura já é negada com o motivo "catraca fechada pelo operador"." (`GerenciarCatraca.cs:174-175`) aparece no
   **topo** da página (`GerenciarCatraca.xaml:51-52`).

**Passos para reabrir:** na mesma tela, o texto muda para "Fechada por Ana Souza (ana.souza) em 09/10/2026 14:05:
troca de catraca. Ninguém passa até reabrir." (`GerenciarCatraca.cs:144-145`). Digitar **novo motivo** (5 letras ou
mais, `:155`) e clicar em **`Re_abrir`** (`:141`). 2 cliques (campo + botão). **Sem confirmação.**

**Cliques até a função:** 1. **Fechar:** 3. **Reabrir:** 2 (3 se vier de outra tela). **Tempo:** PENDENTE.

| ID | Atrito | Onde | Princípio | Situação |
|---|---|---|---|---|
| F-26 | **Catraca fechada continua "Atendendo" (verde) no Painel ao vivo, em Catracas e no cartão "Catracas 2/2 online" do topo.** A situação de fechada só é lida em Gerenciar catraca (`ListarCatracasFechadas` só é chamada ali) e nenhum campo de `LinhaDeCatraca` guarda "fechada". O operador vê tudo verde enquanto ninguém passa | `GerenciarCatraca.cs:185`; `Textos.cs:16-19,208-221`; `Telas.cs:200-206,377-400` | P5 | REPROVADO |
| F-27 | **Reabrir libera a passagem com motivo, mas sem passo de confirmação** (um clique depois do motivo). Liberação manual e Refazer conexão têm dois passos | `GerenciarCatraca.cs:153-155,158-179` x `:70-90,93-113` | P6 | REPROVADO |
| F-28 | Fechar (ninguém passa) também é um clique depois do motivo. P6 só cita "libera passagem ou apaga", então não é violação literal | `GerenciarCatraca.cs:150-152` | P6 | BLOQUEADO (decisão do dono: incluir "fechar" na regra) |
| F-29 | O botão Fechar/Reabrir está no fim de uma página com rolagem, e o aviso do resultado fica no alto: em 1366×768 o operador pode não ver que deu certo | `GerenciarCatraca.xaml:10,51-52,131-144` | P7 | PENDENTE (visual) |
| F-30 | Os dois botões ficam desabilitados com motivo curto sem dizer "mínimo de 5 letras" (o aviso só vale no rótulo "fica registrado") | `GerenciarCatraca.cs:152,155`; `GerenciarCatraca.xaml:136` | P10 | REPROVADO |
| F-31 | Três "fechar" diferentes: "Fechar a catraca" (ninguém passa), "Fechar o painel (as catracas continuam)" e "Encerrar a operação (parar as catracas)…" na bandeja | `GerenciarCatraca.xaml:133`; `BandejaDoSistema.cs:47-49` | P2 | REPROVADO |
| F-32 | O papel Portaria vê a tela e os botões, mas não tem `catraca.fechar`; o botão não está escondido: o serviço devolve o texto "Seu papel não permite…" | `Telas.cs:1320`; `InterceptadorDeSessao.cs:95-96`; migração 022, linhas 14-17; `InterceptadorDeSessao.cs:190`; `MensagemDeFalha.cs:34-35` | P9 | REPROVADO |

**O que está certo e foi verificado:** o motivo de fechar e reabrir fica registrado com o usuário da sessão
(`EdgeControlService.Pessoas.cs:354,360,376`); o texto de estado diz quem, quando e por quê
(`GerenciarCatraca.cs:144-145`). APROVADO.

---

## 8. Roteiro 7: fechar o dia e gerar a prestação de contas

**Objetivo.** No fim do dia, tirar o resumo do que passou pelas catracas e entregar um arquivo.

**Passos reais**

1. Menu **Prestação de contas** (`ContasViewModel.Titulo`, `Telas.cs:757`). Só aparece para quem tem `relatorios.ver`
   (`Telas.cs:1319`). 1 clique.
2. **A tela já abre gerada**, porque trocar de tela chama `AtualizarAsync` (`Telas.cs:1537`) e o campo **De** nasce em
   hoje (`Telas.cs:754`). Conferir a linha "Período: de … a … (horário de Brasília)" (`Contas.xaml:44-48`).
3. Se mudar o período: **De (data · hora)**, **Até (data · hora; vazio = agora)** e clicar em **`_Gerar`**
   (`Contas.xaml:15-29`). 1 clique.
4. Clicar em **Exportar para Excel** (`Contas.xaml:30-37`). 1 clique. Abre "Exportar prestação de contas" com o nome
   `prestacao-de-contas-2026-10-09-1800.csv` (`Telas.cs:905-906`; `Contas.xaml.cs:19-20`). Clicar em **Salvar**. 1 clique.
5. Mensagem "Exportado para …" (`Telas.cs:922`).

**Cliques até a função:** 1. **Até concluir:** 3 (menu + Exportar + Salvar), 4 se refizer o filtro. **Tempo:** PENDENTE.

| ID | Atrito | Onde | Princípio | Situação |
|---|---|---|---|---|
| F-33 | **"Fechar o dia" não existe.** Não há hora de corte do dia, PDF, boletim do dia nem congelamento dos números ("Fechar o corte com código de conferência" aparece desabilitado) | `Telas.cs:815,821,823` | P3 | BLOQUEADO (decisões E9, hora de corte, e E10, meia-entrada; fase 6) |
| F-34 | **O campo "De" nasce com o dia em que o programa foi aberto, não com o dia de hoje.** `HojeNoEvento()` roda uma vez no construtor. Como o painel fica aberto na bandeja, no terceiro dia "hoje" ainda é o dia 1 e a prestação soma três dias sem avisar (só a linha "Período" denuncia) | `Telas.cs:754,97`; `Contas.xaml:44-48` | P10 | REPROVADO |
| F-35 | Nove linhas desabilitadas com siglas internas ("R1 · Boletim do dia (PDF)", "E9", "E10", "SHA-256", "fase 6", "docs/25") e a etiqueta "Aguardando confirmação" repetida | `Telas.cs:813-824`; `Contas.xaml:8,117-135` | P2, P10 | REPROVADO |
| F-36 | O arquivo que vai para fora (o CSV) usa "giros pelo mapa de giro" nas linhas de Entradas e Saídas | `Telas.cs:868-869` | P2 | REPROVADO |
| F-37 | O mesmo número tem nomes diferentes: "Acessos autorizados / Passagens confirmadas / Acessos negados" no Painel, "Liberados / Com giro confirmado / Negados" na Prestação | `PainelAoVivo.xaml:21-27`; `Contas.xaml:51-55` | P2 | REPROVADO |
| F-38 | Botão "Exportar para Excel" grava `.csv` (filtro "Planilha CSV"), com ponto e vírgula e UTF-8 com BOM, pensado para o Excel em português | `Contas.xaml:35`; `Contas.xaml.cs:19-20`; `Telas.cs:849` | P2 | PENDENTE (abrir no Excel do Windows) |
| F-39 | As liberações manuais (Roteiro 3) não entram nos números do dia | `Telas.cs:820` | P6 | BLOQUEADO (R6, fase 6) |
| F-40 | Para saber se tudo subiu para a nuvem antes de fechar, o operador precisa sair da tela: o dado "Aguardando envio à nuvem" está no Painel ao vivo e na Sincronização, não na Prestação | `PainelAoVivo.xaml:33-36`; `Telas.cs:719` | P3 | REPROVADO |
| F-41 | Papel Portaria não vê a tela (sem `relatorios.ver`); nada diz quem pode gerar | `Telas.cs:1319`; migração 020, linhas 93-95 | P9 | PENDENTE (o menu esconde; falta a frase "quem libera") |

**O que está certo e foi verificado:** o período mostrado é o que o serviço aplicou, e não o digitado
(`Telas.cs:796`); "De" vazio quer dizer "desde o começo", e isso está escrito (`Contas.xaml:8`); arquivo aberto no Excel
vira mensagem, não erro (`Telas.cs:924-928`). APROVADO.

---

## 9. Roteiro 8: trocar a própria senha

**Objetivo.** A operadora Ana quer trocar a senha que usa todos os dias.

**Situação encontrada: não existe tela, menu nem botão para o usuário já logado trocar a própria senha.**
Verificado: `TrocarSenha` aparece só em `Login.cs` (a tela de entrada), em `JanelaPrincipal.xaml:214` (o formulário de
primeiro acesso) e no login de automação (`LoginDeAutomacao.cs:57`). O serviço aceitaria (`TrocarSenha` exige só estar
logado, `InterceptadorDeSessao.cs:38`); falta a tela.

**O desvio que existe hoje, e depende de um administrador:**

1. O administrador abre **Usuários** (1), seleciona a Ana na lista (1), digita uma senha em **Esqueceu a senha? Senha
   provisória (a pessoa troca no próximo acesso)** (1) e clica em **`_Redefinir a senha`** (1)
   (`Usuarios.xaml:29,65-68`; `Usuarios.cs:272`).
2. Ana clica em **`_Sair`** (no topo, `JanelaPrincipal.xaml:156-157`; só aparece se o login está ligado, `:153`) e entra
   de novo com a senha provisória.
3. O sistema mostra "Troque a senha" (`JanelaPrincipal.xaml:197`) com "Primeiro acesso ou senha redefinida: escolha uma
   senha só sua, com pelo menos 8 caracteres." e os campos **Senha atual**, **Senha nova**, **Confirme a senha nova**;
   botão **`_Trocar a senha e entrar`** (`:199-214`).

**Cliques:** o usuário comum não consegue; com administrador, 4 (admin) + 1 (Sair) + 1 (entrar) + 1 (trocar) = 7, mais
digitação. **Tempo:** PENDENTE.

| ID | Atrito | Onde | Princípio | Situação |
|---|---|---|---|---|
| F-42 | Sem caminho de autoatendimento para trocar a senha; o operador que desconfia da própria senha depende do administrador | `Login.cs:214-246` (única entrada); busca por `TrocarSenha` em `Desktop.*` | P3 | REPROVADO |
| F-43 | A senha provisória é digitada em caixa de texto normal, visível na tela da portaria (o login usa `PasswordBox`) | `Usuarios.xaml:67` x `JanelaPrincipal.xaml:192` | — (segurança) | BLOQUEADO (decisão do dono; nenhum princípio do docs/46 cobre) |
| F-44 | O mínimo de 8 caracteres só aparece no formulário de troca, e não ao redefinir; a recusa vem como texto do serviço depois de clicar | `UsuariosDoSistema.cs:75,635-637`; `Usuarios.xaml:65-68` | P10 | REPROVADO |
| F-45 | "Senha atual" no primeiro acesso é, na verdade, a senha padrão/provisória; a rotulagem não diz isso | `JanelaPrincipal.xaml:208-209` | P2 | PENDENTE (visual) |

**Conclusão do roteiro 8: REPROVADO.** O critério P3 (≤ 2 cliques) não se aplica: a função não tem entrada.

---

## 10. Roteiro 9: criar um operador de portaria que não vê o restante

**Objetivo.** O administrador cria o login "ana.portaria" para a Ana, que só deve operar a portaria.

**Passos reais**

1. Menu **Usuários** (`UsuariosViewModel.Titulo`, `Usuarios.cs:83`; a página se chama "Usuários e papéis",
   `Usuarios.xaml:7`). 1 clique. Só aparece para `usuarios.gerenciar` (`Telas.cs:1327`).
2. O formulário já abre em "Novo usuário" (`Usuarios.cs:138-140`); se estiver editando outro, clicar **`_Novo usuário`**
   (`Usuarios.xaml:26`). 0–1 clique.
3. **Nome**: clicar e digitar "Ana Souza". **Login (letras sem acento, números, ponto, hífen)**: clicar e digitar
   `ana.portaria`. **Senha inicial (a pessoa troca no primeiro acesso)**: clicar e digitar (`Usuarios.xaml:44-50`).
   3 cliques.
4. Em **Papéis**, marcar **Portaria** (`Usuarios.xaml:53-60`). 1 clique.
5. Clicar em **`_Gravar usuário`** (`:61`). 1 clique. Aviso: "Usuário ana.portaria criado. No primeiro acesso, a pessoa
   troca a senha inicial." (`Usuarios.cs:257`).

**Cliques até a função:** 1. **Até concluir:** 6 (menu + Nome + Login + Senha + Portaria + Gravar). **Tempo:** PENDENTE.

**O que o papel Portaria enxerga** (a partir das permissões de instalação: migração 020 linhas 93-95 e migração 022
linhas 15-18; o administrador pode mudar) — verificado em `Telas.cs:1312-1328,1522-1528`:

| Item do menu | Permissão exigida pelo menu | Portaria vê? | Permissão que o serviço exige para gravar |
|---|---|---|---|
| Painel ao vivo, Catracas, Acessos, Sincronização | `operacao.ver` | sim | gravar não se aplica |
| Consultar código | `codigos.consultar` | sim | — |
| Gerenciar catraca | `operacao.ver` | sim | `catraca.comandar` (tem); `catraca.fechar` (**não tem**) |
| Configuração da catraca | `operacao.ver` | **sim** | `catraca.configurar` (**não tem**) |
| Configurações | `operacao.ver` | **sim** | `configuracao.editar` (**não tem**) |
| Pessoas | `pessoas.ver` | sim | `pessoas.editar`/`bloquear` (tem); `pessoas.importar` (não tem) |
| Prestação de contas | `relatorios.ver` | não | — |
| Diagnóstico | `diagnostico.ver` | não | — |
| Perfis e horários | `cadastro.parametros` | não | — |
| Simulador | `simulador.usar` + simulação | não | — |
| **Usuários** | `usuarios.gerenciar` | **não** (APROVADO) | — |

| ID | Atrito | Onde | Princípio | Situação |
|---|---|---|---|---|
| F-46 | **"Não vê o restante" é só parcialmente verdade.** Portaria enxerga "Configurações" e "Configuração da catraca" porque o menu liga essas telas a `operacao.ver`, e não a `configuracao.editar` / `catraca.configurar`. Salvar ou aplicar é negado pelo serviço, mas o botão está visível e responde com erro | `Telas.cs:1321-1322`; `InterceptadorDeSessao.cs:67,70` (`GravarConfiguracaoDaCatraca`, `GravarConfiguracao`) | P9 | REPROVADO |
| F-47 | Nenhum botão ou campo é escondido por permissão: `Pode(...)` só é usado para o menu (`Telas.cs:1528`). Dentro das telas visíveis a Portaria vê Fechar/Reabrir, Salvar etc. e só descobre a recusa depois do clique | `Telas.cs:1525-1528`; busca por `Pode(` em `Desktop.*` | P9 | REPROVADO |
| F-48 | A descrição de cada papel só aparece em dica ao passar o mouse sobre a caixa de seleção | `Usuarios.xaml:57`; `Usuarios.cs:217` | P10 | REPROVADO |
| F-49 | A senha inicial é digitada em caixa visível e o mínimo de 8 caracteres não aparece no rótulo | `Usuarios.xaml:49-50`; `UsuariosDoSistema.cs:75` | P10 | REPROVADO |
| F-50 | Nomes parecidos para coisas diferentes: "perfil" (tipo de pessoa em Pessoas e Perfis e horários) x "papel" (permissões de quem opera) x título "Usuários" no menu e "Usuários e papéis" na página; "Perfis e horários" no menu e "Empresas, horários e perfis" na página | `Usuarios.cs:83` x `Usuarios.xaml:7`; `ParametrosDoCadastro.cs:99` x `ParametrosDoCadastro.xaml:7` | P2 | REPROVADO |
| F-51 | As tabelas de Usuários, Papéis e Pessoas não têm estado vazio com próximo passo | `Usuarios.xaml:29-37,85-92`; `Pessoas.xaml:40-48` | P10 | REPROVADO |

**O que está certo e foi verificado:** Usuários some para a Portaria (`Telas.cs:1327`, `:1528`); a tela de Pessoas da
Portaria avisa que documento e contato aparecem mascarados (`Pessoas.xaml:59-60`), e o serviço guarda o que está
gravado ao salvar (`Pessoas.cs:25-28`). O último administrador ativo não pode ser tirado
(`UsuariosDoSistema.cs:445-455`). APROVADO por leitura de código; o menu real por papel é PENDENTE (teste A6).

---

## 11. Os atritos mais graves (ordem de importância)

| Posição | ID | Atrito | Roteiros | Situação |
|---|---|---|---|---|
| 1 | F-26 | Catraca fechada pelo operador aparece verde e "Atendendo" no Painel e no topo; só se vê em Gerenciar | 2, 6 | REPROVADO (P5) |
| 2 | F-08 | A faixa diz "Tudo funcionando" com uma catraca parada; só o cartão "1/2 online" avisa | 1, 2 | REPROVADO (P5) |
| 3 | F-15 | "Seu nome" digitado à mão em pelo menos seis lugares, apesar do login, e o serviço grava o digitado nas liberações e comandos | 2, 3, 6 | REPROVADO (P6, P10) |
| 4 | F-46, F-47 | Portaria vê Configurações e Configuração da catraca; botões não são escondidos por permissão | 6, 9 | REPROVADO (P9) |
| 5 | F-42 | Não existe troca da própria senha | 8 | REPROVADO (P3) |
| 6 | F-34 | "De" da Prestação nasce na data de abertura do programa | 7 | REPROVADO (P10) |
| 7 | F-27 | Reabrir catraca libera a passagem sem confirmação | 6 | REPROVADO (P6) |

## 12. Bloqueios e decisões para o dono

| ID | O que bloqueia | Decisão ou hardware |
|---|---|---|
| F-22, F-24, F-25 | Roteiros 4 e 5: não existe cadastro de cartões, lote ou leituras recusadas | Decisão: fluxo de GESTÃO › Cartões e Leituras recusadas; cartão avulso x cartão de pessoa. Hardware: ensaio da urna (relé 2, `GerenciarCatraca.cs:343`) |
| F-33, F-39 | Roteiro 7: fechar o dia, hora de corte, PDF, liberações manuais na prestação | Decisões E9 (hora de corte) e E10 (meia-entrada); fase 6 |
| F-28 | Fechar catraca com um clique | Decisão: incluir "fechar" na regra P6 |
| F-01 | Passo de "abrir o evento" | Decisão: criar ou não uma conferência de abertura |
| F-14 | Alertas do serviço no painel | Decisão: expor `ListarAlertas` |
| F-43 | Senha visível ao digitar | Decisão de segurança |

## 13. O que ficou PENDENTE de execução no Windows

- Todo tempo de cada roteiro (§0).
- Corte do menu em 1366×768 e em 125% (F-07), posição do botão e do aviso de Fechar em Gerenciar (F-29), nomes "Gêmeo"
  no build instalado (§0.1), "Senha atual" no primeiro acesso (F-45) e abrir o `.csv` no Excel em português (F-38).
- O menu real por papel (A6) e o desfecho dos pedidos na catraca real (liberação, fechar, refazer conexão).

**Total de atritos registrados:** 52 (F-01 a F-52), dos quais 36 REPROVADOS, 10 BLOQUEADOS e 6 PENDENTES. Os itens de §0.1 são verificações de divergência entre o docs/46 e o código, e não entram nessa conta.
