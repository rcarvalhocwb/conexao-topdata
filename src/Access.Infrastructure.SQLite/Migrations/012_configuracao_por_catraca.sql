-- Migração 012 — configuração por catraca (Etapa A.3 do docs/35; docs/34 §4.1, "Para a A.3").
--
-- Uma linha por inner_number com o que ESTA catraca sobrepõe ao evento. Coluna nula = herda
-- do evento (edge_setting), que herda da fábrica (MontadorDaConfiguracao). Só entram os campos
-- que dependem do equipamento físico. Ficam de fora, de propósito: as chaves técnicas (uma
-- bancada liga para todas), RegistrarAcessoNegado, DataHoraNoEventoOnLine e TipoDeLista (do
-- evento até o ensaio dizer que variam por equipamento) e o cartão master (nunca em claro).
--
-- As faixas são as de DeviceConfiguration.Validar(); o tempo do relé 1 e a mensagem usam as do
-- evento (ConfiguracaoDaOperacao.Validar), que são mais estreitas e valem para o mesmo campo.
-- O inner vai de 1 a 99 (docs/34 §4.2, regra 12; T9).
CREATE TABLE device_config (
    inner_number            INTEGER NOT NULL PRIMARY KEY CHECK (inner_number BETWEEN 1 AND 99),

    reader_type             INTEGER NULL CHECK (reader_type BETWEEN 0 AND 8),         -- EI-013, FUN:14
    reader1_operation       INTEGER NULL CHECK (reader1_operation BETWEEN 0 AND 4),   -- EI-014, FUN:15
    reader2_operation       INTEGER NULL CHECK (reader2_operation BETWEEN 0 AND 4),   -- EI-015, FUN:16 (urna)
    relay1_seconds          INTEGER NULL CHECK (relay1_seconds BETWEEN 1 AND 50),     -- EI-016
    release_function        TEXT    NULL                                              -- FuncaoDeLiberacao
                            CHECK (release_function IN ('Entrada', 'EntradaInvertida', 'Saida', 'SaidaInvertida')),
    default_message         TEXT    NULL CHECK (length(default_message) BETWEEN 1 AND 32), -- FUN:57

    -- ConfigurarWiegandDoisLeitores (EI-024, FUN:25): o par vai junto ou não vai.
    wiegand_enabled         INTEGER NULL CHECK (wiegand_enabled IN (0, 1)),
    wiegand_show_message    INTEGER NULL CHECK (wiegand_show_message IN (0, 1)),

    -- EnviarFormasEntradasOnLine (EI-032, FUN:33): os cinco vão juntos ou nenhum. Só a faixa de
    -- FormaEntrada é documentada; as outras quatro são byte (T26).
    entry_keypad_digits     INTEGER NULL CHECK (entry_keypad_digits BETWEEN 0 AND 255),
    entry_keypad_echo       INTEGER NULL CHECK (entry_keypad_echo BETWEEN 0 AND 255),
    entry_form              INTEGER NULL CHECK (entry_form BETWEEN 0 AND 7
                                               OR entry_form BETWEEN 10 AND 14
                                               OR entry_form BETWEEN 100 AND 105),
    entry_keypad_time       INTEGER NULL CHECK (entry_keypad_time BETWEEN 0 AND 255),
    entry_cursor_position   INTEGER NULL CHECK (entry_cursor_position BETWEEN 0 AND 255),

    revision                INTEGER NOT NULL CHECK (revision >= 1),
    updated_by              TEXT    NOT NULL CHECK (length(trim(updated_by)) > 0),  -- nome digitado; não há login (docs/27 §11)
    updated_at              TEXT    NOT NULL,                                       -- ISO-8601 UTC

    CHECK ((wiegand_enabled IS NULL) = (wiegand_show_message IS NULL)),
    CHECK ((entry_keypad_digits IS NULL) = (entry_keypad_echo IS NULL)
       AND (entry_keypad_echo IS NULL) = (entry_form IS NULL)
       AND (entry_form IS NULL) = (entry_keypad_time IS NULL)
       AND (entry_keypad_time IS NULL) = (entry_cursor_position IS NULL))
) STRICT;

-- Histórico só-INSERT: o RETRATO COMPLETO da linha depois de cada gravação, numerado pela
-- revisão. Retrato, e não "campo, valor antigo, valor novo": a configuração de uma catraca num
-- instante é uma linha só, sem reconstrução; "limpou o campo" (voltou a herdar) fica gravado
-- como NULL explícito, sem confundir com "não mexeu"; e o antigo de cada campo é a revisão
-- anterior. Quem grava o histórico é o gatilho, não o repositório: não há caminho de escrita
-- em device_config que escape dele.
CREATE TABLE device_config_history (
    inner_number            INTEGER NOT NULL,
    revision                INTEGER NOT NULL,
    reader_type             INTEGER NULL,
    reader1_operation       INTEGER NULL,
    reader2_operation       INTEGER NULL,
    relay1_seconds          INTEGER NULL,
    release_function        TEXT    NULL,
    default_message         TEXT    NULL,
    wiegand_enabled         INTEGER NULL,
    wiegand_show_message    INTEGER NULL,
    entry_keypad_digits     INTEGER NULL,
    entry_keypad_echo       INTEGER NULL,
    entry_form              INTEGER NULL,
    entry_keypad_time       INTEGER NULL,
    entry_cursor_position   INTEGER NULL,
    updated_by              TEXT    NOT NULL,
    updated_at              TEXT    NOT NULL,
    PRIMARY KEY (inner_number, revision)
) STRICT;

-- A primeira gravação de uma catraca é a revisão 1; cada uma depois, a seguinte. Assim a
-- chave do histórico nunca colide e nenhuma revisão some.
CREATE TRIGGER device_config_comeca_na_revisao_1
BEFORE INSERT ON device_config
WHEN NEW.revision <> 1
BEGIN
    SELECT RAISE(ABORT, 'device_config: a primeira gravação é a revisão 1');
END;

CREATE TRIGGER device_config_revisao_anda_de_um_em_um
BEFORE UPDATE ON device_config
WHEN NEW.inner_number <> OLD.inner_number OR NEW.revision <> OLD.revision + 1
BEGIN
    SELECT RAISE(ABORT, 'device_config: cada gravação é a revisão seguinte, da mesma catraca');
END;

-- Apagar a linha não deixaria rastro. Para voltar a herdar tudo, grava-se tudo nulo.
CREATE TRIGGER device_config_nao_se_apaga
BEFORE DELETE ON device_config
BEGIN
    SELECT RAISE(ABORT, 'device_config: não se apaga; grave os campos nulos para herdar do evento');
END;

CREATE TRIGGER device_config_historico_ao_inserir
AFTER INSERT ON device_config
BEGIN
    INSERT INTO device_config_history (
        inner_number, revision, reader_type, reader1_operation, reader2_operation, relay1_seconds,
        release_function, default_message, wiegand_enabled, wiegand_show_message,
        entry_keypad_digits, entry_keypad_echo, entry_form, entry_keypad_time, entry_cursor_position,
        updated_by, updated_at)
    VALUES (
        NEW.inner_number, NEW.revision, NEW.reader_type, NEW.reader1_operation, NEW.reader2_operation, NEW.relay1_seconds,
        NEW.release_function, NEW.default_message, NEW.wiegand_enabled, NEW.wiegand_show_message,
        NEW.entry_keypad_digits, NEW.entry_keypad_echo, NEW.entry_form, NEW.entry_keypad_time, NEW.entry_cursor_position,
        NEW.updated_by, NEW.updated_at);
END;

CREATE TRIGGER device_config_historico_ao_mudar
AFTER UPDATE ON device_config
BEGIN
    INSERT INTO device_config_history (
        inner_number, revision, reader_type, reader1_operation, reader2_operation, relay1_seconds,
        release_function, default_message, wiegand_enabled, wiegand_show_message,
        entry_keypad_digits, entry_keypad_echo, entry_form, entry_keypad_time, entry_cursor_position,
        updated_by, updated_at)
    VALUES (
        NEW.inner_number, NEW.revision, NEW.reader_type, NEW.reader1_operation, NEW.reader2_operation, NEW.relay1_seconds,
        NEW.release_function, NEW.default_message, NEW.wiegand_enabled, NEW.wiegand_show_message,
        NEW.entry_keypad_digits, NEW.entry_keypad_echo, NEW.entry_form, NEW.entry_keypad_time, NEW.entry_cursor_position,
        NEW.updated_by, NEW.updated_at);
END;

CREATE TRIGGER device_config_history_nao_muda
BEFORE UPDATE ON device_config_history
BEGIN
    SELECT RAISE(ABORT, 'device_config_history é auditoria: não muda');
END;

CREATE TRIGGER device_config_history_nao_se_apaga
BEFORE DELETE ON device_config_history
BEGIN
    SELECT RAISE(ABORT, 'device_config_history é auditoria: não se apaga');
END;
