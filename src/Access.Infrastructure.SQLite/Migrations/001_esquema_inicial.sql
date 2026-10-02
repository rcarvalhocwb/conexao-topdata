-- Migração 001 — esquema inicial da Fase 1.
-- Ver docs/05-modelo-de-dados.md

-- Eventos brutos. NADA é descartado: origem desconhecida é evento válido.
-- Ver docs/ADR/ADR-0018-eventos-desconhecidos.md
CREATE TABLE raw_event (
    id              TEXT    NOT NULL PRIMARY KEY,   -- UUIDv7
    device_id       TEXT    NOT NULL,
    boot_id         TEXT    NOT NULL,
    device_seq      INTEGER NOT NULL,
    origin_raw      INTEGER NOT NULL,
    origin_known    TEXT    NULL,
    payload         BLOB    NOT NULL,
    device_time     TEXT    NULL,
    received_time   TEXT    NOT NULL,
    server_time     TEXT    NULL,
    correlation_id  TEXT    NOT NULL
) STRICT;

-- A chave que torna reenvio e reconciliação idempotentes.
-- Ver docs/ADR/ADR-0009-identidade-de-evento.md
CREATE UNIQUE INDEX ux_raw_event_dedupe ON raw_event (device_id, boot_id, device_seq);
CREATE INDEX ix_raw_event_recebido ON raw_event (received_time);
CREATE INDEX ix_raw_event_origem_desconhecida ON raw_event (origin_raw) WHERE origin_known IS NULL;

CREATE TABLE access_decision (
    id               TEXT    NOT NULL PRIMARY KEY,
    raw_event_id     TEXT    NULL REFERENCES raw_event (id),
    device_id        TEXT    NOT NULL,
    gate_id          TEXT    NULL,
    credential_value TEXT    NULL,   -- valor normalizado; nunca vai para log
    outcome          TEXT    NOT NULL,
    reason_code      TEXT    NOT NULL,
    rule_trace_json  TEXT    NULL,
    elapsed_ms       INTEGER NOT NULL,
    degradation_tier TEXT    NOT NULL,
    decided_at       TEXT    NOT NULL
) STRICT;

CREATE INDEX ix_decision_dispositivo ON access_decision (device_id, decided_at);
CREATE INDEX ix_decision_motivo ON access_decision (reason_code);

CREATE TABLE device_command (
    id              TEXT    NOT NULL PRIMARY KEY,
    device_id       TEXT    NOT NULL,
    decision_id     TEXT    NULL REFERENCES access_decision (id),
    command         TEXT    NOT NULL,
    params_json     TEXT    NULL,
    issued_at       TEXT    NOT NULL,
    native_return   TEXT    NULL,
    completed_at    TEXT    NULL,
    idempotency_key TEXT    NOT NULL
) STRICT;

CREATE UNIQUE INDEX ux_command_idempotencia ON device_command (idempotency_key);

-- Passagem física SÓ com prova (origem 6). Tabela separada de propósito.
-- Ver docs/ADR/ADR-0007-autorizacao-versus-passagem.md
CREATE TABLE physical_passage (
    id           TEXT NOT NULL PRIMARY KEY,
    decision_id  TEXT NULL REFERENCES access_decision (id),
    device_id    TEXT NOT NULL,
    raw_event_id TEXT NOT NULL REFERENCES raw_event (id),
    direction    TEXT NOT NULL,
    confirmed_at TEXT NOT NULL
) STRICT;

CREATE INDEX ix_passagem_dispositivo ON physical_passage (device_id, confirmed_at);

-- Outbox transacional: gravada na MESMA transação do evento e da decisão.
-- Ver docs/ADR/ADR-0003.
CREATE TABLE outbox (
    id              TEXT    NOT NULL PRIMARY KEY,
    aggregate_type  TEXT    NOT NULL,
    aggregate_id    TEXT    NOT NULL,
    payload_json    TEXT    NOT NULL,
    priority        INTEGER NOT NULL,   -- 0 = bloqueio emergencial ... 9 = histórico
    connector       TEXT    NOT NULL,
    idempotency_key TEXT    NOT NULL,
    created_at      TEXT    NOT NULL,
    attempts        INTEGER NOT NULL DEFAULT 0,
    next_attempt_at TEXT    NULL,
    last_error      TEXT    NULL,
    sent_at         TEXT    NULL
) STRICT;

CREATE UNIQUE INDEX ux_outbox_idempotencia ON outbox (connector, idempotency_key);
CREATE INDEX ix_outbox_pendente ON outbox (priority, created_at) WHERE sent_at IS NULL;

CREATE TABLE inbox (
    idempotency_key TEXT NOT NULL PRIMARY KEY,
    connector       TEXT NOT NULL,
    payload_json    TEXT NOT NULL,
    received_at     TEXT NOT NULL,
    processed_at    TEXT NULL,
    error           TEXT NULL
) STRICT;

-- Auditoria imutável, encadeada por hash. Gatilhos impedem UPDATE e DELETE.
CREATE TABLE audit_log (
    seq         INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    at          TEXT    NOT NULL,
    actor       TEXT    NULL,
    action      TEXT    NOT NULL,
    target_type TEXT    NULL,
    target_id   TEXT    NULL,
    detail_json TEXT    NULL,
    prev_hash   TEXT    NOT NULL,
    hash        TEXT    NOT NULL
);

CREATE TRIGGER trg_audit_log_sem_update
BEFORE UPDATE ON audit_log
BEGIN
    SELECT RAISE(ABORT, 'audit_log e imutavel: UPDATE proibido');
END;

CREATE TRIGGER trg_audit_log_sem_delete
BEFORE DELETE ON audit_log
BEGIN
    SELECT RAISE(ABORT, 'audit_log e imutavel: DELETE proibido');
END;

-- Transições da máquina de estados, para diagnóstico de campo.
CREATE TABLE device_state_transition (
    id             TEXT    NOT NULL PRIMARY KEY,
    device_id      TEXT    NOT NULL,
    state_from     TEXT    NOT NULL,
    trigger        TEXT    NOT NULL,
    state_to       TEXT    NOT NULL,
    at             TEXT    NOT NULL,
    correlation_id TEXT    NOT NULL,
    native_return  TEXT    NULL
) STRICT;

CREATE INDEX ix_transicao_dispositivo ON device_state_transition (device_id, at);
