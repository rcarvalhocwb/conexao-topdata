-- Migração 007 — leituras simuladas, para operar sem catraca física.
-- Ver docs/23-modo-simulacao.md.

-- O painel pede "passe este código na catraca 2"; o worker em modo simulação retira a
-- leitura daqui e a entrega ao simulador como se o leitor da catraca tivesse lido. Daí
-- em diante é o caminho de verdade: decisão, registro, giro, envio à nuvem.
CREATE TABLE simulated_read (
    id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    inner_number INTEGER NOT NULL,
    code         TEXT    NOT NULL,
    at_urn       INTEGER NOT NULL,   -- 1 = fenda da urna (leitor 2); 0 = leitor da frente
    turn         INTEGER NOT NULL,   -- 1 = a pessoa gira se for liberada
    created_at   TEXT    NOT NULL,
    taken_at     TEXT    NULL
) STRICT;

CREATE INDEX ix_leitura_simulada_pendente ON simulated_read (inner_number) WHERE taken_at IS NULL;
