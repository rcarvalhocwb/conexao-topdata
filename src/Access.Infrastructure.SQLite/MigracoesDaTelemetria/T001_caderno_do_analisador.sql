-- Migração T001 de telemetria.db — Etapa I.0 do docs/36 (desenho: docs/36-anexos/02 §3.3).
--
-- telemetria.db é um arquivo PRÓPRIO, ao lado de acesso.db, com migrador próprio (prefixo T).
-- O SQLite tem um escritor por arquivo: se a camada inteligente escrevesse em acesso.db, cada
-- escrita dela poderia segurar a decisão de um worker. Aqui, o pior caso da telemetria travada é
-- perder telemetria, nunca atrasar um giro.
--
-- Por construção, NENHUMA tabela deste arquivo tem coluna para código de ingresso, máscara, nome
-- de titular ou de operador (invariante I6, §3.1; docs/36-anexos/02 §6). Um teste varre as
-- colunas e reprova coluna nova com esse tipo de nome.
--
-- A T001 só cria o caderno do próprio Analisador. As tabelas do coletor do worker (device_signal,
-- health_minute, I.1) e do Analisador (agg_minute, alert, suggestion, insight; I.3 em diante)
-- entram nas migrações T002 em diante, quando cada etapa precisar delas: migração publicada nunca
-- é editada, e criar tabela antes de quem a usa congela um desenho ainda não provado.

-- Um ciclo do Analisador que terminou bem: quando, quanto durou, quantas tentativas novas leu da
-- base e até onde. Serve ao Diagnóstico depois de um reinício e à medição do orçamento
-- (NOVO-PERF-IA-01). Ciclo que falha não chega aqui (a falha pode ser justamente este arquivo):
-- fica só na memória do serviço, que o Diagnóstico mostra.
CREATE TABLE analyzer_cycle (
    id                 INTEGER PRIMARY KEY,
    session_id         TEXT    NULL,                                   -- partida do serviço (016)
    finished_at        TEXT    NOT NULL,                               -- ISO-8601 UTC
    duration_ms        INTEGER NOT NULL CHECK (duration_ms >= 0),
    new_attempts       INTEGER NOT NULL CHECK (new_attempts >= 0),
    last_attempt_rowid INTEGER NOT NULL CHECK (last_attempt_rowid >= 0), -- cursor em acesso.db
    over_budget        INTEGER NOT NULL CHECK (over_budget IN (0, 1))
) STRICT;

CREATE INDEX ix_ciclo_momento ON analyzer_cycle (finished_at);
