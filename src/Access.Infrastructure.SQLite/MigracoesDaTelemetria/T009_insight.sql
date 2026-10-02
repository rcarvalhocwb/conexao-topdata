-- Migração T009 de telemetria.db — Etapa I.10 do docs/36 (desenho: docs/36-anexos/02 §5.6).
--
-- Tabela de insights pós-evento (relatório R8). Um registro por encerramento de evento com os
-- achados determinísticos: maior pico, catraca lenta, desperdício, disponibilidade etc.
-- Sem código de ingresso, máscara nem nome (invariante I6, §3.1; docs/36-anexos/02 §6).

-- Um insight é gravado quando o evento encerra (fim de operação). Contém achados agregados
-- e determinísticos para o relatório de prestação de contas.
CREATE TABLE insight (
    id                 TEXT PRIMARY KEY,                                 -- UUIDv7
    session_id         TEXT NULL,                                        -- partida do serviço (016)
    encerrado_em       TEXT NOT NULL,                                    -- ISO-8601 UTC (fim do evento)

    -- Maior pico de 15 min por portão (IN-09)
    maior_pico_portao  TEXT NULL,                                        -- nome do portão
    maior_pico_leituras INTEGER NOT NULL CHECK (maior_pico_leituras >= 0),
    maior_pico_minuto  TEXT NULL,                                        -- minuto do pico ISO-8601

    -- Catraca mais lenta (mediana de Δ liberação→giro)
    catraca_mais_lenta_numero INTEGER NULL CHECK (catraca_mais_lenta_numero IS NULL OR catraca_mais_lenta_numero BETWEEN 1 AND 99),
    catraca_mais_lenta_delta_ms INTEGER NOT NULL CHECK (catraca_mais_lenta_delta_ms >= 0),

    -- Catraca mais ociosa (menor ρ)
    catraca_mais_ociosa_numero INTEGER NULL CHECK (catraca_mais_ociosa_numero IS NULL OR catraca_mais_ociosa_numero BETWEEN 1 AND 99),
    catraca_mais_ociosa_ocupacao REAL NOT NULL CHECK (catraca_mais_ociosa_ocupacao >= 0.0),

    -- Desperdício: Σ liberações sem giro × tempo do relé (em segundos)
    desperdicio_segundos REAL NOT NULL CHECK (desperdicio_segundos >= 0.0),

    -- Disponibilidade média por catraca (segundos em operação / segundos total)
    disponibilidade_media REAL NOT NULL CHECK (disponibilidade_media >= 0.0 AND disponibilidade_media <= 1.0),

    -- Total de alertas disparados e quantos foram marcados como ciente
    total_alertas INTEGER NOT NULL CHECK (total_alertas >= 0),
    alertas_ciencia INTEGER NOT NULL CHECK (alertas_ciencia >= 0),

    -- Dimensionamento para próximo evento (pico de chegadas / μ medido)
    dimensionamento_catracas REAL NOT NULL CHECK (dimensionamento_catracas >= 0.0),

    -- Hash dos parâmetros para reprodutibilidade (I5)
    versao_dos_parametros TEXT NOT NULL,

    -- Simulação ou operação real
    simulacao          INTEGER NOT NULL CHECK (simulacao IN (0, 1)),

    -- Achados em JSON para futuras análises
    achados_json       TEXT NOT NULL                                     -- {"maior_pico": {...}, ...}
) STRICT;

CREATE INDEX ix_insight_encerrado ON insight (encerrado_em);
