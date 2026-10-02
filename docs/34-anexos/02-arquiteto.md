# 02 — Arquiteto: estado atual e lacunas (catraca, cartões, gêmeo)

> Estudo somente leitura do repositório `conexao-topdata` no commit `78517de`
> (merge do PR #3). Nada foi editado no repositório. Referências no formato
> `arquivo:linha`. "A_CONFIRMAR_COM_TOPDATA" marca o que depende da DLL/firmware e
> não pode ser deduzido do código nem dos docs.

(Relatório escrito em partes; seções abaixo na ordem da entrega.)

## Resumo executivo

- **Configuração da catraca**: três camadas — instalação (`workers.json`: só Inner e Nome por catraca), evento (`edge_setting`: 7 chaves globais) e `DeviceConfiguration` (17 campos, 13 fixos em código). **Não existe parametrização por catraca.**
- **Comandos**: `operator_command` (migração 009) com auditoria por gatilho, fila lida pelo worker a cada 500 ms, execução só em `Polling`. Cinco tipos. Tipo novo não exige migração (`kind` sem CHECK).
- **A catraca hoje usa 23 funções da DLL no caminho real**; outras 9 estão no adapter mas só seriam chamadas com parâmetros que hoje não mudam ou estados nunca alcançados (saída/invertida/dois sentidos, off-line, dígito fixo, PingOnLine, AcionarRele2, ColetarBilhete); bip, LEDs e EI-029 estão declarados à mão sem uso; EI-012 e ~190 outras só geradas.
- **Defeitos latentes** a corrigir antes de expor parâmetros: dupla inversão de sentido (pump + adapter), dígitos variáveis nunca enviados (EI-012, viola ADR-0020), configuração completa enviada 3× por conexão, sinais da catraca (origem 7/20/sensores) caindo na decisão e mostrando "Acesso nao autorizado".
- **Cartões**: moram em `ticket` (provedor reutilizável), chegam só pela nuvem (`sync-cards`), bancada/simulação e venda de balcão sem tela. **Fase 3 inteira não existe**: tipos, cadastro, importação `.xlsx/.csv`, desfazer. O `.proto` não pode conter `cartao`/`card`. Importação grande pode travar o worker (busy_timeout 5 s) — risco nº 1 da frente B.
- **Gêmeo**: observa `AcompanharEventos`; sem origem da leitura, sem giro tardio, sem comandos, catálogo estático. Tudo resolvível sem tocar no worker, exceto a coluna de origem na tentativa.



---

## 1. Mapa do fluxo atual, de ponta a ponta

### 1.1 Onde mora a configuração da catraca (três lugares, três donos)

| Camada | Onde | Quem grava | Quem lê | Escopo |
|---|---|---|---|---|
| **Instalação** (`workers.json`) | `src/Edge.Supervisor/Instalacao/AssistenteDeConfiguracao.cs:14-39` (`DadosDaInstalacao`: catracas {Inner, Nome}, porta, nuvem, perfil, reuso, somente-na-urna, simulação) | Assistente (`Edge.Configurador`, `AssistenteDeConfiguracao.Gravar` :142-172, grava em temporário e troca :161-164; segredo vai para o cofre :166-169) | Serviço (`ConfiguracaoDoSupervisor.Ler`) → monta 1 grupo `catracas` com todos os Inner numa porta (`Montar` :124-136) | por PC; **por catraca só existe Inner e Nome** |
| **Evento** (`edge_setting`, migração 006) | `src/Access.Infrastructure.SQLite/ConfiguracoesDaBorda.cs:24-31` (`ConfiguracaoDaOperacao`: TipoDeLeitor=8, LeitorDaUrna=true, TempoDeAcionamento=5, MensagemPadrao, ConectorDoEspelho, EsperaPeloGiroSegundos=10, AcertarRelogioAoDivergir=false); chaves `:71-77` | Serviço, RPC `GravarConfiguracao` (`src/Edge.Supervisor/EdgeControlService.cs:266-307`), chamado pela tela Configurações | Worker ao subir (`src/Edge.Worker.X86/Program.cs:230-242`) e no "Aplicar agora" (`Program.cs:260-267`) | **global: a mesma para todas as catracas** |
| **Equipamento** (`DeviceConfiguration`) | `src/Access.Application/Devices/DeviceConfiguration.cs:21-71` (17 campos) + `Validar()` :77-173 | Não é persistido. É montado em memória no worker: `ConfiguracaoDeBancada.TopFit4(...)` (`src/Edge.Worker/Bancada/SessaoDeBancada.cs:31-49`) sobrescrito por `TempoDoAcionamento1` e `MensagemPadrao` (`Program.cs:198-203`) | `DevicePump` a cada conexão | por worker (mesmo valor para todos os slots, `SessaoDeOperacao.cs:126`) |

Consequências:
- `DeviceConfiguration` é **constante de código** em 13 dos 17 campos (PadraoCartao=1, dígitos variáveis 4..16, Leitor1=1, Acionamento1=2, Acionamento2=0/0, Online, Teclado off, MudancaAutomatica=0...). Só `TipoDeLeitor`, `OperacaoDoLeitor2` (via `LeitorDaUrna`), `TempoDoAcionamento1` e `MensagemPadrao` vêm do operador.
- O modelo de dados de `docs/05 §2` (`device`, `device_capability`, `device_config_version` com hash/aprovação/rollback/drift) **não existe** nas migrações 001–009. É esboço.
- `GatePhysicalProfile.SentidoInvertido` é sempre `false` (`SessaoDeBancada.cs:48`); não há tela nem chave para ele.

### 1.2 Como o assistente grava e como o worker aplica

1. Assistente → `workers.json` (Inner/Nome/porta) → `Program` do Supervisor sobe `ProcessoDeWorker` com `--porta --inners --banco [--simulador]`.
2. Worker (`Edge.Worker.X86/Program.cs:209-320`): aplica migrações (`:228`), lê `edge_setting` (`:230`), valida; inválida → sobe com o padrão e registra (`:236-242`, regra "configuração ruim não para catraca"). Monta `DeviceConfiguration` (`:255`), cria `SessaoDeOperacao` com `FilaDeComandosSqlite` e o delegado `Recarregar` (`:274-289`).
3. `DevicePump` (máquina por catraca, uma chamada bloqueante por passo — `src/Edge.Worker/DevicePump.cs:146-153`):
   - `Conectar` → `TestarConexao` (= `Ping` EI-004) `:321-350`; marca acerto de relógio `:342`.
   - `LerIdentidade` → `LerFirmware` (EI-006) `:352-365`; `VerificarCompatibilidade` contra linhas {14,16} `:231,367-378` (ADR-0010, sem chamada nativa).
   - **`EnviarCfgOffline`, `EnviarConfigMudOnlineOffline`, `EnviarCfgOnline` chamam os três o mesmo `EnviarConfiguracaoCompleta`** (`:306-308`, `:380-394`). Ou seja, a sequência completa (DefinirPadraoCartao… HabilitarMudancaOnLineOffLine + EnviarMensagemPadraoOnLine + EnviarConfiguracoes) é enviada **3 vezes** por conexão. O adapter nunca chama `EnviarConfiguracoesMudancaAutomaticaOnLineOffLine` (EI-029, declarada em `src/Topdata.EasyInner.Interop/EasyInnerNative.cs`, fora de `IEasyInnerNative`). O "offline" do nome do estado não usa `ConfigurarInnerOffLine` — só se `Online=false`. **Lacuna de fidelidade ao manual 2.1.2**; semântica exata: A_CONFIRMAR_COM_TOPDATA.
   - `SincronizandoDadosOffline` é passo vazio (`:396-402`: "vazio nesta fase") — nenhuma lista é enviada à catraca.
   - `ConfigurarEntradasOnline` → `EnviarFormasEntradasOnLine(forma=7 "teclado e os dois leitores")` fixo (`src/Topdata.EasyInner.Adapter/TopdataInnerAdapter.cs:32,188-197`).
   - `EnviarMsgPadrao` → `EnviarMensagemPadraoOnLine(exibirData=0)` (`DevicePump.cs:418-430`; adapter `:199-203`).
   - `Polling`/`MonitoraGiroCatraca` → `AguardarEvento` = `ReceberDadosOnLine` (EI-040) `:432-473`. Origem 6 → `GiroConfirmado`; origem 5 → `TempoDeAcionamentoEsgotado`; outra → `EventoRecebido`.
   - `ValidarAcesso` → `_decidir` (decisor) `:497-518`; `LiberarCatraca` → `LiberarGiro` `:520-543`; `EnviarMsgAcessoNegado` → mensagem temporária fixa "Acesso nao autorizado" 3 s `:545-557`.
4. **"Aplicar agora"**: tela Configurações → `EnviarComando(APLICAR_CONFIGURACAO, inner=0)` → serviço cria um `operator_command` por catraca cadastrada (`EdgeControlService.cs:488-558`, alvo "todas" em `:508-511`) → worker lê a cada 500 ms (`SessaoDeOperacao.cs:185-231`), relê `edge_setting` pelo `Recarregar` (`:233-248`; inválida → comando `Falhou` sem tocar a catraca), `DeviceSlot.Enfileirar` valida de novo (`DevicePump.cs:48-74`) → em `Polling`, `Executar` troca `Configuracao` e dispara `ReconexaoSolicitada` (`:597-613`) → reconexão completa reenvia tudo (ADR-0020) → `AcompanharComando` conclui quando volta a `Polling` ou falha em 2 min (`:661-684`). Só leitor/urna/tempo/mensagem mudam sem reiniciar; espelho e espera pelo giro só no próximo início (`Program.cs:257-259`). **`GravarConfiguracao` ainda responde `exige_reinicio`** (`EdgeControlService.cs:305`, proto `:206-207`) — texto do contrato ficou desatualizado frente ao "Aplicar agora".

### 1.3 Comandos por catraca (`operator_command`)

- Domínio: `TipoDeComando` {AcertarRelogio, MensagemTemporaria, LiberacaoManual, ReiniciarConexao, AplicarConfiguracao} (`src/Access.Application/Devices/ComandoDeCatraca.cs:8-24`); validade 15 s (liberação) / 60 s (demais) `:77-78`; validação em `Criar` `:81-143` (inner 1–99, operador 2–80, mensagem 1–32 e 1–60 s, motivo 5–200).
- Porta: `IFilaDeComandos` `:150-160` (Pendentes / Receber / Concluir). Implementação `src/Access.Infrastructure.SQLite/FilaDeComandosSqlite.cs` — `Receber` é `UPDATE … WHERE status='pendente' AND expires_at > $em` (`:94-95`, só um worker vence); `Concluir` `:116-117`; `ExpirarVencidos` `:133-136` (chamado pelo serviço em `ListarComandos`).
- Tabela: migração 009 (`Migrations/009_comandos_da_catraca.sql:5-44`), `CHECK` de status, três gatilhos: sem DELETE, pedido imutável, status final não volta.
- Worker: executa só em `Polling` (`DevicePump.cs:285-298`); desfechos gravados com retentativa em memória se a base estiver ocupada (`SessaoDeOperacao.cs:257-281`); fila expira na catraca presa (`DevicePump.cs:244-269`).
- Liberação manual: `DescartarPendente` antes (`DevicePump.cs:589-595`, `DecisorDeIngresso.cs:169-177`) → mesmo caminho `LiberarCatraca`→`MonitoraGiro`; conclusão "girou / ninguém girou / sem sinal em 60 s" (`DevicePump.cs:631-659`).
- Proto: `EnviarComando`/`ListarComandos` (`src/Contracts/Protos/edge_control.proto:42-43, 320-377`), enums com `*_NAO_ESPECIFICADO = 0`.
- Auditoria de **configuração** (`GravarConfiguracao`) é só `edge_setting.updated_by/updated_at` da última mudança — sem histórico.

### 1.4 Decisão de acesso (QR/cartão, urna, reuso)

- Entrada: `SessaoDeOperacao.Decidir` → `DecisorDeIngresso.Decidir` (`src/Access.Application/Ingressos/DecisorDeIngresso.cs:83-136`). Credencial = `evento.RawCardData?.Trim()` (`:88`) — **o perfil de normalização do provedor NÃO é aplicado na leitura** (só na ingestão: `FonteDeCartoesDoPainel.cs` `TentarLerNumero`, `ArquivoDeBancada.cs:69,93`). Um perfil que complete zeros (`mifare-catraca4`, `PerfisDeLeitura.cs:51-54`) só casa se a catraca já entregar os 10 dígitos. A_CONFIRMAR na bancada (docs/21 linhas 8–12).
- Leitor: origem Leitor1/Leitor2 vira `KnownEventOrigin` (`:95-97`); Leitor2 = fenda da urna.
- Consumo atômico: `RepositorioDeIngressos.TentarUsar` (`src/Access.Infrastructure.SQLite/RepositorioDeIngressos.cs:327-384`) com um único `UPDATE ticket … WHERE` (`ConsumirUmUso` `:961-1002`): status válido, usos, janela, provedor habilitado, `urn_only` exige Leitor2, intervalo de reuso por `last_used_epoch`. Toda tentativa vira linha em `ticket_use_attempt` e (se ligado) outbox do espelho, na mesma transação.
- Pendência por equipamento: giro (origem 6) confirma (`DecisorDeIngresso.cs:141-162` → `ConfirmarPassagemFisica`), origem 5 encerra sem giro (ADR-0007).
- Falha de base → nega com `FalhaNaBaseLocal` (`:110-115`), nunca libera por omissão.
- **Urna física não é acionada**: `FuncaoDoAcionamento2 = 0` sempre (`SessaoDeBancada.cs:40`); `AcionarReleDaUrna` existe no adapter (`TopdataInnerAdapter.cs:259`) mas **não é chamado por ninguém** fora do simulador. "Cartão na urna" hoje = "lido no leitor 2", não "recolhido".

### 1.5 Eventos e prestação de contas

- Worker grava `ticket_use_attempt` (e `passage_confirmed_at`), `device_status` (batimento, `Operacao.GravarSituacao`, `src/Access.Infrastructure.SQLite/Operacao.cs:70`), `operator_command`.
- `raw_event`, `access_decision`, `device_command`, `physical_passage`, `device_state_transition` (migração 001) **existem, mas só o `AccessJournal` grava nelas, e ele só é usado por testes e pelo `tests/CrashProbe`** (conferido: nenhum `INSERT` dessas tabelas no caminho de operação; `device_state_transition` não é gravada por ninguém, apesar do comentário em `DeviceStateMachine.cs:57-62` dizer que a trilha durável vai para ela). A fonte da prestação é `ticket_use_attempt`.
- Serviço: `AcompanhamentoDaOperacao` lê `ticket_use_attempt` por `rowid` a cada 500 ms e difunde `EventoDeAcesso` (`src/Edge.Supervisor/AcompanhamentoDaOperacao.cs:43-75`). **Não preenche `origem_bruta`/`origem_conhecida`** (`:61-74`), e o giro posterior à gravação não é redifundido (limitações do gêmeo, docs/33 §7).
- Prestação: `ObterPrestacaoDeContas` (por categoria, catraca, hora, negativas). Liberações manuais ficam só em `operator_command` e **não entram nos relatórios** (docs/32 §3, docs/29 §4).
- Bilhetes off-line: estado `ColetarBilhetes` existe, mas **nenhum gatilho `IniciarColetaDeBilhetes` é disparado** e o bilhete coletado **é descartado** (`DevicePump.cs:475-495` não entrega o `Bilhete` a ninguém; comentário R-68 exige commit antes do próximo passo). Mesmo para `CairParaListaLocal`, `EntrarEmManutencao`, `Quarentenar`: só existem na tabela da máquina.

### 1.6 Gêmeo digital (demonstração e ao vivo)

- ViewModel sem WPF: `src/Desktop.ViewModels/GemeoDigital/GemeoDigitalViewModel.cs` (`TelaBase`, testável em Linux). Tela: `src/Desktop.App/Telas/Gemeo.xaml(.cs)` (949 linhas de desenho/câmera, nenhuma regra).
- Cena: `CenaDaCatraca` (máquina **visual** pura; `CenaDaCatraca.cs:126-430`), sinais `SinalDaCena` (`:69-91`: Conectou, PerdeuComunicacao, Credencial(leitor), Liberado, Negado, Giro(sentido), TempoEsgotado, UrnaCheia, UrnaEsvaziada). `SentidoDoGiro` já tem Entrada/Saída (`:43`).
- **Demonstração**: `Roteiros` (8 cenários, `Roteiros.cs:45-181`) executados por `ExecucaoDeRoteiro`; "Rodar na catraca simulada" chama `SimularLeitura` (só com o serviço em simulação, `GemeoDigitalViewModel.cs:595-620`).
- **Ao vivo**: assina `AcompanharEventos` (`:454-493`), filtra a catraca escolhida, ignora o histórico reentregue (`:423-429`), traduz por `TraducaoAoVivo.Sinais` (`TraducaoAoVivo.cs:25-59`). Saúde vem de `ListarEquipamentos.em_operacao` (`:77-81`). Configuração do evento (`ObterConfiguracao`) muda mensagem padrão, urna ligada e espera pelo giro (`GemeoDigitalViewModel.cs:532-560`).
- **O gêmeo não comanda nada** (docs/33 §2). Liberação manual / mensagem temporária / aplicar **não aparecem** ao vivo (vivem em `operator_command`, fora do fluxo). Origem do leitor não chega (`origem_bruta` vazio) → leitura ao vivo sem celular/cartão, exceto `ForaDaUrna` deduzido pelo motivo (`TraducaoAoVivo.cs:62-74`).
- Catálogo de funções por peça é **estático**: `CatalogoDaFit4` (`Pecas.cs:110-262`) com `SituacaoDaFuncao` {Disponivel, AguardandoConfirmacao, NaoUsadaNestaInstalacao, ForaDoEscopo} escrita à mão. Não lê capacidades nem a configuração por catraca.
- Modelo 3D por código (`GeometriaFit4`), medidas em `fit4.json` embutido (`A_CONFIRMAR` nas medidas).
- Captura de tela: `src/Desktop.App/CapturaDeTela.cs:69-150` percorre `janela.Telas` e tem roteiro próprio para o gêmeo.

---

## 2. Inventário: cartões e credenciais

### 2.1 O que existe

**Tabelas (migrações reais, não o esboço do docs/05):**

| Tabela | Migração | Colunas relevantes | Papel hoje |
|---|---|---|---|
| `ticket_provider` | 003 (+004, 005) | `id, name, normalization_profile, connector, enabled, created_at` + `reusable, reuse_interval_seconds` (004) + `urn_only` (005) | Provedor (bilheteria local = "cartão"; site/Zet = "QR") |
| `ticket` | 003 (+004) | `id, provider_id, external_ref, qr_raw, qr_normalized, sector, valid_from, valid_to, max_uses, used_count, status ∈ {valido,consumido,cancelado,bloqueado}, ingested_at, first_used_at, last_used_at, last_gate_id, reported_at` + `category, last_used_epoch` (004). Índices únicos `ux_ticket_provedor_ref (provider_id, external_ref)` e **`ux_ticket_qr (qr_normalized)` global** | **É aqui que o "cartão" mora**: uma linha por cartão físico (provedor reutilizável) |
| `ticket_sale` | 004 | `id, ticket_id, provider_id, category, uses, sold_at, operator` | Venda de balcão que "carrega" um cartão |
| `ticket_use_attempt` | 003 (+004, 006) | `id, ticket_id?, provider_id?, qr_normalized, gate_id, device_id, outcome, reason, decision_id?, at, passage_confirmed_at` + `category` (fotografada) | Toda tentativa; fonte da prestação de contas |
| `access_decision.credential_value` | 001 | texto normalizado | Só usado pelo `AccessJournal` (testes/CrashProbe) |

**Não existem**: `credential`, `person`, `person_credential`, `card_stock`, `card_custody_event` (esboço do docs/05 §3), tabela de **tipos** (nome exibido, ordem, cor, ativo), tabela de **lote de importação** (para "desfazer a última importação"), auditoria de alterações de cadastro.

**Entidades/porta:** `Access.Domain/Ticketing/Ingresso.cs` (`IngressoRecebido`, `ProvedorDeIngresso` com `PerfilDeNormalizacao`, `Reutilizavel`, `IntervaloDeReuso`, `SomenteNaUrna`), `ResultadoDoUso`/`MotivoDoUso`; `IValidadorDeIngressos` (`DecisorDeIngresso.cs:15-27`); `IDestinoDeIngressos` (Sync.Ingestao `Portas.cs`).

**Formas de um código entrar na base hoje:**
1. **Nuvem (painel Supabase)** — `FonteDeCartoesDoPainel` (`src/Sync.Connectors.Rest/Painel/FonteDeCartoesDoPainel.cs`): `middleware-sync-cards`, cursor = `sync_timestamp` do servidor; recusa `card_number` numérico JSON (ADR-0008); aplica o perfil (`raw`/`mifare-catraca4`/`qr-catraca4`, `SincronizacaoComANuvem.cs:42-48`); `admission_type` → `category`; `removed_cards` → cancelado; não avança cursor se suspeitar de corte (≥1000 sem `contract_version`). Vai para o provedor `bilheteria-local` (reutilizável, reuso 240 s, somente-na-urna por padrão). Laço: `Sync.Ingestao/LacoDeIngestao.cs` → `RepositorioDeIngressos.Ingerir` (`RepositorioDeIngressos.cs:252-300`, lote não tudo-ou-nada: colisão é registrada e o resto entra).
2. **Zet / contrato v1** — `TradutorDaZet`, `TradutorDoContratoV1`, `FonteDeRelay` (Relay.Ingressos): ingressos (QR) de site.
3. **Venda de balcão** — `RepositorioDeIngressos.VenderNoBalcao` (`:595-705`): sem tela nem RPC; só usado por `ArquivoDeBancada`.
4. **Arquivo de bancada / simulação** — `ArquivoDeBancada.Carregar` (`src/Access.Infrastructure.SQLite/ArquivoDeBancada.cs`), JSON com provedores/ingressos/cartões (cartões normalizados com `MifareCatraca4`, `:93`), usado por `--bancada` (`Edge.Worker.X86/Program.cs:140`) e pela simulação (`Edge.Supervisor/Program.cs:121`, `simulacao.exemplo.json`). **Ferramenta de teste, não de produção** (comentário `:20-23`).

**Consulta de código**: RPC `ConsultarCodigo` (`edge_control.proto:274-288`) → `ConsultasDaOperacao.ConsultarCodigo` (`src/Access.Infrastructure.SQLite/ConsultasDaOperacao.cs:217+`) procura por `qr_normalized` **ou** `qr_raw` (sem aplicar perfil), devolve mascarado + histórico. Tela `ConsultaViewModel` (`Telas.cs:424`) apaga o código digitado.

**Credencial como string (ADR-0008)**: `CredentialValue` (bruto + normalizado + perfil, `ToString()` mascarado, `Mascarar`), `CredentialNormalization` (sem opção de tirar zeros; `PadLeftTo`, `UpperCase`, `AllowedLengths`), `PerfisDeLeitura` (`QrCatraca4` 4–16; `MifareCatraca4` pad 10). Todas as colunas são `TEXT` em tabelas `STRICT`. O contrato gRPC **não tem campo** para o número em claro (teste `ContratoIpcTests.O_contrato_nao_tem_campo_para_dado_sensivel` proíbe as palavras `cartao`, `card`, `credencial =` em todo o `.proto`).

**Modelo de planilha**: `installer/modelos/{modelo-cadastro-de-cartoes.xlsx, tipos-modelo.csv, cartoes-modelo.csv}` + `ModelosDePlanilhaTests` (só confere cabeçalho e exemplos `9999…`). **Não há nenhum código de importação** (`grep xlsx|ZipArchive` só acha o teste). Nenhum pacote de planilha nas dependências (as únicas são gRPC, Protobuf, Sqlite, WindowsServices, ProtectedData, ServiceController).

### 2.2 O que NÃO existe (fase 3 inteira)

- Cadastro de **tipos** (código, nome exibido, ordem, cor, ativo) — `category` é texto livre (migração 004 `:11-13`). "Tipo inativo nega o uso" (docs/26 §1) não é possível.
- Cadastro/edição de **um cartão** pela tela (com leitura pelo leitor do balcão), bloqueio/desbloqueio manual, titular, observação.
- **Importação** `.xlsx`/`.csv` do docs/26: prévia, erros por linha, duplicados no arquivo, detecção de célula numérica/notação científica, aviso "mesmo código com zeros a mais/menos", transação única, arquivo de devolução, **desfazer a última importação**, teste de 100 mil linhas.
- Validade `dd/mm/aaaa hh:mm` em Brasília (a base guarda ISO; conversão de entrada não existe).
- Regra de **fonte da verdade** entre cadastro local e `sync-cards` (docs/29 §4; ADR-0023 diz "cadastro é da nuvem" — um cadastro local contradiz isso e precisa de decisão).
- Titular/pessoa e LGPD (base legal, retenção, expurgo) — docs/29 §5 exige antes de cadastrar pessoas.
- Lista local da catraca (T2): `SincronizandoDadosOffline` vazio; `DefinirTipoListaAcesso/InserirUsuarioListaAcesso/EnviarListaAcesso` só gerados (`EasyInnerGerada.cs`).
- **Normalização na leitura**: a decisão compara `RawCardData.Trim()` (`DecisorDeIngresso.cs:88`) com `qr_normalized` gravado com o perfil do provedor. Se um cadastro importado for normalizado com `mifare-catraca4` e a catraca entregar menos dígitos, não casa. Precisa de decisão: normalizar a leitura pelo perfil do leitor (por catraca/leitor), não só na entrada.

---

## 3. O que a catraca faz HOJE no produto

### 3.1 Funções nativas: usadas, e em que estado

Costura `IEasyInnerNative` (`src/Topdata.EasyInner.Interop/IEasyInnerNative.cs`, 33 membros: 31 funções da DLL + 2 utilitários de buffer) ← `TopdataInnerAdapter` ← `ITopdataInnerAdapter` (15 métodos + Dispose, `src/Access.Application/Devices/ITopdataInnerAdapter.cs:95-171`) ← `DevicePump`.

| EI | Função DLL | Método do adapter | Chamado por / estado | Observação |
|---|---|---|---|---|
| 001/002 | DefinirTipoConexao(2) + AbrirPortaComunicacao | `AbrirPorta` | `DeviceGroupLoop.Iniciar` (antes do laço) | uma vez por worker |
| 003 | FecharPortaComunicacao | `FecharPorta`/`Dispose` | `Encerrar` | |
| 004 | Ping | `TestarConexao` | `Conectar` (Discovering/Reconectar/Degradado/Conectar) | |
| 005 | PingOnLine | `Ping` | **ninguém** | só necessário com MudancaAutomatica=2 |
| 006 | ReceberVersaoFirmware | `LerFirmware` | `LendoIdentidade` | linha comparada com {14,16} (`DevicePump.cs:231`) |
| 007 | ReceberRelogio | `LerRelogio` | `Polling` (conferência 1 min / 1 h) | |
| 008 | EnviarRelogio | `AcertarRelogio` | `Polling` (após conexão; comando `AcertarRelogio`) | acerto automático ao divergir desligado (A_CONFIRMAR, docs/21 6A) |
| 010,011,013–020,028,056,030 | DefinirPadraoCartao, DefinirQuantidadeDigitosCartao (só se fixo), ConfigurarTipoLeitor, ConfigurarLeitor1/2, ConfigurarAcionamento1/2, ConfigurarInnerOnLine/OffLine, HabilitarTeclado, HabilitarMudancaOnLineOffLine, EnviarMensagemPadraoOnLine, EnviarConfiguracoes | `EnviarConfiguracaoCompleta` | `EnviarCfgOffline`, `EnviarConfigMudOnlineOffline`, `EnviarCfgOnline` (3× a mesma sequência) | ver 3.3 |
| 032 | EnviarFormasEntradasOnLine(forma=7) | `ConfigurarEntradasOnline` | `ConfigurarEntradasOnline` (após conectar, negar, girar) | rearma o leitor; forma fixa |
| 056 | EnviarMensagemPadraoOnLine(exibirData=0) | `EnviarMensagemPadrao` | `EnviarMsgPadrao` | |
| 040 | ReceberDadosOnLine | `AguardarEvento` | `Polling`, `MonitoraGiroCatraca` | retorno 0 + origem 0 = sem evento (A_CONFIRMAR, HIL-EVT-01) |
| 041/043 (042/044/045) | LiberarCatracaEntrada[Invertida] (Saida[Invertida], DoisSentidos) | `LiberarGiro` | `LiberarCatraca` (ingresso e liberação manual) | ver bug 3.4 |
| 057 | EnviarMensagemTemporariaOnLine | `ExibirMensagemTemporaria` | `EnviarMsgAcessoNegado` (fixo "Acesso nao autorizado", 3 s) e comando `MensagemTemporaria` | |
| 039 | ColetarBilhete | `ColetarBilhete` | `ColetarBilhetes` — **estado nunca alcançado**; bilhete descartado | |
| 047 | AcionarRele2 | `AcionarReleDaUrna` | **ninguém** (só simulador) | função do relé 2 na TopFit 4: A_CONFIRMAR_COM_TOPDATA |

**Declaradas à mão em `EasyInnerNative.cs` mas fora de `IEasyInnerNative`/adapter:** `EnviarConfiguracoesMudancaAutomaticaOnLineOffLine` (EI-029), `AcionarBipCurto`/`AcionarBipLongo` (EI-048/049), `LigarLedVerde/DesligarLedVerde/LigarLedVermelho/DesligarLedVermelho` (EI-050–053; docs/11 §2.3: LEDs só Linha 3, não TopFit 4).

**Só geradas (`EasyInnerGerada.cs`, 192 externs, nada chamado):** entre as relevantes às frentes A/B/C — `InserirQuantidadeDigitoVariavel` (EI-012), `DefinirFuncaoDefaultLeitoresProximidade`, `DefinirNumeroCartaoMaster`, `RegistrarAcessoNegado` (EI-021), `DefinirEntradasMudancaOffLine`, `DefinirMensagemPadraoOffLine`/`…Entrada/SaidaOffLine`, `DefinirConfiguracoesFuncoes`/`EnviarConfiguracoesFuncoes`, `DefinirLogicaRele`, `DefinirEventoSensor`, `DefinirSensorPortaOffline`, `DesabilitarBipCatraca`, `DesabilitarBloqueioCatracaMicroSwitch`, `DesabilitarWebServer`, `AvisarQuandoMemoriaCheia`, `CartaoMasterLiberaAcesso`; listas e horários (`DefinirTipoListaAcesso`, `InserirUsuarioListaAcesso`, `EnviarListaAcesso`, `ApagarListaAcesso`, `InserirHorarioAcesso`, `EnviarHorariosAcesso`, `EnviarHorarioVerao`); leitura (`ReceberConfiguracoesInner` — base para a "leitura de volta" do ADR-0020 —, `ReceberDadosOnLine_ComLetras`/`_QRCodeComLetras`, `ReceberDataHoraDadosOnLine`, `ReceberVersaoFirmware6xx`, `ReceberQuantidadeBilhetes`); tempo real (`AcionarRele1`, `ManterRele1/2Acionado`, `DesabilitarRele1/2`, `EngolirCartao`, `DevolverCartao`, `LerSensoresInner`, `LigarBipIntermitente`/`DesligarBipIntermitente`, `EnviarSinalizacao`). **Semântica de todas estas na TopFit 4: A_CONFIRMAR_COM_TOPDATA** (a matriz `docs/compatibility-matrix/funcoes-easyinner.csv` cobre EI-001..058; `modelos.csv` tem a Fit 4 como `NAO_ENSAIADO` com quase tudo `LACUNA`).

### 3.2 Estados sem ação de laço

`OfflineAutonomo`, `Quarentena`, `Manutencao`, `FirmwareIncompativel`, `Disabled` (retornam "estado sem ação de laço", `DevicePump.cs:317`). Gatilhos `IniciarColetaDeBilhetes`, `CairParaListaLocal`, `EntrarEmManutencao`, `SairDeManutencao`, `Quarentenar`, `Habilitar`/`Desabilitar` **não são disparados por código de produção**. `FirmwareIncompativel` é beco sem saída (só sai por `EntrarEmManutencao`, que ninguém dispara).

### 3.3 Parametrização atual — todos os campos de `DeviceConfiguration`

| Campo | Faixa validada | Valor em operação | Origem do valor | Enviado por |
|---|---|---|---|---|
| `PadraoCartao` | 0–1 | 1 (livre) | código (`SessaoDeBancada.cs:33`) | `DefinirPadraoCartao` |
| `QuantidadeFixaDeDigitos` | 1–16 ou nulo | nulo | código | `DefinirQuantidadeDigitosCartao` (só se não nulo) |
| `QuantidadesVariaveisDeDigitos` | cada 1–16 | [4..16] | código (`:34`) | **NUNCA ENVIADO** — `InserirQuantidadeDigitoVariavel` (EI-012, "alto risco Linha 4/QR") não está no adapter. Viola ADR-0020 item 3 (campo não coberto pelo montador); a catraca fica com o padrão da DLL |
| `TipoDeLeitor` | 0–8 | 8 | `edge_setting leitor.tipo` | `ConfigurarTipoLeitor` |
| `OperacaoDoLeitor1` | 0–4 | 1 | código | `ConfigurarLeitor1` |
| `OperacaoDoLeitor2` | 0–4 | 1 ou 0 | `edge_setting leitor.urna` | `ConfigurarLeitor2` |
| `FuncaoDoAcionamento1` | 0–9 | 2 (registro entrada) | código | `ConfigurarAcionamento1` |
| `TempoDoAcionamento1` | 0–50 | 5 | `edge_setting catraca.acionamento_segundos` (1–50) | idem |
| `FuncaoDoAcionamento2` | 0–9 | 0 | código | `ConfigurarAcionamento2` |
| `TempoDoAcionamento2` | 0–50 | 0 | código | idem |
| `Online` | bool | true | código | `ConfigurarInnerOnLine/OffLine` |
| `TecladoHabilitado` | bool | false | código | `HabilitarTeclado` |
| `EcoDoTeclado` | 0–2 | 0 | código | idem |
| `MudancaAutomatica` | 0–2 | 0 | código | `HabilitarMudancaOnLineOffLine` |
| `TempoDaMudancaAutomatica` | 1–50 | 10 | código | idem |
| `MensagemPadrao` | ≤32 | "Aproxime o ingresso" | `edge_setting catraca.mensagem` | dentro de `EnviarConfiguracaoCompleta` **e** em `EnviarMsgPadrao` |
| `PerfilFisico.SentidoInvertido` | bool | false | código | usado em `LiberarGiro` (não é enviado à DLL) |

Fixos fora de `DeviceConfiguration` (no adapter): tipo de conexão 2, `formaEntrada=7` e teclado zerado em `EnviarFormasEntradasOnLine`, `exibirData=0` nas mensagens, mensagem de negação "Acesso nao autorizado"/3 s (`DevicePump.cs:547`). Constantes do laço: timeouts por estado (`DeviceStateMachine.cs:207-221`), relógio 30 s/1 h/1 min/5 min, 2 min de reconexão, 60 s de liberação manual.

### 3.4 Defeitos latentes que a frente A precisa corrigir antes de expor parâmetros

1. **Dupla inversão de sentido.** `DevicePump.Liberar` troca para `GateDirection.Saida` quando `SentidoInvertido` (`DevicePump.cs:524-526`), e o adapter, com o mesmo perfil, troca de novo para a variante invertida (`TopdataInnerAdapter.cs:240-250`) → chamaria `LiberarCatracaSaidaInvertida`. O teste do adapter (`tests/HardwareInLoop/AdapterTests.cs:227-241`) espera `LiberarCatracaEntradaInvertida` chamando `LiberarGiro(Entrada)` direto, por isso não pega. Hoje dormente (`SentidoInvertido` é sempre `false`). Correção: o pump sempre pede `Entrada`; a inversão fica **só** no adapter. Teste de ponta a ponta (pump + CosturaFalsa ou simulador que registre a variante).
2. **Dígitos variáveis não enviados** (3.3).
3. **Três envios da mesma configuração completa** por conexão, e EI-029 nunca chamado (1.2). Antes de mudar, A_CONFIRMAR_COM_TOPDATA qual é a sequência real do manual 2.1.2 para "offline → mudança automática → online".
4. **Eventos que não são leitura vão para a decisão.** Em `Polling`, qualquer origem que não seja 5/6 dispara `EventoRecebido` → `ValidarAcesso` (`DevicePump.cs:448-455`). Origem 7 (cartão recolhido), 20 (urna cheia), 8–10 (sensores), teclas… sem credencial viram "leitura sem credencial" → negação → **"Acesso nao autorizado" no display** e rearme do leitor. Com urna/sensores ligados, isso mostra negação indevida para quem está na frente. Precisa de classificação de origem (leitura × sinal da catraca) antes de ligar relé 2/sensores.
5. **Origem 21 (QR) não é leitor conhecido na decisão** (`DecisorDeIngresso.cs:95-97` só aceita Leitor1/Leitor2); com provedor `urn_only` ela nega, o que é correto, mas relatórios "por origem" não a distinguem. O simulador só produz origens 2/3 (`src/Simulator/ConducaoDeLeituras.cs:77-79`).
6. `GravarConfiguracaoResponse.exige_reinicio` com texto antigo (`edge_control.proto:206-207`).

---

## 4. Lacunas por frente

Regra do dono: **DESIGN NÃO PODE QUEBRAR OPERAÇÃO.** Em cada item, o risco à operação vem explícito. Princípios já no código que valem para tudo abaixo: o worker é thread única e só chama a DLL pelo `DevicePump`, uma chamada por passo, com comandos só em `Polling` (`DevicePump.cs:285-298`); serviço e worker só conversam pela base (ADR-0024); configuração ruim **não para catraca**, sobe com o padrão e avisa (`Edge.Worker.X86/Program.cs:236-242`); falha de base nega, nunca libera (`DecisorDeIngresso.cs:110-115`); o que não foi ensaiado nasce desligado (ADR-0010).

### 4.A Módulo catraca completo (funções + parametrização)

| Lacuna | Onde entra | Contrato novo | Dependências | Risco de quebrar operação |
|---|---|---|---|---|
| A0. Corrigir defeitos latentes 3.4 (sentido, origens que não são leitura, dígitos variáveis) | `Edge.Worker/DevicePump.cs` (`Liberar`, `Aguardar`), `Access.Domain/Devices/EventOrigin.cs` (classificar "é leitura"), `Topdata.EasyInner.Adapter`, `IEasyInnerNative` + `EasyInnerReal` + `tests/HardwareInLoop/CosturaFalsa.cs` (EI-012) | nenhum | EI-012 e a sequência 2.1.2: A_CONFIRMAR_COM_TOPDATA / HIL-CARD-02 | **Alto** se feito sem teste: mexe no caminho do giro. Dígitos variáveis muda o que a catraca aceita ler → só com bancada |
| A1. Configuração **por catraca** (hoje só global) | Nova migração (próximo número livre; ver etapa A.2) (tabela tipada STRICT, 1 linha por `inner_number`, colunas anuláveis = "herda do evento"; + histórico só-INSERT com gatilhos como a 009). Repositório em `Access.Infrastructure.SQLite` (ao lado de `ConfiguracoesDaBorda`). Montador puro `DeviceConfiguration` ← (padrão de fábrica + evento + catraca) em `Access.Application/Devices` substituindo `ConfiguracaoDeBancada.TopFit4` + `ConfiguracaoDasCatracas` (`Program.cs:198-203`) | Proto: `ObterConfiguracaoDaCatraca`, `GravarConfiguracaoDaCatraca` (campos: tipo de leitor, leitor 1/2, urna, tempo de acionamento, sentido invertido, mensagem padrão própria, teclado…) | ADR-0020 (sempre completa), ADR-0010 (o que não é homologado fica travado) | Médio: configuração inválida por catraca precisa cair no padrão **daquela** catraca, com aviso, sem derrubar as outras do worker |
| A2. Worker aplica por catraca | `SessaoDeOperacao` (ctor recebe `Func<int, DeviceConfiguration>`/dicionário em vez de uma só, `SessaoDeOperacao.cs:81,126`); `Recarregar` por Inner (`Program.cs:260-267`). `DeviceSlot.Configuracao` já é por slot (`DevicePump.cs:28`) | "Aplicar agora" por catraca já existe no proto (`inner != 0`) | A1 | Baixo: reaproveita o caminho de reconexão já testado; a catraca fica segundos sem atender (já avisado) |
| A3. Auditoria de configuração | Hoje só `edge_setting.updated_by` da última mudança. Histórico só-INSERT (gatilhos sem UPDATE/DELETE) para evento e catraca; opcional hash da `DeviceConfiguration` aplicada gravado em `device_status` (desejada × aplicada) | `ListarHistoricoDeConfiguracao` (opcional) | — | Nenhum no giro |
| A4. Novos comandos documentados, **desligados por padrão** | `TipoDeComando` (`ComandoDeCatraca.cs:8-24`, validação em `Criar`), `DevicePump.Executar` (`:559-619`), `ITopdataInnerAdapter` (+ métodos), `TopdataInnerAdapter`, `InnerSimulator`/`SimulatedDevice` (registrar), `EdgeControlService.Tipo()` (`:615-633`), `Textos.NomeDoComando`, `GerenciarCatracaViewModel.AguardandoConfirmacao` (`GerenciarCatraca.cs:153-159`). **`operator_command.kind` não tem CHECK → tipo novo não exige migração** (status tem) | Proto: novos valores em `TipoDeComando` (6, 7, …, nunca reaproveitar). Candidatos: bip curto/longo (EI-048/049, nativas já declaradas), liberar saída, liberar dois sentidos (evacuação; motivo obrigatório e talvez duas pessoas — docs/05 §7 `approval_request`), coletar bilhetes, entrar/sair de manutenção, habilitar/desabilitar catraca | Chave técnica por função em `edge_setting` (padrão do `relogio.acertar_ao_divergir`), linha de bancada no docs/21 para cada uma, A_CONFIRMAR_COM_TOPDATA (bip na Linha 4, relés, dois sentidos, sentido do giro) | Médio: função nova só roda em `Polling`, com validade; **dois sentidos permite carona** (docs/32 §5) — exige decisão B4 (fail-safe) |
| A5. Coleta de bilhetes off-line | Gatilho `IniciarColetaDeBilhetes` (hoje nunca disparado), `DevicePump.ColetarBilhete` precisa **entregar** o `Bilhete` a quem grava e só avançar depois do commit (R-68, CHAOS-REC-01); migração para a tabela de bilhetes coletados | `ColetarBilhetes` como comando e/ou automático na volta do off-line (`OfflineAutonomo → ColetarBilhetes` já está na tabela) | Só faz sentido com `MudancaAutomatica ≠ 0` (hoje 0) → depende de E3/B4 | **Alto**: `ColetarBilhete` apaga da memória da catraca; perder o commit perde o evento |
| A6. Urna recolhendo o cartão (relé 2 + origem 7, docs/04) | Novos estados/gatilhos na `DeviceStateMachine` (tabela é dado; testes `Todo_estado_operacional_e_alcancavel`, `Tabela_nao_tem_transicao_ambigua` vão exigir cobertura), `FuncaoDoAcionamento2`/`TempoDoAcionamento2` por catraca, `AcionarReleDaUrna` (já existe no adapter), reserva do ingresso até origem 7 | — | **Inteiramente A_CONFIRMAR_COM_TOPDATA** (função do relé 2, `EngolirCartao`/`DevolverCartao` existem na DLL sem semântica para a TopFit 4; HIL-URNA-01) | **Muito alto**: muda o caminho do giro do cartão da bilheteria. Só atrás de chave desligada, depois da bancada |
| A7. Leitura de volta da configuração (drift, ADR-0020 item 5) | `ReceberConfiguracoesInner` (só gerada) → adapter → comparação no pump fora do caminho do giro | campo "configuração conferida/divergente" em `Equipamento` | Formato do buffer: A_CONFIRMAR_COM_TOPDATA | Baixo se só informa |
| A8. Mensagens de negação por motivo, `exibirData`, mensagem off-line | `DevicePump.ExibirNegado` usa texto fixo (`:547`); motivo já existe na `Decision` | campos na configuração | acentos no display: A_CONFIRMAR (docs/21 6B-2) | Baixo |
| A9. Ping on-line e mudança automática (T2) | `Ping` do adapter nunca chamado; `MudancaAutomatica=2` exige `PingOnLine` periódico (validação `DeviceConfiguration.cs:166-170`); lista local (`SincronizandoDadosOffline` vazio) | — | Decisão E3/B4, tamanho da lista 15.000 (docs/15 §2) | **Alto**: muda o que a catraca faz sem o PC |

### 4.B Cadastro de cartões e importação (fase 3 — não existe)

| Lacuna | Onde entra | Contrato novo | Dependências | Risco |
|---|---|---|---|---|
| B0. **Decisão de dono**: fonte da verdade de cada campo entre cadastro local e `middleware-sync-cards` (ADR-0023: "cadastro é da nuvem") | ADR novo (ex.: ADR-0025) + docs/26 §3 | — | dono do produto | Se errar: a sincronização sobrescreve o cadastro local, ou o contrário. Hoje `Gravar` faz upsert por `(provider_id, external_ref)` e `ux_ticket_qr` é **global** (`003:47`) — um código importado localmente que também vem da nuvem colide |
| B1. Tipos de entrada (código, nome exibido, ordem, cor, ativo) | Migração nova: tabela `entry_type` STRICT (código em MAIÚSCULAS, CHECKs), sem mexer em `ticket` (o código do tipo = texto de `ticket.category`, compatível com o que já vem da nuvem em `admission_type`). Repositório + validação no domínio | `ListarTiposDeEntrada`, `GravarTipoDeEntrada` | — | Baixo |
| B2. Tipo inativo nega | Condição extra no **único** `UPDATE` de `ConsumirUmUso` (`RepositorioDeIngressos.cs:961-1002`) + `MotivoDoUso.TipoInativo` + `ReasonCodes` + `DecisorDeIngresso.CodigoPara` + `AcompanhamentoDaOperacao.MensagemPara` + `Textos` | valor novo de motivo (texto) | B1 | **Médio**: é o caminho de decisão; precisa manter a atomicidade (uma instrução) e índice; categoria sem linha em `entry_type` continua valendo |
| B3. Leitura e validação da planilha (docs/26) | **Projeto novo e puro** (ex.: `Access.Importacao`, `net10.0`, só BCL: `System.IO.Compression` + `System.Xml` para `.xlsx`; CSV `;` UTF-8). Nenhum pacote novo (NuGetAudit quebra o build). Produz `PreviaDaImportacao` (novos / alterados / iguais / erros por linha / duplicados no arquivo / "mesmo código com zeros a mais ou a menos"), recusando célula numérica (`t="n"`/sem `t`), notação científica, espaço/ponto/traço, 4–16 caracteres, datas `dd/mm/aaaa hh:mm` Brasília (`Access.Domain/Tempo/HoraDeBrasilia.cs`) | — | `ArquiteturaTests` precisa de regra para o projeto novo (não referenciar Sync nem WPF) | Nenhum no giro (roda no serviço) |
| B4. Aplicação atômica e desfazer | Migrações: `import_batch` (id, nome do arquivo, sha256, quem, quando, contagens, aplicado/desfeito) e itens com o estado anterior de cada cartão; gatilhos sem DELETE. Repositório no SQLite | `ConfirmarImportacao(id, operador)`, `DesfazerImportacao(id, operador)` (recusa se algum cartão foi usado depois — docs/26 §3.7) | B0, B1 | **ALTO**: `busy_timeout = 5000` (`SqliteConnectionFactory.cs:56`); uma transação longa de 100 mil linhas segura a escrita da base e **trava o `TentarUsar` do worker até 5 s, parando todas as catracas do worker** (laço de thread única), e depois disso a decisão **nega** por `FalhaNaBaseLocal`. Mitigação: preparar tudo fora da transação (tabela de preparo em lotes pequenos) e deixar a transação final curta e medida; teste de "catraca decide durante importação" |
| B5. Transporte do arquivo | O painel não acessa a base (teste `A_interface_grafica_so_conhece_os_contratos`); o arquivo precisa ir ao serviço. gRPC tem limite padrão de 4 MB por mensagem (não há `MaxReceiveMessageSize` no código) → 100 mil linhas passam disso. Opções: *client streaming* em pedaços, ou subir o limite só para esse RPC. Caminho de arquivo é frágil (serviço roda como LocalSystem, sem as unidades mapeadas do usuário) | `PreverImportacao(stream PedacoDoArquivo) returns PreviaDaImportacao` | — | Nenhum no giro. **Atenção ao teste de contrato**: o `.proto` não pode conter `cartao`, `card` nem `credencial =` (nem em comentário). Usar "código", "tipo de entrada", "cadastro". A prévia devolve só códigos mascarados |
| B6. Cadastro de um código pela tela | RPC `GravarCodigo` (campo `codigo` como em `ConsultarCodigoRequest`), bloqueio/desbloqueio, leitura pelo leitor do balcão (A_CONFIRMAR: qual leitor/teclado-wedge) | idem | B0, B1 | Baixo |
| B7. Telas | `Desktop.ViewModels`: `TiposDeEntradaViewModel`, `CadastroViewModel`/`ImportacaoViewModel` (prévia, erros, confirmar, desfazer); `Desktop.App/Telas/*.xaml` + `DataTemplate` em `App.xaml:166-177` + entrada em `JanelaViewModel.Telas` (`Telas.cs:910-923`). Diálogo de arquivo só na View | — | B3–B6 | Nenhum |
| B8. Normalização na leitura por catraca/leitor | `DecisorDeIngresso.Decidir` (`:88`) recebe o perfil do leitor daquela catraca (vem da configuração por catraca, A1) | — | Bancada docs/21 linhas 8–12 (12/14 dígitos × "ABA 10") | **Alto** se escolhido errado: nenhum cartão casa. Manter `raw` até a bancada |
| B9. Titular e LGPD | Coluna/tabela de titular cifrada (ADR-0014), retenção e expurgo | — | Base legal (docs/29 §5) | — (PROPOSTA; não coletar nada além do docs/26) |
| B10. Relatórios por tipo na ordem cadastrada | `ObterPrestacaoDeContas` ordena por `entry_type.sort_order` e usa o nome exibido | campos novos em `LinhaPorCategoria` | B1 | Nenhum |

### 4.C Gêmeo digital acompanhando as novas funções

| Lacuna | Onde entra | Contrato novo | Risco |
|---|---|---|---|
| C1. Origem da leitura ao vivo | `ticket_use_attempt` não guarda a origem → migração com `reader_origin INTEGER NULL`; `IValidadorDeIngressos.TentarUsar` passa a origem bruta (hoje só `KnownEventOrigin?` Leitor1/2); `AcompanhamentoDaOperacao.Converter` preenche `origem_bruta`/`origem_conhecida` (`AcompanhamentoDaOperacao.cs:61-74`). `TraducaoAoVivo.Leitor` já sabe usar 2/3/21 | nenhum campo novo (já existem 4–6 em `EventoDeAcesso`) | Baixo: coluna nova anulável na mesma transação da tentativa |
| C2. Giro que chega depois da decisão | `AcompanhamentoDaOperacao` também lê `passage_confirmed_at` novos (cursor próprio + índice) e difunde um segundo `EventoDeAcesso` com o mesmo `evento_id` e `passagem_confirmada=true` (proposta do docs/33 §7); `TraducaoAoVivo` trata "confirmação" como só `Giro` | nenhum campo novo | Baixo |
| C3. Comandos do operador no desenho | Tradutor puro novo (ex.: `TraducaoDeComandos`) de `ComandoRegistrado` → sinais da cena (`MostrarMensagem` para mensagem temporária; `Liberado`+`Giro` para liberação manual "girou"; `PerdeuComunicacao`/`Conectou` para reconexão/aplicar); o VM já atualiza periodicamente (`JanelaViewModel.AtualizarAsync` inclui o gêmeo) e pode chamar `ListarComandos(inner)` | nenhum (ou um fluxo `AcompanharComandos` se o polling não bastar) | Nenhum: o gêmeo continua só observando |
| C4. Estado técnico da máquina | `Equipamento.estado` já vem (`edge_control.proto:93`); mapear para a cena (Reconectar, Manutenção, Quarentena, ColetarBilhetes, Firmware incompatível) | — | Nenhum |
| C5. Fichas das peças conforme a catraca | `CatalogoDaFit4` (`Pecas.cs:110`) deixa de ser estático: função pura `(configuração da catraca, funções habilitadas) → fichas` (urna desligada = "Não usada aqui"; sentido invertido; bip habilitado; urna recolhendo só se a chave estiver ligada) | consome A1/A4 (`ObterConfiguracaoDaCatraca`, funções habilitadas) | Nenhum |
| C6. Cenários novos | `Roteiros` (`Roteiros.cs`): bip, urna recolhendo (origem 7), urna cheia com esvaziamento, liberação de saída/evacuação, manutenção, coleta de bilhetes; **com selo "Aguardando confirmação" enquanto a função estiver desligada** (mesma regra do docs/32 §5) | — | Nenhum |
| C7. Desenho | `GeometriaFit4` já tem `UrnaDeposito`, sentido de giro com sinal (`CenaDaCatraca.Sinal`), display 2×16; só precisa dos sinais novos na cena | — | Nenhum; capturas no CI (`CapturaDeTela.GravarGemeoAsync`) |

---

## 5. Padrões do código que a implementação deve seguir

**Build e CI**
- `Directory.Build.props`: `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`, `EnforceCodeStyleInBuild`, `Nullable`, `InvariantGlobalization`, `NuGetAudit` com `NU1901–1904` como erro → **não adicionar pacote NuGet** sem necessidade (docs/33 §1 recusou pacote 3D por isso; leitura de `.xlsx` deve ser BCL).
- `.github/workflows/ci.yml`: job **Linux** compila e roda **todos** os testes (inclusive `tests/HardwareInLoop`, que usa `CosturaFalsa`, sem DLL); WPF compila no Linux por `EnableWindowsTargeting` (`Desktop.App.csproj`) mas não executa. Job **Windows** roda os testes de novo e analisa os `.ps1`. Job **instalador** publica, sobe o serviço em simulação, roda `--autoteste` do painel e do assistente e tira **capturas de todas as telas** (`--capturar`, temas claro e escuro) como artefato `capturas-das-telas`; confere worker 32 bits e o conteúdo do MSI. Tela nova só aparece nas capturas se estiver em `JanelaViewModel.Telas`.
- Regras de arquitetura por teste (`tests/Contract/ArquiteturaTests.cs`): só o worker referencia o interop (`:54`); domínio não referencia nada (`:86`); a interface só conhece `Contracts` (`:124`); **ViewModels só conhecem `Contracts` e `Shared.Observability`** (`:164`); decisão (`Access.Domain`, `Access.Application`) não alcança `Sync.*` (`:194`); nenhum projeto de produção referencia teste (`:288`).

**Contrato gRPC** (`src/Contracts/Protos/edge_control.proto`, testes `tests/Contract/ContratoIpcTests.cs`)
- Pacote versionado `conexaotopdata.edge.v1`; número de campo nunca reaproveitado (removido vira `reserved`); sem número repetido.
- **Todo enum com `*_NAO_ESPECIFICADO = 0`** (ex.: `TIPO_DE_COMANDO_NAO_ESPECIFICADO = 0`, `:321`); o C# mapeia o 0 para "desconhecido/recusa" (`EdgeControlService.Tipo`, `:615-623`).
- Palavras proibidas no arquivo inteiro: `cartao`, `card`, `credencial =`, `senha`, `password`, `template`, `foto`, `image`. Credencial só como `credencial_mascarada`/`codigo_mascarado`; entrada digitada como `codigo`.
- Toda resposta de erro volta como `repeated string problemas` + `bool aceito/gravada`, nunca exceção; a tela usa `TelaBase.Tentar` (falha de comunicação vira mensagem, `Telas.cs:46-66`).

**Base local**
- Migrações SQL puras, numeradas `NNN_nome_em_portugues.sql`, embutidas (`Access.Infrastructure.SQLite.csproj`: `EmbeddedResource Migrations/*.sql`), aplicadas pelo serviço antes dos workers (`Edge.Supervisor/Program.cs:99`); **migração publicada nunca é editada** (comentário da `005_cartao_so_na_urna.sql:4-5`). Próximo número livre: **010**.
- Tabelas `STRICT`, ids `TEXT` UUIDv7 (`Guid.CreateVersion7(agora)`), tempo `TEXT` ISO-8601 UTC (`ToString("O")`), epoch inteiro quando precisa comparar (004 `last_used_epoch`), credencial sempre `TEXT`, `CHECK` para enumerações de status.
- Auditoria por gatilho: `operator_command_nao_se_apaga`, `operator_command_pedido_nao_muda`, `operator_command_final_e_final` (009); `trg_audit_log_sem_update/_sem_delete` (001). Quem digitou fica como texto ("não há login", docs/27 §11).
- Conexões por `SqliteConnectionFactory.Abrir()` (WAL, `foreign_keys`, `synchronous=FULL`, `busy_timeout=5000`, sem pool). Decisão = uma instrução `UPDATE … WHERE` (vencedor único). Toda tentativa vira linha, na mesma transação da outbox.
- Worker nunca para por base ocupada: grava depois (`SessaoDeOperacao.GravarDesfechos`, `PublicarSeFor`).

**Worker / DLL**
- Uma thread; um passo do `DevicePump` = no máximo uma chamada bloqueante (`DevicePump.cs:146-153`); comandos e relógio **só em `Polling`**; máquina de estados como **tabela de dados** (`DeviceStateMachine.BuildTable`), `TryFire` nunca lança; falha incrementa disjuntor e backoff com jitter (`Falhar`, `:772-778`).
- Adapter: toda chamada via `Medir` com `AdapterResult` (retorno bruto preservado, ADR-0018); validação **antes** de qualquer chamada nativa (`EnviarConfiguracaoCompleta`, `TopdataInnerAdapter.cs:140-147`); o que o manual não diz fica com comentário `A_CONFIRMAR` e o id do ensaio (HIL-…, INT-…).
- Toda função nativa nova entra em `IEasyInnerNative` + `EasyInnerReal` + `CosturaFalsa` (`tests/HardwareInLoop/CosturaFalsa.cs`) + `InnerSimulator`/`SimulatedDevice` + linha em `docs/compatibility-matrix/funcoes-easyinner.csv` (id `EI-xxx`, selo, teste).
- Registro: `Action<string> registrar` → `RegistroEmArquivo` (arquivo diário) + console; `LogEstruturado` com `RedatorDeDadoSensivel` no laço (`DeviceGroupLoop.cs:105-112`); código sempre por `CredentialValue.Mascarar`; nome e motivo digitados **não** vão para o registro do worker (`DevicePump.cs:561-563`). Onde houver `ILogger` (serviço hospedado), usar `LoggerMessage.Define` com `EventId` nomeado (`Edge.Supervisor/ImpedirSuspensao.cs:57-60`).
- Relógio sempre injetado (`Func<DateTimeOffset>`, `TimeProvider`); exibição em Brasília (`HoraDeBrasilia`, `FusoDoEvento`).

**Nomes, comentários, textos**
- Código novo em português (`SessaoDeOperacao`, `FilaDeComandosSqlite`, `ConfiguracoesDaBorda`, `GerenciarCatracaViewModel`); o núcleo da fase 1 tem nomes em inglês (`DevicePump`, `DeviceStateMachine`, `Decision`) — ao mexer, manter o idioma do arquivo.
- Comentário XML diz **por quê**, com referência a docs/ADR e ao risco (ex.: `DevicePump.cs:244`, `RepositorioDeIngressos.cs:990-992`). Texto de tela em português de portaria (`Desktop.ViewModels/Textos.cs`), sem jargão do SDK; o que não existe aparece **desabilitado com o selo "Aguardando confirmação" e o motivo** (`GerenciarCatraca.cs:153-159`, `CatalogoDaFit4`).
- Chave técnica em `edge_setting` para comportamento ainda não confirmado, desligada por padrão e sem tela (`relogio.acertar_ao_divergir`, `ConfiguracoesDaBorda.cs:77`).

**Testes** (xUnit; nomes em frase com `_`, CA1707 liberado em `Directory.Build.targets`)
- `tests/Unit`: domínio, máquina de estados (`Devices/DeviceStateMachineTests.cs`: `Tabela_nao_tem_transicao_ambigua`, `Todo_estado_operacional_e_alcancavel`), configuração (`ConfiguracaoDoEquipamentoTests.cs`), decisor com dublê (`Ingressos/DecisorDeIngressoTests.cs`), ViewModels do gêmeo (`GemeoDigital/*`), sincronização.
- `tests/Integration`: `BancoTemporario` (SQLite real descartável), `InnerSimulator` com `SimulatedDevice.Roteirizar`, serviço real atrás do pipe (`TelasTests`, `ServicoDaOperacaoTests`), ponta a ponta serviço+base+worker (`GerenciarCatracaTests`), laço (`ComandosDaCatracaTests`, `RelogioDaCatracaTests`), `LigacoesDasTelasTests` (toda ligação XAML aponta para propriedade existente; toda VM tem `DataTemplate`).
- `tests/HardwareInLoop/AdapterTests.cs`: tradução do adapter contra `CosturaFalsa` (sequência exata de chamadas nativas).
- `tests/Contract`: arquitetura, proto, origens (`origens-evento.csv` × enum), limites de capacidade.
- `tests/LoadAndSoak`: soak curto no CI; `tests/CrashProbe`: `kill -9` sem duplicar.
- Dados de teste sempre sintéticos (`9999…`); nunca números reais (repositório público, docs/24).

---

## 6. Fatiamento proposto (etapas pequenas, cada uma compilável e com o teste que prova)

Ordem pensada para: (1) nada de hardware não confirmado ligado por padrão; (2) o caminho do giro só mudar com teste de ponta a ponta; (3) worker de thread única e serviço↔worker pela base (ADR-0024). Cada etapa = um PR com CI verde.

### Etapa 0 — Endurecer o que existe (sem função nova)
| # | Mudança | Teste que prova |
|---|---|---|
| 0.1 | Pump sempre pede `GateDirection.Entrada`; inversão só no adapter (`DevicePump.cs:524-526`) | Integração com simulador: `SentidoInvertido=true` gera pedido `Entrada` (via `SimulatedDevice.LiberacoesPedidas`); e teste pump+`TopdataInnerAdapter`+`CosturaFalsa` que termina em `LiberarCatracaEntradaInvertida` |
| 0.2 | `EventOrigin` ganha "é leitura" (1, 2, 3, 21); em `Polling`, sinal da catraca (7, 8–10, 20, teclas) vai para `aoReceberEvento` e fica em `Polling` sem decidir | `DevicePump`/simulador: origem 20 em `Polling` não exibe "Acesso nao autorizado", não cria tentativa e não chama `ConfigurarEntradasOnline`; origem 21 com código decide. Novo teste em `DeviceStateMachineTests` se entrar gatilho novo |
| 0.3 | `ticket_use_attempt.reader_origin` (migração 010) e `IValidadorDeIngressos` recebendo a origem bruta; `AcompanhamentoDaOperacao` preenche `origem_bruta`/`origem_conhecida` | Integração: tentativa pela urna chega ao `AcompanharEventos` com origem 3; unit `TraducaoAoVivo` já cobre 2/3/21 → base da frente C |
| 0.4 | Texto de `exige_reinicio` e a mensagem da tela (só contrato/comentário) | `TelasTests.Configuracoes_carrega_valida_e_grava` ajustado |

### Etapa A — Catraca
| # | Mudança | Teste que prova |
|---|---|---|
| A.1 | Montador puro da `DeviceConfiguration` (fábrica + evento + catraca) substituindo `ConfiguracaoDeBancada`+`ConfiguracaoDasCatracas` com **o mesmo resultado de hoje** | Unit: para a configuração atual, montador == `ConfiguracaoDeBancada.TopFit4(...) with {...}` campo a campo; teste por reflexão de que **todo** campo de `DeviceConfiguration` é setado (ADR-0020 item 3) |
| A.2 | Migração `011_configuracao_da_catraca.sql` (tabela por `inner_number` + histórico só-INSERT com gatilhos) + repositório | Integração `BancoTemporario`: ausente = herda o evento; gravar/ler; histórico não aceita UPDATE/DELETE; valor inválido volta como problema, não exceção |
| A.3 | Worker usa por catraca (`SessaoDeOperacao` com configuração por Inner; `Recarregar` por Inner) | `ComandosDaCatracaTests`: aplicar na catraca 2 muda só a 2 (`SimulatedDevice.ConfiguracoesRecebidas`); inválida na 2 → comando `Falhou` e a 1 continua em `Polling`; worker sobe com padrão se a linha estiver ilegível |
| A.4 | Proto `ObterConfiguracaoDaCatraca`/`GravarConfiguracaoDaCatraca` + serviço | `ContratoIpcTests` (enums/palavras), `ServicoDaOperacaoTests`/`GerenciarCatracaTests` (gravar + aplicar só naquela) |
| A.5 | Tela: parâmetros da catraca em Gerenciar catraca (modo técnico), campos não confirmados desabilitados com selo | `TelasTests` (só habilita com nome; problemas mostrados), `LigacoesDasTelasTests`, captura no CI |
| A.6 | Enviar dígitos variáveis (EI-012) — **atrás de chave desligada** até HIL-CARD-02 | `AdapterTests`: com a chave, uma chamada `InserirQuantidadeDigitoVariavel` por tamanho antes de `EnviarConfiguracoes`; sem a chave, sequência idêntica à de hoje |
| A.7 | Comando bip curto/longo (EI-048/049) atrás de chave `funcao.bip` | `AdapterTests` (chamada exata), `ComandosDaCatracaTests` (só em `Polling`, validade, desfecho), serviço recusa com a chave desligada; linha nova no docs/21 6B |
| A.8 | Liberar saída / dois sentidos (evacuação) atrás de chave, motivo obrigatório | Simulador registra `Saida`/`DoisSentidos`; `GerenciarCatracaTests` de auditoria; nada disso roda sem a chave. Depende de B4 do cliente |
| A.9 | Coleta de bilhetes com gravação antes do próximo passo (migração própria) | Teste de queda entre coleta e commit (padrão `QuedaAbruptaTests`/CrashProbe): nenhum bilhete perdido nem duplicado |
| A.10 | Urna (relé 2 + origem 7) — só depois da bancada HIL-URNA-01 | Tabela da máquina com estados novos + `Todo_estado_operacional_e_alcancavel`; cenários do docs/04 (desistência, recolhimento órfão) no simulador |

### Etapa B — Cadastro e importação
| # | Mudança | Teste que prova |
|---|---|---|
| B.0 | ADR da fonte da verdade (local × nuvem) e do provedor dos cartões locais | — (documento; bloqueia B.4+) |
| B.1 | Migração `entry_type` + repositório + RPCs de tipos | Integração: CHECK de código (MAIÚSCULAS, sem espaço/acento), ordem, ativo; contrato |
| B.2 | Tipo inativo nega no `UPDATE` único | `BilheteriaLocalTests`/`CicloDoIngressoTests`: tipo inativo → `TipoInativo`; categoria sem tipo cadastrado continua liberando; corrida de duas catracas continua com um vencedor |
| B.3 | Projeto puro de leitura/validação (CSV e XLSX por BCL) | Unit com arquivos **gerados no teste**: zeros à esquerda preservados, célula numérica recusada, notação científica, duplicado no arquivo (erro nas duas linhas), tipo inexistente, datas, 4–16 caracteres, aviso de "mesmo código com zeros a mais"; 100 mil linhas com tempo medido |
| B.4 | Migrações `import_batch` + itens; aplicar atômico curto; desfazer | Integração: tudo ou nada; desfazer volta ao estado anterior; desfazer recusado se usado depois; **worker decide durante uma importação de 100 mil** (latência medida, nenhuma negativa por `FalhaNaBaseLocal`) |
| B.5 | RPC com *client streaming* (`PreverImportacao`), `ConfirmarImportacao`, `DesfazerImportacao`, `GravarCodigo` | `ContratoIpcTests` (sem `cartao`/`card`), serviço devolve só mascarado |
| B.6 | Telas de tipos, cadastro e importação | `TelasTests` (prévia, confirmar exige nome, desfazer), `LigacoesDasTelasTests`, capturas |
| B.7 | Relatório por tipo na ordem/nome cadastrados | `PainelTests`/prestação: soma por tipo contra SQL independente |
| B.8 | Perfil de normalização por leitor na decisão — **só depois da bancada** | `DecisorDeIngressoTests` com perfil por catraca |

### Etapa C — Gêmeo
| # | Mudança | Teste que prova |
|---|---|---|
| C.1 | (depende de 0.3) leitura ao vivo mostra QR/cartão/urna | `GemeoDigitalTests` (tradução) + `TelasTests.Gemeo_digital_…` |
| C.2 | Segundo evento de confirmação de giro no serviço; cena aplica só o giro | Integração serviço (dois eventos, mesmo `evento_id`); `CenaDaCatracaTests` (giro depois de "liberada" não duplica, fila de giros) |
| C.3 | Tradutor de comandos → cena (mensagem temporária, liberação manual, reconexão) | Unit do tradutor; `TelasTests` com comando concluído aparecendo no desenho ao vivo |
| C.4 | Estado técnico da máquina na cena | Unit (mapa estado → cena, todos os `DeviceState` cobertos) |
| C.5 | `CatalogoDaFit4` em função da configuração da catraca e das chaves | Unit: urna desligada = "Não usada aqui"; chave desligada = "Aguardando confirmação"; nada "Disponível" que o serviço não execute |
| C.6 | Cenários novos (bip, urna recolhendo/cheia, saída, manutenção, coleta) | `GemeoDigitalTests`: todos terminam com a catraca livre; códigos de teste existem no `simulacao.exemplo.json`; capturas no CI |

### Dependências externas que não se resolvem em código
- **A_CONFIRMAR_COM_TOPDATA**: função do relé 2 / `EngolirCartao`/`DevolverCartao` na TopFit 4; bip na Linha 4; relés avulsos; sequência 2.1.2 (três envios × EI-029); `InserirQuantidadeDigitoVariavel` na Linha 4; `ReceberDadosOnLine` × `_QRCodeComLetras` para QR com letras; sinalização de "sem evento" (origem 0); sentido do giro distinguível (lotação); formato de `ReceberConfiguracoesInner`; acentos no display; acertar relógio com a catraca em uso.
- **Cliente/dono**: E2 (parque, urna e QR em todas?), E3/B4 (fail-safe × fail-secure, evacuação), E5 (dígitos do cartão), fonte da verdade do cadastro (B.0), LGPD do titular.
