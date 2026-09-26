-- Migração 004 — cartão físico reutilizável da bilheteria local, e categorias abertas.
-- Ver docs/19-bilheteria-local-e-divisao-das-catracas.md

-- A bilheteria local é só mais um provedor. O que a distingue é a política de reuso.
ALTER TABLE ticket_provider ADD COLUMN reusable INTEGER NOT NULL DEFAULT 0;

-- Segundos mínimos entre dois usos do mesmo cartão. 0 desliga.
ALTER TABLE ticket_provider ADD COLUMN reuse_interval_seconds INTEGER NOT NULL DEFAULT 0
    CHECK (reuse_interval_seconds >= 0);

-- Inteira, meia, solidária, ou o que aparecer. Texto aberto: tipo novo de entrada
-- criado na véspera não pode exigir versão nova do sistema.
ALTER TABLE ticket ADD COLUMN category TEXT NULL;

-- O instante do último uso, em segundos. Existe ao lado de last_used_at porque comparar
-- "há menos de 300 segundos" em texto ISO com fração e fuso é frágil no SQLite; em
-- inteiro, é uma subtração.
ALTER TABLE ticket ADD COLUMN last_used_epoch INTEGER NULL;

-- A categoria é FOTOGRAFADA em cada tentativa. O cartão da bilheteria é revendido: se o
-- relatório lesse a categoria atual do cartão, a meia-entrada vendida às 19h viraria a
-- inteira vendida às 21h, e a prestação de contas por tipo mentiria.
ALTER TABLE ticket_use_attempt ADD COLUMN category TEXT NULL;

-- Cada venda de balcão que carrega um cartão. É o "vendido" da bilheteria local, do
-- mesmo jeito que o ingresso ingerido é o "vendido" do online.
CREATE TABLE ticket_sale (
    id          TEXT    NOT NULL PRIMARY KEY,
    ticket_id   TEXT    NOT NULL REFERENCES ticket (id),
    provider_id TEXT    NOT NULL REFERENCES ticket_provider (id),
    category    TEXT    NOT NULL,
    uses        INTEGER NOT NULL CHECK (uses >= 1),
    sold_at     TEXT    NOT NULL,
    operator    TEXT    NULL
) STRICT;

CREATE INDEX ix_venda_provedor ON ticket_sale (provider_id, sold_at);
CREATE INDEX ix_venda_categoria ON ticket_sale (provider_id, category);
CREATE INDEX ix_tentativa_categoria ON ticket_use_attempt (provider_id, category) WHERE outcome = 'consumido';
