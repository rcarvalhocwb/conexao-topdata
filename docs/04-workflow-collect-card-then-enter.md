# 04 — Workflow `CollectCardThenEnter`

> **Recolher o cartão para liberar a ENTRADA.**
> Uso invertido em relação ao típico da urna Topdata (que é saída).

**Este workflow nasce desabilitado.** Só é ativado após o assistente de comissionamento
ser concluído por um técnico, em modo de manutenção, com confirmação física dos dois
sentidos.

> ## ⚠️ Atualização de 24/09/2026 — a documentação oficial muda duas coisas aqui
>
> **1. São dois equipamentos diferentes, com finais de fluxo diferentes.**
>
> | | **Catraca com Urna Coletora** (manual, seção 5.3) | **Coletor Urna 4** (pedestal autônomo) |
> |---|---|---|
> | Mecanismo | Catraca com braço e sensor óptico | Aciona **cancela ou porta** por contato seco |
> | Confirma passagem? | **Sim** — origem 6 | **Não existe origem 6**: não há giro |
> | Consumo do ingresso | Na origem 6 | **Decisão de negócio** — ver abaixo |
>
> Para o Coletor Urna 4, o passo 9 do fluxo **não existe**. O evento mais forte
> disponível é a **origem 7 (cartão recolhido)**, e é nela que o ingresso precisa ser
> consumido. Isso **precisa ser aprovado explicitamente pelo cliente**: significa aceitar
> que "recolheu" conta como "entrou", com o risco de fraude que isso carrega.
> Não é uma decisão que o software pode tomar sozinho ([ADR-0007](ADR/ADR-0007-autorizacao-versus-passagem.md)).
>
> **2. Boa notícia: o firmware já garante a ordem.** A especificação oficial do Coletor
> Urna 4 lista, como característica nativa, **"liberação de acesso somente após
> recolhimento dos cartões"**, além de **detecção de desistência** e de **urna cheia**.
> A parte mais delicada do workflow é responsabilidade do equipamento; o software valida
> *quem* pode passar e registra o que aconteceu. Ainda assim, **confirmar em bancada** —
> o produto não assume isso para toda versão de firmware.
>
> **3. As funções existem** (`FONTE_PRIMARIA`): `AcionarRele2(Inner, Tempo)` abre a fenda;
> `LiberarCatracaEntrada` e `LiberarCatracaEntradaInvertida` liberam o sentido de entrada.
> **B6 está respondida: o fluxo é implementável.**

## 1. A regra que governa tudo

Três fatos distintos, que sistemas ingênuos tratam como um só:

| Fato | Comprovado por | Significa |
|---|---|---|
| **Autorizado** | Decisão local favorável | O direito existe |
| **Recolhido** | Origem 7 | O cartão está na urna, fora das mãos da pessoa |
| **Passou** | Origem 6 | Alguém atravessou fisicamente |

O ingresso só é **consumido definitivamente** no terceiro. Entre o primeiro e o terceiro,
ele fica **reservado** — nem livre (evita uso duplo em outro gate), nem consumido (evita
cobrar de quem desistiu).

## 2. Máquina de estados

```
                          ┌──────────────┐
                          │     Idle     │◄──────────────────────────┐
                          └──────┬───────┘                           │
                     origem 2/3/21│ leitura                          │
                          ┌───────▼────────┐                         │
                          │ CardPresented  │                         │
                          └───────┬────────┘                         │
                                  │                                  │
                          ┌───────▼────────┐   negado                │
                          │   Validating   ├────────────► Denied ─────┤
                          │   (T1 150 ms)  │   (NÃO recolhe,         │
                          └───────┬────────┘    NÃO libera)          │
                        aprovado  │                                  │
                          ┌───────▼────────┐                         │
                          │ TicketReserved │  reserva com TTL         │
                          └───────┬────────┘                         │
                                  │                                  │
                          ┌───────▼────────┐                         │
                          │ CollectingCard │  aciona relé (T2)        │
                          └───────┬────────┘                         │
                    origem 7      │      T2 estoura                  │
              ┌───────────────────┴───────────────┐                  │
              ▼                                   ▼                  │
     ┌─────────────────┐                ┌──────────────────┐         │
     │ CardCollected   │                │ CollectTimeout   │         │
     └────────┬────────┘                │ → CardStuck?     │         │
              │                         └────────┬─────────┘         │
     ┌────────▼────────┐                         │ libera reserva    │
     │  EntryReleased  │  libera sentido ENTRADA  │ Quarantined       │
     │  (T3)           │  (perfil comissionado)   └───────────────────┤
     └────────┬────────┘                                             │
              │ origem 6                    T3 estoura               │
     ┌────────▼────────┐              ┌──────────────────────────┐   │
     │ EntryCompleted  │              │ AuthorizedWithoutPassage │   │
     │ consome ingresso│              │ → reconciliação manual   │   │
     └────────┬────────┘              └────────────┬─────────────┘   │
              └────────────────────────────────────┴─────────────────┘

  Origem 20 (urna cheia) em QUALQUER estado → WorkflowBlocked (gate para de aceitar)
```

## 3. Passo a passo, com o que pode dar errado

| # | Passo | Evento/Ação | Se falhar |
|---|---|---|---|
| 1 | Pessoa apresenta o cartão | origem 2/3/21 | — |
| 2 | Edge recebe o identificador **como string** | — | Normalização errada → negado indevido (`HIL-CARD-*`) |
| 3 | Motor valida: ingresso, status, janela, setor, anti-passback, usos, bloqueios, assinatura | T1 = 150 ms | Timeout → `OfflineFallback` conforme política |
| 4 | **Negado** → não recolhe, não libera, exibe motivo | mensagem + `reasonCode` | — |
| 5 | **Aprovado** → reserva o ingresso e aciona o relé de recolhimento (`AcionarRele2`) | T2 = tempo configurado (0–50 s) | Relé não aciona → libera reserva, alerta |
| 6 | **Aguarda origem 7.** Antes disso, **não libera entrada** | — | Sem origem 7 em T2 → `CollectTimeout` |
| 7 | Origem 20 (urna cheia) → bloqueia o fluxo, orienta outro portão, alerta | — | Gate sai de operação até esvaziamento |
| 8 | Recolhimento confirmado → libera o sentido **ENTRADA** | função do perfil físico comissionado | Retorno de erro → registra, libera reserva |
| 9 | Aguarda origem 6 — **só em catraca com mecanismo de giro** | T3 = 8 s default | Sem origem 6 → `AuthorizedWithoutPassage`. **No Coletor Urna 4 este passo não se aplica** |
| 10 | Consome o ingresso, registra entrada física | — | — |
| 11 | Exceções abaixo | — | — |

**A nomenclatura da função de liberação não é fixada em código.** O perfil físico do gate
(resultado do comissionamento) declara qual acionamento corresponde a `EntradaLógica`; o
adapter resolve isso para `LiberarCatracaEntrada`, saída invertida ou sentido configurado,
conforme o modelo. Ver [ADR-0010](ADR/ADR-0010-capability-discovery.md).

## 4. Temporizadores

| ID | O que mede | Default | Ao estourar |
|---|---|---|---|
| T1 | Decisão local | 150 ms | `OfflineFallback` |
| T2 | Leitura aprovada → origem 7 | 6 s (configurável) | `CollectTimeout`, libera reserva |
| T3 | Liberação → origem 6 | 8 s (configurável) | `AuthorizedWithoutPassage` |
| T4 | TTL da reserva do ingresso | T2+T3+margem (20 s) | Reserva expira, ingresso volta a ficar livre |
| T5 | Janela de anti-replay no mesmo gate | 3 s | Segunda leitura idêntica é ignorada |
| T6 | Silêncio do equipamento até `Degraded` | 22 s | Alerta + T2/T3 do nível de degradação |

**T4 > T2 + T3 sempre.** Se a reserva expirar antes do giro, a pessoa passa com um
ingresso já liberado para outro — a origem exata de "entrou duas vezes com o mesmo
ingresso". A invariante é verificada na carga da configuração e o sistema recusa
configuração que a viole.

## 5. Exceções, todas com tratamento definido

| Exceção | Detecção | Comportamento | Registro |
|---|---|---|---|
| **Desistência** | origem 7 ausente em T2, cartão devolvido/retirado | Libera reserva, volta a `Idle` | `ABANDONED_BEFORE_COLLECT` |
| **Cartão preso** | `CollectTimeout` repetido no mesmo gate (≥2 seguidos) | `Quarantined`, alerta de manutenção | `CARD_JAM_SUSPECTED` |
| **Tempo esgotado** | T2 ou T3 | Conforme acima | `COLLECT_TIMEOUT` / `TURN_TIMEOUT` |
| **Recolhimento sem leitura válida** | origem 7 sem `CardPresented` correspondente | Não libera entrada; cartão vai para reconciliação física | `ORPHAN_COLLECTION` |
| **Duas leituras** | Segunda leitura dentro de T5 | Ignorada (anti-replay) | `REPLAY_SUPPRESSED` |
| **Dois cartões** | Leitura de credencial diferente com fluxo em aberto | Recusada; o fluxo em aberto tem precedência | `CONCURRENT_CREDENTIAL` |
| **Giro contrário** | origem 6 com sentido inverso ao liberado | Não consome ingresso; alerta de segurança | `REVERSE_TURN` |
| **Tentativa simultânea** | Mesma credencial em dois gates | Reserva é atômica: o primeiro ganha, o segundo recebe negação explicada | `TICKET_ALREADY_RESERVED` |
| **Abertura manual** | Comando de operador/botão | Passagem registrada como manual, com operador identificado | `MANUAL_OVERRIDE` |
| **Urna cheia** | origem 20 | `WorkflowBlocked` no gate; operação orientada a outro portão | `BIN_FULL` |
| **Energia cai no meio** | Reinício detectado (novo `bootId`) | Reconciliação: reserva expira por TTL; origem 7/6 pendentes viram exceção | `POWER_LOSS_MIDFLOW` |

**Toda exceção é visível na Central de Incidentes com ação recomendada em português
simples** — não como código. `CARD_JAM_SUSPECTED` chega ao operador como
*"Catraca 08: o cartão pode ter ficado preso. Verifique a entrada da urna e libere o
equipamento."*

## 6. Reconciliação de urna

O que é recolhido fisicamente e o que o sistema registrou precisam bater ao fim do evento:

```
cartões na urna  ==  EntryCompleted + AuthorizedWithoutPassage + ORPHAN_COLLECTION
```

Divergência é relatório, não erro silencioso. O esvaziamento da urna é um evento com
**cadeia de custódia**: quem retirou, quando, qual gate, contagem declarada, contagem
esperada, lacre.

> Lembrete de dimensionamento: 50.000 cartões ÷ ~750 por urna ≈ **67 esvaziamentos**
> durante o evento. Isso é escala de pessoal, e o software alerta em ~70% e ~85% da
> capacidade — mas não esvazia nada.

## 7. Assistente de comissionamento

Obrigatório antes de o workflow operar em produção. Em modo de manutenção:

1. Identifica modelo e firmware; confere na matriz.
2. Pede o teste do **sentido A** e pergunta ao instalador: *"Por onde a pessoa passou?"*
3. Repete para o **sentido B**.
4. Associa `EntradaLógica` ao acionamento físico confirmado.
5. Testa o relé de recolhimento com um cartão de teste e confirma a origem 7.
6. Verifica se a origem 6 aparece. **Num Coletor Urna 4 ela não vai aparecer** — o
   assistente avisa em destaque que aquele gate não confirma passagem física e exige
   decisão consciente sobre onde consumir o ingresso.
7. Exige política de fail-safe/fail-secure ([ADR-0013](ADR/ADR-0013-fail-safe-versus-fail-secure.md)).
8. Gera o **perfil físico do gate**, assinado, com quem comissionou e quando.

O software **não** altera fiação, firmware ou sentido mecânico. Ele descobre, registra e
recusa operar sobre suposição.
