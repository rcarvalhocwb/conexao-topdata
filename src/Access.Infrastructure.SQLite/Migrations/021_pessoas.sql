-- Migração 021: cadastro local de pessoas e credenciais (docs/43, ADR-0026).
-- Pessoas são só locais: nada aqui sobe para a nuvem. Dados pessoais vão cifrados por campo
-- (AES-256-GCM, chave no cofre do serviço), em colunas *_enc; a decisão da catraca não precisa
-- deles e usa só situação, validade, portões, horários e limite diário, que ficam em claro.

CREATE TABLE company (
    id          TEXT NOT NULL PRIMARY KEY,
    name        TEXT NOT NULL CHECK (length(name) BETWEEN 1 AND 120),
    cnpj        TEXT NULL,                       -- dado de empresa, não pessoal
    status      TEXT NOT NULL DEFAULT 'ativa' CHECK (status IN ('ativa', 'inativa')),
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL
) STRICT;

CREATE TABLE place (
    id          TEXT NOT NULL PRIMARY KEY,
    company_id  TEXT NULL REFERENCES company (id),
    name        TEXT NOT NULL CHECK (length(name) BETWEEN 1 AND 80),   -- sala, conjunto, unidade
    floor       TEXT NULL,
    block       TEXT NULL,
    status      TEXT NOT NULL DEFAULT 'ativo' CHECK (status IN ('ativo', 'inativo')),
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL
) STRICT;

-- Tabelas de horário: até 100 (as 100 tabelas do Inner, para a lista off-line no futuro).
CREATE TABLE time_schedule (
    id          INTEGER NOT NULL PRIMARY KEY CHECK (id BETWEEN 1 AND 100),
    name        TEXT    NOT NULL UNIQUE COLLATE NOCASE CHECK (length(name) BETWEEN 2 AND 60),
    created_at  TEXT    NOT NULL,
    updated_at  TEXT    NOT NULL
) STRICT;

-- Faixas por dia: 0 = domingo … 6 = sábado, 7 = feriado. Minutos desde a meia-noite, horário de Brasília.
CREATE TABLE time_schedule_slot (
    schedule_id INTEGER NOT NULL REFERENCES time_schedule (id) ON DELETE CASCADE,
    weekday     INTEGER NOT NULL CHECK (weekday BETWEEN 0 AND 7),
    start_min   INTEGER NOT NULL CHECK (start_min BETWEEN 0 AND 1439),
    end_min     INTEGER NOT NULL CHECK (end_min BETWEEN 1 AND 1440),
    CHECK (end_min > start_min),
    PRIMARY KEY (schedule_id, weekday, start_min)
) STRICT;

CREATE TABLE holiday (
    day   TEXT NOT NULL PRIMARY KEY CHECK (length(day) = 10),   -- AAAA-MM-DD
    name  TEXT NOT NULL
) STRICT;

CREATE TABLE person_profile (
    id                  TEXT    NOT NULL PRIMARY KEY,
    name                TEXT    NOT NULL UNIQUE COLLATE NOCASE CHECK (length(name) BETWEEN 2 AND 40),
    builtin             INTEGER NOT NULL DEFAULT 0 CHECK (builtin IN (0, 1)),
    required_fields     TEXT    NOT NULL DEFAULT '[]' CHECK (json_valid(required_fields)),  -- nomes de campo obrigatórios
    requires_host       INTEGER NOT NULL DEFAULT 0 CHECK (requires_host IN (0, 1)),
    default_days        INTEGER NULL CHECK (default_days IS NULL OR default_days BETWEEN 0 AND 3650), -- validade padrão
    schedule_id         INTEGER NULL REFERENCES time_schedule (id),
    daily_limit         INTEGER NULL CHECK (daily_limit IS NULL OR daily_limit BETWEEN 1 AND 100),
    retention_days      INTEGER NOT NULL DEFAULT 180 CHECK (retention_days BETWEEN 1 AND 3650),
    status              TEXT    NOT NULL DEFAULT 'ativo' CHECK (status IN ('ativo', 'inativo'))
) STRICT;

-- Catracas permitidas por perfil e por pessoa (número do Inner). Sem linha: todas.
CREATE TABLE person_profile_gate (
    profile_id  TEXT    NOT NULL REFERENCES person_profile (id) ON DELETE CASCADE,
    inner_number INTEGER NOT NULL CHECK (inner_number BETWEEN 1 AND 99),
    PRIMARY KEY (profile_id, inner_number)
) STRICT;

CREATE TABLE person (
    id                  TEXT    NOT NULL PRIMARY KEY,
    profile_id          TEXT    NOT NULL REFERENCES person_profile (id),
    company_id          TEXT    NULL REFERENCES company (id),
    place_id            TEXT    NULL REFERENCES place (id),
    host_person_id      TEXT    NULL REFERENCES person (id),          -- visitante: quem recebe
    status              TEXT    NOT NULL DEFAULT 'ativo' CHECK (status IN ('ativo', 'bloqueado', 'inativo')),
    status_reason       TEXT    NULL,
    valid_from          TEXT    NULL,
    valid_to            TEXT    NULL,
    schedule_id         INTEGER NULL REFERENCES time_schedule (id),   -- sobrepõe o do perfil
    daily_limit         INTEGER NULL CHECK (daily_limit IS NULL OR daily_limit BETWEEN 1 AND 100),
    priority_service    INTEGER NOT NULL DEFAULT 0 CHECK (priority_service IN (0, 1)),  -- sem diagnóstico
    full_name_enc       BLOB    NOT NULL,
    social_name_enc     BLOB    NULL,
    document_type       TEXT    NULL CHECK (document_type IS NULL OR document_type IN ('cpf', 'rg', 'cnh', 'passaporte', 'rne', 'outro')),
    document_enc        BLOB    NULL,
    document_hash       TEXT    NULL,                                 -- HMAC, para achar duplicado sem guardar em claro
    birth_date_enc      BLOB    NULL,
    phone_enc           BLOB    NULL,
    email_enc           BLOB    NULL,
    vehicle_enc         BLOB    NULL,
    guardian_enc        BLOB    NULL,                                 -- responsável legal (menor)
    department          TEXT    NULL,                                 -- departamento, cargo, matrícula: dado funcional
    role_title          TEXT    NULL,
    registration        TEXT    NULL,
    note                TEXT    NULL CHECK (note IS NULL OR length(note) <= 300),
    name_search         TEXT    NOT NULL DEFAULT '',                  -- HMAC das partes do nome, separadas por espaço
    retention_until     TEXT    NULL,
    created_at          TEXT    NOT NULL,
    created_by          TEXT    NULL,
    updated_at          TEXT    NOT NULL,
    updated_by          TEXT    NULL
) STRICT;

CREATE INDEX ix_person_company ON person (company_id);
CREATE INDEX ix_person_profile ON person (profile_id);
CREATE UNIQUE INDEX ux_person_document ON person (document_hash) WHERE document_hash IS NOT NULL;

CREATE TABLE person_gate (
    person_id   TEXT    NOT NULL REFERENCES person (id) ON DELETE CASCADE,
    inner_number INTEGER NOT NULL CHECK (inner_number BETWEEN 1 AND 99),
    PRIMARY KEY (person_id, inner_number)
) STRICT;

-- Credencial de pessoa: cartão/crachá, QR ou senha de teclado. O código fica em claro, como o dos
-- ingressos (a base é protegida pela permissão da pasta, ADR-0014): a catraca decide por ele.
CREATE TABLE person_credential (
    id                  TEXT NOT NULL PRIMARY KEY,
    person_id           TEXT NOT NULL REFERENCES person (id) ON DELETE CASCADE,
    kind                TEXT NOT NULL CHECK (kind IN ('cartao', 'qr', 'senha')),
    value_normalized    TEXT NOT NULL UNIQUE,
    status              TEXT NOT NULL DEFAULT 'ativa' CHECK (status IN ('ativa', 'bloqueada', 'perdida', 'devolvida')),
    status_reason       TEXT NULL,
    valid_from          TEXT NULL,
    valid_to            TEXT NULL,
    created_at          TEXT NOT NULL,
    updated_at          TEXT NOT NULL
) STRICT;

CREATE INDEX ix_person_credential_person ON person_credential (person_id);

-- Trilha só-INSERT das mudanças no cadastro de pessoas. Sem dado pessoal: só ids, ação e campos não pessoais.
CREATE TABLE person_event (
    seq         INTEGER PRIMARY KEY AUTOINCREMENT,
    at          TEXT NOT NULL,
    actor_id    TEXT NULL,
    person_id   TEXT NULL,
    action      TEXT NOT NULL,
    detail      TEXT NULL
) STRICT;

CREATE TRIGGER person_event_nao_muda BEFORE UPDATE ON person_event
BEGIN
    SELECT RAISE(ABORT, 'person_event é auditoria: não muda');
END;

CREATE TRIGGER person_event_nao_se_apaga BEFORE DELETE ON person_event
BEGIN
    SELECT RAISE(ABORT, 'person_event é auditoria: não se apaga');
END;

-- Catraca fechada pelo operador: ninguém passa (motivo CatracaFechada), sem depender de função da DLL.
CREATE TABLE gate_closure (
    inner_number INTEGER NOT NULL PRIMARY KEY CHECK (inner_number BETWEEN 1 AND 99),
    closed_at   TEXT    NOT NULL,
    closed_by   TEXT    NOT NULL,
    reason      TEXT    NOT NULL
) STRICT;

-- As passagens de pessoas entram na mesma tabela das tentativas (painel, acessos, "Por quê?"), com a
-- pessoa e sem ingresso. Nunca vão para a nuvem.
ALTER TABLE ticket_use_attempt ADD COLUMN person_id TEXT NULL;
CREATE INDEX ix_tentativa_pessoa ON ticket_use_attempt (person_id, at) WHERE person_id IS NOT NULL;

-- Perfis prontos (docs/43 §5.2). Os campos obrigatórios usam os nomes do formulário.
INSERT INTO person_profile (id, name, builtin, required_fields, requires_host, default_days, retention_days) VALUES
    ('colaborador', 'Colaborador', 1, '["nome","empresa"]', 0, NULL, 180),
    ('prestador',   'Prestador',   1, '["nome","empresa","documento"]', 0, 30, 180),
    ('visitante',   'Visitante',   1, '["nome","documento","anfitriao"]', 1, 0, 90),
    ('morador',     'Morador',     1, '["nome","sala"]', 0, NULL, 180),
    ('aluno',       'Aluno',       1, '["nome"]', 0, 30, 180),
    ('staff',       'Staff',       1, '["nome"]', 0, 3, 90),
    ('imprensa',    'Imprensa',    1, '["nome","documento","empresa"]', 0, 3, 90),
    ('fornecedor',  'Fornecedor',  1, '["nome","empresa"]', 0, 3, 90),
    ('artista',     'Artista',     1, '["nome"]', 0, 3, 90);
