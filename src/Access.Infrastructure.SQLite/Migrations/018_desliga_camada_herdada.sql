-- Migração 018: apaga as chaves da camada inteligente gravadas por builds antigos (achado E7-2 do docs/41).
-- Entre os commits d6fd80c e 3a6cc94, o serviço gravava inteligencia.ligada = 1 e
-- inteligencia.coletor = 1 em toda partida. A gravação saiu, mas o valor ficou na base: uma
-- instalação que rodou um desses builds continuava com a camada ligada depois de atualizar,
-- contra a regra "desligada até a I.11" (docs/36). inteligencia.coletor não tem consumidor.
-- Sem linha, a camada fica desligada (ChavesDaInteligencia). Idempotente.
DELETE FROM edge_setting WHERE key IN ('inteligencia.ligada', 'inteligencia.coletor');
