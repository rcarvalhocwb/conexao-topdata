-- Migração 015 — bilhetes coletados da memória da catraca (Etapa A.9 do docs/35).
-- (A 014 está reservada para a Etapa A.8; o migrador aplica por nome, sem exigir sequência.)
--
-- ColetarBilhete (EI-039) REMOVE o bilhete do equipamento (FUN:40) e a memória é circular,
-- 30.000 marcações (docs/34 §3). O worker grava cada bilhete aqui ANTES de pedir o próximo
-- (R-68): é a única cópia que sobra. Por isso a tabela é só-INSERT, como a auditoria.
--
-- O bilhete da DLL traz só tipo, data, hora AO MINUTO (sem segundos) e o código (EI-039).
-- Não há número de sequência nem identificação de reinício do equipamento: a deduplicação é
-- pelo conteúdo — (catraca, data e hora, tipo, impressão do código) — mais o tipo 128 ("já
-- retornado em coleta anterior", tipos-bilhete.csv, manual 5.2.2), tratado por quem grava.
-- Limitação registrada no docs/34 §11: duas marcações idênticas na mesma catraca, no mesmo
-- minuto, com o mesmo código e o mesmo tipo, viram uma (a DLL não as distingue).
--
-- LGPD (docs/34 §5.1 e §6): nenhum código em claro. Fica a máscara (CredentialValue.Mascarar,
-- "cred:****01(10)") e a impressão HMAC-SHA256 com chave fora da base (ImpressaoDeCodigo,
-- Etapa B.1; DPAPI, ADR-0014). A impressão é necessária: a máscara (2 últimos dígitos) colide
-- entre cartões diferentes no mesmo minuto e apagaria marcações legítimas na deduplicação.
-- A impressão é do código como a catraca o devolveu (sem perfil de provedor: o bilhete não
-- diz de que provedor é).
CREATE TABLE collected_ticket (
    id              TEXT    NOT NULL PRIMARY KEY,   -- UUID v7
    inner_number    INTEGER NOT NULL,
    raw_type        INTEGER NOT NULL,               -- tipo do bilhete, como veio (tipos-bilhete.csv)
    repeated        INTEGER NOT NULL,               -- 1 = tipo 128 sem original na base
    marked_at       TEXT    NULL,                   -- relógio da catraca, ao minuto; NULL = data inválida
    code_mask       TEXT    NOT NULL,
    code_hmac       TEXT    NOT NULL,
    code_key_id     TEXT    NOT NULL,               -- qual chave calculou a impressão (troca sem reescrever)
    collection_id   TEXT    NOT NULL,               -- a coleta que trouxe (o comando, quando houver)
    collection_seq  INTEGER NOT NULL,               -- posição nesta coleta, a partir de 1
    collected_at    TEXT    NOT NULL,               -- relógio da borda
    CHECK (inner_number BETWEEN 1 AND 99),
    CHECK (raw_type BETWEEN 0 AND 255),
    CHECK (repeated IN (0, 1) AND repeated = (raw_type = 128)),
    -- Ao minuto, sem segundos: o que o bilhete tem (EI-039). Formato fixo para a deduplicação.
    CHECK (marked_at IS NULL OR (length(marked_at) = 16 AND marked_at GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]')),
    -- Só a máscara: "cred:****" + até 2 últimos dígitos + "(n)". Nunca o código.
    CHECK (code_mask GLOB 'cred:[*][*][*][*]*(*)' AND length(code_mask) <= 20),
    CHECK (length(code_hmac) = 76 AND code_hmac GLOB 'hmac-sha256:*' AND substr(code_hmac, 13) NOT GLOB '*[^0-9a-f]*'),
    CHECK (length(code_key_id) BETWEEN 4 AND 40),
    CHECK (length(collection_id) = 36),
    CHECK (collection_seq >= 1)
) STRICT;

-- A chave da deduplicação. Data inválida (NULL) entra como vazia, senão dois bilhetes iguais
-- com o relógio da catraca zerado escapariam (NULL é distinto de NULL num índice único).
CREATE UNIQUE INDEX ux_collected_ticket_marcacao
    ON collected_ticket (inner_number, ifnull(marked_at, ''), raw_type, code_hmac);

-- Achar o original de um 128 (mesma catraca, minuto e código; qualquer tipo).
CREATE INDEX ix_collected_ticket_original
    ON collected_ticket (inner_number, ifnull(marked_at, ''), code_hmac);

CREATE INDEX ix_collected_ticket_coleta ON collected_ticket (collection_id, collection_seq);

CREATE TRIGGER collected_ticket_nao_muda
BEFORE UPDATE ON collected_ticket
BEGIN
    SELECT RAISE(ABORT, 'collected_ticket é só-INSERT: o bilhete coletado não muda');
END;

CREATE TRIGGER collected_ticket_nao_se_apaga
BEFORE DELETE ON collected_ticket
BEGIN
    SELECT RAISE(ABORT, 'collected_ticket é só-INSERT: o bilhete coletado não se apaga');
END;
