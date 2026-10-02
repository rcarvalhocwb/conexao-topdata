-- Migração T005 de telemetria.db — Etapa I.4 do docs/36 (desenho: docs/36-anexos/02 §5.2).
--
-- Tabela de alertas com ciclo de vida: Aberto → Ciente → Fechado. Sem código de ingresso,
-- máscara nem nome (invariante I6, §3.1; docs/36-anexos/02 §6).

-- Um alerta é identificado pela regra e catraca/portão. O ciclo é:
-- 1. aberto_em: quando a regra foi disparada
-- 2. atualizado_em: quando a evidência mudou
-- 3. ciente_por/ciente_em: quando um operador marcou como ciente (nome gravado)
-- 4. fechado_em: quando a regra deixou de estar ativa (0 leituras, sem pico, etc.)
--
-- Situação só anda para frente: Aberto → Ciente → Fechado. Nunca volta.
CREATE TABLE alert (
    id                 TEXT PRIMARY KEY,                                 -- UUIDv7
    regra              TEXT NOT NULL CHECK (regra IN ('leitor_calado', 'comunicacao', 'relogio', 'configuracao', 'desconhecidos')),
    session_id         TEXT NULL,                                        -- partida do serviço (016)
    inner_number       INTEGER NULL CHECK (inner_number IS NULL OR inner_number BETWEEN 1 AND 99),
    portao             TEXT NULL,                                        -- ao menos um dos dois preenchido
    nivel              TEXT NOT NULL CHECK (nivel IN ('normal', 'atencao', 'acao', 'sem_dados', 'aprendendo')),
    texto              TEXT NOT NULL,                                    -- ao operador, sem código
    conta              TEXT NOT NULL,                                    -- a conta que disparou, legível (JSON)

    aberto_em          TEXT NOT NULL,                                    -- ISO-8601 UTC
    atualizado_em      TEXT NOT NULL,                                    -- última mudança de evidência
    fechado_em         TEXT NULL,                                        -- NULL enquanto aberto

    ciente_por         TEXT NULL,                                        -- nome digitado do operador
    ciente_em          TEXT NULL,                                        -- ISO-8601 UTC

    versao_dos_parametros TEXT NOT NULL,                                 -- hash para reprodutibilidade (I5)
    simulacao          INTEGER NOT NULL CHECK (simulacao IN (0, 1)),     -- veio de catraca simulada

    elegivel_a_rele    INTEGER NOT NULL CHECK (elegivel_a_rele IN (0, 1)), -- poderia acionar relé 2 no futuro

    CONSTRAINT regra_catraca_unica UNIQUE (regra, inner_number, portao) -- deduplica por (regra, catraca ou portão)
) STRICT;

CREATE INDEX ix_alerta_aberto ON alert (aberto_em);
CREATE INDEX ix_alerta_regra ON alert (regra, inner_number);
