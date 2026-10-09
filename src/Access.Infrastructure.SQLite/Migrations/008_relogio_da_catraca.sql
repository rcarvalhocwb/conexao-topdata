-- Migração 008 — o relógio de cada catraca (fase 4a, docs/32).
-- O worker acerta o relógio a cada conexão e confere a cada hora; o painel mostra o
-- resultado. Divergência em segundos inteiros: a catraca não guarda fração.
ALTER TABLE device_status ADD COLUMN clock_set_at        TEXT    NULL;  -- último acerto aceito
ALTER TABLE device_status ADD COLUMN clock_checked_at    TEXT    NULL;  -- última conferência
ALTER TABLE device_status ADD COLUMN clock_drift_seconds INTEGER NULL;  -- catraca − borda; nulo se data inválida
ALTER TABLE device_status ADD COLUMN clock_divergent     INTEGER NOT NULL DEFAULT 0;  -- 1 = acima do limite ou data inválida
