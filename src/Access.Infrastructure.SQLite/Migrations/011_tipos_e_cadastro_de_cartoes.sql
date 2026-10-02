-- Migração 011 — tipos de entrada, colunas de cadastro do cartão, trilha do cadastro e lotes
-- de importação (Etapa B.1 do docs/35; modelo do docs/34 §5.1 e do docs/34-anexos/03 §1).
-- Uma migração nova, e não uma alteração da 003/004: migração publicada nunca é editada.
--
-- Nada aqui muda o que já funciona: as colunas novas de ticket nascem nulas (ou 'nuvem', o
-- valor de hoje de owner_of_fields, ADR-0025) e a sincronização, o balcão e a ingestão
-- continuam gravando exatamente como antes. Quem passa a preenchê-las são as etapas B.4 em
-- diante (cadastro e importação sem conexão, ADR-0025). A fila de subida das mudanças da
-- queda e a lista de conflitos NÃO estão aqui: dependem do contrato da nuvem (PROPOSTA
-- FUTURA) e da definição medida de "sem conexão", e entram com a B.4.
--
-- Nenhuma tabela nova guarda o número do cartão em claro: a trilha e os lotes guardam a
-- máscara (CredentialValue.Mascarar: "cred:****01(10)") e, na trilha, um HMAC com chave
-- que não está na base (DPAPI, docs/34 §6; ADR-0014). O número continua só em ticket, onde
-- a decisão de 150 ms precisa dele por índice (ADR-0008).

-- ---------------------------------------------------------------------------------------
-- 1. Tipos de entrada (docs/26 §1): INTEIRA, MEIA, SOCIAL…
-- ---------------------------------------------------------------------------------------
-- ticket.category continua texto aberto (docs/19 §5.4, migração 004): um tipo novo criado
-- na véspera não pode exigir versão nova. O tipo cadastrado dá nome, ordem, cor e a chave
-- "ativo" à categoria de mesmo código. Categoria sem tipo cadastrado continua valendo.
-- Tipo inativo NEGA o uso (TipoInativo, Etapa B.2), mas nunca é apagado: os relatórios
-- antigos dependem dele (docs/26 §1).
CREATE TABLE ticket_type (
    code          TEXT    NOT NULL PRIMARY KEY,   -- INTEIRA, MEIA; é o que vai em ticket.category
    display_name  TEXT    NOT NULL,               -- "Meia-entrada"
    sort_order    INTEGER NOT NULL DEFAULT 0,     -- posição nos relatórios (docs/25)
    color_token   TEXT    NULL,                   -- token do Rayzer Design System, nunca cor solta
    active        INTEGER NOT NULL DEFAULT 1,
    created_at    TEXT    NOT NULL,
    created_by    TEXT    NULL,
    updated_at    TEXT    NULL,
    updated_by    TEXT    NULL,
    -- Maiúsculas, dígitos e sublinhado, sem espaço e sem acento (docs/26 §1). GLOB é
    -- sensível a caixa e não conhece acento: "meia" e "MÉIA" não passam.
    CHECK (length(code) BETWEEN 2 AND 20 AND code NOT GLOB '*[^A-Z0-9_]*'),
    CHECK (length(trim(display_name)) BETWEEN 1 AND 40),
    CHECK (sort_order >= 0),
    CHECK (active IN (0, 1)),
    -- A cor do tipo é um token (docs/27 §3.2: nenhuma tela escreve valor de cor) e nunca
    -- um token de situação: verde e vermelho dizem "liberado" e "negado" na operação, e um
    -- tipo pintado com eles seria lido como resultado de acesso.
    CHECK (color_token IS NULL OR (
        length(color_token) BETWEEN 8 AND 60
        AND color_token GLOB 'Rayzer.?*'
        AND color_token NOT GLOB '*[^A-Za-z0-9.]*'
        AND color_token NOT GLOB 'Rayzer.Access.*'
        AND color_token NOT GLOB 'Rayzer.Device.*'
        AND color_token NOT GLOB 'Rayzer.Success*'
        AND color_token NOT GLOB 'Rayzer.Danger*'
        AND color_token NOT GLOB 'Rayzer.Warning*')),
    CHECK (created_by IS NULL OR length(trim(created_by)) BETWEEN 2 AND 80),
    CHECK (updated_by IS NULL OR length(trim(updated_by)) BETWEEN 2 AND 80)
) STRICT;

-- Apelidos por provedor: as ~20 grafias da Zet para o mesmo tipo ("Meia Entrada",
-- "MEIA-ENTRADA"…, docs/30 Z6). Uma grafia de um provedor aponta para um tipo só; a
-- chave primária é o próprio índice que a decisão usa. Comparação exata, de propósito:
-- quem mapeia é o operador, não uma regra de caixa que ninguém vê.
CREATE TABLE ticket_type_alias (
    provider_id TEXT NOT NULL REFERENCES ticket_provider (id),
    alias       TEXT NOT NULL,
    type_code   TEXT NOT NULL REFERENCES ticket_type (code),
    created_at  TEXT NOT NULL,
    created_by  TEXT NULL,
    PRIMARY KEY (provider_id, alias),
    CHECK (length(alias) BETWEEN 1 AND 80 AND alias = trim(alias)),
    CHECK (created_by IS NULL OR length(trim(created_by)) BETWEEN 2 AND 80)
) STRICT;

CREATE INDEX ix_ticket_type_alias_tipo ON ticket_type_alias (type_code);

-- ---------------------------------------------------------------------------------------
-- 2. Colunas novas em ticket. Todas anuláveis: NULL = "anterior à 011 ou não informado",
--    e é exatamente o que as gravações de hoje continuam produzindo.
-- ---------------------------------------------------------------------------------------

-- Tipo de credencial (docs/34-anexos/03 §1.2). NULL = não classificado.
ALTER TABLE ticket ADD COLUMN kind TEXT NULL
    CHECK (kind IS NULL OR kind IN ('qr_online', 'cartao_bilheteria', 'codigo_barras', 'staff', 'cortesia'));

-- Por onde o cartão entrou (docs/34 §5.1). NULL = gravado por um caminho anterior à 011.
ALTER TABLE ticket ADD COLUMN source TEXT NULL
    CHECK (source IS NULL OR source IN ('sync', 'importacao', 'manual', 'balcao', 'webhook'));

-- Quem mudou o cadastro deste cartão por último (ADR-0025, decisão D1): 'nuvem', ou
-- 'local' enquanto uma mudança feita sem conexão não subiu e foi aceita. NÃO é um dono
-- permanente: depois de subir, volta a 'nuvem'. O padrão 'nuvem' é o de hoje — tudo o que
-- existe veio da sincronização ou do balcão, e nada está esperando para subir. Quem grava
-- 'local' é o cadastro e a importação sem conexão (B.4 em diante); a fila de subida e a
-- lista de conflitos entram na migração da B.4, junto com a definição medida de "sem
-- conexão" (ADR-0025, consequências).
ALTER TABLE ticket ADD COLUMN owner_of_fields TEXT NOT NULL DEFAULT 'nuvem'
    CHECK (owner_of_fields IN ('nuvem', 'local'));

-- Motivo da situação atual (bloqueio, cancelamento): 5 a 200, como a liberação manual
-- (ComandoDeCatraca). Quem exige o motivo em cada ação é o cadastro (B.6), não este CHECK:
-- a sincronização de hoje cancela sem motivo e não pode passar a falhar.
ALTER TABLE ticket ADD COLUMN status_reason TEXT NULL
    CHECK (status_reason IS NULL OR length(trim(status_reason)) BETWEEN 5 AND 200);

-- Autoria (nome digitado, 2 a 80; não há login, docs/27 §11). Hora em ISO-8601 UTC.
ALTER TABLE ticket ADD COLUMN status_changed_at TEXT NULL;
ALTER TABLE ticket ADD COLUMN status_changed_by TEXT NULL
    CHECK (status_changed_by IS NULL OR length(trim(status_changed_by)) BETWEEN 2 AND 80);
ALTER TABLE ticket ADD COLUMN created_by TEXT NULL
    CHECK (created_by IS NULL OR length(trim(created_by)) BETWEEN 2 AND 80);
ALTER TABLE ticket ADD COLUMN updated_at TEXT NULL;
ALTER TABLE ticket ADD COLUMN updated_by TEXT NULL
    CHECK (updated_by IS NULL OR length(trim(updated_by)) BETWEEN 2 AND 80);

-- Portões em que o cartão vale: lista JSON de identificadores; NULL = todos. A lista vazia
-- é recusada, porque "vale em nenhum portão" é bloqueio, e bloqueio tem motivo.
-- ATENÇÃO: a decisão NÃO lê esta coluna (o motivo GATE_NAO_PERMITIDO existe no catálogo e
-- não é usado). Ninguém a grava até a regra entrar na decisão, para que a tela nunca
-- mostre uma restrição que a catraca não aplica.
ALTER TABLE ticket ADD COLUMN allowed_gates TEXT NULL
    CHECK (allowed_gates IS NULL OR (
        json_valid(allowed_gates)
        AND json_type(allowed_gates) = 'array'
        AND json_array_length(allowed_gates) >= 1));

-- Lote físico do cartão impresso (docs/05, card_stock.batch). Não é o lote de importação.
ALTER TABLE ticket ADD COLUMN batch_label TEXT NULL
    CHECK (batch_label IS NULL OR length(trim(batch_label)) BETWEEN 1 AND 40);

-- ---------------------------------------------------------------------------------------
-- 3. Lotes de importação (docs/34 §5.3; docs/34-anexos/03 §3.7). A prévia (B.3) não grava
--    nada; quem cria o lote é a confirmação (B.4), que só roda sem conexão (ADR-0025).
-- ---------------------------------------------------------------------------------------
CREATE TABLE import_batch (
    id            TEXT    NOT NULL PRIMARY KEY,   -- UUIDv7
    kind          TEXT    NOT NULL CHECK (kind IN ('cartoes', 'tipos', 'bloqueio_em_massa', 'painel_csv')),
    provider_id   TEXT    NULL REFERENCES ticket_provider (id),   -- tipos não têm provedor
    file_name     TEXT    NOT NULL,               -- só o nome: o caminho traz o usuário do Windows
    file_format   TEXT    NOT NULL CHECK (file_format IN ('csv', 'xlsx')),
    file_sha256   TEXT    NOT NULL,               -- reconhece o mesmo arquivo importado de novo
    file_bytes    INTEGER NOT NULL CHECK (file_bytes >= 0),
    mode          TEXT    NOT NULL CHECK (mode IN ('incluir_e_atualizar', 'so_incluir')),
    rows_total    INTEGER NOT NULL DEFAULT 0 CHECK (rows_total >= 0),
    rows_new      INTEGER NOT NULL DEFAULT 0 CHECK (rows_new >= 0),
    rows_changed  INTEGER NOT NULL DEFAULT 0 CHECK (rows_changed >= 0),
    rows_same     INTEGER NOT NULL DEFAULT 0 CHECK (rows_same >= 0),
    rows_error    INTEGER NOT NULL DEFAULT 0 CHECK (rows_error >= 0),
    rows_warning  INTEGER NOT NULL DEFAULT 0 CHECK (rows_warning >= 0),
    reason        TEXT    NULL,
    requested_by  TEXT    NOT NULL,               -- nome digitado
    workstation   TEXT    NOT NULL,               -- conta Windows e máquina, postas pelo serviço
    previewed_at  TEXT    NOT NULL,
    applied_at    TEXT    NULL,
    undone_at     TEXT    NULL,
    undone_by     TEXT    NULL,
    status        TEXT    NOT NULL DEFAULT 'previa'
                  CHECK (status IN ('previa', 'aplicando', 'aplicada', 'falhou', 'desfeita', 'descartada')),
    CHECK (kind = 'tipos' OR provider_id IS NOT NULL),
    CHECK (length(file_name) BETWEEN 1 AND 255 AND instr(file_name, '/') = 0 AND instr(file_name, '\') = 0),
    CHECK (length(file_sha256) = 64 AND file_sha256 NOT GLOB '*[^0-9a-f]*'),
    CHECK (rows_new + rows_changed + rows_same + rows_error <= rows_total),
    CHECK (reason IS NULL OR length(trim(reason)) BETWEEN 5 AND 200),
    CHECK (length(trim(requested_by)) BETWEEN 2 AND 80),
    CHECK (length(trim(workstation)) BETWEEN 1 AND 200),
    CHECK (undone_by IS NULL OR length(trim(undone_by)) BETWEEN 2 AND 80)
) STRICT;

CREATE INDEX ix_import_batch_arquivo ON import_batch (file_sha256);
CREATE INDEX ix_import_batch_situacao ON import_batch (status, previewed_at);

-- O lote é auditoria: não se apaga, e a origem (arquivo, quem, quando) não muda depois de
-- gravada — só a situação, as contagens e os carimbos de aplicar e desfazer andam.
CREATE TRIGGER import_batch_nao_se_apaga
BEFORE DELETE ON import_batch
BEGIN
    SELECT RAISE(ABORT, 'import_batch é auditoria: não se apaga');
END;

CREATE TRIGGER import_batch_origem_nao_muda
BEFORE UPDATE OF id, kind, provider_id, file_name, file_format, file_sha256, file_bytes, mode,
                 requested_by, workstation, previewed_at
ON import_batch
BEGIN
    SELECT RAISE(ABORT, 'import_batch: a origem do lote não muda depois de gravada');
END;

-- A situação só anda para a frente: prévia → (aplicando →) aplicada → desfeita, ou termina
-- em falhou/descartada. Um lote desfeito não volta a valer.
CREATE TRIGGER import_batch_situacao_anda_para_frente
BEFORE UPDATE OF status ON import_batch
WHEN OLD.status <> NEW.status AND NOT (
       (OLD.status = 'previa'    AND NEW.status IN ('aplicando', 'aplicada', 'falhou', 'descartada'))
    OR (OLD.status = 'aplicando' AND NEW.status IN ('aplicada', 'falhou'))
    OR (OLD.status = 'aplicada'  AND NEW.status = 'desfeita'))
BEGIN
    SELECT RAISE(ABORT, 'import_batch: essa mudança de situação não é permitida');
END;

-- Uma linha do arquivo. Sem código em claro e sem titular: a linha aponta para o ticket, e
-- o código completo só existe no arquivo de devolução, que é do operador (anexo 03 §3.6).
-- before_json guarda o estado anterior do cartão (sem o código) para o desfazer.
CREATE TABLE import_batch_row (
    batch_id              TEXT    NOT NULL REFERENCES import_batch (id),
    line                  INTEGER NOT NULL CHECK (line >= 1),   -- linha física, a que o Excel mostra
    ticket_id             TEXT    NULL REFERENCES ticket (id),
    outcome               TEXT    NOT NULL CHECK (outcome IN ('novo', 'alterado', 'igual', 'erro')),
    code_masked           TEXT    NULL,
    error                 TEXT    NULL,
    before_json           TEXT    NULL,
    used_count_at_apply   INTEGER NULL CHECK (used_count_at_apply IS NULL OR used_count_at_apply >= 0),
    last_used_at_at_apply TEXT    NULL,
    PRIMARY KEY (batch_id, line),
    CHECK (code_masked IS NULL OR (
        length(code_masked) BETWEEN 12 AND 18
        AND (code_masked GLOB 'cred:[*][*][*][*]([1-9]*)'
          OR code_masked GLOB 'cred:[*][*][*][*]??([1-9]*)'))),
    CHECK (error IS NULL OR length(error) BETWEEN 1 AND 300),
    CHECK (before_json IS NULL OR json_valid(before_json)),
    CHECK (outcome <> 'erro' OR error IS NOT NULL),
    CHECK (outcome <> 'alterado' OR before_json IS NOT NULL)
) STRICT;

CREATE INDEX ix_import_batch_row_ticket ON import_batch_row (ticket_id) WHERE ticket_id IS NOT NULL;

CREATE TRIGGER import_batch_row_nao_se_apaga
BEFORE DELETE ON import_batch_row
BEGIN
    SELECT RAISE(ABORT, 'import_batch_row é auditoria: não se apaga');
END;

CREATE TRIGGER import_batch_row_nao_muda
BEFORE UPDATE ON import_batch_row
BEGIN
    SELECT RAISE(ABORT, 'import_batch_row é auditoria: não muda');
END;

-- Defesa em profundidade: o texto livre da linha não carrega o código do próprio cartão.
-- Só para códigos de 6 ou mais caracteres (o piso do redator de log): abaixo disso o
-- código aparece por acaso dentro de datas e contagens, e a trava recusaria o legítimo.
CREATE TRIGGER import_batch_row_sem_codigo_em_claro
BEFORE INSERT ON import_batch_row
WHEN NEW.ticket_id IS NOT NULL AND EXISTS (
    SELECT 1 FROM ticket t
    WHERE t.id = NEW.ticket_id
      AND ((length(t.qr_normalized) >= 6
            AND (instr(COALESCE(NEW.before_json, ''), t.qr_normalized) > 0
              OR instr(COALESCE(NEW.error, ''), t.qr_normalized) > 0))
        OR (length(trim(t.qr_raw)) >= 6
            AND (instr(COALESCE(NEW.before_json, ''), trim(t.qr_raw)) > 0
              OR instr(COALESCE(NEW.error, ''), trim(t.qr_raw)) > 0))))
BEGIN
    SELECT RAISE(ABORT, 'import_batch_row: o código do cartão não entra em claro');
END;

-- ---------------------------------------------------------------------------------------
-- 4. Trilha do cadastro (docs/34 §5.1; anexo 03 §1.5 e §2.4). Só INSERT.
-- ---------------------------------------------------------------------------------------
-- Um evento por mudança de cadastro de um cartão, gravado na mesma transação da mudança
-- (B.4/B.6). Acha-se o histórico de um código pelo HMAC (TrilhaDeCredenciais.Historico),
-- sem que a trilha guarde o número: quem copia a base não tem a chave, e sem ela não
-- consegue testar os 10^10 códigos possíveis. Destruir a chave encerra a ligação
-- (expurgo por destruição de chave, anexo 03 §5.4; retenção e prazos são B.9/D7).
CREATE TABLE credential_event (
    id           TEXT NOT NULL PRIMARY KEY,           -- UUIDv7
    ticket_id    TEXT NULL REFERENCES ticket (id),    -- nulo: código que não chegou a virar ticket
    code_masked  TEXT NOT NULL,                       -- cred:****01(10)
    code_hmac    TEXT NOT NULL,                       -- hmac-sha256:<64 hex> do código normalizado
    code_key_id  TEXT NOT NULL,                       -- qual chave calculou (rotação sem reescrever)
    action       TEXT NOT NULL CHECK (action IN (
                     'criado', 'editado', 'tipo_alterado', 'bloqueado', 'desbloqueado', 'cancelado',
                     'importado', 'importacao_desfeita', 'custodia_alterada', 'titular_revelado',
                     'expurgado')),
    before_json  TEXT NULL,                           -- só campos não pessoais
    after_json   TEXT NULL,
    reason       TEXT NULL,
    actor        TEXT NOT NULL,                       -- nome digitado
    workstation  TEXT NOT NULL,                       -- conta Windows e máquina, postas pelo serviço
    batch_id     TEXT NULL REFERENCES import_batch (id),
    at           TEXT NOT NULL,                       -- ISO-8601 UTC
    prev_hash    TEXT NOT NULL,                       -- encadeamento (padrão do audit_log, 001)
    hash         TEXT NOT NULL,
    CHECK (length(code_masked) BETWEEN 12 AND 18
           AND (code_masked GLOB 'cred:[*][*][*][*]([1-9]*)'
             OR code_masked GLOB 'cred:[*][*][*][*]??([1-9]*)')),
    CHECK (length(code_hmac) = 76
           AND substr(code_hmac, 1, 12) = 'hmac-sha256:'
           AND substr(code_hmac, 13) NOT GLOB '*[^0-9a-f]*'),
    CHECK (length(code_key_id) BETWEEN 1 AND 40),
    CHECK (before_json IS NULL OR json_valid(before_json)),
    CHECK (after_json IS NULL OR json_valid(after_json)),
    CHECK (reason IS NULL OR length(trim(reason)) BETWEEN 5 AND 200),
    CHECK (action NOT IN ('bloqueado', 'desbloqueado', 'cancelado') OR reason IS NOT NULL),
    CHECK (length(trim(actor)) BETWEEN 2 AND 80),
    CHECK (length(trim(workstation)) BETWEEN 1 AND 200),
    CHECK (length(prev_hash) = 64 AND prev_hash NOT GLOB '*[^0-9a-f]*'),
    CHECK (length(hash) = 64 AND hash NOT GLOB '*[^0-9a-f]*')
) STRICT;

CREATE INDEX ix_credential_event_codigo ON credential_event (code_hmac, at);
CREATE INDEX ix_credential_event_ticket ON credential_event (ticket_id, at) WHERE ticket_id IS NOT NULL;
CREATE INDEX ix_credential_event_lote ON credential_event (batch_id) WHERE batch_id IS NOT NULL;

-- A cadeia não bifurca: dois eventos nunca apontam para o mesmo anterior. Se duas escritas
-- lerem o mesmo "último" ao mesmo tempo, a segunda falha aqui em vez de criar um galho
-- que o verificador acusaria depois.
CREATE UNIQUE INDEX ux_credential_event_cadeia ON credential_event (prev_hash);

CREATE TRIGGER credential_event_nao_se_apaga
BEFORE DELETE ON credential_event
BEGIN
    SELECT RAISE(ABORT, 'credential_event é auditoria: não se apaga');
END;

CREATE TRIGGER credential_event_nao_muda
BEFORE UPDATE ON credential_event
BEGIN
    SELECT RAISE(ABORT, 'credential_event é auditoria: não muda');
END;

-- Mesma defesa em profundidade de import_batch_row, sobre todo texto livre do evento.
CREATE TRIGGER credential_event_sem_codigo_em_claro
BEFORE INSERT ON credential_event
WHEN NEW.ticket_id IS NOT NULL AND EXISTS (
    SELECT 1 FROM ticket t
    WHERE t.id = NEW.ticket_id
      AND ((length(t.qr_normalized) >= 6
            AND (instr(COALESCE(NEW.before_json, ''), t.qr_normalized) > 0
              OR instr(COALESCE(NEW.after_json, ''), t.qr_normalized) > 0
              OR instr(COALESCE(NEW.reason, ''), t.qr_normalized) > 0
              OR instr(NEW.actor, t.qr_normalized) > 0
              OR instr(NEW.workstation, t.qr_normalized) > 0))
        OR (length(trim(t.qr_raw)) >= 6
            AND (instr(COALESCE(NEW.before_json, ''), trim(t.qr_raw)) > 0
              OR instr(COALESCE(NEW.after_json, ''), trim(t.qr_raw)) > 0
              OR instr(COALESCE(NEW.reason, ''), trim(t.qr_raw)) > 0
              OR instr(NEW.actor, trim(t.qr_raw)) > 0
              OR instr(NEW.workstation, trim(t.qr_raw)) > 0))))
BEGIN
    SELECT RAISE(ABORT, 'credential_event: o código do cartão não entra em claro');
END;
