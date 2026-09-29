-- Migração 009 — comandos do operador para cada catraca (fase 4b, docs/32).
-- O serviço grava o pedido; o worker da catraca pega, executa quando a catraca estiver
-- livre (Polling) e grava o desfecho. É também a auditoria: nada aqui se apaga, e o que
-- foi pedido não muda depois de gravado — só a situação e o resultado andam.
CREATE TABLE operator_command (
    id              TEXT    NOT NULL PRIMARY KEY,   -- UUID v7
    inner_number    INTEGER NOT NULL,
    kind            TEXT    NOT NULL,               -- TipoDeComando
    text            TEXT    NULL,                   -- mensagem temporária
    duration_s      INTEGER NOT NULL DEFAULT 0,
    reason          TEXT    NULL,                   -- motivo da liberação manual
    requested_by    TEXT    NOT NULL,               -- nome digitado; não há login (docs/27 §11)
    requested_at    TEXT    NOT NULL,
    expires_at      TEXT    NOT NULL,
    status          TEXT    NOT NULL DEFAULT 'pendente'
                    CHECK (status IN ('pendente', 'recebido', 'concluido', 'falhou', 'expirado')),
    taken_at        TEXT    NULL,
    finished_at     TEXT    NULL,
    result          TEXT    NULL
) STRICT;

CREATE INDEX ix_operator_command_pendente ON operator_command (status, inner_number, requested_at);
CREATE INDEX ix_operator_command_pedido ON operator_command (requested_at);

CREATE TRIGGER operator_command_nao_se_apaga
BEFORE DELETE ON operator_command
BEGIN
    SELECT RAISE(ABORT, 'operator_command é auditoria: não se apaga');
END;

CREATE TRIGGER operator_command_pedido_nao_muda
BEFORE UPDATE OF id, inner_number, kind, text, duration_s, reason, requested_by, requested_at, expires_at
ON operator_command
BEGIN
    SELECT RAISE(ABORT, 'operator_command: o pedido não muda depois de gravado');
END;

-- Situação final não volta atrás.
CREATE TRIGGER operator_command_final_e_final
BEFORE UPDATE OF status ON operator_command
WHEN OLD.status IN ('concluido', 'falhou', 'expirado')
BEGIN
    SELECT RAISE(ABORT, 'operator_command: situação final não muda');
END;
