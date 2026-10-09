# 01 — Engenheiro de integração Topdata: 100% da catraca, sinais escondidos, relés e composições

> Estudo de inovação (Rayzer XAcess), 2026-10-01, sobre a branch atual (Etapa 0, A.1–A.9, B.1–B.3;
> último commit `23c3beb`). **Nada no repositório foi alterado.**
>
> **Regra de leitura.** Toda afirmação sobre a catraca cita `arquivo:linha` do repositório ou uma
> página pública (URL completa, lida em 2026-10-01). Selos:
> `FONTE_PRIMARIA` (matriz FUN/INV, manual ou SDK **registrados no repo**) · `PUBLICO_TOPDATA` (página
> de suporte pública da Topdata, **fora do repositório**, não é manual nem SDK; vale para Catraca 4 /
> Fit 4 / Box 4 e é **a confirmar para a TopFit 4 de bancada**) · `PUBLICO_INTEGRADOR` (central de ajuda
> de integrador ou relato de terceiro — Next Fit, Pacto etc. —, a fonte mais fraca: só gera pergunta e
> ensaio, nunca regra) ·
> `ASSINATURA_SDK` (só a assinatura é conhecida) · `INFERIDO` (dedução minha, nunca vira código
> ligado) · `A_CONFIRMAR_COM_TOPDATA`. Nenhum SDK, DLL ou PDF da Topdata foi lido.
>
> Abreviações: `FUN` = `docs/compatibility-matrix/funcoes-easyinner.csv`; `INV` =
> `docs/compatibility-matrix/inventario-completo-dll.csv`; `ORI` = `.../origens-evento.csv`; `BIL` =
> `.../tipos-bilhete.csv`; `PUMP` = `src/Edge.Worker/DevicePump.cs`; `FSM` =
> `src/Access.Application/Devices/DeviceStateMachine.cs`; `ADP` =
> `src/Topdata.EasyInner.Adapter/TopdataInnerAdapter.cs`; `ITA` =
> `src/Access.Application/Devices/ITopdataInnerAdapter.cs`; `CFG` =
> `src/Access.Application/Devices/DeviceConfiguration.cs`; `DEC` =
> `src/Access.Application/Ingressos/DecisorDeIngresso.cs`; `A01` = `docs/34-anexos/01-engenheiro-topdata.md`.
> Linhas de CSV = linha física (1 = cabeçalho).
>
> Páginas públicas lidas nesta sessão (citadas pela sigla):
> - **[OP4]** https://suporte.topdata.com.br/suporte/operacoes-nas-catracas-4/
> - **[ESP-FIT4]** https://suporte.topdata.com.br/suporte/especificacoes-tecnicas-catraca-fit-4/
> - **[ESP-BOX4]** https://suporte.topdata.com.br/suporte/especificacoes-tecnicas-catraca-box-4/
> - **[URNA-RELE]** https://suporte.topdata.com.br/suporte/coletor-urna-com-o-acionamento-muito-rapido-do-rele/
> - **[INCENDIO]** https://suporte.topdata.com.br/suporte/integracao-de-catraca-com-alarme-de-incendio/
> - **[RELES-INNER]** https://suporte.topdata.com.br/suporte/os-produtos-na-linha-inner-possui-quantos-reles-para-acionamento/
> - **[CONTADOR]** https://suporte.topdata.com.br/suporte/o-contador-faz-contagem-de-giros-mesmo-sem-a-passagem-do-cartao-no-leitor/
> - **[MEM-CHEIA]** https://suporte.topdata.com.br/suporte/catraca-apresentando-mensagem-memoria-cheia/
> - **[GIRO-PESADO]** https://suporte.topdata.com.br/suporte/catraca-com-braco-pesado-travando-e-com-dificuldade-no-giro/
> - **[REINICIA-GIRO]** https://suporte.topdata.com.br/suporte/catraca-reiniciando-ao-realizar-o-giro/
> - **[NAO-RECOLHIDO]** https://suporte.topdata.com.br/suporte/catraca-apresenta-a-mensagem-cartao-nao-foi-recolhido-mas-o-cartao-caiu-na-urna/
> - **[TORNIQUETE]** https://suporte.topdata.com.br/suporte/instalacao-do-inner-acesso-2-em-torniquetes/
> - **[FAQ-BOX]** https://suporte.topdata.com.br/categorias/catraca-box-duvidas/page/3/ (lista das
>   dúvidas/reclamações recorrentes: "Falha na placa da catraca", "giro pesado ou travando",
>   "reiniciando ao realizar o giro", "braço articulado não fica preso", "Cartão não foi recolhido mas
>   caiu na urna", "acionamento muito rápido do relé", "Memória Cheia", "contador conta giro sem
>   cartão").
>
> Fontes públicas trazidas pelo relatório irmão `03-produto-demo-qa.md` (§3.2–3.3, perguntas T36–T39),
> incorporadas aqui com o selo correto (§3.3):
> - **[SENTIDO-WS]** https://suporte.topdata.com.br/suporte/e-possivel-configurar-o-sentido-da-catraca-4-atraves-do-web-server/ (PUBLICO_TOPDATA)
> - **[CONTADOR-MASTER]** https://suporte.topdata.com.br/suporte/como-acessar-o-contador-de-giros-da-catracas-4/ (PUBLICO_TOPDATA)
> - **[OFFLINE-LIVRE]** https://ajuda.nextfit.com.br/support/solutions/articles/69000854456-topdata-catraca-n%C3%A3o-est%C3%A1-travando-para-nenhum-lado (PUBLICO_INTEGRADOR)
> - **[NAO-BLOQUEIA]** https://suporte.topdata.com.br/suporte/catraca-nao-bloqueia-o-giro/ (PUBLICO_TOPDATA, citada no 03 §3.2 R4)
>
> **Numeração das perguntas à Topdata.** T1–T35 são do docs/34 §10; **T36–T39 são do 03** (sentido do
> WebServer, "Buffer: Para/Segue", off-line liberando qualquer entrada, contador por sentido); as
> deste relatório começam em **T40** (§8).

---

## 0. Resumo em uma página

**O que a catraca entrega e nós jogamos fora.** O laço já recebe tudo o que a catraca manda
(`PUMP:549` entrega todo evento a `aoReceberEvento`), mas a operação **não grava** `raw_event`,
`physical_passage` nem `device_state_transition` (docs/34-estudo-modulo-catraca.md:98-100): o que não
é leitura vira uma linha de texto no registro (`src/Edge.Worker/Operacao/SessaoDeOperacao.cs:481-485`)
e some. Três exemplos que mudam a operação:

1. **Giro sem liberação é descartado em silêncio.** Origem 6 chegando em `Polling` dispara
   `GiroConfirmado`, que não tem transição a partir de `Polling` (`FSM:200-215`), e o decisor só faz
   algo se houver tentativa pendente (`DEC:172-181`). A Topdata confirma que o equipamento **conta
   giro mesmo sem cartão** ([CONTADOR]). Isso é o sinal de carona/forçamento/botoeira/master que
   ninguém olha.
2. **`MonitoraGiroCatraca` não tem prazo efetivo (achado novo, F9).** O prazo de 8 s existe na tabela
   (`FSM:48`) e a transição `TempoEsgotado` também (`FSM:235`), mas **nenhum ponto do worker dispara
   `TempoEsgotado`** (só a definição em `src/Access.Application/Devices/DeviceTrigger.cs:82`; nenhum
   `TryFire(TempoEsgotado)` em `src/Edge.Worker`). Se a origem 5 não vier — e isso é
   `A_CONFIRMAR` no próprio código (`PUMP:909-910`) —, a pista fica em `MonitoraGiroCatraca`, sem
   rearmar o leitor, e leituras novas não têm transição (`FSM:232-236`): **a catraca para de atender
   sem erro**. Dormente; acorda na bancada.
3. **As métricas já foram desenhadas e nunca ligadas.** `MetricasDoEdge` define passagens
   confirmadas, autorizações sem giro, retornos nativos, latência de chamada
   (`src/Shared.Observability/MetricasDoEdge.cs:22-56`), mas **nenhum projeto as consome** (grep:
   só a própria definição). `AdapterResult.Elapsed` mede cada chamada (`ITA:101`, `ADP:581-587`) e
   ninguém agrega.

**Relés.** O relé 1 é o giro e não se toca (FUN:17; [OP4]). O relé 2, **em catraca sem urna, está
livre e a própria Topdata manda usá-lo para "dispositivo sinalizador externo como uma lâmpada, uma
campainha"** ([OP4]); a saída de acionamento externo da Fit 4 é **12 Vcc, até 1 A** ([ESP-FIT4]). Com
`AcionarRele2` (EI-047, já na costura, `ADP:377-378`, nunca chamado) e a temporização de
`ConfigurarAcionamento2` (EI-017, 0–50 s) dá para: sinaleiro de pista, aviso discreto de giro órfão ao
orientador, revista sorteada **on-line** (a nativa é só off-line, [OP4]), pulso para câmera/NVR no
giro e "homem-morto" de pista (contato que cai sozinho se o worker morrer). O risco central, que
ninguém tinha visto: **a origem 5 é "tempo de acionamento de um relé expirou" sem dizer qual**
(ORI:6); um pulso do relé 2 durante `MonitoraGiroCatraca` seria lido como fim da janela do giro
(`PUMP:557`, `DEC:183-188`). Por isso todo uso do relé 2 só sai em `Polling` e só liga depois do
ensaio que separa as duas origens 5 (NOVO-HIL-REL-06).

**Sentido do giro (para o "Mapa de giro").** A catraca **sabe** o sentido físico: o contador de giros
é por sentido "Anti-horário-AH e Horário-H" ([ESP-FIT4], [ESP-BOX4]). Mas o evento on-line documentado
no repositório **não traz o sentido**: a origem 6 é "giro concluído" (ORI:7) e o `Complemento` não tem
semântica documentada (docs/11-capacidades-do-sdk.md:67; `src/Access.Domain/Devices/DeviceEvent.cs:40-41`;
T14). Hoje o sentido só pode ser **atribuído** pela função de liberação que precedeu o giro
(EI-041..044), e é indeterminado depois de `LiberarCatracaDoisSentidos` (EI-045) e em giro sem
liberação. Atenção: segundo página pública da Topdata (fora do repo), o WebServer também define a
entrada como "Direita/Esquerda" ([SENTIDO-WS], T36) — dois lugares mexem no sentido físico. Detalhe no §6.

**Cobertura honesta.** Das 130 funções não biométricas com assinatura, **22 (17%) rodam na operação**,
**+16 estão implementadas atrás de chave desligada** e **+2 estão na costura sem chamador** → 40 (31%)
no código. Contra as 48 funções da matriz FUN aplicáveis à Linha 4: **46% ligadas, 79% no código, 83%
na costura**. Das 17 origens de evento não biométricas documentadas, **5 (29%) têm comportamento**
(2, 3, 21, 5, 6); as outras 12 só viram texto. §3.

**Top 10 para implementar já** (detalhe no §9): (1) prazo real de `MonitoraGiroCatraca`;
(2) caderno de sinais da catraca (gravar o que já chega); (3) sentinela de giro órfão; (4) detector de
"liberou e não girou" em série (catraca travada / sentido trocado); (5) saúde do giro por pista;
(6) display orientador por motivo; (7) check-up pré-abertura; (8) impressão digital do equipamento;
(9) saúde da comunicação; (10) correlação multi-pista (evacuação/alarme/queda de energia).

---

## 1. Achados que mudam algo antes de qualquer inovação

| # | Achado | Evidência | Situação | O que fazer |
|---|---|---|---|---|
| F9 | `MonitoraGiroCatraca` sem prazo efetivo: `TempoEsgotado` nunca é disparado; sem origem 5 a pista fica presa e ignora leituras | `FSM:48`, `FSM:232-236`; `DeviceTrigger.cs:82`; nenhum disparo em `src/Edge.Worker` (grep); `PUMP:909-910` ("a saída de MonitoraGiro depende da origem 5") | dormente até a bancada (HIL-EVT-01, SIM-URNA-02 só cobre o caso com origem 5) | composição C1 (§7) |
| F10 | Giro em `Polling` (origem 6 sem liberação nossa) é perdido: sem transição, sem contagem, sem gravação | `PUMP:556` → `FSM:200-215` (sem `GiroConfirmado` em `Polling`); `FSM:114-118` (`TryFire` devolve `false` em silêncio); `DEC:172-181` | **ativo** (qualquer botoeira/master/forçamento hoje) | C3 |
| F11 | Leitura chegando durante `MonitoraGiroCatraca` é perdida (sem transição `EventoRecebido`) e a pessoa não recebe resposta | `FSM:232-236`; `PUMP:558` | ativo se a catraca mandar leitura com o giro liberado (`A_CONFIRMAR`) | sinal S9 |
| F12 | `MetricasDoEdge` definido e não consumido; `AdapterResult.Elapsed` medido e não agregado; `ErrosDeRecepcao` não vai para a situação publicada | `MetricasDoEdge.cs:22-56`; `ITA:101`; `SessaoDeOperacao.cs:11-25` (sem o contador) | ativo (cegueira) | C9 |
| F13 | A origem 5 não diz **qual** relé expirou; qualquer uso do relé 2 colide com a janela do giro | ORI:6 ("Tempo de acionamento de um rele expirou"); `PUMP:557`; `DEC:183-188` | dormente (relé 2 = 0 hoje, `src/Access.Application/Devices/MontadorDaConfiguracao.cs:52-53`) | §5.2, NOVO-HIL-REL-06 |
| F14 | A "função default" do leitor de proximidade **prevalece** sobre leitor e acionamento quando está em Função 0–9 — e nós não enviamos `DefinirFuncaoDefaultLeitoresProximidade` (EI-022), logo vale o padrão da DLL | [OP4] ("…quando estiverem configuradas com 'Função' compreendidas entre 'Função 0' e 'Função 9', prevalecem sobre as configurações do 'Leitor' e 'Acionamento'"); FUN:23; ADP não chama (só na costura gerada, A01:224) | ativo e desconhecido | pergunta T44; ensaio INT-CFG-06 vira prioridade |
| F15 | A memória cheia tem modo **"Para" ou "Segue"** escolhido localmente ("Buffer"); "Para" mostra "Memória Cheia" no display (PUBLICO_TOPDATA, página de suporte fora do repo; o 03 a levantou como T37) | [MEM-CHEIA]; docs/34:63-65 só conhece o "circular" | contradiz em parte a premissa "circular" do docs/34 | `AvisarQuandoMemoriaCheia` (INV:14) talvez seja esse ajuste — `INFERIDO`; T34 + **T37**, ensaio **NOVO-INT-REC-08** (do 03). Para evento, "Para" pode ser pior (a catraca deixa de registrar ou de liberar?) |

---

## 2. Método

- Catálogo: FUN (58 linhas, semântica do manual/SDK), INV (265 exportações; 229 com assinatura; 99
  biométricas com assinatura → **130 não biométricas com assinatura**), ORI (27 linhas), BIL (17).
- Uso: costura `src/Topdata.EasyInner.Interop/IEasyInnerNative.cs` (40 funções), chamadas do `ADP`
  e do `PUMP`, chaves técnicas em `edge_setting` (`ChavesDosComandos.cs`, `ConfiguracoesDaBorda.cs`).
- Classes de uso: **Ligada** (roda com tudo no padrão) · **Chave** (implementada, desligada até bancada
  ou decisão) · **Costura** (declarada e testável, ninguém chama) · **Ninguém** (só gerada ou nem isso).

---

## 3. Inventário capacidade × uso

### 3.1 Por grupo

| Grupo | Documentado (evidência) | Ligada na operação | Atrás de chave | Costura sem uso | Ninguém usa (motivo) | Cobertura (ligada / no código / aplicável) |
|---|---|---|---|---|---|---|
| Conexão e ping | `DefinirTipoConexao` (FUN:2), `AbrirPortaComunicacao` (FUN:3), `FecharPortaComunicacao` (FUN:4), `Ping` (FUN:5, nome corrigido A01:122), `PingOnLine` (FUN:6); modem ×3 (INV:20,116,144) | 4 (`ADP:89-116`) | — | `PingOnLine` (`ADP:118`; nunca no PUMP) | modem (fora) | 4 / 5 / 5 |
| Configuração geral e enviadores | `EnviarConfiguracoes` (FUN:31), `EnviarConfiguracoesFuncoes` (FUN:32 LACUNA), `DefinirConfiguracoesFuncoes` (INV:52), `ReceberConfiguracoesInner` (INV:171) | 1 | — | — | 3 (`…Funcoes` sem semântica T13; leitura de volta proibida T12) | 1 / 1 / 4 |
| Leitores, cartão e recepção | `ConfigurarTipoLeitor` 0–8 (FUN:14), `ConfigurarLeitor1/2` 0–4 (FUN:15-16), `DefinirPadraoCartao` (FUN:11), dígitos fixos/variáveis (FUN:12-13), `DefinirFuncaoDefaultLeitoresProximidade` 0–12 (FUN:23), 7× `ConfigurarLeitorProximidade*` (INV:36-42), `ReceberDadosOnLine` + `_ComLetras` + `_QRCodeComLetras` (FUN:41, INV:174,176), `EnviarFormasEntradasOnLine` (FUN:33) | 6 | 2 (fixo só se declarado; variável: `catraca.enviar_digitos_variaveis`) | — | 10 (função default **prevalece** — F14; letras exigem buffer > 64 B, T25) | 6 / 8 / 18 |
| Teclado | `HabilitarTeclado` (FUN:21), `DefinirConfiguracaoTecladoOnLine` (INV:51), padrão Topdata ×3 (INV:50,70,266) | 1 (enviado desligado) | — | — | 4 (evento usa padrão Livre) | 1 / 1 / 2 |
| Display e mensagens | `EnviarMensagemPadraoOnLine` (FUN:57), `…TemporariaOnLine` (FUN:58), apresentação ×2 (INV:62-63), off-line ×4 (INV:64,65,68,69), mudança ×2 (INV:66-67), `EnviarMensagensOffLine`/`Apagar…` (INV:113,10); backlight só nome (INV:84,155) | 2 (`PUMP:519-531`, `PUMP:754`, comando) | — | — | 11 (sem linha na FUN; apresentação pode mostrar o número do cartão — LGPD, A01:355-358) | 2 / 2 / 13 |
| Relés e acionamentos | `ConfigurarAcionamento1/2` função 0–9, tempo 0–50 s (FUN:17-18), `AcionarRele2` (FUN:48), `AcionarRele1`, `ManterRele1/2Acionado`, `DesabilitarRele1/2`, `DefinirLogicaRele` (INV:5,162,163,81,82,61), sirene ×3 (INV:140,103,8), `DefinirPorcentagemRevista` (INV:73) | 2 (relé 1 = 2/5 s; relé 2 = 0/0, `MontadorDaConfiguracao.cs:50-53`) | — | `AcionarRele2` (`ADP:377-378`) | 10 (relé 1 é o giro; resto só assinatura) | 2 / 3 / 9 (sem os 3 do relé 1 e a sirene por horário) |
| Bip e sinalização | `AcionarBipCurto/Longo` (FUN:49-50), bip intermitente ×2 (INV:156,85), `DesabilitarBipCatraca/Coletor` (INV:78-79), `EnviarSinalizacao` (INV:115); LEDs ×4 só Linha 3 (FUN:51-54) | — | 2 (`comando.bip_curto/longo`, `PUMP:813-823`) | — | 5 | 0 / 2 / 7 |
| Sentidos de liberação | EI-041..045 (FUN:42-46) | 1 (`LiberarCatracaEntrada`, perfil padrão) | 4 (invertidas pelo perfil, aguardando HIL-DIR-05/06; saída `comando.liberar_saida`; dois sentidos `comando.liberar_dois_sentidos` + D5) | — | **Fora do SDK:** sentido de entrada "Direita/Esquerda" ajustável no **WebServer** (padrão "Direita" = entrada anti-horária) — PUBLICO_TOPDATA [SENTIDO-WS]; quem vence depois de `EnviarConfiguracoes` é **T36** (NOVO-HIL-MAP-02, do 03) | 1 / 5 / 5 |
| Giro, sensores e mecânica | origens 4, 5, 6, 8–10 (ORI:5-11); `LerSensoresInner` (INV:146), `DefinirEventoSensor` (INV:56), `ConfigurarBotaoExternoOffline` (INV:28), `DefinirSensorPortaOffline` (INV:75), `DesabilitarBloqueioCatracaMicroSwitch` (INV:80); contador de giro só nome (INV:13,145); contador **por sentido** no display e no menu Master → Informações (PUBLICO_TOPDATA [ESP-FIT4], [CONTADOR-MASTER]) | origens 5 e 6 | — | — | 5 funções (só assinatura; micro switch nunca editável); contador por sentido: leitura **manual** pelo Master (check-up do 03), pela DLL só com T17/**T39** (NOVO-HIL-CHK-02) | origens 2/6; funções 0/0/5 |
| Eventos e origens on-line | 17 origens não biométricas (ORI:2-11, 21-28); `ReceberDataHoraDadosOnLine` (FUN:28), `RegistrarAcessoNegado` (FUN:22) | origens 2, 3, 21 (decisão), 5, 6 (ciclo); 1 é leitura mas teclado desligado | 2 funções (`catraca.enviar_data_hora_no_evento`, `catraca.registrar_acesso_negado`) | — | 12 origens só em texto (4, 7, 8, 9, 10, 20, 35, 42, 65, 66, 67, desconhecidas) | origens 5/17 (29%) |
| Bilhetes, memória e 128 | `ColetarBilhete` (FUN:40), `ReceberQuantidadeBilhetes` (INV:186), `AvisarQuandoMemoriaCheia` (INV:14); tipos 0–13, 100–113, 128 (BIL:2-18) | — | 1 (`catraca.coletar_bilhetes`, A.9) | — | 2 (array de tamanho desconhecido T20; aviso T34). Opção local "Buffer: Para/Segue" (PUBLICO_TOPDATA [MEM-CHEIA]; **T37**, NOVO-INT-REC-08) — sem função conhecida no SDK que a leia ou grave | 0 / 1 / 3 — o 128 é interpretado (dedup, `015_bilhetes_coletados.sql:46-52`); os demais tipos ficam crus |
| Listas e horários | FUN:34-39 + leitura de volta/exclusão (INV:196,260,249,261,262) + master (FUN:24, INV:15) + mudança de sentido por horário (INV:138-139) | — | 2 (tipo de lista só 0; master só se custodiado = nunca) | — | 14 (D3/D4, Etapa D) | 0 / 2 / 16 |
| Relógio | `ReceberRelogio`, `EnviarRelogio` (FUN:8-9), `EnviarHorarioVerao` (FUN:10) | 2 (`PUMP:964-1041`) | `relogio.acertar_ao_divergir` | — | horário de verão (de propósito: não se apaga, A01:338-340) | 2 / 2 / 2 |
| Firmware e versão | `ReceberVersaoFirmware` (FUN:7), `…6xx`, `…6xx_ComComplementar` (INV:200-201) | 1 (`PUMP:444-457`) | — | — | 2 (só assinatura) | 1 / 1 / 3 |
| Mudança on-line/off-line | `ConfigurarInnerOnLine/OffLine` (FUN:19-20), `HabilitarMudancaOnLineOffLine` (FUN:29), `EnviarConfiguracoesMudanca…` (FUN:30), entradas da mudança ×2 (INV:53,55), `EnviarBufferEventosMudancaAuto` (INV:91) | 2 (on-line + mudança 0) | 2 (`catraca.sequencia_oficial`) | (`PingOnLine` no grupo 1) | 3. Relato de integrador: "em off-line a catraca libera com qualquer entrada" (PUBLICO_INTEGRADOR [OFFLINE-LIVRE], outros modelos) — **T38**, NOVO-INT-OFF-13 (do 03); pesa em D4/D5/D8 | 2 / 4 / 7 |
| Wiegand | `ConfigurarWiegandDoisLeitores` (FUN:25); tipos 3/6/7 (FUN:14) | — | 1 (`catraca.enviar_wiegand_dois_leitores`) | — | — | 0 / 1 / 1 |
| WebServer | `DesabilitarWebServer` (INV:83); senha de fábrica publicada (A01:92-93) | — | — | — | 1 (sem linha na FUN; T32) | 0 / 0 / 1 |
| Urna | leitor 2 + relé 2 + origens 3, 7, 20 (FUN:16,18,48; ORI:4,8,21); `EngolirCartao`/`DevolverCartao` (INV:89,88) | leitura pela fenda (origem 3) | — | `AcionarRele2` | recolhimento real (A.11, T18/T19) | parcial |
| Biometria | 104 exportações (A01:431-453) | **fora** (ingresso não é biométrico; LGPD; ADR-0014) | — | — | — | fora |

### 3.2 Percentuais (sem arredondar a favor)

| Base | Ligadas | No código (ligadas + chave) | Na costura (+ sem chamador) |
|---|---|---|---|
| 48 funções da FUN aplicáveis à Linha 4 (58 − 3 biometria − `LiberarLeitor` inexistente − 2 backlight sem assinatura − 4 LEDs da Linha 3) | 22 (46%) | 38 (79%) | 40 (83%) |
| 130 exportações não biométricas com assinatura (INV) | 22 (17%) | 38 (29%) | 40 (31%) |
| 17 origens não biométricas documentadas (ORI) | 5 com comportamento (29%) | — | as 17 chegam ao laço; 0 gravadas em tabela |

**Leitura honesta:** o produto usa bem o **núcleo do giro** (conectar, configurar, ler, decidir,
liberar, confirmar). O que está fora são famílias inteiras de **observação** (sensores, contador,
quantidade de bilhetes, firmware estendido) e de **expressão** (display contextual, relé 2, bip). As
duas primeiras têm muito "só assinatura"; o ganho imediato está em **interpretar o que já chega**, não
em chamar funções novas.

### 3.3 Fatos públicos fora do repositório, incorporados ao inventário

Nenhum destes é documentação da Topdata registrada no repo (FUN/INV/ORI/BIL, docs/11). Entram como
**pergunta e ensaio**, nunca como valor de enum, faixa ou comportamento assumido no código.

| Fato | Selo | Fonte | Onde muda o inventário | Pergunta / ensaio |
|---|---|---|---|---|
| Memória com opção local **"Buffer: Para ou Segue"** (parar e mostrar "Memória Cheia" × sobrescrever a mais antiga) | PUBLICO_TOPDATA | [MEM-CHEIA] (via 03 §3.3) | Bilhetes: a memória pode **não** ser circular; o modo de fábrica e se o SDK o grava são desconhecidos | T37 · NOVO-INT-REC-08 |
| Sentido de entrada da Catraca 4 ajustável no **WebServer** ("Direita" padrão = entrada anti-horária; "Esquerda" = horária) | PUBLICO_TOPDATA | [SENTIDO-WS] (via 03) | Sentidos: há **dois** lugares que definem o sentido físico (WebServer e EI-041..044); o WebServer fica indisponível on-line (A01:92-93) e o `EnviarConfiguracoes` manda padrões (FUN:31) — pode desfazer o ajuste, como na urna (T19) | T36 · NOVO-HIL-MAP-02 |
| **Em off-line, a catraca libera com qualquer entrada** | **PUBLICO_INTEGRADOR** (afirmação de terceiro, sobre outros modelos) | [OFFLINE-LIVRE] (via 03) | Mudança on/off: se valer para a Catraca 4 em alguma configuração, a contingência (D8) e a política fail-safe × fail-secure (D5, ADR-0013) mudam; também explicaria "giro órfão" em massa (C3/C10) | T38 · NOVO-INT-OFF-13 |
| **Contador de giros por sentido** (horário × anti-horário) no display e no menu Master | PUBLICO_TOPDATA | [CONTADOR-MASTER], [ESP-FIT4] | Giro e sensores: o equipamento conhece o sentido físico (§6); leitura automática depende de `LerContadorGiro` (só nome) | T17 + T39 · NOVO-HIL-CHK-02 |
| Contador conta giro **mesmo sem cartão** | PUBLICO_TOPDATA | [CONTADOR] | Eventos: base para S1/C3 (giro órfão) | T42 · NOVO-HIL-GIRO-01 |
| Relé 2 livre em modelos sem urna; saída 12 Vcc/1 A | PUBLICO_TOPDATA | [OP4], [ESP-FIT4] | Relés (§5) | T40 · NOVO-HIL-REL-04 |
| Função default de proximidade 0–9 **prevalece** sobre leitor e acionamento | PUBLICO_TOPDATA | [OP4] | Leitores: F14 | T44 · INT-CFG-06 |

**Reclamações públicas (03 §3.2) que as composições deste relatório atacam:** R1 comunicação → C12/C8;
R3 "lê mas não libera" → C4 (liberou e não girou) + C6 (motivo no display); R4 "não trava / gira livre"
→ C3/C10 (detectar, não consertar; premissa T38/T42); R5 memória → C14 + T37; R6 sentido → §6 + C4 + C9;
R7 relógio → C11; R10 hardware (display, bip, placa) → C8; queixas da base Topdata "giro pesado",
"reinicia ao girar", "cartão não recolhido mas caiu na urna" ([FAQ-BOX]) → C4/C5, C10(c), C7.

---

## 4. Sinais escondidos: o que a catraca já entrega e é desperdiçado

| # | Sinal | De onde vem | Hoje | O que permite inferir |
|---|---|---|---|---|
| S1 | **Origem 6 sem liberação nossa** (giro órfão) | ORI:7; chega em `Polling` por `PUMP:543-565` | perdido (F10) | carona/forçamento, botoeira ([ESP-FIT4] "liberação do acesso através de um botão externo"), master local ("Libera 30s", [ESP-BOX4]), alarme de incêndio no CN1 ([INCENDIO]: "o giro ficará liberado… funciona online e offline"), braço articulado caído, origem 6 tardia depois da origem 5 |
| S2 | **Origem 5 sem origem 6** (autorizado e não girou) | ORI:6; `DEC:183-188` conta `AutorizacoesSemGiro` em memória (`DEC:104`) | contador só na bancada (`SessaoDeBancada.cs:121`); na base é `ticket_use_attempt` consumido com `passage_confirmed_at` nulo (`003_ingressos_e_provedores.sql:54-66`) | desistência, pessoa confusa, **braço pesado/travando** ([GIRO-PESADO]), **sentido de liberação trocado** (F1/HIL-DIR-05/06): a pessoa empurra o lado errado |
| S3 | **Tempo liberar → girar** | `ticket_use_attempt.at` × `passage_confirmed_at` (gravado com o `ReceivedTime` da origem 6: `DEC:176`; `RepositorioDeIngressos.cs:415-417`) | gravado e nunca lido | fluidez por pista, efeito de sinalização, mecânica relativa entre pistas (com cautela: §7 C5) |
| S4 | **Retornos ≠ 0 de `ReceberDadosOnLine`** e falhas por função | `PUMP:570-580`, `PUMP:596-598`; `RetornosDocumentados` (docs/34:88) | contador no slot, registro no 1º/10º/100º | cabo/switch degradando antes da queda |
| S5 | **Latência de cada chamada nativa** | `ITA:101` (`Elapsed`), `ADP:581-587` | só no `ToString` | rede lenta, catraca sobrecarregada, laço perto do limite (NOVO-LOAD-LOOP-01) |
| S6 | **Tentativas de reconexão e disjuntor** | `PUMP:1046-1052`; `device_status.reconnect_attempts` (`006_operacao.sql:13`) | publicado o número, sem histórico | pista instável; reconexão logo após origem 6/`MonitoraGiro` = **reinício ao girar = fonte/alimentação** ([REINICIA-GIRO]: "Problema na alimentação") |
| S7 | **Firmware lido a cada conexão** (linha, variação, versão, bio) | `ITA:146`; `PUMP:444-457`; `device_status.firmware` (`006_operacao.sql:12`) | último valor em texto | troca de equipamento ou firmware atualizado sob o mesmo Inner (C8) |
| S8 | **Deriva do relógio** (hora em hora) | `PUMP:1008-1041`; `008_relogio_da_catraca.sql:4-7` | só a última | relógio fora do esperado (Topdata: "precisão de 1 (um) minuto por ano", [ESP-BOX4]) ⇒ bateria, reinício, acerto externo pelo WebServer |
| S9 | **Leitura durante o giro** | `FSM:232-236` (sem transição) | perdida (F11) | fila colada, pressão de vazão, pessoa sem resposta |
| S10 | **Origens 7 e 20** (recolhido, urna cheia) | ORI:8, ORI:21 | texto (`SessaoDeOperacao.cs:483`) | ritmo de enchimento da urna; origem 3 aprovada sem 7 repetida ⇒ **sensor da urna sujo/desalinhado** ([NAO-RECOLHIDO]) |
| S11 | **Origens 8–10** (sensores 1–3) | ORI:9-11 | texto | botoeira, porta de manutenção, tampa da urna — depende de fiação (§5) |
| S12 | **Origem desconhecida** (11, 14–17, 19…) | ORI:12,15-18,20; ADR-0018 | texto; métrica definida e não ligada | firmware novo; insumo da matriz |
| S13 | **Bilhete tipo 128** | BIL:18; `015_bilhetes_coletados.sql` (`repeated`) | gravado | coleta anterior interrompida entre devolver e confirmar (T35) |
| S14 | **Data/hora por evento** (`DeviceTime`, `ClockDrift`) | FUN:41 (tem segundos); `DeviceEvent.cs:63-64` | depende de `catraca.enviar_data_hora_no_evento` (desligada) | deriva **por evento**, sem chamada extra |
| S15 | **Tecla de função / teclas 35, 42, 65–67** | ORI:23-28 | texto | uso indevido do teclado; com teclado desligado, qualquer tecla é curiosidade ou vandalismo |
| S16 | **Configuração aceita × salva** | `PUMP:487-488`; `VersaoDaConfiguracao` (docs/34:576, A.5) | **já usado** (A.6) | — (fica como base para C7) |

---

## 5. Relés a nosso favor

> "Não documentado" aqui = **uso novo de uma saída documentada**. Nenhuma função, valor de enum ou
> comportamento novo é inventado; o que é dedução leva `INFERIDO`.

### 5.1 O que está documentado

**Hardware (Catraca 4)**

| Fato | Selo | Evidência |
|---|---|---|
| A placa tem **dois acionamentos**; "um relê é utilizado para controlar o bloqueio do giro"; "o outro acionamento é utilizado pela catraca nos modelos com urna. **Nos modelos que não utilizam urna esse acionamento está disponível** e pode ser utilizado para conectar um dispositivo externo" (lâmpada, campainha) | PUBLICO_TOPDATA | [OP4], seção Revista |
| Acionamento 1 "tem como função única o controle do giro" | PUBLICO_TOPDATA | A01:369 (WebServer) |
| Saída de acionamento externo da Fit 4 e da Box 4: **12 Vcc, até 1 A** | PUBLICO_TOPDATA | [ESP-FIT4], [ESP-BOX4] |
| O solenoide da urna liga em **NA2 / C2** da placa principal | PUBLICO_TOPDATA | [URNA-RELE] |
| Linha Inner (coletores): dois relés **NA1/C1/NF1 e NA2/C2/NF2** "para fechaduras, sirenes, fechos-eletromagnéticos e cancelas" | PUBLICO_TOPDATA (Inner, não catraca) | [RELES-INNER] |
| Coletor Urna 4: contato seco até 3 A (não é a catraca) | FONTE do repo | `docs/02-matriz-compatibilidade.md:63` |
| Alarme de incêndio: contato **NF** do alarme em série com o solenoide no **CN1** da PCI Catraca → "o giro ficará liberado… funciona online e offline" | PUBLICO_TOPDATA | [INCENDIO] |
| Braço articulado (anti-pânico) cai quando a alimentação é interrompida | PUBLICO_TOPDATA | [ESP-BOX4] |

Conclusão elétrica: na TopFit 4 sem urna, o relé 2 é **provavelmente** uma saída com contato NA2/C2 ou
uma saída de 12 Vcc/1 A — as duas páginas descrevem a mesma saída de jeitos diferentes. **Qual dos dois,
e se há NF2, é `A_CONFIRMAR_COM_TOPDATA`** (T40) e se resolve com multímetro na bancada (NOVO-HIL-REL-04).

**Software (SDK)**

| Função | Parâmetros | Selo | Evidência | No produto |
|---|---|---|---|---|
| `ConfigurarAcionamento1(Funcao, Tempo)` | Funcao 0–9; Tempo 0–50 s | FONTE_PRIMARIA | FUN:17 | ligado: 2 / 5 s (`MontadorDaConfiguracao.cs:50-51`); tempo por catraca (A.3) |
| `ConfigurarAcionamento2(Funcao, Tempo)` | idem; "Rele 2 e o da urna" | FONTE_PRIMARIA | FUN:18 | ligado: 0 / 0 (`MontadorDaConfiguracao.cs:52-53`) |
| `AcionarRele2(int Inner)` | **sem tempo**: dura o `Tempo` do acionamento 2 | FONTE_PRIMARIA (SDK corrige o manual) | FUN:48; `ITA:295-301` | na costura, sem chamador (`ADP:377-378`) |
| `ManterRele2Acionado`, `DesabilitarRele2` | `(int Inner)` | ASSINATURA_SDK; efeito `INFERIDO` | INV:163, INV:82 | gerada, sem uso |
| `AcionarRele1`, `ManterRele1Acionado`, `DesabilitarRele1` | `(int Inner)` | ASSINATURA_SDK | INV:5,162,81 | **proibidas** (relé 1 = giro; A01:559) |
| `DefinirLogicaRele(byte)` | ? (NA/NF `INFERIDO`) | ASSINATURA_SDK | INV:61 | não enviada (padrão da DLL) |
| `DefinirPorcentagemRevista(byte)` | ? | ASSINATURA_SDK | INV:73 | não enviada |
| `InserirHorarioSirene` / `EnviarHorariosSirene` / `ApagarHorariosSirene` | hora, minuto, 7 dias | ASSINATURA_SDK | INV:140,103,8 | não |
| Origem 5 | "Tempo de acionamento de um rele expirou" — **não diz qual** | FONTE_PRIMARIA | ORI:6 | fecha a janela do giro (`PUMP:557`) |

**Os dez valores de função — nome do SDK × nome da Topdata para o acionamento 1**

A tabela de [OP4] ("Detalhamento das configurações do acionamento") lista **nove** comportamentos na
mesma ordem dos valores 1–9 do SDK. A correspondência um a um é `INFERIDO` (pela ordem e pelos nomes),
forte o bastante para orientar o ensaio, nunca para ligar código.

| Valor (FUN:17) | Nome no SDK | Nome na Topdata (Acionamento 1) [OP4] | Pictogramas (Revolution 4) | Uso no relé 2 (ideia, `INFERIDO`) |
|---|---|---|---|---|
| 0 | não utilizado | — | — | relé 2 só por comando (`AcionarRele2`): **o modo mais previsível** |
| 1 | registro entrada ou saída | Libera giro para entrada e saída | verde / verde | pulso a cada registro? (ensaio) |
| 2 | registro entrada | Libera giro para entrada | verde / vermelho | **pulso no registro de entrada** → câmera, contador externo (ensaio NOVO-HIL-REL-05) |
| 3 | registro saída | Libera giro para saída | vermelho / verde | idem, saída; na urna do Coletor, "Libera para Saída" (A01:408) |
| 4 | sirene | Sirene (**Não utilizar no acionamento 1**) | apagados | sirene por horário (fora do evento) |
| 5 | revista | Revista (**Não utilizar no acionamento 1**) | apagados | revista **nativa só off-line**, "três bips longos e mensagem" ([OP4]) |
| 6 | catraca saída liberada | Catraca com saída liberada | verde / verde | **perigo**: sugere sentido livre sem controle |
| 7 | catraca entrada liberada | Catraca com entrada liberada | verde / verde | **perigo**, idem entrada |
| 8 | liberada dois sentidos | Catraca liberada em ambos os sentidos | verde / verde | **nunca** fora de evacuação (D5) |
| 9 | dois sentidos com marcação | "Libera giro em ambos os sentidos e **bilhete é gerado conforme giro da catraca**" | verde / verde | evidência de que o firmware sabe o sentido do giro (§6) |

**Tempo.** SDK 0–50 s (FUN:17-18) × WebServer 1–255 s, padrão 5 (A01:94); 0 indefinido (T8,
NOVO-HIL-DIR-09). No relé 1 o tempo é a janela do giro ("tempo em que uma catraca aguarda para
completar o giro", A01:380). No torniquete a Topdata usa o relé por 1 s fixo e o "tempo de
acionamento" vira espera do sinal de giro ([TORNIQUETE]) — mostra que o firmware separa duração do
pulso e janela de espera em alguns produtos (`INFERIDO` para a catraca).

### 5.2 Restrições que governam qualquer uso

1. **Relé 1 nunca.** É o giro; usar fora de `LiberarCatraca*` burla o controle (FUN:17, A01:559; [OP4]
   "Não utilizar no acionamento1" para sirene e revista).
2. **Relé 2 só em catraca sem urna** ([OP4]) — e a regra 3 atual **recusa** relé 2 ≠ 0 com leitor 2
   desligado (`CFG:439-444`). Uso de sinalização exige trocar a regra por "relé 2 ≠ 0 ⇒ leitor 2 ≠ 0
   **ou** papel do relé 2 = sinalização declarado no comissionamento".
3. **Origem 5 ambígua (F13).** Enquanto o ensaio não mostrar que o `Complemento` da origem 5 separa os
   relés (T14 ampliada), **nenhum pulso do relé 2 pode coincidir com `MonitoraGiroCatraca`**: só em
   `Polling`, como bip e mensagem (`PUMP:374-387`). E a origem 5 que chegar em `Polling` precisa ser
   tratada como "fim do relé 2", não como giro.
4. **Configuração é sempre completa (ADR-0020; FUN:31).** Função e tempo do relé 2 ajustados no WebServer
   são desfeitos no próximo `EnviarConfiguracoes`. O papel do relé 2 tem de morar na nossa
   `DeviceConfiguration`, versionado (A.5).
5. **Thread única, uma chamada por passo** (`PUMP:206-213`). Cada pulso é uma chamada a mais no laço do
   worker (até ~20 catracas): nunca no caminho da passagem autorizada; sempre depois do evento.
6. **Fail-safe elétrico.** Lógica NA/NF do relé é `DefinirLogicaRele` (só assinatura) → até o ensaio,
   assumir que **sem energia e sem software o relé está aberto**. Sinal que precisa significar "tudo
   bem" deve ser o **contato fechado** (cai sozinho em falha), nunca o contrário.
7. **Evacuação não é relé de software.** O caminho de evacuação documentado é o NF do alarme de
   incêndio no CN1 ([INCENDIO]) e o braço articulado ([ESP-BOX4]) — hardware, sem PC. O software só
   **reconhece** que aconteceu (C10). Isso é argumento forte para a D5 (ADR-0013).

### 5.3 Usos possíveis

Esforço: P (≤ 2 dias), M (≤ 1 semana), G (> 1 semana).

| # | Uso | Como (composição) | Documentado | Inferido / a confirmar | Fiação e ensaio | Risco elétrico / operacional | Classificação | Esforço |
|---|---|---|---|---|---|---|---|---|
| R1 | **Acionar relé 2 pelo painel** (teste de sinaleiro, abrir a fenda da urna para desencravar cartão — `CARD_JAM_SUSPECTED`, docs/04-workflow-collect-card-then-enter.md:137) | novo `TipoDeComando` no padrão da A.8 (chave desligada, só em `Polling`, validade, auditoria), chamando `AcionarRele2` (já na costura) | EI-047 (FUN:48); relé 2 livre sem urna ([OP4]) | duração = tempo do acionamento 2 com função 0 (`INFERIDO`) | NOVO-HIL-REL-04 (identificar borne e tensão, pulso com função 0 e tempos 1/5/50 s) | baixo: uma chamada em `Polling` | IMPLEMENTÁVEL AGORA (simulador: `InnerSimulator.AcionarReleDaUrna`, `src/Simulator/InnerSimulator.cs:316`) · efeito físico DEPENDE DE BANCADA | P |
| R2 | **Sinaleiro de pista** (lâmpada verde "pista atendendo") | contato fechado = catraca em `Polling` há < N s. Duas formas: (a) `ManterRele2Acionado`/`DesabilitarRele2` (só assinatura); (b) **"homem-morto"**: `AcionarRele2` periódico com tempo T (≤ 50 s), reenviado a cada T/2 em `Polling` — se o worker morre, a luz apaga sozinha em até T | (b) só usa EI-047 + EI-017 | (a) semântica `INFERIDO` → DEPENDE DA TOPDATA; (b) re-disparo antes de expirar estende ou reinicia? (`A_CONFIRMAR`); cada fim gera origem 5? (F13) | NOVO-HIL-REL-06 (origem 5 do relé 2 × relé 1, `Complemento`) e NOVO-HIL-REL-07 (re-disparo) | operacional: pulsos geram origem 5 e chamadas a mais; com 20 pistas e T = 50 s, ~0,8 chamada/s no worker — medir em NOVO-LOAD-LOOP-01 | DEPENDE DE BANCADA | M |
| R3 | **Aviso discreto de giro órfão** (luz/buzzer para o orientador, não sirene ao público) | C3 detecta origem 6 sem liberação → pulso de relé 2 em `Polling` | EI-047; origem 6 (ORI:7) | o giro órfão gera mesmo origem 6? (T42) | NOVO-HIL-GIRO-01 + NOVO-HIL-REL-04 | **pânico/constrangimento** se for sirene; falso positivo em botoeira/master/evacuação (suprimir com C10) | DEPENDE DE BANCADA | P (depois de C3) |
| R4 | **Pulso para câmera/NVR no giro** (marca o instante para buscar imagem depois; a imagem fica no sistema do cliente) | (a) **firmware**: função 2 no relé 2 ("registro entrada") pulsa a cada registro — zero chamada nossa; (b) software: `AcionarRele2` depois da origem 6, já em `Polling` | FUN:18 (valores); EI-047 | (a) que a função 2 no relé 2 pulse no giro é `INFERIDO`; (b) atraso = uma volta do laço | NOVO-HIL-REL-05 (função 2 no relé 2, girar, medir com multímetro/osciloscópio o pulso e o atraso; conferir origem 5) | LGPD: **nada de imagem no nosso sistema**; só o id do evento e a hora. Elétrico: entrada de alarme do NVR por contato seco; se a saída for 12 V, usar relé de interface | DEPENDE DE BANCADA | P |
| R5 | **Revista sorteada on-line, auditável** (docs/14-estudo-de-caso-evento-30k.md:86-88) | sorteio determinístico no software (semente do evento + contador de passagem, registrado), disparado **depois** da origem 6, pulsando relé 2 para a luz "revista" do segurança | revista nativa existe mas é **só off-line e só na saída** ([OP4]); EI-047 | — | NOVO-HIL-REL-04 | jurídico: critério precisa ser aprovado pelo dono; sem dado pessoal; o sorteio **não** decide acesso | IMPLEMENTÁVEL AGORA (lógica + simulador) · luz DEPENDE DE BANCADA | P/M |
| R6 | **Indicação "pista fora de operação"** | R2 invertido não serve (sem worker não há quem ligue a luz vermelha). Fazer pelo **verde homem-morto** (R2b): verde apagado = fora | idem R2 | idem R2 | idem R2 | ver §5.2 item 6 | DEPENDE DE BANCADA | — (é o R2) |
| R7 | **Totalizador externo / segunda testemunha de lotação** | função 2/3 no relé 2 pulsando por registro (R4a) alimenta um contador eletromecânico independente do PC | FUN:18 | `INFERIDO` como R4a | NOVO-HIL-REL-05 | baixo; útil em auditoria de prestação de contas | DEPENDE DE BANCADA | P |
| R8 | **Sirene / trava eletromagnética de urna** | sirene: função 4 + horários (ponto, fora do evento). Trava de urna: o relé 2 **já é** o solenoide da urna ([URNA-RELE]); não sobra relé | INV:140,103,8; [URNA-RELE] | — | — | sirene em evento = pânico | **DESCARTADA** (sem relé livre na catraca com urna; sirene não é para evento) | — |
| R9 | **Urna "libera para entrada"** (núcleo do docs/04) | `ConfigurarAcionamento2` + `AcionarRele2` + origem 7 → `LiberarCatracaEntrada` | FUN:18,48; ORI:8 | função certa do relé 2 (2 ou 3) `A_CONFIRMAR` (T19); docs/21-roteiro-da-bancada.md:428-433 | HIL-URNA-01, NOVO-HIL-URNA-02 | já planejado (A.11) | DEPENDE DE BANCADA | (A.11) |
| R10 | **Relé 2 no regime off-line** (sinal de "catraca em contingência") | `DefinirEntradasMudancaOffLine(…, Catraca)` + função do relé | INV:53 (só assinatura) | tudo `INFERIDO` | — | — | PROPOSTA FUTURA (DEPENDE DA TOPDATA) | — |

**Entradas complementares (o "I" do I/O).** Os sensores 1–3 geram origens 8–10 ("botões ou sensores
externos", ORI:9-11). Com fiação, viram: **tampa/porta da urna aberta** (marca o início do esvaziamento
na cadeia de custódia, docs/04-workflow-collect-card-then-enter.md:161-163), **botoeira do orientador**
(a liberação dela aparece como evento e não como giro órfão) e **porta de manutenção**. A ligação de
cada entrada a um evento passa por `DefinirEventoSensor(Sensor, Evento, Tempo)` (INV:56, só assinatura)
→ PROPOSTA FUTURA / NOVO-HIL-SEN-03. A Topdata usa o "sensor 2 (sensor de porta aberta)" para fim de
giro de torniquete ([TORNIQUETE]), o que mostra que as entradas são genéricas (`INFERIDO` para a catraca).

### 5.4 Ensaios novos de bancada (relés, giro, energia)

| Id | Pergunta | Como | Bloqueia |
|---|---|---|---|
| NOVO-HIL-REL-04 | Onde está e o que é a saída do relé 2 na TopFit 4 sem urna (NA2/C2? NF2? 12 V/1 A?) | multímetro com a catraca energizada; `AcionarRele2` com função 0 e tempos 1/5/50 s; anotar retorno, duração e tensão | R1–R7 |
| NOVO-HIL-REL-05 | Função 2 ou 3 no relé 2 pulsa a cada registro? Quando (liberação ou giro)? Gera origem 5? | configurar 2 e depois 3; 10 giros cada; medir pulso e registrar eventos | R4a, R7 |
| NOVO-HIL-REL-06 | A origem 5 do relé 2 é distinguível da do relé 1 (`Complemento`)? Chega se o pulso cair durante `MonitoraGiroCatraca`? | pulso do relé 2 em `Polling` e logo após liberar; anotar origem e `Complemento` | **todo uso do relé 2** |
| NOVO-HIL-REL-07 | `AcionarRele2` antes de expirar estende ou reinicia o tempo? | disparar a cada T/2 por 5 min; cronometrar | R2b |
| NOVO-HIL-REL-08 | Funções 6/7/8 no relé 2 deixam o **giro** livre? (prova de que nunca podem ser escolhidas) | só em manutenção, com a catraca sem público; tentar girar sem liberar | regra de validação nova |
| NOVO-HIL-GIRO-01 | Giro sem liberação gera origem 6? (forçar o braço; botoeira; master "libera 30s"; WebServer indisponível on-line) | cada caso 5 vezes; anotar origem, `Complemento`, estado do laço | C3, R3 |
| NOVO-HIL-GIRO-02 | `Complemento` da origem 6 traz o sentido? (amplia T14 / NOVO-HIL-DIR-08) | girar entrada e saída com cada função EI-041..045 | §6, Mapa de giro |
| NOVO-HIL-GIRO-03 | Alarme de incêndio no CN1 ([INCENDIO]) e corte de energia com braço articulado: o que o laço recebe? | abrir o NF do CN1 em bancada; cortar energia 10 s | C10 |
| NOVO-HIL-GIRO-04 | Sem origem 5, quanto tempo o laço fica em `MonitoraGiroCatraca`? | liberar e não girar com tempo do relé 1 = 0, 5, 50 | F9/C1 |
| NOVO-HIL-PWR-01 | Depois de 5 min e de 2 h sem energia, o que `ReceberRelogio` devolve **antes** do acerto? | ler o relógio na reconexão antes do `EnviarRelogio` | C11 |
| NOVO-HIL-SEN-03 | Sensores 1–3 com contato seco geram origens 8–10 com a configuração padrão da DLL? | fechar cada entrada; anotar origem | entradas (§5.3) |

### 5.5 O que nunca fazer com relé

- `AcionarRele1`, `ManterRele1Acionado`, `DesabilitarRele1` — o relé 1 é o giro.
- Funções 6, 7, 8 e 9 em qualquer relé fora de evacuação/D5 — o nome sugere catraca liberada
  ([OP4]); hoje a função do relé 1 é constante de código (`MontadorDaConfiguracao.cs:50`) e não é
  sobreponível por catraca (A.3, docs/34:230-235); manter assim e acrescentar `Validar()` recusando
  6–9 no relé 1 e no relé 2.
- Carga direta (sirene 127/220 V, eletroímã) na saída da catraca — sempre relé de interface; 1 A/12 V é
  o teto publicado ([ESP-FIT4]).
- Qualquer sinal que signifique "seguro" com contato aberto.

---

## 6. Sentido do giro — o que confirmar para o "Mapa de giro"

| Pergunta | Resposta com evidência | Selo |
|---|---|---|
| Quais origens de giro a catraca reporta? | **Uma só confirma passagem: 6** "Giro concluído. Gerado pelo SENSOR OPTICO apos liberacao" (ORI:7). **4** é sensor de giro legado, obsoleto, "NUNCA usar para confirmar passagem" (ORI:5). **5** é fim do tempo de um relé, **não é giro** (ORI:6). O domínio já fixa isso (`src/Access.Domain/Devices/EventOrigin.cs:126-132`) | FONTE_PRIMARIA |
| O evento on-line distingue o sentido físico? | **Não, pelo que está documentado.** `ReceberDadosOnLine` devolve origem, `Complemento`, cartão e data/hora (FUN:41); nenhuma fonte do repo dá semântica ao `Complemento` (docs/11-capacidades-do-sdk.md:67; `DeviceEvent.cs:40-41` "Semântica varia"); a pergunta está aberta (T14, NOVO-HIL-DIR-08) e o próprio docs/29-o-que-falta.md:98 registra "A TopFit 4 distingue o sentido do giro? Sem isso não há lotação" | FONTE_PRIMARIA (ausência) |
| O equipamento conhece o sentido? | **Sim**: "Função de contador de giros dos sentidos Anti-horário-AH e Horário-H" ([ESP-FIT4], [ESP-BOX4]); e a função 9 de acionamento gera bilhete "conforme giro da catraca" ([OP4]). Ler o contador exige `LerContadorGiro`, que **só tem nome** (INV:145; T17) | PUBLICO_TOPDATA; leitura `A_CONFIRMAR` |
| O sentido físico também é configurado fora do SDK? | **Sim, segundo página pública:** o WebServer da Catraca 4 define a entrada como "Direita" (padrão, anti-horária) ou "Esquerda" (horária) ([SENTIDO-WS], PUBLICO_TOPDATA, via 03). Combinado com EI-043/044 ("invertidas") e com a dupla inversão já corrigida (F1, docs/34:83), **há risco de inversão tripla**: WebServer × perfil × função. Quem vence depois de `EnviarConfiguracoes` é **T36** (NOVO-HIL-MAP-02) | PUBLICO_TOPDATA; `A_CONFIRMAR` |
| E nos bilhetes (off-line)? | Tipos **10 entrada / 11 saída** pelo cartão (BIL:4-5); o WebServer exporta "010 ou 110 Entrada, 011 ou 111 Saída" (A01:321-323). O sentido do bilhete vem da configuração do leitor/acionamento, não comprovadamente do sensor | FONTE_PRIMARIA (tipos) / `INFERIDO` (origem do sentido) |
| Então como classificar hoje? | **Por atribuição**: o giro herda o sentido da função que o liberou — EI-041 entrada, EI-042 saída, EI-043/044 invertidas, conforme o perfil comissionado (`CFG:47-94`; `PUMP:728-735`). **Indeterminado** em: EI-045 (dois sentidos), giro órfão (S1), giro tardio fora da janela. O `physical_passage.direction` é preenchido pelo chamador, não pela catraca (`001_esquema_inicial.sql:59-66`; `src/Access.Infrastructure.SQLite/AccessJournal.cs:113-141`) | — |
| O que o Mapa de giro precisa garantir | (1) a classe "entrada/saída do sistema" sai de **origem da liberação × perfil**, nunca do evento de giro; (2) um valor **"sentido desconhecido"** explícito para dois sentidos e órfãos; (3) `REVERSE_TURN` (docs/04:142) **não é detectável** até NOVO-HIL-GIRO-02 mostrar o sentido no `Complemento`; (4) o `Complemento` bruto da origem 6 precisa ser **gravado** desde já, para a bancada e para reprocessar depois (C2); (5) no comissionamento, **anotar o ajuste "Direita/Esquerda" do WebServer** e a leitura do contador AH/H do menu Master antes e depois do giro de teste — é a única prova física do sentido disponível sem a DLL (T36, T39) | — |

---

## 7. Composições

Formato de cada uma: problema · como funciona (primitivas + evidência) · o que é novo · risco e
mitigação · classificação · teste · esforço. **Nenhuma delas entra no caminho da decisão de liberar**;
todas leem eventos que já chegaram ou falam com a catraca só em `Polling`.

### C1. Prazo real do giro (auto-recuperação do `MonitoraGiroCatraca`)

- **Problema.** F9: sem origem 5, a pista fica presa e ignora leituras sem dar erro — o "modo de falha
  mais caro" (docs/14-estudo-de-caso-evento-30k.md:81-84).
- **Como.** O laço dispara `TempoEsgotado` quando `agora − entrada no estado ≥ TimeoutFor(estado)`
  (`FSM:40-48`, `FSM:91`); para `MonitoraGiroCatraca` a transição já existe e leva a
  `ConfigurarEntradasOnline` (`FSM:235`), que chama EI-032 e rearma o leitor. O instante de entrada está
  no histórico da máquina (`FSM:120-128`). O decisor encerra a pendência como "sem giro" (mesmo caminho de
  `DEC:183-188`).
- **Novo.** A sequência oficial do manual depende da catraca mandar a origem 5; isto acrescenta a rede de
  segurança do lado do software, determinística.
- **Risco.** Tempo do relé 1 ≥ 8 s rearma o leitor com a catraca ainda liberada — já alertado
  (`CFG:483-492`). Origem 6 que chegue depois do prazo vira giro órfão (C3 cobre).
- **Classificação.** IMPLEMENTÁVEL AGORA (não muda o caminho feliz; só age quando nada chega).
- **Teste.** Unidade (laço + `CosturaFalsa`: liberar, nenhuma origem, 8 s → `ConfigurarEntradasOnline`,
  uma chamada EI-032); simulador (`ScriptedEvent` sem origem 5, `src/Simulator/SimulatedDevice.cs:11-15`);
  bancada NOVO-HIL-GIRO-04.
- **Esforço.** P.

### C2. Caderno de sinais da catraca (gravar o que já chega)

- **Problema.** S1, S2, S7, S9–S15 morrem numa linha de texto (`SessaoDeOperacao.cs:481-485`); a tabela
  `raw_event` existe e a operação não grava (docs/34:98-100).
- **Como.** No `aoReceberEvento` (`PUMP:549`), toda origem que **não é leitura** (`EventOrigin.EhLeitura`,
  `EventOrigin.cs:152-155`) vai para uma tabela própria só-INSERT: catraca, origem bruta, `Complemento`,
  hora da borda, hora da catraca, estado da máquina no instante, id da última liberação. **Sem cartão**
  (as origens não-leitura não trazem credencial; se trouxerem, máscara + HMAC como `collected_ticket`).
  Leituras continuam onde estão (`ticket_use_attempt`).
- **Novo.** Transforma a catraca num sensor auditável. Base de C3–C10.
- **Risco.** Escrita no SQLite (um escritor) no laço: gravar **fora** do passo, por fila em memória com
  descarte contado, como os desfechos de comando (`SessaoDeOperacao.cs:425-444`); nunca bloquear o passo.
- **Classificação.** IMPLEMENTÁVEL AGORA.
- **Teste.** Integração com simulador (origens 4, 5, 6 órfã, 7, 8, 20, 35, 99 → linhas; nenhuma coluna
  com código); carga: 4 catracas × 10 eventos/s sem `FALHA_NA_BASE_LOCAL` (LOAD-DEC-01).
- **Esforço.** M (migração nova + fila + testes).

### C3. Sentinela de giro órfão (passagem sem liberação registrada)

- **Problema.** Carona por forçamento, botoeira, master local "libera 30s" ([ESP-BOX4]), giro tardio.
  Hoje é invisível (F10). A Topdata confirma que o contador conta giro sem cartão ([CONTADOR]).
- **Como.** Origem 6 recebida com a máquina em `Polling` (ou sem tentativa pendente no decisor,
  `DEC:172-181`) ⇒ "giro órfão". Classificação explicável por contexto: (a) origem 5 desta pista nos
  últimos N s ⇒ "giro tardio" (pessoa lenta); (b) várias pistas ao mesmo tempo ⇒ C10; (c) origem 8–10
  logo antes ⇒ "botoeira"; (d) resto ⇒ "sem explicação". Contagem por pista e alerta ao painel com
  texto em português.
- **Novo.** O integrador "da época" tratava a origem 6 só como fecho da liberação; aqui ela é auditada
  também quando ninguém a pediu.
- **Risco.** Falso positivo (evacuação, botoeira) → limiar e supressão por C10; **nunca bloquear a
  pista** por isso, só avisar.
- **Classificação.** IMPLEMENTÁVEL AGORA (lógica, simulador); a premissa física é DEPENDE DE BANCADA
  (NOVO-HIL-GIRO-01, T42). Se o relato de integrador "off-line libera qualquer entrada" ([OFFLINE-LIVRE],
  T38) valer, uma pista caída para off-line gera giros órfãos em série: C10 precisa distinguir.
- **Teste.** Simulador: `Roteirizar(origem 6)` em `Polling` → 1 órfão; origem 5 + origem 6 em 2 s →
  "tardio"; liberação + origem 6 → 0 órfãos.
- **Esforço.** P (com C2).

### C4. Detector de "liberou e não girou" em série (pista travada ou sentido trocado)

- **Problema.** Braço pesado/travando é queixa recorrente ([GIRO-PESADO], [FAQ-BOX]); sentido de
  liberação errado (F1, HIL-DIR-05/06) produz o mesmo sintoma: a pessoa empurra e nada gira.
- **Como.** Por pista, k autorizações seguidas terminando em origem 5 sem 6 (S2; `DEC:183-188`;
  `ticket_use_attempt` com `passage_confirmed_at` nulo). Mensagem: "Catraca 3: 3 liberações seguidas sem
  giro. Verifique o braço; se a catraca foi trocada ou reinstalada, confira o sentido de liberação". Se
  a impressão digital mudou (C8) ou a configuração foi aplicada há pouco (`ConfiguracaoAplicadaEm`,
  `PUMP:487-488`), a hipótese "sentido trocado" sobe.
- **Novo.** Usa a sequência de origens para diagnosticar mecânica e comissionamento em tempo real.
- **Risco.** Pessoas desistindo em sequência (ingresso no bolso) → limiar k e janela de tempo; só alerta.
- **Classificação.** IMPLEMENTÁVEL AGORA.
- **Teste.** Simulador: 3 × (leitura válida, origem 5) → 1 alerta; intercalar uma origem 6 → zera.
- **Esforço.** P.

### C5. Saúde do giro por pista (cronômetro e desperdício)

- **Problema.** Medir fluidez e degradação mecânica sem sensor novo.
- **Como.** Para cada tentativa consumida: Δ = `passage_confirmed_at − at` (S3); por pista e por janela
  de 15 min: mediana, p90, taxa de "sem giro". Comparar a pista com a mediana das outras **na mesma
  janela** (mesmo público). Tendência dentro do evento (Δ crescendo só numa pista = braço endurecendo).
  Medidor de desperdício do docs/14:76-79.
- **Novo.** Nenhuma chamada nova: só leitura de dado já gravado.
- **Risco/limite honesto.** Δ é dominado pelo comportamento humano; resolução limitada à volta do laço
  (até `limiteDeEspera` 500 ms × catracas do worker, `src/Edge.Worker/DeviceGroupLoop.cs:75`). Desgaste
  mecânico só como **comparação relativa e persistente**, nunca como laudo.
- **Classificação.** IMPLEMENTÁVEL AGORA (consulta; tela na Etapa C).
- **Teste.** Unidade sobre base fabricada; simulador com `AtrasoAntes` diferente por catraca
  (`SimulatedDevice.cs:15`).
- **Esforço.** P.

### C6. Display orientador por motivo de negação

- **Problema.** Hoje toda negação mostra "Acesso nao autorizado" por 3 s (`PUMP:754`); a pessoa não sabe
  o que fazer e trava a fila (docs/14:91-93).
- **Como.** **Mesma chamada** EI-057 que já existe, com o texto escolhido pelo `ReasonCode` da decisão
  (`src/Access.Domain/Access/ReasonCodes.cs`): p. ex. `ForaDaUrna` → "USE A URNA AO LADO";
  `ForaDaJanela` → "FORA DO HORARIO"; `CredencialDesconhecida` → "PROCURE O BALCAO". Texto ≤ 32,
  sem acento até T27, **nenhum dado do titular nem do código**. Tabela motivo → texto configurável,
  com "genérico" como padrão por motivo.
- **Novo.** O display vira canal de orientação; zero chamada a mais.
- **Risco.** Oráculo para fraude ("JA UTILIZADO" confirma que o código existe) → padrão genérico para os
  motivos sensíveis (usado, cancelado, bloqueado), escolha do dono. Leitura difícil se a mensagem for
  longa → teste de legibilidade.
- **Classificação.** IMPLEMENTÁVEL AGORA, atrás de chave (`catraca.mensagem_por_motivo`).
- **Teste.** Unidade (cada motivo → texto ≤ 32, sem dígitos do código); `AdapterTests` (mesma função,
  mesmo número de chamadas); bancada INT-MSG-02 + NOVO-INT-MSG-06 (legibilidade a 1 m, acentos).
- **Esforço.** P.

### C7. Urna e display: "urna cheia" e "sensor da urna sujo"

- **Problema.** Origem 20 hoje é só texto; o público continua tentando a pista.
- **Como.** Origem 20 em `Polling` ⇒ `EnviarMensagemPadraoOnLine("URNA CHEIA USE OUTRA")` (EI-056, uma
  chamada, fora de passagem) + alerta; volta ao padrão na reconexão ou por comando "urna esvaziada".
  Padrão "leitura na fenda aprovada, sem origem 7" repetido ⇒ "verifique o sensor da urna"
  ([NAO-RECOLHIDO]: "Limpar sensor da urna").
- **Classificação.** IMPLEMENTÁVEL AGORA (simulador já tem urna cheia, `InnerSimulator.cs:102`); comportamento
  real DEPENDE DE BANCADA (T33; urna recolhendo é A.11).
- **Teste.** SIM-URNA-12-like: 3 recolhimentos + origem 20 → mensagem padrão trocada uma vez.
- **Esforço.** P.

### C8. Check-up pré-abertura (diagnóstico não intrusivo)

- **Problema.** Hoje só se descobre que a pista está ruim com o público na frente.
- **Como.** Comando "Check-up" (padrão A.8: chave, só em `Polling`, um passo por chamada):
  1. `Ping` ×5 (EI-004) → latência e falhas (S5);
  2. `ReceberVersaoFirmware` (EI-006) → impressão digital (C9);
  3. `ReceberRelogio` (EI-007) → deriva (S8);
  4. versão aceita = versão salva (A.5/A.6, sem chamada);
  5. `EnviarMensagemTemporariaOnLine("TESTE CATRACA n", 5 s)` (EI-057) → o técnico confirma visualmente;
  6. opcional: `AcionarBipCurto` (EI-048, chave própria) → confirma o bip;
  7. `EnviarFormasEntradasOnLine` (EI-032) → deixa o leitor rearmado;
  8. opcional e **assistido**: liberação manual com motivo "check-up" → espera origem 6 e pergunta "por
     onde girou?" (docs/04-workflow-collect-card-then-enter.md:169-180).
  Resultado: cartão por pista "pronta / atenção / não abrir", com o motivo.
- **Novo.** Juntar funções documentadas num roteiro reproduzível; nenhum integrador clássico fazia isso
  fora do instalador.
- **Risco.** Nenhuma chamada nova à DLL; cada passo em `Polling`; o passo 8 só com operador. Nunca durante
  a operação (o painel só oferece com a pista sem leitura há X min).
- **Classificação.** IMPLEMENTÁVEL AGORA (simulador); passo 6 DEPENDE DE BANCADA (INT-UX-03).
- **Teste.** Integração serviço + worker + simulador (sequência exata de funções pela costura, uma por
  passo); simulador com `Desconectado` no meio → "não abrir".
- **Esforço.** M.

### C9. Impressão digital do equipamento (troca de catraca ou firmware)

- **Problema.** O perfil físico (sentido de liberação) é do **lugar**; o Inner é gravado na catraca
  (A01:133-135). Trocar o equipamento por outro configurado com o mesmo Inner herda um sentido que pode
  estar errado.
- **Como.** A cada conexão, `FirmwareInfo` (linha, variação, versão, bio — `ITA:146`) é comparado ao
  último registrado daquele Inner; mudou ⇒ alerta "equipamento trocado ou firmware atualizado: confira o
  sentido" e C4 passa a vigiar com limiar menor.
- **Limite honesto.** Duas catracas com o mesmo firmware são indistinguíveis: não há número de série
  documentado (`ReceberInformacoesInner` só tem nome, INV:180, T11).
- **Classificação.** IMPLEMENTÁVEL AGORA.
- **Teste.** Simulador: trocar `SimulatedDevice.Firmware` (`SimulatedDevice.cs:51`) entre reconexões → 1
  alerta; mesma versão → 0.
- **Esforço.** P.

### C10. Correlação entre pistas (evacuação, alarme, queda de energia, rede)

- **Problema.** Um evento que afeta várias pistas gera N alertas soltos e esconde a causa.
- **Como.** Janela curta (p. ex. 10 s): (a) giros órfãos em ≥ 2 pistas ⇒ "possível liberação por alarme
  de incêndio/anti-pânico" ([INCENDIO], [ESP-BOX4]) — agrega e suprime C3/R3; (b) várias pistas caindo
  juntas ⇒ "queda de rede/energia do setor" em vez de N "catraca caiu"; (c) uma pista só caindo logo após
  origem 6 ⇒ "reinício ao girar: verifique a fonte" ([REINICIA-GIRO]).
- **Novo.** Raciocínio sobre o conjunto, determinístico e explicável.
- **Risco.** Inferência errada → sempre "possível", com os fatos que levaram à conclusão.
- **Classificação.** IMPLEMENTÁVEL AGORA (lógica); a premissa (a) DEPENDE DE BANCADA (NOVO-HIL-GIRO-03).
- **Teste.** Simulador com 4 catracas: origem 6 órfã nas 4 em 3 s → 1 alerta agregado.
- **Esforço.** P/M.

### C11. Diagnóstico da queda: rede × energia (relógio antes do acerto)

- **Problema.** Depois de uma queda, ninguém sabe se faltou energia ou rede — e isso muda a conduta.
- **Como.** Na reconexão, **antes** do `EnviarRelogio`, uma leitura `ReceberRelogio` (EI-007) no
  primeiro passo em `Polling` (onde o relógio já é tratado, `PUMP:964-977`): data inválida ou deriva
  grande ⇒ "a catraca ficou sem energia"; deriva normal ⇒ "queda de rede". Deriva lenta medida de hora
  em hora comparada à precisão publicada ("1 minuto por ano", [ESP-BOX4]) ⇒ alerta de bateria/relógio.
- **Atenção.** Fontes conflitam: "~1 hora sem energia" (A01:91) × "bateria interna para manter o
  relógio" ([ESP-BOX4]). `A_CONFIRMAR` → NOVO-HIL-PWR-01.
- **Classificação.** IMPLEMENTÁVEL AGORA atrás de chave (uma chamada a mais por conexão, fora de
  passagem); interpretação DEPENDE DE BANCADA.
- **Teste.** Simulador (`LerRelogio` devolvendo inválido após `Reiniciar()`, `SimulatedDevice.cs:197`).
- **Esforço.** P.

### C12. Saúde da comunicação (antes da queda)

- **Problema.** A queda só aparece quando acontece; o watchdog só pega o laço parado.
- **Como.** Ligar `MetricasDoEdge` (F12) e publicar por pista: p95 de `Elapsed` de `ReceberDadosOnLine` e
  `Ping`, `ErrosDeRecepcao`, reconexões/hora, retornos por função (`RetornosDocumentados`). Alerta de
  tendência ("catraca 7: latência 4× a das outras na última meia hora").
- **Classificação.** IMPLEMENTÁVEL AGORA.
- **Teste.** Unidade com `CosturaFalsa` atrasando uma catraca; integração com a situação publicada.
- **Esforço.** P.

### C13. Sentinela de silêncio do leitor (rearme preventivo)

- **Problema.** "O leitor trava se o ciclo de acesso não fechar; a catraca para de ler sem dar erro"
  (docs/14:81-84).
- **Como.** Pista em `Polling` sem nenhuma leitura há X min **enquanto as vizinhas leem** (silêncio
  relativo) ⇒ alerta; com chave, um `EnviarFormasEntradasOnLine` (EI-032, o rearme oficial, FUN:33 e
  FUN:47) em `Polling`.
- **Classificação.** Alerta IMPLEMENTÁVEL AGORA; rearme automático DEPENDE DE BANCADA (reproduzir o
  travamento: HIL-ERR-02).
- **Teste.** Simulador: duas catracas, só uma recebe leituras por 5 min → 1 alerta, 1 EI-032 (com chave).
- **Esforço.** P.

### C14. Caixa-preta pós-queda (bilhetes como segunda testemunha)

- **Problema.** Depois de queda ou para auditoria, só temos a nossa versão.
- **Como.** Com a pista fora de operação, coleta (A.9, por comando, `PUMP:843-855`) e reconciliação:
  `collected_ticket` (HMAC, `015_bilhetes_coletados.sql:21-35`) × `ticket_use_attempt` (mesma impressão,
  mesmo minuto) × giros órfãos (C3). Diferenças: marcação da catraca sem tentativa nossa (giro com
  master/botoeira/WebServer), tentativa nossa sem marcação. 128 em quantidade ⇒ coleta anterior
  interrompida.
- **Premissa a confirmar.** Em **on-line**, a catraca grava marcação na memória? A Topdata diz que "o
  registro somente é realizado na memória da Catraca após o giro ser concluído" e que ela armazena
  "nos casos em que a conexão com o computador não for permanente" ([OP4]) — não fecha o caso on-line →
  T41, NOVO-INT-REC-09. E se o "Buffer" estiver em "Segue", marcações antigas já podem ter sido
  sobrescritas; em "Para", a catraca exibe "Memória Cheia" ([MEM-CHEIA], T37, NOVO-INT-REC-08).
- **Classificação.** DEPENDE DE BANCADA.
- **Esforço.** M.

### C15. Bip como feedback de problema da pista (não da pessoa)

- **Avaliação.** "Bip só em erro" por pessoa negada acrescenta uma chamada no fluxo de negação e foi
  excluído de propósito (`Decisao_automatica_nunca_bipa`, docs/34:576; docs/34:148). **Refinado:** bip
  longo **uma vez** quando a pista muda para um estado que o orientador precisa ver (urna cheia,
  check-up reprovado, C4 disparado), sempre em `Polling`.
- **Classificação.** DEPENDE DE BANCADA (INT-UX-03). **Esforço.** P.

### C16. Configuração aplicada × salva por hash

- **Avaliação.** **Já existe** (A.5/A.6: `VersaoDaConfiguracao`, docs/34:576). A extensão "detectar
  configuração mudada por fora (WebServer)" exige leitura de volta (`ReceberConfiguracoesInner`, T12) →
  DEPENDE DA TOPDATA. Como mitigação sem leitura: o WebServer fica indisponível on-line (A01:92-93) e
  cada reconexão reenvia tudo (ADR-0020); C9 avisa troca de equipamento. **Não é composição nova.**

### Ideias descartadas ou rebaixadas

| Ideia | Motivo |
|---|---|
| Medir "desgaste mecânico" absoluto pelo tempo de giro | Δ é comportamento humano + volta do laço; só comparação relativa (C5) |
| Detectar carona com dois corpos num giro | A catraca não informa; nenhum sinal documentado distingue |
| Ler contador de giros AH/H para lotação | `LerContadorGiro` só tem nome (INV:145); proibido declarar (T17) |
| Ler quantidade de bilhetes para o "medidor de memória" | `ReceberQuantidadeBilhetes` com array de tamanho desconhecido (T20): corrompe memória |
| Sirene no público em caso de carona | pânico em evento; trocado por aviso discreto (R3) |
| LLM decidindo liberação | fora do princípio; tudo acima é determinístico e explicável |

---

## 8. Perguntas novas para a Topdata (continuação de T1–T35 e das T36–T39 do 03)

| # | Pergunta | Ensaio |
|---|---|---|
| T36 (do 03) | WebServer "Direita/Esquerda" × `LiberarCatraca*Invertida`: qual vale; `EnviarConfiguracoes` desfaz? | NOVO-HIL-MAP-02 |
| T37 (do 03) | "Buffer: Para/Segue" existe na Catraca 4? padrão? o SDK lê/grava? com "Para" a catraca segue liberando? | NOVO-INT-REC-08 |
| T38 (do 03) | Em off-line a Catraca 4 libera "qualquer entrada" (relato de integrador)? em que configuração? | NOVO-INT-OFF-13 |
| T39 (do 03) | Contador de giros por sentido do menu Master: lido pela função de T17? zera? | NOVO-HIL-CHK-02 |
| T14 (ampliada) | `Complemento` da origem 5 identifica o relé? da origem 6 traz o sentido (AH/H)? | NOVO-HIL-REL-06, NOVO-HIL-GIRO-02 |
| T40 | Relé 2 da TopFit 4 sem urna: contato NA2/C2(/NF2) ou saída 12 Vcc/1 A? Corrente máxima? | NOVO-HIL-REL-04 |
| T41 | Em regime on-line a catraca grava marcação na memória a cada giro? | NOVO-INT-REC-09 |
| T42 | Giro por botoeira, por master ("libera 30s") e com o NF de incêndio aberto gera origem 6? algum evento identifica a causa? | NOVO-HIL-GIRO-01, -03 |
| T43 | Funções 1–3 e 6–9 no **relé 2**: quando o contato fecha (liberação, giro, registro)? Funções 6–8 deixam o giro livre? | NOVO-HIL-REL-05, -08 |
| T44 | Qual o padrão da DLL de `DefinirFuncaoDefaultLeitoresProximidade` (EI-022)? Ele prevalece sobre leitor e acionamento como diz [OP4]? | INT-CFG-06 |
| T45 | `ManterRele2Acionado`/`DesabilitarRele2`/`DefinirLogicaRele`: semântica e faixas | NOVO-HIL-REL-03 |

---

## 9. Ranking: 10 melhores para implementar já

| # | Composição | Por que agora | Classificação | Esforço |
|---|---|---|---|---|
| 1 | **C1 Prazo real do giro** | fecha um defeito dormente que para a pista sem erro (F9) | IMPLEMENTÁVEL AGORA | P |
| 2 | **C2 Caderno de sinais** | sem gravar, nenhuma inteligência acima tem matéria-prima; base de 3–10 | IMPLEMENTÁVEL AGORA | M |
| 3 | **C3 Sentinela de giro órfão** | segurança (carona/forçamento/master) com dado que já chega e hoje se perde (F10) | IMPLEMENTÁVEL AGORA (premissa: bancada) | P |
| 4 | **C4 Liberou e não girou em série** | pega braço travado e sentido trocado (F1) em minutos, não no fim do dia | IMPLEMENTÁVEL AGORA | P |
| 5 | **C5 Saúde do giro por pista** | zero chamada nova; prestação de contas e operação | IMPLEMENTÁVEL AGORA | P |
| 6 | **C6 Display orientador por motivo** | mesma chamada de hoje; destrava fila; sem dado pessoal | IMPLEMENTÁVEL AGORA (chave) | P |
| 7 | **C8 Check-up pré-abertura** | demonstração forte para a Topdata; só funções documentadas | IMPLEMENTÁVEL AGORA | M |
| 8 | **C9 Impressão digital do equipamento** | protege o comissionamento do sentido | IMPLEMENTÁVEL AGORA | P |
| 9 | **C12 Saúde da comunicação** | liga métricas que já existem (F12) | IMPLEMENTÁVEL AGORA | P |
| 10 | **C10 Correlação entre pistas** | evita tempestade de alertas e explica evacuação/queda | IMPLEMENTÁVEL AGORA (premissa: bancada) | P/M |

**Logo depois (bancada curta):** R1 acionar relé 2 pelo painel (P) → NOVO-HIL-REL-04/06; R5 revista
sorteada on-line; R4 pulso para câmera; C7 urna cheia no display; C11 rede × energia; C13 rearme
preventivo.

**Para depois:** R2/R6 sinaleiro homem-morto (NOVO-HIL-REL-06/07), C14 caixa-preta (T41), entradas 8–10
(`DefinirEventoSensor`, T45), C15 bip de pista (INT-UX-03), leitura do contador AH/H e do sentido no
`Complemento` (T14, T17) — esta última é a que daria **lotação real** e "giro reverso"; R10 (off-line).

**Para não fazer:** relé 1 fora do giro; funções 6–9 nos relés; sirene ao público; carga direta na saída;
qualquer "inteligência" que decida liberar.
