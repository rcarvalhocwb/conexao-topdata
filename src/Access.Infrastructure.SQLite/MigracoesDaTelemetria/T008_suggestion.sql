-- Migração T008 de telemetria.db — Etapa I.9 do docs/36 (IN-07): sugestões de parametrização.
--
-- Sugestões de mudança em campos da catraca (tempo do relé, leitor, display), baseadas em
-- evidência estatística sobre as operações passadas. O operador pode usar a sugestão (ela
-- só preenche o formulário de parametrização) ou descartar. A sugestão, usada ou descartada,
-- fica registrada para análise pós-evento.
--
-- Cada sugestão tem um campo, o valor atual, o valor sugerido, evidência (JSON sem código),
-- a situação (aberta, usada, descartada) e quem a usou/descartou e quando.

CREATE TABLE suggestion (
    id              TEXT PRIMARY KEY,                       -- UUIDv7
    session_id      TEXT NULL,                             -- partida do serviço (016)
    inner_number    INTEGER NOT NULL CHECK (inner_number BETWEEN 1 AND 99),
    field           TEXT NOT NULL CHECK (field IN (
                        'TempoDoAcionamento1',             -- P1: tempo do relé
                        'TipoDeLeitor',                    -- P2: tipo de leitor
                        'MensagemPadrao'                   -- P3: display
                    )),
    current_value   TEXT NOT NULL,                         -- o valor que está configurado hoje
    suggested_value TEXT NOT NULL,                         -- o valor que a análise recomenda
    evidence        TEXT NOT NULL,                         -- JSON com: motivo, amostra, origem do cálculo
    created_at      TEXT NOT NULL,                         -- ISO-8601 UTC
    situation       TEXT NOT NULL CHECK (situation IN (
                        'aberta',                          -- ainda não foi usada nem descartada
                        'usada',                           -- o operador usou e salvou
                        'descartada'                       -- o operador descartou
                    )),
    used_by         TEXT NULL,                             -- quem usou ou descartou (nome do operador)
    used_at         TEXT NULL                              -- ISO-8601 UTC; quando foi usada ou descartada
) STRICT;

CREATE INDEX ix_sugestao_catraca ON suggestion (inner_number, created_at DESC);
CREATE INDEX ix_sugestao_situacao ON suggestion (situation, created_at DESC);
