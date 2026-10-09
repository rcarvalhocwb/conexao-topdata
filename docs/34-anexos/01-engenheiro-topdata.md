# Estudo fino da catraca Topdata (linha Inner / EasyInner.dll) para o módulo "catraca" do Rayzer XAcess

> Autor: engenheiro de integração Topdata (agente). Data: 2026-09-30. Repositório lido em `78517de`.
> Escopo: tudo o que a EasyInner.dll oferece, o que o produto já usa, o que vale para eventos
> (ingresso QR + cartão de bilheteria + urna) e o que precisa de bancada/Topdata.
>
> **Regra de leitura deste relatório.** Toda afirmação sobre a DLL traz evidência no formato
> `arquivo:linha` (relativo à raiz do repositório) ou URL pública. Selos usados:
> - `FONTE_PRIMARIA` — está no manual oficial ou no SDK 6.0.2.0 (EasyInner.cs / Enumeradores.cs /
>   tabela de exportação), conforme registrado no repositório.
> - `ASSINATURA_SDK` — só a assinatura P/Invoke é conhecida (do EasyInner.cs do SDK, via
>   `docs/compatibility-matrix/inventario-completo-dll.csv`); **semântica, faixas e retornos
>   NÃO são conhecidos**. Tratar a semântica como `A_CONFIRMAR_COM_TOPDATA`.
> - `SO_O_NOME` — só o nome existe na tabela de exportação; nem assinatura. Proibido declarar.
> - `INFERIDO` — dedução minha a partir do nome/contexto; nunca vira código habilitado.
> - `A_CONFIRMAR_COM_TOPDATA` — ambíguo ou ausente; abstração desabilitada por padrão + teste de bancada.
>
> Abreviações de caminho: `FUN` = `docs/compatibility-matrix/funcoes-easyinner.csv`;
> `INV` = `docs/compatibility-matrix/inventario-completo-dll.csv`; `GER` =
> `src/Topdata.EasyInner.Interop/EasyInnerGerada.cs`; `NAT` =
> `src/Topdata.EasyInner.Interop/EasyInnerNative.cs`; `IEN` =
> `src/Topdata.EasyInner.Interop/IEasyInnerNative.cs`; `ADP` =
> `src/Topdata.EasyInner.Adapter/TopdataInnerAdapter.cs`; `ITA` =
> `src/Access.Application/Devices/ITopdataInnerAdapter.cs`; `CFG` =
> `src/Access.Application/Devices/DeviceConfiguration.cs`; `PUMP` = `src/Edge.Worker/DevicePump.cs`;
> `FSM` = `src/Access.Application/Devices/DeviceStateMachine.cs`. Nos CSVs, a linha citada é a
> linha física do arquivo (linha 1 = cabeçalho).

## 0. Sumário executivo (achados que mudam a implementação)

1. **A superfície real da DLL é 265 funções, não 774** (as demais são wrappers JNI duplicados).
   `INV` tem 265 linhas de dados (INV:2–266); 58 foram descritas no manual (`FUN`), 38 estão
   escritas à mão (`NAT`), 191 geradas (`GER`), 36 só têm o nome (commit `99ed59b`, `69a4648`).
2. **Bug crítico de configuração: dígitos variáveis nunca são enviados.** `CFG:30`
   (`QuantidadesVariaveisDeDigitos`) existe e a bancada preenche 4..16
   (`src/Edge.Worker/Bancada/SessaoDeBancada.cs:34`), mas `ADP:155-173` não chama
   `InserirQuantidadeDigitoVariavel` (EI-012, FUN:13) e essa função nem está em `IEN`
   (só em `GER:615`). Com `QuantidadeFixaDeDigitos` nulo, `ADP:158-160` também não chama
   `DefinirQuantidadeDigitosCartao`. Pela regra da ADR-0020 (defaults da DLL vazam,
   `docs/11-capacidades-do-sdk.md:166-173`), a catraca recebe o **padrão da DLL** para dígitos —
   valor desconhecido. Um QR de ingresso pode ser truncado/recusado sem erro.
3. **A sequência oficial offline→mudança→online não é seguida de fato.** `PUMP:306-308` chama
   o mesmo `EnviarConfiguracaoCompleta(d.Configuracao)` nos três estados; `ADP:166` escolhe
   `ConfigurarInnerOnLine` ou `OffLine` por `configuracao.Online` (sempre `true` na operação,
   `SessaoDeBancada.cs:42`). Resultado: a "cfg offline" nunca é enviada, e
   `EnviarConfiguracoesMudancaAutomaticaOnLineOffLine` (EI-029, FUN:30) nunca é chamada
   (existe em `NAT:193`, falta em `IEN`). O manual manda `ESTADO_ENVIAR_CFG_OFFLINE →
   ESTADO_ENVIAR_CONFIGMUD_ONLINE_OFFLINE → ESTADO_ENVIAR_CFG_ONLINE`
   (`docs/11-capacidades-do-sdk.md:227-233`). Contingência offline hoje **não existe**.
4. **Possível dupla inversão de sentido.** `PUMP:524-526` troca `Entrada` por `Saida` quando
   `SentidoInvertido`, e `ADP:240-250` *também* troca para a variante `...Invertida`. Com
   perfil invertido, o produto chama `LiberarCatracaSaidaInvertida`. Não há fonte que diga que
   isso é o correto — `A_CONFIRMAR_COM_TOPDATA` e teste HIL-DIR-05/06 obrigatório.
5. **Bilhetes coletados são descartados.** `PUMP:475-485` recebe o `Bilhete` e só loga o tipo;
   `ColetarBilhete` remove o bilhete do equipamento (FUN:40; `docs/11-capacidades-do-sdk.md:101-106`).
   Hoje isso não dispara porque nenhum código dispara `IniciarColetaDeBilhetes`/`CairParaListaLocal`
   (só a tabela, `FSM:203-204`), mas no dia em que alguém ligar a coleta, **perde evento**.
6. **Lista de acesso na catraca não está implementada** (`PUMP:396-402`, "vazio nesta fase").
   Capacidade publicada: 15.000 (14.900 com 16 dígitos) na produção atual (limites-de-capacidade.csv:2-3).
   Alterar 1 usuário = reenviar a lista inteira (FUN:36). Para 30 mil ingressos, lista branca
   não cabe (limites-de-capacidade.csv:2).
7. **Divergências de assinatura entre `FUN` e `INV`/código que precisam ser corrigidas na matriz:**
   `AcionarRele2` (FUN:48 diz "(int, byte Tempo)" na coluna 4; SDK/INV:6 e NAT:265 dizem só `int Inner`);
   `SetarBioVariavel`/`ConfigurarBioVariavel` (FUN:26-27 dizem `byte`; INV:246/27 e GER:332/341
   dizem `void (int Maior)`); `TestarConexaoInner` e `PingOnline` (FUN:5-6) **não existem**
   como export — o produto usa `Ping`/`PingOnLine` (NAT:93-103, INV:166-167);
   `LigarBackLite`/`DesligarBackLite` estão em FUN:55-56 como FONTE_PRIMARIA, mas **não têm
   assinatura publicada** (INV:84, INV:155) — proibido declarar.
8. **Sinalização (bip/LED) está declarada mas não exposta**: `AcionarBipCurto/Longo` e LEDs
   estão em `NAT:296-318`, mas faltam em `IEN` e `ITA`. `LigarLedVerde/Vermelho` são só Linha 3
   (FUN:51-54). Em TopFit/Catraca 4 (Linha 4) o feedback é display + bip.
9. **A linha homologada padrão é {14, 16}** (`PUMP:231`), mas a tabela de códigos de `Linha`
   está desalinhada no manual (`docs/11-capacidades-do-sdk.md:249`). O mapeamento
   linha→modelo precisa sair da bancada (`ReceberVersaoFirmware` numa TopFit 4 real).

10. **Fontes públicas da Topdata lidas nesta sessão (Firecrawl) trazem fatos novos, não registrados no repo:**
    - O leitor de QR da Catraca 4 *"somente funciona com a configuração **Código de Barras Serial**"*,
      padrão Livre + "Habilitar número de dígitos variáveis" (4–16); com leitor prox/Mifare junto,
      estes se autoconfiguram ABA Track 14 (prox) / 10 (Mifare); leitor 5 V, UART 3,3 V; QR
      numérico **ou alfanumérico** — https://suporte.topdata.com.br/suporte/leitor-qr-code-nas-catracas/ .
      No enum do SDK, "barras serial" é o **5** (FUN:14). O default da bancada é **8**
      (`SessaoDeBancada.cs:31`, `Program.cs` `--tipo-leitor` default "8"). Mapear "Código de Barras
      Serial"→5 é `INFERIDO` (por nome), mas é mais bem fundamentado que 8 → mudar o default da
      bancada para 5 e deixar 8 como variante de ensaio.
    - A memória de marcações é **circular**: *"Caso a capacidade total de marcações seja atingida, a
      marcação mais nova ocupa espaço na memória da marcação mais antiga"* (30.000 registros) —
      https://suporte.topdata.com.br/suporte/web-server-nas-catracas-4/ (seção Registros). Estouro
      **apaga evento em silêncio**; não "trava".
    - Na lista de acesso, **cada associação cartão↔tabela de horário consome uma das 15.000
      posições** (mesma página, "Controle de acesso do cartão"). Um cartão com 3 horários ocupa 3.
    - Relógio da Catraca 4 se mantém só **~1 hora sem energia** (mesma página, "Relógio").
    - WebServer de fábrica: usuário e senha de fábrica publicados no manual; indisponível com a catraca on-line; pode
      ser desabilitado (mesma página). Existe `DesabilitarWebServer(byte)` na DLL (INV:83).
    - Tempos de acionamento no WebServer: **1–255 s, padrão 5 s**; no SDK: **0–50 s** (FUN:17-18).
      Nº do Inner no WebServer: **001–255**; no SDK/produto: **1–99** (FUN:5; `ComandoDeCatraca.cs:95`).
      Divergências a levar à Topdata (T8, T9).
    - Leitor 1 no WebServer tem 7 modos (inclui "Entrada Invertido", "Saída Invertido", "Entrada e
      Saída Invertido"); o enum do SDK tem 0–4 (FUN:15). Não inventar 5/6 — T10.

---

## 1. Catálogo das funções, por domínio

**Legenda das colunas.** *No produto*: `IEN` = exposta na costura testável; `NAT` = declarada à mão;
`GER` = só declarada (gerada, nunca chamada); `—` = não declarada (sem assinatura publicada).
*Uso*: onde o produto chama (adapter/pump). *Evid.*: FP = `FONTE_PRIMARIA`, SDK = só assinatura
(`ASSINATURA_SDK`), NOME = `SO_O_NOME`, INF = `INFERIDO`, AC = `A_CONFIRMAR_COM_TOPDATA`.
*Bancada*: id existente no repo (FUN coluna `teste`) ou novo (prefixo `NOVO-`).

Retornos genéricos conhecidos (valem para todo `byte` salvo indicação): **0 = sucesso**, **1 = erro**,
**8 = GPF/dependência** (ITA:55-61; FUN:3). Parâmetro inválido nas funções de montagem: **128/129/130**
conforme a posição do parâmetro (FUN:11-12, 15-16, 21-24, 29, 34-35). Valores de "sem eventos" e
"sem bilhetes" (`RET_SEM_BILHETES`) **não conhecidos** (`docs/11-capacidades-do-sdk.md:248`) → AC.

### 1.1 Comunicação e conexão

| Função | Assinatura (SDK 6.0.2.0) | Parâmetros / faixas | Retornos | Modo | Efeito | Pré-condição | Risco | No produto | Evid. | Bancada |
|---|---|---|---|---|---|---|---|---|---|---|
| `DefinirTipoConexao` | `byte (byte Tipo)` (INV:76) | 0 serial RS232/485 · 1 TCP porta variável · 2 TCP porta fixa (default) · 3 modem · 4 TopPendrive (FUN:2) | 0 OK · 9 tipo inválido (FUN:2) | ambos | nenhum físico | antes de `AbrirPortaComunicacao` (`ADP:63-68`) | baixo | IEN:26 · usado `ADP:64` com 2 | FP | UNIT-EI-001 |
| `AbrirPortaComunicacao` | `byte (int Porta)` (INV:2) | porta TCP, padrão 3570 (FUN:3; WebServer: "3570 independentemente do número do Inner") | 0 · 2 não aberta · 3 já aberta · 4–6 DLL de apoio ausente · 8 GPF (FUN:3) | ambos | abre socket servidor | **uma vez por processo**, antes do laço (`docs/11…:237-238`) | alto (singleton; porta por worker ADR-0021) | IEN:29 · `ADP:70` | FP | INT-SM-001 / REL-05 |
| `FecharPortaComunicacao` | `void ()` (INV:122) | — | void | ambos | fecha socket | porta aberta | médio (retorno 3 na reabertura se vazar) | IEN:32 · `ADP:79`, `ADP:308` | FP | UNIT-EI-003 |
| `Ping` | `byte (int Inner)` (INV:166) | Inner | 0 OK (FUN:5 descreve "TestarConexaoInner", que **não existe** — NAT:93-97) | ambos | nenhum | porta aberta | baixo | IEN:35 · `ADP:84` (TestarConexao) | FP (nome corrigido pelo SDK) | INT-SM-002 |
| `PingOnLine` | `byte (int Inner)` (INV:167) | Inner | 0 OK | on-line | **mantém o regime on-line**; sem ele, com mudança automática modo 2, a catraca cai para off-line (FUN:6) | exigido com `HabilitarMudancaOnLineOffLine(2,…)` | alto | IEN:38 · `ADP:86` — **nunca chamado pelo PUMP** (grep: só ADP) | FP | INT-SM-010 |
| `ConectarModem` | `int (int Porta, string Str, int Tom, string Telefone, int Inner)` (INV:20) | — | AC | — | discagem | tipo conexão 3 | n/a | GER:355 | SDK | não usar |
| `EnviarStringInicializacaoModem` | `int (string Str)` (INV:116) | — | AC | — | modem | — | n/a | GER:495 | SDK | não usar |
| `LerByteModem` | `int ()` (INV:144) | — | AC | — | modem | — | n/a | GER:630 | SDK | não usar |
| `EnviarTempoKeepAliveRabbit`, `ExecInClientDLL`, `GetBufferConfigDLL`, `GetBufferMsgDLL`, `ResetarModoOnLine`, `LevantarParaOnLine`, `UtilizarCmdCurtoCatraca`, `Utilizar215`, `UtilizarRelogioSegundos` | **sem assinatura** (INV:117,119,123,124,217,148,264,263,265) | — | — | — | nomes sugerem keep-alive, leitura do buffer de config, forçar on-line, relógio com segundos (INF) | — | **proibido declarar** (corrupção de memória) | — | NOME | T11 (pedir assinatura de `LevantarParaOnLine`, `ResetarModoOnLine`, `UtilizarRelogioSegundos`, `GetBufferConfigDLL`) |

**Notas.**
- Uma instância da DLL atende ~30 equipamentos numa thread (limites-de-capacidade.csv:8;
  https://integrador.topdata.com.br/suporte/quantos-equipamentos-a-easyinner-dll-pode-gerenciar-simultaneamente/).
  O produto limita a 20 recomendados / 25 absoluto (`src/Edge.Worker/DeviceGroupLoop.cs:25-28`).
- A catraca é **cliente**; o PC escuta (`docs/21-roteiro-da-bancada.md:103-106`). O IP do servidor e a
  porta ficam gravados **na catraca** (WebServer "Endereço IP do Servidor", página web-server-nas-catracas-4).
  Mover catraca de worker = reconfigurar a catraca (ADR-0021:37-38).

### 1.2 Identificação, versão, firmware, modelo

| Função | Assinatura | Saídas | Modo | Uso para | No produto | Evid. | Bancada |
|---|---|---|---|---|---|---|---|
| `ReceberVersaoFirmware` | `byte (int Inner, ref byte Linha, ref short Variacao, ref byte VersaoAlta, ref byte VersaoBaixa, ref byte VersaoSufixo, ref byte InnerAcessoBio)` (INV:199) | linha, variação, versão, se tem bio | ambos | capability discovery (ADR-0010) | IEN:41 · `ADP:88-99` · `PUMP:352-365` | FP (FUN:7) | HIL-CAP-01 |
| `ReceberVersaoFirmware6xx` | idem + `ref byte TipoModBio` (INV:200) | + tipo de módulo bio | ambos | firmware 6.xx (INF pelo nome) | GER:572 | SDK | NOVO-HIL-CAP-02 |
| `ReceberVersaoFirmware6xx_ComComplementar` | idem + `ComplementarVersaoSuperior`, `ComplementarVersaoInferior`, `TipoDeOperacao` (INV:201) | + versão complementar e "tipo de operação" (INF: on-line/off-line?) | ambos | **potencial leitura do regime real** — AC | GER:575 | SDK | NOVO-HIL-CAP-03 |
| `ReceberConfiguracoesInner` | `byte (int Inner, byte[] ConfiguracoesInner)` (INV:171) | buffer bruto de configuração; **tamanho e layout desconhecidos** | ambos | leitura de volta p/ diff (ADR-0020 item 5) | GER:560 | SDK | NOVO-HIL-CFG-10 (só com buffer ≥ tamanho confirmado pela Topdata — T12) |
| `ReceberInformacoesInner`, `ReceberBufferConfig`, `ReceberBufferMsgA` | **sem assinatura** (INV:180,169,170) | — | — | diagnóstico | — | NOME | T12 |
| `ReceberModeloBio`, `ReceberVersaoBio`, `SolicitarModeloBio`, `SolicitarVersaoBio`, `RequisitarModeloBio/VersaoBio`, `RespostaModeloBio/VersaoBio` | ver §1.12 | modelo/versão do módulo biométrico | — | discovery bio | GER | SDK | HIL-BIO-01 |

**Tabela de `Linha` do firmware:** o PDF traz 8 códigos (1,2,3,6,7,14,16,18) para 7 descrições,
desalinhados (`docs/11-capacidades-do-sdk.md:249`). O produto homologa por padrão `{14, 16}`
(`PUMP:231`) **sem fonte** que diga que 14/16 = Linha 4/TopFit 4 → `A_CONFIRMAR_COM_TOPDATA` (T5).
A primeira leitura numa TopFit 4 real fecha a questão para aquele firmware (HIL-CAP-01).

### 1.3 Modos de operação: on-line, off-line, mudança automática (contingência)

Definições oficiais (https://integrador.topdata.com.br/suporte/qual-a-diferenca-entre-o-modo-online-e-offline/):
**On-line** — *"a catraca depende da autenticação em tempo real realizada pelo sistema para liberar o
mecanismo de giro"*. **Off-line** — *"armazena internamente uma lista de usuários e horários
permitidos, funcionando de forma independente do sistema"*.

| Função | Assinatura | Parâmetros | Retornos | Efeito | Pré-condição | No produto | Evid. | Bancada |
|---|---|---|---|---|---|---|---|---|
| `ConfigurarInnerOnLine` | `byte ()` (INV:33) | — | 0 | marca o buffer para on-line | só vale após `EnviarConfiguracoes` (FUN:19) | IEN:85 · `ADP:166` | FP | INT-SM-020 |
| `ConfigurarInnerOffLine` | `byte ()` (INV:32) | — | 0 | marca o buffer para off-line | idem (FUN:20) | IEN:88 · `ADP:166` (nunca efetivamente, ver §0.3) | FP | INT-SM-020 |
| `HabilitarMudancaOnLineOffLine` | `byte (byte Habilita, byte Tempo)` (INV:128) | Habilita 0/1/2; Tempo 1–50 (unidade **não documentada** → AC) (FUN:29) | 0 · 128 habilita inválido · 129 tempo inválido | catraca muda sozinha de regime ao perder o PC | modo 2 exige `PingOnLine` periódico (FUN:29) | IEN:94 · `ADP:170-172` | FP (semântica de 0/1/2 parcial → AC) | INT-SM-021 |
| `DefinirEntradasMudancaOffLine` | `byte (byte Teclado, byte Leitor1, byte Leitor2, byte Catraca)` (INV:53) | **faixas não publicadas** | AC | define o que a catraca aceita ao cair para off-line (INF) | buffer de mudança | GER:404 | SDK | NOVO-INT-SM-022 |
| `DefinirEntradasMudancaOffLineComBiometria` | `byte (byte Teclado, byte Leitor1, byte Leitor2, byte Verificacao, byte Identificacao)` (INV:54) | AC | AC | idem com bio | idem | GER:83 | SDK | só com bio |
| `DefinirEntradasMudancaOnLine` | `byte (byte Entrada)` (INV:55) | AC (provavelmente mesmo domínio de `FormaEntrada`, INF) | AC | formas de entrada ao voltar a on-line (INF) | idem | GER:407 | SDK | NOVO-INT-SM-023 |
| `DefinirMensagemPadraoMudancaOffLine` / `…MudancaOnLine` | `byte (byte ExibirData, string Mensagem)` (INV:66-67) | ExibirData 0/1; Mensagem (32/16 chars por analogia a FUN:57 → INF) | AC | texto do display em cada regime | idem | GER:434,437 | SDK | NOVO-INT-MSG-05 |
| `EnviarConfiguracoesMudancaAutomaticaOnLineOffLine` | `byte (int Inner)` (INV:96) | Inner | 0 · 1 erro (FUN:30) | aplica o buffer de mudança automática | buffer montado (FUN:30) | NAT:193 · **fora do IEN, nunca chamada** | FP | INT-SM-021 |
| `EnviarBufferEventosMudancaAuto` | `byte (int NumInner)` (INV:91) | — | AC | envia eventos acumulados durante a mudança? (INF) | — | GER:477 | SDK | NOVO-INT-SM-024 |
| `ReceberDataHoraDadosOnLine` | `byte (byte Recebe)` (INV:177) | 0/1 (FUN:28) | 0 | on-line devolve data/hora no evento | buffer de config | GER:569 — **não enviado**: vai o default da DLL | FP | INT-CFG-07 |
| `ConfigurarBotaoExternoOffline` | `byte (byte Funcao)` (INV:28) | AC | AC | função da botoeira em off-line (INF) | config | GER:362 | SDK | NOVO-HIL-BOT-01 |
| `DefinirSensorPortaOffline` | `byte (byte Logica)` (INV:75) | AC | AC | lógica do sensor de porta em off-line (INF) | config | GER:455 | SDK | n/a p/ catraca |

**O que muda entre os regimes (para o produto):**

| Aspecto | On-line (T0/T1 — ADR-0017) | Off-line nativo (T2) | Mudança automática |
|---|---|---|---|
| Quem decide | o software (`docs/11…:157-160`) | a catraca, pela lista + horários + tipo de lista (FUN:34-39) | a catraca passa de um para o outro sozinha (FUN:29) |
| Evento | `ReceberDadosOnLine` com segundos (FUN:41) | bilhete na memória, **sem segundos** (FUN:40) | ao voltar: coletar bilhetes antes de operar (`FSM:241`) |
| Anti-passback entre pistas | sim (software) | **não** (`docs/14-estudo-de-caso-evento-30k.md:221-224`) | — |
| Revogação | imediata no banco | reenviar a lista inteira (FUN:36) | — |
| Urna | software aciona o relé 2 (INF, docs/04:280) | firmware recolhe e libera sozinho (*"A catraca com urna em modo off-line, recolherá todos os cartões aproximados no leitor 2… para logo em seguida o giro ser liberado"* — https://suporte.topdata.com.br/suporte/linha-de-catracas-4/ trecho de busca) | — |
| WebServer | indisponível on-line (página web-server-nas-catracas-4) | disponível | — |

**Sequência oficial de conexão** (`docs/11-capacidades-do-sdk.md:227-233`): CONECTAR →
ENVIAR_CFG_OFFLINE → ENVIAR_CONFIGMUD_ONLINE_OFFLINE → ENVIAR_CFG_ONLINE →
CONFIGURAR_ENTRADAS_ONLINE → ENVIAR_MSG_PADRAO → POLLING ⇄ VALIDAR_ACESSO → LIBERAR_CATRACA →
MONITORA_GIRO → (volta a CONFIGURAR_ENTRADAS_ONLINE). A FSM do produto tem os estados
(`FSM:174-197`), mas o PUMP envia a mesma configuração nos três (`PUMP:306-308`) — ver §0.3 e §3.4.

### 1.4 Parametrização (montagem do buffer + envio)

Todas as funções abaixo **sem parâmetro `Inner`** escrevem num **buffer global da DLL**; só têm efeito
depois de um `Enviar…(Inner)`, que aplica tudo e **limpa o buffer** (ADR-0006:9-12). O que não for
setado vai com o **default da DLL** (ADR-0020:10-15). Logo: toda função desta seção que o produto
**não** chama hoje está sendo enviada com um valor que ninguém conhece. "Reinício": nenhuma fonte do
repositório diz que alguma configuração exige reiniciar a catraca; o fluxo oficial reaplica a cada
conexão (`docs/11…:227-233`) → "exige reinício?" = **AC** para todas; o produto hoje força reconexão
para aplicar (`PUMP:597-613`), o que é suficiente.

**Enviador usado** — `EC` = `EnviarConfiguracoes(Inner)` (FUN:31); `EM` =
`EnviarConfiguracoesMudancaAutomaticaOnLineOffLine(Inner)` (FUN:30); `EF` =
`EnviarConfiguracoesFuncoes(Inner)` (FUN:32, INV:95); `EMO` = `EnviarMensagensOffLine(Inner)` (INV:113).
Atribuição função→enviador é **INF** (pelo nome/assinatura) salvo onde FUN diz; T13 pede a tabela oficial.

| Função | Assinatura | Parâmetros / faixa (fonte) | Default | Retornos | Enviador | Hoje no produto | Evid. | Bancada |
|---|---|---|---|---|---|---|---|---|
| `DefinirPadraoCartao` | `byte (byte Padrao)` INV:72 | 0 Topdata · 1 Livre (FUN:11) | AC | 0 · 128 | EC | IEN:64 · `ADP:157` | FP | INT-CFG-02 / HIL-CARD-01 |
| `DefinirQuantidadeDigitosCartao` | `byte (byte Quantidade)` INV:74 | 4–16 (manual 4.1.2) **ou** 1–16 (tabela 4.1.9) — divergência (FUN:12); WebServer: 4–16 | AC | 0 · 128 | EC | IEN:67 · `ADP:158-160` **só se fixo** | FP (faixa AC) | HIL-CARD-02 |
| `InserirQuantidadeDigitoVariavel` | `byte (byte Digito)` INV:141 | 1–16; 0 desabilita; **uma chamada por tamanho aceito** (FUN:13) | AC | 0 | EC | **GER:615 — não chamada (bug §0.2)** | FP | HIL-CARD-02 |
| `ConfigurarTipoLeitor` | `byte (byte Tipo)` INV:47 | 0 barras · 1 magnético · 2 prox Abatrack2 · 3 Wiegand · 4 prox SmartCard serial · 5 barras serial · 6 Wiegand FC sem separador · 7 Wiegand FC com separador · 8 QR Code por letras (FUN:14) | AC | 0 | EC | IEN:70 · `ADP:161` | FP | HIL-CARD-03 |
| `ConfigurarLeitorProximidadeAcura` / `…HIDAbaTrack2` / `…MotorolaAbaTrack2` / `…SmartCard` / `…SmartCardAcura` / `…Wiegand` / `…WiegandFacilityCode` | `byte ()` INV:36-42 | sem parâmetro | — | AC | EC (INF) | GER:365-383 | SDK (relação com `ConfigurarTipoLeitor` AC) | NOVO-HIL-CARD-06 |
| `ConfigurarLeitorWiegandFacilityCodeSemSeparador` | sem assinatura INV:43 | — | — | — | — | — | NOME | — |
| `ConfigurarWiegandDoisLeitores` | `byte (byte Habilita, byte ExibirMensagem)` INV:48 | 0/1, 0/1 (FUN:25) | AC | 0 | EC | GER:392 — **default da DLL** | FP | HIL-CARD-05 |
| `ConfigurarLeitor1` | `byte (byte Operacao)` INV:34 | 0 desabilitado · 1 só entrada · 2 só saída · 3 entrada/saída (padrão L1) · 4 entrada/saída invertida (FUN:15) | 3 (FUN:15) | 0 · 128 | EC | IEN:73 · `ADP:162` | FP | HIL-DIR-01 |
| `ConfigurarLeitor2` | `byte (byte Operacao)` INV:35 | 0–4 idem; **leitor 2 = fenda da urna** (FUN:16) | 0 (FUN:16) | 0 · 129 | EC | IEN:76 · `ADP:163` | FP | HIL-DIR-02 / SIM-URNA-01 |
| `ConfigurarAcionamento1` | `byte (byte Funcao, byte Tempo)` INV:21 | Funcao 0 não usado · 1 registro entrada ou saída · 2 registro entrada · 3 registro saída · 4 sirene · 5 revista · 6 catraca saída liberada · 7 catraca entrada liberada · 8 liberada dois sentidos · 9 dois sentidos com marcação; Tempo 0–50 s (FUN:17) — WebServer 1–255 s | WebServer: 5 s | 0 | EC | IEN:79 · `ADP:164` | FP | HIL-DIR-03 |
| `ConfigurarAcionamento2` | `byte (byte Funcao, byte Tempo)` INV:22 | idem; relé 2 = urna (FUN:18) | WebServer: 5 s | 0 | EC | IEN:82 · `ADP:165` | FP | SIM-URNA-05 / HIL-URNA-01 |
| `DefinirLogicaRele` | `byte (byte Logica)` INV:61 | AC (NA/NF? INF) | AC | AC | EC | GER:419 — default | SDK | NOVO-HIL-REL-01 |
| `HabilitarTeclado` | `byte (byte Habilita, byte Ecoar)` INV:131 | 0–1; Ecoar 0–1–2 (FUN:21) | AC | 0 · 128 · 129 | EC | IEN:91 · `ADP:167-169` | FP | INT-CFG-04 |
| `DefinirConfiguracaoTecladoOnLine` | `byte (byte Digitos, byte EcoDisplay, byte Tempo, byte PosicaoCursor)` INV:51 | AC | AC | AC | EC (INF) | GER:398 | SDK | NOVO-INT-CFG-08 |
| `RegistrarAcessoNegado` | `byte (byte TipoRegistro)` INV:202 | 0–3 (FUN:22); WebServer: "registrar acesso negado / falha na verificação da digital" | AC | 0 · 128 | EC | GER:627 — default | FP (significado de 0–3 AC) | INT-OFF-08 |
| `DefinirFuncaoDefaultLeitoresProximidade` | `byte (byte Funcao)` INV:58 | 0–12 (FUN:23) | AC | 0 · 128 | EC | GER:416 — default | FP | INT-CFG-06 |
| `DefinirFuncaoDefaultSensorBiometria` | `byte (byte Funcao)` INV:59 | AC | AC | AC | EC | GER:86 | SDK | só bio |
| `DefinirNumeroCartaoMaster` | `byte (string Master)` INV:71 | até 14 dígitos, padrão Livre (FUN:24) | AC | 0 · 128 | EC | GER:449 — default | FP | SEC-MASTER-01 |
| `CartaoMasterLiberaAcesso` | `byte (byte Libera)` INV:15 | 0/1 (INF); WebServer "Master Libera acesso" (libera 30 s cartão negado) | AC | AC | EC | GER:588 — default | SDK | SEC-MASTER-01 |
| `UtilizarSenhaAcesso` | `byte (byte Utiliza)` INV:266 | 0/1 (INF); WebServer: senha de 4 dígitos no cartão padrão Topdata | AC | AC | EC | GER:470 | SDK | n/a (padrão Livre) |
| `DefinirCodigoEmpresa` | `byte (int Codigo)` INV:50 | 3 dígitos (WebServer, padrão Topdata) → INF | AC | AC | EC | GER:395 | SDK | n/a (padrão Livre) |
| `DefinirNivelAcesso` | `byte (byte Nivel)` INV:70 | 0–9 (WebServer, padrão Topdata) → INF | 0 (WebServer) | AC | EC | GER:446 | SDK | n/a |
| `ReceberDataHoraDadosOnLine` | `byte (byte Recebe)` INV:177 | 0/1 (FUN:28) | AC | 0 | EC | GER:569 — default | FP | INT-CFG-07 |
| `HabilitarMudancaOnLineOffLine` | ver §1.3 | | | | EC ou EM (AC) | `ADP:170` → EC | FP | INT-SM-021 |
| `DefinirTipoListaAcesso` | `byte (byte Tipo)` INV:77 | 0 não usar · 1 lista branca · 2 lista negra (FUN:34) | AC | 0 · 128 | EC (INF) | GER:511 — default | FP | INT-OFF-02 |
| `DefinirEventoSensor` | `byte (byte Sensor, byte Evento, byte Tempo)` INV:56 | AC | AC | AC | EC | GER:410 | SDK | NOVO-HIL-SEN-01 |
| `DefinirFuncaoSensor1` | sem assinatura INV:60 | — | — | — | — | — | NOME | — |
| `DesabilitarBipCatraca` / `DesabilitarBipColetor` | `byte (byte Desabilita)` INV:78-79 | 0/1 (INF) | AC | AC | EC | GER:591-594 — default | SDK | NOVO-INT-UX-04 |
| `DesabilitarBloqueioCatracaMicroSwitch` | `byte (byte Desabilita)` INV:80 | AC | AC | AC | EC | GER:597 — default | SDK | NOVO-HIL-MEC-01 (**segurança física**) |
| `DesabilitarWebServer` | `byte (byte Desabilita)` INV:83 | 0/1 (INF) | WebServer habilitado de fábrica | AC | EC | GER:606 — default | SDK | NOVO-SEC-WEB-01 |
| `AvisarQuandoMemoriaCheia` | `byte (byte Avisa)` INV:14 | 0/1 (INF) | AC | AC | EC | GER:585 — default | SDK | NOVO-INT-REC-04 |
| `DefinirPorcentagemRevista` | `byte (byte Porcentagem)` INV:73 | AC (0–100? INF) | AC | AC | EC | GER:452 | SDK | fora do escopo |
| `DefinirConfiguracoesFuncoes` | `byte (byte Funcao, byte Catraca, byte Rele1, byte Rele2, byte Lista, byte Biometria)` INV:52 | AC | AC | AC | **EF** | GER:401 | SDK | T13 |
| `HabilitarScoreFuncoes` | `byte (int Funcao, byte Score)` INV:129 | AC | AC | AC | EF? | GER:458 | SDK | só bio |
| `HabilitarScoreMensagemOffLine` | `byte (int Inner, byte Tipo, byte Habilitar)` INV:130 | AC | AC | AC | direto (tem Inner) | GER:461 | SDK | só bio |
| `ConfigurarTimeoutIdentificacao` / `ConfigurarNivelLFD` | `byte (byte …)` INV:46, INV:44 | AC | AC | AC | EC | GER:389, GER:386 | SDK | só bio |
| `DefinirFormasPictogramasMillenium` | `byte (byte Forma)` INV:57 | AC (linha Millenium, legado — INF) | AC | AC | EC | GER:413 | SDK | não usar |
| `SetarInnerOld` | `void (int Inner, int Old)` INV:247 | AC (compatibilidade com Inner antigo — INF) | — | void | — | GER:636 | SDK | não usar |
| `HabilitarCriptografia`, `ConfigurarComportamentoIndexSearch`, `HabilitaMudancaEventoSeta`, `HabilitaQrAsciiEstendido` | sem assinatura INV:127,30,125,126 | — | — | — | — | — | NOME | T11 |
| `EnviarConfiguracoes` | `byte (int Inner)` INV:94 | Inner | — | byte (FUN:31) | **é o enviador** | IEN:97 · `ADP:184` | FP | INT-CFG-05 / CHAOS-CFG-01 |
| `EnviarConfiguracoesFuncoes` | `byte (int Inner)` INV:95 | Inner | — | AC | enviador | GER:480 | SDK (FUN:32 FONTE_PRIMARIA_PARCIAL) | T13 |

**Achado de conformidade ADR-0020.** Das ~40 funções de montagem com assinatura, o adapter chama **10**
(`ADP:157-172`). Tudo o mais vai com default da DLL, inclusive itens que mudam segurança e memória:
`DefinirTipoListaAcesso`, `RegistrarAcessoNegado`, `DefinirNumeroCartaoMaster`/`CartaoMasterLiberaAcesso`
(cartão master "é porta dos fundos", `docs/14…:107-108`), `DesabilitarWebServer`,
`DesabilitarBloqueioCatracaMicroSwitch`, `ReceberDataHoraDadosOnLine`, dígitos variáveis. O teste de
contrato prometido em ADR-0020:31-32 ("um campo suportado pela matriz não coberto pelo montador faz
o build falhar") **não existe** para essas funções — a matriz FUN só lista 58.

### 1.5 Leitores e formatos (QR, barras, proximidade/Mifare, teclado, biometria, facial)

**Recepção on-line (tempo real).**

| Função | Assinatura | Diferença | Modo | No produto | Evid. | Bancada |
|---|---|---|---|---|---|---|
| `ReceberDadosOnLine` | `byte (int Inner, ref byte Origem, ref byte Complemento, StringBuilder Cartao, ref byte Dia…Segundo)` INV:172 | cartão numérico; tem segundos (FUN:41) | on-line | IEN:120 · `ADP:205-236` (buffer 64 bytes, NAT:45) | FP | INT-SM-030 / CHAOS-DEV-01 |
| `ReceberDadosOnLine_ComLetras` | mesma assinatura INV:174 | devolve letras (INF pelo nome) | on-line | GER:563 | SDK | NOVO-HIL-QR-02 |
| `ReceberDadosOnLine_QRCodeComLetras` | mesma assinatura INV:176 | QR alfanumérico (INF) — par natural de `ConfigurarTipoLeitor(8)` | on-line | GER:566 | SDK | NOVO-HIL-QR-02 |
| `ReceberDadosOnLine2`, `ReceberDadosOnLine_Hexadecimal`, `ReceberQrAsciiEstendido` | sem assinatura INV:173,175,185 | — | — | — | NOME | T11 |
| `EnviarFormasEntradasOnLine` | `byte (int Inner, byte QtdeDigitosTeclado, byte EcoTeclado, byte FormaEntrada, byte TempoTeclado, byte PosicaoCursorTeclado)` INV:100 | FormaEntrada 0–7, 10–14, 100–105 (FUN:33); **é o rearme do leitor** a cada ciclo (FUN:47) | on-line | IEN:100 · `ADP:188-197` com FormaEntrada=7 ("teclado e os dois leitores", comentário `ADP:31-32`) | FP (significado de cada valor **não está na matriz** → AC) | INT-SM-032 / HIL-ERR-02 |

**Campo `Complemento`**: nenhuma fonte do repositório o descreve além de "é devolvido"
(`docs/11…:67`). Se ele trouxer o sentido do giro na origem 6, resolve "giro reverso" e lotação
(`docs/29-o-que-falta.md:62`; `docs/25…:137`). **AC** → T14, NOVO-HIL-DIR-08 (girar nos dois sentidos
e registrar `Complemento`).

**Risco atual do QR alfanumérico.** A bancada usa `TipoDeLeitor=8` ("QR Code por letras") mas recebe
com `ReceberDadosOnLine` (numérico). Se a DLL só entrega letras pela variante `_QRCodeComLetras`, o
ingresso alfanumérico chega vazio/truncado e o motor responde "credencial desconhecida". Para o evento:
**exigir QR numérico de 4–16 dígitos** (já é regra do produto, `docs/20…:147-150`), e abrir a variante
alfanumérica só após NOVO-HIL-QR-02.

**Formatos por tecnologia (o que as fontes dizem):**

| Tecnologia | O que a Topdata publica | Configuração no SDK | Evid. |
|---|---|---|---|
| QR Code (leitor Topdata, tampa, porta `SERIAL_1_TTL`) | QR Mode 1/2, Micro QR, numérico e alfanumérico; 2 de 5, 3 de 9; 4–16 dígitos; só funciona como "Código de Barras Serial"; padrão Livre + dígitos variáveis | `DefinirPadraoCartao(1)` + `ConfigurarTipoLeitor(5)` (INF: "barras serial") + `InserirQuantidadeDigitoVariavel(4..16)` | https://suporte.topdata.com.br/suporte/leitor-qr-code-nas-catracas/ ; FUN:11,13,14 |
| Mifare / proximidade no mesmo equipamento | com QR configurado, prox vira ABA Track 14 dígitos, Mifare 10 dígitos, automaticamente | nada a mais (INF) — confirmar | mesma URL; `docs/20…:86-87` |
| Wiegand | 26/37 bits (WebServer Catraca 4); 26/34/37 (Coletor Urna 4) | `ConfigurarTipoLeitor(3/6/7)` | web-server-nas-catracas-4; `docs/02…:164` |
| Teclado | origem 1 (FUN; origens-evento.csv:2); teclas 35/42/65/66/67 (origens-evento.csv:23-28) | `HabilitarTeclado` + `EnviarFormasEntradasOnLine(QtdeDigitosTeclado…)` | FUN:21,33 |
| Biometria digital | origens 12/13/18/37 | §1.14 | origens-evento.csv |
| Facial | fora da EasyInner; o leitor facial manda **número de cartão** à catraca por ligação física (`docs/11…:123-127`) | nada na EasyInner | ADR-0011 |

**Dois leitores de tecnologias diferentes**: a Topdata afirma que a Catraca 4 lê QR + prox/Mifare no
mesmo produto (`docs/19…:19-25` e URL acima). O SDK tem **um único** `ConfigurarTipoLeitor(byte)`;
a Linha 4 "identifica automaticamente o protocolo" (`docs/02…:137-139`). Qual leitor físico sai como
origem 2 e qual como 3 é o passo 3 do `docs/21` (linhas 1–2). Mantido **AC** (T15).

### 1.6 Lista de acesso na catraca (cadastro, exclusão, lista branca/negra, horários)

| Função | Assinatura | Parâmetros | Retornos | Efeito | Pré-condição | No produto | Evid. | Bancada |
|---|---|---|---|---|---|---|---|---|
| `DefinirTipoListaAcesso` | `byte (byte Tipo)` INV:77 | 0 não usar · 1 branca · 2 negra (FUN:34) | 0 · 128 | semântica da lista | buffer de config | GER:511 | FP | INT-OFF-02 |
| `ApagarListaAcesso` | `byte (int Inner)` INV:9 | Inner (o SDK resolve a dúvida do manual sobre o parâmetro: **tem** `Inner`) | AC | apaga lista no equipamento **ou** só o buffer — manual contraditório (FUN:37) | antes de montar | GER:508 | SDK (efeito AC) | INT-OFF-03 |
| `InserirUsuarioListaAcesso` | `byte (string Cartao, byte Horario)` INV:143 | Cartão string no padrão/dígitos; Horário 1–100 tabela · **101 sempre liberado** · **102 sempre negado** (FUN:35) | 0 · 128 padrão/quantidade · 129 dígitos · 130 horário | adiciona ao **buffer** | horários já enviados (FUN:35) | GER:538 | FP | INT-OFF-03 / LOAD-OFF-01 |
| `EnviarListaAcesso` | `byte (int Inner)` INV:104 | Inner | 0 · 1 | grava; **sobrescreve** a lista existente; limpa o buffer (FUN:36) | buffer populado | GER:523 | FP | INT-OFF-03 |
| `InserirHorarioAcesso` | `byte (byte Horario, byte DiaSemana, byte FaixaDia, byte Hora, byte Minuto)` INV:137 | Horario 1–100 (INF, WebServer: 100 tabelas); DiaSemana / FaixaDia: **faixas não publicadas** (WebServer: 2 faixas início/fim por dia, 7 dias) | AC | buffer de horários | — | GER:526 | FP (assinatura) / AC (faixas) | INT-OFF-04 |
| `EnviarHorariosAcesso` | `byte (int Inner)` INV:102 | Inner | AC | grava horários; **deve preceder** `EnviarListaAcesso` (FUN:39) | — | GER:517 | FP | INT-OFF-04 |
| `ApagarHorariosAcesso` | `byte (int Inner)` INV:7 | Inner | AC | apaga horários | — | GER:502 | SDK | INT-OFF-04 |
| `ReceberUsuarioLista` + `TemProximoUsuario` | `byte (int Inner, StringBuilder Usuario)` INV:196; `int ()` INV:260 | — | AC | **leitura de volta da lista** (INF) | — | GER:541, GER:553 | SDK | NOVO-INT-OFF-09 |
| `SolicitarExclusaoUsuario` + `UsuarioFoiExcluido` | `byte (int Inner, string Usuario)` INV:249; `byte (int Inner, byte OnLine)` INV:262 | — | AC | **exclusão individual?** (INF) — se for da lista de acesso, muda a estratégia incremental | — | GER:544, GER:550 | SDK | NOVO-INT-OFF-10 / T16 |
| `UsuarioFoiEnviado` | `byte (int Inner, byte OnLine)` INV:261 | — | AC | confirmação de envio de usuário (INF; pode ser bio) | — | GER:547 | SDK | T16 |
| `InserirHorarioMudancaEntrada` / `…Saida` | `byte (byte Hora1, byte Minuto1, byte Hora2, byte Minuto2, byte Hora3, byte Minuto3)` INV:138-139 | 3 horários/dia (INF) | AC | troca automática de sentido por horário (INF) | — | GER:529-532 | SDK | não usar em evento |

Capacidade e estratégia: §4.

### 1.7 Bilhetes e tipos de bilhete

`tipos-bilhete.csv` (FONTE_PRIMARIA, manual 5.2.2): 0–9 funções por cartão · 10 entrada · 11 saída ·
12/13 negadas · 100–109 funções por teclado · 110–113 equivalentes por teclado · 003/004/005/010/012/013
expedidora (colidem com 3/4/5/10/12/13 — ambígua) · **128 = bilhete repetido já devolvido em coleta
anterior** (tipos-bilhete.csv:2-18). O WebServer exporta `bilhetes.txt` no formato
`"010 12/08/20 15:40 0000000000000001 01"` com tipos **"010 ou 110 Entrada, 011 ou 111 Saída, 012 ou
112 / 013 ou 113 Acesso bloqueado"** (página web-server-nas-catracas-4). Isso sugere (INF) que os
"003…013" do manual são o **mesmo número escrito com 3 dígitos**, e não códigos distintos — a
colisão pode ser só tipográfica. Continua **AC** (pauta item 9, `docs/08…:99`) até a bancada colher um
bilhete de expedidora — que **não existe** no parque de evento (fora de escopo).

### 1.8 Relógio, datas, horário de verão, feriados

| Função | Assinatura | Parâmetros | Modo | Efeito | No produto | Evid. | Bancada |
|---|---|---|---|---|---|---|---|
| `ReceberRelogio` | INV:188 | Dia, Mes, Ano (2 dígitos), Hora, Min, Seg | ambos | leitura; drift | IEN:51 · `ADP:101-115` · `PUMP:734-767` (1 min após acerto, depois a cada hora) | FP (FUN:8) | INT-CLK-01 |
| `EnviarRelogio` | INV:114 | idem; 2000–2099 (`ADP:120-124`) | ambos | acerta; afeta ordem de eventos | IEN:61 · `ADP:117-134` · a cada conexão (`PUMP:342`) | FP (FUN:9) | INT-CLK-02 |
| `EnviarHorarioVerao` | INV:101 | início e fim completos | ambos | horário de verão | GER:514 | FP (FUN:10) | INT-CLK-03 |
| `InserirHorarioSirene` / `EnviarHorariosSirene` / `ApagarHorariosSirene` | INV:140,103,8 | Hora, Min, 7 flags de dia incl. `DomingoFeriado` | ambos | sirene por horário | GER:535,520,505 | SDK | não usar |

**Feriados:** não existe função de feriado no inventário; o único rastro é o parâmetro `DomingoFeriado`
da sirene (INV:140). Horário de verão: o WebServer diz que *"uma vez configurado a data pode ser alterada,
porém não é possível apagar a configuração já realizada"* — **nunca enviar `EnviarHorarioVerao`**
(o Brasil não tem horário de verão desde 2019; a borda envia hora de Brasília). Relógio sem energia dura
~1 h (WebServer) → acertar sempre na conexão (já feito) e tratar data inválida (`ADP:322-332`, já feito).

### 1.9 Display e mensagens (2×16, 32 caracteres)

| Função | Assinatura | Limite | Modo | No produto | Evid. | Bancada |
|---|---|---|---|---|---|---|
| `EnviarMensagemPadraoOnLine` | `byte (int Inner, byte ExibirData, string Mensagem)` INV:111 | 32 chars, 16 se exibir data (FUN:57) | on-line | IEN:151 · `ADP:183` e `ADP:199-203` (ExibirData=0) | FP | INT-MSG-01 |
| `EnviarMensagemTemporariaOnLine` | `byte (int Inner, byte ExibirData, string Mensagem, byte Tempo)` INV:112 | idem; Tempo em s (unidade **não** está em FUN:58 → AC); produto limita 1–60 (`ComandoDeCatraca.cs:117`) | on-line | IEN:154 · `ADP:287-295` · negação `PUMP:547` | FP | INT-MSG-02 |
| `DefinirMensagemApresentacaoEntrada` / `…Saida` | `byte (byte ExibirData, string Mensagem)` INV:62-63 | AC (WebServer: 2ª linha; 1ª linha mostra o nº do cartão por padrão) | ambos (INF) | GER:422-425 — **default da DLL** | SDK | NOVO-INT-MSG-03 |
| `DefinirMensagemPadraoOffLine` / `…EntradaOffLine` / `…SaidaOffLine` | idem INV:68,64,69 | AC | off-line | GER:440,428,443 | SDK | NOVO-INT-MSG-04 |
| `DefinirMensagemFuncaoOffLine` | `byte (string Mensagem, byte Funcao, byte Habilitada)` INV:65 | AC | off-line | GER:431 | SDK | não usar |
| `EnviarMensagensOffLine` / `ApagarMensagensOffLine` | `byte (int Inner)` INV:113,10 | — | off-line | GER:489,582 | SDK | NOVO-INT-MSG-04 |
| `DefinirMensagemPadraoMudancaOffLine/OnLine` | §1.3 | | | GER | SDK | NOVO-INT-MSG-05 |

**Privacidade (LGPD).** A mensagem de entrada do WebServer **mostra o número do cartão na 1ª linha por
padrão** (página web-server-nas-catracas-4). Como `DefinirMensagemApresentacaoEntrada/Saida` não é
enviada, o que o display mostra depende do default da DLL → pode expor o identificador do ingresso a
quem está atrás na fila. NOVO-INT-MSG-03 precisa observar isso. Acentos: AC (`docs/32…:100`).

### 1.10 Bip, relés, LEDs e sinalização

| Função | Assinatura | Efeito | Restrição | No produto | Evid. | Bancada |
|---|---|---|---|---|---|---|
| `AcionarBipCurto` / `AcionarBipLongo` | `byte (int Inner)` INV:3-4 | bip | — | NAT:298,302 · **fora do IEN/ITA** | FP (FUN:49-50) | INT-UX-03 |
| `LigarBipIntermitente` / `DesligarBipIntermitente` | `byte (int Inner)` INV:156,85 | bip contínuo | risco de "esquecer ligado" → sempre par | GER:646,643 | SDK | NOVO-INT-UX-05 |
| `LigarLedVerde` / `DesligarLedVerde` / `LigarLedVermelho` / `DesligarLedVermelho` | `byte (int Inner)` INV:157,86,158,87 | LEDs | **só Linha 3** (FUN:51-54); Linha 4 tem pictogramas de sentido (https://suporte.topdata.com.br/suporte/linha-de-catracas-4/) | NAT:306-318 · fora do IEN | FP | INT-UX-01 |
| `LigarBackLite` / `DesligarBackLite` | **sem assinatura** (INV:155, INV:84) | luz de fundo | FUN:55-56 marca FONTE_PRIMARIA, mas **não há assinatura** → proibido declarar | — | NOME | T11 |
| `EnviarSinalizacao` | `byte (int Inner, byte TipoSinalizacao, byte ModoSinalizacao, byte Terminal)` INV:115 | sinalização genérica (pictogramas?) | faixas AC | GER:492 | SDK | NOVO-INT-UX-06 |
| `AcionarRele1` | `byte (int Inner)` INV:5 | aciona relé 1 pelo tempo configurado (INF por analogia a `AcionarRele2`) | **Na catraca, o acionamento 1 é o giro** (*"tem como função única o controle do giro"* — web-server-nas-catracas-4) e FUN:17 manda não usá-lo para giro → **não expor** | GER:653 | SDK | NOVO-HIL-REL-02 (só manutenção) |
| `AcionarRele2` | `byte (int Inner)` INV:6 | aciona relé 2 (urna/sirene/revista) pelo tempo de `ConfigurarAcionamento2` | sem parâmetro de tempo (NAT:260-265) | IEN:148 · `ADP:259` · **nunca chamado pelo PUMP** | FP (SDK corrige manual) | SIM-URNA-05 |
| `ManterRele1Acionado` / `ManterRele2Acionado` | `byte (int Inner)` INV:162-163 | mantém acionado (INF) | par obrigatório com `Desabilitar…` | GER:621-624 | SDK | NOVO-HIL-REL-03 |
| `DesabilitarRele1` / `DesabilitarRele2` | `byte (int Inner)` INV:81-82 | desliga o relé (INF) | — | GER:600-603 | SDK | NOVO-HIL-REL-03 |
| `LerSensoresInner` | `byte (int Inner, ref byte StatusSensor1, ref byte StatusSensor2, ref byte StatusSensor3)` INV:146 | estado dos sensores 1–3 (origens 8–10) | valores AC | GER:618 | SDK | NOVO-HIL-SEN-02 |
| `EnviarMensagemImpressora00` / `…FF` | `byte (int Inner, string Mensagem)` INV:109-110 | impressora (ponto/legado — INF) | — | GER:483-486 | SDK | não usar |

### 1.11 Giro, sentido e liberação

| Função | Assinatura | Efeito | Pré-condição | Risco | No produto | Evid. | Bancada |
|---|---|---|---|---|---|---|---|
| `LiberarCatracaEntrada` | `byte (int Inner)` INV:151 | libera um giro no sentido entrada; janela = tempo do acionamento 1 (WebServer: *"tempo em que uma catraca aguarda para completar o giro após a liberação"*) | perfil físico comissionado | crítico | IEN:133 · `ADP:246` | FP (FUN:42) | SIM-URNA-08 / HIL-DIR-03 |
| `LiberarCatracaSaida` | INV:153 | idem saída | idem | crítico | IEN:136 · `ADP:250` | FP (FUN:43) | HIL-DIR-04 |
| `LiberarCatracaEntradaInvertida` | INV:152 | entrada com instalação invertida | posição "Esquerda" (INF — o WebServer chama "Entrada Invertido" a configuração para catraca instalada à esquerda) | crítico | IEN:139 · `ADP:245` | FP (FUN:44) | HIL-DIR-05 |
| `LiberarCatracaSaidaInvertida` | INV:154 | saída invertida | idem | crítico | IEN:142 · `ADP:249` | FP (FUN:45) | HIL-DIR-06 |
| `LiberarCatracaDoisSentidos` | INV:149 | libera ambos os sentidos | só evacuação, duas pessoas (`docs/14…:109-111`) | crítico (carona) | IEN:145 · `ADP:253` (não exposto ao operador, `docs/32…:87`) | FP (FUN:46) | HIL-DIR-07 |
| `LiberarCatracaDoisSentidosPacote` | sem assinatura INV:150 | — | — | — | — | NOME | — |
| `LerContadorGiro` / `AtribuirContadorGiro` | sem assinatura INV:145, INV:13 | contador de giros do próprio equipamento (INF) — auditoria independente de lotação (commit `99ed59b`) | — | — | — | NOME | T17 |
| `DesabilitarBloqueioCatracaMicroSwitch` | §1.4 | afeta o travamento mecânico (INF) | — | **segurança física** | GER:597 | SDK | NOVO-HIL-MEC-01 |

**Anti-dupla passagem:** não há função de anti-passback no inventário. É lógica do software (T5 do
docs/04, reserva atômica) e não existe no off-line entre pistas (`docs/14…:221-224`). **Tempo de
liberação** = `Tempo` do acionamento 1 (0–50 s SDK / 1–255 s WebServer, AC T8). **Sentido**: o
modo de operação físico (entrada/saída/ambos) vem de `ConfigurarLeitor1/2` + `ConfigurarAcionamento1`
(funções 1–3 e 6–9), e a liberação on-line escolhe a variante `LiberarCatraca*`.

**Bug provável — dupla inversão.** Com `SentidoInvertido=true`, `PUMP:524-526` pede `Saida` e
`ADP:248-250` converte para `LiberarCatracaSaidaInvertida`. Se "invertido" já significa "a entrada
lógica é o giro físico oposto", a chamada correta seria `LiberarCatracaEntradaInvertida`
(docs/04:288-291 fala em "saída invertida **ou** sentido configurado"). Hoje a decisão está
distribuída em dois lugares. Recomendação: o perfil comissionado guarda **a função exata** a chamar
(enum de 4 valores), resolvida uma vez no comissionamento (docs/04 §7) e nunca por combinação de
flags; HIL-DIR-05/06 decide.

### 1.12 Urna / recolhimento de cartão

| Função | Assinatura | Papel | Evid. | Estado |
|---|---|---|---|---|
| `ConfigurarLeitor2(op)` | INV:35 | leitor da fenda; desabilitado = urna morta (FUN:16) | FP | produto envia (`ADP:163`) |
| `ConfigurarAcionamento2(funcao, tempo)` | INV:22 | relé da urna; *"o acesso de saída somente será liberado se ocorrer o recolhimento do cartão que é realizada através da configuração do Acionamento 2"*; no Coletor Urna recomenda-se "Desabilitado" ou "**Libera para Saída**" (web-server-nas-catracas-4) → valor SDK 3 = "registro saída" (INF pela correspondência de nomes da §1.4) | FP + INF | bancada envia `FuncaoDoAcionamento2 = 0` (`SessaoDeBancada.cs:40`) → **urna não recolhe** |
| `AcionarRele2(Inner)` | INV:6 | abre a fenda/aciona recolhimento pelo tempo configurado (FUN:48) | FP | declarado, **não usado** |
| `EngolirCartao(Inner)` / `DevolverCartao(Inner)` | INV:89 / INV:88; GER:612 / GER:609 | recolher / devolver cartão (INF pelo nome; pode ser da expedidora) | SDK | **candidatos diretos** ao "recolher" que `docs/21…:321-325` diz não estar documentado → T18, NOVO-HIL-URNA-02 |
| "Possui urna? Sim" (WebServer) | — | flag que destina o leitor 2 **exclusivamente** à urna (web-server-nas-catracas-4) | FP (WebServer) | **não há função SDK conhecida** que grave essa flag; se `EnviarConfiguracoes` mandar o default "sem urna", o SDK desfaz o WebServer (ADR-0020) → T19 |
| Origem 7 (cartão recolhido) / 20 (urna cheia) / 5 (fim do tempo) | origens-evento.csv:8,21,6 | eventos do ciclo | FP | origem 7 e 20 conhecidas no domínio (`EventOrigin.cs`) |

Comportamento nativo: **em off-line** a catraca com urna recolhe o cartão do leitor 2 e libera o giro
sozinha (linha-de-catracas-4, trecho). No **uso invertido** do evento (recolher para **entrar**,
docs/04:180-181), a função de acionamento 2 "Libera para Entrada" (SDK 2) é a análoga — **INF, AC**.
Capacidade física: média de 750 cartões no Coletor Urna 4 (modelos.csv:9); para catraca com urna, AC.

### 1.13 Marcações / coleta off-line

| Função | Assinatura | Efeito | Risco | No produto | Evid. | Bancada |
|---|---|---|---|---|---|---|
| `ColetarBilhete` | INV:16 (StringBuilder; o produto usa `byte[]` de 64, NAT:270-278) | devolve Tipo, D/M/A, H:M **sem segundos**, cartão; **remove o bilhete** (FUN:40) | **crítico** (R-68) | IEN:109 · `ADP:261-285` · `PUMP:475-495` **descarta o bilhete** | FP | INT-REC-03 / CHAOS-REC-01 |
| `ReceberQuantidadeBilhetes` | `byte (int Inner, int[] QtdeBilhetes)` INV:186 | quantidade na memória (INF) — **tamanho do array AC** | estouro de buffer se errar | GER:348 | SDK | NOVO-INT-REC-05 / T20 |
| `AvisarQuandoMemoriaCheia` | INV:14 | aviso de memória cheia (INF) | — | GER:585 | SDK | NOVO-INT-REC-04 |

Capacidade: **30.000 marcações** na Catraca 4 (https://suporte.topdata.com.br/suporte/qual-a-capacidade-de-armazenamento-de-marcacoes-da-catraca-4/)
e na Inner Acesso/Catraca 3 (limites-de-capacidade.csv:6). **Circular**: a mais nova sobrescreve a mais
antiga (web-server-nas-catracas-4). Marcações por passagem em off-line: 1 a 3, **AC** (limites-de-capacidade.csv:7).
Limpeza: não há função "apagar bilhetes" no inventário; a coleta é destrutiva por si (FUN:40). Tipo 128
= repetido (tipos-bilhete.csv:18) → dedup obrigatório.

### 1.14 Biometria digital (104 das 265 exportações)

Todas com assinatura do SDK e **nenhuma** semântica documentada no repo, exceto `SetarBioVariavel`/
`ConfigurarBioVariavel` (FUN:26-27, com assinatura divergente — ver §0.7) e `EnviarDigitalUsuarioBio`
(FUN:59, "LACUNA", mas o SDK deu a assinatura: `byte (int Inner, int TipoModBio, string Usuario, byte[] Digital1, byte[] Digital2)`, INV:99).
Famílias (todas em GER:44-341):

| Família | Funções (INV) | Padrão de uso (INF) |
|---|---|---|
| Configuração do módulo | `ConfigurarBio`, `ConfigurarAjustesQualidadeBio`, `…SegurancaBio`, `…SensibilidadeBio`, `ConfigurarCapturaAdaptativaBio`, `ConfigurarFiltroBio`, `EnviarAjustesBio`, `RequisitarEnviarAjustesBio`/`RespostaEnviarAjustesBio`, `ConfigurarNivelLFD`, `ConfigurarTimeoutIdentificacao`, `RequisitarHabilitarIdentificacaoVerificacao`/`Resposta…`, `SetarBioLight`, `SetarBioVariavel`, `ConfigurarBioVariavel`, `DefinirFuncaoDefaultSensorBiometria`, `DefinirEntradasMudancaOffLineComBiometria` | ajustes 1:1 / 1:N, LFD (dedo falso) |
| Cadastro / exclusão de digitais | `EnviarDigitalUsuario`, `EnviarDigitalUsuarioBio`, `RespostaEnviarDigitalUsuarioBio`, `EnviarUsuarioBio`, `InserirUsuarioLeitorBio`, `RequisitarCadastrarUsuarioLeitorInnerBio`/`Resposta…`, `RequisitarExcluirUsuarioBio`/`Resposta…`, `RequisitarExcluirTodosUsuariosBio`/`Resposta…`, `PermitirCadastroInnerBioVerid`, `PermitirCadastroInnerBio` (NOME) | par **Requisitar…/Resposta…** = assíncrono em duas chamadas |
| Lista de usuários sem digital | `IncluirUsuarioSemDigitalBio`, `…InnerAcesso`, `EnviarListaUsuariosSemDigitalBio`, `…InnerAcesso`, `…Variavel`, `…500` (NOME), `IncluirUsuarioSemDigitalBio500` (NOME) | "dispensar verificação biométrica" do WebServer (INF) |
| Consulta / leitura | `ReceberDigitalUsuario` (**duas sobrecargas** GER:140 e GER:143 para o mesmo símbolo — só uma está em INV:178), `ReceberDigitalUsuarioBio`, `SolicitarDigitalUsuario`, `ReceberTemplateLeitor`, `…InnerBio`, `SolicitarTemplateLeitor`, `Requisitar/RespostaReceberTemplateLeitorInnerBio`, `ReceberUsuarioCadastradoBio`, `Solicitar/Requisitar/RespostaUsuarioCadastradoBio`, `ReceberQuantidadeUsuariosBio`, `Solicitar/Requisitar/RespostaQuantidadeUsuariosBio`, `ReceberListaPacUsuariosBio`, `ReceberPacoteListaUsuariosBio`, `Requisitar/RespostaListarUsuariosBio`, `SolicitarListaUsuariosBio(…Variavel)`, `SolicitarListaUsuariosComDigital`, `ReceberUsuarioComDigital`, `InicializarColetaListaUsuariosBio`, `TemProximoPacote`, `ReceberRespostaRequisicaoBio`, `ReceberTemplateCapturadoInner` (NOME), `ReceberListaUsuariosDigital` (NOME) | extrair template = **dado biométrico sai do equipamento** |
| Identificação / verificação pelo software | `FazerIdentificacaoBiometricaBio`, `FazerVerificacaoBiometricaBio`, `ResultadoIdentificacaoBiometrica`, `ResultadoVerificacaoBiometrica`, `CompararDigitalLeitor`, `ResultadoComparacaoDigitalLeitor`, `RequisitarIdentificarUsuarioLeitorBio`/`Resposta…`, `RequisitarVerificarDigitalBio`/`Resposta…`, `RequisitarVerificarCadastroUsuarioBio`/`Resposta…`, `ResultadoConfiguracaoBio`, `ResultadoInsercaoUsuarioLeitorBio` | — |
| Leitor **Verid** (outro fabricante de módulo, INF) | `ApagarTodosUsuariosVerid(int, string SenhaAdm)`, `ApagarUsuarioVerid`, `CompararPINVerid`, `CompararTemplateVerid`, `ConfigurarRedeVerid`, `CriarUsuarioLeitorVerid`, `IncluirUsuarioVerid`, `LocalizarPrimeiro/Proximo/UsuarioVerid`, `ReceberTotalUsuariosVerid`, `ReceberUsuarioVerid`, `SolicitarTotalUsuariosVerid`, `Resultado*Verid` | legado |

**Riscos específicos.** (a) Sobrecarga dupla de `ReceberDigitalUsuario` em GER:140/143: uma das duas
assinaturas não bate com o SDK/INV:178 — chamar a errada corrompe memória; **remover a que não está no
inventário** antes de qualquer uso. (b) Qualquer uso exige base legal, minimização e
eliminação (ADR-0014; LGPD) — para evento com ingresso, **biometria não se justifica**. (c) Os pares
`Requisitar/Resposta` exigem várias chamadas por operação na mesma thread única → cada cadastro rouba
tempo de polling das outras catracas do worker.

### 1.15 Facial (SDK separado — docs/13)

Não passa pela EasyInner (`docs/13-sdk-facial.md:16-18`). WebSocket (leitor é cliente, porta 7792,
`reg` com resposta **obrigatória**, `sendlog` decide `access`) e Web API HTTP (34 comandos, senha em
todo comando). 50 comandos em `comandos-facial.csv:2-50`. **Não há confirmação de giro** na linha Easy
(`docs/13…:108-118`). Numa Catraca 4 com leitor facial acoplado, o facial manda **número de cartão** à
catraca e o fluxo de acesso é 100% EasyInner (`docs/11…:123-127`). Capacidade da Catraca 4 Facial:
5.000 faces (https://suporte.topdata.com.br/suporte/linha-de-catracas-4/, trecho). Para evento aberto,
`sendlog` envia foto de **desconhecido** em Base64 → dado sensível (`docs/14…:228-231`). Fora do escopo
do módulo catraca-EasyInner; ADR-0011 já separa.

### 1.16 Diagnóstico, erros e códigos de retorno

| Retorno | Significado | Onde | Tratamento hoje |
|---|---|---|---|
| 0 | sucesso | todas (FUN) | `AdapterStatus.Ok` (ITA:57) |
| 1 | erro genérico | FUN:30,36 | `Erro` (ITA:58) |
| 2 / 3 / 4–6 | porta não aberta / já aberta / DLL de apoio ausente | só `AbrirPortaComunicacao` (FUN:3) | caem em `RetornoDesconhecido` (ITA:60) — **mapear por função** |
| 8 | GPF: DLL, .NET 3.5, arquitetura (FUN:3; R-03) | todas | `FalhaDeDependencia` → `DependenciaFatal` (`PUMP:463-467`) |
| 9 | tipo de conexão inválido | `DefinirTipoConexao` (FUN:2) | desconhecido |
| 128/129/130 | 1º/2º/3º parâmetro inválido | funções de montagem e `InserirUsuarioListaAcesso` (FUN:11-24,29,34-35) | desconhecido — deveria virar "configuração recusada: parâmetro N" |
| "sem eventos" | valor **desconhecido** | `ReceberDadosOnLine` | hipótese `ret=0 ∧ origem=0` (`ADP:213-219`) **e** todo retorno ≠0 vira `SemEventos` (`ADP:216`) — isso **mascara queda de comunicação** como silêncio; só o watchdog/T6 pega |
| `RET_SEM_BILHETES` | valor desconhecido | `ColetarBilhete` | hipótese "buffer vazio com ret 0" (`ADP:276-281`) |

Recomendação: `AdapterResult.FromNative` deve receber **o id da função** para mapear retornos
específicos (2/3/9/128–130), e `AguardarEvento` **não** deve converter todo retorno ≠0 em
`SemEventos` — preservar e contar (ADR-0018). Testes: HIL-EVT-01, HIL-BIL-01, HIL-ERR-01.

### 1.17 Outras famílias do inventário

| Família | Funções | Veredito |
|---|---|---|
| TLM / criptografia de cartão de barras | `EnviarBufferTLM`, `EnviarCriptografiaTLM`, `PreencherBufferTLM`, `HabilitarCriptografia` (todas NOME, INV:92,97,168,127) | fora do escopo (cartão de barras criptografado de ponto/empresa) |
| Smart card | `LerSmartCard` (NOME, INV:147) | fora |
| Comando de negação | `EnviarComandoAcessoNegado` (NOME, INV:93) | interessante (negação nativa com sinalização?) → T11 |
| Modem | §1.1 | fora |
| Impressora | §1.10 | fora |
| Sirene / revista | `InserirHorarioSirene`, `EnviarHorariosSirene`, `ApagarHorariosSirene`, `DefinirPorcentagemRevista`, acionamento função 4/5 | fora do evento (revista sorteada é ideia em `docs/14…:86-88`, não requisito) |
| Pictogramas Millenium, Inner antigo | `DefinirFormasPictogramasMillenium`, `SetarInnerOld` | legado, não usar |

---

## 2. Mapa das 265 exportações

Base: INV:2-266 (265 linhas; a tabela de exportação tem 775 entradas, das quais 510 são wrappers JNI —
commit `99ed59b`; trava em `tests/Contract/LimitesDeCapacidadeTests.cs`, `Assert.Equal(265, …)`).

### 2.1 Por família (coluna `categoria` do INV)

| Família | Total | Com assinatura (declaradas) | Só nome | Na costura `IEN` | Chamadas no caminho operacional (PUMP→ADP) |
|---|---|---|---|---|---|
| biometria | 104 | 99 | 5 | 0 | 0 |
| configuracao | 53 | 49 | 4 | 12 | 11 (`ConfigurarInnerOffLine` nunca efetivo) |
| outros | 36 | 22 | 14 | 0 | 0 |
| envio | 21 | 17 | 4 | 5 | 5 (`EnviarConfiguracoes`, `EnviarFormasEntradasOnLine`, `EnviarMensagemPadraoOnLine`, `EnviarMensagemTemporariaOnLine`, `EnviarRelogio`) |
| leitura | 16 | 10 | 6 | 3 | 3 (`ReceberDadosOnLine`, `ReceberRelogio`, `ReceberVersaoFirmware`) |
| tempo real | 10 | 9 | 1 | 6 | 5 liberações possíveis (`DoisSentidos` só por código, nunca pelo PUMP); `AcionarRele2` não |
| listas | 10 | 10 | 0 | 0 | 0 |
| sinalizacao | 8 | 6 | 2 | 0 (4 LEDs + 2 bips em NAT, fora do IEN) | 0 |
| comunicacao | 5 | 5 | 0 | 4 | 4 (`PingOnLine` não) |
| coleta | 2 | 2 | 0 | 1 | 0 (inalcançável, §0.5) |
| **Total** | **265** | **229** (38 NAT + 191 GER) | **36** | **31** | **~27** |

(Contagens por `awk` sobre INV; "declaradas" = `situacao=DECLARADA_NO_PRODUTO`. GER tem 192
`extern` porque `ReceberDigitalUsuario` aparece duas vezes — §1.14.)

### 2.2 O que vale para eventos (QR + cartão de bilheteria + urna)

**Núcleo (já usadas; corrigir o que falta):** `DefinirTipoConexao`, `AbrirPortaComunicacao`,
`FecharPortaComunicacao`, `Ping`, `ReceberVersaoFirmware`, `ReceberRelogio`, `EnviarRelogio`,
`DefinirPadraoCartao`, `DefinirQuantidadeDigitosCartao`, `ConfigurarTipoLeitor`, `ConfigurarLeitor1/2`,
`ConfigurarAcionamento1/2`, `ConfigurarInnerOnLine/OffLine`, `HabilitarTeclado`,
`HabilitarMudancaOnLineOffLine`, `EnviarConfiguracoes`, `EnviarFormasEntradasOnLine`,
`ReceberDadosOnLine`, `LiberarCatraca*` (5), `EnviarMensagemPadraoOnLine`,
`EnviarMensagemTemporariaOnLine`.

**Adicionar ao produto (prioridade para evento), todas atrás de flag desligada até a bancada:**

| Prioridade | Função(ões) | Por quê | Bloqueio |
|---|---|---|---|
| P0 | `InserirQuantidadeDigitoVariavel` | QR 4–16 e Mifare 10 dependem disso; hoje vai default (§0.2) | nenhum: assinatura FP (FUN:13) |
| P0 | `ReceberDataHoraDadosOnLine`, `RegistrarAcessoNegado`, `DefinirTipoListaAcesso`, `DefinirNumeroCartaoMaster`/`CartaoMasterLiberaAcesso`, `ConfigurarWiegandDoisLeitores`, `DefinirFuncaoDefaultLeitoresProximidade` | ADR-0020: parar de mandar default desconhecido | FP para 5 (FUN:23-25,28,34); `CartaoMasterLiberaAcesso` só SDK |
| P0 | `EnviarConfiguracoesMudancaAutomaticaOnLineOffLine`, `PingOnLine` periódico, `ConfigurarInnerOffLine` real | contingência T2 (ADR-0017) | FP; intervalo do ping AC |
| P1 | `DefinirTipoListaAcesso`, `ApagarListaAcesso`, `InserirUsuarioListaAcesso`, `EnviarListaAcesso`, `InserirHorarioAcesso`, `EnviarHorariosAcesso`, `ApagarHorariosAcesso` | lista local para T2 (§4) | faixas de horário AC; efeito de `ApagarListaAcesso` AC |
| P1 | `ColetarBilhete` (ligar de verdade, com commit antes do próximo), `ReceberQuantidadeBilhetes`, `AvisarQuandoMemoriaCheia` | reconciliação pós-queda; memória circular | tamanho do array AC (T20) |
| P1 | `AcionarRele2`, `EngolirCartao`/`DevolverCartao` | urna (docs/04) | semântica AC (T18/T19) |
| P1 | `AcionarBipCurto/Longo` | feedback na Linha 4 (sem LED) | FP; expor no IEN |
| P2 | `DefinirMensagemApresentacaoEntrada/Saida`, `DefinirMensagemPadraoOffLine/EntradaOffLine/SaidaOffLine`, `DefinirMensagemPadraoMudancaOffLine/OnLine`, `EnviarMensagensOffLine` | não expor nº do ingresso; mensagem coerente em T2 | faixas AC |
| P2 | `DesabilitarWebServer` | segurança (WebServer com senha de fábrica) | semântica 0/1 AC |
| P2 | `ReceberDadosOnLine_QRCodeComLetras` / `_ComLetras` | QR alfanumérico | AC (NOVO-HIL-QR-02) |
| P2 | `ReceberVersaoFirmware6xx_ComComplementar`, `ReceberConfiguracoesInner`, `ReceberUsuarioLista`/`TemProximoUsuario` | discovery, leitura de volta, auditoria de lista | tamanho de buffer AC |
| P3 | `LerSensoresInner`, `EnviarSinalizacao`, `LigarBipIntermitente/Desligar…` | diagnóstico / chamada de supervisor | AC |
| Pedir assinatura | `LerContadorGiro`, `AtribuirContadorGiro`, `LigarBackLite`, `DesligarBackLite`, `EnviarComandoAcessoNegado`, `LevantarParaOnLine`, `ResetarModoOnLine`, `UtilizarRelogioSegundos` | auditoria de lotação, sinalização, regime | só nome (T11/T17) |

**Não valem para eventos (e por quê):**

| Grupo | Qtde | Motivo |
|---|---|---|
| Biometria digital + Verid | 104 | ingresso não é biométrico; LGPD exige base legal e minimização (ADR-0014); cada operação ocupa a thread única |
| Modem, impressora, TLM, smart card, Millenium, `SetarInnerOld` | ~14 | tecnologia legada/ponto eletrônico; não há no parque |
| Sirene, revista, horários de mudança de sentido | ~9 | controle de ponto/empresa; em evento a revista é da segurança |
| Padrão Topdata (`DefinirCodigoEmpresa`, `DefinirNivelAcesso`, `UtilizarSenhaAcesso`) | 3 | o evento usa padrão Livre; ainda assim entram no modelo de configuração com valor explícito "neutro" **se** a Topdata confirmar que o default não interfere no padrão Livre (T13) |
| `LiberarCatracaDoisSentidos` no operacional | 1 | só evacuação (ADR-0013) |
| `AcionarRele1`, `ManterRele1Acionado`, `DesabilitarRele1` | 3 | acionamento 1 é o giro; usar fora de `LiberarCatraca*` burla o controle (FUN:17; WebServer) |

---

## 3. Modelo de configuração da catraca (proposta)

Princípios (ADR-0020 + ADR-0006 + ADR-0010):
1. **Um** registro imutável, versionado, com **todos** os campos que o montador envia; nenhum campo
   opcional que signifique "deixa o default da DLL".
2. Campo cuja semântica é `A_CONFIRMAR` existe no modelo com um **valor padrão explícito e
   documentado como "herdado da bancada"**, e a feature que o usa fica **desligada**. Enquanto a
   bancada não disser qual é o default da DLL (T13), o campo é enviado com o valor medido no ensaio
   de leitura de volta (NOVO-HIL-CFG-10) — nunca omitido.
3. Validação completa **antes** da primeira chamada nativa (já é o padrão em `CFG:77-173`/`ADP:140-147`).
4. O mesmo conjunto de campos comuns vai nos **dois** envios (`EC` off-line e `EC` on-line), porque
   cada `EnviarConfiguracoes` limpa o buffer e manda defaults para o resto (ADR-0006:11-12, ADR-0020:13-15).

### 3.1 Campos

`DeviceConfiguration` atual (`CFG:22-70`) cobre 16 campos. Proposta (novos em **negrito**; "AC" =
feature desligada até o teste indicado):

| Grupo | Campo | Tipo | Faixa válida | Padrão proposto (evento TopFit 4) | Função da DLL | Evid. da faixa | Notas / dependência |
|---|---|---|---|---|---|---|---|
| Cartão | `PadraoCartao` | byte | 0 Topdata · 1 Livre | 1 | `DefinirPadraoCartao` | FUN:11 | QR Topdata exige Livre (URL leitor-qr) |
| Cartão | **`ModoDeDigitos`** | enum {Fixo, Variavel} | — | Variavel | — | — | substitui o par anulável atual; elimina o caso "nenhum dos dois" |
| Cartão | `QuantidadeFixaDeDigitos` | byte | 4–16 (conservador; 1–16 AC T6) | — | `DefinirQuantidadeDigitosCartao` | FUN:12 | obrigatório se Fixo |
| Cartão | `QuantidadesVariaveisDeDigitos` | set<byte> | ⊂ {4..16} (1–16 AC) | {4..16} | `InserirQuantidadeDigitoVariavel` × n | FUN:13 | **obrigatório e não vazio** se Variavel; **precisa entrar no adapter** |
| Leitores | `TipoDeLeitor` | byte | 0–8 | **5** (barras serial) | `ConfigurarTipoLeitor` | FUN:14; URL leitor-qr | 8 só após NOVO-HIL-QR-02 |
| Leitores | **`VarianteDeRecepcao`** | enum {Numerica, ComLetras, QRCodeComLetras} | — | Numerica | `ReceberDadosOnLine[_…]` | INV:172,174,176 | não é campo de catraca: é do adapter; Letras = AC |
| Leitores | `OperacaoDoLeitor1` | byte | 0–4 | 1 (só entrada) | `ConfigurarLeitor1` | FUN:15 | valores >4 proibidos (T10) |
| Leitores | `OperacaoDoLeitor2` | byte | 0–4 | 1 se urna, senão 0 | `ConfigurarLeitor2` | FUN:16 | urna ⇒ ≠0 (`CFG:158-164`) |
| Leitores | **`WiegandDoisLeitores`** | (byte Habilita 0–1, byte ExibirMensagem 0–1) | — | (0,0) | `ConfigurarWiegandDoisLeitores` | FUN:25 | hoje vai default |
| Leitores | **`FuncaoDefaultProximidade`** | byte | 0–12 | AC — valor lido na bancada | `DefinirFuncaoDefaultLeitoresProximidade` | FUN:23 | significado de 0–12 AC |
| Acionamento | `FuncaoDoAcionamento1` / `TempoDoAcionamento1` | byte / byte | 0–9 / 0–50 s | 2 ("registro entrada") / 5 | `ConfigurarAcionamento1` | FUN:17 | doc do campo diz "0 a 5" (`CFG:41`) mas valida 0–9 (`CFG:108`) — corrigir o comentário; `Tempo` deve ser ≤ timeout de `MonitoraGiroCatraca` (8 s, FSM:48) |
| Acionamento | `FuncaoDoAcionamento2` / `TempoDoAcionamento2` | byte / byte | 0–9 / 0–50 s | 0/0 sem urna; urna: AC (2 ou 3, T19) / ≥ T2 do docs/04 (6 s) | `ConfigurarAcionamento2` | FUN:18 | T4 > T2+T3 (docs/04:304-307) |
| Acionamento | **`LogicaRele`** | byte | AC | valor da bancada | `DefinirLogicaRele` | INV:61 | desligado |
| Regime | `Online` → **`RegimeAlvo`** | enum {OnLine, OffLine} | — | OnLine | `ConfigurarInnerOnLine/OffLine` | FUN:19-20 | o passo "cfg offline" usa OffLine **sempre**; o campo diz em qual regime terminar |
| Regime | `MudancaAutomatica` / `TempoDaMudancaAutomatica` | byte / byte | 0–2 / 1–50 (unidade AC) | 0 / 10 (bancada); evento: 2 só após INT-SM-021 | `HabilitarMudancaOnLineOffLine` | FUN:29 | 2 ⇒ `PingOnLine` com período < Tempo e < pior volta do laço (§6) |
| Regime | **`EntradasMudancaOffLine`** | (Teclado, Leitor1, Leitor2, Catraca: byte) | AC | valor da bancada | `DefinirEntradasMudancaOffLine` | INV:53 | só com mudança ≠0 |
| Regime | **`EntradaMudancaOnLine`** | byte | AC | valor da bancada | `DefinirEntradasMudancaOnLine` | INV:55 | idem |
| Regime | **`IntervaloDoPingOnLine`** | TimeSpan | > 0, < Tempo da mudança | AC | `PingOnLine` | FUN:6 | não é campo da catraca: é do PUMP |
| Teclado | `TecladoHabilitado` / `EcoDoTeclado` | bool / byte | – / 0–2 | false / 0 | `HabilitarTeclado` | FUN:21 | |
| Teclado | **`FormasDeEntradaOnLine`** | (QtdeDigitosTeclado, EcoTeclado, FormaEntrada, TempoTeclado, PosicaoCursor) | FormaEntrada ∈ {0–7, 10–14, 100–105} | hoje fixo (0,0,7,0,0) em `ADP:191-197` | `EnviarFormasEntradasOnLine` | FUN:33 | FormaEntrada coerente com teclado/leitores ativos; significado de cada valor AC (T26) |
| Registro | **`RegistrarAcessoNegado`** | byte | 0–3 | AC (recomendado: registrar — `docs/14…:95-97`) | `RegistrarAcessoNegado` | FUN:22 | consome memória circular |
| Registro | **`DataHoraNoEventoOnLine`** | bool | 0/1 | 1 | `ReceberDataHoraDadosOnLine` | FUN:28 | o adapter já monta a data (`ADP:222`) — sem isto pode vir zerada |
| Registro | **`AvisarMemoriaCheia`** | bool | AC | AC | `AvisarQuandoMemoriaCheia` | INV:14 | T34 |
| Segurança | **`CartaoMaster`** | string? | ≤14 dígitos, só padrão Livre | valor aleatório não impresso, guardado cifrado (AC: como "desligar" o master — T13) | `DefinirNumeroCartaoMaster` | FUN:24 | nunca default da DLL |
| Segurança | **`MasterLiberaAcesso`** | bool | 0/1 (INF) | false | `CartaoMasterLiberaAcesso` | INV:15 | AC |
| Segurança | **`WebServerDesabilitado`** | bool | 0/1 (INF) | true após comissionamento | `DesabilitarWebServer` | INV:83 | NOVO-SEC-WEB-01 |
| Segurança | **`BipDaCatracaDesabilitado`** | bool | 0/1 (INF) | false | `DesabilitarBipCatraca` | INV:78 | AC |
| Segurança | (não expor) `DesabilitarBloqueioCatracaMicroSwitch` | — | — | valor lido na bancada, **fixo** | INV:80 | — | nunca editável pelo operador |
| Mensagens | `MensagemPadrao` + **`ExibirDataNaPadrao`** | string / bool | ≤32, ≤16 se data | "Aproxime o ingresso" / false | `EnviarMensagemPadraoOnLine` | FUN:57 | `CFG:143-146` já valida 32 |
| Mensagens | **`MensagemEntrada`**, **`MensagemSaida`** (+ExibirData) | string/bool | AC (≤16 por linha, INF) | "Bom evento" / "Volte sempre" | `DefinirMensagemApresentacaoEntrada/Saida` | INV:62-63 | evita expor nº do cartão (§1.9) |
| Mensagens | **`MensagensOffLine`** (padrão, entrada, saída) e **`MensagensMudanca`** (off/on) | string/bool | AC | "Modo contingência" etc. | `DefinirMensagemPadraoOffLine`… / `EnviarMensagensOffLine` | INV:64-69,113 | coerência com T2 |
| Lista | **`TipoDeLista`** | byte | 0/1/2 | 0 até a lista existir; 1 (evento ≤15 mil, docs/14 §7.1) | `DefinirTipoListaAcesso` | FUN:34 | ≠0 ⇒ `SincronizandoDadosOffline` carrega lista (§4); 2 exige decisão B4 por escrito |
| Lista | **`TabelasDeHorario`** | lista (Horario 1–100, Dia, Faixa, Hora, Min) | AC (T-faixas) | vazia (usar 101/102) | `InserirHorarioAcesso` + `EnviarHorariosAcesso` | FUN:38-39 | desligado |
| Físico | `PerfilFisico` → **`FuncaoDeLiberacaoDaEntrada`** | enum {Entrada, EntradaInvertida, Saida, SaidaInvertida} | — | do comissionamento | `LiberarCatraca*` | FUN:42-45 | resolve a dupla inversão (§1.11) |
| Urna | **`UrnaHabilitada`**, **`T2`, `T3`, `T4`** | bool / TimeSpan | T4 > T2+T3 | false; 6 s / 8 s / 20 s | — (lógica) + `AcionarRele2` | docs/04:293-307 | T19 |

### 3.2 Regras entre campos (validar em `Validar()`)

1. `ModoDeDigitos=Variavel ⇒ QuantidadesVariaveis ≠ ∅`; `Fixo ⇒ QuantidadeFixa ∈ [4,16]`.
2. `PadraoCartao=1 ∧ QR na catraca ⇒ Variavel ⊇ comprimento de todo QR aceito pelo provedor` (o produto já recusa QR fora de 4–16, `docs/20…:147-150`).
3. `FuncaoDoAcionamento2≠0 ⇒ OperacaoDoLeitor2≠0` (já existe, `CFG:158-164`).
4. `UrnaHabilitada ⇒ FuncaoDoAcionamento2≠0 ∧ TempoDoAcionamento2 ≥ T2`.
5. `MudancaAutomatica=2 ⇒ RegimeAlvo=OnLine ∧ IntervaloDoPingOnLine < TempoDaMudancaAutomatica` (unidade AC).
6. `ExibirData ⇒ |mensagem| ≤ 16`.
7. `TipoDeLista≠0 ⇒ LimitesDeCapacidade.AvaliarListaDeAcesso(…).Cabe` (`LimitesDeCapacidade.cs:258-287`).
8. `TipoDeLista=2 ⇒ decisão B4 registrada` (ADR-0013).
9. `TipoDeLeitor=8 ⇒ VarianteDeRecepcao ≠ Numerica` (AC até NOVO-HIL-QR-02).
10. `FormaEntrada` compatível com `TecladoHabilitado` e leitores ativos (tabela AC, T26).
11. `TempoDoAcionamento1 ≤ timeout(MonitoraGiroCatraca) − margem` (FSM:48 = 8 s).
12. `Inner ∈ [1,99]` (FUN:5; `ComandoDeCatraca.cs:95`) enquanto T9 não responder.
13. Modelo `NAO_ENSAIADO` ⇒ só aplica em manutenção (ADR-0010:19-20).

### 3.3 Ordem de envio proposta

Função→passo baseada na FSM oficial (`docs/11…:227-233`); a alocação de cada `Definir*` ao envio
`EC` vs `EM` é **INF** até T13.

```
[uma vez por worker]  DefinirTipoConexao(2) → AbrirPortaComunicacao(porta)          (ADP:57-74)
[a cada conexão]
 1 CONECTAR              Ping(inner)                                                (ADP:84)
 2 IDENTIDADE            ReceberVersaoFirmware → conferir matriz (ADR-0010)
 3 ENVIAR_CFG_OFFLINE    [buffer] ConfigurarInnerOffLine + CAMPOS_COMUNS + DefinirTipoListaAcesso
                         + DefinirMensagem*OffLine → EnviarConfiguracoes(inner)
                         → EnviarMensagensOffLine(inner)                            (INF)
 4 DADOS_OFFLINE         [se TipoDeLista≠0 e hash da lista mudou]
                         ApagarHorariosAcesso? → InserirHorarioAcesso* → EnviarHorariosAcesso
                         → ApagarListaAcesso → InserirUsuarioListaAcesso* → EnviarListaAcesso (FUN:35-39)
 5 ENVIAR_CONFIGMUD      [buffer] HabilitarMudancaOnLineOffLine + DefinirEntradasMudancaOffLine
                         + DefinirEntradasMudancaOnLine + DefinirMensagemPadraoMudanca*
                         → EnviarConfiguracoesMudancaAutomaticaOnLineOffLine(inner)   (FUN:29-30)
 6 ENVIAR_CFG_ONLINE     [buffer] ConfigurarInnerOnLine + CAMPOS_COMUNS (+ os mesmos de lista/mensagens
                         off-line, para não voltarem ao default) → EnviarConfiguracoes(inner)
 7 RELÓGIO               EnviarRelogio (hoje no 1º Polling, PUMP:340-343 — mover para cá é equivalente)
 8 ENTRADAS_ONLINE       EnviarFormasEntradasOnLine(...)                            (FUN:33)
 9 MSG_PADRAO            EnviarMensagemPadraoOnLine(...)                            (FUN:57)
10 POLLING               ReceberDadosOnLine ⇄ … ; PingOnLine a cada IntervaloDoPingOnLine se mudança=2
```

Onde `CAMPOS_COMUNS` = `DefinirPadraoCartao`, dígitos (fixo **ou** variáveis), `ConfigurarTipoLeitor`,
`ConfigurarLeitor1/2`, `ConfigurarWiegandDoisLeitores`, `ConfigurarAcionamento1/2`, `DefinirLogicaRele`,
`HabilitarTeclado`, `RegistrarAcessoNegado`, `DefinirFuncaoDefaultLeitoresProximidade`,
`ReceberDataHoraDadosOnLine`, `DefinirNumeroCartaoMaster`, `CartaoMasterLiberaAcesso`,
`DesabilitarWebServer`, `DefinirMensagemApresentacaoEntrada/Saida`.

**Divergência a decidir com a Topdata (T21):** o PUMP atual coloca `SincronizandoDadosOffline` **depois**
do CFG_ONLINE (`FSM:182-188`). Se a lista precisar ser carregada com o equipamento em regime off-line
(passo 3), a ordem atual está errada. A proposta acima segue a ordem do manual; NOVO-INT-OFF-11 confirma
se `EnviarListaAcesso` funciona com a catraca já on-line.

**Correções pontuais no adapter atual:**
- `ADP:183` chama `EnviarMensagemPadraoOnLine` **entre** a montagem e o `EnviarConfiguracoes` — viola
  ADR-0006:19-22 ("nenhuma outra chamada… entre o início da montagem e a confirmação do envio") e
  duplica o estado `EnviarMsgPadrao` (`PUMP:418-430`). Remover de lá.
- `ADP:155-181`: se um passo falhar, retorna **sem** `EnviarConfiguracoes` e deixa o buffer global
  **sujo** para a próxima catraca do mesmo worker. Com `InserirQuantidadeDigitoVariavel` (que **acumula**,
  "uma chamada por tamanho", FUN:13), uma nova tentativa pode somar tamanhos. Mitigação: começar toda
  montagem por um "reset" conhecido — `InserirQuantidadeDigitoVariavel(0)` desabilita (FUN:13), mas se
  isso **zera a lista** é AC (T30).
- **Reinício:** nenhuma fonte exige reiniciar a catraca após configurar; o produto aplica por reconexão
  (`PUMP:597-613`), o que deixa a catraca "alguns segundos sem atender" (`docs/32…:95-96`).

---

## 4. Lista de acesso na catraca (off-line / contingência)

### 4.1 Limites (fonte: limites-de-capacidade.csv + URL da Topdata, lida nesta sessão)

| Placa / modelo | Usuários | Condição | Fonte |
|---|---|---|---|
| Controle Catraca (Catraca 4: Fit/Revolution/Box/PNE 4) | **15.000** | qualquer nº de dígitos | limites-de-capacidade.csv:2; https://suporte.topdata.com.br/suporte/capacidade-da-lista-de-controle-de-acesso-nos-produtos-da-linha-inner/ |
| Inner Acesso (Linha 3) | 15.000 / **14.900** com 16 dígitos | | idem, :3 |
| Inner Plus / Net | 15.000 (4d) … 5.000 (16d) | | idem, :4 |
| Inner antiga (1 relé, ≤1999) | 3.000/1.500/2.250/1.800/1.500/1.125 (4–14d) | tabela **não monotônica** na fonte — o código copia fielmente (`LimitesDeCapacidade.cs:227-230`) | idem, :5 |
| Coletor Urna 4 | 15.000, 4–16 dígitos; 100 tabelas de horário | | modelos.csv:9 |
| Tabelas de horário | 100 | cada tabela: 7 dias × 2 faixas (WebServer) | web-server-nas-catracas-4 |
| **Consumo por usuário** | **1 posição por associação cartão↔horário** | um cartão com k horários ocupa k posições | web-server-nas-catracas-4 ("reserva uma das 15.000 posições… e assim sucessivamente") |

Observação: o código trata "Controle Catraca **ou** Inner Acesso" como uma só placa com 14.900 para 16
dígitos (`LimitesDeCapacidade.cs:246-247`); pela fonte, **Controle Catraca é 15.000 mesmo com 16
dígitos** — o código é conservador (bom), mas a mensagem ao operador subestima em 100.
`AvaliarListaDeAcesso` recebe "usuários"; deveria receber **posições** (Σ horários por usuário, mínimo 1).
Com 101/102 (sem tabela) é 1 posição por cartão — **INF**, confirmar em NOVO-LOAD-OFF-02.

### 4.2 Como gravar (sequência)

```
DefinirTipoListaAcesso(1|2)   (buffer de config; vai no EnviarConfiguracoes — INF)
[opcional] InserirHorarioAcesso(h, dia, faixa, hh, mm) × n → EnviarHorariosAcesso(inner)   (FUN:38-39)
ApagarListaAcesso(inner)      (efeito exato AC — T7)
InserirUsuarioListaAcesso(cartao:string, horario) × N  (checar 128/129/130 a cada chamada, FUN:35)
EnviarListaAcesso(inner)      (sobrescreve; limpa buffer — FUN:36)
[verificação] ReceberUsuarioLista/TemProximoUsuario (AC) ou contagem por amostragem de leitura na bancada
```

- `Cartao` sempre string, com os zeros à esquerda do perfil do leitor (ADR-0008; `docs/20…:130`); o
  comprimento precisa bater com a configuração de dígitos, senão 128/129 (FUN:35).
- `InserirUsuarioListaAcesso` **não tem `Inner`** → o buffer é global do processo; montar e enviar a
  lista de uma catraca é uma unidade atômica na thread do worker (ADR-0006). Duas catracas do mesmo
  worker **nunca** podem ter montagens intercaladas.

### 4.3 Tempo estimado de carga

**Não há número publicado nem medido no repositório.** Qualquer estimativa seria invenção. O que se
sabe: o envio **sobrescreve tudo e trava a catraca enquanto roda** (`docs/14…:68-69`,
`LimitesDeCapacidade.cs:197-199`) e é **uma chamada bloqueante** na thread única. Proposta de medição
(NOVO-LOAD-OFF-02): N ∈ {100, 1.000, 5.000, 12.000, 15.000} cartões de 10 e 16 dígitos; medir (a) tempo
de montagem (N × `InserirUsuarioListaAcesso`, só memória), (b) duração de `EnviarListaAcesso`, (c) se
a catraca lê/libera durante o envio, (d) se outras catracas do mesmo worker ficam sem polling (sim, por
construção), (e) comportamento com cabo puxado no meio (lista antiga? vazia? parcial?). Registrar t(N)
e ajustar uma reta; o planejamento de evento usa o p95 medido.

### 4.4 Estratégia: completa vs incremental

| Aspecto | Fato | Consequência |
|---|---|---|
| Alteração de 1 usuário | exige reenviar a lista inteira (FUN:36; `docs/11…:95-97`) | **não existe incremental documentado** |
| Candidato a exclusão individual | `SolicitarExclusaoUsuario(int, string)` + `UsuarioFoiExcluido` (INV:249,262) | pode ser da lista **ou** da biometria — AC (T16). Até lá, proibido |
| Revogação sem tabela | horário 102 = sempre negado (FUN:35) | útil em lista **negra pequena** ou para marcar exceções |

Estratégia recomendada:
1. **Carga completa, versionada por hash**, feita **antes de abrir os portões**, em modo manutenção, uma
   catraca por vez por worker (workers diferentes podem carregar em paralelo — processos distintos,
   ADR-0021). A borda só recarrega se o hash mudou (evita o "trava a catraca" a cada reconexão —
   hoje a reconexão reenviaria a lista toda, se `SincronizandoDadosOffline` fosse ligado sem hash).
2. **Durante o evento, T1 é o regime normal** (ADR-0017:22-24): revogação e venda nova vivem na borda.
   A lista do equipamento é rede de terceiro nível; ela **envelhece** e o painel mostra "lista de
   cartões velha" (`docs/29…:97`).
3. **Recarga em evento** só por decisão do operador, fora do pico, catraca a catraca, com a pista
   sinalizada (mensagem temporária) — e só se t(N) medido for aceitável.
4. Evento de **12 mil** (docs/14 §7.1): lista **branca** completa cabe (15.000, folga 20%).
   Evento de **30 mil**: não cabe; opções (decisão B4, por escrito): (a) branca parcial priorizada
   (quem sobra é negado em T2), (b) **negra** (só revogados; em T2 passa qualquer desconhecido).
   `LimitesDeCapacidade.AvaliarListaDeAcesso` já produz essa mensagem (`LimitesDeCapacidade.cs:277-286`).
5. **Cartões da bilheteria local** (estoque físico reutilizável, `docs/19…:3-5`): a venda acontece
   durante o evento, então a lista do equipamento não a conhece. Opções: pré-carregar **todo o estoque**
   com 101 (em T2 qualquer cartão do estoque passa, vendido ou não) ou não carregar (em T2 cartão de
   bilheteria é negado). Decisão de negócio, não técnica.

### 4.5 Riscos da lista

| Risco | Detecção / mitigação | Teste |
|---|---|---|
| Janela com lista vazia entre `ApagarListaAcesso` e `EnviarListaAcesso` (branca: nega todos; negra: libera todos) | medir; se existir, carregar só com catraca em manutenção | NOVO-INT-OFF-12 |
| Estouro de capacidade descoberto no envio | avaliação prévia por **posições** | INT-OFF-02 |
| Queda de energia/rede no meio do envio (R-22) | reenviar completo na reconexão (hash "não confirmado") | CHAOS-PWR-01 |
| Watchdog mata o worker no meio de um `EnviarListaAcesso` longo (tolerância 30 s, `Watchdog.cs:29`) | classe de operação "longa" com tolerância própria, ou bater o watchdog antes/depois e medir t(N) | NOVO-CHAOS-OFF-01 |
| Outras catracas do worker sem polling durante a carga | carga só fora de operação / worker dedicado de carga não existe (a catraca aponta para a porta do seu worker) | LOAD-OFF-01 |
| Zeros à esquerda / dígitos divergentes do leitor | normalização por perfil; retorno 128/129 contado | HIL-CARD-02 |
| Cartão master ignora a lista (FUN:24) | master aleatório e custodiado | SEC-MASTER-01 |
| Lista velha liberando revogado em T2 | idade da lista no painel; recarga planejada | INT-OFF-03 |


---

## 5. Lacunas e perguntas para a Topdata

Coluna "WebServer": o que o manual público do WebServer da Catraca 4
(https://suporte.topdata.com.br/suporte/web-server-nas-catracas-4/, lido nesta sessão) responde.
**Responde** = fato publicado, mas no vocabulário do WebServer (ainda falta o valor no SDK);
**Parcial** = dá uma pista; **Não** = silencioso. Nenhuma resposta do WebServer vira valor de enum do
SDK sem bancada (regra do projeto).

| # | Pergunta | Por que importa | WebServer | Teste de bancada que resolve |
|---|---|---|---|---|
| T1 | NDA do protocolo TCP/IP de baixo nível (manual 6.7; pauta item 1, `docs/08…:91`) | tira x86, thread única, teto de 30, porta por worker | Não | — (documental) |
| T2 | Valor de retorno de "sem eventos" em `ReceberDadosOnLine`; timeout interno da chamada; o que devolve com socket caído | hoje todo retorno ≠0 vira `SemEventos` (`ADP:216`), mascarando queda e até GPF | Não | HIL-EVT-01 + CHAOS-NET-01 (cabo puxado durante a espera; medir duração e retorno) |
| T3 | Valor de `RET_SEM_BILHETES` | laço de coleta | Não | HIL-BIL-01 (memória vazia → 1 chamada) |
| T4 | Origens 11, 14–17, 19 existem? | ADR-0018 | Não | registrar toda origem desconhecida durante todo o roteiro docs/21 |
| T5 | Tabela de `Linha` do firmware (8 códigos p/ 7 descrições); TopFit 4 = 14? 16? | `PUMP:231` homologa {14,16} sem fonte | Parcial (mostra "Versão FW", não a linha) | HIL-CAP-01 (ler `ReceberVersaoFirmware` numa TopFit 4 e conferir com o WebServer/etiqueta) |
| T6 | `DefinirQuantidadeDigitosCartao`: 1–16 ou 4–16? | validação | **Responde** para o WebServer: "entre 4 e 16" | HIL-CARD-02 (enviar 1, 3, 4; anotar retorno 0/128) |
| T7 | `ApagarListaAcesso(Inner)`: apaga só o buffer ou o equipamento? imediato? | janela de lista vazia | Não | NOVO-INT-OFF-12 (apagar, ler cartão da lista antiga em off-line, depois enviar) |
| T8 | Tempo de acionamento: SDK 0–50 s vs WebServer 1–255 s (padrão 5); o que significa 0? | janela de giro e urna | **Responde** para o WebServer (1–255, padrão 5, "tempo em que a catraca aguarda completar o giro") | NOVO-HIL-DIR-09 (enviar 0, 50, 51; cronometrar a janela e anotar retorno) |
| T9 | Nº do Inner: SDK 1–99 vs WebServer 001–255 | endereçamento | **Responde** para o WebServer | NOVO-HIL-NET-02 (Inner 100 no WebServer; `Ping(100)`) |
| T10 | `ConfigurarLeitor1/2` aceita valores além de 0–4 ("Entrada Invertido", "Saída Invertido")? | catraca instalada à esquerda | **Parcial**: WebServer lista 7 modos p/ Leitor 1 e 4 p/ Leitor 2, e usa "Invertido" para posição Esquerda | NOVO-HIL-DIR-10 (configurar no WebServer, ler com `ReceberConfiguracoesInner` — depende de T12) |
| T11 | Assinaturas de `LigarBackLite`, `DesligarBackLite`, `EnviarComandoAcessoNegado`, `LevantarParaOnLine`, `ResetarModoOnLine`, `UtilizarRelogioSegundos`, `ReceberDadosOnLine_Hexadecimal`, `HabilitaQrAsciiEstendido`/`ReceberQrAsciiEstendido`, `GetBufferConfigDLL` | 36 exportações sem assinatura; declarar por dedução corrompe memória | Não | documental; depois UNIT de assinatura + INT por função |
| T12 | `ReceberConfiguracoesInner`: tamanho e layout do buffer | leitura de volta / diff (ADR-0020 item 5) e descobrir os defaults da DLL | Parcial (WebServer exporta configurações, mas cifradas) | NOVO-HIL-CFG-10 |
| T13 | Tabela oficial: cada `Definir*/Configurar*` → qual enviador (`EnviarConfiguracoes`, `…Funcoes`, `…MudancaAutomatica…`, `EnviarMensagensOffLine`) e **qual o default da DLL** de cada campo; como "não ter" cartão master | ADR-0020 exige enviar tudo explícito | Parcial (defaults do WebServer: tempos 5 s, mensagens "Entrada OK"/"Saída OK", urna desabilitada, nível 0) | NOVO-HIL-CFG-10 (enviar só `EnviarConfiguracoes` com buffer vazio e ler de volta/observar no WebServer) |
| T14 | Significado de `Complemento` em cada origem; ele traz o sentido do giro na origem 6? | giro reverso, lotação | Não | NOVO-HIL-DIR-08 (girar nos dois sentidos; anotar `Complemento`) |
| T15 | QR + Mifare no mesmo equipamento: qual sai como origem 2/3/21? precisa de algo além de `ConfigurarTipoLeitor`? | fluxo QR + urna | Não (a página do leitor QR responde: prox vira ABA 14 / Mifare 10 automaticamente) | docs/21 passo 3, linhas 1–4 |
| T16 | `SolicitarExclusaoUsuario`/`UsuarioFoiEnviado`/`UsuarioFoiExcluido`/`ReceberUsuarioLista`: são da lista de acesso? permitem exclusão individual e leitura? | incremental vs completa | Parcial (WebServer permite excluir um usuário — logo o firmware sabe fazer) | NOVO-INT-OFF-10 (excluir 1 cartão; ler de volta; cartão deve ser negado em off-line) |
| T17 | Assinatura de `LerContadorGiro`/`AtribuirContadorGiro` | auditoria de lotação | Não | depois da assinatura: comparar com contagem de origem 6 (NOVO-INT-REC-06) |
| T18 | `EngolirCartao`/`DevolverCartao`: são da urna? No on-line, o recolhimento é `AcionarRele2` ou `EngolirCartao`? | fluxo CollectCardThenEnter | Parcial ("recolhimento… através da configuração do Acionamento 2") | NOVO-HIL-URNA-02 |
| T19 | Como gravar "Possui urna" pelo SDK? `EnviarConfiguracoes` desfaz a flag do WebServer? Para urna liberando **entrada**, `Acionamento2` = 2? | urna no sentido invertido (docs/04) | **Responde** o comportamento nativo (leitor 2 exclusivo, libera **saída** após recolher; urna desabilitada no reset de fábrica) | HIL-URNA-01 (ligar urna no WebServer, reconectar pelo SDK, ver se continua) |
| T20 | `ReceberQuantidadeBilhetes(int, int[])`: tamanho do array e significado | medidor de memória | Não | NOVO-INT-REC-05 (só com o tamanho informado) |
| T21 | A lista de acesso precisa ser enviada com a catraca em off-line? O `EnviarConfiguracoes` on-line zera tipo de lista/mensagens off-line? | ordem da FSM (`FSM:182-188`) | Parcial (WebServer indisponível com a catraca on-line) | NOVO-INT-OFF-11 |
| T22 | Quantas marcações uma passagem gera em off-line (1–3)? | dimensionamento de memória | Parcial (o exemplo de registro traz 1 linha por acesso) | INT-REC-02 (10 passagens off-line, coletar, contar) |
| T23 | Duração de `EnviarListaAcesso` para 15.000; a catraca atende durante? | janela de manutenção, watchdog | Parcial (import pelo WebServer mostra "Aguarde…", recomenda não desligar) | NOVO-LOAD-OFF-02 |
| T24 | `HabilitarMudancaOnLineOffLine`: significado de 0/1/2, unidade de `Tempo`, período mínimo de `PingOnLine`, e em qual envio entra | contingência T2 | Não | INT-SM-021 (modo 2, parar o ping, cronometrar a queda; voltar) |
| T25 | Leitor QR Topdata: `ConfigurarTipoLeitor` 5 ou 8? letras exigem `ReceberDadosOnLine_QRCodeComLetras`? | QR do evento | Parcial (página do leitor: "somente funciona com Código de Barras Serial"; QR alfanumérico suportado) | NOVO-HIL-QR-02 (QR numérico e alfanumérico × tipo 5/8 × 3 variantes de recepção) |
| T26 | Tabela de `FormaEntrada` (0–7, 10–14, 100–105) | rearme do leitor coerente com teclado/urna | Não | INT-SM-032 (cada valor usado; qual leitor reage) |
| T27 | Unidade de `Tempo` em `EnviarMensagemTemporariaOnLine`; charset/acentos | mensagens | Parcial (linhas de 16) | INT-MSG-02 + docs/21 §6B linha 2 |
| T28 | `EnviarRelogio` com a catraca em uso é seguro? | acerto automático | Parcial (relógio dura ~1 h sem energia) | INT-CLK-02 / docs/21 §6A |
| T29 | Mifare UID de 7 bytes em leitor "ABA 10 dígitos": trunca? | colisão de cartões | Não | docs/20 §7 ensaio 2 |
| T30 | `InserirQuantidadeDigitoVariavel` acumula no buffer entre tentativas? `(0)` zera? | reenvio após falha | Não | NOVO-HIL-CARD-07 (montar 4..16, falhar, remontar só 10; ler QR de 12) |
| T31 | `ReceberDigitalUsuario`: qual das duas assinaturas (GER:140 vs 143) é a real | corrupção de memória | Não | nenhuma — remover a não inventariada antes de qualquer uso |
| T32 | `DesabilitarWebServer(1)` desabilita? Persiste? Como reabilitar sem o SDK? | segurança (senha de fábrica publicada no manual) | **Responde** que dá para desabilitar (via Gerenciador/TopAcesso) | NOVO-SEC-WEB-01 |
| T33 | Quais modelos emitem origem 6 e sob qual sensor; a urna cheia bloqueia sozinha? (pauta itens 6 e 11) | passagem física | Não | B-07 e B-08 do `docs/09` |
| T34 | Memória circular: sobrescreve **bilhete ainda não coletado**? `AvisarQuandoMemoriaCheia` muda isso para "parar"? | perda silenciosa de evento | **Responde** que sobrescreve a mais antiga ao atingir 30.000 | NOVO-INT-REC-04 (encher com carga sintética é inviável na bancada; pedir confirmação documental + teste com `AvisarQuandoMemoriaCheia` ligado) |

---

## 6. Riscos técnicos por função nova

Restrições de base: x86, bloqueante, não thread-safe, ~30 equipamentos por instância, uma porta TCP
por worker (`docs/11…:15-20`; ADR-0001/0006/0021). O laço faz **uma chamada bloqueante por passo** e
alterna catraca a catraca (`PUMP:150-153`); o watchdog mata o worker após 30 s sem batida
(`Watchdog.cs:29`); o limite de espera de 500 ms passado a `AguardarEvento` (`DeviceGroupLoop.cs:75`)
**é ignorado** — `ReceberDadosOnLine` não tem parâmetro de timeout (`ADP:205-211`), então o tempo real
de cada volta depende do timeout interno da DLL (T2).

| Função / grupo novo | Thread única / bloqueio | Timeout | x86 / memória | Porta por worker | Mitigação |
|---|---|---|---|---|---|
| `InserirQuantidadeDigitoVariavel` e demais `Definir*` | buffer **global**: montagem de uma catraca não pode intercalar com outra | chamadas locais, rápidas (INF) | nenhum | — | montar+enviar como unidade atômica; reset no início (T30) |
| `EnviarConfiguracoes` ×2 + `…MudancaAutomatica…` por conexão | 3 envios por catraca; com 20 catracas reconectando juntas ("Aplicar agora", `docs/32…:95`), a última espera todas | estados com 30 s (FSM:41-43) | — | reconectar em ondas | escalonar reconexões; medir p95 por envio |
| `PingOnLine` periódico | disputa o mesmo fio com `ReceberDadosOnLine` das outras 19 | se a volta do laço passar do `Tempo` da mudança, a catraca **cai para off-line em silêncio** (`docs/14…:104-106`) | — | — | medir pior volta (NOVO-LOAD-LOOP-01) e exigir `IntervaloPing < Tempo − pior volta`; reduzir catracas por worker se preciso |
| `EnviarListaAcesso` (+ montagem de 15.000 `InserirUsuarioListaAcesso`) | **uma chamada longa** congela as demais catracas do worker | pode passar de 30 s → watchdog mata o worker no meio (lista em estado desconhecido) | buffer de 15 mil strings no processo x86: aceitável (INF) | carga paralela só entre workers diferentes | modo manutenção; tolerância de watchdog por classe de operação; hash para não recarregar; medir t(N) |
| `EnviarHorariosAcesso` / `ApagarHorariosAcesso` / `ApagarListaAcesso` | idem, menores | AC | — | — | junto da carga de lista |
| `ColetarBilhete` em laço + `ReceberQuantidadeBilhetes` | 1 bilhete por passo já intercala (bom); mas 30.000 bilhetes × tempo por chamada | estado `ColetarBilhetes` = 10 min (FSM:49) pode não bastar → `Degradado` no meio | `int[]` de tamanho desconhecido (T20) = risco de estouro | — | **commit local antes do próximo** (R-68); retomar sem perda; medir chamadas/s |
| `AcionarRele2` / `EngolirCartao` (urna) | curtos, mas o ciclo urna→origem 7→liberar→origem 6 ocupa vários passos da mesma catraca | T2/T3/T4 (docs/04) precisam somar a latência da volta do laço | — | — | calibrar T2/T3 com a volta medida, não com o tempo físico |
| `AcionarBipCurto/Longo`, mensagens temporárias | cada um é uma chamada a mais no caminho crítico da passagem | aumenta o ciclo (docs/14: 0,5 s = ~350 pessoas/h em 4 catracas) | — | — | só em negação/erro; nunca por passagem autorizada |
| `ReceberDadosOnLine_QRCodeComLetras` / `_ComLetras` | mesma natureza bloqueante | igual à `ReceberDadosOnLine` | comprimento máximo **não documentado**; buffer de 64 bytes (`NAT:38-45`) pode estourar → corrupção de memória | — | só após T25; aumentar buffer por prudência e validar terminador |
| `ReceberConfiguracoesInner` / `ReceberUsuarioLista` | leitura potencialmente longa | AC | buffer de tamanho **desconhecido** = corrupção de memória | — | proibido chamar antes de T12/T16 |
| Biometria (`Requisitar…`/`Resposta…`) | 2+ chamadas por operação, várias rodadas por cadastro | AC | templates em `byte[]` sem tamanho documentado | — | fora do escopo do evento |
| `DesabilitarWebServer`, `DefinirNumeroCartaoMaster`, `DesabilitarBloqueioCatracaMicroSwitch` | parte do buffer comum (sem custo extra) | — | — | — | risco é de **segurança física/lógica**, não de desempenho: valor explícito, nunca editável no modo guiado |
| Qualquer função `SO_O_NOME` | — | — | declarar por dedução = corrupção de memória (x86, sem proteção) | — | proibido até T11 (trava de contrato já existe) |
| Retorno 8 (GPF) em qualquer função nova | derruba o processo inteiro (ADR-0001) | — | .NET FW 3.5 / arquitetura | um worker = até 20 catracas | `AdapterResult.FromNative` por função; `AguardarEvento` não pode converter 8 em `SemEventos` (`ADP:216`) |

**Porta por worker (ADR-0021)**: nenhuma função nova muda a regra; o que muda é **operacional** —
cargas de lista e reconexões em massa só paralelizam entre workers, e mover uma catraca de worker exige
reconfigurar o IP/porta **na catraca** (WebServer "Endereço IP do Servidor"/"Porta").

