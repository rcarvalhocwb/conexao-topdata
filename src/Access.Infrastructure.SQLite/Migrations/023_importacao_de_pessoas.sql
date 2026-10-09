-- Migração 023: importação de pessoas por planilha, com desfazer (docs/43 P5).
-- O lote guarda só ids e contagens, nunca dado pessoal; as pessoas criadas apontam para o lote, e
-- desfazer apaga as do lote (as passagens delas ficam, sem a pessoa: anônimas).

CREATE TABLE person_import (
    id                  TEXT    NOT NULL PRIMARY KEY,
    at                  TEXT    NOT NULL,
    by                  TEXT    NULL,
    file_sha256         TEXT    NOT NULL,
    people              INTEGER NOT NULL,
    credentials         INTEGER NOT NULL,
    created_companies   TEXT    NOT NULL DEFAULT '[]' CHECK (json_valid(created_companies)),
    created_places      TEXT    NOT NULL DEFAULT '[]' CHECK (json_valid(created_places)),
    undone_at           TEXT    NULL,
    undone_by           TEXT    NULL
) STRICT;

ALTER TABLE person ADD COLUMN import_id TEXT NULL REFERENCES person_import (id);
CREATE INDEX ix_person_import ON person (import_id) WHERE import_id IS NOT NULL;

INSERT OR IGNORE INTO app_role_permission (role_id, permission) VALUES
    ('administrador', 'pessoas.importar'),
    ('supervisor', 'pessoas.importar');
