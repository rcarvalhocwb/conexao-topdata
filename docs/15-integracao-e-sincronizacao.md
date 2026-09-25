# 15 — Integração e sincronização

> **A pergunta que originou este documento:** o sistema guarda cartões, biometrias e QR
> codes off-line e, quando a internet volta, sincroniza sozinho com o sistema integrado?
> E serve para evento, academia, condomínio e portaria ao mesmo tempo?
>
> A resposta honesta tem duas partes: **a arquitetura foi desenhada exatamente para isso
> desde o primeiro dia**, e **uma parte dela ainda não existe em código**. Este documento
> separa uma coisa da outra e projeta o resto inteiro.

---

## 1. O que existe hoje, camada por camada

Nada aqui é promessa. Cada linha "construído" tem teste que reprova se quebrar.

| Capacidade | Situação | Onde |
|---|---|---|
| Gravar evento + decisão + comando + fila de saída **numa transação só** | **construído** | `AccessJournal`, `tests/Integration/PersistenciaTests` |
| Não perder nada num `kill -9` | **construído** | `tests/Integration/QuedaAbruptaTests` |
| Fila de saída (outbox) com prioridade e chave de idempotência | **construído** | migração `001` |
| **Drenar a fila quando a conexão voltar** | **construído nesta entrega** | `Sync.Core`, migração `002` |
| Cartas mortas e cursores de sincronização | **construído nesta entrega** | migração `002` |
| Contrato de conector para sistema externo | **construído nesta entrega** | `IConectorDeSincronizacao` |
| Um conector concreto (REST, Postgres, SQL Server…) | **não existe** | Fase 4 |
| Caminho de volta: revogação vinda do sistema do cliente | **não existe** — tabela `inbox` criada e nunca escrita | Fase 4, projetado na §6 |
| Cadastro local de pessoas, credenciais e regras | **não existe** — modelado em [`05`](05-modelo-de-dados.md), fora da migração `001` | Fase 2 |
| Motor de decisão (lista, janela, lotação, anti-passback) | **não existe** — `Decision`, `ReasonCode` e degradação existem; quem os produz, não | Fase 2 |
| Enviar lista de acesso e digital para o equipamento | **declarado, não chamado** — 77 dos 218 nomes declarados tocam biometria | `EasyInnerGerada.cs` |
| Leitor de QR code | **suportado na configuração** (`TipoLeitor` 8) | `DeviceConfiguration` |

**A frase que resume:** a borda já sabe *guardar sem perder* e agora sabe *entregar sem
duplicar*. O que ela ainda não sabe é *decidir sozinha quem entra* — isso é cadastro e
motor de regra, e é a Fase 2, que depende do ensaio `HIL-STACK-01`.

---

## 2. As três memórias, e quem é dono da verdade

O erro clássico de sistema de catraca é ter um lugar só. Aqui são três, de propósito, com
donos diferentes:

```
  ┌───────────────────────────┐   cadastro (quem pode entrar)
  │  Sistema do cliente       │   bilheteria · ERP de academia · app de condomínio · RH
  │  NUVEM                    │   dono do CADASTRO. Nunca no caminho do giro.
  └────────────┬──────────────┘
               │  descida: credenciais, revogações, regras   (inbox + cursor)
               │  subida:  passagens, decisões, alarmes      (outbox + drenador)
  ┌────────────┴──────────────┐
  │  Edge (este produto)      │   SQLite WAL. Dono do FATO: o que aconteceu, quando,
  │  BORDA                    │   em qual catraca, com qual resultado.
  └────────────┬──────────────┘
               │  configuração, lista de acesso, digitais
               │  eventos e bilhetes (coleta)
  ┌────────────┴──────────────┐
  │  Catraca / coletor        │   Lista local: 15.000 usuários. Marcações: 30.000.
  │  EQUIPAMENTO              │   Dono de NADA — é cache e testemunha.
  └───────────────────────────┘
```

**A regra de conflito cabe numa linha:** *cadastro é da nuvem, fato é da borda.* Um cartão
revogado no ERP vence a lista local do equipamento. Uma passagem registrada na borda vence
qualquer coisa que a nuvem ache que aconteceu — a nuvem não estava lá.

Isso não é filosofia: é o que decide o que sobrescreve o quê quando dois lados
divergirem depois de uma partição de rede.

### Os quatro níveis de degradação, e quem decide em cada um

| Nível | Situação | Quem decide | Onde a credencial está |
|---|---|---|---|
| `T0Normal` | Tudo no ar | Borda | SQLite da borda |
| `T1SemInternet` | Sem nuvem. **Regime normal de um evento** | Borda | SQLite da borda |
| `T2ListaLocal` | Borda caiu; equipamento vivo | Equipamento | Lista de acesso gravada na catraca |
| `T3Isolado` | Equipamento sozinho | Ninguém — vale a política do portão | — |

A internet cair é `T1`, e `T1` **não degrada nada** para o usuário final: a borda decide
igual, na mesma latência. O que para é a sincronização, e ela para sem perder uma linha.
É por isso que a fila é durável antes de ser rápida.

---

## 3. Cartão, QR code e biometria: onde cada um fica

Os três são credenciais. Mudam o leitor e o tamanho, não o caminho.

| Credencial | Como chega | Armazenamento off-line | Cuidado próprio |
|---|---|---|---|
| **Cartão** RFID/Mifare/proximidade/código de barras | Leitor 1 ou 2 | Lista de acesso do equipamento + cadastro da borda | **Sempre string.** Converter para número come o zero à esquerda e transforma `0001234567` em outro cartão. [ADR-0008] |
| **QR code** (ingresso, convite, app) | `TipoLeitor` 8 — QR por letras | Idêntico ao cartão | Comprimento e alfabeto maiores: o perfil de normalização é outro, o caminho é o mesmo |
| **Digital** | Sensor do próprio equipamento | *Template* na catraca (casamento no hardware) **e/ou** base segregada da borda | Base própria, cifrada, chave distinta, retenção com prazo, exportação sob aprovação de duas pessoas |
| **Face** | SDK distinto, WebSocket/JSON | Fora da EasyInner, nunca misturado | Idem digital, mais o fato de ser outro fornecedor ([`13`](13-sdk-facial.md)) |
| **Senha/PIN** | Teclado | Hash, nunca o valor | Nunca em log, nunca em relatório |

**Sobre biometria, uma distinção que muda o projeto inteiro:** quando o *template* está no
equipamento, o casamento acontece no hardware e a borda nem precisa estar viva — é
`T2ListaLocal` com biometria. Quando o template está na borda, a decisão é da borda e o
equipamento vira só sensor. Os dois modos estão declarados no SDK; qual usar é decisão por
modelo de equipamento, e depende de `HIL-STACK-01` e do inventário do parque (B2).

**LGPD não é apêndice:** dado biométrico é dado pessoal sensível. Exige base legal,
minimização, prazo de retenção e expurgo automático. O modelo já reserva
`consent_basis`, `consent_at`, `retention_until` e `purged_at` — e a base biométrica é um
**arquivo separado**, para que um backup do banco operacional não a arraste junto.

---

## 4. O fluxo inteiro, do cadastro ao relatório

```
(1) CADASTRO           bilheteria/ERP/app  ──► inbox ──► aplicador ──┬─► cadastro da borda
    descida                                  (cursor)                └─► lista de acesso
                                                                          do equipamento
                                                                          (+ digitais)

(2) OPERAÇÃO           pessoa encosta o cartão / mostra o QR / põe o dedo
                              │
                              ▼
                       leitura chega ao worker  (origem 2)
                              │
                              ▼
                       MOTOR DE DECISÃO — local, sem rede, orçamento de 150 ms
                       lista · janela de horário · setor · usos · lotação · anti-passback
                              │
                    ┌─────────┴─────────┐
                 permite                nega
                    │                     │
                    ▼                     ▼
              libera o giro         mensagem no display
                    │                     │
                    └─────────┬───────────┘
                              ▼
                   UMA TRANSAÇÃO NO SQLITE
                   raw_event + access_decision + device_command + outbox
                              │
                              ▼
                   giro confirmado? (origem 6 — o sensor óptico)
                   sim → physical_passage        não → autorizado sem passagem
                              │
(3) SUBIDA                    ▼
                   outbox ──► DRENADOR ──► conector ──► sistema do cliente
                   por prioridade, com espera crescente, sem duplicar
```

**Os três momentos têm exigências opostas, e é por isso que são três componentes:**
o (2) precisa ser rápido e não pode depender de rede; o (3) pode demorar horas e não pode
perder nada; o (1) precisa ser idempotente porque vai ser reprocessado.

---

## 5. A subida: o drenador (construído nesta entrega)

### Prioridade, porque ordem de chegada é a ordem errada

Depois de oito horas sem internet, a fila tem centenas de milhares de linhas. Drenar por
ordem de chegada faria a revogação de um cartão roubado esperar o histórico da manhã
inteira terminar de subir. As dez classes:

| # | Classe | Exemplo |
|---|---|---|
| 0 | `BloqueioEmergencial` | pânico, evacuação, liberação geral |
| 1 | `RevogacaoDeCredencial` | cartão roubado, demissão, morador que saiu |
| 2 | `Alarme` | urna cheia, equipamento fora, porta arrombada |
| 3 | `PassagemFisica` | giro confirmado — alimenta lotação e painel ao vivo |
| 4 | `DecisaoDeAcesso` | autorização e negativa |
| 5 | `MovimentoDeCredencial` | entrega, recolhimento, reemissão de cartão |
| 6 | `InventarioEConfiguracao` | firmware, mudança de configuração |
| 7 | `Auditoria` | trilha — importa muito, urge pouco |
| 8 | `Metrica` | latência, contadores |
| 9 | `Historico` | só para consulta posterior |

O drenador ordena **por conector**, e escolhe primeiro o conector que guarda o item mais
urgente.

### As quatro decisões que o drenador toma, e por quê

| Resposta do destino | O que acontece | Por quê |
|---|---|---|
| `Aceito` | sai da fila | — |
| `Duplicado` | sai da fila, conta como sucesso | É o caminho normal depois de uma queda no meio do envio: o destino gravou, a resposta se perdeu na volta |
| `FalhaTemporaria` | volta para a fila com espera crescente | Rede fora, 5xx, limite de requisições. Repetir resolve |
| `FalhaPermanente` | vai para **cartas mortas**, com o conteúdo inteiro | Payload que o destino recusa não melhora com repetição — repetir só empurra a fila para trás |
| *silêncio* (item sem resposta) | tratado como falha temporária | O custo de repetir é um duplicado que a idempotência descarta; o de assumir entrega é uma passagem que some do relatório |

E três invariantes que os testes protegem:

1. **Nada sai da fila sem confirmação ou registro em cartas mortas.** Nada é apagado: item
   recusado é problema de integração que alguém precisa ver, não lixo.
2. **Um conector doente não atrasa os outros.** A bilheteria fora do ar não segura a
   contagem de lotação que vai para o painel.
3. **Conector não registrado preserva os itens intactos.** Um arquivo de configuração
   faltando não pode destruir a fila.

### Dimensionamento, com a conta à vista

Para o evento de 30.000 pessoas de [`14`](14-estudo-de-caso-evento-30k.md), supondo quatro
linhas por pessoa (decisão, passagem, auditoria, métrica) e payload de ~400 B:

- fila acumulada: **≈ 120.000 linhas, ≈ 48 MB**
- em lotes de 500, a 200 ms por lote: **≈ 48 segundos** para drenar o evento inteiro

O gargalo, portanto, não é a fila — é o destino. Por isso o lote é declarado **pelo
conector**, e não pelo drenador: quem conhece o limite do outro lado é quem fala com ele.

> As quatro linhas por pessoa e os 400 B são **estimativa de projeto**, não medição. O
> ensaio `LOAD-SYNC-01` (Fase 4) é que fecha esse número.

---

## 6. A descida: revogação chegando da nuvem *(projetado, não construído)*

É a perna mais delicada, e a razão é assimétrica: um evento que sobe atrasado é um
relatório atrasado; **uma revogação que desce atrasada é alguém entrando onde não podia.**

O desenho:

```
sistema do cliente ──(webhook assinado ou consulta por cursor)──► inbox
                                                                    │
                                                    idempotency_key deduplicando
                                                                    ▼
                                                              aplicador
                                                     ┌──────────────┴──────────────┐
                                          cadastro da borda            lista do equipamento
                                          (efeito imediato)            (envio serializado)
```

Quatro decisões que já estão tomadas:

1. **`inbox` antes de aplicar.** O webhook responde "recebi" gravando uma linha, e só
   depois aplica. Entrega repetida é descartada pela chave de idempotência — e webhook
   repete, sempre.
2. **Cursor por conector *e* por fluxo.** A queda do fluxo de credenciais não pode fazer o
   fluxo de regras reprocessar tudo desde o começo.
3. **Revogação tem caminho expresso.** Entra como prioridade 1 e é aplicada na lista do
   equipamento fora do lote de sincronização normal.
4. **Empurrar e puxar.** Webhook para latência, varredura por cursor para consistência — o
   webhook que se perdeu é recuperado pela varredura, sem depender de ninguém perceber.

Um detalhe que só aparece com hardware na mesa: **15.000 é o teto da lista local**. Num
evento de 30.000 pessoas a lista branca não cabe, e a operação inverte para lista negra —
todo mundo passa, exceto os revogados. Isso muda o significado de uma revogação atrasada,
e é exatamente por isso que ela tem prioridade 1. Ver [`14`](14-estudo-de-caso-evento-30k.md).

---

## 7. O contrato do conector: o que um integrador precisa escrever

Um método. Tudo que é difícil — ordem, repetição, idempotência, cartas mortas, não perder
nada numa queda — está no drenador.

```csharp
public interface IConectorDeSincronizacao
{
    string Nome { get; }                    // chave de roteamento, gravada na outbox
    int TamanhoMaximoDoLote { get; }        // quem conhece o limite do outro lado é quem fala com ele

    Task<IReadOnlyList<RespostaDeItem>> EnviarAsync(
        IReadOnlyList<ItemDeSaida> lote,
        CancellationToken cancelamento);    // resposta ITEM A ITEM
}
```

A resposta é por item, e não do lote, porque aceitação parcial é o caso comum: de 500
passagens, 499 entram e uma referencia um setor apagado no sistema de origem. Tratar isso
como falha do lote trava a fila para sempre.

Duas exigências ao integrador, e nenhuma outra:

- **Tolerar reenvio.** O drenador repete sempre que não tiver certeza de que o item chegou
   — e "não ter certeza" inclui o destino ter recebido, gravado, e a resposta ter se
   perdido na volta.
- **Classificar o erro.** Distinguir "tente de novo" de "não adianta" é o que separa uma
   fila que drena de uma fila que entope.

---

## 8. Os verticais: uma máquina, vários perfis

A tentação é construir quatro produtos. Não são quatro: **o mecanismo é o mesmo e a
política muda.** Quem entra, quando pode, o que acontece se a rede cair — isso é
configuração, não código.

| | **Evento** | **Academia** | **Condomínio** | **Portaria / empresa** |
|---|---|---|---|---|
| Dono do cadastro | bilheteria | ERP de mensalidade | app do síndico / administradora | RH |
| Credencial típica | QR do ingresso, cartão de urna | biometria, cartão | tag, biometria, facial, app | crachá, biometria |
| Regra que domina | setor, uso único, lotação | **adimplência** e horário do plano | morador × visitante, unidade | jornada, turno, terceiro com prazo |
| Volume | 30.000 num pico de 2 h | 500/dia, pico previsível | 200/dia, contínuo | 2.000/dia, dois picos |
| Rede | **assume-se ausente** | presente, cai às vezes | presente | presente, com VPN |
| O que não pode falhar | fila não pode parar | não barrar quem pagou | registrar visitante | ponto e jornada |
| Retenção (LGPD) | curta — acaba com o evento | enquanto durar o contrato | prazo da convenção | prazo trabalhista |
| Peculiar | urna, recolhimento de cartão, credenciamento | bloqueio por inadimplência precisa **descer rápido** | interfone, autorização do morador | integração com folha |

O que muda de verdade entre eles, e que o produto precisa ter como **perfil**:

1. **Ciclo de vida da credencial.** Ingresso morre no fim do evento; crachá morre na
   demissão; tag de morador morre na mudança. É `valid_from`/`valid_to` mais uma regra de
   expurgo por perfil.
2. **O que fazer quando a rede cai.** No evento, a borda decide tudo e a lista local é
   negra. Na academia, alguém precisa responder se um inadimplente entra ou não quando o
   ERP está fora — é a pergunta **B4**, e é decisão do cliente, não minha.
3. **Anti-passback.** Fundamental no condomínio, irritante no evento, obrigatório no ponto.
   Liga e desliga por zona.
4. **Retenção.** Um evento não deve guardar o rosto de ninguém depois que acabou.

O resto — decisão local, transação única, outbox, drenagem, degradação, auditoria
encadeada — é idêntico nos quatro. É isso que faz um produto, em vez de quatro projetos.

---

## 9. Falhas, e o que cada uma faz

| Falha | Consequência | Comportamento |
|---|---|---|
| Internet cai | Nenhuma para quem passa | `T1`: borda decide igual, fila acumula |
| Internet volta | Rajada no destino | Espera com *jitter*: bordas de uma rede não martelam o servidor no mesmo instante |
| Destino do cliente fora por horas | Fila cresce | 12 tentativas com espera exponencial ≈ 4 h; depois, cartas mortas com o conteúdo inteiro |
| Contrato do destino mudou | Recusa permanente | Cartas mortas imediatas, sem queimar a fila. Alguém vê e reprocessa |
| Conector com defeito (exceção) | — | Tratado como destino fora do ar, nunca como item ruim: a causa quase sempre é cabo |
| Disco cheio na borda | Grave | A fila é durável: é a primeira coisa a monitorar. *(alerta de espaço: não construído)* |
| Borda cai | Equipamento assume | `T2`: lista local, até 15.000 usuários |
| Processo morto no meio do envio | Nenhuma | Item não confirmado é reenviado; idempotência descarta do outro lado |

---

## 10. O que falta, em ordem de dependência

1. **`HIL-STACK-01`** — a DLL carrega num processo .NET 10 `win-x86`? Nada da Fase 2 começa
   antes. É ensaio de bancada, na sua máquina.
2. **Cadastro e motor de decisão** (Fase 2) — as tabelas de [`05`](05-modelo-de-dados.md)
   que a migração `001` não trouxe, e quem produz a `Decision`. **É o que falta para o
   sistema decidir sozinho quem entra.**
3. **Lista de acesso e digitais no equipamento** (Fase 2) — as 77 declarações de biometria
   passam a ser chamadas.
4. **Primeiro conector concreto** (Fase 4) — REST assinado, já contra o contrato desta entrega.
5. **Descida: `inbox` + cursor + aplicador** (Fase 4) — a §6 vira código.
6. **Painel de sincronização** — backlog por conector, idade do item mais antigo, cartas
   mortas, botão de reprocessar.
7. **Perfis de vertical** — evento, academia, condomínio, portaria como configuração.

---

## 11. Limitações conhecidas desta entrega

1. **Nenhum conector real existe.** O drenador foi exercitado contra conectores de teste,
   em SQLite de verdade, nunca contra um sistema externo de verdade.
2. **A descida não existe.** `inbox` e `sync_cursor` são tabelas criadas e não escritas.
   Revogação vinda da nuvem está projetada (§6) e não construída.
3. **O drenador não está hospedado.** A classe existe e é testada; nenhum processo a
   executa ainda — falta o ponto de composição no `Edge.Supervisor`.
4. **As 12 tentativas ≈ 4 h** são cálculo, não medição: dependem da função de espera que o
   hospedeiro injetar.
5. **48 segundos para drenar um evento de 30.000** é estimativa de projeto. `LOAD-SYNC-01`
   é que fecha.
6. **Nada disso foi exercitado com hardware.** Continua valendo a regra: compilar e passar
   nos testes não é estar pronto para produção.

### Como esta entrega foi verificada

23 testes novos: 15 de política (sem banco, sem rede, sem esperar) e 8 contra SQLite de
verdade, do que o diário gravou até o conector. E a trava foi violada de propósito —
trocar `ORDER BY priority ASC, created_at ASC` por ordem de chegada faz o teste de
prioridade reprovar, como tem de fazer. Só então foi restaurada.
