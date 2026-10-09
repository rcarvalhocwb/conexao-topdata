-- Migração 013 — configuração salva × aplicada (Etapa A.5 do docs/35; docs/34-anexos/04, "Ao vivo").
-- Uma migração nova, e não uma alteração da 006/008: migração publicada nunca é editada.
--
-- O que a catraca ACEITOU, publicado pelo worker com o resto da situação (ADR-0024). No contrato
-- são Equipamento.configuracao_aplicada_em e Equipamento.configuracao_versao.
--
-- config_applied_at: a última vez que EnviarConfiguracoes (EI-030) devolveu 0 para esta catraca.
--   Não muda quando o operador salva nem quando o "Aplicar agora" troca a configuração no worker
--   (a troca vem antes da reconexão); só quando o envio dá certo.
-- config_version: SHA-256, em hexadecimal minúsculo, da forma canônica do que foi enviado
--   (VersaoDaConfiguracao, Access.Application). Sem o número do cartão master: só se ele existe.
--
-- NULL = o worker que atende a catraca ainda não teve um envio aceito desde que subiu (ou linha
-- gravada antes desta migração). Não é "configuração vazia": a catraca pode estar com a de antes.
--
-- Anuláveis e sem DEFAULT: as linhas que já existem ficam NULL, que é a verdade sobre elas.
ALTER TABLE device_status ADD COLUMN config_applied_at TEXT NULL;  -- ISO-8601 UTC
ALTER TABLE device_status ADD COLUMN config_version    TEXT NULL
    CHECK (config_version IS NULL
           OR (length(config_version) = 64 AND config_version NOT GLOB '*[^0-9a-f]*'));
