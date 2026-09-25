-- Migração 006 — o que o serviço e o painel precisam enxergar da operação.
-- Ver docs/ADR/ADR-0024-worker-e-servico-pelo-banco-local.md.

-- A situação de cada catraca, gravada pelo worker que a atende. É o batimento: o serviço
-- sabe que o worker está vivo E trabalhando porque esta linha continua mudando.
CREATE TABLE device_status (
    device_id           TEXT    NOT NULL PRIMARY KEY,   -- inner-N
    inner_number        INTEGER NOT NULL,
    worker              TEXT    NOT NULL,
    state               TEXT    NOT NULL,               -- DeviceState
    online              INTEGER NOT NULL,               -- 1 = em operação
    firmware            TEXT    NULL,
    reconnect_attempts  INTEGER NOT NULL DEFAULT 0,
    last_event_at       TEXT    NULL,
    last_decision       TEXT    NULL,                   -- "liberado" | "negado"
    updated_at          TEXT    NOT NULL
) STRICT;

-- Configuração do evento que o operador muda pelo painel e o worker lê ao subir.
-- Chave e valor em texto: poucas linhas, lidas raramente, e sem migração a cada ajuste.
CREATE TABLE edge_setting (
    key         TEXT NOT NULL PRIMARY KEY,
    value       TEXT NOT NULL,
    updated_at  TEXT NOT NULL,
    updated_by  TEXT NULL
) STRICT;

-- O painel ao vivo lê as tentativas em ordem de chegada.
CREATE INDEX ix_tentativa_momento ON ticket_use_attempt (at);
