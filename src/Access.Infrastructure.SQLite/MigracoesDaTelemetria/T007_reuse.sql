-- Migração T007 de telemetria.db — Etapa I.7 do docs/36 (desenho: docs/36-anexos/02 §5.4).
--
-- Tabela de detecção de reuso: quando a mesma credencial é usada em múltiplas catracas
-- num período curto (intervalo de reuso), sem a confirmação de giro anterior.
-- Sem código de ingresso em claro; usa a impressão HMAC (B.1, docs/34:577).

-- Um evento de reuso é:
-- 1. credential_hash: impressão HMAC da credencial (não o código)
-- 2. periodos: lista de tentativas em catracas/portões distintos dentro de T segundos
-- 3. padrao: "multiplas_catracas", "multiplos_portoes", "muito_rapido"
-- 4. confianca: Wilson score interval lower bound (estatística de acertos em amostras pequenas)
--
-- Só registra reuso detectado (não tentativas isoladas). O alerta está em T005.
CREATE TABLE reuse_detection (
    id                      TEXT PRIMARY KEY,                                 -- UUIDv7
    session_id              TEXT NULL,                                        -- partida do serviço (016)
    credential_hmac         TEXT NOT NULL,                                    -- impressão HMAC (256 hex)
    padrao                  TEXT NOT NULL CHECK (padrao IN ('multiplas_catracas', 'multiplos_portoes', 'muito_rapido', 'confirmacao_faltante')),

    -- Janela de reuso: intervalo de tempo em que as tentativas foram feitas
    janela_inicio           TEXT NOT NULL,                                    -- ISO-8601 UTC
    janela_fim              TEXT NOT NULL,                                    -- ISO-8601 UTC
    duracao_segundos        INTEGER NOT NULL CHECK (duracao_segundos > 0),

    -- Catracas/portões envolvidas (JSON array de {inner, portao, tentativa_em, resultado})
    tentativas              TEXT NOT NULL,                                    -- JSON array

    -- Estatística: quantidade de tentativas negadas vs passagens confirmadas
    negadas_count           INTEGER NOT NULL CHECK (negadas_count >= 0),
    confirmadas_count       INTEGER NOT NULL CHECK (confirmadas_count >= 0),

    -- Wilson score interval (confiança em %)
    confianca_percentual    INTEGER NOT NULL CHECK (confianca_percentual BETWEEN 0 AND 100),

    -- Severidade: baixa (1 reuso), média (2-5), alta (>5 ou padrão muito_rapido)
    severidade              TEXT NOT NULL CHECK (severidade IN ('baixa', 'media', 'alta')),

    gravado_em              TEXT NOT NULL,                                    -- ISO-8601 UTC
    simulacao               INTEGER NOT NULL CHECK (simulacao IN (0, 1)),     -- veio de catraca simulada

    CONSTRAINT reuso_hmac_janela UNIQUE (credential_hmac, janela_inicio)
) STRICT;

CREATE INDEX ix_reuse_hmac ON reuse_detection (credential_hmac);
CREATE INDEX ix_reuse_severidade ON reuse_detection (severidade, janela_inicio);
