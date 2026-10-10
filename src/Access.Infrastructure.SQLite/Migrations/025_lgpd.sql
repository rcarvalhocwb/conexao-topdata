-- P7 (docs/43): prazo desde a inativação, independente de edições posteriores.
ALTER TABLE person ADD COLUMN inactivated_at TEXT NULL;
UPDATE person SET inactivated_at = COALESCE(
    (SELECT e.at FROM person_event e WHERE e.person_id = person.id AND e.action = 'pessoa.inativo'
     AND e.seq > COALESCE((SELECT MAX(a.seq) FROM person_event a WHERE a.person_id = person.id
                          AND a.action IN ('pessoa.ativo', 'pessoa.bloqueado')), 0)
     ORDER BY e.seq LIMIT 1), updated_at)
WHERE status = 'inativo';
CREATE INDEX ix_person_inactive ON person (inactivated_at) WHERE status = 'inativo';
CREATE TRIGGER person_inactivated_insert AFTER INSERT ON person WHEN NEW.status = 'inativo'
BEGIN
    UPDATE person SET inactivated_at = NEW.updated_at WHERE id = NEW.id;
END;
CREATE TRIGGER person_inactivated_status AFTER UPDATE OF status ON person WHEN OLD.status <> NEW.status
BEGIN
    UPDATE person SET inactivated_at = CASE WHEN NEW.status = 'inativo' THEN NEW.updated_at ELSE NULL END,
                      retention_until = NULL WHERE id = NEW.id;
END;

-- O cliente fornece o texto: não há termo inventado nem aceite automático.
CREATE TABLE consent_term (
    version TEXT NOT NULL PRIMARY KEY CHECK (length(version) BETWEEN 1 AND 60),
    text TEXT NOT NULL CHECK (length(text) BETWEEN 1 AND 100000),
    sha256 TEXT NOT NULL,
    created_at TEXT NOT NULL,
    created_by TEXT NULL
) STRICT;
CREATE TRIGGER consent_term_no_update BEFORE UPDATE ON consent_term
BEGIN SELECT RAISE(ABORT, 'Versão do termo é imutável'); END;
CREATE TRIGGER consent_term_no_delete BEFORE DELETE ON consent_term
BEGIN SELECT RAISE(ABORT, 'Versão do termo é imutável'); END;
CREATE TABLE person_consent (
    person_id TEXT NOT NULL REFERENCES person (id) ON DELETE CASCADE,
    term_version TEXT NOT NULL REFERENCES consent_term (version),
    accepted_at TEXT NOT NULL,
    recorded_at TEXT NOT NULL,
    recorded_by TEXT NULL,
    PRIMARY KEY (person_id, term_version)
) STRICT;

-- Preserva horários, resultados e prova física, removendo códigos e vínculos pessoais.
-- Vale também para outras rotas que apagam person (como desfazer importação).
CREATE TRIGGER person_erase_links BEFORE DELETE ON person
BEGIN
    UPDATE person SET host_person_id = NULL WHERE host_person_id = OLD.id;
    UPDATE raw_event SET payload = X'' WHERE id IN (
        SELECT d.raw_event_id FROM access_decision d JOIN ticket_use_attempt a ON a.decision_id = d.id
        WHERE a.person_id = OLD.id
        UNION
        SELECT p.raw_event_id FROM physical_passage p JOIN ticket_use_attempt a ON a.decision_id = p.decision_id
        WHERE a.person_id = OLD.id);
    UPDATE access_decision SET credential_value = NULL, rule_trace_json = NULL
        WHERE id IN (SELECT decision_id FROM ticket_use_attempt WHERE person_id = OLD.id);
    UPDATE ticket_use_attempt SET person_id = NULL, qr_normalized = '' WHERE person_id = OLD.id;
END;

-- Auditoria segue só-INSERT, com uma única exceção: retirar dados de um titular já apagado.
-- A exceção não permite mudar ação, autoria, sequência ou instante, nem apagar eventos.
DROP TRIGGER person_event_nao_muda;
CREATE TRIGGER person_event_nao_muda BEFORE UPDATE ON person_event
WHEN NOT (OLD.person_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM person WHERE id = OLD.person_id)
          AND NEW.person_id IS NULL AND NEW.detail IS NULL AND NEW.seq = OLD.seq
          AND NEW.at = OLD.at AND NEW.actor_id IS OLD.actor_id AND NEW.action = OLD.action)
BEGIN SELECT RAISE(ABORT, 'person_event é auditoria: não muda'); END;
CREATE TRIGGER person_erase_audit AFTER DELETE ON person
BEGIN
    UPDATE person_event SET person_id = NULL, detail = NULL WHERE person_id = OLD.id;
END;

INSERT INTO app_role_permission (role_id, permission) VALUES ('administrador', 'pessoas.excluir');
