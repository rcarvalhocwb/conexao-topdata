-- Migração 003 — múltiplos provedores de ingresso num mesmo evento.
-- Ver docs/16-multiplos-provedores-de-ingresso.md

-- Uma bilheteria, um site, um sistema de vendas.
CREATE TABLE ticket_provider (
    id                    TEXT    NOT NULL PRIMARY KEY,
    name                  TEXT    NOT NULL,
    normalization_profile TEXT    NOT NULL,   -- cada provedor emite o QR do seu jeito
    connector             TEXT    NOT NULL,   -- para onde volta o aviso de uso
    enabled               INTEGER NOT NULL DEFAULT 1,
    created_at            TEXT    NOT NULL
) STRICT;

CREATE TABLE ticket (
    id             TEXT    NOT NULL PRIMARY KEY,   -- UUIDv7 local
    provider_id    TEXT    NOT NULL REFERENCES ticket_provider (id),
    external_ref   TEXT    NOT NULL,               -- id NO provedor: é por ele que se presta contas
    qr_raw         TEXT    NOT NULL,               -- como o provedor mandou, preservado
    qr_normalized  TEXT    NOT NULL,               -- o que o leitor casa
    sector         TEXT    NULL,
    valid_from     TEXT    NULL,
    valid_to       TEXT    NULL,
    max_uses       INTEGER NOT NULL DEFAULT 1,
    used_count     INTEGER NOT NULL DEFAULT 0,
    status         TEXT    NOT NULL,               -- valido | consumido | cancelado | bloqueado
    ingested_at    TEXT    NOT NULL,
    first_used_at  TEXT    NULL,
    last_used_at   TEXT    NULL,
    last_gate_id   TEXT    NULL,
    reported_at    TEXT    NULL,                   -- quando o PROVEDOR confirmou o aviso de uso
    CHECK (max_uses  >= 1),
    CHECK (used_count >= 0),
    CHECK (used_count <= max_uses),
    CHECK (status IN ('valido', 'consumido', 'cancelado', 'bloqueado'))
) STRICT;

-- Duas chaves, dois propósitos diferentes.

-- 1. O provedor é dono do seu identificador: reenviar o mesmo ingresso é atualização,
--    não duplicata. É o que torna a ingestão repetível sem medo.
CREATE UNIQUE INDEX ux_ticket_provedor_ref ON ticket (provider_id, external_ref);

-- 2. A TRAVA CENTRAL desta migração. A catraca lê uma string e nada mais: se dois
--    provedores emitirem o mesmo QR, o portão não tem como saber de quem é. A colisão
--    precisa estourar na INGESTÃO, com hora e culpado, e não na roleta com uma fila de
--    duzentas pessoas atrás. Este índice é o que garante isso.
CREATE UNIQUE INDEX ux_ticket_qr ON ticket (qr_normalized);

CREATE INDEX ix_ticket_provedor_status ON ticket (provider_id, status);
CREATE INDEX ix_ticket_uso_pendente_de_aviso ON ticket (provider_id) WHERE used_count > 0 AND reported_at IS NULL;

-- TODA tentativa vira linha, inclusive a negada e inclusive o QR desconhecido.
-- Sem isto não existe prestação de contas: só se sabe quem entrou, nunca quem tentou.
CREATE TABLE ticket_use_attempt (
    id                   TEXT NOT NULL PRIMARY KEY,
    ticket_id            TEXT NULL REFERENCES ticket (id),  -- NULL = QR desconhecido
    provider_id          TEXT NULL,
    qr_normalized        TEXT NOT NULL,
    gate_id              TEXT NOT NULL,
    device_id            TEXT NOT NULL,
    outcome              TEXT NOT NULL,   -- consumido | negado
    reason               TEXT NOT NULL,   -- MotivoDoUso
    decision_id          TEXT NULL REFERENCES access_decision (id),
    at                   TEXT NOT NULL,
    passage_confirmed_at TEXT NULL        -- preenchido só com prova de giro (origem 6)
) STRICT;

CREATE INDEX ix_tentativa_provedor ON ticket_use_attempt (provider_id, at);
CREATE INDEX ix_tentativa_motivo ON ticket_use_attempt (reason);
CREATE INDEX ix_tentativa_desconhecida ON ticket_use_attempt (at) WHERE ticket_id IS NULL;
