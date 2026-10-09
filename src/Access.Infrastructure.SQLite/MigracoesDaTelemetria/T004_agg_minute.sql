-- Migração T004 de telemetria.db — Etapa I.3+ do docs/36 (saúde e alertas; §3.3, §2.2).
--
-- Agregados por minuto e catraca, lidos do Analisador a partir das tentativas em acesso.db:
-- leituras, liberados, negados por motivo (JSON de contagens), giros, sem giro, histogramas,
-- entradas/saídas por sentido lógico (migração 017).
--
-- Por construção, NENHUMA coluna contém código, máscara ou nome de titular (invariante I6).

CREATE TABLE agg_minute (
    inner_number INTEGER NOT NULL CHECK (inner_number BETWEEN 1 AND 99),
    minute TEXT NOT NULL,                       -- 'yyyy-MM-ddTHH:mm'
    
    session_id TEXT NULL,                       -- partida do serviço (migração 016); nula em teste
    
    -- Contadores básicos da tentativa
    attempts INTEGER NOT NULL CHECK (attempts >= 0),                  -- total de tentativas
    reads INTEGER NOT NULL CHECK (reads >= 0),                        -- leituras com código
    liberados INTEGER NOT NULL CHECK (liberados >= 0),                -- tentativas com liberação
    negados INTEGER NOT NULL CHECK (negados >= 0),                    -- tentativas negadas
    
    -- Histogramas de negações por motivo (JSON: {"motivo": count, ...})
    negados_por_motivo TEXT NOT NULL DEFAULT '{}',
    
    -- Giros e latência
    giros INTEGER NOT NULL CHECK (giros >= 0),                        -- tentativas com giro confirmado
    sem_giro INTEGER NOT NULL CHECK (sem_giro >= 0),                  -- liberadas sem giro
    passage_delay_ms_hist TEXT NOT NULL,        -- JSON de baldes fixos: Δ(liberação→giro)
    
    -- Leituras vazias e desconhecidas (contagens; nunca o código em si — invariante I6)
    empty_reads INTEGER NOT NULL CHECK (empty_reads >= 0),
    unknown_reads INTEGER NOT NULL CHECK (unknown_reads >= 0),
    
    -- Por origem de leitura (JSON: {"origin": count, ...})
    reads_by_origin TEXT NOT NULL DEFAULT '{}',
    
    -- Por sentido (migração 017: entrada/saída; nulo até merge)
    entradas INTEGER NULL CHECK (entradas IS NULL OR entradas >= 0),
    saidas INTEGER NULL CHECK (saidas IS NULL OR saidas >= 0),
    sem_sentido_conhecido INTEGER NULL CHECK (sem_sentido_conhecido IS NULL OR sem_sentido_conhecido >= 0),

    PRIMARY KEY (inner_number, minute)
) STRICT;

CREATE INDEX ix_agg_minute_catraca ON agg_minute (inner_number, minute);

-- Retenção: sem dado pessoal, fica com o evento (proposta: 1 ano para calibração; docs/36-anexos/02 §3.3).
