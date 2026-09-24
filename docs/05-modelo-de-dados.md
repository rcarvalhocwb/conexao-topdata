# 05 — Modelo de dados inicial

SQLite/WAL, `foreign_keys=ON`, migrações versionadas e numeradas. O DDL abaixo é o
**esboço da Fase 1** — nomes e tipos são estáveis o bastante para discussão, e migrações
posteriores refinam.

Convenções:

- Chaves primárias: `TEXT` com **UUIDv7** (ordenável por tempo).
- Tempo: `TEXT` ISO-8601 UTC. Nunca hora local no banco; fuso é apresentação.
- Credencial: **sempre `TEXT`** ([ADR-0008](ADR/ADR-0008-credencial-como-string.md)).
- Dado sensível: coluna `_enc` cifrada ([ADR-0014](ADR/ADR-0014-criptografia-e-biometria.md)).

## 1. Estrutura organizacional

```sql
CREATE TABLE organization (id TEXT PRIMARY KEY, name TEXT NOT NULL, created_at TEXT NOT NULL);
CREATE TABLE site         (id TEXT PRIMARY KEY, organization_id TEXT NOT NULL REFERENCES organization(id),
                           name TEXT NOT NULL, timezone TEXT NOT NULL);
CREATE TABLE event        (id TEXT PRIMARY KEY, site_id TEXT NOT NULL REFERENCES site(id),
                           name TEXT NOT NULL, starts_at TEXT NOT NULL, ends_at TEXT NOT NULL,
                           expected_attendance INTEGER, entry_window_minutes INTEGER);
CREATE TABLE sector       (id TEXT PRIMARY KEY, event_id TEXT NOT NULL REFERENCES event(id),
                           name TEXT NOT NULL, capacity INTEGER);
CREATE TABLE zone         (id TEXT PRIMARY KEY, site_id TEXT NOT NULL REFERENCES site(id), name TEXT NOT NULL);
CREATE TABLE gate         (id TEXT PRIMARY KEY, zone_id TEXT NOT NULL REFERENCES zone(id),
                           name TEXT NOT NULL,
                           physical_profile_json TEXT,          -- resultado do comissionamento
                           commissioned_at TEXT, commissioned_by TEXT,
                           fail_policy_json TEXT NOT NULL,      -- ADR-0013: sem default silencioso
                           workflow TEXT NOT NULL DEFAULT 'Standard'); -- ou 'CollectCardThenEnter'
```

`gate.physical_profile_json` é o que impede a nomenclatura de liberação de virar
`hardcode`: ele diz qual acionamento é `EntradaLógica` **naquele** portão.

## 2. Equipamentos e capacidades

```sql
CREATE TABLE device (
  id TEXT PRIMARY KEY, gate_id TEXT REFERENCES gate(id), site_id TEXT NOT NULL REFERENCES site(id),
  model TEXT NOT NULL, serial TEXT, firmware TEXT, board TEXT,
  ip TEXT, port INTEGER,
  worker_group TEXT NOT NULL,                  -- partição (ADR-0005)
  state TEXT NOT NULL,                         -- máquina de estados
  homologation_status TEXT NOT NULL DEFAULT 'NAO_ENSAIADO',
  last_seen_at TEXT, current_boot_id TEXT);

CREATE TABLE device_capability (               -- resultado da descoberta (ADR-0010)
  device_id TEXT NOT NULL REFERENCES device(id), capability TEXT NOT NULL,
  supported INTEGER NOT NULL, source TEXT NOT NULL,   -- 'firmware' | 'matriz' | 'bancada'
  verified_at TEXT, PRIMARY KEY (device_id, capability));

CREATE TABLE device_config_version (           -- diff, aprovação e rollback (CA-08)
  id TEXT PRIMARY KEY, device_id TEXT NOT NULL REFERENCES device(id),
  version INTEGER NOT NULL, config_json TEXT NOT NULL, hash TEXT NOT NULL,
  applied_at TEXT, applied_by TEXT, approved_by TEXT,
  rollback_of TEXT REFERENCES device_config_version(id),
  drift_detected_at TEXT);                     -- divergência lida do equipamento (WebServer)
```

`drift_detected_at` existe porque a configuração feita pelo WebServer do equipamento pode
sobrescrever a nossa. A fonte de verdade é `device_config_version`; a leitura periódica
compara e **alerta**, em vez de reescrever em silêncio.

## 3. Pessoas, credenciais e ingressos

```sql
CREATE TABLE person (
  id TEXT PRIMARY KEY, site_id TEXT NOT NULL REFERENCES site(id),
  kind TEXT NOT NULL,                          -- 'visitante'|'operador'|'equipe'|'prestador'
  full_name_enc TEXT NOT NULL, document_enc TEXT, contact_enc TEXT,
  consent_basis TEXT, consent_at TEXT, retention_until TEXT,
  created_at TEXT NOT NULL);

CREATE TABLE credential (
  id TEXT PRIMARY KEY, site_id TEXT NOT NULL REFERENCES site(id),
  type TEXT NOT NULL,                          -- 'rfid'|'mifare'|'prox'|'barcode'|'qr'|'pin'|'bio'|'face'
  raw_value TEXT NOT NULL,                     -- string, sempre (ADR-0008)
  normalized_value TEXT NOT NULL,
  normalization_profile TEXT NOT NULL,
  status TEXT NOT NULL,                        -- 'ativa'|'bloqueada'|'perdida'|'recolhida'|'reemitida'
  blocked_reason TEXT, created_at TEXT NOT NULL);
CREATE UNIQUE INDEX ux_credential_lookup ON credential(site_id, type, normalized_value);

CREATE TABLE person_credential (
  person_id TEXT NOT NULL REFERENCES person(id), credential_id TEXT NOT NULL REFERENCES credential(id),
  valid_from TEXT, valid_to TEXT, PRIMARY KEY (person_id, credential_id));

CREATE TABLE ticket (
  id TEXT PRIMARY KEY, event_id TEXT NOT NULL REFERENCES event(id),
  sector_id TEXT REFERENCES sector(id), external_ref TEXT,
  credential_id TEXT REFERENCES credential(id), person_id TEXT REFERENCES person(id),
  max_uses INTEGER NOT NULL DEFAULT 1, used_count INTEGER NOT NULL DEFAULT 0,
  status TEXT NOT NULL,                        -- 'valido'|'reservado'|'consumido'|'cancelado'
  reserved_until TEXT, reserved_by_gate TEXT REFERENCES gate(id));
CREATE INDEX ix_ticket_credential ON ticket(credential_id);
```

**`reserved_until` é o coração da correção do workflow da urna.** A reserva é atômica
(`UPDATE ... WHERE status='valido'`), o que faz a tentativa simultânea em dois gates ter
exatamente um vencedor, sem lock distribuído.

### Estoque e custódia de cartões

```sql
CREATE TABLE card_stock (
  credential_id TEXT PRIMARY KEY REFERENCES credential(id),
  batch TEXT, location TEXT, state TEXT NOT NULL); -- 'em_estoque'|'entregue'|'recolhido'|'perdido'|'danificado'

CREATE TABLE card_custody_event (
  id TEXT PRIMARY KEY, credential_id TEXT NOT NULL REFERENCES credential(id),
  action TEXT NOT NULL,                        -- 'entrega'|'coleta'|'esvaziamento_urna'|'perda'|'reemissao'
  gate_id TEXT REFERENCES gate(id), operator_id TEXT, at TEXT NOT NULL,
  declared_count INTEGER, expected_count INTEGER, seal TEXT, notes TEXT);
```

## 4. Regras de acesso

```sql
CREATE TABLE access_rule (
  id TEXT PRIMARY KEY, event_id TEXT REFERENCES event(id), name TEXT NOT NULL,
  priority INTEGER NOT NULL, effect TEXT NOT NULL, -- 'allow'|'deny'|'review'
  criteria_json TEXT NOT NULL,                 -- datas, dias, horários, gates, sentido, grupo, usos
  enabled INTEGER NOT NULL DEFAULT 1, version INTEGER NOT NULL);

CREATE TABLE occupancy_counter (
  scope_type TEXT NOT NULL, scope_id TEXT NOT NULL,   -- 'sector'|'zone'|'event'
  current INTEGER NOT NULL, limit_value INTEGER, updated_at TEXT NOT NULL,
  PRIMARY KEY (scope_type, scope_id));

CREATE TABLE antipassback_state (
  person_id TEXT NOT NULL, zone_id TEXT NOT NULL,
  last_direction TEXT NOT NULL, last_at TEXT NOT NULL, last_gate_id TEXT NOT NULL,
  seq INTEGER NOT NULL, PRIMARY KEY (person_id, zone_id));
```

**Anti-passback sob partição de rede:** quando workers ou hosts perdem contato, o estado
converge por `(last_at, seq)` — o registro mais recente vence, empate resolve por `seq`.
Durante a partição, cada partição decide com o que tem e **marca a decisão como
`ConvergencePending`**. O comportamento é documentado, configurável (permissivo ou
restritivo durante partição) e visível no relatório. Não existe promessa de consistência
forte entre gates sem rede — existe comportamento declarado.

## 5. Eventos, decisões e comandos

```sql
CREATE TABLE raw_event (                       -- ADR-0018: nada é descartado
  id TEXT PRIMARY KEY,                         -- UUIDv7
  device_id TEXT NOT NULL REFERENCES device(id), boot_id TEXT NOT NULL, device_seq INTEGER NOT NULL,
  origin_raw INTEGER NOT NULL, origin_known TEXT,
  payload BLOB NOT NULL,                       -- original, íntegro
  device_time TEXT, received_time TEXT NOT NULL, server_time TEXT,
  correlation_id TEXT NOT NULL);
CREATE UNIQUE INDEX ux_raw_event_dedupe ON raw_event(device_id, boot_id, device_seq);

CREATE TABLE access_decision (
  id TEXT PRIMARY KEY, raw_event_id TEXT REFERENCES raw_event(id),
  device_id TEXT NOT NULL, gate_id TEXT, credential_id TEXT, person_id TEXT, ticket_id TEXT,
  outcome TEXT NOT NULL,                       -- Allowed|Denied|Review|OfflineFallback
  reason_code TEXT NOT NULL, rule_trace_json TEXT,
  elapsed_ms INTEGER NOT NULL, degradation_tier TEXT NOT NULL, -- T0..T3
  decided_at TEXT NOT NULL);

CREATE TABLE device_command (
  id TEXT PRIMARY KEY, device_id TEXT NOT NULL, decision_id TEXT REFERENCES access_decision(id),
  command TEXT NOT NULL, params_json TEXT,
  issued_at TEXT NOT NULL, native_return TEXT, completed_at TEXT,
  idempotency_key TEXT NOT NULL UNIQUE);

CREATE TABLE physical_passage (                -- ADR-0007: só com prova física
  id TEXT PRIMARY KEY, decision_id TEXT REFERENCES access_decision(id),
  device_id TEXT NOT NULL, raw_event_id TEXT NOT NULL REFERENCES raw_event(id),
  direction TEXT NOT NULL, confirmed_at TEXT NOT NULL);

CREATE TABLE exception_case (
  id TEXT PRIMARY KEY, kind TEXT NOT NULL,     -- CARD_JAM_SUSPECTED, ORPHAN_COLLECTION, ...
  device_id TEXT, gate_id TEXT, decision_id TEXT, detail_json TEXT,
  opened_at TEXT NOT NULL, resolved_at TEXT, resolved_by TEXT, resolution TEXT);
```

A separação `access_decision` / `physical_passage` é o que torna possível relatar
"autorizados sem confirmação" como coluna própria — e o que impede inflar a contagem de
público.

## 6. Outbox, inbox e sincronização

```sql
CREATE TABLE outbox (
  id TEXT PRIMARY KEY, aggregate_type TEXT NOT NULL, aggregate_id TEXT NOT NULL,
  payload_json TEXT NOT NULL,
  priority INTEGER NOT NULL,                   -- 0 = bloqueio emergencial ... 9 = histórico
  created_at TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0,
  next_attempt_at TEXT, last_error TEXT, sent_at TEXT,
  connector TEXT NOT NULL, idempotency_key TEXT NOT NULL);
CREATE INDEX ix_outbox_pending ON outbox(sent_at, priority, next_attempt_at);

CREATE TABLE inbox (
  idempotency_key TEXT PRIMARY KEY, connector TEXT NOT NULL,
  payload_json TEXT NOT NULL, received_at TEXT NOT NULL, processed_at TEXT, error TEXT);

CREATE TABLE dead_letter (
  id TEXT PRIMARY KEY, outbox_id TEXT, connector TEXT NOT NULL, payload_json TEXT NOT NULL,
  error TEXT NOT NULL, failed_at TEXT NOT NULL, reprocessed_at TEXT, reprocessed_by TEXT);

CREATE TABLE sync_cursor (
  connector TEXT NOT NULL, stream TEXT NOT NULL, cursor TEXT NOT NULL, updated_at TEXT NOT NULL,
  PRIMARY KEY (connector, stream));
```

**A regra que sustenta CA-02:** `raw_event` + `access_decision` + `device_command` +
`outbox` são gravados **na mesma transação**. Ou tudo, ou nada.

## 7. Acesso, auditoria e aprovação em duas pessoas

```sql
CREATE TABLE user_account (
  id TEXT PRIMARY KEY, login TEXT NOT NULL UNIQUE, password_hash TEXT NOT NULL,
  mfa_secret_enc TEXT, status TEXT NOT NULL, created_at TEXT NOT NULL);

CREATE TABLE role_assignment (
  user_id TEXT NOT NULL REFERENCES user_account(id), role TEXT NOT NULL,
  scope_type TEXT, scope_id TEXT, PRIMARY KEY (user_id, role, scope_type, scope_id));
-- superadmin | administrador | configurador | supervisor | operador
-- credenciamento | portaria | auditor | somente_leitura

CREATE TABLE approval_request (                -- ações perigosas exigem duas pessoas
  id TEXT PRIMARY KEY, action TEXT NOT NULL,   -- 'liberar_todos'|'apagar_listas'|'restaurar_fabrica'
                                               -- |'mudanca_massiva_regra'|'exportacao_biometrica'
  payload_json TEXT NOT NULL, requested_by TEXT NOT NULL, requested_at TEXT NOT NULL,
  approved_by TEXT, approved_at TEXT, executed_at TEXT, status TEXT NOT NULL,
  CHECK (approved_by IS NULL OR approved_by <> requested_by));

CREATE TABLE audit_log (                       -- imutável: só INSERT, encadeado por hash
  seq INTEGER PRIMARY KEY AUTOINCREMENT,
  at TEXT NOT NULL, actor TEXT, action TEXT NOT NULL,
  target_type TEXT, target_id TEXT, detail_json TEXT,
  prev_hash TEXT NOT NULL, hash TEXT NOT NULL);
```

O `CHECK (approved_by <> requested_by)` põe a regra das duas pessoas **no banco**, não só
na tela — porque é exatamente o tipo de regra que alguém contorna chamando o caso de uso
por outro caminho.

`audit_log` encadeado por hash: adulterar um registro antigo quebra a cadeia e a
verificação de integridade acusa. Gatilhos impedem `UPDATE` e `DELETE`.

## 8. Biometria (segregada)

Base separada, chave distinta, permissão específica, exportação sob aprovação dupla:

```sql
-- arquivo/banco próprio: biometrics.db
CREATE TABLE biometric_template (
  id TEXT PRIMARY KEY, person_id TEXT NOT NULL, kind TEXT NOT NULL,  -- 'digital'|'face'
  template_enc BLOB NOT NULL, quality INTEGER, enrolled_at TEXT NOT NULL,
  retention_until TEXT NOT NULL, purged_at TEXT);
```

Nunca no mesmo arquivo do banco operacional; nunca em backup não cifrado; nunca em log.
