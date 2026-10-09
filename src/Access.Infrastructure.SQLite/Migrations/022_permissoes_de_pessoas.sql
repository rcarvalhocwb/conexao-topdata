-- Migração 022: permissões do cadastro de pessoas (docs/43 P3, ADR-0026) nos papéis prontos.
-- O administrador escolhe depois o que cada papel tem; isto é só o ponto de partida.

INSERT OR IGNORE INTO app_role_permission (role_id, permission)
SELECT 'administrador', p FROM (
    SELECT 'pessoas.ver' AS p UNION ALL SELECT 'pessoas.ver_dados' UNION ALL SELECT 'pessoas.editar'
    UNION ALL SELECT 'pessoas.bloquear' UNION ALL SELECT 'cadastro.parametros' UNION ALL SELECT 'catraca.fechar');

INSERT OR IGNORE INTO app_role_permission (role_id, permission)
SELECT 'supervisor', p FROM (
    SELECT 'pessoas.ver' AS p UNION ALL SELECT 'pessoas.ver_dados' UNION ALL SELECT 'pessoas.editar'
    UNION ALL SELECT 'pessoas.bloquear' UNION ALL SELECT 'cadastro.parametros' UNION ALL SELECT 'catraca.fechar');

-- A portaria cadastra e bloqueia, mas vê documento e contato mascarados.
INSERT OR IGNORE INTO app_role_permission (role_id, permission)
SELECT 'portaria', p FROM (
    SELECT 'pessoas.ver' AS p UNION ALL SELECT 'pessoas.editar' UNION ALL SELECT 'pessoas.bloquear');
