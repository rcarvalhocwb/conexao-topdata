> Relatório bruto de um especialista da auditoria linha a linha (docs/40), commit auditado 29df501.
> Os estados aqui são os do especialista, antes da revisão cruzada; o consolidado é o docs/41.

E6, interface e operação. Commit 29df501, somente leitura. Não editei nenhum arquivo e não rodei build nem testes. Usei Read, Grep, cat/sed/grep via Bash e as linhas conferidas em tests/, installer/ e Edge.Supervisor para validar textos.

Resumo: achei 2 ALTO e 7 MÉDIO, sem CRÍTICO. Nenhum Binding de XAML aponta para propriedade inexistente. O defeito mais sério é o "Aplicar agora nas catracas", cujo segundo passo pode ficar desabilitado para sempre. O outro ALTO é a falta de prazo nas chamadas: um serviço travado aparece como "Operacional".

## A) Achados

**E6-1 | ALTO (aguarda revisão cruzada E9 e prova em Windows)**
- **Onde:** `src/Desktop.ViewModels/Telas.cs:870-898`, `ConfiguracoesViewModel.ConfirmandoAplicacao`, `PrepararAplicacao`, `AplicarAgora` e `CancelarAplicacao`. Tela em `src/Desktop.App/Telas/Configuracoes.xaml:38-51`.
- **O que o código faz:**
  - O setter (`Telas.cs:891-897`) só chama `Avisar(TextoDaConfirmacaoDaAplicacao)`. Ele não reavalia os três comandos.
  - `ComandoAssincrono` só dispara `CanExecuteChanged` em si mesmo (`Infra.cs:62-73,76`), sem `CommandManager.RequerySuggested`.
  - Os botões "Confirmar a aplicação" e "Cancelar" existem desde o início dentro de uma Border colapsada. Começam com CanExecute falso.
  - Depois de "Aplicar agora nas catracas…", só o próprio PrepararAplicacao é renotificado.
  - Compare com `GerenciarCatraca.cs:118-122`, `Parametrizacao.cs:707-710` e `MapaDeGiro.cs:438-441`, que chamam `Reavaliar()`.
- **Por que é problema:** o painel de confirmação abre com "Confirmar" e "Cancelar" desabilitados. Depois de um envio aceito, "Aplicar agora…" também fica desabilitado. A única saída é sair da tela e voltar, o que recria a view, ou aplicar catraca por catraca. A capacidade "aplicar configuração em todas" fica inoperante pela tela, sem pista para o operador.
- **Como provar:**
  - O teste `tests/Integration/TelasTests.cs:747` chama `AplicarAgora.ExecutarAsync()` direto, que reavalia `CanExecute` na hora. Por isso passa com o defeito.
  - Nenhum teste do repositório assina `CanExecuteChanged` (grep retornou 0).
  - Falta um teste que assine o `CanExecuteChanged` de `AplicarAgora` e espere evento após `PrepararAplicacao`.
- **Estado:** CONFIRMADO no código. O efeito na tela é NÃO VERIFICÁVEL AQUI: depende de o `ButtonBase` do WPF só reconsultar `CanExecute` quando recebe `CanExecuteChanged`.
- **Correção mínima:** no setter, chamar `PrepararAplicacao/AplicarAgora/CancelarAplicacao.ReavaliarDisponibilidade()`, e acrescentar o teste.

**E6-2 | ALTO (hipótese quanto à ocorrência; revisão E2)**
- **Onde:** `src/Contracts/TransporteLocal.cs:89-119`, só com prazo de conexão. Nenhum `Deadline` em Desktop.ViewModels ou Desktop.App (grep). Também `JanelaPrincipal.xaml.cs:19,79` e `TelaBase.Tentar` em `Telas.cs:46-65`.
- **O que o código faz:**
  - As chamadas gRPC do painel não têm prazo.
  - O `DispatcherTimer` de 2 s dispara um `async` Tick sem esperar o anterior.
  - Se o serviço aceita a conexão mas não responde (handler travado, base ocupada), `Tentar` nunca volta.
  - Enquanto isso, `ServicoResumo` continua "Operacional" verde e os números ficam congelados.
  - O ramo `DeadlineExceeded` de `MensagemDeFalha.cs:36` nunca é exercido.
  - A cada 2 s uma nova chamada se acumula.
- **Por que é problema:** é exatamente a falha que o operador precisa ver e não vê. O único indício é a hora no topo (`Telas.cs:181`) parar.
- **Como provar:** servidor de teste que segura a chamada; observar que `Estado` não muda. Não existe teste assim.
- **Estado:** fato de código CONFIRMADO. O impacto é HIPÓTESE (depende de o serviço travar).
- **Correção mínima:** prazo por chamada (por exemplo, 5 s nas leituras) e não iniciar uma atualização com outra em curso.

**E6-3 | MÉDIO**
- **Onde:** `Telas.cs:223-228` (`PainelAoVivoViewModel.AtualizarAsync`, ramo de falha), `PainelAoVivo.xaml:53-55` e `Catracas.xaml:21-23`.
- **O que o código faz:**
  - Na falha, só `Estado`, `ServicoResumo` e `ServicoSinal` mudam.
  - Continuam no estado anterior: `CatracasResumo` ("2/2 online", verde), `NuvemResumo`, os cartões de catraca com "Atendendo" verde e os contadores.
  - Com o serviço fora desde a partida, a lista vazia mostra "Nenhuma catraca configurada — … Assistente (passo 2)".
- **Por que é problema:** sinais verdes falsos, e o próximo passo indicado está errado (o assistente não resolve um serviço parado). Atenuante: o cabeçalho diz "mostrando dados de X atrás" (`EstadoDoPainel.cs:156`).
- **Como provar:** um teste com o canal derrubado após uma resposta, verificando que `CatracasSinal` continua `Bom`.
- **Estado:** CONFIRMADO no código.
- **Correção mínima:** na falha, marcar catracas e nuvem como Neutro/"desconhecido". O estado vazio deve depender de o serviço ter respondido.

**E6-4 | MÉDIO**
- **Onde:** `GerenciarCatraca.cs:91` ("Refazer conexão", `TemCatracaEOperador`). Tela em `GerenciarCatraca.xaml:112-113` e `CentralDaCatraca.xaml:115-117`.
- **O que o código faz:** "Refazer conexão" é um clique só. O efeito é o mesmo de "Aplicar" (a catraca reconecta e fica alguns segundos sem atender), e "Aplicar" exige dois passos.
- **Por que é problema:** um clique no pico de entrada derruba uma catraca. É inconsistente com a regra de confirmar ações perigosas.
- **Como provar:** ler o código; não há teste que exija confirmação.
- **Estado:** CONFIRMADO.
- **Correção mínima:** dois passos, como na liberação manual.

**E6-5 | MÉDIO**
- **Onde:** `src/Edge.Configurador/JanelaDoAssistente.xaml.cs:285-326` (`Gravar` e `ReiniciarServico`) e `JanelaDoAssistente.xaml:139-140`.
- **O que o código faz:** "Gravar e iniciar o serviço" para o serviço se ele estiver rodando e o inicia de novo, sem confirmação. O texto do botão não diz "reiniciar".
- **Por que é problema:** feito durante o evento, todas as catracas ficam sem atendimento do sistema por até 60 s.
- **Estado:** CONFIRMADO.
- **Correção mínima:** com o serviço rodando, confirmar com "as catracas ficam sem atender por alguns segundos".

**E6-6 | MÉDIO**
- **Onde:** texto em `GerenciarCatraca.xaml:66` e confirmação em `GerenciarCatraca.cs:127-129`.
- **O que o código faz:**
  - A tela afirma "Libera um giro no sentido de entrada", e a confirmação não diz o sentido.
  - O worker resolve a função pelo mapa de giro da origem LiberacaoManual (`src/Edge.Worker/DevicePump.cs:791`).
  - O mapa permite Saída e as invertidas (`MapaDeGiro.cs:37-42,63`).
  - O mesmo texto aparece na ficha da peça (`Pecas.cs:165`) e no roteiro (`Roteiros.cs:130`).
- **Por que é problema:** o operador confirma uma "entrada" e o braço pode liberar para o outro lado.
- **Estado:** CONFIRMADO, com base na leitura do DevicePump.
- **Correção mínima:** mostrar na confirmação a função efetiva do mapa para LiberacaoManual.

**E6-7 | MÉDIO (HIPÓTESE no lado do repositório; revisão E3/E1)**
- **Onde:** `MapaDeGiro.cs:595-636` (`ConferirAsync`) e servidor em `src/Edge.Supervisor/EdgeControlService.MapaDeGiro.cs:191-221`.
- **O que o código faz:**
  - A tela só recusa se a linha foi alterada e não salva (`MapaDeGiro.cs:604-608`). A mensagem diz "Salve e aplique".
  - Não verifica `VersaoAplicada == VersaoSalva`, nem se há aplicação em curso.
  - O handler do servidor também não verifica. Não li o repositório `RegistrarConferencia`.
- **Por que é problema:** se o operador salvou mas não aplicou, gira (pela função antiga) e marca "girou para o lado da seta", fica o selo verde "Sentido conferido" para uma função que a catraca nunca usou (capacidade 5).
- **Estado:** HIPÓTESE.
- **Correção mínima:** bloquear a conferência enquanto a situação não for "Aplicada".

**E6-8 | MÉDIO (HIPÓTESE, NÃO VERIFICÁVEL AQUI)**
- **Onde:** `GerenciarCatraca.xaml:20-21` e `Parametrizacao.xaml:45-46` (`ComboBox SelectedValue={Binding Catraca}`). `Catracas` é trocada a cada atualização (`GerenciarCatraca.cs:219`, periódica por `Telas.cs:1387`).
- **O que o código faz:** o próprio código registra esse defeito ("seletor vazio, borda vermelha") e o corrige só no gêmeo (`GemeoDigitalViewModel.cs:647-649`).
- **Por que é problema:** na tela de liberação manual o seletor pode aparecer vazio enquanto `Catraca` ainda aponta para uma catraca. O nome na confirmação atenua o risco.
- **Correção mínima:** repetir `Avisar(nameof(Catraca))` depois de trocar `Catracas`, como no gêmeo.

**E6-9 | MÉDIO (HIPÓTESE)**
- **Onde:**
  - `Tentar` só captura `RpcException` (`Telas.cs:56`).
  - `async void` em `Infra.cs:52,102` e `JanelaPrincipal.xaml.cs:79`.
  - Disparos sem `await` em `Telas.cs:1374`, `Parametrizacao.cs:589,1063` e `GemeoDigitalViewModel.cs:207`.
  - Tratadores globais em `App.xaml.cs:116-148`.
- **O que o código faz:** qualquer outra exceção vira MessageBox modal com o tipo e a mensagem técnicos. Com o timer de 2 s, os MessageBoxes podem se empilhar. Já as falhas das tarefas disparadas sem `await` vão só para arquivo (`UnobservedTaskException`), sem nada na tela.
- **Correção mínima:** em `Tentar`, capturar `Exception` (exceto cancelamento) e transformar em mensagem; observar as tarefas soltas.

**BAIXO e INFORMATIVO (uma linha cada)**
- **E6-10 BAIXO.** Termos técnicos visíveis ao operador:
  - `Telas.cs:710-718`: "Fase 6", "(E9)", "(E10)", "docs/25 §4", "SHA-256". Aparecem com o selo "Aguardando confirmação" em relatórios que simplesmente não existem (`Contas.xaml:127`).
  - `GerenciarCatraca.cs:207-211`: "INT-UX-03", "D5", "HIL-DIR-07", "docs/21 §8", "INT-REC-03, CHAOS-REC-01".
  - `Pecas.cs:174,258`: "(D5)", "Fase 5".
  - `MensagemDeFalha.cs:38`: código gRPC em inglês ("PermissionDenied", "Internal").
  - `Textos.cs:42`: estado cru da catraca como fallback.
  - `Configuracoes.xaml:23`: "8 na bancada; 5…".
  - `CentralDaCatraca.cs:40-41`: "NOVO-HIL-REL-04/06 … origem 5" (só no modo técnico).
- **E6-11 BAIXO.** `JanelaDoAssistente.xaml:120-121` diz que o segredo "nunca é gravado em arquivo". Mas `src/Edge.Supervisor/CofreDeSegredos.cs:68-70` grava arquivo cifrado com DPAPI de escopo LocalMachine. O texto é falso; o escopo vai para E8.
- **E6-12 BAIXO.** "O Windows tenta reiniciá-lo sozinho" (`MensagemDeFalha.cs:28-30`, `EstadoDoPainel.cs:157`) é verdade para queda (`installer/wix/ConexaoTopdata.wxs:145-149`). É falso depois de "Encerrar a operação" (`ControleDaOperacao.cs:199`): o operador espera em vez de usar "Iniciar a operação".
- **E6-13 BAIXO.** Código morto:
  - `GerenciamentoDeCatracasViewModel.cs:30-218` sem uso e sem teste. Não implementa `INotifyPropertyChanged` útil, mostra "Erro: {Message}" e o comando só faz `Debug.WriteLine`.
  - `PainelViewModel.cs` só é usado em `tests/Integration/PainelTests.cs:93`.
  - `Parametrizacao.cs:695-699,1046-1095` (Sugestões) sem tela e sem teste. Defeito latente: com `Herda=true`, `UsarSugestao` diz "Preenchido" mas `ValorNovo` continua nulo (`:299`), e registra "usada" antes de salvar.
- **E6-14 BAIXO.** Bandeja:
  - Só avisa quando a catraca sai de Bom para outro estado (`Bandeja.cs:72-77`). Catraca que nunca atendeu desde a abertura não avisa.
  - Não avisa relógio divergente, embora `Novidades.cs:45` prometa "aviso na hora".
  - Não avisa nuvem fora nem cartas mortas.
- **E6-15 BAIXO.** Acessibilidade:
  - `RayzerAlert` sem `LiveSetting` (`Themes/Generic.xaml:136-188`): erros e resultados não são anunciados.
  - `DataGridCell` sem indicador de foco (`Controles.xaml:679-690`).
  - `Estado.Mensagem` com Assertive muda a cada 2 s quando o serviço está fora ("dados de N s atrás"), interrompendo o leitor de tela.
  - O menu é reconstruído a cada mudança de `Estado` (`Telas.cs:1223-1235,1364`), possível perda de foco (NÃO VERIFICÁVEL AQUI).
  - No alto contraste o botão de tema não faz nada (`TemaRayzer.cs:138-141`).
- **E6-16 BAIXO.** `Tentar` aninhado: `Parametrizacao.cs:903` chama `CarregarSugestoesAsync`, que também usa `Tentar` (`:1023`), e zera `Ocupada` antes do fim.
- **E6-17 BAIXO.** Campos inteiros sem validação na tela (`Configuracoes.xaml:19,24,30`): texto inválido não chega à ViewModel, e Salvar responde "Nada mudou." (`Telas.cs:979`).
- **E6-18 BAIXO.** `tests/Integration/LigacoesDasTelasTests.cs:63-100` aceita o nome se ele existir em qualquer um dos tipos listados para o arquivo. Ignora bindings com `RelativeSource` e `ElementName`. Não cobre `JanelaDeNovidades.xaml`, o Configurador nem Rayzer.Design.
- **E6-19 INFORMATIVO.** Conferi todos os bindings dos 16 XAML de telas, da janela, do assistente e das novidades, inclusive os `RelativeSource` (`DataContext.PorQue.Abrir`, `Gerenciar`, `VerAcessos`, `AbrirNoGemeo`, `Parametrizar`, `PreVisualizar`, `Conferir*`, `RodarNaCatracaSimulada`) e as mensagens proto (`LinhaDeNegativa`, `ProvedorCadastrado`, `Diagnostico`/`DiagnosticoDeWorker`). Nenhum aponta para propriedade inexistente.
- **E6-20 INFORMATIVO.** Confirmei que estão corretos:
  - Liberação manual em dois passos, com reavaliação (`GerenciarCatraca.cs:70-124`).
  - "Aplicar nesta catraca" em Parametrização, Mapa e Gêmeo em dois passos, com reavaliação.
  - "Encerrar a operação" pede confirmação com Cancelar como padrão (`BandejaDoSistema.cs:110-126`).
  - Simulador oculto em modo real: `Telas.cs:1223-1235,1364`, servidor recusa (`EdgeControlService.cs:678`), "Na simulada" do gêmeo oculto (`Gemeo.xaml:289`, `GemeoDigitalViewModel.cs:813`).
  - `--capturar` e `--autoteste` vão no exe de produção (`App.xaml.cs:22-58`) e não gravam nada.

## B) Cobertura

Contagens aproximadas (funções públicas e com lógica, ou bindings).

| Arquivo | Linhas | Lido inteiro | Funções/bindings | Achados |
|---|---|---|---|---|
| ViewModels/Telas.cs | 1398 | sim | ~60 | E6-1,2,3,9,10,16,17 |
| ViewModels/Infra.cs | 132 | sim | 8 | E6-1,9 |
| ViewModels/MensagemDeFalha.cs | 39 | sim | 1 | E6-10,12 |
| ViewModels/PainelViewModel.cs | 98 | sim | 2 | E6-13 |
| ViewModels/EstadoDoPainel.cs | 175 | sim | 3 | E6-3,12,15 |
| ViewModels/GerenciarCatraca.cs | 292 | sim | 12 | E6-4,6,8,10 |
| ViewModels/CentralDaCatraca.cs | 638 | sim | ~25 | E6-10 |
| ViewModels/Parametrizacao.cs | 1096 | sim | ~40 | E6-13,16 |
| ViewModels/MapaDeGiro.cs | 637 | sim | ~25 | E6-6,7 |
| ViewModels/Textos.cs | 244 | sim | 8 | E6-10 |
| ViewModels/PorQue.cs | 112 | sim | 3 | 0 |
| ViewModels/Periodo.cs | 112 | sim | 6 | 0 |
| ViewModels/Bandeja.cs | 103 | sim | 3 | E6-14 |
| ViewModels/GerenciamentoDeCatracasViewModel.cs | 218 | sim | 6 | E6-13 |
| ViewModels/Novidades.cs | 56 | sim | 0 | E6-14 |
| ViewModels/NovidadesViewModel.cs | 92 | sim | 3 | 0 |
| GemeoDigital/GemeoDigitalViewModel.cs | 940 | sim | ~35 | E6-9 |
| GemeoDigital/CenaDaCatraca.cs | 458 | sim | 10 | 0 |
| GemeoDigital/ConfiguracaoPorPeca.cs | 141 | sim | 4 | 0 |
| GemeoDigital/Display2x16.cs | 73 | sim | 3 | 0 |
| GemeoDigital/Especificacao.cs | 336 | sim | 5 | 0 |
| GemeoDigital/GeometriaFit4.cs | 549 | sim | 6 | 0 |
| GemeoDigital/Malha.cs | 430 | sim | 15 | 0 |
| GemeoDigital/Pecas.cs | 283 | sim | 3 | E6-6,10 |
| GemeoDigital/Roteiros.cs | 217 | sim | 3 | E6-6 |
| GemeoDigital/TraducaoAoVivo.cs | 82 | sim | 3 | 0 |
| GemeoDigital/Vistas.cs | 30 | sim | 0 | 0 |
| App/App.xaml + .cs | 209+264 | sim | ~25 bindings, 5 funções | E6-9,20 |
| App/BandejaDoSistema.cs | 164 | sim | 5 | E6-20 |
| App/CapturaDeTela.cs | 592 | sim | 12 | 0 |
| App/Conversores.cs | 165 | sim | 9 | 0 |
| App/InstanciaUnica.cs | 58 | sim | 3 | 0 |
| App/JanelaDeNovidades.xaml + .cs | 60+50 | sim | 4 bindings, 2 funções | 0 |
| App/JanelaPrincipal.xaml + .cs | 173+224 | sim | ~15 bindings, 10 funções | E6-2,15 |
| App/Portugues.cs | 54 | sim | 2 | 0 |
| Telas/Acessos.xaml + .cs | 91+9 | sim | ~25 | 0 |
| Telas/CampoDaCatraca.xaml | 57 | sim | ~14 | 0 |
| Telas/Catracas.xaml + .cs | 26+9 | sim | 4 | E6-3 |
| Telas/CentralDaCatraca.xaml + .cs | 126+9 | sim | ~30 | E6-4 |
| Telas/Configuracoes.xaml + .cs | 69+9 | sim | ~14 | E6-1,17 |
| Telas/Consulta.xaml + .cs | 60+17 | sim | ~10 | 0 |
| Telas/Contas.xaml + .cs | 139+33 | sim | ~30 | E6-10 |
| Telas/Diagnostico.xaml + .cs | 88+44 | sim | ~15 | 0 |
| Telas/Gemeo.xaml + .cs | 381+1059 | sim | ~55 bindings, ~35 funções | 0 |
| Telas/GerenciarCatraca.xaml + .cs | 164+9 | sim | ~25 | E6-4,6,8 |
| Telas/MapaDeGiro.xaml + .cs | 150+12 | sim | ~35 | E6-7 |
| Telas/PainelAoVivo.xaml + .cs | 105+9 | sim | ~25 | E6-3 |
| Telas/Parametrizacao.xaml + .cs | 176+9 | sim | ~30 | E6-8 |
| Telas/Simulador.xaml + .cs | 74+9 | sim | ~12 | 0 |
| Telas/Sincronizacao.xaml + .cs | 51+9 | sim | ~10 | 0 |
| Configurador/App.xaml + .cs | 27+114 | sim | 3 | 0 |
| Configurador/ControleDaOperacao.cs | 125 | sim | 7 | E6-12 |
| Configurador/JanelaDoAssistente.xaml + .cs | 158+327 | sim | ~12 funções, 6 bindings | E6-5,11 |
| Rayzer.Design/Componentes.cs | 603 | **não**: 1-90 e 470-603 lidos; 90-470 varridos (só propriedades de dependência) | ~20 | E6-15 |
| Rayzer.Design/TemaRayzer.cs | 194 | sim | 8 | E6-15 |
| Rayzer.Design/Themes/Generic.xaml | 745 | **não**: 1-200 lidos; resto varrido por foco e automação | — | E6-15 |
| Rayzer.Design/Controles.xaml | 895 | **não**: 1-130, 280-300 e 676-692 lidos; resto varrido | — | E6-15 |
| Rayzer.Design/Tokens.xaml | 209 | **não**: varrido por grep | — | 0 |
| Rayzer.Design/Temas/Claro, Escuro, AltoContraste.xaml | 131, 132, 127 | **não**: não lidos (só contagem) | — | 0 |

Pendência de cobertura: os XAML de estilo e tema do Rayzer.Design e o trecho 90-470 de `Componentes.cs` não foram lidos linha a linha. Priorizei telas e ViewModels; o risco funcional nesses arquivos é só visual.

## C) Capacidades (docs/40 §7)

**12. Liberação manual, mensagem no display, relógio, aplicar configuração, com auditoria (parte tela): PARCIAL.**
- **Liberação manual em dois passos: PROVADO EM CI na ViewModel.**
  - `TelasTests.cs:685` afirma `LiberarManualmente.CanExecute` falso antes de preparar. Falharia se o primeiro passo fosse removido.
  - `TelasTests.cs:660` falharia se a confirmação sobrevivesse à troca de motivo ou de catraca.
- **Aplicar por catraca: PROVADO EM CI na ViewModel** (`TelasTests.cs:826, 879, 936, 1477`).
- **Aplicar em todas as catracas:** a ViewModel passa (`:747`), mas na tela está provavelmente quebrado (E6-1), e nenhum teste falharia.
- **Mensagem, relógio e refazer:** o comando e o histórico com operador existem (`GerenciarCatraca.cs:253-291`), sem prova na tela. "Refazer conexão" não pede confirmação (E6-4).
- **Hardware:** nada provado.

**14. Painel do operador (telas, textos verdadeiros, erros com próximo passo, confirmação em ação perigosa): PARCIAL.**
- **O que existe:**
  - As 12 telas, com nomes de binding conferidos por `LigacoesDasTelasTests`. A prova é fraca (E6-18).
  - O autoteste WPF no CI Windows percorre todas as telas nos dois temas (`.github/workflows/ci.yml:156-187`). Pega exceção de renderização, mas não CanExecute nem binding errado em silêncio.
- **O que falta:**
  - Serviço travado aparece como operacional (E6-2).
  - Sinais verdes ficam velhos quando o serviço cai (E6-3).
  - Textos falsos ou técnicos (E6-6, 10, 11, 12).
  - Ações perigosas sem confirmação (E6-4, E6-5).

**15. Alertas ao operador: PARCIAL.**
- Há balão da bandeja quando uma catraca sai de "Atendendo", quando o serviço some e quando volta (`Bandeja.cs:42-88`). `TelasTests.cs:772` falharia se a lógica de transição quebrasse.
- O painel parte com o Windows na bandeja (`installer/wix/ConexaoTopdata.wxs:209`).
- O que falta está em E6-14: catraca que nunca atendeu, relógio divergente, nuvem e cartas mortas, além de alertas sem a sessão do operador aberta.
- A exibição do balão é NÃO VERIFICÁVEL AQUI.

## D) Perguntas de decisão

1. **Confirmação em "Refazer conexão" e em "Gravar e iniciar o serviço" do assistente:** (a) dois passos nas duas **[recomendada]**; (b) só no assistente; (c) manter. Motivo: as duas derrubam atendimento, e "Aplicar", que tem o mesmo efeito, já exige dois passos.
2. **Prazo por chamada do painel ao serviço:** (a) 5 s nas leituras e 15 s nos comandos, sem atualização sobreposta **[recomendada]**; (b) prazo único de 30 s; (c) sem prazo, como hoje. Motivo: sem prazo, serviço travado aparece como "Operacional" (E6-2).
3. **Termos técnicos e "pendências de projeto" nas telas do operador (Fase 6, D5, HIL-, INT-, docs/):** (a) tirar das telas de operação e manter só em Diagnóstico e no modo técnico, trocando "Aguardando confirmação" por "Ainda não existe" nos relatórios **[recomendada]**; (b) manter o texto e esconder os códigos; (c) manter. Motivo: o operador não sabe agir sobre "HIL-DIR-07", e o selo atual dá a entender que os relatórios R1–R8 existem e só falta confirmar.
