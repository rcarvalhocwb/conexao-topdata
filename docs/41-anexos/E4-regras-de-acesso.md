> Relatório bruto de um especialista da auditoria linha a linha (docs/40), commit auditado 29df501.
> Os estados aqui são os do especialista, antes da revisão cruzada; o consolidado é o docs/41.

# E4 — Regras de acesso e domínio (commit 29df501)

Auditoria só de leitura. Não editei nada nem rodei build ou testes. Comandos usados: `git log`, `wc`, `grep`, `sed`, `cat` e Read.

## A) Achados

### Resumo
Não achei nenhum caminho em que o código atual libere por engano. Toda falha nega:
- **Base local com erro:** a decisão captura a exceção e nega com `FALHA_NA_BASE_LOCAL` (DecisorDeIngresso.cs:189-194).
- **Leitura sem código:** nega antes de consultar a base (DecisorDeIngresso.cs:167-170).
- **Status desconhecido no banco:** cai em `Bloqueado` (RepositorioDeIngressos.cs:1221-1225).
- **Mesmo ingresso em duas catracas ao mesmo tempo:** o consumo é um único `UPDATE` com todas as condições no `WHERE` (RepositorioDeIngressos.cs:1094-1119). Só uma linha afetada vence (`== 1`, :380).
- **QR duplicado:** o índice `ux_ticket_qr` é UNIQUE (003_ingressos_e_provedores.sql:47).
- **Relógio da catraca errado:** não afeta a decisão. Ela usa o relógio do PC (`_relogio.GetUtcNow()`, DecisorDeIngresso.cs:184), não `DeviceTime`.

Não há achado CRÍTICO nem ALTO. São 4 MÉDIO, 12 BAIXO e 4 INFORMATIVO.

---

**E4-1 | MÉDIO | DeviceStateMachine.cs:295 | DeviceStateMachine.BuildTable**
- **O que o código faz:** declara a transição `ValidarAcesso + TempoEsgotado → LiberarCatraca`. Ou seja, se a decisão estourar o prazo, a catraca libera.
- **Por que é problema:** é uma liberação sem decisão que fica latente na tabela. Hoje só um guarda no chamador impede que ela dispare: `PrazoEstourado` ignora `ValidarAcesso` (DevicePump.cs:1147). Qualquer código novo que dispare `TempoEsgotado` nesse estado libera o giro sem ingresso. O comentário do próprio arquivo (DevicePump.cs:1138-1142) reconhece o risco.
- **Como provar:** chamar `TryFire(TempoEsgotado)` com a máquina em `ValidarAcesso` leva a `LiberarCatraca`. O guarda tem teste: `PrazoDoGiroTests.Validar_acesso_sem_decisao_nunca_libera_pelo_prazo` (tests/HardwareInLoop/PrazoDoGiroTests.cs:230). A tabela em si não tem teste.
- **Estado:** CONFIRMADO no código, falta revisão cruzada do E3. A consequência hoje é HIPÓTESE, porque o guarda existe.
- **Correção mínima:** apontar a transição para `EnviarMsgAcessoNegado` e criar um teste que trave a tabela.

**E4-2 | MÉDIO | PerfisDeLeitura.cs:86 + DecisorDeIngresso.cs:137 | PerfisDeLeitura.DaLeitura / DecisorDeIngresso.Decidir**
- **O que o código faz:** a leitura na catraca usa sempre o perfil `raw`, que só tira espaços. Edge.Worker.X86/Program.cs:176 e :358 criam o decisor sem perfil. Já o cadastro usa o perfil do provedor, e `mifare-catraca4` completa zeros até 10 dígitos (PerfisDeLeitura.cs:51-54). Esse perfil pode ser escolhido no Configurador (JanelaDoAssistente.xaml.cs:86) e na sincronização com a nuvem (SincronizacaoComANuvem.cs:20-21).
- **Por que é problema:** se a catraca entregar menos de 10 dígitos (por exemplo `78234567`), o cartão cadastrado como `0078234567` não casa. O cartão válido é negado como `CREDENCIAL_DESCONHECIDA`, e o operador não tem pista da causa. O comentário em PerfisDeLeitura.cs:77 diz que o perfil da leitura "precisa ser o mesmo que o provedor usou", mas com `mifare-catraca4` no provedor ele não é.
- **Teste que mascara o problema:** `DecisorDeIngressoTests.Leitura_e_cadastro_do_mesmo_cartao_casam_com_cada_perfil` (tests/Unit/Ingressos/DecisorDeIngressoTests.cs:120-131) passa o perfil mifare também na leitura, coisa que a produção nunca faz.
- **Como provar:** provedor com `mifare-catraca4`, cartão `78234567` cadastrado e lido como `78234567` dá `Desconhecido`.
- **Estado:** CONFIRMADO no código. O formato que a TopFit 4 de fato entrega é NÃO VERIFICÁVEL AQUI (HIL-CARD).
- **Correção mínima:** até haver perfil por leitor, uma de duas: recusar ou alertar quando o provedor usa perfil diferente de `raw`; ou aplicar na leitura o mesmo `PadLeftTo` quando há um único perfil numérico ativo.

**E4-3 | MÉDIO | PreviaDaImportacao.cs:479-501 + PerfisDeLeitura.cs:51-54 | PreviaDaImportacao.AnalisarCodigo**
- **O que o código faz:** o filtro `ForaDoAlfabeto` (:796) aceita letras, e o perfil `mifare-catraca4` não exige só dígitos. Um identificador em hexadecimal, como `04A1B2C3`, vira `0004A1B2C3` e é aceito, só com um aviso de "completado com zeros" (:503-507).
- **Por que é problema:** a catraca 4 entrega Mifare em decimal ABA de 10 dígitos (PerfisDeLeitura.cs:39-42). O código em hexadecimal nunca vai casar, e uma planilha com milhares desses passa pela prévia como válida. Resultado: fila de cartões válidos negados na porta.
- **Como provar:** contexto com `MifareCatraca4` e CSV `codigo;tipo;situacao` com a linha `04A1B2C3;INTEIRA;ATIVO` dá `ClasseDaLinha.Novo`.
- **Estado:** CONFIRMADO.
- **Correção mínima:** perfil numérico (Mifare) exige `^[0-9]+$` na prévia e no `CredentialNormalization`, por exemplo com um parâmetro `SomenteDigitos`.

**E4-4 | MÉDIO | DecisorDeIngresso.cs:226-228 | DecisorDeIngresso.AoReceberEvento**
- **O que o código faz:** tira a tentativa de `_pendentes` antes de chamar `ConfirmarPassagemFisica`, e essa chamada não fica dentro de `try`. O caminho é `SessaoDeOperacao.Receber` (:574), chamado de `DevicePump` (:589), também sem `catch` local.
- **Por que é problema:** se a base estiver ocupada no instante da origem 6, a prova do giro se perde. A entrada vira "uso sem passagem" na prestação de contas e a exceção sobe pelo laço antes de disparar o gatilho `GiroConfirmado`.
- **Como provar:** um `ValidadorFalso` que lança em `ConfirmarPassagemFisica`. Depois da exceção, a pendência some e nada fica registrado. Não existe teste para isso.
- **Estado:** CONFIRMADO no domínio. O efeito no laço é HIPÓTESE (é área do E1).
- **Correção mínima:** confirmar primeiro e só remover a pendência depois do sucesso, ou capturar a exceção, registrar e reenfileirar a confirmação.

---

**BAIXO**
- **E4-5:** o relógio do PC não passa por checagem de plausibilidade. Janela e reuso dependem só dele (DecisorDeIngresso.cs:184; RepositorioDeIngressos.cs:1104-1105, 1114-1115). Se o relógio voltar, o reuso é negado mesmo com intervalo 0, porque `last_used_epoch + 0 <= now` falha. Nesse caso `Diagnosticar` informa `UsosEsgotados`, que é o motivo errado (:1239-1246, 1303-1306).
- **E4-6:** o limite exato da janela não tem teste. `valid_to >= $em` é inclusivo (:1105), mas a planilha só tem precisão de minuto (PreviaDaImportacao.cs:150), então "23:59" vale até 23:59:00,000. O teste existente usa ±1 h (IngressosDeVariosProvedoresTests.cs:225-240).
- **E4-7:** códigos que diferem só na caixa (`ab12` e `AB12`) são cartões distintos. Nenhum perfil converte para maiúsculas, e `MarcarRepetidos` compara de forma ordinal (PreviaDaImportacao.cs:640). Como o leitor de QR entrega a caixa ainda está A_CONFIRMAR (T25).
- **E4-8:** `ComandoDeCatraca.Criar` aceita `LiberarDoisSentidos` sem passar pela D5. A recusa só existe no serviço (EdgeControlService.cs:715), e o worker executa o que estiver na fila (DevicePump.cs:883). Não há defesa em profundidade: uma linha gravada direto em `operator_command` liberaria os dois sentidos.
- **E4-9:** `DeviceConfiguration.Validar` aceita `FuncaoDoAcionamento1` de 6 a 9, que inclui "entrada liberada" e "liberada nos dois sentidos" (DeviceConfiguration.cs:405-416). Hoje nenhuma camada sobrepõe esse campo (MontadorDaConfiguracao.cs:266-299), mas a validação deveria recusar 7 e 8 enquanto a D5 não for decidida.
- **E4-10:** o texto da mensagem temporária não é filtrado contra caracteres de controle nem acentos (ComandoDeCatraca.cs:236-241). O `MapaDeGiro.Validar` filtra os de controle (MapaDeGiro.cs:193).
- **E4-11:** a tabela `Antiga` não é monotônica: 6 dígitos dão 1.500 e 8 dígitos dão 2.250 (LimitesDeCapacidade.cs:67-70). O CSV não traz os valores por dígito (limites-de-capacidade.csv, linha 5), então o "teste de contrato" não tem com o que conferir.
- **E4-12:** uma leitura sem código não grava tentativa (DecisorDeIngresso.cs:167-170), e a negação fica fora da trilha `ticket_use_attempt`.
- **E4-13:** um ingresso `consumido` cujo `max_uses` aumenta no reenvio continua `consumido`, porque o `CASE` cai no `ELSE ticket.status` (RepositorioDeIngressos.cs:1020-1031). Área do E3.
- **E4-14:** `Decision.Tier` vem sempre fixo como `T1SemInternet` (DecisorDeIngresso.cs:212, 284), mesmo com a internet no ar.
- **E4-15:** vários códigos do catálogo nunca são emitidos: `AntiPassback`, `LotacaoAtingida`, `SetorNaoPermitido` e `UrnaCheia` (ReasonCodes.cs:23-37). Também o setor do ingresso não é conferido contra o portão: o campo existe em `ResultadoDoUso.Setor` mas nenhuma regra o usa.
- **E4-16:** com o teclado habilitado (origem 1, `EventOrigin.EhLeitura`, EventOrigin.cs:152), um código digitado consome um ingresso de site (provedor sem urna). Hoje o teclado vem desligado (`TecladoHabilitado = false`, MontadorDaConfiguracao.cs:55).

**INFORMATIVO**
- **E4-17:** `TentativaEspelhada.Codigo` leva o código normalizado em claro para a nuvem (TentativaEspelhada.cs:36). Repassar ao E8.
- **E4-18:** `HoraDeBrasilia` tem reserva de UTC−3 fixo, correta enquanto não houver horário de verão. Com horário de verão, depende do banco de fusos do Windows (HoraDeBrasilia.cs:30-43).
- **E4-19:** a prévia da importação não tem chamador em `src/`, só os testes. A confirmação e gravação (B.4) não existem e `usos_maximos` vazio ("sem limite") não tem para onde ir.
- **E4-20:** o tempo de relé 0 é aceito por `DeviceConfiguration.Validar`, mas as camadas de origem recusam com a faixa 1–50 (ConfiguracoesDaBorda.cs:138; ConfiguracoesDasCatracas.cs:204).

## B) Cobertura (todos os arquivos lidos inteiros)

| Arquivo | Linhas | Lido inteiro | Funções | Achados |
|---|---|---|---|---|
| Domain/Credentials/CredentialNormalization.cs | 90 | sim | 3 | E4-3 |
| Domain/Credentials/CredentialValue.cs | 88 | sim | 7 | 0 |
| Domain/Credentials/PerfisDeLeitura.cs | 112 | sim | 4 | E4-2, E4-3 |
| Domain/Credentials/ImpressaoDeCodigo.cs | 74 | sim | 3 | 0 |
| Domain/Ticketing/Ingresso.cs | 172 | sim | 1 | E4-15 |
| Domain/Ticketing/Ingestao.cs | 30 | sim | 1 | 0 |
| Domain/Ticketing/TentativaEspelhada.cs | 79 | sim | 2 | E4-17 |
| Domain/Tempo/HoraDeBrasilia.cs | 44 | sim | 3 | E4-18 |
| Domain/Access/Decision.cs | 85 | sim | 4 | 0 |
| Domain/Access/ReasonCodes.cs | 67 | sim | 0 | E4-15 |
| Domain/Devices/DeviceEvent.cs | 86 | sim | 3 | 0 |
| Domain/Devices/DeviceState.cs | 91 | sim | 0 | 0 |
| Domain/Devices/EventOrigin.cs | 159 | sim | 5 | E4-16 |
| Application/Ingressos/DecisorDeIngresso.cs | 287 | sim | 9 | E4-2, E4-4, E4-12, E4-14 |
| Application/Devices/ComandoDeCatraca.cs | 305 | sim | 10 | E4-8, E4-10 |
| Application/Devices/DeviceConfiguration.cs | 693 | sim | 11 | E4-9, E4-20 |
| Application/Devices/MontadorDaConfiguracao.cs | 301 | sim | 3 | 0 |
| Application/Devices/CamposDaConfiguracao.cs | 147 | sim | 8 | 0 |
| Application/Devices/ConfiguracaoComRecuo.cs | 146 | sim | 4 | 0 |
| Application/Devices/VersaoDaConfiguracao.cs | 196 | sim | 5 | 0 |
| Application/Devices/MapaDeGiro.cs | 212 | sim | 8 | 0 |
| Application/Devices/DeviceStateMachine.cs | 344 | sim | 8 | E4-1 |
| Application/Devices/DeviceTrigger.cs | 114 | sim | 0 | 0 |
| Application/Devices/BilheteColetado.cs | 60 | sim | 1 | 0 |
| Application/Devices/SequenciaOficialDeConexao.cs | 68 | sim | 1 | 0 |
| Application/Devices/RetornosDocumentados.cs | 79 | sim | 2 | 0 |
| Application/Devices/LimitesDeCapacidade.cs | 177 | sim | 5 | E4-11 |
| Application/Devices/ITopdataInnerAdapter.cs | 311 | sim | 6 lógicas + 17 do contrato | 0 |
| Importacao/Apoio.cs | 73 | sim | 3 | 0 |
| Importacao/Planilha.cs | 84 | sim | 5 | 0 |
| Importacao/LeitorDeCsv.cs | 216 | sim | 2 | 0 |
| Importacao/LeitorDeXlsx.cs | 504 | sim | 13 | 0 |
| Importacao/PreviaDaImportacao.cs | 813 | sim | 16 | E4-3, E4-6, E4-7, E4-19 |

São 33 arquivos e 6.307 linhas, 100% lidos. Fora dos meus projetos, li para contexto (não por inteiro):
- RepositorioDeIngressos.cs: 260-500 e 960-1310 (`TentarUsar`, `ConsumirUmUso`, `Estado`, `Diagnosticar`, `Gravar`, `Ingerir`).
- DevicePump.cs: 575-600, 770-910 e 1135-1190.
- EdgeControlService.cs: 700-787.
- SessaoDeOperacao.cs: 365-395 e 520-575.

Na importação, conferi o que o pedido citava:
- **Zeros à esquerda:** preservados. O CSV é sempre texto, e célula Número ou notação científica no .xlsx é recusada (:447-477).
- **Fórmula:** recusada (:452).
- **Duplicados:** viram erro nas duas linhas (:637-649).
- **Encoding:** só UTF-8, e UTF-16 é recusado com instrução de como salvar (LeitorDeCsv.cs:42-60).
- **Volume:** aviso acima de 100 mil linhas, recusa acima de 200 mil, 20 MB por arquivo e 100 MB descompactados.
- **Segurança do pacote:** XML sem DTD, macro e vínculo externo são recusados.

## C) Veredito das capacidades (docs/40 §7)

- **Capacidade 3, ler e normalizar o código: PARCIAL.**
  - Provado em CI: a normalização no domínio preserva zeros e só tira espaços. Testes: `CredentialValueTests.Preserva_zeros_a_esquerda`, `PerfisDeLeituraTests.Nenhum_perfil_remove_zeros_a_esquerda` e `DecisorDeIngressoTests.Sem_perfil_informado_a_leitura_e_comparada_como_sempre_foi`. Todos falhariam se a normalização tirasse zeros.
  - Falta o perfil por leitor (E4-2).
  - Falta verificar se o código é só de dígitos (E4-3).
  - QR com letras pela recepção numérica está A_CONFIRMAR (DeviceConfiguration.cs:515-520, T25).
  - Formato real do Mifare e do QR: NÃO VERIFICÁVEL AQUI, precisa de bancada.
- **Capacidade 4, decidir: PROVADO EM CI, com lacunas de valor-limite.** Testes, que falhariam se a regra quebrasse porque conferem `Motivo` e `Liberou`:
  - janela: `IngressosDeVariosProvedoresTests.Fora_da_janela_de_validade_e_negado`;
  - provedor desabilitado: `Provedor_desabilitado_para_de_validar_sem_apagar_nada`;
  - reuso: `BilheteriaLocalTests` :157 e :258;
  - urna: `Leitor_desconhecido_e_recusado_quando_o_provedor_exige_urna`;
  - tipo desativado e corrida: `CicloDoIngressoTests.Tipo_inativo_nega_com_TipoInativo_e_nao_consome` e `Corrida_de_catracas_com_ingresso_de_tipo_ativo_ainda_tem_um_vencedor` (8 em paralelo, exatamente 1 libera);
  - base que falha: `DecisorDeIngressoTests.Base_que_falha_nega_em_vez_de_liberar`;
  - prazo da decisão: `PrazoDoGiroTests.Validar_acesso_sem_decisao_nunca_libera_pelo_prazo`.
  - Lacunas: o instante exato do fim da janela, o relógio do PC voltando (E4-5, E4-6) e a corrida entre processos diferentes (a de CI é no mesmo processo).
- **Capacidade 12, comandos (parte de domínio): PARCIAL.**
  - Provado em CI: validação, prazo e motivo obrigatório (`ComandosNovosTests`), além da recusa da D5 com ou sem a chave ligada (`Dois_sentidos_e_recusado_pela_d5_com_ou_sem_a_chave`).
  - Dois sentidos: BLOQUEADO POR DECISÃO, a D5 é do dono do produto.
  - Liberar saída e bips: atrás de chave técnica até HIL-DIR-04 e INT-UX-03.
  - Nada foi provado em hardware.
- **Importação de cartões: PARCIAL.**
  - A prévia tem 36 testes em `PreviaDaImportacaoTests`, inclusive `Cem_mil_linhas_de_csv_com_tempo_medido`, `Zeros_a_esquerda_sao_preservados_no_csv` e `..._no_xlsx...`, `Codigo_de_formula_e_recusado` e `Codigo_repetido_no_arquivo_e_erro_nas_duas_linhas`.
  - Não há confirmação nem gravação (B.4), nem chamador em `src/` (E4-19).
  - Falta a regra de só dígitos para Mifare (E4-3).

## D) Perguntas de decisão

1. **Perfil de leitura enquanto não há parametrização por catraca (E4-2):**
   - (a) manter `raw` e proibir provedor com perfil diferente de `raw`;
   - (b) aplicar na leitura o completar com zeros do perfil Mifare quando ele for o único perfil numérico ativo;
   - (c) esperar a bancada.
   - **Recomendada: (a).** Não arrisca casamento errado e torna visível, ainda no cadastro, uma recusa que hoje só aparece na porta.
2. **O que fazer com `ValidarAcesso + TempoEsgotado` na tabela (E4-1):**
   - (a) apontar para negação;
   - (b) remover a transição;
   - (c) manter e confiar no guarda do laço.
   - **Recomendada: (a).** A tabela passa a ser segura por si mesma, sem depender de quem a chama, e continua testável.
3. **Código Mifare com letras na importação (E4-3):**
   - (a) recusar a linha;
   - (b) converter hexadecimal para decimal;
   - (c) aceitar com aviso, como hoje.
   - **Recomendada: (a).** A conversão depende da ordem dos bytes, que ainda não foi confirmada com a Topdata, e aceitar com aviso deixa o erro chegar à porta.
