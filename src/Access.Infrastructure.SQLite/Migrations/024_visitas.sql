-- P6: agenda local. Nome e motivo ficam cifrados; documento fica no cadastro de pessoas.
-- O agendamento não cria pessoa nem credencial. Ambas só são ligadas na chegada conferida.
CREATE TABLE visit (
    id                  TEXT NOT NULL PRIMARY KEY,
    host_person_id      TEXT NULL REFERENCES person (id) ON DELETE SET NULL,
    visitor_person_id   TEXT NULL REFERENCES person (id) ON DELETE SET NULL,
    visitor_name_enc    BLOB NOT NULL,
    reason_enc          BLOB NOT NULL,
    scheduled_from      TEXT NOT NULL,
    scheduled_to        TEXT NOT NULL CHECK (scheduled_to > scheduled_from),
    arrived_at          TEXT NULL,
    departed_at         TEXT NULL,
    credential_id       TEXT NULL REFERENCES person_credential (id) ON DELETE SET NULL,
    created_at          TEXT NOT NULL,
    created_by          TEXT NULL,
    arrived_by          TEXT NULL,
    departed_by         TEXT NULL,
    CHECK (departed_at IS NULL OR arrived_at IS NOT NULL)
) STRICT;

CREATE INDEX ix_visit_period ON visit (scheduled_from, id);
CREATE INDEX ix_visit_host ON visit (host_person_id);
CREATE INDEX ix_visit_person ON visit (visitor_person_id);

-- Uma credencial provisória pertence à visita atual, mesmo quando o cartão físico é reutilizado.
-- As visitas anteriores preservam o id para auditoria. A decisão lê só a janela e os estados.
ALTER TABLE person_credential ADD COLUMN visit_id TEXT NULL REFERENCES visit (id) ON DELETE SET NULL;
CREATE UNIQUE INDEX ux_person_credential_visit ON person_credential (visit_id) WHERE visit_id IS NOT NULL;

INSERT OR IGNORE INTO app_role_permission (role_id, permission)
SELECT r, p FROM
    (SELECT 'administrador' AS r UNION ALL SELECT 'supervisor' UNION ALL SELECT 'portaria'),
    (SELECT 'visitas.ver' AS p UNION ALL SELECT 'visitas.agendar'
     UNION ALL SELECT 'visitas.receber' UNION ALL SELECT 'visitas.encerrar');
