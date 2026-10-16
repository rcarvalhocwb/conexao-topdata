# Fluxo da catraca em modo online: hardware, relés e sequência

Documento de referência para a catraca em produção (TopFit 4, linha 19, firmware 6.42.1, urna na
entrada). Compara o que o **middleware que operou em 2025** fazia com o que o sistema faz hoje, e
lista as diferenças que dependem de decisão ou de bancada.

Fontes: middleware antigo (`Middleware.App/Inner/TopdataEasyInnerDeviceClient.cs`,
`EasyInnerManager.cs`, `EasyInnerWrapper.cs`); Manual de Desenvolvimento EasyInner (seção 5.3, fluxo 8);
exemplo do SDK 6.0.2.0; `docs/61-urna-leitores-e-teclado.md`.

## 1. Inventário de hardware e funções

| Elemento | Função na DLL | Como o sistema usa | Fonte |
|---|---|---|---|
| Porta TCP da catraca | `DefinirTipoConexao(2)`, `FecharPortaComunicacao`, `AbrirPortaComunicacao(porta)` | Abre uma vez por processo; fecha a anterior antes | Antigo: `EasyInnerManager`; atual: `TopdataInnerAdapter.AbrirPorta` |
| Teste de conexão | `PingOnLine(inner)` | Conectar e keep-alive a cada 3 s | Antigo: `StateTestConnectionAsync`; manual, passos 11–12 |
| Leitor 1 (frente) | `ConfigurarLeitor1` = 1 (somente entrada) | Leitura que libera direto | Antigo linha 303; atual `LeitorSomenteEntrada` |
| Leitor 2 (fenda da urna) | `ConfigurarLeitor2` = 1 (somente entrada) | Leitura que abre a urna | Antigo linha 310; atual igual |
| Relé 1 (passagem) | `ConfigurarAcionamento1(função, tempo)` | Janela para completar o giro | **Diferença**, ver §4 |
| Relé 2 (urna) | `ConfigurarAcionamento2(3, 15)` + `AcionarRele2` | Abre a guia da urna | Antigo linhas 330–331 e 1163; atual igual, sem `DesabilitarRele2` (D5) |
| Liberação de giro | `LiberarCatracaEntrada` | Libera a passagem depois da urna | Antigo linha 585; atual padrão `FuncaoDeLiberacao.Entrada` |
| Forma de entrada on-line | `EnviarFormasEntradasOnLine` = 6 (L1+L2, sem teclado) | Quem pode passar | `docs/61` §3 |
| Mensagens no display | `EnviarMensagemPadraoOnLine` / `Temporaria` | "DEPOSITE O CARTAO", "ACESSO LIBERADO", "Modo Online" | Antigo linhas 570, 686, 737, 830 |
| Eventos (origens) | `ReceberDadosOnLine` | 2 = leitor 1, 3 = leitor 2, 5 = fim do tempo, 6 = giro, 7 = cartão recolhido na urna, 20 = urna cheia | Manual §4.3.2 e §5.3.1 |
| WebServer da catraca | — | Não é acionado pelo sistema; fica indisponível on-line | `docs/34-anexos/01-engenheiro-topdata.md` |

## 2. Sequência do middleware antigo (urna na entrada)

1. **Cartão no leitor 2.** Recebe a origem 3. Se já há um cartão pendente, ignora o novo.
2. **`DesabilitarRele2`** (garante a guia fechada), estado `WaitingAuthorization`, evento `CardRead`.
3. **Autorização.** `GrantAccessAsync` com `WaitingAuthorization`: `AcionarRele2` (abre a guia).
   Falhou: limpa o cartão pendente e volta a ler.
4. **`ValidatingUrnaCheia`.** Uma leitura de eventos: se vier a origem 20, a urna está cheia.
5. **`SendingMsgUrna`.** `EnviarMensagemPadraoOnLine("DEPOSITE O CARTAO")`, com início da espera.
6. **`MonitoringUrna`.** Espera a origem 7 (cartão recolhido) pelo tempo da espera de depósito.
   - Origem 7: mensagem "ACESSO LIBERADO" + tipo de cartão; **`DesabilitarRele2`**; **`LiberarCatracaEntrada`**.
   - Origem 5 (não caiu) ou 20 (cheia): mensagem de negação, **`DesabilitarRele2`**, devolve o uso.
   - Evento 7 chegado cedo: fica registrado (latch) e é consumido ao entrar em `MonitoringUrna`.
7. **`MonitoringGiro`.** Espera a origem 6 (giro).
   - Origem 6: **`DesabilitarRele2`** (de novo) e rearme (`ConfigurarEntradasOnline`).
   - Origem 5 (sem giro no prazo): **`DesabilitarRele2`**, mensagem "Modo Online", rearme.
8. Volta a `Polling`.

Pontos de cuidado do antigo: `DesabilitarRele2` aparece em todas as saídas da urna, além da
limpeza no início de cada leitura. Ele foi usado em produção em 2025 com a urna na entrada.

## 3. Sequência atual do sistema

1. Leitura no leitor 2 (`EventoNaUrna`, sem `DesabilitarRele2`).
2. Primeira passagem em `AbrirUrna`: mensagem "DEPOSITE O CARTAO" (`ExibirMensagemTemporaria`).
3. Segunda passagem: `AcionarRele2`. Estado `AcionarUrna` → `MonitoraUrna`.
4. Espera da origem 7 pelo tempo de depósito (7 s). Origem 5 e 20 negam e devolvem o uso.
5. Origem 7: `LiberarCatraca` (`LiberarCatracaEntrada` no padrão).
6. `MonitoraGiroCatraca`: origem 6 confirma; origem 5 desiste.
7. Rearme: `ConfigurarEntradasOnline` e volta a `Polling`.
8. A guia fecha pelo tempo do relé 2 (15 s), sem comando de fechamento.

## 4. Diferenças e decisões

| # | Diferença | Antigo | Atual | Risco | Decisão |
|---|---|---|---|---|---|
| D-A | Tempo e função do relé 1 | `ConfigurarAcionamento1(1, 15)` | `(2, 5)` | O prazo do giro é o tempo do relé 1 + 3 s (`DeviceStateMachine.cs:170`): 8 s hoje contra 18 s no antigo. Pode não dar tempo de completar a passagem | **Alinhar com o antigo (1, 15)?** Mudança de parâmetro de hardware: depende da sua decisão |
| D-B | Ordem na urna | Relé abre **antes** da mensagem | Mensagem **antes** do relé | Pequeno: o display mostra a mensagem um passo antes | Manter ou seguir o antigo |
| D-C | `DesabilitarRele2` | Chamado em todas as saídas da urna | Removido (D5, manual à risca) | A guia fecha pelo tempo; o próximo cartão pode esperar até 15 s | Medir na bancada (U8). O manual proíbe o comando |
| D-D | `AcionarRele2` | Chamado após autorização | Chamado após a mensagem | Mesmo comando proibido pelo manual (§5.3) | Manter, até a Topdata confirmar (ver `docs/61` §4) |
| D-E | Checagem de urna cheia | Leitura de eventos em `ValidatingUrnaCheia` | Origem 20 em `MonitoraUrna` | Equivalente em estado normal | Nenhuma |
| D-F | Espera do depósito | Tempo do monitoramento do antigo | 7 s (`EsperaPeloDeposito`) | Pode ser curto ou longo para quem deposita | Confirmar na bancada (U1) |

## 5. Fluxo exato proposto (depende das decisões de §4)

Quando D-A, D-B e D-C forem decididas, o fluxo fica assim:

1. Leitura no leitor 2 → guarda o cartão → mensagem "DEPOSITE O CARTAO".
2. Abre o relé 2 (`AcionarRele2`, tempo 15 s configurado).
3. Espera a origem 7 por 7 s. Sem ela: mensagem de negação, devolve o uso.
4. Origem 7: mensagem "ACESSO LIBERADO" e `LiberarCatracaEntrada`.
5. Espera a origem 6 pelo tempo do relé 1 (D-A). Sem giro: mensagem "Modo Online", devolve o uso.
6. Rearme dos leitores e volta a `Polling`.

## 6. Testes de bancada

- **U1:** a guia abre com (3, 15 s) e o cartão cai? (decidido que a função 3 é a da urna; falta a
  medição de tempo.)
- **U8:** depois do giro, o próximo cartão é lido em quanto tempo sem `DesabilitarRele2`?
- **U-A:** com relé 1 em 5 s e em 15 s, quantas pessoas completam o giro?
- **U-D:** o `AcionarRele2` abre a urna na catraca de produção (já em uso no antigo).

## 7. Atualização após o manual Rev. 17

Ver `docs/64-manual-catracas4-rev17.md`. Decisões confirmadas pelo dono:

- **Urna na entrada** é a configuração de produção. O manual descreve a urna na saída como padrão, mas
  permite a configuração de entrada pelo leitor 2.
- **O prazo do giro é definido pelo operador.** O padrão do manual é 5 s, e a faixa é de 1 a 255 s no Web
  Server. O SDK aceita de 0 a 50 s.
- **D-A (relé 1, 5 s ou 15 s) deixa de ser uma correção.** Passa a ser um parâmetro do operador. O
  prazo efetivo ainda soma a margem de 3 s do `MargemDoGiro`, que precisa de decisão (ver `docs/64`, §9).
