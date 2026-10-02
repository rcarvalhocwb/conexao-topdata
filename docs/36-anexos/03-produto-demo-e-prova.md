# 03 — Produto, demonstração à Topdata e plano de prova

> Estudo de inovação, papel **Designer de operação e do gêmeo + QA e bancada**. Feito em
> 01/10/2026 sobre a branch `claude/gallant-wright-pdloor` (commit `23c3beb`: Etapa 0, A.1–A.9,
> B.1–B.3). Só leitura do repositório; nada foi alterado. Segue o `brief.md` à risca.
>
> **Selos de classificação** (do brief): `IMPLEMENTÁVEL AGORA` (prova em simulação/teste) ·
> `DEPENDE DE BANCADA` · `DEPENDE DA TOPDATA` · `PROPOSTA FUTURA`. Afirmação sobre a catraca cita
> `arquivo:linha`; o que não tem fonte fica `A_CONFIRMAR_COM_TOPDATA`.
>
> **Limites deste relatório.**
> - Os relatórios irmãos (engenharia e arquitetura deste mesmo estudo) não estavam disponíveis
>   quando este foi escrito. As capacidades da §1 são o **conjunto de trabalho** que este papel
>   precisa para desenhar telas, demo e testes; os nomes devem ser alinhados com os deles na
>   consolidação.
> - O **Mapa de giro** está sendo implementado por outro agente (branch `mapa-de-giro`, ainda
>   sem commit próprio em `git log main..mapa-de-giro`). Aqui ele entra como **experiência e
>   demonstração**, sobre o que o repositório já tem (`FuncaoDeLiberacaoDaEntrada`, docs/34:83,
>   :204), sem supor detalhes de código que não vi.
> - A pesquisa pública (§3) é qualitativa: amostra pequena, achada por busca na web em
>   01/10/2026. Contagens são "fontes distintas encontradas", não estatística.

---

## 0. Resumo em uma página

1. **A inteligência mora acima da DLL e fora do caminho do giro.** Tudo o que se propõe aqui
   lê o que o XAcess **já grava** (`ticket_use_attempt` com `at`, `reason`, `reader_origin` e
   `passage_confirmed_at` — `Migrations/003_ingressos_e_provedores.sql:54-66`,
   `010_origem_da_leitura.sql:15`; `device_status` com reconexões, relógio e versão aplicada —
   `006_operacao.sql:6-17`, `008_relogio_da_catraca.sql:4-7`, `013_configuracao_aplicada.sql:17-18`;
   `operator_command`; `collected_ticket`) e **explica, sugere ou alerta**. Nenhuma capacidade
   desta lista decide liberar.
2. **Sete telas/cartões novos** (§2.3): Saúde da catraca, Alertas, Ritmo das catracas (fila
   estimada), Por que negou, Check-up da catraca, Replay de incidente no gêmeo e Mapa de giro no
   gêmeo. Todos com "Por quê?" visível, selo de origem do dado e sem termo do SDK no guiado.
3. **O que o público reclama** (§3): comunicação perdida (IP/firewall), "lê mas não libera",
   catraca que não trava, sentido de giro trocado, memória cheia, hora errada, instalação
   difícil e suporte. **Cinco desses o XAcess já ataca em código** (firewall no assistente,
   acerto/conferência do relógio, coleta com gravação antes da próxima, explicação de negação,
   salva × aplicada); dois dependem de bancada; suporte e defeito mecânico estão fora do alcance
   do software.
4. **Achados públicos que viram perguntas novas à Topdata** (§4.6): a memória da Catraca 4 tem
   opção local "Para/Segue" (não só circular); o sentido de entrada se ajusta no WebServer
   (Direita/Esquerda) — o SDK desfaz isso?; integrador afirma que em off-line a catraca libera
   "qualquer entrada"; o menu Master conta giros por sentido.
5. **Demonstração de 19 min** (§4) em duas partes: simulador (9 cenas, cada frase com a prova
   que a sustenta) e bancada (3 cenas). Exige **6 ganchos novos no simulador** (§4.3), todos em
   cima do que `SimulatedDevice` já tem.
6. **Plano de prova** (§5): um teste de simulação e uma linha de bancada (ids novos no padrão do
   docs/21) por capacidade, com critério numérico. Três testes guardam a operação:
   `NOVO-LOAD-IA-01` (p95 da decisão < 150 ms com a camada ligada, variação ≤ 10 ms),
   `NOVO-CHAOS-IA-01` (camada travada não muda a decisão) e `NOVO-SOAK-IA-24H` (zero falso
   positivo em 24 h simuladas).
7. **Material** (§6): modo "Demonstração Topdata" só em simulação, vídeo das cenas gerado pelo CI
   e um documento executivo de 2 páginas cujo rascunho está na §6.3.
8. **Antes da demo, corrigir uma frase do gêmeo** que hoje diz mais do que o sistema faz:
   `Roteiros.cs:147` ("pode operar pela lista local, se tiver") — o XAcess não grava lista e,
   sem o PC, a catraca para de liberar (docs/34:38-41).

---

## 1. Capacidades consideradas (conjunto de trabalho)

| Id | Capacidade | O que compõe (evidência) | Novo em relação ao uso convencional da DLL | Classificação |
|---|---|---|---|---|
| CI-01 | **Saúde da catraca** (índice explicável) | reconexões (`device_status.reconnect_attempts`, `006:13`); erros de recepção contados (`DevicePump.cs:125, 576-598`; F6, docs/34:88); deriva do relógio (`008:4-7`); salva × aplicada (`013:17-18`; A.5/A.6, docs/34:576); taxa de "liberado sem giro" e tempo liberação→giro (`ticket_use_attempt.at` → `passage_confirmed_at`, `003:64-65`; origem 6 e 5, `origens-evento.csv:6-7`); latência da decisão (`MetricasDoEdge.cs:49-50`) | A DLL entrega eventos soltos; o índice cruza **seis sinais** por catraca e compara com as vizinhas e com a própria linha de base | IMPLEMENTÁVEL AGORA (sinais 1, 3, 4, 5); os erros de recepção e a latência **precisam ser publicados** em `device_status` (hoje ficam na memória do worker e no medidor) |
| CI-02 | **Alertas inteligentes** | os mesmos sinais + `reason` (`Ingresso.cs:25-90`) + `operator_command` | regras por janela e por comparação entre catracas, com "ciente" registrado (R8, docs/25:92-96) | IMPLEMENTÁVEL AGORA |
| CI-03 | **Ritmo das catracas (fila estimada)** | chegadas = tentativas por minuto por catraca; tempo de ciclo = `at` → `passage_confirmed_at` | estimativa de folga por catraca; **a fila física não é vista** | IMPLEMENTÁVEL AGORA (como estimativa rotulada) |
| CI-04 | **Por que negou** | `reason` + contexto (último uso, catraca, intervalo de reuso, urna) + `reader_origin` | explica cada negação em português, com o que fazer | IMPLEMENTÁVEL AGORA para regras; "a catraca recusou a liberação (retorno ≠ 0)" exige gravar `access_decision`, que hoje não é gravada pela operação (docs/34:98-100) |
| CI-05 | **Reuso e compartilhamento** | `UsosEsgotados`, `EmIntervaloDeReuso` em várias catracas, por impressão HMAC (B.1, docs/34:577) | padrão entre catracas, sem código em claro (R6, docs/25:77-84) | IMPLEMENTÁVEL AGORA |
| CI-06 | **Check-up da catraca** | `Ping`/`TestarConexao`, `ReceberVersaoFirmware` × matriz (ADR-0010), `ReceberRelogio` (EI-007), versão aplicada, mensagem temporária (EI-057), liberação manual (EI-041) + origem 6, bip (EI-048, chave desligada) | rotina guiada e auditada antes de abrir os portões, com relatório sem dado pessoal | IMPLEMENTÁVEL AGORA no simulador; cada passo DEPENDE DE BANCADA |
| CI-07 | **Replay de incidente no gêmeo** | `ticket_use_attempt`, `operator_command`, `collected_ticket`; transições de estado **se** `device_state_transition` passar a ser gravada (existe na `001`, não é gravada, docs/34:98-100) | reconstrução, numa camada visual própria, do que a catraca relatou num intervalo | IMPLEMENTÁVEL AGORA (parcial); completo exige gravar transições fora do caminho do giro |
| CI-08 | **Queda e recuperação** | coleta de bilhetes da A.9 (`collected_ticket`, gravação antes do próximo, docs/34:87, :576) | mostra quantas marcações voltaram e que nenhuma duplicou | DEPENDE DE BANCADA (chave `catraca.coletar_bilhetes` desligada; T35) |
| CI-09 | **Mapa de giro** | `FuncaoDeLiberacaoDaEntrada` (Entrada, EntradaInvertida, Saida, SaidaInvertida; docs/34:83, :204); origem 6; `LiberacoesPedidas` por sentido no simulador (`SimulatedDevice.cs:103`) | "entrada/saída" vira **nome do sistema** atribuído pelo operador a um sentido físico, editável clicando no giro do gêmeo | IMPLEMENTÁVEL AGORA para `Entrada`; as variantes invertidas DEPENDEM DE BANCADA (HIL-DIR-05/06) |
| CI-10 | **Demonstração guiada** | cenários do gêmeo (`Roteiros.cs`), `SimularLeitura` (`GemeoDigitalViewModel.cs:619`), modo simulação (docs/23) | roteiro de demo reproduzível, verificável e gravado pelo CI | IMPLEMENTÁVEL AGORA |

Fora daqui, de propósito: LLM e qualquer modelo na nuvem (PROPOSTA FUTURA opcional, sem dado
pessoal, nunca no giro — brief:9); biometria e facial (docs/34:421).

---

## 2. Experiência do operador

### 2.1 Regras de UX que valem para toda capacidade nova

1. **Sugere, não decide.** Nenhum cartão novo tem botão que libera catraca. Onde a ação é um
   comando (Check-up), ela passa pela fila auditada de sempre (`operator_command`, docs/32 §2),
   só com a catraca em `Polling`, com nome digitado.
2. **Todo número inteligente tem "Por quê?"** — um botão que abre a conta: quais sinais, de
   quando até quando, com quantas amostras. Sem amostra suficiente, o cartão diz
   "Poucos dados ainda (12 passagens; precisa de 30)" em vez de mostrar um índice.
3. **Origem do dado sempre visível**, no padrão do gêmeo (docs/33 §2): `AO VIVO`,
   `DEMONSTRAÇÃO`, `REPLAY · 19:40–19:45`, `ESTIMATIVA`. Estimativa nunca usa o tom de sucesso.
4. **Cor + símbolo + texto** (docs/27:130-143). Os três tons das capacidades novas:
   `✓ Normal`, `! Atenção`, `× Ação necessária`. "Atenção" nunca é vermelho.
5. **Sem termo do SDK no modo guiado** (GLOSSARIO; docs/34:448). "Origem 6" vira "giro
   confirmado pelo sensor"; "retorno 8" vira "o programa da catraca não carregou um componente";
   "EI-041" só no modo técnico.
6. **Selos honestos**: o que depende de bancada aparece com `Aguardando confirmação` e o motivo
   (`Pecas.cs:49-77`); **ao vivo, função não confirmada não aparece** (docs/34:456-457).
7. **1366 × 768 sem rolagem para o essencial.** Cartões novos têm altura máxima de 120 px no
   Painel ao vivo; detalhes vão para um painel lateral de 360 px, que empurra (não cobre) a lista.
   Alvos de 40–44 px, foco visível, `LiveSetting="Assertive"` só para "Ação necessária"
   (docs/27:193-205).
8. **Movimento reduzido respeitado** no replay e no mapa de giro (pendência registrada no gêmeo,
   docs/34:449).
9. **LGPD**: código sempre mascarado (últimos 2 dígitos), nunca nome; padrões de reuso usam a
   impressão HMAC da B.1, nunca o código.

### 2.2 Onde cada capacidade aparece

| Tela | O que muda | Capacidades |
|---|---|---|
| **Painel ao vivo** | faixa "Alertas" acima dos cartões (até 3, o resto em "+N"); no cartão de cada catraca, uma linha "Saúde: ✓ Normal" e "Ritmo: 68% da folga"; na lista de acessos, "Por quê?" em cada negado | CI-01, CI-02, CI-03, CI-04 |
| **Catracas** | coluna "Saúde" ordenável; filtro "precisa de atenção"; atalho "Ver no gêmeo" (resolve P15, anexo 04 §1.3) | CI-01, CI-07 |
| **Gerenciar catraca** | botão **"Fazer check-up"** ao lado de "Parametrização desta catraca"; histórico mostra o relatório do último check-up | CI-06 |
| **Parametrização** | aba Liberação ganha "Ver no gêmeo" que abre o Mapa de giro já focado no rotor; sem mudança de regra (recusar o que aguarda confirmação continua, docs/34:576 A.6) | CI-09 |
| **Gêmeo digital** | 4ª camada visual **Replay**; clique no rotor abre o **Mapa de giro**; chips do palco (relógio, configuração, memória) já propostos no docs/34:459-462 | CI-07, CI-09 |
| **Diagnóstico** | seção "Saúde por catraca" com a conta completa do índice e "Copiar relatório para o suporte" (sem dado pessoal) | CI-01, CI-06 |
| **Prestação de contas** | R6 ganha "Possível compartilhamento de ingresso" (mascarado) e R8 ganha "Alertas do dia e quando foram marcados como ciente" (docs/25:92-96); nota de rodapé "estimativas não entram nos totais" | CI-02, CI-05 |
| **Simulador** | "Cenários" (pico, desgaste, reuso, queda e recuperação), só no modo simulação | CI-10 |

### 2.3 Telas e cartões novos

#### T1 — Cartão "Saúde da catraca" (Painel ao vivo, Catracas, Diagnóstico)

**Problema real:** hoje o operador só vê "Atendendo" ou "Sem notícia". Uma catraca que reconecta
a cada 4 minutos, ou onde metade das pessoas desiste depois de liberada, parece "Atendendo".

**Layout (1366 × 768, no cartão da catraca, 1 linha):**
`! Saúde: Atenção — reconectou 4 vezes em 15 min   [Por quê?]`

**Painel lateral "Por quê?" (360 px):**
```
CATRACA 03 · Portão Norte                         AO VIVO · últimos 30 min
Saúde: ! Atenção

  Comunicação      ! 4 reconexões em 15 min (normal: até 1)
  Relógio          ✓ conferido há 12 min, diferença 0 s
  Configuração     ✓ a catraca está com a configuração salva (aplicada 18:02)
  Passagens        ✓ 3 de 112 liberados sem giro (vizinhas: 2 a 4)
  Tempo até girar  ✓ mediana 2,1 s (vizinhas: 1,9 a 2,4 s)
  Leitor           ✓ 112 leituras; nenhuma de origem desconhecida

O que fazer: confira o cabo de rede e a porta do switch desta catraca.
Se continuar, abra o Diagnóstico e copie o relatório para o suporte.
[Abrir Diagnóstico]  [Marcar como ciente]
```

- **Sem nota de 0 a 100.** Um índice numérico convida a comparar o que não é comparável; o
  operador vê o pior sinal na frente e a lista completa embaixo. A "nota" existe só no modo
  técnico, como a contagem de sinais em Atenção/Ação.
- **"Liberados sem giro" e "tempo até girar" nunca se chamam "desgaste".** O sistema não mede o
  mecanismo; mede o comportamento de quem passa. Microcópia quando o sinal sobe:
  *"Nesta catraca as pessoas estão demorando mais para girar ou desistindo mais que nas vizinhas.
  Pode ser o braço duro, o leitor mal posicionado ou a fila. Vá olhar."*
- Sinal sem dado (worker que ainda não publicou erros de recepção): linha em cinza
  "Comunicação — ainda não medido nesta versão".
- Simulação: o selo do cartão vira `DEMONSTRAÇÃO` e o texto ganha "(catraca simulada)".

#### T2 — "Alertas" (faixa no Painel ao vivo + lista em Diagnóstico)

**Problema real:** o operador olha para a fila, não para a tela. Precisa de poucos avisos, certos
e com ação.

**Faixa (altura 44 px, até 3 alertas):**
`! Catraca 05 sem leituras há 4 min — as vizinhas leram 63 no mesmo período.  [Ver]  [Ciente]`

| Alerta | Regra (proposta, calibrar no `NOVO-SOAK-IA-24H`) | Microcópia | Tom |
|---|---|---|---|
| Leitor calado | 0 leituras na catraca por ≥ 3 min **e** ≥ 20 leituras somadas por ≥ 2 vizinhas no período | "Catraca 05 sem leituras há 4 min — as vizinhas leram 63. Confira o leitor e o display." | Atenção |
| Comunicação instável | ≥ 3 reconexões em 15 min | "Catraca 03 reconectou 4 vezes em 15 min. Confira o cabo e a porta do switch." | Atenção |
| Relógio derivando | divergência > 30 s (já existe, docs/32 §1) **ou** inclinação > 2 s/h em ≥ 3 conferências | "O relógio da catraca 02 está adiantando cerca de 3 s por hora." | Atenção |
| Configuração não aplicada | salva ≠ aplicada por > 2 min sem pedido na fila | "A catraca 04 ainda está com a configuração anterior. Aplique fora do pico." | Atenção |
| Muitos desconhecidos | ≥ 10 `Desconhecido` em 5 min no evento | "12 códigos desconhecidos em 5 min. Pode ser um lote que não chegou à base. Veja a sincronização." | Ação necessária |
| Possível compartilhamento | mesma impressão negada por `UsosEsgotados` em ≥ 2 catracas em 5 min | "Um ingresso já usado foi apresentado em 2 catracas em 3 min (final ••42)." | Atenção |
| Urna cheia | origem 20 (`origens-evento.csv:20`) | "Urna da catraca 01 cheia. Esvazie ou oriente para outra catraca." | Ação necessária |

Regras de comportamento: um alerta por causa (não repete a cada minuto); some sozinho quando a
condição acaba há 2 min e fica no histórico; "Ciente" grava quem e quando (vai ao R8).

#### T3 — "Ritmo das catracas (fila estimada)" (Painel ao vivo)

**Problema real:** no pico, decidir **onde pôr um orientador** ou abrir uma catraca "QR expresso"
(docs/21 §6, docs/19 §2.5).

**Cartão (largura de uma métrica, `RayzerMetricCard`):**
```
RITMO · ESTIMATIVA            últimos 5 min
Catraca 01  ████████░░  82%   ~9 por min
Catraca 02  █████░░░░░  51%   ~6 por min
Catraca 03  ██████████  97% ! perto do limite
[Como calculamos]
```

- **"%" = chegadas por minuto ÷ ciclos por minuto que esta catraca já mostrou hoje** (ciclo =
  leitura → giro confirmado, mediana). Sem 30 ciclos, mostra "Aprendendo o ritmo (12 de 30)".
- "Como calculamos": *"Contamos as leituras por minuto e quanto tempo cada pessoa leva da
  leitura ao giro. O sistema não vê a fila: perto de 100% quer dizer que a catraca está
  atendendo no máximo do que mostrou hoje."*
- Barra com padrão (não só cor) e texto do valor; nunca verde/vermelho — tons neutro e Atenção.
- Proibido: "faltam 12 min de fila", "47 pessoas aguardando". Não há sensor para isso.

#### T4 — "Por que negou" (lista de acessos, Consulta, gêmeo)

**Problema real:** "a catraca leu e não liberou" é a dúvida nº 2 do público (§3). O operador
precisa dizer à pessoa, em uma frase, o que aconteceu e o que fazer.

**Linha da lista:** `× Negado · ingresso já usado   [Por quê?]`

**Painel "Por quê?":**
```
× Negado às 19:44:07 · Catraca 02 · leitor de QR
Ingresso ••••••42 · Venda online · Inteira (1 uso)

O que aconteceu: este ingresso já entrou às 19:31 pela catraca 04, com giro confirmado.
O que dizer à pessoa: "Este ingresso já foi usado. Procure o atendimento."
O que conferir: se a pessoa diz que não entrou, veja o replay da catraca 04 às 19:31.
[Ver replay]
```

| Motivo (`Ingresso.cs`) | "O que aconteceu" | "O que dizer à pessoa" |
|---|---|---|
| `Desconhecido` | "Este código não está na base deste PC. Base atualizada há 3 min." | "Não encontramos este ingresso. Procure a bilheteria." |
| `UsosEsgotados` | "Já entrou às 19:31 pela catraca 04 (com giro)" ou "(liberado, sem giro)" | "Este ingresso já foi usado." |
| `EmIntervaloDeReuso` | "Usado há 1 min 12 s; volta a valer às 19:46." | "Aguarde alguns minutos para usar este cartão de novo." |
| `ForaDaUrna` | "Cartão da bilheteria lido no leitor da frente; ele só vale na fenda da urna." | "Coloque o cartão na fenda da urna." |
| `Cancelado`, `Bloqueado`, `TipoInativo`, `ForaDaJanela`, `ProvedorDesabilitado`, `VendaAnteriorNaoUsada` | texto direto do motivo + quando e por quem, se houver (trilha da B.1) | "Procure o atendimento." |
| **Liberado, sem giro** (não é negação) | "Liberado às 19:44; ninguém girou até o fim do tempo de liberação. Não conta como entrada." | — |

- O texto "Acesso nao autorizado" do display continua o mesmo (o display tem 2 × 16 sem acento
  confirmado, T27); a explicação é **para o operador**, no painel.
- Quando `access_decision` passar a ser gravada: "A catraca não aceitou o pedido de liberação
  (código 3)" separado de "a regra negou" — é exatamente a reclamação "lê mas não libera".

#### T5 — "Check-up da catraca" (Gerenciar catraca)

**Problema real:** instalação e comissionamento são onde o público mais tropeça (§3) e onde o
suporte pede para "reexplicar". Antes de abrir os portões, o operador precisa de uma lista
curta que prove que cada catraca está pronta, com um relatório que o suporte entende.

**Fluxo em passos (componente "passos" do assistente, docs/27 §6):**

| # | Passo | Como (função / dado) | Automático? | Microcópia de resultado |
|---|---|---|---|---|
| 1 | Conversa com o PC | `Ping` (já no laço) | sim | "✓ Responde · 4 ms" / "× Sem resposta — confira cabo, IP e firewall" |
| 2 | Modelo e firmware | `ReceberVersaoFirmware` × matriz (ADR-0010) | sim | "✓ TopFit 4, firmware homologado" / "! Firmware não homologado — só em manutenção" |
| 3 | Relógio | `ReceberRelogio` (EI-007) | sim | "✓ Diferença de 0 s" / "! 45 s adiantado — use Acertar o relógio" |
| 4 | Configuração | versão aplicada × salva (A.5/A.6) | sim | "✓ Com a configuração salva" / "! Configuração anterior — aplique agora" |
| 5 | Display | mensagem temporária "CHECK-UP 03" por 10 s (EI-057) | pede confirmação | "A mensagem apareceu no display? [Apareceu] [Não apareceu]" |
| 6 | Leitor | operador mostra o **QR de check-up** (credencial de teste, provedor fora dos relatórios) | pede a leitura | "✓ Lido pelo leitor de QR em 0,3 s" / "× Nada lido em 30 s" |
| 7 | Giro | liberação manual com motivo "Check-up" (EI-041); a pessoa gira | pede o giro | "✓ Giro confirmado pelo sensor em 2,4 s, no sentido Entrada do mapa" |
| 8 | Bip | `AcionarBipCurto` (EI-048) | — | **"Aguardando confirmação"** enquanto `comando.bip_curto` estiver desligada (docs/32 §5) |
| 9 | Contador de giros | operador lê no menu Master → Informações → Contador giros (página pública da Topdata) e digita | manual | "Contador da catraca: 10.482 (anotado)" — comparação automática só com T17 |

- **Só com a catraca em `Polling`** e com aviso "Prefira antes de abrir os portões: a catraca
  fica ~1 min ocupada". Cada passo que comanda a catraca é um `operator_command` auditado.
- **Relatório do check-up** (copiar/salvar .txt): catraca, firmware, resultado de cada passo,
  versão da configuração (hash, A.5), sem código de ingresso nem nome além de quem fez.
- O passo 6 exige **credencial de teste que não conte** na prestação de contas: provedor
  "checkup" marcado como de teste (PROPOSTA, precisa de coluna/regra nova e teste que prove que
  não entra em R1–R7).

#### T6 — "Replay de incidente" (gêmeo digital, 4ª camada)

**Problema real:** "a pessoa diz que passou e o sistema diz que não" — hoje responder exige ler
tabela. O gêmeo já sabe desenhar leitura → decisão → giro (`CenaDaCatraca`, docs/33 §3).

**Entrada:** Catracas/Acessos → "Ver no gêmeo" num acesso, ou no gêmeo: seletor
"Replay · Catraca 04 · de 19:29 a 19:34".

**Tela:**
- Selo no alto: `REPLAY · CATRACA 04 · 19:29–19:34 · só o que foi gravado` (tom Info, nunca o
  verde do ao vivo).
- **Exclusivo** com ao vivo e com demonstração; nada vai para o serviço além da leitura.
- Linha do tempo embaixo do palco (altura 56 px, cabe em 768 px junto da narração depois que o
  P3 for resolvido — docs/34:442): marcas de leitura (●), negação (×), liberação (↑), giro (↻),
  comando do operador (✎), "sem notícia" (faixa cinza).
- Velocidade 1× / 4× / passo a passo; teclado: espaço, ←, →.
- Narração gerada **só** do que existe na base: *"19:31:02 — leitura no leitor de QR, ingresso
  ••42. 19:31:02 — liberado em 18 ms. 19:31:05 — giro confirmado pelo sensor."*
- Lacuna é dita, não preenchida: *"19:32:10 a 19:32:41 — sem notícia da catraca. O que ela fez
  nesse intervalo não foi relatado."*
- **Limitação dita na tela até as transições serem gravadas:** "Os estados da catraca entre os
  acessos não são gravados nesta versão."

#### T7 — "Mapa de giro" (gêmeo digital; decisão do dono: entrada/saída é nomenclatura do sistema)

**Problema real:** o sentido do giro é configurado em lugares diferentes (WebServer
"Direita/Esquerda", Gerenciador de Inners, função do SDK) e trocado na instalação é uma dúvida
pública recorrente (§3, item R6). No XAcess, "Entrada" e "Saída" são **nomes que o operador dá**
a cada sentido físico.

**Interação:** no gêmeo, clicar nos braços ou no rotor (ou "Mapa de giro" na barra) abre um
balão ancorado no rotor:
```
MAPA DE GIRO · Catraca 03
Visto de quem chega pela frente (lado do leitor):

  ↻ Sentido horário       →  [ Entrada ▾ ]
  ↺ Sentido anti-horário  →  [ Saída   ▾ ]

Esta catraca foi instalada: (•) com a frente para fora  ( ) invertida
                             Aguardando confirmação: só a instalação de fábrica
                             pode ser escolhida até o ensaio HIL-DIR-05/06.
[Ver giro de entrada no desenho]   [Salvar com meu nome]
```

- O desenho mostra uma **seta curva sobre o rotor** com o nome escolhido ("ENTRADA"), e o botão
  "Ver giro de entrada" anima um terço de volta **no desenho** (camada de pré-visualização com
  hachura, docs/34:453-455) — sem falar com a catraca.
- Salvar grava a camada da catraca (como a Parametrização, A.6) e entra no "o que muda
  (atual → novo)". A catraca só recebe ao **aplicar**, e o cartão mostra "salva × aplicada".
- Opções que dependem de bancada aparecem **desabilitadas com o selo e o motivo**, nunca
  escondidas e nunca clicáveis.
- Ao vivo: cada giro confirmado acende a seta do sentido nomeado; se um dia a origem 6 trouxer o
  sentido (T14), o desenho diz "sentido relatado pela catraca"; até lá diz "sentido pedido na
  liberação".
- Microcópia de ajuda: *"A catraca não sabe o que é entrada: ela sabe para que lado o braço
  girou. Aqui você diz qual lado é a entrada do seu evento."*

### 2.4 Microcópia: evitar × usar

| Evitar | Usar |
|---|---|
| "IA detectou anomalia" | "Catraca 05 sem leituras há 4 min — as vizinhas leram 63" |
| "Desgaste do braço" | "As pessoas estão demorando mais para girar nesta catraca" |
| "Fila de 47 pessoas" | "Perto do limite do que esta catraca mostrou hoje (estimativa)" |
| "ORIGEM_GIRO_CATRACA_TOPDATA" | "Giro confirmado pelo sensor" |
| "Retorno 8" | "O programa da catraca não carregou um componente — reinstale pelo assistente" |
| "Saúde 72/100" | "! Atenção — reconectou 4 vezes em 15 min" |
| "Sinal verde acendeu" | "Liberado (no desenho, o verde só marca o momento)" — já na ficha, docs/33:136 |

---

## 3. Reclamações e dúvidas públicas sobre as catracas Topdata

### 3.1 Método e limites

- Busca na web em 01/10/2026 (Reclame Aqui, centrais de ajuda de integradores — Next Fit, Pacto,
  Tecnofit, Senior, Edusoft, Pwi —, base de suporte pública da Topdata, blog técnico 4MINDS,
  Stack Overflow em português, vídeos públicos). Resumido com palavras próprias; **nenhum dado de
  quem reclamou** é reproduzido.
- **Volume é baixo e enviesado.** A página da Topdata no Reclame Aqui mostra poucas reclamações e
  "sem reputação definida" no período de 03 a 08/2026; o Stack Overflow em português quase não
  tem perguntas sobre EasyInner (uma pergunta genérica de integração encontrada). **O saber de
  integração está nas centrais de ajuda dos integradores**, que repetem os mesmos problemas —
  isso é, em si, um achado: o integrador resolve sozinho o que o equipamento não diz.
- Muitas fontes falam de **Catraca 3, Fit, Box ou linha Inner em geral**, não só da Catraca 4 /
  TopFit 4. A coluna "Linha" diz isso; transposição para a TopFit 4 é `INFERIDO` até a bancada.

### 3.2 Reclamações recorrentes

"Fontes" = fontes distintas encontradas nesta busca.

| # | Problema | Fontes (URL) | Nº | Linha | Causa provável | O XAcess resolve? Com o quê | Classificação |
|---|---|---|---|---|---|---|---|
| R1 | **Catraca "off-line" / sem comunicação** depois de trocar IP do PC, firewall ou antivírus; "responde ao ping mas não comunica" | https://ajuda.nextfit.com.br/support/solutions/articles/69000854021-topdata-mensagem-de-off-line-na-tela-da-catraca · https://sistemapacto.zendesk.com/hc/pt-br/articles/49272342587411-Catraca-Topdata-offline · https://suporte.topdata.com.br/suporte/catracainner-net-nao-comunica/ · https://suporte.topdata.com.br/suporte/equipamento-responde-ao-ping-mas-nao-comunica-com-o-software/ · https://tecnofit.my.site.com/help/s/article/como-testar-a-comunicacao-da-sua-catraca-topdata-via-software-da-fabricante | 5 | Inner em geral | a catraca aponta para o IP do servidor; PC com IP dinâmico; firewall bloqueia a porta (3570) | **Em parte, já.** O assistente cria a regra de entrada do firewall (`Edge.Supervisor/Instalacao/FirewallDasCatracas.cs:18-36`); o cartão diz "Sem notícia do programa da catraca" + último evento (docs/27 §7). **Novo:** Check-up passo 1 e Saúde "Comunicação" dizem **em que camada** parou; o check-up do PC pode avisar "IP deste PC não é fixo" (verificação de Windows, sem catraca). Não resolve rede ruim | IMPLEMENTÁVEL AGORA (diagnóstico); a rede é do cliente |
| R2 | **DLL não carrega / "Erro ao abrir porta… Retorno = 8"**; DLLs copiadas para a pasta errada; reinstalar como administrador | https://4minds.com.br/blog/4GYm-Controle-de-acesso-catraca-TopData-Erro-ao-tentar-abrir-porta----Retorno-=-8 · https://sistemapacto.zendesk.com/hc/pt-br/articles/49275507769363-Catraca-Inner-Topdata · https://tecnofit.my.site.com/help/s/article/como-testar-a-comunicacao-da-sua-catraca-topdata-via-software-da-fabricante | 3 | SDK EasyInner | DLL x86 com dependências; versão do SDK; permissão | **Em parte, já.** DLL isolada em processo x86 (ADR-0001), pacote autocontido e `verificar-ambiente.ps1` (docs/21 §0); retorno 8 mapeado como falha de dependência e nunca convertido em "sem eventos" (F6, docs/34:88). **Novo:** microcópia "o programa da catraca não carregou um componente" + passo do assistente | IMPLEMENTÁVEL AGORA (texto); HIL-STACK-01 confirma |
| R3 | **"Lê a leitura mas não libera"** e "catraca bloqueada" depois de trocar de software | https://ajuda.nextfit.com.br/support/solutions/articles/69000853235-catraca-serial-a-catraca-reconhece-a-leitura-mas-n%C3%A3o-faz-a-liberac%C3%A3o · https://www.reclameaqui.com.br/actuar-sistemas/sistema-incompativel-com-catraca-e-suporte-ineficiente_ckUm4sIsIo4r7hJ6/ · https://www.reclameaqui.com.br/topdata/suporte-manutencao-novos-negocios_AAq_yMQ0EVpgOJu0/ (cartão negado entre os itens) | 3 | serial/Inner | pulso de liberação não chega; configuração incompatível; regra do software | **Sim, para separar as causas:** "Por que negou" (T4) distingue regra × liberado sem giro; com `access_decision` gravada, distingue também "a catraca recusou o pedido". Liberação usa a função do perfil comissionado (F1, docs/34:83) | IMPLEMENTÁVEL AGORA (regra); DEPENDE DE BANCADA (recusa da catraca) |
| R4 | **Catraca não trava / gira livre** ("não trava para nenhum lado", braço bambo) | https://ajuda.nextfit.com.br/support/solutions/articles/69000854456-topdata-catraca-n%C3%A3o-est%C3%A1-travando-para-nenhum-lado · https://suporte.topdata.com.br/suporte/catraca-nao-bloqueia-o-giro/ · https://vimeo.com/706160426 · https://www.reclameaqui.com.br/topdata/suporte-manutencao-novos-negocios_AAq_yMQ0EVpgOJu0/ | 4 | Fit 3 e outras | ajuste mecânico/placa; ou catraca em off-line (o integrador afirma que, em off-line, ela libera com qualquer entrada) | **Não conserta mecânica.** Pode **detectar**, se a catraca emitir origem 6 sem liberação pedida: alerta "giro sem liberação". Se emite, é `A_CONFIRMAR` (T33 + pergunta nova T38). Mostrar o regime configurado (A.7) evita o "off-line sem saber" | DEPENDE DE BANCADA + DEPENDE DA TOPDATA |
| R5 | **Memória cheia / marcações a recuperar** | https://suporte.topdata.com.br/suporte/catraca-apresentando-mensagem-memoria-cheia/ · https://suporte.topdata.com.br/suporte/como-recuperar-os-bilhetes-da-catraca-4/ · https://sistemapacto.zendesk.com/hc/pt-br/articles/49274053942035-Catraca-Topdata-Fit-Lfd | 3 | Catraca 4, Fit | marcações off-line não coletadas; opção local "Buffer: Para ou Segue" (parar e avisar × sobrescrever a mais antiga) | **Sim, atrás de chave:** coleta da A.9 grava cada bilhete antes de pedir o próximo, sem duplicar, sobrevive a queda (`QuedaNaColetaDeBilhetesTests`, docs/34:576). Falta: contar a memória (`ReceberQuantidadeBilhetes`, T20) e saber a opção Para/Segue (T34 + nova T37) | DEPENDE DE BANCADA (INT-REC-03, CHAOS-REC-01) |
| R6 | **Sentido de giro / entrada × saída** trocado; "como deixar a saída livre" | https://suporte.topdata.com.br/suporte/e-possivel-configurar-o-sentido-da-catraca-4-atraves-do-web-server/ · https://suporte.topdata.com.br/suporte/posso-deixar-a-catraca-sempre-liberada-ou-liberada-somente-para-entrada-ou-para-saida/ · https://sistemapacto.zendesk.com/hc/pt-br/articles/49273212706195-Configurar-a-catraca-Inner-TopData-de-sa%C3%ADda-para-que-a-entrada-seja-travada-e-a-sa%C3%ADda-sempre-liberada · https://help.edusoft.inf.br/doku.php?id=help:mentorweb:configuracoes_da_catraca_topdata_inner&do= · https://suporte.topdata.com.br/suporte/como-acessar-o-contador-de-giros-da-catracas-4/ | 5 | Catraca 4 e Inner | o sentido vive em 3 lugares (WebServer Direita/Esquerda, acionamento no Gerenciador, função do SDK); lado de instalação | **Sim, como experiência:** Mapa de giro (T7) dá um lugar só, com nome do sistema, prévia no gêmeo e salva × aplicada. Variantes invertidas e "o SDK desfaz o WebServer?" dependem de bancada e da Topdata (HIL-DIR-05/06, T19, nova T36) | IMPLEMENTÁVEL AGORA (Entrada); DEPENDE DE BANCADA (invertidas) |
| R7 | **Hora/data errada** na catraca e efeitos (validade expirada por data) | https://suporte.senior.com.br/hc/pt-br/articles/4408640625172-Ronda-XT-Bloquei-de-Crach%C3%A1s-Bloqueio-de-crach%C3%A1s-na-catraca-TopData-Validade-Expirada · https://suporte.topdata.com.br/suporte/e-possivel-configurar-data-e-hora-diretamente-na-catraca-3/ | 2 | Inner/Catraca 3 | relógio não acertado; fuso | **Sim, já:** acerto a cada conexão, conferência 1 min depois e a cada hora, aviso acima de 30 s (docs/32 §1; `008:4-7`). **Novo:** alerta de deriva por inclinação | IMPLEMENTÁVEL AGORA; INT-CLK-01/02 confirmam |
| R8 | **Instalação e configuração difíceis**; "precisei reexplicar a vários técnicos"; catraca comprada e nunca posta em funcionamento | https://www.reclameaqui.com.br/topdata/catraca-de-academia_sprrgEpBxeRQv8FO/ · https://www.reclameaqui.com.br/topdata/catraca-nunca-foi-efetivada-falta-de-instalacao_fJ6K1uPA7Z7G-xWK/ · https://www.reclameaqui.com.br/actuar-sistemas/sistema-incompativel-com-catraca-e-suporte-ineficiente_ckUm4sIsIo4r7hJ6/ | 3 | geral | muitos lugares de configuração; nenhum retrato único do que está na catraca | **Em parte:** Parametrização guiada com "o que muda" e salva × aplicada (A.6) já existe; **novo:** Check-up com relatório para o suporte (T5) — o técnico recebe o retrato, não a história | IMPLEMENTÁVEL AGORA |
| R9 | **Suporte, assistência e peças** demorados; garantia discutida entre revenda e fabricante | https://www.reclameaqui.com.br/topdata/sem-assistencia-tecnica_puELGzlMrp7Suf-5/ · https://www.reclameaqui.com.br/topdata/falta-de-suporte-tecnico-para-controladores-de-acesso-fornecidos-pela-topdata_uATbdHTLQA1MowpO/ · https://www.reclameaqui.com.br/topdata/suporte-tecnico-muito-ruim_pg9I7nZz0_s_1CoR/ · https://www.reclameaqui.com.br/topdata/comprei-uma-catraca-top-data-a-7-meses-e-ha-5-nao-funciona-e-nao-consigo-contato-com-eles_4SlVlKEsGcGuj9fh/ · https://www.reclameaqui.com.br/topdata/catraca-com-defeito-na-placa_OF1Hs4pLHNqcL7aB/ | 5 | geral | rede de assistência e pós-venda | **Não.** Ajuda indireta: relatório de check-up e de saúde sem dado pessoal encurta o diagnóstico remoto (no caso resolvido, a causa foi achada por fotos: cabos soltos de fábrica) | fora do alcance do software |
| R10 | **Hardware**: display intermitente, sinal sonoro baixo, defeito de placa | https://www.reclameaqui.com.br/topdata/suporte-manutencao-novos-negocios_AAq_yMQ0EVpgOJu0/ · https://www.reclameaqui.com.br/topdata/catraca-com-defeito-na-placa_OF1Hs4pLHNqcL7aB/ | 2 | geral | defeito físico | **Não conserta.** Check-up passos 5 e 8 (display e bip) dão um teste repetível; Saúde "Comunicação" sinaliza falha intermitente de placa/cabo | IMPLEMENTÁVEL AGORA (teste); bip DEPENDE DE BANCADA |
| R11 | **QR não lê** sem a configuração certa (só "Código de barras serial", padrão livre, dígitos) | https://suporte.topdata.com.br/suporte/leitor-qr-code-nas-catracas/ · https://suporte.topdata.com.br/suporte/catraca-box-3-proxsmart-qr-code/ · https://manuais.pwi.com.br/accesscontroller/fabricante-topdata | 3 | Catraca 4, Box 3 | tipo de leitor e quantidade de dígitos | **Em parte:** lista com nomes no guiado e regra 2 (dígitos cobrem o QR) já existem (A.2/A.6, docs/34:263, :576); tipo 5 × 8 é T25 | DEPENDE DE BANCADA (NOVO-HIL-QR-02) |
| R12 | Biometria sem backup, cadastro na catraca | https://ajuda.nextfit.com.br/support/solutions/articles/69000853993-catraca-topdata-sum%C3%A1rio | 1 | Fit biometria | modelo guarda digitais localmente | **Fora do escopo** (biometria fora, docs/34:421) | — |

### 3.3 O que a pesquisa acrescenta ao estudo da catraca

| Achado público | Fonte | Consequência para o XAcess |
|---|---|---|
| Memória com opção local **"Buffer: Para ou Segue"** (parar e mostrar "Memória Cheia" × sobrescrever a mais antiga) | https://suporte.topdata.com.br/suporte/catraca-apresentando-mensagem-memoria-cheia/ | O docs/34:63-65 trata a memória como **circular**; pode ser só um dos dois modos. Pergunta nova **T37**. "Para" é pior para evento (catraca deixa de registrar?) — precisa de ensaio |
| Sentido de entrada da Catraca 4 ajustável no **WebServer** (padrão "Direita" = entrada anti-horária; "Esquerda" para horária) | https://suporte.topdata.com.br/suporte/e-possivel-configurar-o-sentido-da-catraca-4-atraves-do-web-server/ | Convive com `LiberarCatraca*Invertida` do SDK. Qual vence depois de `EnviarConfiguracoes`? Mesmo risco da urna (T19). Pergunta nova **T36**; o Mapa de giro precisa saber |
| O menu Master mostra **contador de giros por sentido** (horário × anti-horário) | https://suporte.topdata.com.br/suporte/como-acessar-o-contador-de-giros-da-catracas-4/ | Base para o passo 9 do check-up (leitura manual) e para conciliação; pela DLL é T17 |
| Integrador afirma que, **em off-line, a catraca libera com qualquer entrada**, ignorando as regras do software | https://ajuda.nextfit.com.br/support/solutions/articles/69000854456-topdata-catraca-n%C3%A3o-est%C3%A1-travando-para-nenhum-lado | Afirmação de terceiro, não primária. Se valer para a Catraca 4 com alguma configuração, pesa em D4/D5/D8. Pergunta nova **T38** |
| Página de suporte "Catraca Top (descontinuada)" com a mensagem "Aguardando Configuração" | https://suporte.topdata.com.br/categorias/catraca-top/ | Mostra que a catraca exibe estados próprios no display; o gêmeo deveria ter a lista oficial dos textos de display por estado (pergunta para T27) |

---

## 4. Roteiro de demonstração para a Topdata (15–20 min + bancada)

### 4.1 A história

- **Problema:** num evento de 12 a 30 mil pessoas (docs/14), o que dá errado na catraca não é o
  giro: é **não saber** — por que negou, por que essa catraca está lenta, o que ela fez enquanto
  o cabo estava fora, se a configuração que eu salvei é a que está nela.
- **O que a catraca já faz (e muito bem):** lê QR e cartão por leitores distintos (origens 2, 3,
  21), libera por sentido, confirma o giro pelo sensor (origem 6), avisa urna cheia (20), guarda
  marcações, tem relógio, display e bip (`origens-evento.csv`; docs/34 §3).
- **O que o XAcess acrescenta:** **lê os eventos que ela já emite com mais atenção** — decide
  local em milissegundos, separa liberado de passou, explica, compara catracas, mostra no gêmeo.
  **Nenhuma função nova na DLL ou no firmware.**

**Regras de honestidade da demo** (lidas em voz alta na cena 0):
1. Tudo que for mostrado no simulador tem a frase "simulado"; o selo `MODO SIMULAÇÃO` fica na
   tela o tempo todo (docs/23).
2. Cada afirmação tem um teste que a prova; o nome do teste aparece no rodapé da cena.
3. O que depende de vocês aparece com "Aguardando confirmação" — e é a pergunta que levamos.
4. Nada de "IA" no sentido de modelo de linguagem: são regras e estatísticas que se explicam.

### 4.2 Parte A — simulador (19 min)

Preparação: serviço em modo simulação com 4 catracas (docs/23), painel em 1366 × 768, tema
claro, base limpa, roteiro carregado pelo modo "Demonstração Topdata" (§6.1), relógio do cenário
determinístico.

| Cena | Tempo | O que mostrar | Cenário / dado simulado | Frase que acompanha | Como a Topdata verifica |
|---|---|---|---|---|---|
| 0 Abertura | 1 min | barra superior `MODO SIMULAÇÃO`; as 4 catracas "Atendendo" | partida limpa | "Vamos mostrar o que o XAcess faz com os eventos que a sua catraca já emite. Tudo nesta parte é simulado; a segunda parte é na catraca de verdade." | selo na tela; docs/23 |
| 1 O que a catraca já faz | 2 min | gêmeo, cenário "QR válido" **na simulada**: leitura → liberado → giro; Acessos mostra a tentativa com "leitor de QR" | `1000000001`, catraca 1, gira | "Leitor de QR, liberação de entrada, sensor de giro: tudo isso é a catraca. Nós decidimos localmente — esta decisão levou 18 ms — e só contamos a entrada quando o sensor confirma o giro." | linha em Acessos com origem 21; tempo no registro do worker (`SessaoDeOperacao.cs:476`); `GemeoDigitalTests` |
| 2 Liberar não é passar | 1,5 min | cenário "Liberada, mas a pessoa desiste"; Prestação de contas "autorizados sem giro: 1" | `1000000002`, `Gira: false` | "A catraca avisou o fim do tempo sem giro. Isso não entra como público. É a diferença entre autorização e passagem." | R1 mostra 1 sem giro; ADR-0007; `CenaDaCatracaTests` |
| 3 Por que negou | 2 min | mesmo QR na catraca 2 → "ingresso já usado: entrou às … pela catraca 1"; cartão na frente → "use a fenda da urna"; código desconhecido | `1000000001` (de novo), `0000000101` na frente, `9999999999` | "A sua catraca mostra 'Acesso não autorizado' — e deve. O operador precisa saber o porquê para responder à pessoa em uma frase." | painel "Por quê?" ligado ao `reason` gravado; `NOVO-SIM-NEG-01` |
| 4 Pico de público | 3 min | Painel ao vivo com chegadas nas 4 catracas; cartão "Ritmo · estimativa"; catraca 3 sobe para "perto do limite"; o p95 da decisão continua na mesma faixa | **cenário novo "pico"**: chegadas de Poisson, 10/min por catraca subindo a 18/min; tempo até girar com distribuição | "Isto é estimativa: contamos leituras e o tempo de cada giro. Não vemos a fila. Perto de 100% quer dizer que a catraca está no máximo do que mostrou hoje — é aí que o supervisor põe um orientador." | rótulo `ESTIMATIVA`; `NOVO-SIM-FLX-01`; `NOVO-LOAD-IA-01` no rodapé |
| 5 Catraca diferente das vizinhas | 2 min | na catraca 4 as pessoas demoram 2× para girar e desistem 4×; Saúde "! Atenção" com "Por quê?" | **cenário novo "catraca com sinais de atenção"**: atraso do giro e desistência maiores só na 4 | "O sistema não vê desgaste — não há sensor para isso. Ele vê que nesta catraca as pessoas demoram e desistem mais que nas outras. Alguém precisa olhar." | painel com os 6 sinais; `NOVO-SIM-SAU-01` |
| 6 Queda e recuperação | 3 min | catraca 2 "Sem notícia" 30 s; volta; operador pede "Coletar marcações": "5 coletadas: 5 gravadas"; pede de novo: "0"; Replay da catraca 2 mostra a lacuna dita em texto | **cenário novo "queda e recuperação"**: `Desconectado` por 30 s + 5 bilhetes pré-carregados na memória simulada; chave de coleta ligada **só na simulação** | "Hoje, sem o PC, a catraca para de liberar — isso é uma decisão de segurança que ainda vamos tomar com o dono do evento. O que mostramos é a outra metade: cada marcação que a catraca guardou é gravada antes de pedirmos a próxima. Se o programa cair no meio, nada se perde nem duplica — e esse teste mata o processo de verdade." | `collected_ticket` sem duplicata; `QuedaNaColetaDeBilhetesTests` (SIGKILL, docs/34:576); a frase sobre a memória é dita como pergunta (T34/T35/T37) |
| 7 Mapa de giro e check-up | 2,5 min | gêmeo → clique no rotor → Mapa: horário = Entrada; "Ver giro de entrada"; opção "instalada invertida" com selo. Gerenciar → Check-up da catraca 3: relógio 45 s adiantado e configuração não aplicada aparecem | **cenários novos**: `DesvioDeRelogio = 45 s` e `RetornoDaConfiguracao = 1` na catraca 3 | "Entrada e saída são nomes do evento; a catraca conhece sentidos. Aqui o operador diz qual sentido é a entrada, vê no desenho, e só a instalação de fábrica pode ser escolhida até o ensaio com vocês. O check-up acha, antes de abrir os portões, o que a catraca não conta sozinha." | simulador registra a função pedida por sentido (`LiberacoesPedidas`); `NOVO-SIM-CHK-01`; `NOVO-SIM-MAP-01` |
| 8 O que seus clientes reclamam | 1,5 min | painel com 5 linhas, cada uma com o teste que prova (só o que é verificável): R1, R3, R5, R6, R7 da §3.2 | — (slide no modo demo) | "Pesquisamos o que integradores e clientes publicam sobre a linha Inner. Cinco temas o XAcess já ataca em código, e mostramos o teste de cada um. Mecânica e assistência técnica não são problema de software, e não fingimos que são." | cada linha cita o teste; URLs no documento executivo |
| 9 Prestação de contas e pedido | 1,5 min | R1 do dia com os números da demo; R6 com "possível compartilhamento ••01"; tela final "O que pedimos / O que oferecemos" | números determinísticos do roteiro | "Estes números são a soma do que vocês viram: confiram. E aqui está o que precisamos de vocês para ligar o que ainda está com o selo amarelo." | `NOVO-SIM-DEMO-01` prova que os números batem com o roteiro |

**Total: 19,5 min.** Se faltar tempo, cortar a cena 2 (o conceito reaparece na 9).

### 4.3 O que precisa existir no simulador para encenar

| Cenário | Já existe | Falta | Esforço |
|---|---|---|---|
| Pico de público | `Gerador` contínuo (`SimulatedDevice.cs:142-149`); `ConducaoDeLeituras` gira assim que liberada (`ConducaoDeLeituras.cs:85-100`) | perfil de chegadas por catraca (Poisson com semente) e **atraso do giro** com distribuição (hoje gira na volta seguinte); fila de leituras sem o painel | M |
| Catraca com sinais de atenção | `RetornoForcado`, `Desconectado` (`:75-78`) | por catraca: atraso médio do giro, probabilidade de desistência, intermitência de comunicação com período | P |
| Reuso de QR / compartilhamento | códigos de teste (docs/23); `SimularLeitura` | roteiro com sequência de leituras em várias catracas no tempo | P |
| Queda e recuperação com coleta | `Desconectado`; `ComBilhetes` (`:152`); `ConfirmaNaProximaColeta` (`:175`); `ExportarBilhetes`/`RestaurarBilhetes` | **o simulador não modela o regime off-line** (`InnerSimulator.cs`, comentário da sequência oficial): as marcações "da queda" entram pré-carregadas, e a demo **diz isso**. Ligar `catraca.coletar_bilhetes` só quando o serviço está em simulação | P |
| Relógio derivando | `DesvioDeRelogio` (`:54`) | rampa (s/h) em vez de valor fixo | P |
| Configuração recusada | `RetornoDaConfiguracao` (`:71`) | nada | — |
| Urna cheia | `UrnaCheia` (`:100`) | nada | — |
| Mapa de giro | `LiberacoesPedidas` por `GateDirection` (`:103`) | mostrar no Simulador a função pedida por sentido | P |
| **Disparo pela tela** | `simulated_read` (migração 007) leva leituras do painel ao worker | um caminho igual para **cenários** (ex.: tabela `simulated_scenario` ou RPC `IniciarCenarioDeSimulacao`), **recusado fora do modo simulação**, como `SimularLeitura` (docs/23 "Segurança") | M |
| Determinismo para o CI | relógio injetável (`InnerSimulator.cs:27-31`) | semente única por roteiro; o relógio do serviço também injetável no modo demo | M |

### 4.4 Parte B — bancada (com a TopFit 4 real; 15 min, depois da Parte A ou em outra visita)

Só depois de o docs/21 §1–§5 ter sido aprovado. Cada cena reutiliza um passo do roteiro.

| Cena | O que mostrar | Passo do docs/21 | Frase |
|---|---|---|---|
| B1 Girando de verdade | QR real libera, gira, `GiroConfirmado (6)`; mesmo QR de novo → "já usado" com "Por quê?" | §4, §5 | "O mesmo caminho da simulação, agora com o seu sensor." |
| B2 Cabo puxado | QR liberado, cabo fora antes do giro, cabo de volta; prestação mostra "sem giro"; replay mostra a lacuna | §6B linha 5 e 7 | "O que a catraca não relatou, nós não inventamos." |
| B3 Check-up e mapa | check-up completo na catraca real; giro de entrada no sentido do mapa; contador do menu Master anotado | §6A, §6B, HIL-DIR-04 + `NOVO-HIL-CHK-01` | "Isto é o que entregamos ao técnico antes de abrir os portões." |

### 4.5 Frases proibidas na demo

"A catraca agora faz X" (ela não ganhou função) · "funciona off-line" (não hoje) · "detecta
desgaste" · "prevê a fila" · "IA" sem qualificação · qualquer número de desempenho que não tenha
vindo de teste nomeado · "a urna recolhe" (docs/21 §8).

### 4.6 O que pedimos à Topdata

Do docs/34 §10, as que **destravam capacidades desta proposta**:

| Pergunta | Destrava |
|---|---|
| **T2** retorno de "sem eventos" e o que volta com o cabo puxado | Saúde "Comunicação" sem falso alarme; reconexão em erro (F6) |
| **T4** origens 11, 14–17, 19 | Saúde "Leitor" (origens desconhecidas); replay completo |
| **T14** o `Complemento` da origem 6 traz o sentido? | Mapa de giro "sentido relatado pela catraca"; lotação (docs/29:98) |
| **T17** assinatura do contador de giros | check-up passo 9 automático; conciliação giros × passagens |
| **T20, T34, T35** memória de bilhetes (tamanho, sobrescrita, quando apaga) | Cena 6 sem ressalva; alerta "memória perto de cheia" |
| **T22** marcações por passagem off-line | conciliação da recuperação |
| **T24** mudança automática on/off-line | contingência (D8) e Saúde "Regime" |
| **T25** QR: tipo 5 ou 8; letras | R11 resolvido; parametrização sem aviso |
| **T27** unidade da mensagem temporária, acentos | check-up passo 5 e textos do display |
| **T33** quais modelos emitem origem 6; urna cheia bloqueia sozinha? | alerta "giro sem liberação" (R4) |
| **T13, T19** padrões da DLL; SDK desfaz o WebServer? | check-up "a catraca está como pensamos" |

**Perguntas novas, nascidas da pesquisa pública (§3.3)** — propostas para entrar no docs/34 §10:

| # | Pergunta | Ensaio proposto |
|---|---|---|
| T36 | O sentido "Direita/Esquerda" do WebServer da Catraca 4 e as funções `LiberarCatraca*Invertida` do SDK: qual vale? Um `EnviarConfiguracoes` desfaz o ajuste do WebServer? | `NOVO-HIL-MAP-02` |
| T37 | Na Catraca 4, o "Buffer: Para / Segue" existe? Qual o padrão de fábrica? O SDK lê ou grava essa opção? Com "Para", a catraca continua liberando com a memória cheia? | `NOVO-INT-REC-08` |
| T38 | Em off-line, a Catraca 4 libera "qualquer entrada" (como integradores descrevem para outros modelos) ou só a lista? Em que configuração? | `NOVO-INT-OFF-13` |
| T39 | O contador de giros do menu Master separa os sentidos; ele é lido pela mesma função de T17? Zera? | `NOVO-HIL-CHK-02` |

### 4.7 O que oferecemos (sem prometer o que não foi provado)

1. **O relatório da bancada completo** (tabelas do docs/21 preenchidas), inclusive as
   divergências já achadas entre SDK, WebServer e matriz (T8, T9; docs/34:103-107).
2. **O simulador como modelo de comportamento** para vocês corrigirem: cada hipótese dele
   (ex.: o bilhete 128 depois da queda) está marcada e tem o ensaio que a confirma.
3. **Os cenários das reclamações públicas** (R1, R3, R5, R6, R7) em forma de roteiro de
   simulação e de check-up, que podem servir à sua base de suporte, se fizer sentido.
4. **O formato do relatório de check-up**, sem dado pessoal, como proposta de anexo padrão para
   chamados de suporte.
5. **Ensaio em campo** num evento real com métricas (p95 da decisão, giros × passagens,
   marcações coletadas), compartilhadas com vocês.

Não oferecemos: certificação, exclusividade, "solução off-line" nem funções novas na catraca.

---

## 5. Plano de prova (QA)

### 5.1 Testes que guardam a operação (valem para todas as capacidades)

| Id | Tipo | O que prova | Critério de aceite |
|---|---|---|---|
| `NOVO-LOAD-IA-01` | carga, simulador | a camada inteligente ligada não pesa na decisão | 4 catracas simuladas a 10 leituras/s por 30 min (como LOAD-IMPORT-01, docs/34:383-385): **p95 da decisão < 150 ms**, p95 com a camada ligada − desligada **≤ 10 ms**, **0** `FALHA_NA_BASE_LOCAL` |
| `NOVO-CHAOS-IA-01` | caos, simulador | a camada travada, lenta ou com exceção não muda nada no giro | analisador com atraso de 5 s e exceção a cada 10 chamadas: sequência de chamadas nativas e decisões **idêntica** byte a byte à execução sem camada (padrão dos testes congelados da A.1) |
| `NOVO-ARQ-IA-01` | arquitetura | nenhuma capacidade nova é chamada pelo `DevicePump` no passo da decisão | teste por reflexão/dependência: `Edge.Worker` não referencia o projeto da camada; ela só lê a base (como o painel) |
| `NOVO-SOAK-IA-24H` | soak, simulador | sem falso positivo nem vazamento | 10 catracas saudáveis, chegadas de Poisson com semente, 24 h simuladas: **0 alertas** e **0** catracas fora de "Normal"; memória dentro do teto do SOAK (docs/06:153-175) |
| `NOVO-SEC-IA-01` | LGPD | nenhuma tela, relatório, registro ou exportação nova tem código em claro ou nome | varredura das saídas com códigos de teste conhecidos: 0 ocorrências; padrões de reuso só por impressão HMAC |
| `NOVO-UX-IA-01` | tela | contraste AA nos dois temas, 1366 × 768, nada só por cor | `Texto_e_situacao_passam_no_contraste_WCAG_AA` estendido aos cartões novos; capturas novas no CI em 1366 × 768 (como P4, docs/34:443); cada tom tem símbolo e texto |

### 5.2 Por capacidade

| Capacidade | Teste em simulação (novo) | Linha de bancada (nova, docs/21) | Critério de aceite |
|---|---|---|---|
| CI-01 Saúde | `NOVO-SIM-SAU-01`: injeta cada sinal isolado (reconexões, deriva, configuração recusada, desistência 4×, atraso 2×, origem desconhecida) | `NOVO-HIL-SAU-01`: cabo fora 30 s três vezes em 15 min; deixar 10 liberações sem giro seguidas | Simulação: cada injeção leva **só o sinal certo** a Atenção em ≤ 2 min; 20 de 20 injeções; 0 sinais errados. Bancada: "Comunicação" vai a Atenção na 3ª queda e volta a Normal em ≤ 15 min sem queda |
| CI-02 Alertas | `NOVO-SIM-ALR-01`: cada regra da tabela T2 com caso que dispara e caso no limite que não dispara | `NOVO-HIL-ALR-01`: leitor coberto por 4 min com outra catraca lendo; urna cheia (se T33) | Detecção ≤ 4 min em 20 de 20; **0** falsos positivos no `NOVO-SOAK-IA-24H`; "Ciente" gravado com nome e hora e no R8 |
| CI-03 Ritmo | `NOVO-SIM-FLX-01`: chegadas e tempo de ciclo conhecidos | `NOVO-INT-FLX-01`: 10 passagens cronometradas por fluxo (docs/21 §6) | Simulação: erro do % ≤ 20% depois de 30 ciclos; "Aprendendo" antes disso. Bancada: tempo de ciclo do sistema dentro de ±20% do cronômetro |
| CI-04 Por que negou | `NOVO-SIM-NEG-01`: um caso por `MotivoDoUso` + "liberado sem giro" | `NOVO-INT-NEG-01`: as 6 regras do docs/21 §5, cada uma com o texto esperado | 100% dos motivos com texto próprio; texto cita catraca/hora certas do uso anterior; nenhum código em claro |
| CI-05 Reuso | `NOVO-SIM-RUSO-01`: 20 reusos em catracas diferentes + 20 usos legítimos de passe de 2 usos (`2000000004`) | `NOVO-INT-RUSO-01`: mesmo QR em duas catracas reais em 1 min | 20 de 20 detectados; **0** alertas nos passes de 2 usos |
| CI-06 Check-up | `NOVO-SIM-CHK-01`: catraca saudável + 4 falhas injetadas (desconectada, relógio 45 s, configuração recusada, firmware não homologado) | `NOVO-HIL-CHK-01`: check-up completo na TopFit 4; `NOVO-HIL-CHK-02` (T39) contador do Master | Simulação: 4 de 4 falhas apontadas no passo certo; saudável 9 de 9 passos ✓ (bip "Aguardando confirmação"); cada passo que comanda a catraca vira um `operator_command` concluído. Bancada: relatório gerado; giro de teste não aparece em R1–R7 |
| CI-07 Replay | `NOVO-SIM-RPL-01`: intervalo com leituras, negações, giros, comando manual e queda | `NOVO-INT-RPL-01`: docs/21 §6B linha 5 reproduzida no replay | Tudo que está na base no intervalo aparece, **na ordem**; **nada** que não está na base aparece (replay ⊆ gravado); lacuna dita em texto |
| CI-08 Queda e recuperação | já existem `ColetaDeBilhetesTests`, `QuedaNaColetaDeBilhetesTests` (docs/34:576) | `INT-REC-03`, `CHAOS-REC-01`, `NOVO-INT-REC-07` (docs/21 §6F) + `NOVO-INT-REC-08` (T37) | 0 perdidos, 0 duplicados; bancada como no docs/21 §6F |
| CI-09 Mapa de giro | `NOVO-SIM-MAP-01`: para cada perfil, a liberação de entrada chama a função certa (EI-041 a 044) e o desenho anima o sentido nomeado | `HIL-DIR-04` a `-06` (docs/21 §6E) + `NOVO-HIL-MAP-01` (giro no sentido do mapa, 10 de 10) + `NOVO-HIL-MAP-02` (T36, WebServer × SDK) | Simulação: 4 de 4 perfis; opções invertidas não podem ser salvas sem a bancada (como a A.6). Bancada: 10 de 10 giros no sentido nomeado como entrada |
| CI-10 Demo guiada | `NOVO-SIM-DEMO-01`: o roteiro da §4.2 roda inteiro no CI com relógio e semente fixos | — | cada cena termina com o estado esperado; os números da cena 9 batem com o roteiro; o serviço **recusa** o modo demo fora da simulação |

### 5.3 Ordem dos testes na esteira

1. Unidade (Linux): regras de saúde, alertas, ritmo e textos de "Por que negou" como **funções
   puras** sobre linhas da base (como `ConfiguracaoComRecuo`).
2. Integração (Linux, simulador): `NOVO-SIM-*`, `NOVO-ARQ-IA-01`, `NOVO-CHAOS-IA-01`.
3. Carga/soak (agendado): `NOVO-LOAD-IA-01`, `NOVO-SOAK-IA-24H` com relatório publicado.
4. Windows (CI com tela): capturas das telas novas em 1366 × 768 e o vídeo da demo (§6.2).
5. Bancada: linhas `NOVO-HIL-*`/`NOVO-INT-*`, acrescentadas ao critério de aprovação do docs/21 §9.

---

## 6. Material da demonstração

### 6.1 Modo "Demonstração Topdata" no painel

- **Só existe com o serviço em modo simulação** (o serviço recusa o pedido fora dele, como
  `SimularLeitura`). Entra pelo Simulador → "Roteiro de demonstração".
- **Painel lateral de roteiro** (360 px, à direita, empurra a tela): número e título da cena,
  a frase (para o apresentador ler), "O que conferir" (o teste e onde olhar), botões
  "Preparar cena", "Próxima", "Repetir". Teclas: F8 próxima, F7 repetir.
- Barra superior: `MODO SIMULAÇÃO · DEMONSTRAÇÃO TOPDATA · cena 4 de 9` (tom Atenção, como a
  simulação, P11).
- "Preparar cena" dispara o cenário no simulador (§4.3) e navega para a tela certa; a
  apresentação continua clicando nas telas de verdade — o roteiro não "finge" telas.
- Ao fim, exporta um `.txt` com o que foi mostrado, os números e os testes citados.

### 6.2 Vídeo gravado pelo CI

- Mesma infraestrutura das capturas (`CapturaDeTela.cs`, janela sem moldura 1366 × 768,
  docs/34:443): roda `NOVO-SIM-DEMO-01` com relógio fixo e grava **uma sequência de quadros por
  cena** (PNG), nos dois temas.
- Montagem do vídeo (MP4) por `ffmpeg` no runner Windows, se disponível; **senão**, publica a
  sequência de PNG e um GIF por cena (`A_CONFIRMAR` a ferramenta no runner).
- Legenda gravada no quadro com a frase da cena e o nome do teste; selo `SIMULAÇÃO` sempre
  visível. Artefato do CI: `demo-topdata-{data}-{commit}`.
- Uso: enviar antes da reunião e servir de plano B se a máquina falhar.

### 6.3 Documento executivo (2 páginas) — rascunho de conteúdo

**Página 1 — "O que fizemos com a sua catraca"**
- *Uma frase:* "O Rayzer XAcess usa 100% do que a Catraca 4 já emite — leitores, sentido,
  sensor de giro, relógio, memória — e acrescenta, no PC do evento, decisão local, explicação e
  comparação entre catracas. Nenhuma função nova na DLL ou no firmware."
- *Três números com o teste ao lado* (só os que existirem no dia): p95 da decisão
  (`NOVO-LOAD-IA-01`); marcações recuperadas sem perda nem duplicata depois de queda do processo
  (`QuedaNaColetaDeBilhetesTests`); falsos alertas em 24 h simuladas (`NOVO-SOAK-IA-24H`).
- *O que o operador ganha* (5 linhas): por que negou; saúde de cada catraca; ritmo estimado;
  check-up antes de abrir; replay de incidente.
- *O que está provado onde:* tabela de 3 colunas — "no simulador", "na bancada", "aguardando
  vocês".

**Página 2 — "O que seus clientes publicam, e o que pedimos"**
- Tabela curta da §3.2 (R1, R3, R5, R6, R7, R8) com "como o XAcess trata" e a URL de uma fonte.
- "O que pedimos": T2, T14, T17, T25, T34/T35, T36–T39, com uma linha de por quê cada.
- "O que oferecemos": §4.7.
- Rodapé: "Afirmações sobre a catraca seguem o manual e o SDK 6.0.2.0 conforme o repositório;
  nada aqui foi ensaiado em campo ainda." (até a bancada mudar isso).

### 6.4 Folha de verificação para o engenheiro da Topdata

Uma tabela: afirmação da demo → teste → onde conferir na tela/base → selo. É a mesma do
"O que conferir" do modo demo, impressa. Serve para a Topdata **auditar** a demonstração.

---

## 7. Ranking — implementar já (para a demo) e depois

| # | O quê | Por quê agora | Classificação | Esforço |
|---|---|---|---|---|
| 1 | Corrigir `Roteiros.cs:147` e revisar toda narração do gêmeo contra o docs/34 §1 | a demo não pode começar dizendo mais do que o sistema faz | IMPLEMENTÁVEL AGORA | P |
| 2 | "Por que negou" (T4) com regras atuais | resposta direta à reclamação R3; usa só o que já é gravado | IMPLEMENTÁVEL AGORA | P |
| 3 | Guardas da operação: `NOVO-ARQ-IA-01`, `NOVO-CHAOS-IA-01`, `NOVO-LOAD-IA-01` | sem eles nenhuma capacidade entra | IMPLEMENTÁVEL AGORA | M |
| 4 | Ganchos do simulador (§4.3) + disparo de cenário só em simulação | pré-requisito de toda a Parte A | IMPLEMENTÁVEL AGORA | M |
| 5 | Saúde da catraca com 4 sinais já gravados (reconexões, relógio, configuração, giro/desistência) + publicar erros de recepção | o cartão que mais muda o dia do operador | IMPLEMENTÁVEL AGORA | M |
| 6 | Alertas (leitor calado, comunicação, relógio, configuração, desconhecidos) + "Ciente" no R8 | poucos, certos, com ação | IMPLEMENTÁVEL AGORA | M |
| 7 | Mapa de giro no gêmeo (com o agente que o implementa): balão no rotor, prévia hachurada, selo nas invertidas | decisão do dono; responde R6 | IMPLEMENTÁVEL AGORA (Entrada) | M |
| 8 | Check-up da catraca (passos 1–7; 8 e 9 como selo/manual) + relatório | responde R1, R8, R10; vira o kit da bancada | IMPLEMENTÁVEL AGORA no simulador | M |
| 9 | Modo "Demonstração Topdata" + `NOVO-SIM-DEMO-01` + vídeo do CI | reproduzível e auditável | IMPLEMENTÁVEL AGORA | M |
| 10 | Ritmo das catracas (estimativa rotulada) | cena do pico; baixo risco | IMPLEMENTÁVEL AGORA | P |

**Depois:**
- Replay completo com transições gravadas (`device_state_transition` fora do caminho do giro) e
  o P3 do gêmeo resolvido (Etapa C) — **M/G**.
- "A catraca recusou a liberação" no Por que negou (gravar `access_decision`) — **M**,
  DEPENDE DE BANCADA.
- Reuso/compartilhamento no R6 — **P**, depois que a B.1 tiver a impressão em `ticket_use_attempt`.
- Alerta "giro sem liberação" (R4) — DEPENDE DA TOPDATA (T33, T38).
- Check-up passo 9 automático (T17, T39) e alerta de memória (T20, T34, T37) — DEPENDE DA TOPDATA.
- Sentido relatado pela catraca no Mapa de giro (T14) — DEPENDE DA TOPDATA.
- Resumo em linguagem natural do dia por modelo na nuvem — PROPOSTA FUTURA opcional, sem dado
  pessoal, nunca no giro.
