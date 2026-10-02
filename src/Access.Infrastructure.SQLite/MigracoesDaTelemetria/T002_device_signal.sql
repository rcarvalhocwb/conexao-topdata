-- Migração T002 de telemetria.db — Etapa I.1 do docs/36 (coletor mínimo; §3.3, §2.2 G-01/02/07/08/10/14).
--
-- Caderno de sinais: eventos brutos do worker, em ordem cronológica, sem código de ingresso nem
-- máscara. Só-INSERT. Enfileirado num anel em memória durante a operação, descarregado pelo worker
-- a cada 2 s (entre voltas, no mesmo try/catch da publicação de situação; SessaoDeOperacao.cs).
--
-- Por construção, NENHUMA coluna contém código, máscara ou nome de titular (invariante I6).

CREATE TABLE device_signal (
    id TEXT PRIMARY KEY,               -- UUIDv7, único em todo o arquivo
    session_id TEXT NULL,              -- partida do serviço (migração 016); nula em teste
    inner_number INTEGER NOT NULL CHECK (inner_number BETWEEN 1 AND 99),
    kind TEXT NOT NULL CHECK (kind IN ('origem', 'leitura_vazia', 'transicao', 'liberacao_recusada', 'conexao')),

    origin_raw INTEGER NULL,           -- bruto, sem CHECK (ADR-0018: contar, nunca calar)
    complement INTEGER NULL,           -- 0..255, bruto

    state_from TEXT NULL,              -- estado anterior da máquina
    state_to TEXT NULL,                -- estado novo
    trigger TEXT NULL,                 -- motivo da transição

    native_return INTEGER NULL,        -- retorno nativo de LiberarCatraca* (só em liberacao_recusada)
    firmware TEXT NULL,                -- versão identificada (só em conexao)

    pending_attempt_id TEXT NULL,      -- ticket_id da tentativa pendente no instante (referência fraca)
    device_time TEXT NULL,             -- hora da catraca (ISO-8601 UTC)
    received_at TEXT NOT NULL          -- hora de recebimento no worker (ISO-8601 UTC)
) STRICT;

CREATE INDEX ix_signal_catraca ON device_signal (inner_number, received_at);

-- Retenção: sem dado pessoal (código, máscara, nome), fica com o evento (proposta: 1 ano para
-- calibração entre eventos; ver docs/36-anexos/02 §3.3).
