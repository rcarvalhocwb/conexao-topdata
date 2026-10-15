-- Migração 026 — cadastro de cartões por leitura: a sessão de lote aberta pelo operador.
--
-- Uma migração nova, e não uma alteração de migração publicada. A 024 (visitantes) e a 025 (LGPD)
-- estão reservadas no issue do time; o migrador aplica por nome e não exige sequência.
--
-- O operador abre uma sessão com um tipo de entrada (INTEIRA, MEIA…), um nome de lote e os usos de
-- cada cartão. Enquanto ela está aberta, um cartão DESCONHECIDO lido na urna (leitor 2) entra no lote
-- com esse tipo e NÃO libera ninguém. Só uma sessão pode estar aberta por vez: o índice único sobre
-- 'aberta' garante isso mesmo se duas telas pedirem ao mesmo tempo.

CREATE TABLE enrollment_session (
    id           TEXT    NOT NULL PRIMARY KEY,
    provider_id  TEXT    NOT NULL REFERENCES ticket_provider (id),
    category     TEXT    NOT NULL
        CHECK (category NOT GLOB '*[^A-Z0-9_]*' AND length(category) BETWEEN 2 AND 20),
    batch_label  TEXT    NOT NULL CHECK (length(trim(batch_label)) BETWEEN 1 AND 40),
    max_uses     INTEGER NOT NULL CHECK (max_uses BETWEEN 1 AND 50),
    operator     TEXT    NOT NULL CHECK (length(trim(operator)) BETWEEN 2 AND 80),
    opened_at    TEXT    NOT NULL,
    closed_at    TEXT    NULL,
    registered   INTEGER NOT NULL DEFAULT 0 CHECK (registered >= 0),
    aberta       INTEGER NOT NULL DEFAULT 1 CHECK (aberta IN (0, 1)),
    CHECK ((aberta = 1 AND closed_at IS NULL) OR (aberta = 0 AND closed_at IS NOT NULL))
) STRICT;

CREATE UNIQUE INDEX ux_enrollment_session_aberta ON enrollment_session (aberta) WHERE aberta = 1;

-- Papéis prontos: quem cadastra cartões na operação. A portaria não cadastra; o administrador escolhe depois.
INSERT OR IGNORE INTO app_role_permission (role_id, permission) VALUES
    ('administrador', 'cartoes.cadastrar'),
    ('supervisor',    'cartoes.cadastrar');
