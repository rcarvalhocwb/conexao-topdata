> Relatório bruto de um especialista da auditoria linha a linha (docs/40), commit auditado 29df501.
> Os estados aqui são os do especialista, antes da revisão cruzada; o consolidado é o docs/41.

E7 — CAMADA INTELIGENTE. Auditoria somente leitura no commit 29df501. Não rodei build nem testes. Usei Read, grep e `git log`/`git show` (commits 3a6cc94, d6fd80c e c09270c).

## A) Achados

**E7-1 | MÉDIO | `src/Contracts/Protos/edge_control.proto:72-91` × `src/Edge.Supervisor/EdgeControlService.cs` (nenhum override)**
- **Funções:** `ObterSugestoes`, `RegistrarDestinoDaSugestao`, `ObterSaudeDasCatracas`, `ObterRitmo`, `ListarAlertas`, `MarcarAlertaComoCiente` e `ObterRelatorioPosEvento`.
- **O que o código faz:** o proto declara as 7 RPCs com comentários que descrevem um serviço funcionando: "Só leitura", "sete sinais por catraca", "ciclo de vida Aberto → Ciente → Fechado". O `EdgeControlService` só sobrescreve `ExplicarNegativa` (:523). As outras 7 caem na base gerada e devolvem `UNIMPLEMENTED`.
- **Como o painel reage:**
  - `Parametrizacao.cs:1027-1036` engole o erro e mostra lista vazia de sugestões, sem aviso. Isso é aceitável.
  - `GerenciamentoDeCatracasViewModel.cs:93` mostraria "Erro: Status(...Unimplemented...)", mas essa ViewModel não é referenciada em nenhum lugar de `src` (código morto).
  - `ListarAlertas`, `MarcarAlertaComoCiente`, `ObterRitmo` e `ObterRelatorioPosEvento` não têm cliente nenhum.
- **Promessa descumprida:** docs/36-anexos/02 §3.5:290 diz que, desligada, a RPC devolve "desligada nesta instalação". Hoje ela devolve erro de transporte.
- **Por que é problema no evento:** não existe alerta nenhum da camada, nem saúde, ritmo ou pós-evento. O contrato e o commit c09270c ("Integração completa com AnalisadorDaOperacao", "Determinismo garantido (Wilson, EWMA, Theil-Sen, CUSUM)") induzem a acreditar que a capacidade existe. O docs/29:75-84 está correto: só I.0 e I.2.
- **Como provar:** grep dos 7 nomes em `src/Edge.Supervisor` dá zero overrides. Teste que deveria existir: cliente gRPC chama cada RPC e espera resposta, não `RpcException`.
- **Estado:** CONFIRMADO por leitura. Revisão cruzada E2 pendente.
- **Correção mínima:** override de cada RPC devolvendo uma resposta vazia marcada "desligada nesta instalação", e comentários do proto rotulando o que não está implementado.

**E7-2 | MÉDIO | `src/Edge.Supervisor/Program.cs:108-110` × commits d6fd80c..3a6cc94**
- **O que o código faz:** o commit 3a6cc94 apagou a classe que gravava `inteligencia.ligada=1` e `inteligencia.coletor=1` em toda partida. Nada mais grava essas chaves:
  - `ConfiguracoesDaBorda.cs:320-338` só grava a sua lista fechada de chaves;
  - nenhuma migração nem o `installer/` cita `inteligencia`.
- **O furo:** nenhuma migração apaga o `1` que já foi gravado. Uma base que rodou qualquer build entre d6fd80c e 3a6cc94 continua com a camada ligada depois da atualização. O teste `tests/Integration/InteligenciaDesligadaTests.cs` só prova base nova.
- **Por que é problema no evento:** na bancada ou no PC já usado, o Analisador sobe e roda um ciclo por segundo, contrariando a regra "desligada até I.11". O impacto sobre a liberação continua nulo (ver C). O Diagnóstico mostra a verdade: "ligado".
- **Como provar:** base com `edge_setting('inteligencia.ligada','1')` mais migração atual: `ValorDaChave` continua devolvendo `'1'`.
- **Estado:** a lógica está CONFIRMADA. Não dá para verificar aqui se alguma instalação rodou aqueles builds.
- **Correção mínima:** migração idempotente `DELETE FROM edge_setting WHERE key IN ('inteligencia.ligada','inteligencia.coletor')`. A chave `inteligencia.coletor` também não tem consumidor nenhum.

**E7-3 | MÉDIO (latente; hoje não ligado) | `src/Access.Infrastructure.SQLite/ColetorDeTelemetriaReal.cs:211-452` e `src/Edge.Worker/Operacao/SessaoDeOperacao.cs:496-521`**
- **O que o código faz hoje:** o coletor real não é instanciado em `src`. `Edge.Worker.X86/Program.cs:354-376` não passa `coletor`, então `SessaoDeOperacao.cs:235` usa o `Nulo`. O serviço também não passa `--telemetria` (`Edge.Supervisor/Program.cs:146-151`).
- **Defeitos que aparecem se alguém ligar:**
  - A descarga é síncrona, na thread do laço (`UmaVolta`, :298), com espera por trava de 250 ms + `DefaultTimeout=1` s (`Telemetria.cs:27,47`). Com `telemetria.db` presa, até cerca de 1 s de bloqueio a cada 2 s em todas as catracas do grupo. A estimativa é HIPÓTESE; não medi.
  - `kind` é gravado como `ToString().ToLowerInvariant()` (:329), o que gera `leituravazia` e `liberacaorecusada`. O CHECK da `T002:13` só aceita `leitura_vazia` e `liberacao_recusada`, então a transação inteira falha e é engolida (:446-451). Os contadores já foram zerados (:234-298), e a telemetria se perde.
  - O worker nunca chama o `MigradorDaTelemetria`, então daria "no such table".
  - `worker: "worker-1"` fixo e `sessionId: null` (`SessaoDeOperacao.cs:509`).
  - `dropped` é o descarte global repetido em toda catraca, e `INSERT OR REPLACE` sobrescreve em vez de somar (:440).
  - `device_signal` não tem retenção e fica no mesmo disco de `acesso.db`. Disco cheio faria a gravação da tentativa falhar, e uma falha de base local nega o acesso.
- **Como provar:** teste de unidade com `ColetorDeTelemetriaReal` enfileirando `TipoDeSinal.LeituraVazia` contra a T002 real.
- **Estado:** a lógica está CONFIRMADA. O efeito no laço é HIPÓTESE.
- **Correção mínima:** não ligar no primeiro evento. Antes de ligar: descarga fora da thread do laço, enum mapeado para o CHECK, migração no worker, retenção, e `NOVO-COL-IA-01` / `NOVO-LOAD-IA-01` reais.

**E7-4 | MÉDIO | `src/Edge.Supervisor/EdgeControlService.cs:534-540` e `src/Access.Inteligencia/PorQueNegou.cs:285-294`**
- **Função:** `PorQueNegou.Desconhecido` (contexto montado em `EdgeControlService.ExplicarNegativa`).
- **O que o código faz:** `IdadeDaBase` é calculada como `agora − UltimoSucesso` no instante do clique, não no instante da negação. O texto diz, no presente, "Este código não está na base deste PC. A base recebeu a última atualização da nuvem há X".
- **Por que é problema no evento:** negado às 19:00 como desconhecido, o lote chega às 19:05 e o operador clica às 19:10. O texto afirma algo que pode já ser falso (o código pode estar na base agora) e sugere ingresso falso.
- **Contorno:** tela Consulta.
- **Estado:** CONFIRMADO por leitura.
- **Correção mínima:** texto no passado ("não estava na base quando foi lido, às HH:mm") e idade da base calculada em relação a `tentativa.Em`, ou omitida.

**E7-5 | MÉDIO | código da I.3–I.10 é morto, defeituoso e com lacuna no teste de LGPD**
- **Sem consumidor em `src`** (só testes de unidade): `RepositorioDosAlertas`, `CadernoDosPosEventos`, `ConsultorDeReuso`, `CadernoDesugestoes` (stub com TODO, `CadernoDesugestoes.cs:8-43`) e `GerenciadorDaSaude`. As tabelas `agg_minute`, `fluidity_window` e `reuse_detection` não têm quem as escreva.
- **Defeitos que impediriam funcionar:**
  - `RepositorioDosAlertas.cs:50` grava `leitorcalado`, mas o CHECK é `leitor_calado` (`T005:15`).
  - `:51` grava `alerta.Id` como `session_id`.
  - `:46` usa `WHERE NEW.fechado_em` dentro de um upsert, que é inválido no SQLite (lança exceção).
  - `T005:35`: o `UNIQUE(regra, inner_number, portao)` com `portao` NULL não deduplica.
  - `T005:15` aceita só 5 regras; o enum tem 15.
- **Risco de LGPD:** `ConsultorDeReuso.cs:56` seleciona `qr_normalized` de `acesso.db`, contra a regra de 02 §6:774. O arquivo fica fora da lista do teste de contrato (`tests/Contract/CamadaInteligenteTests.cs:186-193`).
- **Outro defeito:** `ConsultorDeReuso.cs:59` faz `CAST(device_id AS INTEGER)` sobre `inner-N` (`ContextoDasNegativas.cs:12`), que dá 0. A regra de reuso nunca dispararia.
- **Por que é problema no evento:** nenhum hoje. O risco é ligarem achando que está pronto (ver E7-1).
- **Estado:** CONFIRMADO por leitura.
- **Correção mínima:** marcar como não integrado, ou remover. Incluir no teste de contrato todo arquivo que lê `acesso.db`.

**Achados BAIXO e INFORMATIVO (uma linha cada):**
- **E7-6 | BAIXO** | `AvaliacaoDaFluidez.cs:96-97`: a recomendação "orientar fila" tem números fixos e inventados ("há 6 min", "está a 45%").
- **E7-7 | BAIXO** | `RegrasDeAlerta.cs:180-301`: os métodos A2–A5 não correspondem ao enum (A2 é `PicoDeNegacao` em :24, mas o método `A2Comunicacao` trata comunicação). A1 não calcula o λ≥7 de Poisson e o texto fixa "há 3 min".
- **E7-8 | BAIXO** | `AvaliacaoDaHealth.cs:80-87`: sem vizinhas, `AvaliarGiro` dá Atenção, o que viola I8 e gera falso alarme. :89-99 diz usar Wilson, mas usa margem de ±10 pontos.
- **E7-9 | BAIXO** | `AvaliacaoDeSugestoes.cs:122-140`: P1 pode sugerir 8 s ou mais, ignorando a regra 11 (< 8 s) que o próprio comentário cita (:80). P3 (:268-275) sugere no display "Ingresso esgotado" e "Ingresso ja foi usado", o risco de oráculo para fraude do 02 IN-06. `IngressoJaUsado` não é um `MotivoDoUso`.
- **E7-10 | BAIXO** | `AvaliacaoDeReuso.cs`: hora da prova em UTC (:205); `impressao[..8]` lança exceção se a impressão tiver menos de 8 caracteres (:222); `agora ??= UtcNow` não é determinístico (:85).
- **E7-11 | BAIXO** | `GerenciadorDaSaude.cs:37,43`: usa `DateTimeOffset.UtcNow`, não o `TimeProvider` injetado, o que quebra I5.
- **E7-12 | BAIXO** | `PorQueNegou.cs:138-161`: textos de urna ("recolheu", "cartão preso", "giro reverso") existem para códigos que nada em `src` emite (grep). Nunca aparecem, então não há texto falso hoje. `ForaDaUrna` (`RepositorioDeIngressos.cs:1209-1211`) depende do provedor `urn_only`, não de a catraca ter urna. Com configuração errada, o texto manda "mostrar a fenda da urna" numa catraca sem urna. HIPÓTESE; revisão cruzada com E4.
- **E7-13 | BAIXO** | `Desktop.ViewModels/Textos.cs:145`: "a explicação vem do que a catraca já gravou". Quem grava é o PC.
- **E7-14 | BAIXO** | `CamadaInteligenteTests.cs:244`: `NOVO-CTR-IA-01` só varre 3 mensagens; `Alerta`, `SinalDeSaude`, `RelatorioPosEvento` etc. ficam fora. `T007:17` cria a coluna `credential_hmac` (pseudônimo) em `telemetria.db`, arquivo que o 02 §6:780 descreve como "sem dado pessoal por construção". A tabela não é usada.
- **E7-15 | INFORMATIVO** | `SessaoDeOperacao.cs:539-540,561-566`: os ganchos do coletor chamam o `Nulo` depois de a decisão estar calculada. Em `Receber`, aloca `SinalDaOperacao` + `Guid` por evento mesmo com o `Nulo`. Custo de microssegundos.
- **E7-16 | INFORMATIVO** | `ContextoDasNegativas` usa a fábrica de leitura e escrita (`ConsultasDaOperacao.cs:88`), não `LeituraSomenteDaOperacao`. Só faz `SELECT`, com varredura reversa por `rowid`, sob clique. Aceitável.
- **E7-17 | INFORMATIVO** | Com `telemetria.db` presa, corrompida ou com o disco cheio, o ciclo do Analisador falha a cada 1 s, conta e mostra o tipo do erro (`AnalisadorDaOperacao.cs:203-209`). Não se recupera sozinho. O crescimento é limitado: `analyzer_cycle` guarda no máximo 3600 linhas (`Telemetria.cs:143,181`). Coberto por `NOVO_CHAOS_IA_01_telemetria_presa_nao_muda_nem_atrasa_a_decisao` (`tests/Integration/AnalisadorDaOperacaoTests.cs:287`).

## B) Cobertura

| Arquivo | Linhas | Lido inteiro | Funções analisadas | Achados |
|---|---|---|---|---|
| `Access.Inteligencia/RegrasDeAlerta.cs` | 387 | sim | 13 | E7-5, E7-7 |
| `Access.Inteligencia/ChavesDaInteligencia.cs` | 41 | sim | 1 | — |
| `Access.Inteligencia/OrcamentoDoCiclo.cs` | 25 | sim | 1 | — |
| `Access.Inteligencia/RelatorioDosPosEvento.cs` | 337 | sim | 10 | E7-5 |
| `Access.Inteligencia/AvaliacaoDoLaco.cs` | 78 | sim | 5 | E7-5 |
| `Access.Inteligencia/AvaliacaoDaHealth.cs` | 246 | sim | 4 | E7-8 |
| `Access.Inteligencia/PorQueNegou.cs` | 342 | sim | 9 | E7-4, E7-12 |
| `Access.Inteligencia/AvaliacaoDeReuso.cs` | 225 | sim | 5 | E7-10 |
| `Access.Inteligencia/SituacaoDoAnalisador.cs` | 71 | sim | 3 | — |
| `Access.Inteligencia/AvaliacaoDaFluidez.cs` | 215 | sim | 9 | E7-6 |
| `Access.Inteligencia/AvaliacaoDeSugestoes.cs` | 323 | sim | 5 | E7-9 |
| `Access.Inteligencia/GerenciadorDaSaude.cs` | 101 | sim | 5 | E7-5, E7-11 |
| `Access.Inteligencia/AvaliacaoDeLeitura.cs` | 46 | sim | 2 | — |
| `Access.Inteligencia/AvaliacaoDeComuncacao.cs` | 42 | sim | 2 | — |
| `Access.Inteligencia/Access.Inteligencia.csproj` | — | sim | — | — |
| `Edge.Supervisor/AnalisadorDaOperacao.cs` | 299 | sim | 9 | E7-17 |
| `Edge.Supervisor/Program.cs` | 295 | sim | — | E7-2 |
| `Edge.Supervisor/RepositorioDosAlertas.cs` | 156 | sim | 4 | E7-5 |
| `Edge.Supervisor/CadernoDosPosEventos.cs` | 69 | sim | 1 | E7-5 |
| `Edge.Supervisor/ConsultorDeReuso.cs` | 105 | sim | 1 | E7-5 |
| `Edge.Supervisor/EdgeControlService.cs` | 961 | não: trechos 440-600 + grep das RPCs | 3 | E7-1, E7-4 |
| `Access.Infrastructure.SQLite/Telemetria.cs` | 195 | sim | 6 | E7-17 |
| `Access.Infrastructure.SQLite/LeituraSomenteDaOperacao.cs` | 97 | sim | 4 | — |
| `Access.Infrastructure.SQLite/ColetorDeTelemetriaReal.cs` | 456 | sim | 14 | E7-3 |
| `Access.Infrastructure.SQLite/ContextoDasNegativas.cs` | 126 | sim | 2 | E7-16 |
| `Access.Infrastructure.SQLite/CadernoDesugestoes.cs` | 44 | sim | 3 | E7-5 |
| `Edge.Worker/ColetorDeTelemetria.cs` | 109 | sim | 14 | E7-3 |
| `Edge.Worker/Operacao/SessaoDeOperacao.cs` | ~580 | não: trechos 285-300, 490-575 | 3 | E7-3, E7-15 |
| `MigracoesDaTelemetria/T001`–`T009` | 358 | sim (todas) | — | E7-3, E7-5, E7-14 |
| `Contracts/Protos/edge_control.proto` | 980 | não: trechos 60-95, 395-425, 730-980 | 8 RPCs | E7-1 |
| `Desktop.ViewModels` (PorQue, Parametrização, GerenciamentoDeCatracas, Textos) | — | não: só os trechos de inteligência | 5 | E7-1, E7-13 |

Lacunas desta cobertura:
- `EdgeControlService.cs` e `SessaoDeOperacao.cs` não foram lidos inteiros. Ficam com E2 e E1.
- `HistogramaDeBaldes.cs` não foi lido.

## C) Veredito

**A camada pode afetar a liberação? Não, no estado atual.** Evidência:
1. O worker não referencia a camada. Só `Edge.Supervisor.csproj:21` referencia `Access.Inteligencia`. O worker x86 referencia `Edge.Worker`, a infraestrutura SQLite, o adaptador e o simulador, e nenhum deles alcança a camada. Há o teste `NOVO_ARQ_IA_01` (`CamadaInteligenteTests.cs:55`).
2. O Analisador roda no processo do serviço, numa thread própria `BelowNormal` (`AnalisadorDaOperacao.cs:236`). Exceções são contidas (:203, :279). A base `acesso.db` é aberta só para leitura, duas vezes travada: `Mode=ReadOnly` mais `query_only` (`LeituraSomenteDaOperacao.cs:39,56`).
3. O coletor no worker é o `Nulo` (`SessaoDeOperacao.cs:235`; `Edge.Worker.X86/Program.cs:354-376` não passa coletor). Os ganchos em `Decidir` rodam depois de a decisão estar calculada (:533-541).
4. Risco residual, só se ligarem: E7-3 (descarga síncrona no laço, que atrasaria a próxima volta) e disputa de disco e CPU.
5. Custo por ciclo em I.0, quando ligado: abre 2 conexões, faz `COUNT`/`MAX` por `rowid` e um `INSERT` + `DELETE` em `telemetria.db`, a cada 1 s, com orçamento de 50 ms. `NOVO-PERF-IA-01` com carga real não existe.

**Estado para o primeiro evento:** em base nova, desligada. As duas chaves só ligam com o valor `"1"` (`ChavesDaInteligencia.cs:40`), e nenhum caminho de código grava esse valor. A exceção é uma base herdada dos builds d6fd80c..3a6cc94 (E7-2). Recomendo manter desligada.

**Capacidade 15 (alertas, a parte da camada): AUSENTE.** Nenhuma regra roda no Analisador, `ListarAlertas` e `MarcarAlertaComoCiente` devolvem `UNIMPLEMENTED` e não há tela que as chame (E7-1). As regras puras existem só com testes de unidade e têm defeitos (E7-5 a E7-8).

**"Por que negou" (parte da 14): PROVADO EM CI** pelo teste de integração `NOVO-SIM-NEG-01` (`tests/Integration/PorQueNegouTests.cs`), funcionando com a camada desligada.
- O texto é determinístico e não leva código, máscara nem id de ingresso.
- Não cita urna "recolheu" sem urna: esses textos existem, mas nenhum caminho os emite (E7-12).
- Ressalvas: E7-4 (texto no presente sobre a base, que pode já ser falso) e o caso `ForaDaUrna` com provedor mal configurado.

## D) Perguntas de decisão

1. **As 7 RPCs `Unimplemented`:**
   - (a) remover do proto até implementar;
   - **(b) RECOMENDADA:** implementar resposta vazia com "desligada nesta instalação" e rotular os comentários do proto;
   - (c) implementar a I.3–I.10 antes do evento.

   Motivo: (b) cumpre o que o 02 §3.5 promete, tira o erro de transporte do painel e não mexe no worker. (c) é trabalho grande sem bancada.

2. **Chaves `inteligencia.*` gravadas por builds antigos:**
   - **(a) RECOMENDADA:** migração idempotente que apaga as duas chaves;
   - (b) passo manual no runbook;
   - (c) não fazer nada.

   Motivo: garante "desligada" em toda instalação, a um custo de uma linha de SQL.

3. **Coletor real (`ColetorDeTelemetriaReal`) e o código morto da I.3–I.10:**
   - **(a) RECOMENDADA:** manter fora do evento e marcar como "não integrado", com o teste de LGPD estendido a `ConsultorDeReuso`;
   - (b) ligar o coletor já;
   - (c) apagar tudo.

   Motivo: (b) põe I/O síncrono no laço e perde a telemetria por causa do CHECK. (c) desperta retrabalho sem ganho para o evento.
