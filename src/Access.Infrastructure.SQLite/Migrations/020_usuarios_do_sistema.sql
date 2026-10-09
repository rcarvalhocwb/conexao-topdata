-- Migração 020: usuários do sistema, papéis e permissões (ADR-0026).
-- O administrador padrão não é criado aqui: a senha precisa de hash com sal, feito pelo serviço na
-- partida (UsuariosDoSistema.GarantirAdministradorPadrao), e só quando não há nenhum usuário.

CREATE TABLE app_user (
    id              TEXT    NOT NULL PRIMARY KEY,
    login           TEXT    NOT NULL UNIQUE COLLATE NOCASE CHECK (length(login) BETWEEN 3 AND 40),
    display_name    TEXT    NOT NULL CHECK (length(display_name) BETWEEN 2 AND 80),
    password_hash   BLOB    NOT NULL,
    password_salt   BLOB    NOT NULL,
    iterations      INTEGER NOT NULL CHECK (iterations >= 100000),
    must_change     INTEGER NOT NULL DEFAULT 1 CHECK (must_change IN (0, 1)),
    status          TEXT    NOT NULL DEFAULT 'ativo' CHECK (status IN ('ativo', 'inativo')),
    failed_count    INTEGER NOT NULL DEFAULT 0,
    locked_until    TEXT    NULL,
    last_login_at   TEXT    NULL,
    created_at      TEXT    NOT NULL,
    updated_at      TEXT    NOT NULL
) STRICT;

CREATE TABLE app_role (
    id          TEXT    NOT NULL PRIMARY KEY,
    name        TEXT    NOT NULL UNIQUE COLLATE NOCASE CHECK (length(name) BETWEEN 2 AND 40),
    description TEXT    NULL,
    builtin     INTEGER NOT NULL DEFAULT 0 CHECK (builtin IN (0, 1))
) STRICT;

CREATE TABLE app_role_permission (
    role_id     TEXT NOT NULL REFERENCES app_role (id) ON DELETE CASCADE,
    permission  TEXT NOT NULL,
    PRIMARY KEY (role_id, permission)
) STRICT;

CREATE TABLE app_user_role (
    user_id TEXT NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,
    role_id TEXT NOT NULL REFERENCES app_role (id),
    PRIMARY KEY (user_id, role_id)
) STRICT;

-- Trilha das ações sobre usuários e papéis: quem fez o quê, quando. Só INSERT. Nunca senha nem hash.
CREATE TABLE app_user_event (
    seq         INTEGER PRIMARY KEY AUTOINCREMENT,
    at          TEXT NOT NULL,
    actor_id    TEXT NULL,
    action      TEXT NOT NULL,
    target      TEXT NULL,
    detail      TEXT NULL
) STRICT;

CREATE TRIGGER app_user_event_nao_muda BEFORE UPDATE ON app_user_event
BEGIN
    SELECT RAISE(ABORT, 'app_user_event é auditoria: não muda');
END;

CREATE TRIGGER app_user_event_nao_se_apaga BEFORE DELETE ON app_user_event
BEGIN
    SELECT RAISE(ABORT, 'app_user_event é auditoria: não se apaga');
END;

-- O papel Administrador não pode ficar sem a gestão de usuários: sem isso, ninguém mais administra.
CREATE TRIGGER app_role_admin_mantem_gestao BEFORE DELETE ON app_role_permission
WHEN OLD.role_id = 'administrador' AND OLD.permission = 'usuarios.gerenciar'
BEGIN
    SELECT RAISE(ABORT, 'o papel Administrador sempre gerencia usuários');
END;

CREATE TRIGGER app_role_builtin_nao_se_apaga BEFORE DELETE ON app_role
WHEN OLD.builtin = 1
BEGIN
    SELECT RAISE(ABORT, 'papel do sistema não se apaga');
END;

INSERT INTO app_role (id, name, description, builtin) VALUES
    ('administrador', 'Administrador', 'Tudo, inclusive usuários e papéis', 1),
    ('supervisor', 'Supervisor', 'Opera, configura catracas e o evento, estorna; não gerencia usuários', 1),
    ('portaria', 'Portaria', 'Acompanha, consulta códigos e comanda catracas', 1),
    ('somente_leitura', 'Somente leitura', 'Só acompanha e vê relatórios', 1);

INSERT INTO app_role_permission (role_id, permission)
SELECT 'administrador', p FROM (
    SELECT 'operacao.ver' AS p UNION ALL SELECT 'codigos.consultar' UNION ALL SELECT 'relatorios.ver'
    UNION ALL SELECT 'diagnostico.ver' UNION ALL SELECT 'catraca.comandar' UNION ALL SELECT 'catraca.configurar'
    UNION ALL SELECT 'configuracao.editar' UNION ALL SELECT 'sincronizacao.operar' UNION ALL SELECT 'acessos.estornar'
    UNION ALL SELECT 'simulador.usar' UNION ALL SELECT 'usuarios.gerenciar');

INSERT INTO app_role_permission (role_id, permission)
SELECT 'supervisor', p FROM (
    SELECT 'operacao.ver' AS p UNION ALL SELECT 'codigos.consultar' UNION ALL SELECT 'relatorios.ver'
    UNION ALL SELECT 'diagnostico.ver' UNION ALL SELECT 'catraca.comandar' UNION ALL SELECT 'catraca.configurar'
    UNION ALL SELECT 'configuracao.editar' UNION ALL SELECT 'sincronizacao.operar' UNION ALL SELECT 'acessos.estornar'
    UNION ALL SELECT 'simulador.usar');

INSERT INTO app_role_permission (role_id, permission)
SELECT 'portaria', p FROM (
    SELECT 'operacao.ver' AS p UNION ALL SELECT 'codigos.consultar' UNION ALL SELECT 'catraca.comandar');

INSERT INTO app_role_permission (role_id, permission)
SELECT 'somente_leitura', p FROM (SELECT 'operacao.ver' AS p UNION ALL SELECT 'relatorios.ver');
