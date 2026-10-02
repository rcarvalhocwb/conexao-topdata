-- Migração 016 — de que partida do serviço vem a situação de cada catraca, e se é simulação.
-- Uma migração nova, e não uma alteração da 006/008/013: migração publicada nunca é editada.
--
-- Defeito relatado pelo dono do produto (01/10): com o simulador desligado e nenhuma catraca
-- na rede, o painel mostrava três catracas "Atendendo", com o firmware 4.2.0 do simulador.
-- Causa: workers de uma partida anterior do serviço, que morreu sem encerrá-los, continuaram
-- gravando device_status fresco na mesma base, e o serviço novo, em modo real, acreditou
-- neles (ADR-0024: a base é o canal entre worker e serviço). Ver docs/29 §1A e docs/21 §6H.
--
-- session_id: o identificador que o serviço gerou na partida e passou ao worker
--   (--sessao). O serviço só acredita na linha da partida dele; linha de outra partida conta
--   como "sem notícia" e nunca como "Atendendo".
-- simulated: 1 = a situação veio de uma catraca simulada (modo simulação); 0 = do worker
--   que fala com a catraca de verdade. O painel mostra o selo "Simulação" por catraca.
--
-- NULL nas duas = linha gravada antes desta migração, ou por quem não foi subido pelo serviço
-- (bancada, ferramentas, testes). Não é "sessão vazia" nem "real": é desconhecido, e o serviço
-- não acredita nela.
--
-- Anuláveis e sem DEFAULT: as linhas que já existem ficam NULL, que é a verdade sobre elas.
ALTER TABLE device_status ADD COLUMN session_id TEXT    NULL
    CHECK (session_id IS NULL OR length(session_id) BETWEEN 1 AND 64);
ALTER TABLE device_status ADD COLUMN simulated  INTEGER NULL
    CHECK (simulated IS NULL OR simulated IN (0, 1));
