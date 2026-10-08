> Relatório bruto de um especialista da auditoria linha a linha (docs/40), commit auditado 29df501.
> Os estados aqui são os do especialista, antes da revisão cruzada; o consolidado é o docs/41.

# E5 — Integrações externas: relatório (commit 29df501, somente leitura)

Usei apenas Read, Grep e Bash de leitura (grep, sed, wc, ls). Não fiz build, não rodei testes e não acessei a rede. Tudo o que depende da nuvem (Supabase, Worker da Zet) está marcado NÃO VERIFICÁVEL AQUI. Os achados CRÍTICO e ALTO ainda precisam da revisão cruzada do E8; até lá, o estado deles é "LIDO NO CÓDIGO".

**Fato de partida (o que está ligado em produção):** o único caminho com a nuvem montado em produção é `SincronizacaoComANuvem.Montar` (`src/Edge.Supervisor/SincronizacaoComANuvem.cs:176-200`, chamado em `Program.cs:206`). Ele liga `FonteDeCartoesDoPainel` e `ConectorDeTentativasDoPainel`.
- `TradutorDaZet`, `TradutorDoContratoV1`, `FonteDeRelay` e `ConectorRest` só são instanciados em `tests/`. Nenhuma referência em `src/` fora das próprias definições.
- `Relay.Ingressos` está no .sln, mas não tem artefato de implantação: nenhum Dockerfile, nada em `installer/` nem em `ci.yml`.
- Conclusão: **a Zet não chega à catraca por nenhum caminho de produção hoje.**

---

## A) Achados

**E5-1 | CRÍTICO | lista de cartões cortada aparece como "nuvem ok"**
- **Onde:** `src/Edge.Supervisor/SincronizacaoComANuvem.cs:211-246` (`UmaRodadaAsync`), com `src/Sync.Connectors.Rest/Painel/FonteDeCartoesDoPainel.cs:188-199` (`Interpretar`).
- **O que o código faz:** quando a lista parece cortada (≥1.000 cartões sem `contract_version`, ou menos que `total_cards`), a fonte devolve uma página com `ProximoCursor: null`. Não lança erro. Por isso `cartoes.Interrompido` fica falso e a rodada chama `_estado.RegistrarSucesso` (linha 245). O único aviso é uma linha de log (`:190-192`).
- **Por que é problema no evento:** são 2.243 cartões (docs/31:21) e o limite padrão do Supabase é 1.000. Se a função ainda não pagina (docs/22:352-355 e :494-496 dizem "falta confirmar"), uns 1.243 cartões válidos nunca chegam à base local. Essas pessoas são negadas com `CREDENCIAL_DESCONHECIDA`, e o painel do operador mostra a nuvem sincronizada.
- **Como provar:**
  - Hoje `tests/Integration/PainelNaNuvemTests.cs:441-454` (`Lista_possivelmente_cortada_avisa_e_nao_da_por_sincronizado`) só confere o cursor nulo e o callback. Não olha `EstadoDaNuvem`. O nome do teste promete mais do que ele prova.
  - Teste que deveria existir: em `SincronizacaoDoServicoTests`, montar um painel falso com mais de 1.000 cartões e sem `contract_version`, e exigir que `UmaRodadaAsync` devolva `false` e que `UltimaFalha` fique preenchida.
- **Estado:** o comportamento local está lido no código. O corte da função de produção é NÃO VERIFICÁVEL AQUI.
- **Correção mínima:** suspeita de corte vira falha da rodada (`falhas.Add`), aparece na tela e entra em `Interrompido`/`Erro`.

**E5-2 | ALTO | 401/403 manda toda tentativa direto para cartas mortas, sem caminho de volta**
- **Onde:**
  - `src/Sync.Connectors.Rest/Painel/ConectorDeTentativasDoPainel.cs:146-152` e `ConectorRest.cs:156-163` (`ClassificacaoHttp.ValeRepetir`).
  - `src/Sync.Core/DrenadorDaOutbox.cs:278-283`.
  - `src/Access.Infrastructure.SQLite/FilaDeSaidaSqlite.cs:177-222` e `:244-249`.
- **O que o código faz:** um 401 ou 403 vira `FalhaPermanente`, e o item é movido na hora para `dead_letter`. O teste `PainelTests.cs:254` fixa esse comportamento de propósito. A coluna `reprocessed_at` só é lida (`FilaDeSaidaSqlite.cs:248`). Nenhum código em `src/` reenfileira carta morta.
- **Por que é problema no evento:** basta um segredo ausente no cofre, revogado, trocado ou com `device_id` errado (ver E5-3). Quando a nuvem passar a exigir o segredo (P0 do docs/31 §2.2), cada lote de 100 tentativas vai inteiro para cartas mortas na primeira rodada, a cada 30 s.
  - A prestação de contas na nuvem é perdida. O registro local continua no SQLite.
  - A tela mostra só um contador ("Recusados pela nuvem", `Desktop.ViewModels/Telas.cs:617`), e o operador não tem como reenviar.
  - Agravante: sem segredo no cofre, `CabecalhoDeSegredo.SendAsync` (`src/Edge.Supervisor/CofreDeSegredos.cs:164`) simplesmente não põe o cabeçalho. Não há aviso.
- **Como provar:** um painel falso que responde 401 e uma rodada do drenador. Todos os itens acabam em `dead_letter` e a outbox fica vazia.
- **Estado:** lido no código. A data em que a nuvem passa a exigir o segredo é NÃO VERIFICÁVEL AQUI.
- **Correção mínima:**
  - Tratar 401/403 como falha de configuração: suspender o conector (mesmo tratamento de "conector não registrado", `DrenadorDaOutbox.cs:148-156`) e alertar o operador, em vez de matar o item.
  - Criar um comando "reenviar cartas mortas" que grave `reprocessed_at`.
  - Recusar subir a sincronização sem segredo no cofre.

**E5-3 | ALTO | `device_id` das tentativas não bate com o segredo único do equipamento**
- **Onde:** `ConectorDeTentativasDoPainel.cs:109` e `:124`; `SincronizacaoComANuvem.cs:198`; `src/Access.Domain/Ticketing/TentativaEspelhada.cs:36`.
- **O que o código faz:**
  - As tentativas são agrupadas e enviadas com `device_id` = `Tentativa.Dispositivo`, ou seja, o nome de cada catraca.
  - `Montar` não passa `equipamentoNoPainel`, então não há tradução de nome.
  - Os cartões usam `nuvem.Dispositivo` (`borda-01`).
  - O segredo é um só, `"nuvem"` (`CofreDeSegredos.cs:142`).
- **Por que é problema no evento:** o contrato (docs/31 §2.2, linha 70) diz que "o `device_id` do pedido tem de ser o do segredo". O próprio docs/31 se contradiz: o exemplo de cartões usa `"borda-01"` (linha 87) e o de eventos usa `"CATRACA-ENTRADA-01"` (linha 135). docs/22:328 manda "uma requisição por catraca". Quando a nuvem implementar o §2.2 como está escrito, todo envio de tentativa recebe 401 e cai no E5-2.
- **Como provar:** `PainelNaNuvemTests.Uma_requisicao_por_catraca` já mostra `device_id` diferente por catraca, com o mesmo Bearer.
- **Estado:** NÃO VERIFICÁVEL AQUI (depende de como a nuvem vai fazer a conferência). A divergência entre o código e o docs/31 está lida.
- **Correção mínima:** decidir a pergunta D1. Ou `device_id` = equipamento e a catraca vai em `extra`, ou um segredo por catraca.

**E5-4 | ALTO | depois de uns 35 minutos sem internet, as tentativas vão para cartas mortas**
- **Onde:** `SincronizacaoComANuvem.cs:196-200` e `DrenadorDaOutbox.cs:85` e `:286-297`.
- **O que o código faz:**
  - A espera entre tentativas é `min(300, 5·2^min(t,6))` s: 10, 20, 40, 80, 160 e depois 300 s.
  - O máximo é 12 tentativas, que é o padrão porque `Montar` não muda.
  - Soma das 11 esperas: cerca de 2.110 s, uns 35 min.
  - Rede fora e tempo esgotado contam como tentativa (`ConectorDeTentativasDoPainel.cs:135-142`). Na 12ª, o item vai para carta morta.
- **Por que é problema no evento:** com a internet caída por mais de 35 min, toda tentativa que já entrou no ciclo vai para cartas mortas, sem reenvio (E5-2). Dois textos dizem o contrário:
  - `SincronizacaoComANuvem.cs:91-92` diz que as tentativas "se acumulam na outbox até a próxima vez".
  - `DrenadorDaOutbox.cs:75-77` promete "cerca de quatro horas". É falso com esta espera, e também com a de 2 min citada ali.
- **Como provar:** teste com `FakeTimeProvider`, o painel falso respondendo com exceção de rede, avançando 40 min em rodadas de 30 s. O resultado esperado é que todos os itens acabem em `dead_letter`.
- **Estado:** lido no código.
- **Correção mínima:** falha temporária (rede, 5xx, tempo esgotado) não deve contar para carta morta, ou deve ter limite por idade (por exemplo, dias) e não por número de tentativas. Corrigir os dois comentários.

**E5-5 | ALTO | maquininha e cortesia: nenhum caminho (5,8% dos pedidos)**
- **Onde:** docs/30:60-66 e :112 (Z1); docs/31:214-216. Não há nenhum código que consuma esses ingressos.
- **O que o código faz:** `TradutorDaZet` só lê webhooks CP/ES. Vendas na maquininha (1.161 em 2025) e cortesias (1.602 pedidos no total, 5,8%) nunca geram webhook.
- **Por que é problema no evento:** esses ingressos não existem na base da catraca. A pessoa é negada, a menos que haja contingência manual.
- **Estado:** BLOQUEADO POR TERCEIRO (a Zet precisa responder o Z1).
- **Correção mínima:** exportação ou API da Zet com voucher, entrando pelo mesmo tradutor (caminho C do docs/30 §4).

**E5-6 | ALTO (latente; não está ligado em produção) | relé aceita ingresso forjado de quem tiver a URL**
- **Onde:** `src/Relay.Ingressos/Program.cs:45-83`; `TradutorDaZet.cs:158-168`.
- **O que o código faz:**
  - A única credencial é o token no caminho da URL. Não há assinatura do corpo, não há limite de taxa, e não há configuração de log: o projeto não tem `appsettings`.
  - Hipótese: o diagnóstico de hospedagem do ASP.NET Core registra "Request starting … /webhook/<token>" no nível Information, o que poria o token em texto no log.
  - Do lado do tradutor, só `data.event.id` filtra. Qualquer voucher num corpo CP vira ingresso válido.
- **Por que é problema no evento:** se o token vazar (log, Worker, captura de tela), alguém injeta vouchers válidos e libera quem não pagou. Pela escala, isso seria CRÍTICO no dia em que o caminho A for ligado.
- **Como provar:** `ReleDeWebhookTests` não testa os endpoints HTTP (não há `WebApplicationFactory`). Teste que deveria existir: POST com o token certo e um corpo CP inventado; o ingresso tem de ser ingerido. Conferir também o log da hospedagem.
- **Estado:** HIPÓTESE para o vazamento por log. NÃO VERIFICÁVEL AQUI para a hospedagem e o Worker.
- **Correção mínima:**
  - Filtro de log para `Microsoft.AspNetCore.Hosting` em Warning.
  - HMAC do corpo assinado pelo Worker (que é nosso), conferido no relé.
  - Limite de taxa.

**E5-7 | MÉDIO | dados pessoais guardados para sempre no relé**
- **Onde:** `src/Relay.Ingressos/ArmazenamentoDeEntregas.cs:58-77`, `:82-105`; `Program.cs:70-72`.
- **O que o código faz:** o corpo da Zet é guardado inteiro, com nome, e-mail, telefone e CPF do comprador (`TradutorDaZet.cs:17-18` admite que vêm no corpo). Os gatilhos proíbem DELETE. Não há expurgo. Cabeçalhos como `X-Forwarded-For` (IP) também são guardados. E `/entregas` devolve tudo isso à borda.
- **Por que é problema:** a LGPD exige retenção definida e expurgo (Z7 em docs/30:118). Aqui o expurgo só é possível desmontando a proteção.
- **Estado:** lido no código. Precisa de revisão do E8.
- **Correção mínima:** ou guardar o corpo minimizado, ou expurgar por idade com o gatilho trocado por "DELETE só depois de N dias".

**E5-8 | MÉDIO | uma linha ruim derruba a entrega inteira, inclusive estorno**
- **Onde:** `TradutorDaZet.cs:130-136`, `TradutorDoContratoV1.cs:87-94` e `FonteDeRelay.cs:176-183`.
- **O que o código faz:** um único item inválido (voucher ausente ou de tamanho errado, data inválida) lança `FormatException` para a entrega inteira. A entrega é pulada e o cursor segue.
- **Por que é problema:** numa compra com vários vouchers, ninguém entra. Num estorno (ES), nada é cancelado, e o ingresso estornado continua válido. "Reprocessar depois" não existe: `ReiniciarVarredura` e `VarrerTudoAsync` só são chamados em testes.
- **Estado:** lido no código, latente (não está ligado em produção).
- **Correção mínima:** recusar por item. No ES, cancelar os itens que forem legíveis.

**E5-9 | MÉDIO | a lista completa de cartões nunca é refeita**
- **Onde:** `LacoDeIngestao.cs:122-137` e `SincronizacaoComANuvem.cs:211`.
- **O que o código faz:** só o incremental roda. Completa só acontece com o cursor nulo (primeira vez ou corte). Cartão recusado (`FonteDeCartoesDoPainel.cs:205-283`) deixa o cursor avançar e nunca é relido. Mudar `nuvem.perfil` não zera o cursor (`Montar`, `:147-194`), então os cartões já gravados ficam normalizados pelo perfil antigo. A lista completa também não cancela cartões que sumiram dela.
- **Por que é problema:**
  - Uma mudança perdida no incremental (`sync_timestamp` marcado depois da consulta: NÃO VERIFICÁVEL AQUI) ou um cartão recusado por perfil errado nunca se corrige.
  - Hipótese a confirmar pelo E4: trocar o perfil depois da bancada deixa a base inconsistente com o leitor.
- **Correção mínima:** varredura completa periódica e zerar o cursor quando o perfil mudar.

**E5-10 | MÉDIO | falha parcial depende da grafia que a nuvem devolve**
- **Onde:** `ConectorDeTentativasDoPainel.cs:164-172`.
- **O que o código faz:** a correspondência de `failed_events` é por texto exato de `(card_id, occurred_at)`.
- **Por que é problema:** se a nuvem devolver o horário normalizado pelo Postgres (`+00:00`, ou centésimos em vez de milésimos), nenhum item é reconhecido. O grupo inteiro de até 100 é repetido 12 vezes e depois vai para cartas mortas (E5-4), embora 99 já estivessem salvos. Dois itens com o mesmo cartão e o mesmo milissegundo causam o mesmo efeito.
- **Estado:** NÃO VERIFICÁVEL AQUI.
- **Correção mínima:** comparar instantes como `DateTimeOffset` e contar só os que casarem.

**E5-11 | MÉDIO | Worker repassa ao relé sem retentativa**
- docs/31:200-204: `ctx.waitUntil(fetch(...).catch(() => {}))`.
- Se o relé estiver fora ou devolver 500 (exceção de disco em `ArmazenamentoDeEntregas.Gravar`), a entrega se perde para a catraca, e não há reconciliação além do backup.
- Estado: NÃO VERIFICÁVEL AQUI.

**Baixos e informativos (uma linha cada):**
- **E5-12 | BAIXO:** `Program.cs:57-68` lê o corpo chunked inteiro antes de checar 1 MB. O teto real é o padrão do Kestrel, 30 MB.
- **E5-13 | BAIXO:** o relé não força TLS nem HSTS, e o Bearer de leitura (`Program.cs:113-119`) depende da hospedagem.
- **E5-14 | BAIXO:** `FonteDeRelay.cs:168` não trata `CorpoBase64` nulo. A `ArgumentNullException` trava o cursor naquela página.
- **E5-15 | BAIXO:** `TradutorDaZet.cs:158-168` descarta em silêncio (devolve `[]`, sem contar como ilegível) um `event.id` que venha como texto ou ausente.
- **E5-16 | BAIXO:** drenagem de 100 tentativas a cada 30 s (`SincronizacaoComANuvem.cs:230`, uma chamada por rodada) dá 200/min. Recuperar 2 h de fila leva uns 20 min.
- **E5-17 | BAIXO:** `ConectorRest` e `TradutorDoContratoV1` são código morto em produção.
- **E5-18 | INFORMATIVO:**
  - TLS para a nuvem é exigido (`SincronizacaoComANuvem.cs:51-54`).
  - O Bearer vem do cofre.
  - `Segredos.Conferem` usa `FixedTimeEquals`; só o tamanho vaza, o que é aceitável.
  - O cursor do relé por `seq` (AUTOINCREMENT, escritor único) não pula nem repete.
  - O estorno é definitivo na base (`RepositorioDeIngressos.cs:1020-1029`), então ES antes de CP fica correto.

---

## B) Cobertura

| Arquivo | Linhas | Lido inteiro | Nº funções | Nº achados |
|---|---|---|---|---|
| `src/Sync.Connectors.Rest/TradutorDaZet.cs` | 280 | sim | 10 | 4 (E5-5, 6, 8, 15) |
| `src/Sync.Connectors.Rest/TradutorDoContratoV1.cs` | 286 | sim | 10 | 2 (E5-8, 17) |
| `src/Sync.Connectors.Rest/FonteDeRelay.cs` | 221 | sim | 5 | 2 (E5-8, 14) |
| `src/Sync.Connectors.Rest/ConectorRest.cs` | 193 | sim | 9 | 2 (E5-2, 17) |
| `src/Sync.Connectors.Rest/Painel/FonteDeCartoesDoPainel.cs` | 360 | sim | 10 | 2 (E5-1, 9) |
| `src/Sync.Connectors.Rest/Painel/ConectorDeTentativasDoPainel.cs` | 244 | sim | 7 | 4 (E5-2, 3, 4, 10) |
| `src/Sync.Ingestao/LacoDeIngestao.cs` | 244 | sim | 7 | 1 (E5-9) |
| `src/Sync.Ingestao/Portas.cs` | 73 | sim | 1 | 0 |
| `src/Relay.Ingressos/Program.cs` | 127 | sim | 4 | 4 (E5-6, 7, 12, 13) |
| `src/Relay.Ingressos/ArmazenamentoDeEntregas.cs` | 166 | sim | 6 | 2 (E5-7, 11) |
| `src/Relay.Ingressos/Segredos.cs` | 49 | sim | 2 | 0 (E5-18 informativo) |
| `src/Relay.Ingressos/Cabecalhos.cs` | 28 | sim | 1 | 0 |

Também li inteiros, para contexto: `SincronizacaoComANuvem.cs` (279), `Sync.Core/DrenadorDaOutbox.cs` (346), docs/31 e docs/40. Li trechos de `FilaDeSaidaSqlite.cs`, `CofreDeSegredos.cs:139-179`, `RepositorioDeIngressos.cs:1015-1035`, docs/22 §8 e docs/30 §4-5.

---

## C) Veredito nas capacidades

**Capacidade 10 — sincronizar com a nuvem (fila, retentativa, cartas mortas): PARCIAL.**
- **O que existe e tem prova em CI:**
  - Cartões descem e tentativas sobem, com falha parcial 200 (`PainelNaNuvemTests.Falha_parcial_com_200_manda_para_cartas_mortas_so_o_evento_recusado`).
  - Repetição sem duplicar (`Resposta_perdida_na_volta_nao_vira_entrada_duplicada_na_nuvem`).
  - Cursor (`LacoDeIngestaoTests.Provedor_fora_do_ar_nao_move_o_cursor`).
  - Zeros à esquerda (`PainelTests.Numero_de_cartao_como_numero_json_e_recusado`).
  - Esses testes falhariam com a função errada, porque comparam itens e cursor. Mas rodam contra um painel falso.
- **O que falta:**
  - Cartas mortas sem reenvio (E5-2).
  - Morte por tempo, aos 35 min (E5-4).
  - Corte silencioso dado como sucesso (E5-1); o teste correspondente não prova o que o nome diz.
  - `device_id` incoerente com o segredo (E5-3).
  - Nada testado contra a nuvem real: NÃO VERIFICÁVEL AQUI.

**Capacidade 11 — receber ingressos da Zet e conciliar: PARCIAL, e BLOQUEADO POR DECISÃO OU TERCEIRO.**
- `TradutorDaZet`, `FonteDeRelay` e o relé existem com testes unitários (`TradutorDaZetTests.O_estorno_cancela_pela_mesma_referencia_da_compra`, `FonteDeRelayTests.Tradutor_nao_configurado_nao_consome_entrega_nenhuma`, `ReleDeWebhookTests.Uma_entrega_nao_pode_ser_alterada_nem_removida`).
- Mas **não estão ligados em nenhum caminho de produção**: o supervisor não instancia `FonteDeRelay`, e o relé não tem implantação.
- Os endpoints HTTP do relé não têm teste.
- Bloqueios:
  - Z2: escolher o caminho A, B ou C e alterar o Worker. Quem decide: você e a equipe da nuvem.
  - Z1: maquininha e cortesia, 5,8% (E5-5). Depende da Zet.
- A conciliação com a bilheteria não está nos meus arquivos.

**Capacidade 16 — segurança da nuvem (parte nuvem): PARCIAL e NÃO VERIFICÁVEL AQUI no servidor.**
- **Do nosso lado:**
  - https obrigatório (`SincronizacaoComANuvem.cs:51`).
  - Bearer vindo do cofre DPAPI, testado em `SincronizacaoDoServicoTests.O_segredo_sai_do_cofre_para_o_cabecalho_e_nunca_para_o_registro`. Esse teste falharia sem o cabeçalho.
  - O relé compara em tempo constante.
- **Falhas do nosso lado:**
  - Sem segredo, a borda envia sem cabeçalho e sem avisar (E5-2).
  - Relé sem assinatura do corpo e com token provavelmente em log (E5-6).
- **Do lado da nuvem:** as P0 do docs/31 (`anon` lendo `authorizations`; funções sem conferir credencial) dependem de terceiro.

---

## D) Perguntas de decisão

**D1. Que `device_id` vai nas tentativas, frente a "um segredo por equipamento"?**
- (a) **Recomendada:** um `device_id` e um segredo por PC de borda; o nome da catraca vai em `extra.catraca`.
- (b) Um segredo por catraca no cofre.
- (c) A nuvem aceita qualquer `device_id` sob o segredo da borda.
- **Motivo:** (a) é a menor mudança (passar `equipamentoNoPainel` em `Montar`), bate com o §2.2 e não multiplica segredos.

**D2. O que faz uma falha temporária longa e um 401?**
- (a) **Recomendada:** falha temporária nunca vira carta morta por contagem, só por idade de dias; 401/403 suspende o conector com alerta; tela com "reenviar cartas mortas".
- (b) Manter as 12 tentativas e só aumentar o teto.
- (c) Manter como está.
- **Motivo:** hoje 35 min sem internet, ou um segredo errado, apagam a prestação de contas da nuvem sem caminho de volta.

**D3. Como a Zet chega à catraca no evento?**
- (a) **Recomendada:** caminho A (Worker repassa ao relé) com HMAC do corpo, mais caminho C (arquivo diário) como contingência, e exigir da Zet a exportação da maquininha e das cortesias até uma data.
- (b) Só o caminho C.
- (c) Caminho B (ler do Supabase de produção).
- **Motivo:** 61% dos ingressos são comprados no dia, então só o A cobre a tempo. O C cobre falha do relé e o E5-11. O B põe dado pessoal de produção no caminho do evento.
