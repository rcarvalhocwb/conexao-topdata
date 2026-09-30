# 34 — Estudo do módulo catraca: funções, parametrização, cartões e gêmeo

> Estudo feito em 30/09/2026, sobre o commit `78517de`, por quatro agentes especialistas que
> trabalharam em paralelo e só leram o repositório:
>
> | Agente | Relatório completo |
> |---|---|
> | Engenheiro de integração Topdata | [01-engenheiro-topdata.md](34-anexos/01-engenheiro-topdata.md) |
> | Arquiteto do XAcess | [02-arquiteto.md](34-anexos/02-arquiteto.md) |
> | Especialista em credenciais e LGPD | [03-cartoes-e-lgpd.md](34-anexos/03-cartoes-e-lgpd.md) |
> | Designer de operação e do gêmeo | [04-gemeo-e-ux.md](34-anexos/04-gemeo-e-ux.md) |
>
> Este documento consolida os quatro, cruza as conclusões e resolve as divergências. Os anexos
> trazem `arquivo:linha` para cada afirmação. O prompt de implementação que sai deste estudo
> está no [docs/35](35-prompt-modulo-catraca.md).
>
> **Fontes e limite.** O manual oficial em PDF e os exemplos do SDK **não** estavam disponíveis:
> a rede do ambiente bloqueia o Dropbox e os sites da Topdata. A base foi o que o repositório já
> registrou do SDK 6.0.2.0 (`docs/11`, `docs/compatibility-matrix/*.csv`, o P/Invoke gerado)
> **mais páginas públicas de suporte da Topdata**, lidas por um serviço de leitura de páginas e
> citadas com a URL. Nada foi testado numa catraca real. Selos: `FONTE_PRIMARIA`, `INFERIDO`
> (dedução, nunca vira código ligado) e `A_CONFIRMAR_COM_TOPDATA`.

---

## 1. Resumo executivo

1. **Um processo e um adaptador bons, com defeitos dormentes.** O caminho que hoje atende o
   operador (QR, urna, giro, relógio, comandos) está correto e testado no simulador. Mas há
   **oito defeitos** que só aparecem quando alguém liga uma função nova (§2). Eles vêm antes de
   qualquer funcionalidade.
2. **A catraca recebe valores que ninguém conhece.** Das cerca de 40 funções de montagem da
   configuração, o adaptador envia 10. O resto vai com o **padrão da DLL**, que não está
   documentado. Isso inclui **dígitos do cartão**, **cartão master** (passa por cima da lista),
   **WebServer** (vem ligado com senha de fábrica), **registro de acesso negado**, **tipo de
   lista** e a **mensagem de apresentação**, que por padrão pode mostrar o número do cartão no
   display. Contraria a ADR-0020.
3. **A contingência off-line não existe hoje.** A sequência oficial (cfg off-line → mudança
   automática → cfg on-line) não é seguida: a mesma configuração on-line é enviada três vezes e o
   `PingOnLine` nunca é chamado. **Se o PC cair, a catraca para de liberar.** Isso não é
   necessariamente ruim (é fail-secure), mas precisa ser uma **decisão** (D5, D8), não um acidente.
4. **O leitor de QR da Catraca 4, segundo a Topdata, só funciona como "Código de Barras
   Serial"**, com padrão Livre e dígitos variáveis de 4 a 16. No SDK isso aponta para o tipo **5**,
   mas a bancada usa **8** por padrão. A correspondência é `INFERIDO` e a bancada decide (T25).
5. **A Catraca 4 não tem LEDs verde e vermelho acionáveis pelo SDK**: essas funções são da Linha 3.
   Na Linha 4 o retorno ao usuário é o **display e o bip**. O gêmeo hoje mostra os sinais
   luminosos como "Disponível", o que precisa virar "Aguardando confirmação".
6. **Cadastro e importação de cartões (fase 3) não existem**, mas a base está pronta: `ticket`
   já trata o cartão da bilheteria como recipiente, com código único no evento e texto preservado.
   Faltam tipos, cadastro, importação, desfazer e trilha.
7. **Importar é perigoso para a operação.** O SQLite tem **um escritor**; uma importação grande
   numa transação só segura esse escritor e **trava a decisão de todas as catracas do worker**
   por até 5 s, e depois nega com `FALHA_NA_BASE_LOCAL`. A importação precisa de prévia sem
   escrita e aplicação curta, medida por um teste de carga (LOAD-IMPORT-01).
8. **Leitura e cadastro não comparam o mesmo texto.** A decisão compara a leitura crua; o cadastro
   guarda o texto normalizado pelo perfil do provedor. Com o perfil `raw` de hoje não há efeito,
   mas qualquer perfil que complete zeros quebra o casamento. A sincronização também grava a
   referência externa **sem** normalizar, ao contrário do balcão.
9. **Lista gravada na catraca:** até **15.000 posições**, e **cada par cartão × tabela de
   horário ocupa uma**. Mudar um cartão exige **reenviar a lista inteira** e não há leitura de
   volta documentada. Para 30 mil pessoas a lista branca **não cabe**. Hoje ela nem é usada,
   porque a contingência não existe (item 3). É decisão do dono (D3, D4) antes de ser código.
10. **A memória de marcações da catraca é circular**: 30.000 registros, e a mais nova
    **sobrescreve a mais antiga em silêncio**. O produto também **descarta** o bilhete coletado
    (dormente, mas perde evento no dia em que a coleta for ligada).
11. **LGPD:**
    - o relé guarda o corpo do webhook da Zet (nome, CPF, e-mail, telefone) e um gatilho **proíbe
      apagar**, o que impede qualquer prazo de retenção;
    - o redator de log deixa passar `card_number`, `titular`, `email` e parecidos, porque compara
      o nome do campo por igualdade exata;
    - tipos como "PCD" ligados a uma pessoa identificada são **dado de saúde**.
12. **O gêmeo é honesto no essencial**, mas tem falhas que enganam o operador:
    - no tema escuro, o seletor "Demonstração/Ao vivo" fica ilegível e a lista de peças sai branca;
    - a captura do CI não prova 1366×768;
    - os botões desabilitados de Gerenciar catraca parecem clicáveis.

---

## 2. Defeitos no código atual (corrigir antes de funções novas)

| # | Defeito | Onde | Hoje | Quando acorda | Correção |
|---|---|---|---|---|---|
| F1 | **Dupla inversão de sentido**: o pump troca Entrada por Saída e o adaptador troca de novo para a variante invertida → `LiberarCatracaSaidaInvertida` | `DevicePump.cs:524-526`; `TopdataInnerAdapter.cs:240-250` | dormente (`SentidoInvertido` é sempre `false`) | catraca instalada à esquerda | **Corrigido na Etapa 0 (0.1):** `GatePhysicalProfile.FuncaoDeLiberacaoDaEntrada` (Entrada, EntradaInvertida, Saida, SaidaInvertida; padrão Entrada = comportamento de hoje) traduzido um para um; o laço não inverte e o adapter não consulta perfil. Teste de ponta a ponta `LiberacaoDePontaAPontaTests` (laço + adapter + costura falsa, ingresso e liberação manual). Qual valor serve à catraca à esquerda: HIL-DIR-05/06 |
| F2 | **Dígitos variáveis nunca enviados** (`InserirQuantidadeDigitoVariavel`, EI-012, nem está na costura) | `TopdataInnerAdapter.cs:155-181`; `IEasyInnerNative.cs` | **ativo**: a catraca usa o padrão da DLL | já | **Corrigido na Etapa 0 (0.4):** EI-012 entrou na costura (`IEasyInnerNative`, `EasyInnerReal`, `CosturaFalsa`); o adapter envia uma chamada por tamanho, logo após as funções de cartão e antes de `EnviarConfiguracoes`, **só com a chave técnica `catraca.enviar_digitos_variaveis`** (`edge_setting`, sem tela, **desligada**). Com a chave desligada, a sequência nativa é idêntica à de antes e **a catraca segue com o padrão da DLL** para dígitos variáveis. Liga depois de HIL-CARD-02 (e T30) |
| F3 | **Sequência oficial não seguida**: 3 envios iguais; EI-029 e `PingOnLine` nunca chamados | `DevicePump.cs:306-308, 380-394` | **ativo** (sem contingência) | já | Etapa A.7, atrás de chave (INT-SM-021, T24) |
| F4 | **Sinal da catraca vira leitura**: origens 7, 20 e sensores caem na decisão e o display mostra "Acesso nao autorizado" | `DevicePump.cs:448-455` | dormente (urna e sensores desligados) | ao ligar urna ou sensores | **Corrigido na Etapa 0 (0.2):** `EventOrigin.EhLeitura` (1, 2, 3, 21, conferido contra `origens-evento.csv`); em `Polling`, o resto vai a `aoReceberEvento` com o bruto preservado e dispara `SinalDaCatraca` (Polling → Polling), sem decisão, sem negação no display e sem rearmar o leitor. Testes `SinalDaCatracaTests` (simulador) e `Matriz_e_dominio_concordam_sobre_o_que_e_leitura` |
| F5 | **Bilhete coletado é descartado** (sai da memória da catraca e ninguém grava) | `DevicePump.cs:475-495` | dormente (coleta nunca disparada) | ao ligar a coleta | Etapa A.9 (gravar antes do próximo, R-68) |
| F6 | **Retorno ≠ 0 vira "sem eventos"**, inclusive o 8 (falha de dependência) | `TopdataInnerAdapter.cs:216` | **ativo** (queda fica invisível até o watchdog) | já | **Corrigido na Etapa 0 (0.6):** `AdapterResult` leva a função (`Funcao`, `Significado`) e `RetornosDocumentados` mapeia só o que a matriz FUN diz (EI-001 9; EI-002 2/3/4–6; 128/129/130 das funções de montagem e de EI-034), conferido nas duas direções por teste de contrato. `AguardarEvento` preserva o retorno ≠ 0 com o seu status (8 segue falha de dependência) e conta (`TopdataInnerAdapter.ErrosDeRecepcao`, `DeviceSlot.ErrosDeRecepcao`); reconectar (disjuntor, backoff, reconexão) só com a chave técnica `catraca.reconectar_em_erro_de_recepcao` (lida pelo worker ao subir e aplicada em `TopdataInnerAdapter.ReconectarEmErroDeRecepcao`; o simulador segue declarando a queda por si), **desligada** até HIL-EVT-01: continua `A_CONFIRMAR` que a DLL devolva 0 com origem 0 quando não há evento, e se devolver ≠ 0 a catraca reconectaria sem parar. Desligada, o erro é contado e registrado com o bruto e o laço segue como "sem eventos" |
| F7 | **Mensagem padrão enviada no meio da montagem** (viola ADR-0006) e buffer sujo se um passo falhar | `TopdataInnerAdapter.cs:155-183` | **ativo** | já | **Corrigido na Etapa 0 (0.5):** `EnviarMensagemPadraoOnLine` saiu da montagem; nada com Inner entre a primeira `Definir*`/`Configurar*` e `EnviarConfiguracoes` (`AdapterTests.Nada_com_inner_entre_montar_e_enviar`). A mensagem chega uma vez por conexão pelo estado `EnviarMsgPadrao`, depois de rearmar o leitor (`SequenciaDeConexaoTests`). O buffer sujo quando um passo falha continua (a montagem não é desfeita): fica para A.1/A.7 e T30 |
| F8 | **Normalização assimétrica** e `external_ref` bruto na sincronização | `DecisorDeIngresso.cs:88`; `FonteDeCartoesDoPainel.cs:234` | dormente (perfil `raw`) | ao escolher perfil pela bancada | **corrigido na Etapa 0** (0.7): `PerfisDeLeitura.Normalizar` é a função única da leitura, cadastro, sincronização e consulta; a leitura segue `raw` até o perfil por catraca (Etapa A); a sincronização grava `external_ref` normalizado e adota a referência bruta antiga sem colidir |

Menores, mas registrados:
- **~30 campos do buffer vão com o padrão da DLL** (ADR-0020). Resolvido no modelo completo da
  Etapa A.2; o que for `A_CONFIRMAR` continua não enviado até a bancada medir o padrão.
- **`exige_reinicio`** no contrato ficou desatualizado depois do "Aplicar agora".
- `raw_event`, `access_decision`, `physical_passage` e `device_state_transition` existem, mas
  **não são gravadas** pela operação (só por testes). A fonte da prestação de contas é
  `ticket_use_attempt`.
- O comentário de `FuncaoDoAcionamento1` diz "0 a 5" e a validação aceita 0–9 (a faixa do manual
  é 0–9).
- A matriz `funcoes-easyinner.csv` tem divergências de assinatura com o SDK (`AcionarRele2`,
  `SetarBioVariavel`/`ConfigurarBioVariavel`, `TestarConexaoInner`/`PingOnline`,
  `LigarBackLite`/`DesligarBackLite`). Corrigir a matriz (anexo 01 §0.7).
- `ReceberDigitalUsuario` aparece **duas vezes** com assinaturas diferentes no P/Invoke
  gerado; uma delas está errada. Remover a que não está no inventário antes de qualquer uso (T31).

---

## 3. O que a catraca oferece

### 3.1 As 265 funções da DLL

| Família | Total | Com assinatura | Só o nome | No produto hoje | Para eventos |
|---|---|---|---|---|---|
| Biometria | 104 | 99 | 5 | 0 | **não** (ingresso não é biométrico; LGPD) |
| Configuração | 53 | 49 | 4 | 11 no caminho real | **sim**, e o modelo precisa cobrir todas as úteis |
| Outros (modem, impressora, TLM, sirene…) | 36 | 22 | 14 | 0 | quase nada |
| Envio | 21 | 17 | 4 | 5 | sim |
| Leitura | 16 | 10 | 6 | 3 | sim (inclui leitura de volta, após T12) |
| Tempo real (liberar, relés) | 10 | 9 | 1 | 5 | sim |
| Listas | 10 | 10 | 0 | 0 | sim, após decisão D3 |
| Sinalização (bip, LED) | 8 | 6 | 2 | 0 | bip sim; LED só Linha 3 |
| Comunicação | 5 | 5 | 0 | 4 | sim |
| Coleta | 2 | 2 | 0 | 1 (inalcançável) | sim |
| **Total** | **265** | **229** | **36** | **~27** | |

As 36 funções que só têm o nome **não podem ser declaradas**: fazer isso por dedução corrompe
a memória do processo x86 (T11).

### 3.2 Por domínio: o que existe, o que o produto usa e o que falta

| Domínio | Funções principais | No produto | A acrescentar | Evidência |
|---|---|---|---|---|
| Conexão | `DefinirTipoConexao`, `AbrirPortaComunicacao`, `Ping`, `PingOnLine` | sim, menos `PingOnLine` | `PingOnLine` periódico (só com contingência) | FP |
| Identificação | `ReceberVersaoFirmware` (+ `6xx`, `_ComComplementar`) | sim | variantes 6xx (discovery) | FP / SDK |
| Regime on-line/off-line | `ConfigurarInnerOnLine/OffLine`, `HabilitarMudancaOnLineOffLine`, `EnviarConfiguracoesMudancaAutomaticaOnLineOffLine`, `DefinirEntradasMudanca*` | parcial (sempre on-line) | sequência oficial (A.7) | FP + AC nas faixas |
| Cartão e dígitos | `DefinirPadraoCartao`, `DefinirQuantidadeDigitosCartao`, `InserirQuantidadeDigitoVariavel` | parcial (**F2**) | dígitos variáveis | FP |
| Leitores | `ConfigurarTipoLeitor` (0–8), `ConfigurarLeitor1/2` (0–4), `ConfigurarWiegandDoisLeitores`, `DefinirFuncaoDefaultLeitoresProximidade` | parcial | Wiegand e função padrão com valor explícito | FP |
| Recepção | `ReceberDadosOnLine` (+ `_ComLetras`, `_QRCodeComLetras`), `EnviarFormasEntradasOnLine` | numérica | letras só após T25 (buffer de 64 bytes pode estourar) | FP / SDK |
| Relés e giro | `ConfigurarAcionamento1/2` (funções 0–9, 0–50 s), `LiberarCatraca*` (5 variantes), `AcionarRele2` | sim, menos relé 2 | relé 2 com a urna (após bancada) | FP |
| Urna | `ConfigurarLeitor2`, `ConfigurarAcionamento2`, `AcionarRele2`, `EngolirCartao`/`DevolverCartao` | só leitura pela fenda | recolhimento real (T18, T19) | FP + SDK |
| Lista na catraca | `DefinirTipoListaAcesso`, `InserirUsuarioListaAcesso` (101 sempre libera, 102 sempre nega), `EnviarListaAcesso`, horários | não | após D3/D4 e bancada | FP |
| Bilhetes (off-line) | `ColetarBilhete`, `ReceberQuantidadeBilhetes`, `AvisarQuandoMemoriaCheia` | inalcançável (**F5**) | coleta real com gravação | FP / SDK |
| Relógio | `ReceberRelogio`, `EnviarRelogio`, `EnviarHorarioVerao` | sim (menos horário de verão) | **nunca** enviar horário de verão (não se apaga, e o Brasil não usa desde 2019) | FP |
| Display | `EnviarMensagemPadraoOnLine`, `…TemporariaOnLine`, `DefinirMensagemApresentacao*`, `DefinirMensagem*OffLine` | padrão e temporária | apresentação (para não mostrar o número do cartão) e off-line | FP / SDK |
| Bip | `AcionarBipCurto/Longo`, `DesabilitarBipCatraca` | declarado, não exposto | comando de bip (A.8) | FP |
| Segurança | `DefinirNumeroCartaoMaster`, `CartaoMasterLiberaAcesso`, `DesabilitarWebServer`, `DesabilitarBloqueioCatracaMicroSwitch` | nada (padrão da DLL) | valores explícitos após bancada | FP / SDK |

### 3.3 Fatos das páginas públicas da Topdata (novos para o projeto)

Lidos nesta sessão; URLs no anexo 01. Nenhum vira valor de enum do SDK sem bancada.

| Fato | Consequência |
|---|---|
| O leitor de QR "somente funciona com a configuração **Código de Barras Serial**", padrão Livre, dígitos variáveis 4–16; QR numérico **ou** alfanumérico | Tipo de leitor provavelmente **5**, não 8 (T25). Dígitos variáveis são obrigatórios (F2) |
| Com QR configurado, proximidade vira ABA 14 dígitos e Mifare 10 dígitos, automaticamente | Coerente com `docs/20`; confirmar na bancada (T15) |
| Memória de **30.000 marcações** na Catraca 4, **circular** | Estouro apaga evento em silêncio (T34) |
| Lista: **cada par cartão × tabela de horário ocupa uma das 15.000 posições** | O avaliador de capacidade deve contar posições, não usuários |
| Relógio mantém a hora por **cerca de 1 hora** sem energia | Acertar sempre na conexão (já feito) |
| WebServer vem **ligado, com senha de fábrica publicada**; fica indisponível com a catraca on-line; pode ser desabilitado | Desabilitar após o comissionamento (T32); nunca expor à internet |
| Tempo de acionamento **1–255 s** (padrão 5) no WebServer contra **0–50 s** no SDK; Inner **1–255** contra **1–99** | Divergências para a Topdata (T8, T9); o produto segue o SDK |
| "Possui urna" torna o leitor 2 exclusivo da urna e libera a **saída** depois do recolhimento; não há função conhecida no SDK para gravar essa opção | Um `EnviarConfiguracoes` pode desfazer o que foi ajustado no WebServer (T19) |
| Em off-line, a catraca com urna recolhe o cartão do leitor 2 e libera o giro sozinha | Relevante para a contingência (D4) |

---

## 4. Parametrização da catraca

### 4.1 Modelo de configuração proposto

Um registro **completo e versionado** por catraca (herda do evento o que não for definido nela).
O que é `A_CONFIRMAR` fica no modelo com **chave desligada**; desligado significa **não enviar**,
que é o comportamento de hoje, e fica registrado. Detalhes, faixas e fontes: anexo 01 §3.1.

| Grupo | Campos | Padrão proposto (TopFit 4, evento) | Situação |
|---|---|---|---|
| Cartão | `PadraoCartao`; `ModoDeDigitos` (Fixo/Variável); dígitos fixos 4–16 ou conjunto variável ⊂ 4..16 | Livre; Variável {4..16} | variável não enviado hoje (F2) |
| Leitores | `TipoDeLeitor` 0–8 (lista com nomes); leitor 1 e 2 (0–4); Wiegand dois leitores; função padrão da proximidade 0–12 | 5 ou 8 **pela bancada**; L1 = só entrada; L2 = entrada se urna | Wiegand e função padrão: AC |
| Recepção | variante numérica / com letras / QR com letras (do adaptador) | numérica | letras: AC (T25) |
| Acionamentos | função 0–9 e tempo 0–50 s para relés 1 e 2; lógica do relé | relé 1 = registro de entrada, 5 s; relé 2 = 0 sem urna | urna: AC (T18, T19) |
| Liberação | `FuncaoDeLiberacaoDaEntrada` (Entrada, EntradaInvertida, Saida, SaidaInvertida) | do comissionamento | corrige F1 |
| Regime | regime alvo; mudança automática 0–2 e tempo; entradas na mudança; intervalo do `PingOnLine` | on-line, sem mudança | contingência: D8 + T24 |
| Teclado e entradas | teclado e eco; formas de entrada on-line (0–7, 10–14, 100–105) | desligado; forma 7 (a de hoje) | significado dos valores: AC (T26) |
| Registro | registrar acesso negado 0–3; data/hora no evento on-line; avisar memória cheia | data/hora ligada; demais após bancada | AC |
| Segurança | cartão master (aleatório, cifrado); master libera acesso; WebServer desabilitado; bip desabilitado; micro switch (**nunca editável**) | master aleatório; WebServer desligado após comissionamento | AC (T13, T32) |
| Mensagens | padrão (≤32; ≤16 com data); apresentação de entrada e saída; off-line e de mudança | "Aproxime o ingresso"; apresentação sem o número do cartão | apresentação e off-line: AC |
| Lista | tipo de lista 0/1/2; tabelas de horário | 0 (sem lista) | D3 |
| Urna | habilitada; tempos T2, T3, T4 do docs/04 | desligada | após bancada |

### 4.2 Regras entre campos (vão para `Validar()`)

1. Variável ⇒ conjunto não vazio; Fixo ⇒ 4–16.
2. Padrão Livre com QR ⇒ o conjunto de dígitos cobre todo QR aceito pelo provedor.
3. Relé 2 ≠ 0 ⇒ leitor 2 ≠ 0 (já existe).
4. Urna habilitada ⇒ relé 2 ≠ 0 e tempo do relé 2 ≥ T2.
5. Mudança automática = 2 ⇒ regime on-line e intervalo do ping menor que o tempo da mudança.
6. Exibir data ⇒ mensagem ≤ 16.
7. Tipo de lista ≠ 0 ⇒ a lista cabe (em **posições**).
8. Lista negra ⇒ decisão D5 registrada.
9. Tipo de leitor 8 ⇒ variante de recepção com letras (até T25).
10. Forma de entrada coerente com teclado e leitores ativos (T26).
11. Tempo do relé 1 ≤ espera do giro (8 s) − margem.
12. Inner 1–99 (enquanto T9 não responder).
13. Modelo `NAO_ENSAIADO` ⇒ só aplica em manutenção (ADR-0010).

### 4.3 Ordem de envio (segue a sequência do manual; atrás de chave até INT-SM-021)

```
uma vez por worker   DefinirTipoConexao(2) → AbrirPortaComunicacao(porta)
a cada conexão
 1 conectar          Ping
 2 identidade        ReceberVersaoFirmware → matriz de compatibilidade (ADR-0010)
 3 cfg off-line      ConfigurarInnerOffLine + CAMPOS COMUNS + tipo de lista + mensagens off-line
                     → EnviarConfiguracoes → EnviarMensagensOffLine (INF)
 4 dados off-line    só se houver lista e o hash mudou: horários → lista (§5.5)
 5 mudança           HabilitarMudancaOnLineOffLine + entradas e mensagens de mudança
                     → EnviarConfiguracoesMudancaAutomaticaOnLineOffLine
 6 cfg on-line       ConfigurarInnerOnLine + os MESMOS CAMPOS COMUNS → EnviarConfiguracoes
 7 relógio           EnviarRelogio
 8 entradas on-line  EnviarFormasEntradasOnLine
 9 mensagem padrão   EnviarMensagemPadraoOnLine
10 polling           ReceberDadosOnLine ⇄ …  (+ PingOnLine se mudança = 2)
```

Os mesmos campos comuns vão nos **dois** `EnviarConfiguracoes`, porque cada envio limpa o buffer
e manda o padrão da DLL para o que faltar. A atribuição de cada `Definir*` a um enviador é
`INFERIDO` até a Topdata responder T13. Se a lista precisar ser gravada com a catraca em
off-line, a ordem atual da máquina (lista depois do cfg on-line) está errada (T21).

---

## 5. Cartões: cadastro, importação e lista na catraca

### 5.1 Modelo de domínio

**Não criar um cadastro paralelo.** `ticket` continua sendo "o código que a catraca lê e o direito
associado a ele".

| Peça | Proposta |
|---|---|
| `ticket_type` (novo) | código `^[A-Z0-9_]{2,20}$`, nome exibido, ordem, cor (token do design system), ativo, apelidos por provedor (as ~20 grafias da Zet). Tipo inativo **nega o uso**, mas continua nos relatórios |
| `ticket` (colunas novas) | `kind` (QR online, cartão da bilheteria, código de barras, staff/cortesia); `source` (sync, importação, manual, balcão, webhook); `owner_of_fields` (nuvem ou local); `status_reason`; autoria; `allowed_gates`; `batch_label`; `retention_until` |
| Estado de acesso × custódia física | acesso: válido, consumido, bloqueado, cancelado (já existe). Custódia, só para cartão da bilheteria: em estoque, em circulação, recolhido, perdido, danificado, descartado. Perdido ou danificado força bloqueio |
| `credential_event` (novo, só INSERT) | máscara do código + HMAC com chave local (acha o histórico sem guardar o número), ação, antes e depois só com campos não pessoais, motivo, quem (nome digitado + conta Windows vinda do serviço), lote, encadeamento por hash |
| `import_batch` e itens | arquivo, SHA-256, quem, quando, contagens, estado anterior de cada cartão (para desfazer) |
| Unicidade | um código normalizado = um dono no evento (já existe); reimportar o mesmo item = atualização (já existe); "mesmo código com zeros a mais ou a menos" = **aviso** na prévia, nunca junção automática |

### 5.2 Cadastro manual

Criar, editar, bloquear, desbloquear, cancelar e consultar. Motivo obrigatório (5–200) nas três
mudanças de estado. **Sem login** (decisão já registrada, docs/27): o serviço grava a **conta
Windows de quem chamou**. Ações em massa ou perigosas exigem o grupo **Administradores** e
confirmação digitada. Aprovação por duas pessoas fica para quando houver login.

### 5.3 Importação

- **Formatos:** CSV (`;`, UTF-8 com ou sem BOM, CRLF, aspas) e XLSX, lidos **só com a BCL**
  (nenhum pacote novo).
- **Zeros à esquerda:** célula numérica e notação científica do Excel são **recusadas**. Colar
  por cima do modelo apaga o formato de texto da coluna, então o importador revalida tudo.
- **Prévia sem escrita:** novos, alterados, iguais, erros por linha, duplicados no arquivo e
  possíveis duplicatas por zeros. Gera arquivo de devolução com os erros.
- **Comprimento:** pelo **perfil do provedor**, com teto 4–16. O docs/26 fala em 4–16, o perfil
  `mifare-catraca4` exige 10 e a base real tem 12 e 14 dígitos: o perfil só é escolhido depois
  da bancada (D6).
- **Idempotência:** reimportar o mesmo arquivo não muda nada; desfazer a última importação
  volta ao estado anterior (recusado se algum cartão foi usado depois). Cancelamento em massa é
  operação separada.
- **Limites:** aviso a partir de 100 mil linhas, recusa acima de 200 mil.
- **Nada sobe para a nuvem** no contrato v1.

### 5.4 Desempenho: a importação não pode parar a catraca

| Situação | Como aplicar |
|---|---|
| Fora da operação (nenhuma catraca atendendo) | uma transação, comandos preparados, eventos em lote |
| Durante a operação | fatias curtas (500–1.000 linhas, a medir); se uma falhar, o serviço desfaz as já aplicadas antes de responder. O operador vê "tudo ou nada" |
| Sempre | um lote por vez; o worker nunca importa |

**Critério (LOAD-IMPORT-01):** 30 mil e 100 mil linhas com 4 catracas simuladas lendo 10
códigos/s. O p95 da decisão fica abaixo de 150 ms e não há nenhuma `FALHA_NA_BASE_LOCAL`. O
tamanho da fatia sai desse teste.

### 5.5 Lista gravada na catraca (contingência)

| Fato | Fonte |
|---|---|
| Só serve se o PC cair com a catraca viva (T2 da ADR-0017). Hoje esse caso nem acontece (§1 item 3) | ADR-0017; docs/21 |
| Até 15.000 posições; uma por par cartão × horário | limites-de-capacidade.csv; Topdata |
| Mudar um cartão = reenviar a lista inteira; o envio trava a catraca; tempo **não publicado** | FUN:36; docs/14 |
| Sem leitura de volta documentada (`ReceberUsuarioLista` só tem assinatura) | T16 |
| Em off-line se perdem: limite de usos, intervalo de reuso, "só na urna", validade por data | anexo 03 §4.2 |
| Evento de 12 mil: lista branca cabe. Evento de 30 mil: não cabe | docs/14 §7.1 |

**Estratégia quando liberada:**
- carga completa, versionada por hash, antes de abrir os portões, em modo manutenção, uma
  catraca por vez por worker;
- a borda só recarrega se o hash mudar;
- a tela mostra a idade da lista;
- o watchdog precisa tolerar a operação longa (hoje mata o worker após 30 s).

---

## 6. LGPD e segurança

| Tema | Hoje | Proposta |
|---|---|---|
| Número do cartão | texto, mascarado na tela e no contrato; o proto proíbe as palavras de cartão | manter; nenhuma tela nova mostra o código inteiro por padrão |
| Redator de log | compara o nome do campo por igualdade exata | reconhecer padrões (`card_number`, `titular`, `email`, `telefone`, `codigo`, `qr`) e UIDs hexadecimais |
| Titular | não existe | **recusar a coluna** até existir cifra por evento (DPAPI, com a chave apagável), prazos aprovados e expurgo |
| Tipos que revelam saúde (PCD, autista) | texto livre | proibidos com titular; só em relatório agregado |
| Relé `delivery` (corpo do webhook da Zet) | gatilho **proíbe apagar** | retenção por evento (proposta: fim do dia + 7 dias), por migração nova e com registro |
| Tentativas e decisões | guardam o código | pseudonimizar depois do fechamento da prestação de contas (proposta: + 90 dias) |
| Expurgo | não existe | caso de uso com Administrador, motivo e confirmação digitada; gera registro; nunca automático sem aviso |
| Display da catraca | a mensagem de apresentação padrão pode mostrar o número do cartão | enviar mensagem de apresentação própria (após bancada) |
| WebServer | ligado com senha de fábrica | desabilitar após comissionamento (T32); VLAN e ACL |
| Cartão master | padrão da DLL | número aleatório, cifrado, custodiado (SEC-MASTER-01) |
| Biometria e facial | fora | PROPOSTA FUTURA; exige login real, base legal do art. 11 e RIPD |

Prazos são **propostas** para o jurídico validar (D7).

---

## 7. Gêmeo digital e telas

### 7.1 Diagnóstico

**O que está bom:**
- separa demonstração de ao vivo e não comanda nada;
- distingue liberado de passagem confirmada;
- não desenha meio giro nem inventa o leitor.

**O que corrigir:**

| # | Problema | Gravidade | Situação |
|---|---|---|---|
| P1 | "Demonstração/Ao vivo" ilegível no tema escuro (`RadioButton` sem estilo Rayzer) | alta | **corrigido na Etapa 0** (0.9): estilo implícito Rayzer; contraste AA testado nos dois temas |
| P2 | Lista de peças branca no tema escuro (`ListBox` sem estilo) | alta | **corrigido na Etapa 0** (0.9): `ListBox`/`ListBoxItem` com tokens; teste reprova controle interativo das telas sem estilo implícito |
| P3 | Em 1366×768, o desenho e a narração não cabem juntos (palco com altura fixa de 460 px) | alta | aberto: mexe no layout do gêmeo (Etapa C); não coube com segurança na Etapa 0 |
| P4 | A captura do gêmeo sai em 1044×768 com o menu recolhido; o CI não prova 1366×768 | alta | **corrigido na Etapa 0** (0.9): causa era o monitor 1024×768 do runner limitando a janela com moldura; a captura usa janela sem moldura de 1366×768 lógicos, o `relatorio.txt` traz tamanho e DPI de cada imagem e o CI reprova fora de 1366×768 (confirmação na próxima execução no Windows) |
| P16 | Sinais liberado/bloqueado como "Disponível" sem fonte (a Linha 4 sinaliza por display e bip) | alta (conteúdo) | **corrigido na Etapa 0** (0.9): "Aguardando confirmação" com o motivo; a narração não diz mais que a catraca acende o sinal |
| P12 | Botões desabilitados de Gerenciar catraca parecem links | média | **corrigido na Etapa 0** (0.9): desabilitado com `Rayzer.Surface.Disabled` e `Rayzer.Text.Disabled`, sem opacidade |
| P11 | "✓ MODO SIMULAÇÃO" em verde (tom de sucesso) | média | **corrigido na Etapa 0** (0.9): simulação em tom de Atenção ("!") |
| P6 | "Testar giro" é o botão azul principal, mas só gira o desenho | média | aberto |
| P13 | "Tipo de leitor" técnico no modo guiado | média | aberto |
| — | O gêmeo ignora a opção de reduzir animações do Windows | média | aberto |

### 7.2 Proposta

- **Três camadas visuais, nunca misturadas:** ao vivo, demonstração e **pré-visualização** (o
  efeito de uma configuração ainda não aplicada). A pré-visualização tem hachura, é exclusiva com
  o ao vivo e **não fala com o serviço**.
- **Função não confirmada** aparece esmaecida, com cadeado e selo "Aguardando confirmação". **Ao
  vivo, ela nunca aparece.**
- **Bip, relés, relógio e memória** viram marcações 2D sobre a peça, não geometria nova.
- **Chips no palco:**
  - "Relógio · acertado há 41 s";
  - "Configuração · salva × na catraca";
  - "Memória · lista não gravada, marcações a coletar".
- **Tempo de liberação** configurado usado também na demonstração (hoje são 15 s fixos), com anel
  de contagem.
- **Cenários novos:** aviso no display, relógio conferido, volta da comunicação, e bip, saída ou
  evacuação e recolhimento, todos "a confirmar", sem animar como se funcionassem.
- **Selos por família:**

  | Selo | Funções |
  |---|---|
  | Disponível | QR, proximidade na frente, urna (leitura), mensagens padrão, temporária e de negação, giro de entrada, tempo de liberação, relógio |
  | Aguardando confirmação | QR com letras, acentos, sinais luminosos, bip, relés, saída e dois sentidos, recolher, urna cheia, off-line, lista |
  | Não usada aqui | código de barras, teclado, data no display |
  | Fora do escopo | biometria, facial |

### 7.3 Telas do módulo

| Tela | Situação | Classificação |
|---|---|---|
| Detalhe da catraca em abas (Visão, Comandos, Parametrização, Lista, Histórico) | não existe | MELHORIA RECOMENDADA |
| Parametrização com validação no campo, "o que muda (atual → novo)", aplicar com confirmação e resultado por catraca | parcial (Configurações do evento, 5 campos, global) | MELHORIA RECOMENDADA |
| Cartões (lista, busca, detalhe, histórico, código mascarado) | não existe | PROPOSTA FUTURA (fase 3) |
| Importação em 5 passos (arquivo → mapeamento → prévia e erros → confirmar → relatório) | não existe | PROPOSTA FUTURA (fase 3) |
| Lista na catraca | não existe; a aba **não aparece** enquanto não existir | PROPOSTA FUTURA |
| Modo guiado × técnico | não existe | MELHORIA RECOMENDADA |

Nenhuma tela aparece no menu antes de ter função.

---

## 8. Riscos técnicos das funções novas

A DLL é x86, bloqueante e não thread-safe, com até ~30 equipamentos por instância e uma porta
por worker. Uma chamada longa congela todas as catracas daquele worker.

| Função | Risco principal | Mitigação |
|---|---|---|
| `InserirQuantidadeDigitoVariavel` e demais `Definir*` | buffer **global**: a montagem de uma catraca não pode intercalar com a de outra; a função acumula entre tentativas (T30) | montar e enviar como unidade atômica; reset no início |
| Três envios por conexão | 20 catracas reconectando juntas ("Aplicar agora") esperam em fila | reconectar em ondas; medir p95 por envio |
| `PingOnLine` | se a volta do laço passar do tempo da mudança, a catraca **cai para off-line em silêncio** | medir a pior volta (NOVO-LOAD-LOOP-01); intervalo menor que tempo − pior volta |
| `EnviarListaAcesso` | chamada longa: pode passar de 30 s e o watchdog mata o worker no meio | modo manutenção; tolerância por classe de operação; hash |
| `ColetarBilhete` em laço | 30.000 bilhetes podem estourar os 10 min do estado; o array de `ReceberQuantidadeBilhetes` tem tamanho desconhecido | gravar antes do próximo; retomar; só chamar após T20 |
| Recepção com letras | buffer de 64 bytes pode estourar: corrupção de memória, não exceção | só após T25; buffer maior e terminador validado |
| `ReceberConfiguracoesInner`, `ReceberUsuarioLista` | buffer de tamanho desconhecido | proibido antes de T12/T16 |
| Bip e mensagens | cada chamada a mais no caminho da passagem reduz a vazão | só em negação ou erro |
| Retorno 8 em qualquer função | derruba o processo | nunca converter em "sem eventos" (F6) |

---

## 9. Decisões do dono do produto (bloqueiam etapas)

| # | Decisão | Bloqueia |
|---|---|---|
| D1 | **Fonte da verdade** do cadastro de cartões: nuvem, local ou dono por campo (a ADR-0023 hoje diz "cadastro é da nuvem") | Etapa B.4 em diante |
| D2 | Parque do evento: urna e QR em **todas** as catracas? | urna, perfis |
| D3 | Lista na catraca: branca, negra ou nenhuma (30 mil não cabem em lista branca) | Etapa D |
| D4 | Cartões da bilheteria em off-line: carregar todo o estoque (qualquer cartão do estoque passa) ou negar | Etapa D |
| D5 | **Fail-safe × fail-secure** e evacuação (liberar nos dois sentidos permite carona) | A.8 (dois sentidos), Etapa D |
| D6 | Perfil e dígitos do cartão da bilheteria (10, 12 ou 14), **depois** da bancada | normalização, importação |
| D7 | Titular: coletar ou não, base legal e prazos de retenção (com o jurídico) | coluna titular, expurgo |
| D8 | Ligar a contingência automática (mudança on-line/off-line) no evento? | A.7 ligado por padrão |

---

## 10. Perguntas para a Topdata

Cada uma tem o ensaio que a responde. Detalhe e evidência: anexo 01 §5.

| # | Pergunta | Ensaio |
|---|---|---|
| T1 | NDA do protocolo TCP/IP (tira x86, thread única e porta por worker) | documental |
| T2 | Retorno de "sem eventos" e timeout interno de `ReceberDadosOnLine`; o que volta com o cabo puxado | HIL-EVT-01, CHAOS-NET-01 |
| T3 | Valor de `RET_SEM_BILHETES` | HIL-BIL-01 |
| T4 | As origens 11, 14–17 e 19 existem? | todo o roteiro docs/21 |
| T5 | Tabela de `Linha` do firmware; TopFit 4 = 14 ou 16? | HIL-CAP-01 |
| T6 | Dígitos fixos: 1–16 ou 4–16 (o WebServer diz 4–16) | HIL-CARD-02 |
| T7 | `ApagarListaAcesso` apaga o buffer ou o equipamento? | NOVO-INT-OFF-12 |
| T8 | Tempo de acionamento 0–50 (SDK) × 1–255 (WebServer); o que é 0? | NOVO-HIL-DIR-09 |
| T9 | Inner 1–99 (SDK) × 1–255 (WebServer) | NOVO-HIL-NET-02 |
| T10 | Leitor 1/2 aceita valores além de 0–4 ("invertido")? | NOVO-HIL-DIR-10 |
| T11 | Assinaturas das 36 funções só com nome (backlight, contador de giro, comando de negação…) | documental |
| T12 | Tamanho e layout do buffer de `ReceberConfiguracoesInner` | NOVO-HIL-CFG-10 |
| T13 | Qual enviador aplica cada `Definir*` e **qual o padrão da DLL** de cada campo; como "não ter" cartão master | NOVO-HIL-CFG-10 |
| T14 | O `Complemento` da origem 6 traz o sentido do giro? | NOVO-HIL-DIR-08 |
| T15 | QR + Mifare: qual sai como origem 2, 3 e 21? | docs/21 passo 3 |
| T16 | Exclusão individual e leitura de volta da lista existem? | NOVO-INT-OFF-10 |
| T17 | Assinatura do contador de giros do equipamento | NOVO-INT-REC-06 |
| T18 | `EngolirCartao`/`DevolverCartao` são da urna? Recolher on-line é relé 2 ou `EngolirCartao`? | NOVO-HIL-URNA-02 |
| T19 | Como gravar "Possui urna" pelo SDK; o SDK desfaz o WebServer? Urna liberando **entrada** = relé 2 função 2? | HIL-URNA-01 |
| T20 | Tamanho do array de `ReceberQuantidadeBilhetes` | NOVO-INT-REC-05 |
| T21 | A lista precisa ser gravada com a catraca em off-line? | NOVO-INT-OFF-11 |
| T22 | Quantas marcações uma passagem gera em off-line? | INT-REC-02 |
| T23 | Duração de `EnviarListaAcesso` para 15.000; a catraca atende durante? | NOVO-LOAD-OFF-02 |
| T24 | Mudança automática: significado de 0/1/2, unidade do tempo, período do `PingOnLine` | INT-SM-021 |
| T25 | Leitor de QR: tipo 5 ou 8? Letras exigem `_QRCodeComLetras`? | NOVO-HIL-QR-02 |
| T26 | Tabela de `FormaEntrada` | INT-SM-032 |
| T27 | Unidade do tempo da mensagem temporária; acentos | INT-MSG-02 |
| T28 | Acertar o relógio com a catraca em uso é seguro? | docs/21 §6A |
| T29 | Mifare de 7 bytes num leitor "ABA 10 dígitos" trunca? | docs/20 §7 ensaio 2 |
| T30 | `InserirQuantidadeDigitoVariavel` acumula entre tentativas? `(0)` zera? | NOVO-HIL-CARD-07 |
| T31 | Qual das duas assinaturas de `ReceberDigitalUsuario` é a real | remover a não inventariada |
| T32 | `DesabilitarWebServer(1)` desabilita e persiste? Como reabilitar? | NOVO-SEC-WEB-01 |
| T33 | Quais modelos emitem origem 6 e com qual sensor; a urna cheia bloqueia sozinha? | B-07, B-08 do docs/09 |
| T34 | A memória circular sobrescreve bilhete **não coletado**? `AvisarQuandoMemoriaCheia` muda isso? | NOVO-INT-REC-04 |

---

## 11. Situação dos itens

Tabela a atualizar a cada PR do [docs/35](35-prompt-modulo-catraca.md).

| Etapa | Itens | Situação |
|---|---|---|
| 0 — Endurecer | 0.1–0.9 (F1, F2, F4, F6, F7, F8, contrato, telas) | **concluída no código** (PR #1): 809 testes passando. Falta bancada: HIL-DIR-05/06 (F1), HIL-CARD-02 e T30 (F2, chave desligada), HIL-EVT-01 (F6, chave desligada). P3 foi para a Etapa C |
| A — Parametrização e funções | A.1–A.9 (A.10 e A.11 bloqueadas por T12 e bancada) | não iniciada |
| B — Cartões | B.0–B.9 (B.4+ bloqueada por D1) | não iniciada |
| C — Gêmeo e telas | C.1–C.9 | não iniciada |
| D — Lista na catraca | bloqueada por D3, D4, D5 e bancada | bloqueada |
