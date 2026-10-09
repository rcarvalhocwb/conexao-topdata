-- Migração T006 de telemetria.db — Etapa I.6 do docs/36 (desenho: docs/36-anexos/02 §5.3).
--
-- Tabela de métricas de ritmo e fluência das catracas: chegadas por minuto e tempo até girar.
-- Sem código de ingresso, máscara nem nome (invariante I6, §3.1; docs/36-anexos/02 §6).

-- O ritmo é medido em janelas de 1 minuto. Para cada catraca, em cada minuto:
-- 1. arrivals_count: tentativas de leitura (chegadas de Poisson)
-- 2. completion_median_ms: mediana do tempo leitura → giro confirmado
-- 3. completion_p95_ms: p95 do mesmo tempo
-- 4. completion_samples: quantas tentativas viraram giros (para validar a estimativa)
-- 5. abandon_rate: taxa de liberado sem giro (pessoas que desistem)
--
-- "Ritmo" é o quociente: arrivals / ciclos_por_minuto (mediana). Perto de 100% = limite.
CREATE TABLE fluidity_window (
    id                      TEXT PRIMARY KEY,                                 -- UUIDv7
    session_id              TEXT NULL,                                        -- partida do serviço (016)
    inner_number            INTEGER NOT NULL CHECK (inner_number BETWEEN 1 AND 99),
    portao                  TEXT NULL,
    janela_inicio           TEXT NOT NULL,                                    -- ISO-8601 UTC, sempre HH:00 ou HH:MM:00
    janela_duracao_minutos  INTEGER NOT NULL CHECK (janela_duracao_minutos > 0),

    arrivals_count          INTEGER NOT NULL CHECK (arrivals_count >= 0),     -- tentativas lidas
    abandonment_rate        REAL NOT NULL CHECK (abandonment_rate BETWEEN 0 AND 1),
    completion_median_ms    INTEGER NOT NULL CHECK (completion_median_ms >= 0),
    completion_p95_ms       INTEGER NOT NULL CHECK (completion_p95_ms >= 0),
    completion_samples      INTEGER NOT NULL CHECK (completion_samples >= 0),

    -- Comparação com vizinhas na mesma janela
    arrivals_viz_min        INTEGER NOT NULL CHECK (arrivals_viz_min >= 0),
    arrivals_viz_max        INTEGER NOT NULL CHECK (arrivals_viz_max >= 0),
    completion_med_viz_min  INTEGER NOT NULL CHECK (completion_med_viz_min >= 0),
    completion_med_viz_max  INTEGER NOT NULL CHECK (completion_med_viz_max >= 0),

    -- Índice: baixa circulação, normal, perto do limite, pico
    nivel                   TEXT NOT NULL CHECK (nivel IN ('silencio', 'normal', 'pico_proximo', 'saturado', 'aprendendo')),

    -- Recomendação para o operador
    recomendacao            TEXT NULL,                                        -- "abrir portão", "redistribuir", "aguardar"

    gravado_em              TEXT NOT NULL,                                    -- ISO-8601 UTC
    simulacao               INTEGER NOT NULL CHECK (simulacao IN (0, 1)),     -- veio de catraca simulada

    CONSTRAINT ritmo_catraca_janela UNIQUE (inner_number, portao, janela_inicio)
) STRICT;

CREATE INDEX ix_fluidity_janela ON fluidity_window (janela_inicio, inner_number);
CREATE INDEX ix_fluidity_nivel ON fluidity_window (nivel, inner_number);
