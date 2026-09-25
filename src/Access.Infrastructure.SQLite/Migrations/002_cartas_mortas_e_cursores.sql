-- Migração 002 — o que faltava para a outbox ser drenável.
-- Ver docs/15-integracao-e-sincronizacao.md e docs/05-modelo-de-dados.md, seção 6.

-- Item que o destino recusou, ou que esgotou as tentativas.
-- NÃO é lixo: é um problema de integração que alguém precisa ver. Guarda o payload
-- inteiro para que o reprocessamento manual não dependa de reconstituir nada.
CREATE TABLE dead_letter (
    id              TEXT    NOT NULL PRIMARY KEY,
    outbox_id       TEXT    NOT NULL,
    connector       TEXT    NOT NULL,
    aggregate_type  TEXT    NOT NULL,
    aggregate_id    TEXT    NOT NULL,
    payload_json    TEXT    NOT NULL,
    priority        INTEGER NOT NULL,
    idempotency_key TEXT    NOT NULL,
    attempts        INTEGER NOT NULL,
    error           TEXT    NOT NULL,
    created_at      TEXT    NOT NULL,
    failed_at       TEXT    NOT NULL,
    reprocessed_at  TEXT    NULL,
    reprocessed_by  TEXT    NULL
) STRICT;

CREATE INDEX ix_dead_letter_pendente ON dead_letter (connector, failed_at) WHERE reprocessed_at IS NULL;

-- Ponto de retomada da sincronização de entrada (o que o sistema externo manda PARA cá:
-- revogações, novas credenciais, mudanças de regra). Por conector e por fluxo, para que
-- a queda de um fluxo não faça outro reprocessar tudo.
CREATE TABLE sync_cursor (
    connector  TEXT NOT NULL,
    stream     TEXT NOT NULL,
    cursor     TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY (connector, stream)
) STRICT;

-- A drenagem consulta por conector; o índice de 001 ordenava a fila inteira junto.
CREATE INDEX ix_outbox_por_conector ON outbox (connector, priority, created_at) WHERE sent_at IS NULL;
