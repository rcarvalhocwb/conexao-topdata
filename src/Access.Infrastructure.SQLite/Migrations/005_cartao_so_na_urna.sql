-- Migração 005 — cartão da bilheteria só vale na fenda da urna.
-- Ver docs/19-bilheteria-local-e-divisao-das-catracas.md, seção 5.
--
-- Uma migração nova, e não uma alteração da 004: migração publicada nunca é editada.
-- Quem já aplicou a 004 não a aplicaria de novo, e o banco divergiria do repositório.

-- Quando ligado, o ingresso deste provedor só é aceito se lido no leitor 2 (fenda da
-- urna, origem 3). Lido no leitor da frente, a pessoa passaria e FICARIA com o cartão —
-- e a urna, que existe para recolhê-lo, viraria enfeite.
ALTER TABLE ticket_provider ADD COLUMN urn_only INTEGER NOT NULL DEFAULT 0;
