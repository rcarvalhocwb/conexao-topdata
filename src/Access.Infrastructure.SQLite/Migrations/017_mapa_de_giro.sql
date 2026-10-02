-- Migração 017 — mapa de giro por catraca (decisão D9 do dono do produto, docs/34 §9:
-- "nomenclatura de sentido é do sistema"). A 016 está reservada para outra correção.
--
-- A DLL libera o braço por sentido FÍSICO, do ponto de vista da catraca: LiberarCatracaEntrada
-- (EI-041), LiberarCatracaSaida (EI-042) e as invertidas (EI-043/044). O que o evento conta como
-- entrada ou saída é nosso e depende de como a catraca foi instalada. Para cada origem que libera
-- — leitor 1 (frente e QR), leitor 2 (urna), teclado, liberação manual do painel — o mapa diz
-- qual função chamar e como contar o giro.
--
-- Sem linha (ou com a linha toda nula) a origem segue o padrão de hoje: a função do perfil da
-- catraca (device_config.release_function, A.3; EI-041 no padrão) e "conta como entrada".
-- Os dois sentidos (EI-045) NÃO entram: continuam restritos à evacuação (D5).

-- 1. O mapa: uma linha por (catraca, origem). Gravar o mapa troca as linhas da catraca numa
--    transação só. Voltar ao padrão = gravar a linha nula (não se apaga, como a 012).
CREATE TABLE turn_map_rule (
    inner_number      INTEGER NOT NULL CHECK (inner_number BETWEEN 1 AND 99),
    origin            TEXT    NOT NULL CHECK (origin IN ('leitor1', 'leitor2', 'teclado', 'manual')),
    -- NULL = a função do perfil da catraca (A.3). Mesmos nomes de FuncaoDeLiberacao.
    release_function  TEXT    NULL
                      CHECK (release_function IN ('Entrada', 'EntradaInvertida', 'Saida', 'SaidaInvertida')),
    -- NULL = padrão (conta como entrada, função do perfil, texto padrão): a origem herda tudo.
    counted_as        TEXT    NULL CHECK (counted_as IN ('entrada', 'saida')),
    -- Texto curto do giro no display (duas linhas de 16, FUN:57). NULL = "Entrada liberada" /
    -- "Saida liberada", pelo rótulo.
    display_text      TEXT    NULL CHECK (length(display_text) BETWEEN 1 AND 32),
    revision          INTEGER NOT NULL CHECK (revision >= 1),
    updated_by        TEXT    NOT NULL CHECK (length(trim(updated_by)) > 0),  -- nome digitado; não há login
    updated_at        TEXT    NOT NULL,                                       -- ISO-8601 UTC
    PRIMARY KEY (inner_number, origin),
    -- Função ou texto sem rótulo não fazem sentido: a regra existe inteira ou não existe.
    CHECK (counted_as IS NOT NULL OR (release_function IS NULL AND display_text IS NULL))
) STRICT;

-- Histórico só-INSERT, retrato de cada gravação de cada origem (padrão da 012). Escrito pelo
-- gatilho: não há caminho de escrita em turn_map_rule que escape dele.
CREATE TABLE turn_map_rule_history (
    inner_number      INTEGER NOT NULL,
    origin            TEXT    NOT NULL,
    revision          INTEGER NOT NULL,
    release_function  TEXT    NULL,
    counted_as        TEXT    NULL,
    display_text      TEXT    NULL,
    updated_by        TEXT    NOT NULL,
    updated_at        TEXT    NOT NULL,
    PRIMARY KEY (inner_number, origin, revision)
) STRICT;

CREATE TRIGGER turn_map_rule_comeca_na_revisao_1
BEFORE INSERT ON turn_map_rule
WHEN NEW.revision <> 1
BEGIN
    SELECT RAISE(ABORT, 'turn_map_rule: a primeira gravação é a revisão 1');
END;

CREATE TRIGGER turn_map_rule_revisao_anda_de_um_em_um
BEFORE UPDATE ON turn_map_rule
WHEN NEW.inner_number <> OLD.inner_number OR NEW.origin <> OLD.origin OR NEW.revision <> OLD.revision + 1
BEGIN
    SELECT RAISE(ABORT, 'turn_map_rule: cada gravação é a revisão seguinte, da mesma catraca e origem');
END;

CREATE TRIGGER turn_map_rule_nao_se_apaga
BEFORE DELETE ON turn_map_rule
BEGIN
    SELECT RAISE(ABORT, 'turn_map_rule: não se apaga; grave a regra nula para voltar ao padrão');
END;

CREATE TRIGGER turn_map_rule_historico_ao_inserir
AFTER INSERT ON turn_map_rule
BEGIN
    INSERT INTO turn_map_rule_history (
        inner_number, origin, revision, release_function, counted_as, display_text, updated_by, updated_at)
    VALUES (
        NEW.inner_number, NEW.origin, NEW.revision, NEW.release_function, NEW.counted_as, NEW.display_text,
        NEW.updated_by, NEW.updated_at);
END;

CREATE TRIGGER turn_map_rule_historico_ao_mudar
AFTER UPDATE ON turn_map_rule
BEGIN
    INSERT INTO turn_map_rule_history (
        inner_number, origin, revision, release_function, counted_as, display_text, updated_by, updated_at)
    VALUES (
        NEW.inner_number, NEW.origin, NEW.revision, NEW.release_function, NEW.counted_as, NEW.display_text,
        NEW.updated_by, NEW.updated_at);
END;

CREATE TRIGGER turn_map_rule_history_nao_muda
BEFORE UPDATE ON turn_map_rule_history
BEGIN
    SELECT RAISE(ABORT, 'turn_map_rule_history é auditoria: não muda');
END;

CREATE TRIGGER turn_map_rule_history_nao_se_apaga
BEFORE DELETE ON turn_map_rule_history
BEGIN
    SELECT RAISE(ABORT, 'turn_map_rule_history é auditoria: não se apaga');
END;

-- 2. Conferência de comissionamento (docs/21, NOVO-HIL-DIR-11): "para que lado o braço gira com
--    esta função NESTA instalação" é fato do mundo físico. Alguém libera, olha o braço e registra.
--    Só-INSERT: a última conferência de cada (catraca, função) é a que vale; nenhuma some.
CREATE TABLE turn_check (
    id                TEXT    NOT NULL PRIMARY KEY,   -- UUID v7
    inner_number      INTEGER NOT NULL CHECK (inner_number BETWEEN 1 AND 99),
    release_function  TEXT    NOT NULL
                      CHECK (release_function IN ('Entrada', 'EntradaInvertida', 'Saida', 'SaidaInvertida')),
    -- 'como_esperado': girou para o lado da seta do gêmeo; 'ao_contrario': para o outro.
    result            TEXT    NOT NULL CHECK (result IN ('como_esperado', 'ao_contrario')),
    checked_by        TEXT    NOT NULL CHECK (length(trim(checked_by)) > 0),
    checked_at        TEXT    NOT NULL
) STRICT;

CREATE INDEX ix_turn_check_catraca ON turn_check (inner_number, release_function, checked_at);

CREATE TRIGGER turn_check_nao_muda
BEFORE UPDATE ON turn_check
BEGIN
    SELECT RAISE(ABORT, 'turn_check é registro de ensaio: não muda');
END;

CREATE TRIGGER turn_check_nao_se_apaga
BEFORE DELETE ON turn_check
BEGIN
    SELECT RAISE(ABORT, 'turn_check é registro de ensaio: não se apaga');
END;

-- 3. Cada tentativa liberada guarda como o giro foi pedido e contado.
--    counted_as: o rótulo do mapa ('entrada'/'saida'). NULL = sem regra no mapa para aquela
--      origem (ou gravada antes desta migração): conta como entrada, como sempre.
--    release_function: a função que o laço chamou para liberar (o sentido físico pedido). NULL =
--      não liberou, ou gravada por quem não sabe (bancada, antes desta migração).
--    turn_complement: o Complemento bruto da origem 6 que confirmou o giro, como a catraca o
--      mandou. Talvez traga o sentido físico (T14, NOVO-HIL-DIR-08). Sem CHECK, de propósito:
--      valor da catraca é preservado íntegro (ADR-0018), e recusá-lo derrubaria a confirmação.
ALTER TABLE ticket_use_attempt ADD COLUMN counted_as TEXT NULL CHECK (counted_as IN ('entrada', 'saida'));
ALTER TABLE ticket_use_attempt ADD COLUMN release_function TEXT NULL
    CHECK (release_function IN ('Entrada', 'EntradaInvertida', 'Saida', 'SaidaInvertida'));
ALTER TABLE ticket_use_attempt ADD COLUMN turn_complement INTEGER NULL;
