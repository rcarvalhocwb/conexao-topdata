-- Migração 019: liberação que falhou e estorno de uso (achado E1-05 do docs/41).
-- Decisão do dono do produto: quando a liberação falha depois da autorização, o ingresso continua
-- consumido, a tentativa guarda a causa, e o operador estorna pelo painel, com nome e motivo.
--
-- A tentativa já existia (consumida, sem prova de giro). O que faltava era a causa e o estorno.
ALTER TABLE ticket_use_attempt ADD COLUMN release_failed_at TEXT NULL;
ALTER TABLE ticket_use_attempt ADD COLUMN release_failure TEXT NULL;

-- Estornar troca a tentativa de 'consumido' para 'estornado' (os relatórios contam só 'consumido')
-- e devolve o uso ao ingresso. Esta tabela é a auditoria do estorno: nada se apaga nem muda.
CREATE TABLE ticket_use_refund (
    attempt_id   TEXT NOT NULL PRIMARY KEY REFERENCES ticket_use_attempt (id),
    ticket_id    TEXT NOT NULL REFERENCES ticket (id),
    refunded_at  TEXT NOT NULL,
    refunded_by  TEXT NOT NULL,   -- nome digitado; não há login (docs/27 §11)
    reason       TEXT NOT NULL
) STRICT;

CREATE INDEX ix_tentativa_sem_passagem ON ticket_use_attempt (at)
    WHERE outcome = 'consumido' AND passage_confirmed_at IS NULL;

CREATE TRIGGER ticket_use_refund_nao_se_apaga
BEFORE DELETE ON ticket_use_refund
BEGIN
    SELECT RAISE(ABORT, 'ticket_use_refund é auditoria: não se apaga');
END;

CREATE TRIGGER ticket_use_refund_nao_muda
BEFORE UPDATE ON ticket_use_refund
BEGIN
    SELECT RAISE(ABORT, 'ticket_use_refund é auditoria: não muda');
END;
