-- Migração T003 de telemetria.db — Etapa I.1 do docs/36 (coletor mínimo; §3.3, §2.2 G-03/04/05/06/09/13).
--
-- Uma linha por catraca por minuto: contadores e histogramas do estado da máquina e da operação.
-- Escrita pelo worker a cada minuto (entre voltas, como device_signal; o Analisador lê para agregação).
--
-- Por construção, NENHUMA coluna contém código, máscara ou nome de titular (invariante I6).

CREATE TABLE health_minute (
    inner_number INTEGER NOT NULL CHECK (inner_number BETWEEN 1 AND 99),
    minute TEXT NOT NULL,                       -- 'yyyy-MM-ddTHH:mm'
    
    session_id TEXT NULL,                       -- partida do serviço (migração 016); nula em teste
    worker TEXT NOT NULL,                       -- identificação do worker que coletou
    
    seconds_in_operation INTEGER NOT NULL CHECK (seconds_in_operation >= 0),  -- segundos online neste minuto
    reconnects INTEGER NOT NULL CHECK (reconnects >= 0),                      -- reconexões
    recv_errors INTEGER NOT NULL CHECK (recv_errors >= 0),                    -- erros de recepção
    empty_reads INTEGER NOT NULL CHECK (empty_reads >= 0),                    -- leituras vazias
    unknown_origins INTEGER NOT NULL CHECK (unknown_origins >= 0),            -- origens desconhecidas
    loop_turns INTEGER NOT NULL CHECK (loop_turns >= 0),                      -- voltas do laço
    decisions INTEGER NOT NULL CHECK (decisions >= 0),                        -- decisões processadas
    
    decision_ms_hist TEXT NOT NULL,             -- JSON de baldes fixos para histograma de latência
    recv_ms_hist TEXT NOT NULL,                 -- JSON de baldes fixos para latência de recepção
    loop_ms_hist TEXT NOT NULL,                 -- JSON de baldes fixos para latência do laço
    
    clock_drift_s INTEGER NULL,                 -- derivação do relógio em segundos (só no minuto da conferência)
    dropped INTEGER NOT NULL CHECK (dropped >= 0),  -- itens descartados pelo anel
    
    PRIMARY KEY (inner_number, minute, worker)
) STRICT;

-- Retenção: sem dado pessoal, fica com o evento (proposta: 1 ano para calibração; docs/36-anexos/02 §3.3).
