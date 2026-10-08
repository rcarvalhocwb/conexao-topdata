> Relatório bruto de um especialista da auditoria linha a linha (docs/40), commit auditado 29df501.
> Os estados aqui são os do especialista, antes da revisão cruzada; o consolidado é o docs/41.

## E3: dados e persistência. Relatório de auditoria (commit 29df501)

Não editei nada e não rodei build nem testes. Usei Read, Grep, Bash (cat, grep, sed, find) e `python3 -I` com sqlite3 3.45.1 no scratchpad `.../scratchpad/e3`; o banco temporário foi apagado. Fiz quatro verificações:
- o plano de consulta da confirmação de giro;
- a instrução de upsert dos alertas contra o esquema T005;
- a presença do texto literal `BEGIN IMMEDIATE;` na DLL Microsoft.Data.Sqlite 10.0.12;
- as contas de espera da fila a partir do código.

### A) Achados

**E3-01 | ALTO | `src/Sync.Core/DrenadorDaOutbox.cs:285-297`, `src/Edge.Supervisor/SincronizacaoComANuvem.cs:196-200`, `src/Desktop.ViewModels/Telas.cs:617`**
- **Função:** `DrenadorDaOutbox.DrenarConectorAsync`.
- **O que o código faz:** quando o conector devolve `FalhaTemporaria` (rede fora, tempo esgotado, 5xx; ver `ConectorDeTentativasDoPainel.cs:135-141`), a contagem de tentativas sobe. Na 12ª o item vai para `dead_letter`. A espera de produção é `min(300, 5·2^min(t,6))`: 10+20+40+80+160+6×300 = 2110 s, cerca de 35 minutos. O comentário em `DrenadorDaOutbox.cs:75-77` promete "cerca de quatro horas". Nenhum código grava `reprocessed_at` nem devolve um item à fila: em `src/`, `reprocessed_at` só aparece em `FilaDeSaidaSqlite.cs:248`. A tela chama esses itens de "Recusados pela nuvem".
- **Por que é problema no evento:** uma queda de internet de mais de ~35 minutos (comum em evento) faz as tentativas e liberações mais antigas morrerem na fila. O painel na nuvem e a prestação de contas na nuvem ficam sem esses acessos para sempre. O operador vê "recusados", que é falso, e não tem como reenviar.
- **Como provar:** um teste de integração com um conector que lança `HttpRequestException`, `TimeProvider` falso avançado 40 minutos e chamadas a `DrenarUmaVezAsync` até ver `dead_letter > 0`. Hoje não existe esse teste.
- **Estado:** CONFIRMADO na leitura (falta revisão cruzada por E4).
- **Correção mínima:** falha temporária nunca vai para carta morta (retenta com teto de espera); carta morta só para `FalhaPermanente`; uma ação "reenviar" que copie de volta para `outbox` e grave `reprocessed_at`; corrigir o texto da tela.

**E3-02 | ALTO | `src/Edge.Supervisor/AgendadorDeCopias.cs:54-65`, `src/Access.Infrastructure.SQLite/CopiaDeSeguranca.cs:59-60,123-126`, `src/Edge.Supervisor/Program.cs:271`**
- **Função:** `AgendadorDeCopias.Executar` e `CopiaDeSeguranca.Criar`/`Conferir`.
- **O que o código faz:** o `catch` só pega `InvalidOperationException`, `IOException` e `UnauthorizedAccessException`. O `VACUUM INTO` com disco cheio ou erro de E/S, e a abertura de uma cópia corrompida em `Conferir`, lançam `SqliteException`, que não é capturada e sai de `ExecuteAsync`. `Program.cs` não define `BackgroundServiceExceptionBehavior`.
- **Por que é problema no evento:** pelo padrão do .NET (StopHost, comportamento de framework não verificado aqui), o serviço supervisor para por causa de uma cópia de segurança. O painel perde tudo, e os workers podem ficar órfãos (defeito da migração 016). Além disso, a cópia parcial ou corrompida não é apagada.
- **Como provar:** teste com `CopiaDeSeguranca` apontada para um destino que provoque SQLITE_FULL ou SQLITE_CANTOPEN, e `AgendadorDeCopias.Executar()` deve lançar.
- **Estado:** HIPÓTESE quanto à parada do host; o `catch` incompleto está CONFIRMADO no código. Revisão por E2.
- **Correção mínima:** capturar `SqliteException` (ou `Exception` exceto OOM), apagar o destino em qualquer falha e registrar o motivo.

**E3-03 | ALTO (CRÍTICO se a restauração for feita durante o evento) | `CopiaDeSeguranca.cs` inteiro, `docs/runbooks/RB-09-restauracao-do-banco.md:10-31`, `AgendadorDeCopias.cs:25`**
- **Função:** a restauração, que não existe no código.
- **O que o código faz:** não há código de restauração; o único procedimento é o RB-09 manual (renomear e copiar). As cópias saem a cada 6 horas. A cópia restaurada traz `ticket.used_count`, `status` e `last_used_epoch` do instante da cópia.
- **Por que é problema no evento:** todo ingresso consumido entre a cópia e a restauração volta a valer. Isso libera uma segunda entrada a quem já entrou, e a revenda de cartão perde o relógio de reuso. A outbox antiga reenvia itens já enviados; isso é inofensivo porque o destino deduplica. Nada reconcilia a base `*.antes-da-restauracao` com a restaurada.
- **Como provar:** consumir um QR, fazer a cópia, consumir outro QR, restaurar pelo RB-09 e passar o segundo QR: ele é liberado.
- **Estado:** CONFIRMADO na leitura.
- **Correção mínima:** uma ferramenta de restauração que reaplica `ticket_use_attempt` consumidos da base antiga (`used_count`, `last_used_*`, `status`) antes de subir. Até lá, o RB-09 deve proibir restaurar durante o evento.

**E3-04 | MÉDIO | `src/Access.Infrastructure.SQLite/SqliteConnectionFactory.cs:23-29,56`; contraste com `Telemetria.cs:45-47` e `LeituraSomenteDaOperacao.cs:42-44`; efeito em `src/Access.Application/Ingressos/DecisorDeIngresso.cs:178-193`**
- **Função:** `SqliteConnectionFactory`.
- **O que o código faz:** `busy_timeout` é 5000, mas `DefaultTimeout` não é definido. O próprio comentário do projeto (`Telemetria.cs:45-46`) diz que o Microsoft.Data.Sqlite repete sozinho, até 30 s, a instrução que achou o arquivo travado. `BeginTransaction()` usa `BEGIN IMMEDIATE`: a string está na DLL.
- **Por que é problema no evento:** com outro escritor segurando a trava (vários processos escrevem em `acesso.db`, ADR-0024), `TentarUsar` pode esperar cerca de 30 s, e não os 5 s documentados, antes de negar com `FalhaNaBaseLocal`. É a catraca parada com fila na frente. Hoje não há escritor longo conhecido: a ingestão da nuvem usa páginas de até 1000 itens (`FonteDeCartoesDoPainel.cs:64`).
- **Como provar:** um teste que segura `BEGIN IMMEDIATE` numa conexão e mede `TentarUsar` em outra.
- **Estado:** HIPÓTESE (comportamento de biblioteca).
- **Correção mínima:** `DefaultTimeout` curto (1–2 s) no caminho da decisão, coerente com o orçamento de 150 ms.

**E3-05 | MÉDIO | `src/Edge.Supervisor/SincronizacaoComANuvem.cs:230`, `DrenadorDaOutbox.cs:175-212`, `ConectorDeTentativasDoPainel.cs:43`**
- **O que o código faz:** cada rodada de 30 s chama `DrenarUmaVezAsync` uma vez, com 100 itens. O teto é 200 itens/minuto. `ExecutarAsync`, que repete enquanto há trabalho, não tem chamador em produção.
- **Por que é problema no evento:** num pico de 30 mil pessoas (centenas de leituras por minuto, mais as negativas) o atraso da nuvem só cresce durante o pico.
- **Como provar:** contagem de backlog numa carga simulada de 400 leituras/minuto.
- **Estado:** CONFIRMADO na leitura.
- **Correção mínima:** repetir a drenagem enquanto `TeveTrabalho`, com teto de tempo por rodada.

**E3-06 | MÉDIO | `tests/Integration/QuedaAbruptaTests.cs:19-56`, `tests/CrashProbe/Program.cs:56`**
- **O que o teste faz:** o CHAOS-KILL-01 exercita `AccessJournal`, que não tem nenhum chamador em `src/` (grep). O caminho real é `RepositorioDeIngressos.TentarUsar` (`RepositorioDeIngressos.cs:373-417`). Além disso, o processo é morto depois de todos os commits.
- **Por que é problema:** a capacidade 7 parece provada e não está. O caminho de produção não tem teste de queda, nem no meio da transação nem por falta de energia.
- **Como provar:** uma cobaia que chama `TentarUsar` com o espelho ligado, morta durante as gravações, e conferência de que `ticket.used_count`, as tentativas consumidas e a outbox batem.
- **Estado:** CONFIRMADO.
- **Correção mínima:** apontar a cobaia para `TentarUsar` e `ConfirmarPassagemFisica`.

**E3-07 | MÉDIO | `RepositorioDeIngressos.cs:531-557` (Conciliar)**
- **O que o código faz:** `Usados`, `NuncaUsados`, `CanceladosDepoisDeUsados` e `AvisosPendentes` leem o estado atual (`used_count`, `status`, `reported_at`), não o estado no corte. `comGiro` não limita `passage_confirmed_at` ao corte. São várias consultas sem transação, portanto sem foto única.
- **Por que é problema:** contradiz a promessa da linha 510 ("mesmo corte, mesmo número"). O teste `IngressosDeVariosProvedoresTests.cs:332-348` só confere `UsosConsumidos` e passaria com o defeito. Hoje só o modo bancada usa a função (`Edge.Worker.X86/Program.cs:194`).
- **Como provar:** no próprio teste, `corteCedo.Usados == 1` com `UsosConsumidos == 0`.
- **Estado:** CONFIRMADO na leitura.
- **Correção mínima:** derivar tudo de `ticket_use_attempt` e `ticket_sale` filtrados por corte, dentro de uma transação de leitura.

**E3-08 | MÉDIO | `BilhetesColetados.cs:116-150`**
- **O que o código faz:** os bilhetes recuperados da memória da catraca são gravados (`Gravar`, com deduplicação e tipo 128 corretos), mas `Listar` não tem chamador em produção e nenhum relatório lê `collected_ticket`.
- **Por que é problema:** acessos feitos sem o PC não entram na prestação de contas (capacidades 8 e 13).
- **Estado:** CONFIRMADO por grep.
- **Correção mínima:** incluir os bilhetes na conta do período ou numa linha própria do relatório.

**E3-09 | MÉDIO | `FilaDeComandosSqlite.cs:88-100,127-140`**
- **O que o código faz:** `ExpirarVencidos` só expira comandos `pendente`. Um comando `recebido` cujo worker morreu antes de `Concluir` fica `recebido` para sempre.
- **Por que é problema:** uma liberação manual fica sem desfecho na auditoria, e não se sabe se o braço liberou.
- **Estado:** CONFIRMADO.
- **Correção mínima:** varrer `recebido` com prazo vencido para `falhou`, com o texto "desfecho desconhecido: o worker caiu".

**E3-10 | MÉDIO (revisão por E8) | `Migrations/003_ingressos_e_provedores.sql:58`, `RepositorioDeIngressos.cs:1326-1328` (TentativaEspelhada com o código), `FilaDeSaidaSqlite.cs:197-203` (dead_letter copia o payload), `Migrations/007_leituras_simuladas.sql:10`, `CopiaDeSeguranca.cs:59`**
- **O que o código faz:** o número do cartão ou QR fica em claro em `ticket_use_attempt.qr_normalized` (inclusive códigos desconhecidos), no payload da outbox e da dead_letter, e em `simulated_read`. As cópias de segurança não são cifradas.
- **Por que é problema:** contradiz `Migrations/011_tipos_e_cadastro_de_cartoes.sql:12-15` ("O número continua só em ticket").
- **Estado:** CONFIRMADO.
- **Correção mínima:** gravar HMAC e máscara nas tentativas e na fila (o código só se o contrato da nuvem exigir) e cifrar as cópias.

**E3-11 | MÉDIO | `Migrator.cs:65-110`, `Edge.Worker.X86/Program.cs:229`, `Edge.Supervisor/Program.cs:106`**
- **O que o código faz:**
  - Não há guarda de versão: um binário antigo abre uma base com migrações que ele não conhece sem nenhum aviso.
  - Não há reversão de migração.
  - `schema_version` é lido fora da transação de escrita (linhas 65-74 contra 88). Serviço e workers rodando `Aplicar` ao mesmo tempo podem aplicar a mesma migração duas vezes, e o segundo processo cai com "table already exists". Ele se recupera ao reiniciar.
  - Falta a 014 (comentário em `015_bilhetes_coletados.sql:2`). Uma 014 futura rodaria depois da 017 numa base existente.
- **Estado:** CONFIRMADO na leitura; a corrida é HIPÓTESE.
- **Correção mínima:** reler a versão dentro de `BEGIN IMMEDIATE`; recusar subir se houver migração desconhecida; documentar o rollback (só para a frente).

**Achados BAIXO e INFORMATIVO (uma linha cada):**
- **E3-12 BAIXO:** `Operacao.cs:276-288`. `Resumir` faz cinco varreduras completas de `ticket_use_attempt` a cada chamada, e `COUNT(*) FROM dead_letter` inclui itens reprocessados (diverge de `FilaDeSaidaSqlite.cs:248`).
- **E3-13 BAIXO:** `RepositorioDeIngressos.cs:470-477`. A confirmação de giro busca o item da outbox por `aggregate_id` e varre todos os pendentes do conector (plano medido: `ix_outbox_por_conector`). Usar `idempotency_key='tentativa:{id}'`, que é indexado.
- **E3-14 BAIXO:** a outbox enviada, `simulated_read` e `raw_event` não têm expurgo (único `DELETE` em `FilaDeSaidaSqlite.cs:215`), então a base cresce de evento para evento.
- **E3-15 BAIXO:** `CopiaDeSeguranca.cs:45-47,87-90` com `AgendadorDeCopias.cs:58`. O nome usa hora local (`DateTimeOffset.Now`), a retenção ordena por nome (o horário de verão inverte a ordem) e conta qualquer `acesso-*.db`, inclusive cópia parcial (HIPÓTESE).
- **E3-16 BAIXO:** `DrenadorDaOutbox.cs:75-77`. A conta do comentário está errada mesmo com teto de 2 min (cerca de 16,5 min, não 4 h).
- **E3-17 BAIXO (revisão por E7):** `T005_alert.sql:35` e `T006_fluidity.sql:43`. `UNIQUE` com `portao` NULL não deduplica. A instrução de `src/Edge.Supervisor/RepositorioDosAlertas.cs:31-46` falha sempre ("no such column: old.fechado_em", provado no sqlite3 3.45.1); é código morto.
- **E3-18 BAIXO:** `AccessJournal.cs:315`. `Iso` sem `ToUniversalTime`; o arquivo é código morto em produção.
- **E3-19 BAIXO:** `SqliteConnectionFactory.cs:43-56`. `journal_mode` roda antes de `busy_timeout`; a telemetria faz o inverso (`Telemetria.cs:69-75`).
- **E3-20 BAIXO:** `TrilhaDeCredenciais.cs:190`. `Historico` filtra pela chave atual, então trocar a chave esconde o histórico anterior.
- **E3-21 BAIXO:** `ColetorDeTelemetriaReal.cs:234-298,446-451`. Os contadores são zerados antes de gravar; uma falha perde o minuto e só vai para `Debug.WriteLine`. `dropped` é sobrescrito em vez de somado (440).
- **E3-22 BAIXO:** comentários vencidos em `017_mapa_de_giro.sql:2` ("016 reservada", mas ela existe) e `T007_reuse.sql:17` ("256 hex", são 76 caracteres). O tamanho de `code_key_id` diverge entre 011 e 015 (1-40 contra 4-40).
- **E3-23 BAIXO:** `ConfiguracoesDaBorda.cs:310-364` e `ConfiguracoesDasCatracas.cs:117-182`. A gravação sobrescreve sem conferir a revisão lida (a última gravação vence).
- **E3-24 BAIXO (revisão por E4):** `RepositorioDeIngressos.cs:1019-1030`. Reenviar com `max_uses` maior deixa o ingresso `consumido` mesmo com uso sobrando.
- **E3-25 BAIXO:** `CadernoDesugestoes.cs:24-43` é só esqueleto; a tabela T008 nunca é usada.
- **E3-26 BAIXO (revisão por E8):** `ArquivoDeBancada.cs:72,83,96,107`. As mensagens de problema trazem o código inteiro.
- **E3-27 INFORMATIVO:** `BeginTransaction()` usa `BEGIN IMMEDIATE`, então o consumo é serializado corretamente. O teste `IngressosDeVariosProvedoresTests.cs:262-300` admite que não prova a instrução única.
- **E3-28 INFORMATIVO:** todo SQL montado por interpolação usa só constantes ou nomes de parâmetro gerados (`ConsultasDaOperacao.cs:99-139,255-263`, `FilaDeComandosSqlite.cs:69-79`, `LeiturasSimuladas.cs:59-67`, `CopiaDeSeguranca.cs:59` com aspas dobradas). Não encontrei injeção.
- **E3-29 INFORMATIVO:** o desenho é consumir e depois liberar, sem estorno se a liberação falhar (`DecisorDeIngresso.cs:178-209`, ADR-0007). Fica para E1/E4.
- **E3-30 INFORMATIVO:** `TentarUsar`, `MoverParaCartasMortasAsync`, `BilhetesColetados.Gravar`, a trilha encadeada e o cursor gravado depois de aplicar (`LacoDeIngestao.cs:179-211`) são atômicos e idempotentes como prometem.

### B) Cobertura

Todos os arquivos foram lidos inteiros. Nos marcados com *, li com os comentários `///` filtrados; o código foi lido inteiro. A coluna de funções e instruções é contagem aproximada de métodos (C#) ou de CREATE/ALTER (SQL).

| Arquivo | Linhas | Lido inteiro | Funções/instr. | Achados |
|---|---|---|---|---|
| AccessJournal.cs* | 316 | sim | 15 | E3-06, 18 |
| ArquivoDeBancada.cs* | 164 | sim | 2 | E3-26 |
| BilhetesColetados.cs | 177 | sim | 7 | E3-08 |
| CadernoDesugestoes.cs* | 44 | sim | 4 | E3-25 |
| ChavesDosComandos.cs* | 45 | sim | 2 | 0 |
| ColetorDeTelemetriaReal.cs* | 456 | sim | 14 | E3-21 |
| ConfiguracaoPorCatraca.cs* | 110 | sim | 3 | 0 |
| ConfiguracoesDaBorda.cs* | 382 | sim | 7 | E3-23 |
| ConfiguracoesDasCatracas.cs* | 354 | sim | 9 | E3-23 |
| ConsultasDaOperacao.cs* | 421 | sim | 18 | E3-28 |
| ContextoDasNegativas.cs* | 126 | sim | 5 | 0 |
| CopiaDeSeguranca.cs | 151 | sim | 6 | E3-02, 03, 10, 15 |
| CursoresSqlite.cs* | 75 | sim | 4 | 0 |
| FilaDeComandosSqlite.cs* | 217 | sim | 13 | E3-09 |
| FilaDeSaidaSqlite.cs | 261 | sim | 10 | E3-01, 10, 12 |
| LeituraSomenteDaOperacao.cs* | 97 | sim | 5 | 0 |
| LeiturasSimuladas.cs* | 92 | sim | 4 | E3-10 |
| MapasDeGiro.cs* | 284 | sim | 10 | 0 |
| Migrator.cs | 122 | sim | 6 | E3-11 |
| Operacao.cs | 310 | sim | 12 | E3-12 |
| RepositorioDeIngressos.cs | 1447 | sim | 40 | E3-07, 10, 13, 24, 30 |
| SqliteConnectionFactory.cs | 84 | sim | 5 | E3-04, 19 |
| Telemetria.cs* | 195 | sim | 10 | 0 |
| TrilhaDeCredenciais.cs* | 317 | sim | 12 | E3-20 |
| Access.Infrastructure.SQLite.csproj | 31 | sim | – | 0 |
| Migrations 001 | 136 | sim | 21 | 0 |
| 002 | 38 | sim | 5 | E3-01 (contexto) |
| 003 | 70 | sim | 11 | E3-10 |
| 004 | 39 | sim | 9 | 0 |
| 005 | 10 | sim | 1 | 0 |
| 006 | 29 | sim | 3 | 0 |
| 007 | 17 | sim | 3 | E3-10 |
| 008 | 7 | sim | 4 | 0 |
| 009 | 44 | sim | 6 | E3-09 |
| 010 | 15 | sim | 1 | 0 |
| 011 | 338 | sim | 37 | E3-10, 22 |
| 012 | 138 | sim | 9 | 0 |
| 013 | 20 | sim | 2 | 0 |
| 015 | 67 | sim | 6 | E3-11, 22 |
| 016 | 24 | sim | 2 | 0 |
| 017 | 140 | sim | 16 | E3-22 |
| T001–T004 | 31/33/33/46 | sim | 7 | 0 |
| T005 | 39 | sim | 3 | E3-17 |
| T006 | 47 | sim | 3 | E3-17 |
| T007 | 45 | sim | 3 | E3-22 |
| T008 | 34 | sim | 4 | E3-25 |
| T009 | 50 | sim | 2 | 0 |
| Sync.Core/DrenadorDaOutbox.cs | 345 | sim | 8 | E3-01, 05, 16 |
| Sync.Core/IConectorDeSincronizacao.cs | 106 | sim | 2 | 0 |
| Sync.Core/IFilaDeSaida.cs* | 63 | sim | 5 | 0 |
| Sync.Core/PrioridadeDeSincronizacao.cs* | 71 | sim | 1 | 0 |
| Sync.Core.csproj | 15 | sim | – | 0 |

As migrações são 16 (de 001 a 017, sem a 014), mais 9 da telemetria.

### C) Veredito das capacidades (docs/40 §7)

- **7. Registrar cada acesso sem perder e sem duplicar, inclusive com queda: IMPLEMENTADO SEM PROVA.**
  - O caminho real é uma transação única: consumo, tentativa, espelho e aviso ao provedor (`RepositorioDeIngressos.cs:373-417`), com `synchronous=FULL` (`SqliteConnectionFactory.cs:53`).
  - A corrida entre catracas está provada em CI (`IngressosDeVariosProvedoresTests.cs:262`). O teste falharia se dois consumissem, mas não prova que a instrução única é necessária.
  - O teste de queda (`QuedaAbruptaTests.cs:19`) prova outro caminho, `AccessJournal`, que é código morto (E3-06). Corte de energia não é verificável aqui.
- **10. Fila local: PARCIAL.**
  - A outbox é gravada na mesma transação, a prioridade e o envio para carta morta funcionam (`DrenagemDaOutboxTests.cs:56-230`), e a mudança para carta morta copia e apaga numa transação só (`FilaDeSaidaSqlite.cs:186-221`).
  - Falta: falhas temporárias não deveriam virar carta morta depois de ~35 minutos, não existe reenvio de carta morta (E3-01) e a vazão está limitada a 200 itens por minuto (E3-05).
- **13. Base de dados da prestação de contas: PARCIAL.**
  - `ConsultasDaOperacao.Contas` conta por janela de tempo e está correta.
  - Falta:
    - a conciliação por provedor não reproduz o mesmo número com o mesmo corte e só a bancada a usa (E3-07);
    - os bilhetes coletados ficam fora da conta (E3-08);
    - não existe tabela para registrar um "corte fechado";
    - o histórico sem expurgo (E3-14).
- **18. Cópia e restauração: PARCIAL.**
  - A cópia por `VACUUM INTO` é consistente com o WAL, conferida e com retenção de 14. Está provada em CI por `CopiaDeSegurancaTests.cs:27` (falharia se a cópia perdesse dados ou viesse corrompida).
  - Falta:
    - a restauração não existe no código, só o runbook manual, que reverte os consumos (E3-03);
    - uma falha da cópia pode derrubar o serviço (E3-02).

### D) Perguntas de decisão

1. **O que fazer com item que falha por rede?**
   - A) Retentar sem limite, com espera de no máximo 5 minutos; carta morta só para recusa permanente; mais um botão "reenviar". **Recomendada**: rede fora não é erro do item, e hoje ~35 minutos sem internet já descartam acessos.
   - B) Manter as 12 tentativas e só acrescentar o botão de reenvio.
   - C) Subir para 100 tentativas.
2. **Restauração durante o evento.**
   - A) Ferramenta de restauração que reaplica os consumos da base antiga antes de subir.
   - B) Proibir restaurar durante o evento e operar com liberação manual até o fim. **Recomendada até A existir**: com o runbook atual, quem já entrou volta a ser liberado.
   - C) Manter o RB-09 como está.
3. **Número do cartão ou QR em claro fora de `ticket`.**
   - A) HMAC e máscara em `ticket_use_attempt` e na fila; o código só vai à nuvem se o contrato exigir; cópias cifradas. **Recomendada**: alinha com a 011 e com a LGPD sem afetar a decisão de 150 ms, que só usa `ticket`.
   - B) Manter em claro e só cifrar as cópias.
   - C) Manter como está.
   - Quem decide: o responsável pelo contrato da nuvem e pela LGPD.
