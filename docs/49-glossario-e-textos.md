# 49. Glossário e textos visíveis ao operador (E3)

**Status:** entregável E3 do docs/46, produzido pelo agente A3 (nomenclatura e textos). Branch `tarefa/prompt-analise-front-end`.
**Escopo:** só texto. Nenhum código foi alterado. Nenhum commit e nenhum push.
**Árvore de referência:** a árvore do docs/46 §6 é **proposta**. O A1 ainda propõe a árvore final no docs/48, que está pendente de aprovação do dono.

Classificação (docs/46 §7), aplicada a cada item:

- **APROVADO:** texto lido literalmente na fonte, verificado, e atende ao P2 (nome que o operador usa na portaria).
- **PENDENTE:** depende de execução visual ou de um dado que só aparece no Windows ou no build instalado.
- **BLOQUEADO:** depende de decisão do dono do produto. Está na seção 8, com identificador D-xx.
- **REPROVADO:** verificado na fonte e não atende ao P2 ou ao P1.

Nota de método: a aparência real das telas não foi executada (o projeto é WPF e o ambiente é Linux). Por isso a classificação "APROVADO" aqui vale para o **texto**, não para o layout. O layout fica com o A4 e o A5.

---

## 1. Resumo

- **Literais lidos:** cerca de 1.525 candidatos extraídos de `*.xaml` (717) e de `*.cs` (808) em `src/Desktop.App` e `src/Desktop.ViewModels`. Os que o operador vê foram separados à mão. Os demais são logs, nomes de método, identificadores e comentários.
- **Entradas da auditoria com termo técnico:** 144 linhas na seção 4. Algumas linhas agrupam vários literais do mesmo tipo; a contagem por classificação está na seção 4.6.
- **Nomes duplicados ou inconsistentes** (mesma função com nomes diferentes): 8 casos na seção 5. Incluem a configuração de uma catraca, que aparece com pelo menos 8 nomes diferentes.
- **Gêmeo:** 281 ocorrências em código, testes, instalador e ferramentas (fora de `bin/`, `obj/` e `docs/`). Nenhuma aparece em texto de tela. Só duas são texto de runtime, ambas na ferramenta de captura de telas (`src/Desktop.App/CapturaDeTela.cs:216` e `:392`). O resto é identificador, nome de arquivo ou comentário. A lista completa está na seção 7.
- **Gmail:** zero ocorrências em código-fonte, instalador, testes ou ferramentas. Há uma coincidência de bytes em quatro DLLs nativas do runtime .NET (`System.IO.Compression.Native.dll`), que é falso positivo.
- **Os três termos mais confusos para o operador:**
  1. **"giro"** (e "mapa de giro", "função", "sentido"). Aparece em 31 entradas da auditoria (mais de 100 vezes no total), e o operador não sabe se "liberado" e "passou" são a mesma coisa.
  2. **"Aplicar"** (como em "Aplicar nesta catraca"). Não diz que a configuração vai para a catraca. Ele se confunde com "Salvar".
  3. **"Serviço local"** (e "Sem resposta do serviço"). O operador não sabe o que é esse serviço nem o que fazer quando ele some.

---

## 2. Princípios aplicados

- **P1 (um lugar por função):** a seção 5 lista nomes repetidos. A configuração da catraca é o caso mais grave.
- **P2 (nome da portaria):** é o eixo deste documento. Cada termo técnico em texto visível aparece na seção 4 com a proposta.
- **P10 (ninguém precisa de manual):** vários textos de estado vazio ou de erro apontam para jargão ("Aguardando bancada", "Fase 6", "docs/25 §4") em vez de dizer o próximo passo. Estão marcados como REPROVADO.

---

## 3. Glossário (docs/46 §5 completado)

Colunas: termo técnico (como está no código ou no plano) · como o operador deve ler (proposta) · onde aparece hoje · onde fica (árvore proposta do docs/46 §6, ou do docs/48 quando já há proposta) · situação.

| Termo técnico | Como o operador lê (proposta) | Onde aparece hoje (arquivo:linha) | Onde fica (árvore proposta) | Situação |
|---|---|---|---|---|
| Gêmeo digital | Não aparece. Use o nome do item de menu da configuração da catraca | Só em `CapturaDeTela.cs:216`, `:392` (ferramenta) e em identificadores | — | REPROVADO (só na ferramenta de captura); ver seção 7 |
| Configuração da catraca (tela de desenho) | "Configuração da catraca" (um nome só) | `GemeoDigitalViewModel.cs:161`; `Gemeo.xaml:7`, `:28`; `CentralDaCatraca.cs:84` ("Configuração desta catraca") | CATRACAS › [catraca] › Configuração | BLOQUEADO (D-02) |
| Parametrização / Parametrização da catraca (lista) | "Configuração em lista" (modo da mesma tela, sem outro nome) | `Parametrizacao.cs:546`; `Parametrizacao.xaml:5`, `:26`; `GerenciarCatraca.xaml:43`, `:45` | Mesma tela de Configuração, vista lista | BLOQUEADO (D-02) |
| Configurar a catraca / Abrir no gêmeo / Ver em lista | Ações que levam à mesma configuração. Um só nome de ação | `GerenciarCatraca.xaml:39`; `Parametrizacao.xaml:30`; `Gemeo.xaml:145` | Uma ação por catraca | BLOQUEADO (D-02) |
| Giro / mapa de giro | "Sentido e contagem" (ou o que o dono escolher). No texto de ação, "passagem" | Dezenas de ocorrências: `MapaDeGiro.xaml`, `MapaDeGiro.cs`, `GerenciarCatraca.xaml:66-71`, `Configuracoes.xaml:18`, `Textos.cs` | CATRACAS › [catraca] › Configuração (aba) | BLOQUEADO (D-03); REPROVADO no uso atual |
| Função (da DLL) | "Lado que o braço libera" (já usado em `MapaDeGiro.xaml:48`, aprovado) | `MapaDeGiro.cs:39`, `:176`, `:200-208`, `:626-627`; `Pecas.cs:170` | Dentro da aba de giro | REPROVADO |
| DLL | Não deve aparecer. Nenhuma ocorrência em texto visível (só comentários de `MapaDeGiro.cs:15`) | — | — | APROVADO (ausente) |
| Sessão | "Você saiu. Entre de novo." | `Login.cs:183`; `MensagemDeFalha.cs:42`; `Usuarios.cs:272` | Barra superior (Usuário · Sair) | REPROVADO |
| Tentativa | "Acesso" (decisão: liberado ou negado) | `Acessos.xaml:8`; `Consulta.xaml:45`; `Simulador.xaml:48`; `Telas.cs:844`; `PorQue.cs:75-76`; `Textos.cs:39` | OPERAÇÃO › Acessos | REPROVADO |
| Passagem | "Passou" (giro confirmado pela catraca). Diferente de "Acesso" | `PainelAoVivo.xaml:24`; `Simulador.xaml:57`; `GemeoDigitalViewModel.cs:380`–`396`; `Pecas.cs:161`–`168` | OPERAÇÃO › Painel ao vivo | REPROVADO |
| Liberado / Autorizado / Permitido | "Liberado" (catraca deixou passar) e "Negado". Um só par | `PainelAoVivo.xaml:21`, `:31`; `Acessos.xaml:8`; `Contas.xaml:8`; `Telas.cs:490` | OPERAÇÃO › Acessos | REPROVADO |
| Cartões não reconhecidos / Leituras recusadas | "Leituras recusadas". Hoje não existe como tela; o que há é "Acessos negados" e "Usos sem passagem" | `PainelAoVivo.xaml:27` ("Acessos negados"); nome ausente no código (ver seção 10) | OPERAÇÃO › Leituras recusadas (docs/48 C2: aba ou item) | BLOQUEADO (docs/48 C2) |
| Lote de cartões | "Importação de cartões". Hoje só existe importação de pessoas | `Pessoas.xaml:231` ("Importar planilha"), `:243` ("Importações feitas"), `:254` ("Desfazer o lote escolhido") | GESTÃO › Cartões (docs/48 C6: fora do menu até existir) | BLOQUEADO (docs/48 C6) |
| Credencial / cartão / código | "Cartão ou código de acesso" (decisão de termo) | `Pessoas.xaml:28`, `:198-223`; `Pessoas.cs:191`, `:647-648`, `:786`, `:813` | GESTÃO › Pessoas › Cartões | BLOQUEADO (D-07) |
| Fila de envio / outbox / fila | "Aguardando envio" / "Pendências de envio" | `PainelAoVivo.xaml:33`, `:36` (ok); `Telas.cs:720` ("Mais antigo na fila"); `Textos.cs:126`; `GerenciarCatraca.cs:426`; `EstadoDoPainel.cs:132` ("outbox", sem binding) | INFRAESTRUTURA › Sincronização com a nuvem | REPROVADO (fila e outbox) |
| Ingresso | "Ingresso" (já é o nome usado pela portaria) | `Consulta.xaml:8`; `Acessos.xaml:55`; `GerenciarCatraca.xaml:66` | OPERAÇÃO › Consultar código | APROVADO |
| Relógio da catraca / hora da catraca | "Hora da catraca" | `App.xaml:134`, `:139`; `Textos.cs:80-94`, `:107`; `CentralDaCatraca.xaml:88`, `:114`; `GerenciarCatraca.xaml:33`, `:111`; `Bandeja.cs:63`; `Novidades.cs:45` | CATRACAS › [catraca] › Visão geral (com alerta de diferença) | REPROVADO |
| Firmware | "Versão do programa da catraca" (modo técnico) | `App.xaml:130`; `CentralDaCatraca.xaml:85`; `Textos.cs:229`; `Pecas.cs:272` | CATRACAS › [catraca] › Visão geral | REPROVADO |
| Serviço local | "Programa das catracas" (confirmar em D-11) | `JanelaPrincipal.xaml:128`, `:130`; `Bandeja.cs:32-33`, `:62-63`, `:98`; `EstadoDoPainel.cs:68`, `:96`, `:156-157`; `MensagemDeFalha.cs:25-30`, `:53`; `PorQue.cs:91-93`; `Textos.cs:193-205`; `Usuarios.xaml:8` | Barra superior (cartão de estado) | BLOQUEADO (D-11); REPROVADO no uso atual |
| Inner (número da catraca) | "Catraca 1", "Catraca 01" (o número, sem a palavra Inner) | `Textos.cs:197`; `ParametrosDoCadastro.xaml:135` ("lista off-line do Inner") | Em todo lugar, como "Catraca N" | REPROVADO |
| Relé / saída | "Saída 1", "Saída 2" (decisão de termo) | `CentralDaCatraca.xaml:70`; `CentralDaCatraca.cs:41`, `:190`; `Parametrizacao.cs:94`; `GerenciarCatraca.cs:343`; `Pecas.cs:221-222` | CATRACAS › [catraca] › Configuração | REPROVADO |
| Leitor / urna / leitor 1 e 2 | "Leitor da frente", "Leitor da urna" | `Parametrizacao.cs:79-87` ("Leitor da urna" e "Leitor da frente" aprovados, com códigos EI-014/015 proibidos) | CATRACAS › [catraca] › Configuração | REPROVADO (códigos internos no texto) |
| Wiegand, Abatrack, SmartCard, FC | Não deve aparecer no caminho principal. Só no modo técnico | `Parametrizacao.cs:66-71`, `:119-120` | Modo técnico (D-04) | REPROVADO |
| Display / visor | "Visor" (português). "Display" é inglês | `Gemeo.xaml:337-355`; `GerenciarCatraca.xaml:93-106`; `Textos.cs:108`; `MapaDeGiro.xaml:54`; `Acessos.xaml:114`; `PainelAoVivo.xaml:82`; `Pecas.cs:124-126`, `:182-202`; `Display2x16.cs:48-64`; `MapaDeGiro.cs:155` | Todas as telas com a mensagem | REPROVADO |
| Modo simulação / Simulador | Ver D-14. Hoje: "Simulação", "Modo simulação", "Simulador de catraca", "Simulador" | `JanelaPrincipal.xaml:60`, `:137`; `App.xaml:116-118`; `EstadoDoPainel.cs:113`; `Telas.cs:1229`; `Simulador.xaml:5-7` | FERRAMENTAS › Simulador (só com simulação ligada) | BLOQUEADO (D-14) |
| Modo técnico | Decisão do dono: o que sai da tela do operador (D-04) | `Gemeo.xaml:179-180`; `Parametrizacao.xaml:53-54`; `Parametrizacao.cs:379`, `:1054` | Fora do menu; suporte | BLOQUEADO (D-04) |
| Analisador / camada inteligente | Não deve aparecer ao operador. Vai para o suporte | `Diagnostico.xaml:31`, `:38`, `:42`; `Textos.cs:138-172` | INFRAESTRUTURA › Diagnóstico (suporte) | REPROVADO |
| Ciclo / orçamento / pulado / estouro | Não deve aparecer ao operador | `Textos.cs:150-170` | Suporte | REPROVADO |
| Quarentena / Morto / SemBatimento / Degradado / Parado | Frase de situação, sem o nome do estado | `Textos.cs:30-41`, `:42` (fallback mostra o nome cru) | CATRACAS › Catracas (situação) | REPROVADO |
| Pedido / comando | "Pedido" (ação enviada à catraca). Evitar "comando" | `GerenciarCatraca.cs:421`, `:428`; `Textos.cs:105-120`; `Novidades.cs:47` | CATRACAS › [catraca] › Gerenciar | REPROVADO ("comando") / APROVADO ("pedido") |
| Liberação manual | "Liberação manual" | `GerenciarCatraca.xaml:64`, `:70` | CATRACAS › [catraca] › Gerenciar | APROVADO |
| Mensagem padrão / temporária | "Mensagem padrão" / "Mensagem temporária" | `Pecas.cs:184-187`; `CentralDaCatraca.xaml:94` | CATRACAS › [catraca] › Configuração e Gerenciar | APROVADO (a palavra "temporária"); "pedido imediato" REPROVADO |
| Salvar | "Salvar" | `Configuracoes.xaml:37`; `Gemeo.xaml:206`; `Parametrizacao.xaml:128` | Mesma tela da ação | APROVADO |
| Aplicar (enviar a configuração salva à catraca) | Ver D-01 | Seção 4, T-139 | Mesma tela da ação | BLOQUEADO (D-01) |
| Espera pelo giro | "Tempo de espera pela passagem" | `Configuracoes.xaml:18`, `:28`, `:30`; `Parametrizacao.cs:93`; `GemeoDigitalViewModel.cs:730` | CATRACAS › Padrão de todas as catracas (docs/48 C5) | REPROVADO |
| Tempo de acionamento | "Tempo de liberação" | `Configuracoes.xaml:19`; `GemeoDigitalViewModel.cs:729` | Idem | REPROVADO |
| Evento (como "o evento", ou como "registro de acesso") | Usar "acesso" para registro e "evento" só para a festa | `App.xaml:127`; `Gemeo.xaml:256`; `Catracas.xaml:8`; `GemeoDigitalViewModel.cs:73`, `:859`; `Roteiros.cs:161` | Em todo lugar | REPROVADO |
| Origem (de ingresso ou de leitura) | Ver D-09 | `Telas.cs:624`; `Sincronizacao.xaml:45-57`; `MapaDeGiro.xaml:39`, `:84`; `MapaDeGiro.cs:60-64` | GESTÃO / INFRAESTRUTURA | BLOQUEADO (D-09) |
| Categoria | Ver D-10 | `Acessos.xaml:22-23`, `:62`, `:125`; `Contas.xaml:68-71`; | OPERAÇÃO › Acessos | BLOQUEADO (D-10) |
| Perfil / papel / função (de pessoa e de usuário) | Ver D-06: "Perfil" só para a pessoa; "Papel" para o usuário | `ParametrosDoCadastro.xaml:25-32`; `Pessoas.xaml:154`, `:161`; `Usuarios.xaml:53`, `:82-103`; `Usuarios.cs:168` | GESTÃO › Pessoas e ADMINISTRAÇÃO › Usuários e papéis | BLOQUEADO (D-06) |
| Estornar / estorno | "Desfazer" | `Acessos.xaml:68-83`; `UsosSemPassagem.cs:162`, `:193` | OPERAÇÃO › Acessos | REPROVADO |
| Uso sem passagem | "Ingressos usados sem entrada confirmada" (D-12) | `Acessos.xaml:50`, `:55`, `:57`; `UsosSemPassagem.cs:92`; `Sincronizacao.xaml:52` ("REUSO (S)") | OPERAÇÃO › Acessos (aba ou cartão, docs/48) | REPROVADO |
| Usos (contagem de ingresso) | "Vezes usado" | `Telas.cs:634-636` | OPERAÇÃO › Consultar código | REPROVADO |
| Borda | "Leitor" ou "entrada" | `GemeoDigitalViewModel.cs` (demonstração); `Roteiros.cs:52`, `:68`, `:118`; `Pecas.cs:198`; `UsosSemPassagem.cs:193` | Demonstração (Gêmeo) | REPROVADO |
| Base local / base | "Lista deste computador" (já usada em Painel) | `PainelAoVivo.xaml:22`, `:28`; `Roteiros.cs:52`; `PorQue.cs:76` | — | REPROVADO |
| Contingência | "Plano de emergência do evento" | `EstadoDoPainel.cs:90`; `Roteiros.cs:147` | Painel (estado) | REPROVADO |
| Corte (de contas) | "Fechar o dia" | `Telas.cs:821`, `:823` | GESTÃO › Prestação de contas | REPROVADO |
| SHA-256 / código de conferência | Não deve aparecer | `Telas.cs:823` | — | REPROVADO |
| Relatórios R1–R8 | Nomes em português, sem código | `Contas.xaml:8`; `Telas.cs:815-823` | GESTÃO › Prestação de contas (seção "Ainda não disponível") | REPROVADO |
| Prestação de contas | "Prestação de contas" (nome da tela, aprovado no plano) | `Telas.cs:757`; `Contas.xaml:7` | GESTÃO › Prestação de contas | APROVADO |
| Nuvem / sincronização | "Nuvem" e "Sincronização com a nuvem" | `JanelaPrincipal.xaml:134`; `Telas.cs:644`, `:660`; `Sincronizacao.xaml:7` | INFRAESTRUTURA › Sincronização com a nuvem | APROVADO |
| Segredo da nuvem / chave | "Chave de acesso à nuvem" | `Sincronizacao.xaml:40` | INFRAESTRUTURA › Sincronização | REPROVADO |
| Pacote de diagnóstico | "Arquivo para o suporte" | `Diagnostico.xaml:14-15`; `Telas.cs:1159`, `:1174` | INFRAESTRUTURA › Diagnóstico | REPROVADO |
| Tabela de horário / faixa | "Horário" (com as faixas em texto simples) | `ParametrosDoCadastro.xaml:71`, `:97-104`, `:111`, `:135`, `:147-149`; `ParametrosDoCadastro.cs:212`, `:378`, `:394`, `:527`; `Pessoas.xaml:71`, `:161`, `:164` | GESTÃO › Cadastro | REPROVADO |
| Retenção | "Guardar por (dias) depois de inativar" | `ParametrosDoCadastro.xaml:67`; `ParametrosDoCadastro.cs:431` | GESTÃO › Cadastro | REPROVADO |
| Pessoas / Ficha de pessoa | "Pessoas" e "Ficha" | `Pessoas.xaml:5-7`; `Pessoas.cs:349` | GESTÃO › Pessoas | APROVADO |
| Feriado | "Feriado" | `ParametrosDoCadastro.xaml:111` | GESTÃO › Cadastro | APROVADO |
| Empresa / sala / andar / bloco | "Empresa", "Sala", "Andar", "Bloco" | `ParametrosDoCadastro.xaml:158-228` | GESTÃO › Cadastro | APROVADO |
| Instalação | "Neste computador" ou "neste evento" (D-13) | `Telas.cs:445`, `:1013`, `:1110`; `Parametrizacao.cs:882`; `EstadoDoPainel.cs:102`; `Simulador.xaml:8`; `MapaDeGiro.cs:162-164`; `Pecas.cs:178-191` | Todas | BLOQUEADO (D-13) |
| Nome do produto (XAcess / Rayzer XAcess) | Um só nome (D-15) | `JanelaPrincipal.xaml:5`, `:43`; `Bandeja.cs:32`; `BandejaDoSistema.cs:54`; `App.xaml.cs:130` | Barra superior e bandeja | BLOQUEADO (D-15) |
| Operador (quem fez a ação) | "Seu nome" (hoje). docs/48 §9.8 propõe o login | `Configuracoes.xaml:33`; `GerenciarCatraca.xaml:24`; `MapaDeGiro.xaml:34`; `Parametrizacao.xaml:49` | Cada tela de ação | BLOQUEADO (docs/48 §9.8) |
| Aguardando confirmação | Ver D-05 | `CampoDaCatraca.xaml:48`; `CentralDaCatraca.xaml:71`; `GerenciarCatraca.xaml:150`, `:157`; `Pecas.cs:74`; `Parametrizacao.cs:380`, `:425`; `Contas.xaml:128` | Onde o recurso não está liberado | BLOQUEADO (D-05) |
| Ainda não disponível | "Em breve" (com motivo em uma frase, sem código) | `Contas.xaml:120`; `GerenciarCatraca.xaml:149` | Onde houver item sem função | BLOQUEADO (D-05) |
| Mensagem temporária, mensagem padrão (no visor) | "Mensagem no visor" | `GerenciarCatraca.xaml:93`; `Configuracoes.xaml:15` | CATRACAS › [catraca] › Gerenciar | REPROVADO ("display" no título) |
| Acesso / ponto de entrada | "Acesso" e "Entrada" | `Telas.cs:482`; `PainelAoVivo.xaml:59` | OPERAÇÃO › Acessos | APROVADO |


---

## 4. Auditoria: textos visíveis com termo técnico

Convenções: ID da linha · arquivo:linha · texto atual (entre aspas, quando é literal) · termo técnico · proposta em português de portaria · severidade (Alta: caminho principal ou mensagem que o operador vê com frequência; Média; Baixa) · situação.

### 4.1 Janela, estado, bandeja e mensagens de erro

| ID | Arquivo:linha | Texto atual | Termo técnico | Proposta | Sev. | Situação |
|---|---|---|---|---|---|---|
| T-001 | `JanelaPrincipal.xaml:128`, `:130` | "Serviço local" (cartão de estado) | serviço | "Programa das catracas" (D-11) | Alta | REPROVADO |
| T-002 | `App.xaml:127`; `Catracas.xaml:8`; `Gemeo.xaml:256`; `GemeoDigitalViewModel.cs:73`, `:859` | "Último evento:"; "Nenhum evento desde que a tela abriu." | evento (ambíguo: festa ou registro de acesso) | "Último acesso:"; "Nenhum acesso desde que a tela abriu." | Média | REPROVADO |
| T-003 | `App.xaml:130`–`134` | "Firmware", "Grupo", "Porta", "Reconexões", "Relógio" | firmware, grupo, porta, reconexões, relógio | "Versão do programa", "Quedas de conexão", "Hora da catraca"; "Grupo" e "Porta" saem para o modo técnico (D-04) | Alta | REPROVADO |
| T-004 | `App.xaml:139` | "Relógio da catraca fora do horário: os acessos saem com hora errada. Acerte em Gerenciar." | relógio; "Gerenciar" sem objeto | "A hora da catraca está errada: os acessos saem com a hora errada. Acerte em Gerenciar catraca." | Alta | REPROVADO |
| T-005 | `App.xaml.cs:127`–`130` (caixa de erro) | "{tipo da exceção}: {mensagem}" (texto de exceção) | exceção .NET crua, em inglês | Mostrar só "Aconteceu um erro inesperado. A tela continua aberta. Chame o suporte." e gravar o detalhe em arquivo | Alta | REPROVADO |
| T-006 | `JanelaPrincipal.xaml.cs:233` | "O assistente não foi encontrado em: {caminho}" | caminho técnico | "O Assistente de configuração não foi encontrado. Chame o suporte." | Baixa | REPROVADO |
| T-007 | `EstadoDoPainel.cs:96` | "Estado desconhecido informado pelo serviço" | estado; serviço | "Situação desconhecida. Chame o suporte." | Baixa | REPROVADO |
| T-008 | `EstadoDoPainel.cs:90`, `:94` | "Catraca sem comunicação com este computador — siga o procedimento de contingência do evento"; "Catracas isoladas — verifique a rede do local" | contingência; "isoladas" (ambíguo) | "Catraca sem conexão com este computador — siga o plano de emergência do evento."; "Catracas sem conexão com o computador — verifique a rede." | Média | REPROVADO |
| T-009 | `EstadoDoPainel.cs:102` | "Instalação ainda não configurada — abra o Assistente de configuração" | instalação | "Sistema ainda não configurado — abra o Assistente de configuração." (D-13) | Baixa | REPROVADO |
| T-010 | `EstadoDoPainel.cs:132` (campo `Detalhe`) | "versão {v} · nível {n} · outbox {x}" | outbox; nível | Não aparece hoje (sem binding no XAML). Se for exibido: "pendências de envio: {x}" | — | PENDENTE (P-04) |
| T-011 | `EstadoDoPainel.cs:156`–`157`; `MensagemDeFalha.cs:29`–`30` | "Sem resposta do serviço local — mostrando dados de {t} atrás"; "Sem resposta do serviço local — o Windows tenta reiniciá-lo sozinho; se não voltar em um minuto, chame o suporte." | serviço local; reiniciar o serviço | "Sem resposta do programa das catracas. Mostrando dados de há {t}."; "O Windows tenta reabrir o programa sozinho. Se não voltar em um minuto, chame o suporte." | Alta | REPROVADO |
| T-012 | `PainelViewModel.cs:87` | "{erro.StatusCode}: {erro.Status.Detail}" (estado de falha de comunicação) | RPC (código e texto da biblioteca, em inglês) | "Sem resposta do programa das catracas. Tente de novo em instantes." | Alta | REPROVADO |
| T-013 | `MensagemDeFalha.cs:25`–`26` | "Sua conta não tem permissão para falar com o serviço. Peça ao administrador do computador para incluir você no grupo \"ConexaoTopdata Operadores\" e entre de novo no Windows." | serviço | "Sua conta não tem permissão para usar o programa das catracas. Peça ao administrador do computador para liberar seu acesso (grupo ConexaoTopdata Operadores) e entre de novo." | Média | REPROVADO |
| T-014 | `MensagemDeFalha.cs:53` | "O serviço recusou o pedido ({codigo})." | serviço; código numérico | "O programa das catracas não aceitou o pedido. Tente de novo; se continuar, chame o suporte." | Média | REPROVADO |
| T-015 | `Login.cs:183`; `MensagemDeFalha.cs:42` | "Sessão encerrada. Entre de novo com usuário e senha." | sessão | "Você saiu. Entre de novo com usuário e senha." | Média | REPROVADO |
| T-016 | `Login.cs:234` | `string.Join(" ", resposta.Problemas)` (texto que vem do serviço) | texto não controlado no front end | Padronizar as mensagens no serviço, em português | — | PENDENTE (P-06) |
| T-017 | `Bandeja.cs:32`–`33` (tooltip da bandeja) | "Rayzer XAcess · serviço local: {x}"; "Rayzer XAcess · catracas {n} · serviço {x}" | serviço local | "Rayzer XAcess · catracas {n} · programa {x}" (D-11) | Média | REPROVADO |
| T-018 | `Bandeja.cs:62`–`63` | "Serviço local sem resposta" / "As catracas podem ter parado de atender. Abra o painel ou inicie a operação pelo ícone perto do relógio." | serviço local; relógio | "Programa das catracas sem resposta" / "...pelo ícone perto da hora." | Alta | REPROVADO |
| T-019 | `Bandeja.cs:69`; `Bandeja.cs:98`; `BandejaDoSistema.cs:85`, `:115` | "Serviço local de volta"; "O serviço do Rayzer XAcess não está instalado neste computador. Reinstale pelo Setup."; "...perto do relógio"; "Encerrar a operação para o serviço do Rayzer XAcess..." | serviço; "Setup" (inglês); relógio | "Programa das catracas voltou"; "...não está instalado neste computador. Chame o suporte para reinstalar."; "...perto da hora"; "Encerrar a operação das catracas..." | Média | REPROVADO |
| T-020 | `Textos.cs:25` | "Sem notícia do programa da catraca" | sem notícia (estado interno) | "Sem contato com a catraca" | Alta | REPROVADO |
| T-021 | `Textos.cs:205` | "Sem notícia: o serviço não responde" | sem notícia; serviço | "Sem contato: o programa das catracas não responde" | Alta | REPROVADO |
| T-022 | `Textos.cs:39` | "Programa da catraca parou várias vezes; nova tentativa automática em até 15 min" | tentativa; programa | "A catraca parou várias vezes; nova tentativa automática em até 15 minutos." | Média | REPROVADO |
| T-023 | `Textos.cs:42` (ramo padrão do `switch`) | `_ => (estado, Sinal.Atencao)`: mostra o nome interno do estado quando não está mapeado | estado interno em texto de tela | Texto fixo: "Situação não reconhecida. Chame o suporte." | Alta | REPROVADO |
| T-024 | `Textos.cs:80`, `:84`, `:89`, `:93`, `:94`; `Textos.cs:107` | "data inválida na catraca"; "adiantado {d}" / "atrasado {d}"; "certo · conferido há {x}"; "acertado há {x}"; "não conferido"; "Acertar relógio" | relógio | "Hora da catraca: certa"; "Hora da catraca: atrasada {d}"; "Hora da catraca: não conferida"; "Acertar a hora da catraca" | Alta | REPROVADO |
| T-025 | `Textos.cs:110`, `:114`, `:118`, `:108` (`NomeDoComando`) | "Refazer conexão"; "Liberar saída" (ok); "Coletar marcações"; "Mensagem no display"; "Aplicar configuração" | conexão; display (inglês); marcações (registro interno); "Aplicar" (D-01) | "Reconectar a catraca"; "Mensagem no visor"; "Ler registros da catraca" (só histórico); "Enviar configuração" (D-01) | Média | REPROVADO |
| T-026 | `Textos.cs:126`; `GerenciarCatraca.cs:426` | "Na fila da catraca" | fila | "Esperando a catraca" | Média | REPROVADO |
| T-027 | `Textos.cs:138`–`148` (Analisador, linhas de Diagnóstico) | "Desligada nesta instalação"; "Desligada (chave técnica inteligencia.ligada). Liga quem faz o ensaio, com reinício do serviço."; "Funciona com a camada desligada: a explicação vem do que a catraca já gravou." | chave técnica; camada inteligente; ensaio; serviço; instalação | Tirar da tela do operador. No suporte: "Análise de acessos: desligada" | Alta | REPROVADO |
| T-028 | `Textos.cs:150`–`170` (Analisador) | "Último ciclo"; "Duração do último ciclo: {ms} (orçamento: {ms})"; "Ciclos: {n} feitos · {n} acima do orçamento · {n} pulados · {n} com erro"; "Tentativas lidas"; "Ligada, com erro em todos os ciclos"; "Não subiu — veja o último erro" | ciclo; orçamento; pulado; tentativa | Só para o suporte. Na tela do operador: "Funcionando" / "Funcionando, com erros" / "Parada" | Alta | REPROVADO |
| T-029 | `Textos.cs:192`–`198` (sem catracas) | "Sem resposta do serviço"; "Abra o Assistente de configuração e informe o número do Inner de cada catraca."; "As catracas aparecem quando o serviço local responder. Veja o aviso no topo da janela." | serviço; Inner | "Sem resposta do programa das catracas"; "Abra o Assistente de configuração e informe o número de cada catraca."; "As catracas aparecem quando o programa responder. Veja o aviso no topo da janela." | Alta | REPROVADO |
| T-030 | `Textos.cs:229` (linha técnica do cartão da catraca) | "Firmware {v} · grupo {g} · porta {p} · {n} reconexões" | firmware; grupo; porta; reconexões | "Versão do programa {v} · {n} quedas de conexão". Grupo e porta só no modo técnico | Alta | REPROVADO |
| T-031 | `Telas.cs:192`–`195`, `:208`–`211` | "Nuvem: sem sincronização — o acesso segue pela lista local deste computador"; "Nuvem: sem internet desde {t} — o acesso segue..."; "Online"; "Sem sincronização"; "Offline — catracas seguem" | "online" e "offline" (inglês); sincronização | "Nuvem: sem envio — o acesso segue pela lista deste computador"; "Com internet"; "Sem internet — catracas seguem" | Baixa | REPROVADO |
| T-032 | `Telas.cs:1088`–`1090` | "Gravado. Use \"Aplicar agora nas catracas\" para as catracas passarem a usar a nova configuração, sem reiniciar o serviço. A espera pelo giro só muda quando o serviço reiniciar." | aplicar (D-01); serviço; giro | "Salvo. Clique em \"Enviar às catracas\" (D-01) para elas usarem a nova configuração. A espera pela passagem só muda depois de reiniciar o programa." | Média | REPROVADO |
| T-033 | `Telas.cs:1013` | "Aplicar a configuração salva em todas as catracas desta instalação? Cada uma reconecta..." | aplicar (D-01); instalação | Ver D-01 e D-13 | Média | BLOQUEADO (D-01) |
| T-034 | `Telas.cs:1174`, `:1159` | "Pacote de diagnóstico pronto. Escolha onde salvar."; "Pacote salvo em {c}. Envie esse arquivo ao suporte." | pacote | "Arquivo para o suporte pronto. Escolha onde salvar." | Baixa | REPROVADO |
| T-035 | `Telas.cs:927` | "Não foi possível gravar o arquivo ({erro.Message})..." | mensagem de exceção crua | Remover "({erro.Message})" e manter a orientação | Média | REPROVADO |
| T-036 | `Parametrizacao.cs:1092`; `Parametrizacao.cs:1054` | "Erro ao registrar destino da sugestão: {ex.Message}"; "Campo não encontrado (modo técnico necessário?)." | mensagem de exceção crua; modo técnico | "Não foi possível registrar a sugestão. Chame o suporte."; "Este ajuste aparece só no modo técnico." | Média | REPROVADO |
| T-037 | `GerenciarCatraca.cs:341`–`345` (pedidos sem função) | "Bip curto e longo" / "Aguardando bancada: documentado no manual (4.6.2), ainda não ensaiado (INT-UX-03). O serviço recusa até lá."; "Acionar relés avulsos" / "Aguardando confirmação da Topdata: o que cada relé faz na TopFit 4."; "Recolher cartão na urna" / "...a função do relé 2 não está documentada (docs/21 §8)."; "Liberar nos dois sentidos / trocar o sentido" / "Aguardando decisão D5 do dono do produto (evacuação) e a bancada (HIL-DIR-07)."; "Coletar marcações da memória da catraca" / "...(INT-REC-03, CHAOS-REC-01)." | IDs internos (INT-UX-03, HIL-DIR-07, INT-REC-03, CHAOS-REC-01, D5, docs/21 §8, 4.6.2); bancada; relé; memória; serviço | Mostrar só "Indisponível nesta versão" e quem libera ("Topdata"). Sem IDs (P9; D-04 para os IDs e D-05 para o status) | Alta | REPROVADO |
| T-038 | `GerenciarCatraca.cs:225`; `GerenciarCatraca.cs:262` | "Confirmar: refazer a conexão da {x}?"; "Confirmar: liberar um giro na {x}, sem ingresso?..." | conexão; giro | "Confirmar: reconectar a {x}?"; "Confirmar: liberar uma entrada na {x}, sem ingresso?..." | Alta | REPROVADO |
| T-039 | `GerenciarCatraca.cs:421`, `:428` | "{Comando}: pedido à catraca {n}..."; "{Comando} na catraca {n}: {situação} — {resultado}" | comando; pedido; situação em minúscula | "Ação enviada à catraca {n}. Veja o resultado no histórico." | Baixa | REPROVADO |

### 4.2 Painel ao vivo, acessos, consulta e pedidos da catraca

| ID | Arquivo:linha | Texto atual | Termo técnico | Proposta | Sev. | Situação |
|---|---|---|---|---|---|---|
| T-040 | `PainelAoVivo.xaml:21`–`36` (rótulos e nomes de automação) | "Acessos autorizados"; "Passagens confirmadas"; "Fluxo · últimos 5 min"; "Aguardando envio à nuvem" | autorizados x liberados (sinônimos); passagem | "Acessos liberados"; "Passaram pela catraca"; "Entradas nos últimos 5 minutos"; "Aguardando envio à nuvem" (aprovado) | Alta | REPROVADO |
| T-041 | `PainelAoVivo.xaml:22`, `:25`, `:28`, `:31`, `:35` (campo `Detalhe`) | "✓ total desta base"; "giro da catraca confirmado"; "× total desta base"; "entradas autorizadas"; "sobe sozinho quando há internet" | base (banco); giro; autorizadas | "Total de hoje"; "passou pela catraca"; "total de hoje"; "entradas liberadas" | Média | REPROVADO |
| T-042 | `PainelAoVivo.xaml:97`; `Acessos.xaml:8` | "Cada leitura nas catracas aparece aqui na hora, autorizada ou negada."; "Toda tentativa fica registrada, autorizada ou negada." | tentativa; autorizada | "Todo acesso aparece aqui na hora, liberado ou negado." | Média | REPROVADO |
| T-043 | `PainelAoVivo.xaml:82`; `Acessos.xaml:114` | "Explicação para o operador. O display da catraca continua com a mensagem de sempre." | display (inglês); fala em terceira pessoa | "Explicação para você. O visor da catraca continua com a mensagem de sempre." | Baixa | REPROVADO |
| T-044 | `PainelAoVivo.xaml:90`–`93`; `Acessos.xaml:122`–`127`; `Consulta.xaml:49`–`52`; `Simulador.xaml:52`–`54` | Cabeçalho "GIROU" | giro | "PASSOU" (sim ou não) | Média | REPROVADO |
| T-045 | `Acessos.xaml:50`, `:55`, `:57`; `UsosSemPassagem.cs:92` | "Usos sem passagem"; "Ingresso consumido cuja liberação falhou, ou que liberou e não girou. Confira com a pessoa antes de estornar."; "Usos sem passagem encontrados"; "Usos sem passagem ({n}): consumidos sem giro confirmado" | uso; consumido; passagem; giro; estornar | "Ingressos usados sem entrada confirmada". "Liberou, mas a catraca não confirmou a passagem. Confira com a pessoa antes de desfazer." (D-12) | Alta | REPROVADO |
| T-046 | `Acessos.xaml:68`, `:69`, `:72`, `:73`, `:76`, `:83`; `UsosSemPassagem.cs:162`, `:193` | "Quem estorna"; "Motivo do estorno"; "Estornar o selecionado…"; "Estornar"; "Estornar o uso de {c}?"; "Uso de {c} estornado: o ingresso volta a valer nesta borda." | estorno; borda | "Quem desfaz"; "Motivo"; "Desfazer o selecionado…"; "Desfazer"; "Desfazer o uso de {c}?"; "O ingresso volta a valer neste leitor." | Alta | REPROVADO |
| T-047 | `Acessos.xaml:62`, `:125`; `Telas.cs` (cabeçalho do filtro) | "CATEGORIA" | categoria | Ver D-10 | Média | BLOQUEADO (D-10) |
| T-048 | `Consulta.xaml:8` | "O número não fica guardado na tela: aparece só mascarado." | mascarado (jargão de dados) | "...aparece com parte do número oculta." | Baixa | REPROVADO |
| T-049 | `Consulta.xaml:45`; `Simulador.xaml:48`, `:57`–`58` | "Últimas tentativas com este código"; "Últimas tentativas nesta catraca"; "Nenhuma passagem simulada" | tentativa; passagem | "Últimas leituras deste código"; "Últimas leituras nesta catraca"; "Nenhuma leitura simulada ainda." | Média | REPROVADO |
| T-050 | `Telas.cs:844` | "Nenhuma tentativa no período." | tentativa | "Nenhum acesso no período." | Média | REPROVADO |
| T-051 | `Telas.cs:519` | "O número da catraca precisa ser um número inteiro." | inteiro | "Digite só números no campo Catraca." | Baixa | REPROVADO |
| T-052 | `Telas.cs:634`–`636` | "{n} de {m}" / "(sem limite)"; cabeçalho "Usos" | usos | "Vezes usado: {n} de {m}" | Baixa | REPROVADO |
| T-053 | `Telas.cs:624` (campo "Origem" da consulta); `Sincronizacao.xaml:45`–`57`; `MapaDeGiro.xaml:39`, `:84`; `MapaDeGiro.cs:60`–`64` | "Origem"; "Origens dos códigos"; "Origem desconhecida" | origem (provedor, leitor) | Ver D-09 | Média | BLOQUEADO (D-09) |
| T-054 | `Sincronizacao.xaml:49`–`53` | Cabeçalhos "REUTILIZÁVEL", "REUSO (S)", "SÓ NA URNA" | "REUSO (S)" (código) | "Pode usar de novo?"; "Vezes de reuso"; "Só na urna" | Alta | REPROVADO |
| T-055 | `Sincronizacao.xaml:40` | "Depois de corrigir a causa (o segredo da nuvem, por exemplo), manda de novo o que a nuvem recusou. A nuvem reconhece o que já recebeu." | segredo (chave técnica); reconhece | "Depois de corrigir a causa (a chave de acesso à nuvem, por exemplo), reenvia o que a nuvem recusou." | Média | REPROVADO |
| T-056 | `Sincronizacao.xaml:57` | "As origens aparecem quando a nuvem envia os primeiros cartões ou ingressos." | origem | Ver D-09 | Baixa | BLOQUEADO (D-09) |
| T-057 | `Telas.cs:720` | "Mais antigo na fila" | fila | "Pendência mais antiga" | Baixa | REPROVADO |
| T-058 | `Simulador.xaml:8` | "Só funciona com a instalação em modo simulação (Assistente de configuração, passo 2)." | instalação | "Só funciona com o modo simulação ligado (Assistente de configuração, passo 2)." | Baixa | REPROVADO |
| T-059 | `Telas.cs:1260`–`1265` (códigos de teste) | "QR online · inteira · 1 uso"; "QR online · meia · 1 uso"; "Cartão da bilheteria · inteira · só na urna" | online (inglês); uso | "QR do celular · inteira · vale 1 vez" | Baixa | REPROVADO |

### 4.3 Catracas, configuração, Gerenciar e Giro

| ID | Arquivo:linha | Texto atual | Termo técnico | Proposta | Sev. | Situação |
|---|---|---|---|---|---|---|
| T-060 | `Catracas.xaml:8` | "Cada catraca com a sua situação, o último evento e a linha técnica. Para incluir ou tirar catracas, use o Assistente de configuração (passo 2)." | evento; linha técnica | "Cada catraca, com a situação e o último acesso. Para incluir ou tirar catracas, use o Assistente de configuração." | Média | REPROVADO |
| T-061 | `CentralDaCatraca.xaml:70`; `CentralDaCatraca.cs:190` | "Relé 2 (desabilitado)"; "Relé 2 (ConfigurarAcionamento2, EI-017)" | relé; nome interno de método; código EI | "Saída 2 (desligada)" | Alta | REPROVADO |
| T-062 | `Parametrizacao.cs:80`, `:87`, `:94`, `:101`–`106` | "Leitor 2 — urna (ConfigurarLeitor2, EI-015)"; "Leitor 1 — frente (ConfigurarLeitor1, EI-014)"; "Tempo do relé 1, em segundos (ConfigurarAcionamento1, EI-016)"; "Função de liberação da entrada (comissionamento)"; "LiberarCatracaEntrada (EI-041)" e três variações | nome interno de método; código EI; comissionamento; função | "Leitor da urna"; "Leitor da frente"; "Tempo da saída 1, em segundos"; "Lado liberado na entrada" | Alta | REPROVADO |
| T-063 | `Parametrizacao.cs:61`–`72`, `:113`–`148`, `:119`–`125`, `:131`–`132` | "Leitor de ingressos"; "Tipo de leitor (ConfigurarTipoLeitor, EI-013)"; "0 · barras", "1 · magnético", "2 · proximidade Abatrack2", "3 · Wiegand", "4 · proximidade SmartCard serial", "5 · barras serial", "6 · Wiegand FC sem separador", "7 · Wiegand FC com separador", "8 · QR Code por letras"; "Mensagem padrão (EnviarMensagemPadraoOnLine, EI-056)"; "Dois leitores Wiegand (ConfigurarWiegandDoisLeitores, EI-024)"; "Habilita 0 · ExibirMensagem 0" e três variações; "Rearme do leitor"; "Formas de entrada on-line: dígitos, eco, forma, tempo, cursor (EnviarFormasEntradasOnLine, EI-032)"; "0 · desabilitado" a "4 · entrada e saída invertida" | nomes de método; códigos EI; protocolos; números de código | Modo técnico apenas (D-04). No caminho do operador: "Tipo de leitor: QR, cartão ou código de barras" | Alta | REPROVADO |
| T-064 | `Parametrizacao.cs:74`; `Configuracoes.xaml:23` | "Qual dos dois leitores lê o QR desta catraca ainda está sendo confirmado na bancada. Use o de QR Code; troque só se o QR não for lido."; "Tipo de leitor (técnico — 8 na bancada; 5 se o QR não for lido)" | bancada; números de código | "Use Leitor de QR Code. Troque só se o QR não for lido." (tirar "bancada" do texto) | Alta | REPROVADO |
| T-065 | `Parametrizacao.cs:457`, `:463` | "Cinco números de 0 a 255, separados por vírgula."; "A forma de entrada (3º número) vai de 0 a 7, de 10 a 14 ou de 100 a 105 (FUN:33)." | números de código; FUN:33 | Sair do operador (D-04) | Média | REPROVADO |
| T-066 | `Configuracoes.xaml:9`, `:18`, `:28`, `:30`; `Parametrizacao.cs:93` | "A espera pelo giro da nuvem só muda ao reiniciar o serviço."; "Segundos que a catraca fica liberada esperando o giro (1 a 50)"; "Segundos que a liberação espera o giro antes de subir para a nuvem" (no grupo Nuvem); nome de automação "Espera pelo giro" | giro; serviço; grupo errado ("giro da nuvem") | "Segundos de espera pela passagem (1 a 50)" no grupo Catracas (docs/48 C5) | Alta | REPROVADO |
| T-067 | `Configuracoes.xaml:19`; `GemeoDigitalViewModel.cs:729` | "Tempo de acionamento" | acionamento | "Tempo de liberação" | Baixa | REPROVADO |
| T-068 | `CentralDaCatraca.xaml:83`, `:85`, `:88`, `:114`; `GerenciarCatraca.xaml:33`, `:111`; `Novidades.cs:45` | "Equipamento"; "Versão do firmware:"; "Relógio:"; "Acertar o relógio agora"; "Acertar o _relógio agora" | firmware; relógio; "Equipamento" (outro nome de catraca) | "Catraca"; "Versão do programa da catraca:"; "Hora da catraca:"; "Acertar a hora da catraca agora" | Alta | REPROVADO |
| T-069 | `CentralDaCatraca.xaml:94`, `:112`, `:96`; `GerenciarCatraca.xaml:93`, `:98`, `:106`; `Pecas.cs:186`–`187` | "Mensagem temporária (pedido imediato)"; "Manutenção (pedido imediato)"; "Não é configuração: vai agora para a catraca, sem Salvar nem Aplicar..."; "Mostrar no display"; "Mensagem no display" | pedido imediato; display (inglês); "Aplicar" (D-01) | "Mensagem na hora (não grava)"; "Mostrar no visor" | Média | REPROVADO |
| T-070 | `GerenciarCatraca.xaml:112`; `GerenciarCatraca.cs:225`; `Textos.cs:110` | "Refazer a _conexão…"; "Refazer conexão" | conexão (técnico) | "Reconectar a catraca…" | Baixa | REPROVADO |
| T-071 | `CentralDaCatraca.cs:41` | "Aguardando ensaio NOVO-HIL-REL-04/06: o borne e a tensão do relé 2 na TopFit 4, e se o fim do acionamento dele se distingue do relé 1 (origem 5). Até lá, segue o…" | código de ensaio (HIL); borne; tensão; relé; "origem 5" | "Função em estudo: a saída 2 ainda não é usada nesta catraca." | Alta | REPROVADO (ver P-03 para confirmar a tela) |
| T-072 | `CentralDaCatraca.cs:193` | "Função 0, tempo 0 s — o padrão de fábrica, igual em todas as catracas" | função (código numérico) | "Padrão de fábrica: igual em todas as catracas" | Média | REPROVADO |
| T-073 | `CentralDaCatraca.cs:343`, `:356` | "A catraca confirmou a configuração salva (versão {hash})..."; "A catraca está com outra versão ({hash}), não a salva ({hash}). Ela não devolve a própria configuração..." | versão (código de hash) | "A catraca está usando a configuração salva."; "A catraca está com outra configuração. Envie a salva para a tela mostrar o que ela usa." (D-01) | Média | REPROVADO |
| T-074 | `CentralDaCatraca.cs:373`, `:552`; `Pecas.cs:161`, `:177` ("Braços e giro" em `ConfiguracaoPorPeca.cs:119`) | "Giro · {nome}"; "Braços e mecanismo de giro"; "Braços e giro" | giro | D-03 | Média | REPROVADO |
| T-075 | `CentralDaCatraca.cs:472`, `:484`; `MapaDeGiro.cs:556` | "Nada foi salvo: corrija o que o giro indica abaixo."; "O giro foi salvo; os outros parâmetros não."; "Mapa de giro salvo para a catraca {n}..." | giro; mapa de giro | "Nada foi salvo: corrija o que está marcado abaixo."; "A parte de passagem foi salva; o resto não." | Média | REPROVADO |
| T-076 | `Gemeo.xaml:71`, `:72`; `GemeoDigitalViewModel.cs:779`, `:780` | "_Testar giro"; "Libera e gira um terço de volta, só no desenho."; "Teste de giro" | giro | "Testar passagem (só no desenho)" | Média | REPROVADO |
| T-077 | `Gemeo.xaml:73`–`80` | "Vista"; "Vista da câmera" | vista (câmera) | "Ângulo" | Baixa | REPROVADO |
| T-078 | `Gemeo.xaml:83`, `:84`; `Pecas.cs:254`–`258` | "Leitor _facial (variante)"; "A variante Facial tem um leitor sobre uma haste. Fora do escopo do sistema hoje (fase 5)."; "Existe na variante Facial da TopFit 4, sobre uma haste. Usa outro SDK (WebSocket), separado da EasyInner."; "Fase 5, só com base legal definida." | variante; escopo; fase 5; SDK; WebSocket; nome de SDK da Topdata (EasyInner) | "Leitor facial: não disponível nesta versão." Tirar SDK e nome de fornecedor do texto (docs/46 §9) | Alta | REPROVADO |
| T-079 | `Gemeo.xaml:179`, `:180`; `Parametrizacao.xaml:53`, `:54` | "Modo técnico"; "Mostra todos os parâmetros, com os nomes técnicos. É uma conveniência, não uma proteção: o que não está confirmado continua bloqueado." | modo técnico; nomes técnicos | Ver D-04 | Média | BLOQUEADO (D-04) |
| T-080 | `Gemeo.xaml:280`, `:281`, `:286`; `GemeoDigitalViewModel.cs:276`, `:791` | "Cenários"; "Cenários de demonstração"; "DEMONSTRAÇÃO · cenários só no desenho, nada vai para a catraca"; "Os cenários rodam na demonstração." | cenário (jargão de teste) | "Demonstrações prontas" | Baixa | REPROVADO |
| T-081 | `Gemeo.xaml:285`, `:286` | "Na simulada"; "Passa o código de teste na catraca simulada do serviço e acompanha ao vivo o que o sistema decidir." | simulada; serviço; código de teste | "Testar na catraca simulada" + "Passa um QR de teste e mostra o que a catraca decidiu." | Média | REPROVADO |
| T-082 | `Gemeo.xaml:337`–`340`, `:353`–`355`; `Display2x16.cs:48`, `:58`–`59`, `:64` | "Como a mensagem aparece no display"; "Texto para ver no display"; "Prévia do display, duas linhas de 16 caracteres"; "_Ver no display 3D"; "Só no desenho. Para gravar nesta catraca, clique no display: mensagem padrão (salvar e aplicar) e mensagem temporária (na hora)."; "Tem {n} caracteres; o display mostra só os {n} primeiros."; "A palavra \"{p}\" fica cortada entre as duas linhas..."; "Tem acento ou símbolo: o display pode não mostrar. Ainda a confirmar na bancada." | display (inglês); caracteres; bancada; "aplicar" (D-01) | "Como a mensagem aparece no visor"; "Prévia do visor (duas linhas de 16 letras)"; "Ver no visor 3D"; "Só no desenho. Para gravar, clique no visor (mensagem padrão ou mensagem na hora)." | Média | REPROVADO |
| T-083 | `Pecas.cs:124`–`126` | "Na TopFit 4, o aviso que a pessoa vê é o display. As luzes verde e vermelha comandadas pelo sistema só existem na linha anterior de catracas (Linha 3)...; ainda falta confirmar com a Topdata e na bancada." | display; Linha 3; pictograma; Topdata; bancada | "Na TopFit 4, o aviso à pessoa é o visor. A luz de liberado e negado ainda está em confirmação com a Topdata." | Alta | REPROVADO |
| T-084 | `Pecas.cs:73`–`76` | "Fora do escopo" (status) | escopo | "Não disponível nesta versão" | Baixa | REPROVADO |
| T-085 | `Pecas.cs:152`–`155` | "Configurações" (nome de grupo dentro da peça); "Aqui na configuração, no painel desta peça (salvar e aplicar valem para a catraca inteira)." | "Configurações" (terceiro uso do nome); aplicar (D-01) | "Configuração desta peça" | Baixa | REPROVADO |
| T-086 | `Pecas.cs:161`–`176` (painel do giro e do mecanismo) | "Braços e mecanismo de giro"; "Três braços de aço inox. A cada passagem o conjunto gira um terço de volta e trava de novo. Só o giro lido pelo sensor da catraca conta como passagem de verdade."; "Liberar um giro de entrada"; "Confirmar a passagem pelo sensor de giro"; "A passagem só é contada quando a catraca avisa o giro. Liberado sem giro não conta."; "Escolher o sentido do giro e como ele conta"; "Clique nos braços: para cada leitor e para a liberação manual, qual função libera o braço e se o giro conta como entrada ou saída. É decisão do sistema."; "O lado em que o braço gira nesta instalação se confere girando uma vez." | giro; sensor; função; instalação | Reescrever com "passagem", "lado que o braço libera", "conta como entrada ou saída" (D-03) | Alta | REPROVADO |
| T-087 | `Pecas.cs:173`–`176` | "Liberar nos dois sentidos"; "Permite carona; só para evacuação, e depende da decisão do dono do produto (D5)."; "Queda dos braços em emergência"; "Não documentada para a TopFit 4 desta instalação. O desenho não simula." | D5; dono do produto; carona; instalação | "Liberar nos dois sentidos (só evacuação)"; "Em definição pela Topdata." Sem D5 e sem "dono do produto" | Alta | REPROVADO |
| T-088 | `Pecas.cs:178`–`202` | "{aqui}: tempo de liberação e giro. A liberação manual fica em {gerenciar}."; "Mostra a mensagem padrão quando a catraca está livre."; "Até 32 caracteres, de 1 a 60 segundos, numa catraca. É um pedido imediato: vai na hora, sem salvar."; "Integrado na tampa. Lê o QR impresso ou na tela do celular, por aproximação."; "QR de 4 a 16 caracteres — Limite da placa da catraca, não do leitor."; "O código vai para a decisão de acesso da borda, a mesma da operação." | giro; pedido imediato; borda; placa; leitor | "Tempo de liberação e passagem"; "Até 32 letras, de 1 a 60 segundos. Vai na hora, sem salvar."; "O código vai para a decisão de acesso do leitor." | Média | REPROVADO |
| T-089 | `Pecas.cs:188`–`189` | "Letras com acento" / "O display pode não mostrar acentos. Confirmar na bancada." | display; bancada | Ver D-08 | Média | BLOQUEADO (D-08) |
| T-090 | `Pecas.cs:205`–`226` | "Leitor de cartão da frente"; "Leitor 1, por aproximação. Um cartão lido aqui é aceito ou recusado conforme a regra do provedor."; "Cartão da bilheteria com a regra \"só na urna\" é recusado aqui, de propósito."; "Fenda com leitor próprio (leitor 2)."; "No painel da urna, a linha do leitor 2 do giro: o lado em que o braço gira e se conta como entrada."; "A função do relé 2 não está documentada pela Topdata. A urna ainda não engole o cartão."; "O aviso de urna cheia ... ainda não foi ensaiado na bancada." | leitor 1/2; regra; provedor; linha; relé; Topdata; bancada | Reescrever sem código e sem "provedor" (D-09). Ver D-08 para a urna | Alta | REPROVADO |
| T-091 | `Pecas.cs:238`–`249` | "Sinal de liberado"; "Pictograma na tampa..."; "Acender ao liberar" | pictograma (jargão) | "Luz de liberado" | Baixa | REPROVADO |
| T-092 | `Pecas.cs:264`–`276` | "Cabeça da catraca, onde ficam display, teclado, leitores e sinais."; "Pedestal metálico. Por dentro passam a placa de controle e os cabos..."; "{aqui}: firmware, relógio e, no modo técnico, a instalação." | display; placa de controle; firmware; relógio | "Cabeça da catraca, com visor, teclado, leitores e luzes."; "Versão do programa, hora e, no modo técnico, a instalação." | Média | REPROVADO |
| T-093 | `ConfiguracaoPorPeca.cs:113`–`130` | "Leitor da frente (QR)"; "Urna (leitor 2)"; "Braços e giro"; "Placa de controle (dentro da coluna)" | leitor 2; giro; placa | "Braços e passagem"; "Placa (dentro da coluna)" | Baixa | REPROVADO |
| T-094 | `GemeoDigital/Roteiros.cs:46`–`166` (demonstrações, aparecem no modo Demonstração) | "A borda confere o código na base local e libera..."; "O sensor de giro avisa a catraca..."; "Liberar não é passar: sem giro, não conta como entrada."; "Acaba o tempo de acionamento. Fica registrado \"liberado sem giro\"..."; "intervalo de reuso (4 min)"; "Negado: para este provedor o cartão só vale na fenda da urna..."; "O que muda quando o PC perde contato com a catraca."; "...não há lista gravada na catraca. Siga o procedimento de contingência..."; "O sistema guarda o evento."; "...precisa ser confirmado na bancada." | borda; base local; sensor; giro; provedor; reuso; acionamento; contingência; evento; bancada | Reescrever com "leitor", "lista deste computador", "passagem" | Alta | REPROVADO |
| T-095 | `GemeoDigitalViewModel.cs:315`, `:319`, `:322` | "Na catraca agora"; "Configuração do evento (o desenho segue)" | nome duplicado ("Configuração do evento") | Ver seção 5 (D-02) | Baixa | BLOQUEADO (D-02) |
| T-096 | `GemeoDigitalViewModel.cs:380`, `:387`, `:391`, `:394`, `:919`–`930`, `:923`–`924` | "A pré-visualização do giro roda na demonstração..."; "Pré-visualização do giro: {n}"; "PRÉ-VISUALIZAÇÃO · nada foi enviado. {n}: {função}."; "O braço gira no sentido {s} da catraca (a seta), e o giro conta como {c}."; "Liberada: esperando o giro"; "Girando: passagem em andamento"; "{n} giro(s) · {n} liberada(s) · {n} negada(s) · {n} sem giro" | giro; função; pré-visualização | "Liberada: esperando a passagem"; "Pré-visualização de {n}: {lado}"; "{n} passagem(ns) · {n} liberada(s) · {n} negada(s) · {n} sem passagem" (D-03) | Média | REPROVADO |
| T-097 | `GemeoDigitalViewModel.cs:727`–`731` | "Tempo de acionamento {n} s"; "Espera pelo giro {n} s"; "Tipo de leitor (técnico)"; "Desligado (a urna aparece apagada)" | acionamento; giro; técnico | "Tempo da liberação {n} s"; "Espera pela passagem {n} s"; "Tipo de leitor (modo técnico)" | Média | REPROVADO |
| T-098 | `GemeoDigitalViewModel.cs:815`, `:821`, `:844`, `:859` | "Só com o serviço em modo simulação. Com catraca física, a tela apenas observa."; "\"{roteiro}\" não tem código de teste para a catraca simulada."; "Nenhum evento desde que o modo ao vivo começou." | serviço; código de teste; evento | "Só funciona com o modo simulação ligado. Com catraca física, esta tela só mostra." | Média | REPROVADO |
| T-099 | `GemeoDigitalViewModel.cs:919`, `:925`; `Roteiros.cs:68`; `Display2x16.cs:24` | "Negada: \"Acesso nao autorizado\" no display"; "A borda recusa: \"Acesso nao autorizado\" no display por 3 segundos..." | texto de display sem acento | Ver D-08 | Média | BLOQUEADO (D-08) |
| T-100 | `GemeoDigitalViewModel.cs:935`–`938` | "o QR do celular"; "o cartão no leitor da frente"; "o cartão na fenda da urna"; "a credencial" (complementos de frase) | credencial | Ver D-07 | Baixa | BLOQUEADO (D-07) |
| T-101 | `GemeoDigitalViewModel.cs:161` (título do item de menu) e `Gemeo.xaml:7`, `:28`, `:149` | "Configuração da catraca" (menu e cabeçalho) e "Configuração desta catraca" (subtela) | nome duplicado | Ver seção 5 e D-02 | Alta | BLOQUEADO (D-02) |
| T-102 | `Especificacao.cs:87`, `:210`–`332` (mensagens de especificação) | "Especificação da catraca ilegível: {erro}"; "Versão {v} desconhecida; esperada 1."; "fit4.json não foi embutido no Desktop.ViewModels."; "Falta o acabamento {a} em aparencia.acabamentos." | termos de desenvolvedor (JSON, embutido, aparencia.acabamentos) | Só aparece se o arquivo de especificação falhar. Texto de suporte, sem nome de projeto | — | PENDENTE (P-07) |
| T-103 | `MapaDeGiro.xaml:17`, `:21`, `:39`, `:46`, `:50`, `:54`, `:56`, `:117`, `:125`, `:132`; `MapaDeGiro.cs:321`; `Parametrizacao.xaml:97` | "Conferência do sentido nesta catraca"; "Avisos do mapa salvo"; "Origens do giro"; "Padrão (a função da catraca, conta como entrada)"; "Função que libera o giro"; "Texto no display (vazio = padrão)"; "Texto do giro"; "Problemas do giro"; "Salvar o giro"; "Situação do giro na catraca"; "Giro desta catraca"; "_Giro" (aba) | giro; mapa de giro; função; origem; display | D-03 (nome do giro); "Lado que o braço libera" no lugar de "função" | Alta | REPROVADO |
| T-104 | `MapaDeGiro.xaml:30` | "Entrada e saída, na catraca, são só o nome do lado em que o braço gira. Aqui você escolhe, para cada leitor, qual lado liberar e se a passagem conta como entrada ou saída." | função; lado; passagem | "Escolha, para cada leitor, o lado que o braço libera e se a passagem conta como entrada ou saída." | Média | REPROVADO |
| T-105 | `MapaDeGiro.xaml:84`; `MapaDeGiro.cs:60`–`64`; `MapaDeGiro.cs:130`, `:143` | "Depois de aplicar: passe um cartão de teste por esta origem (ou use a liberação manual) e olhe o braço."; "Leitor 1 (frente e QR)"; "Leitor 2 (urna)"; "Liberação manual do painel"; "Origem desconhecida" | origem; aplicar (D-01) | "...passe um cartão de teste por este leitor..."; "Leitor não identificado" | Média | REPROVADO |
| T-106 | `MapaDeGiro.cs:141` | "Saida liberada" / "Entrada liberada" | erro de grafia ("Saida" sem acento) | "Saída liberada" | Baixa | REPROVADO |
| T-107 | `MapaDeGiro.cs:147`–`150` | "Braço gira no sentido de entrada da catraca" / "...de saída da catraca"; "(previsto pelo nome da função; confira girando uma vez)"; "Conta como saída" | função; sentido | "Braço gira para o lado da entrada. Confira girando uma vez." | Média | REPROVADO |
| T-108 | `MapaDeGiro.cs:155` | "O texto do giro tem até 32 caracteres (duas linhas de 16 no display)." | giro; display | "O texto tem até 32 letras (duas linhas no visor)." | Baixa | REPROVADO |
| T-109 | `MapaDeGiro.cs:162`–`164`, `:175`–`176`, `:200`–`208`, `:214`–`218`, `:426`–`430`, `:606`, `:624`–`627` | "Sentido conferido nesta instalação"; "Na conferência o braço girou ao contrário: troque a função"; "Sentido ainda não conferido nesta instalação"; "{função}: ninguém girou ainda com esta função nesta catraca."; "{função}: conferida por {x} em {d}."; "padrão (a função da catraca, conta como entrada)"; "Liberar entrada invertida" e variantes; "Função desconhecida"; "Salve e aplique esta regra antes de conferir: a conferência vale para a função que a catraca está usando."; "Conferido: {função} gira para o lado da seta nesta catraca."; "Registrado: {função} girou ao contrário. Troque a função desta regra." | função (vinda da DLL); regra; instalação; conferência; "invertida"; aplicar | Trocar "função" por "lado que o braço libera"; "invertida" por "sentido trocado"; manter "conferido" (D-03) | Alta | REPROVADO |
| T-110 | `MapaDeGiro.cs:446`, `:555`–`556`, `:583`–`584`, `:600` | "Aplicar o mapa de giro salvo na catraca {n}? Ela reconecta..., gire uma vez por regra..."; "Mapa de giro salvo para a catraca {n}..."; "Pedido à catraca {n}: ... gire uma vez por regra e confirme o lado aqui." | mapa de giro; regra; aplicar | "Enviar o giro salvo para a catraca {n}?..." (D-01) | Média | BLOQUEADO (D-01) |
| T-111 | `Parametrizacao.xaml:83`–`97` (abas) | "_Leitura"; "Li_beração"; "_Display"; "_Instalação"; "_Giro" | display; instalação; giro | "Leitor"; "Liberação"; "Visor"; "Ajustes gerais"; "Giro" (D-03) | Média | REPROVADO |
| T-112 | `Parametrizacao.cs:100`–`101` | "Sentido da liberação"; "Função de liberação da entrada (comissionamento)" | função; comissionamento | "Lado liberado na entrada" | Média | REPROVADO |

### 4.4 Cadastro, pessoas, sincronização, contas, diagnóstico e usuários

| ID | Arquivo:linha | Texto atual | Termo técnico | Proposta | Sev. | Situação |
|---|---|---|---|---|---|---|
| T-113 | `Pessoas.xaml:8` | "...colaboradores, moradores, prestadores, visitantes, alunos e staff. Fica só neste computador, com os dados pessoais cifrados. Bloquear vale na próxima leitura." | staff (inglês); cifrados (jargão) | "...alunos e equipe. Fica só neste computador, com os dados protegidos. Bloquear vale na próxima leitura." | Média | REPROVADO |
| T-114 | `Pessoas.xaml:28`, `:198`–`205`, `:208`–`223`; `Pessoas.cs:191`, `:647`–`648`, `:786`, `:813` | "Nome (3 letras ou mais de cada parte), documento ou código da credencial"; "Credenciais"; "Motivo (perdida, bloqueada ou devolvida)"; "Nova credencial: o código como a catraca lê..."; "Credencial cadastrada: já vale na catraca."; "Importação aplicada: {n} pessoa(s) e {m} credencial(is)." | credencial | Ver D-07 | Média | BLOQUEADO (D-07) |
| T-115 | `Pessoas.xaml:59` | "Documento e contato aparecem mascarados: o seu papel não permite vê-los." | mascarados; papel | "Documento e contato aparecem ocultos: seu tipo de acesso não permite vê-los." (D-06) | Baixa | REPROVADO |
| T-116 | `Pessoas.xaml:154`, `:161`, `:164`; `Pessoas.cs:378`, `:383`–`387`, `:467`–`469`, `:552`–`577`; `ParametrosDoCadastro.xaml:25`–`32`, `:43`, `:76`; `ParametrosDoCadastro.cs:261`, `:447` | "Vale de · até (vazio: a validade do perfil)"; "Horário · entradas por dia · catracas (vazio: as do perfil)"; "Tabela de horário"; "Novo perfil"; "Perfis"; "Ativo (aparece no cadastro)"; "Perfil: {nome}"; "Ativa: passa conforme perfil, catracas, horário e validade." | perfil; papel; "horário" de tabela | Ver D-06 | Alta | BLOQUEADO (D-06) |
| T-117 | `ParametrosDoCadastro.xaml:8`; `ParametrosDoCadastro.xaml:71`, `:97`–`104`, `:111`, `:135`, `:147`–`149`; `ParametrosDoCadastro.cs:212`, `:302`–`305`, `:378`, `:394`, `:527`; `Pessoas.xaml:71`, `:164` | "...tabelas de horário (horário de Brasília) e feriados, e os perfis com..."; "Tabela de horário"; "Nova _tabela"; "Tabelas de horário (até 100)"; "Nº"; "FAIXAS"; "Feriados (usam a linha Feriado da tabela)"; "Faixas de cada dia como 08:00-12:00, 13:00-18:00 (até 4 por dia; o fim não entra). Dia vazio não libera. Mais de 2 faixas por dia talvez não caibam na lista off-line do Inner (a confirmar na bancada)."; "Só exclui tabela que nenhum perfil nem pessoa usa."; "Nova tabela de horário"; "Tabela {Id}: {Nome}"; "{h.Id} · {h.Nome}"; "escreva as faixas como..."; "Tabela {id} excluída."; "nenhuma faixa: não libera" | tabela de horário; faixa; id interno; lista off-line do Inner; bancada | "Horários" (cadastro); "Dias e horas"; sem "Nº" e sem id; "Mais de 2 faixas..." sai do texto (vai para o suporte) | Alta | REPROVADO |
| T-118 | `ParametrosDoCadastro.xaml:59`, `:67`; `ParametrosDoCadastro.cs:431` | "Validade (dias; 0 = no dia)"; "Guardar após inativar (dias)"; "Retenção: 1 a 3650 dias." | retenção (jargão) | "Guardar por (dias) depois de inativar" | Baixa | REPROVADO |
| T-119 | `Pessoas.xaml:231`–`236` | "Use o modelo modelo-pessoas.csv (pasta modelos do programa) ou um .xlsx com a aba Pessoas. Primeiro a prévia confere cada linha com as regras da ficha; só depois você aplica, e entra tudo ou nada." | csv; xlsx; aba; prévia | "Use o modelo de planilha (pasta Modelos do programa) ou uma planilha com a aba Pessoas. Primeiro conferimos cada linha; depois você confirma. Entra tudo ou nada." | Média | REPROVADO |
| T-120 | `Pessoas.xaml:236`, `:243`, `:253`–`254`; `Pessoas.cs:191`, `:212`, `:238` | "Apli_car importação"; "Importações feitas"; "_Desfazer o lote escolhido"; "Apaga as pessoas que o lote criou, com as credenciais. As passagens delas ficam, sem o nome."; "Lote de {d} desfeito: ... As passagens delas ficam, sem o nome." | lote; passagens; "aplicar" | "Confirmar importação"; "Importações feitas"; "Desfazer esta importação"; "As passagens dela continuam, sem o nome." | Média | REPROVADO |
| T-121 | `Sincronizacao.xaml:40` | "(o segredo da nuvem, por exemplo)" | segredo (chave técnica) | "(a chave de acesso à nuvem, por exemplo)" | Média | REPROVADO |
| T-122 | `Contas.xaml:8` | "O resumo do que aconteceu nas catracas no período, no horário de Brasília, e o CSV dele. Autorizado = a catraca liberou; com giro = a pessoa passou. ... Os relatórios R1–R8 ainda não existem..." | CSV; autorizado; giro; R1–R8 | "...e a planilha dele. Liberado = a catraca deixou passar. Passou = a passagem foi confirmada. Os relatórios ainda não disponíveis estão no fim da página." | Alta | REPROVADO |
| T-123 | `Contas.xaml:53`, `:76`–`84` | "Com giro confirmado"; cabeçalho "COM GIRO" | giro | "Passaram pela catraca"; cabeçalho "PASSARAM" | Alta | REPROVADO |
| T-124 | `Contas.xaml.cs:22`–`23` | "Exportar prestação de contas"; "Planilha CSV (*.csv)" | CSV (formato) | "Planilha (*.csv)" | Baixa | REPROVADO |
| T-125 | `Telas.cs:815`–`817` (texto da seção "Ainda não disponível", visível em `Contas.xaml:121`) | "R1 · Boletim do dia (PDF)" — "Fase 6. Depende da hora de corte do dia de operação (E9) e da regra da meia-entrada (E10), e dos tipos cadastrados (fase 3). Hoje: o resumo \"Por categoria\" abaixo."; "R2 · Fluxo por hora e por tipo" — "Fase 6. Hoje: \"Por hora\" abaixo..."; "R3 · Por catraca e disponibilidade" — "Fase 6. ..." | fase 6; E9; E10; corte; fase 3; R1–R3 | "Em breve" + uma frase sem código. Ex.: "Boletim do dia (PDF): em breve." | Alta | REPROVADO |
| T-126 | `Telas.cs:818`–`823` (mesma seção) | "R4 · Por origem do ingresso" — "Fase 6: validados por provedor (bilheteria, venda online, cortesia)."; "R5 · Conciliação (cadastrado × usado)" — "Fase 6: depende do cadastro de cartões (fase 3)."; "R6 · Ocorrências e auditoria" — "Fase 6: negativas repetidas, liberações manuais e comandos..."; "R7 · Consolidado do evento" — "Fase 6: depende da hora de corte do dia de operação (E9)."; "R8 · Saúde da operação" — "Fase 6: quedas de catraca, do serviço e da sincronização, com duração."; "Fechar o corte com código de conferência" — "Fase 6: números congelados com SHA-256 (docs/25 §4)." | provedor; conciliação; corte; SHA-256; docs/25; "fase 6"; comandos | Textos sem código e sem número de fase; "Fechar o dia" no lugar de "Fechar o corte" | Alta | REPROVADO |
| T-127 | `Telas.cs:861`–`895` (conteúdo do CSV, aberto no Excel) | "Prestação de contas;gerada em …"; "Com giro confirmado;"; "Entradas (giros pelo mapa de giro);"; "Saídas (giros pelo mapa de giro);"; "Motivo da negativa;"; "Catraca;Liberados;Com giro;Negados;Entradas;Saídas" | giro; mapa de giro; negativa | "Passaram pela catraca"; "Entradas (pela contagem da catraca)"; "Motivo da negação" | Média | REPROVADO |
| T-128 | `Telas.cs:1028` (menu) e `Configuracoes.xaml:8` ("Configurações do evento") e `Pecas.cs:153` ("Configurações") | "Configurações" / "Configurações do evento" | nome duplicado | Ver seção 5 | Média | REPROVADO |
| T-129 | `Diagnostico.xaml:14`–`15`; `Diagnostico.xaml.cs:32`–`33` | "_Exportar pacote de diagnóstico"; "Exportar pacote de diagnóstico para o suporte"; "Salvar o pacote de diagnóstico"; "Arquivo zip (*.zip)" | pacote; zip | "Salvar arquivo para o suporte"; "Arquivo ZIP (*.zip)" | Baixa | REPROVADO |
| T-130 | `Diagnostico.xaml:20`, `:31`, `:38`, `:42` | "Versão do serviço"; "Camada inteligente"; "Camada inteligente (Analisador)"; "Saúde do Analisador" | serviço; camada inteligente; Analisador | "Versão do programa"; "Análise de acessos (suporte)" | Alta | REPROVADO |
| T-131 | `Simulador.xaml:8` (já na T-058) e `Telas.cs:1229` / `Simulador.xaml:5`, `:7` | "Simulador" (menu) x "Simulador de catraca" (título) | nome duplicado | Ver D-14 | Baixa | BLOQUEADO (D-14) |
| T-132 | `Usuarios.xaml:8`; `Usuarios.cs:272` | "Quem entra no sistema e o que cada papel pode fazer. ... O serviço confere a permissão em cada ação."; "...as sessões abertas dela foram encerradas." | serviço; papel (D-06); sessões | "...O programa confere a permissão em cada ação."; "...ela foi desconectada de todos os computadores." | Média | REPROVADO |
| T-133 | `Usuarios.xaml:53`, `:82`–`83`, `:88`–`90`, `:99`–`103`; `Usuarios.cs:168` | "Papéis"; "Novo papel"; "PAPÉIS"; "Nome do papel"; "Para que serve"; "O que este papel pode fazer"; "Papel: {nome}" | papel (D-06) | Ver D-06 | Alta | BLOQUEADO (D-06) |
| T-134 | `Novidades.cs:45` | "Relógio da catraca conferido sozinho, com aviso na hora em que ele sai do horário certo." | relógio | "A hora da catraca é conferida sozinha, com aviso se ela sair do horário." | Média | REPROVADO |
| T-135 | `Novidades.cs:47`–`48` | "Mais comandos por catraca: liberar a passagem na mão com o motivo, mandar um recado no visor e refazer a conexão, tudo com registro de quem fez." | comandos; passagem; conexão | "Mais ações por catraca: liberar uma entrada com motivo, mandar um recado no visor e reconectar a catraca. Tudo fica registrado com quem fez." | Média | REPROVADO |
| T-136 | `Novidades.cs:39`–`40` | "Configuração da catraca num lugar só: clique numa peça do desenho da catraca para ver e ajustar o que ela faz, e use \"Aplicar nesta catraca\" para mandar tudo de uma vez." | aplicar (D-01); nome da configuração (D-02) | Ver D-01 e D-02. Este texto diz algo que o docs/48 §4 (DU12) aponta como falso | Alta | BLOQUEADO (D-01) |

### 4.5 Textos de estado, Gerenciar e pedidos da catraca (painel de peça e Gerenciar)

| ID | Arquivo:linha | Texto atual | Termo técnico | Proposta | Sev. | Situação |
|---|---|---|---|---|---|---|
| T-137 | `GerenciarCatraca.xaml:13` | "Cada pedido fica registrado com o seu nome e é executado quando a catraca estiver livre — nunca no meio de uma passagem." | pedido; passagem | Aprovado no texto. Manter | — | APROVADO |
| T-138 | `GerenciarCatraca.xaml:39`, `:42`, `:43`, `:45`, `:46` | "_Configurar a catraca"; "Abre o desenho da catraca. Clique numa peça para ver e mudar a configuração dela; salve e aplique a catraca inteira de uma vez."; "_Parametrização (em lista)"; "Parametrização desta catraca"; "Tipo de leitor, urna, tempo de liberação e mensagem desta catraca: salvar, ver o que muda e aplicar." | parametrização; nome duplicado; aplicar (D-01) | Ver D-02 (um nome) e D-01 (verbo) | Alta | BLOQUEADO (D-02) |
| T-139 | Locais de "Aplicar" (ver seção 8, D-01): `Configuracoes.xaml:38`, `:40`, `:50`–`51`; `Gemeo.xaml:207`, `:210`, `:220`–`221`, `:355`; `Parametrizacao.xaml:138`–`150`, `:155`; `MapaDeGiro.xaml:136`, `:143`–`144`; `Pecas.cs:155`, `:191`; `Pessoas.xaml:236`; `CentralDaCatraca.cs:37`, `:292`–`297`, `:348`; `Parametrizacao.cs:715`–`723`, `:751`–`778`, `:943`–`946`, `:972`–`1008`; `MapaDeGiro.cs:446`–`453`; `Telas.cs:1013`; `Textos.cs:111`; `Novidades.cs:39`–`40`; `Gemeo.xaml:207`(ToolTip) | "Aplicar", "Aplicar agora", "Aplicar nesta catraca…", "Aplicar a configuração salva…", "Aplicar importação", "Aplicando: aguardando a catraca", "Salva, não aplicada: o último pedido falhou" | aplicar (verbo técnico) | Ver D-01 | Alta | BLOQUEADO (D-01) |
| T-140 | `GerenciarCatraca.xaml:149`, `:150`, `:157`; (ver também `CampoDaCatraca.xaml:48`, `CentralDaCatraca.xaml:71`); `Parametrizacao.cs:380`, `:425`; `Pecas.cs:74`; `Contas.xaml:120`–`128` | "Ainda não disponível"; "Funções aguardando confirmação"; "Aguardando confirmação"; "Aguardando confirmação: esta opção ainda não pode ser escolhida."; "{nome} — aguardando confirmação" | "aguardando confirmação" (status interno de bancada) | Ver D-05 | Alta | BLOQUEADO (D-05) |
| T-141 | `JanelaPrincipal.xaml:60`, `:137`; `EstadoDoPainel.cs:113`; `App.xaml:116`–`118`; `Telas.cs:1229`; `Simulador.xaml:5`, `:7` | "! MODO SIMULAÇÃO — sem catraca física"; "Modo simulação"; "Simulação"; "Simulador de catraca" | modo simulação x simulador x simulação (três nomes para uma coisa) | Ver D-14 | Média | BLOQUEADO (D-14) |
| T-142 | `Parametrizacao.cs:546`; `Parametrizacao.xaml:5`, `:26`–`27`; `Parametrizacao.xaml:83` ("Grupos da parametrização") | "Parametrização"; "Parametrização da catraca"; "O que esta catraca usa de diferente do evento. Salvar grava; a catraca só passa a usar depois de \"Aplicar nesta catraca\", que pede confirmação." | parametrização; aplicar | Ver D-02 (nome) e D-01 (verbo) | Alta | BLOQUEADO (D-02) |
| T-143 | `GerenciarCatraca.xaml:66`, `:70`–`71`; `GerenciarCatraca.cs:262`; `GerenciarCatraca.xaml:67` | "Libera um giro sem ingresso, no sentido que o mapa de giro desta catraca define para a liberação manual (entrada, no padrão). Não entra na contagem de ingressos: aparece só aqui, com o motivo. Se a catraca estiver ocupada por mais de 15 segundos, o pedido não é executado."; "_Liberar um giro…"; "Liberar um giro sem ingresso, com confirmação" | giro; mapa de giro; contagem; pedido | "Libera uma entrada sem ingresso, com motivo. Não conta como ingresso. Aparece só aqui. Se a catraca estiver ocupada por mais de 15 segundos, o pedido não é feito." | Alta | REPROVADO |
| T-144 | `GerenciarCatraca.xaml:93`, `:98`, `:106`; `Textos.cs:108` | "Mensagem no display"; "Mensagem temporária"; "_Mostrar no display" | display (inglês) | "Mensagem no visor"; "Mostrar no visor" | Média | REPROVADO |

---

### 4.6 Contagem das entradas da auditoria

- **Total:** 144 entradas (T-001 a T-144).
- **REPROVADO:** 119. **BLOQUEADO:** 21. **PENDENTE:** 3. **APROVADO:** 1 (T-137).
- **Severidade:** Alta 51 · Média 58 · Baixa 31 · sem severidade (itens de PENDENTE ou APROVADO) 4.
- Cada entrada pode agrupar mais de um literal do mesmo arquivo. Os literais individuais estão citados na própria linha.
- Os textos aprovados que não entram na auditoria estão na seção 11 e no glossário (seção 3).

---

## 5. Nomes duplicados e inconsistentes (P1 e P2)

| Função | Nomes encontrados (arquivo:linha) | Proposta | Situação |
|---|---|---|---|
| Configuração de uma catraca | "Configuração da catraca" (`GemeoDigitalViewModel.cs:161`; `Gemeo.xaml:7`, `:28`); "Configuração desta catraca" (`CentralDaCatraca.cs:84`; `Gemeo.xaml:149`); "Configurar a catraca" (`GerenciarCatraca.xaml:39`); "Parametrização da catraca" (`Parametrizacao.xaml:5`, `:26`); "Parametrização" (`Parametrizacao.cs:546`); "Parametrização desta catraca" (`GerenciarCatraca.xaml:45`); "Parametrização (em lista)" (`GerenciarCatraca.xaml:43`); "Abrir a confi_guração" (`Parametrizacao.xaml:30`); "Ver em lista" (`Gemeo.xaml:145`); "Configuração do evento (o desenho segue)" (`GemeoDigitalViewModel.cs:315`). Oito nomes para uma função | Um nome: "Configuração da catraca". A visão em lista é um modo da mesma tela, sem outro nome (docs/48 C3) | BLOQUEADO (D-02) |
| Configurações gerais | "Configurações" (`Telas.cs:1028`, item de menu); "Configurações do evento" (`Configuracoes.xaml:8`); "Configuração do evento" (`GemeoDigitalViewModel.cs:315`); "Configurações" (`Pecas.cs:153`, nome de grupo na peça) | "Configurações do evento" no menu e no título. Sai o nome de grupo dentro da peça (docs/48 C5) | REPROVADO |
| Cadastro de empresas, salas, horários, feriados e perfis | "Perfis e horários" (`ParametrosDoCadastro.cs:99`, item de menu); "Empresas, horários e perfis" (`ParametrosDoCadastro.xaml:5`, `:7`); "Parâmetros do cadastro" (docs/46 §3, não existe no código) | Um nome só. Proposta do plano: "Cadastro" (docs/48 C7, pendente de A3, aqui sugerido: "Cadastro" com a lista do que tem dentro) | BLOQUEADO (decisão D-16, herdada do C7) |
| Usuários e papéis | "Usuários" (`Usuarios.cs:83`, item de menu); "Usuários e papéis" (`Usuarios.xaml:7`) | "Usuários e papéis" nos dois lugares | REPROVADO (baixa) |
| Simulador | "Simulador" (`Telas.cs:1229`); "Simulador de catraca" (`Simulador.xaml:7`) | Ver D-14 | BLOQUEADO (D-14) |
| Catracas (o equipamento) | "Catraca" (quase toda a tela); "Equipamento" (`CentralDaCatraca.xaml:83`) | "Catraca" | REPROVADO (baixa) |
| Liberado / autorizado / passou / passagem / com giro | "Liberados" (`Telas.cs:490`; `Contas.xaml:51`); "Acessos autorizados" (`PainelAoVivo.xaml:21`); "Autorizado" (`Contas.xaml:8`); "Passagens confirmadas" (`PainelAoVivo.xaml:24`); "Com giro confirmado" (`Contas.xaml:53`); "Passou" (`Contas.xaml:8`) | "Liberado" (a catraca deixou passar) e "Passou" (a passagem foi confirmada). Um nome para cada | REPROVADO |
| Prestação de contas / relatórios | "Prestação de contas" (`Telas.cs:757`, menu); "Relatórios ainda não disponíveis" (`Contas.xaml:121`); "Relatórios R1–R8" (`Contas.xaml:8`) | "Prestação de contas" nos dois lugares. "Relatórios" só na lista de "em breve" | REPROVADO (baixa) |
| Nome do produto | "XAcess" (`JanelaPrincipal.xaml:43`); "XAcess — Painel do evento · Rayzer" (`JanelaPrincipal.xaml:5`); "Rayzer XAcess" (`Bandeja.cs:32`; `BandejaDoSistema.cs:54`; `App.xaml.cs:130`) | Ver D-15 | BLOQUEADO (D-15) |

---

## 6. Textos em outro idioma ou código

Separados aqui para não se misturarem com o português de portaria.

**Inglês (ou palavra estrangeira) no caminho do operador:**

- "display" (visor): `Gemeo.xaml:337`–`355`; `GerenciarCatraca.xaml:93`–`106`; `Textos.cs:108`; `MapaDeGiro.xaml:54`; `Pecas.cs:182`–`202`; `Display2x16.cs:48`–`64`; `PainelAoVivo.xaml:82`; `Acessos.xaml:114`. Proposta: "visor".
- "Setup": `Bandeja.cs:98`. Proposta: "instalação" / "suporte".
- "staff": `Pessoas.xaml:8`. Proposta: "equipe".
- "online" / "offline": `Telas.cs:202`–`211`; `Telas.cs:1260`–`1265`. Proposta: "com internet" / "sem internet".
- "firmware": `App.xaml:130`; `CentralDaCatraca.xaml:85`; `Textos.cs:229`; `Pecas.cs:272`. Proposta: "versão do programa".
- "Excel", "PDF", "CSV", "ZIP": formatos. "Excel" é nome de produto e pode ficar. "CSV" e "ZIP" devem virar "planilha" e "arquivo ZIP".
- Nomes de protocolo e de produto de terceiros: "Wiegand", "Abatrack2", "SmartCard", "WebSocket" e "EasyInner" (`Pecas.cs:255`). "EasyInner" é o nome de um SDK da Topdata visível ao operador, o que conflita com a restrição do docs/46 §9 (nenhum SDK da Topdata no repositório, e não expor o nome no texto). Proposta: tirar do texto.
- "Discovering", "Conectar", "Reconectar" (estados): mapeados em `Textos.cs:31` para "Conectando…". Não aparecem cru, exceto pelo ramo padrão de `Textos.cs:42` (T-023).

**Código ou texto de máquina no caminho do operador:**

- Mensagens de exceção .NET com tipo e texto em inglês: `App.xaml.cs:128` (T-005); `Telas.cs:927` (T-035); `Parametrizacao.cs:1092` (T-036); `GerenciamentoDeCatracasViewModel.cs:112`, `:131` (classe sem uso, P-05).
- Código de comunicação de biblioteca (RPC): `PainelViewModel.cs:87` (T-012).
- Nomes de método e códigos EI-xxx, "Habilita 0 · ExibirMensagem 0", "FUN:33": `Parametrizacao.cs:61`–`148`, `:457`, `:463`; `CentralDaCatraca.cs:190`; `GerenciarCatraca.cs:343`.
- Estados internos em texto: `Textos.cs:42` (ramo padrão, T-023).
- Identificadores de ensaio e de documento: `CentralDaCatraca.cs:41`; `GerenciarCatraca.cs:341`–`345` (T-037).
- Mensagens de desenvolvedor da especificação 3D: `GemeoDigital/Especificacao.cs` (T-102).
- Nome de usuário de automação: `LoginDeAutomacao.cs:56` ("Automação do CI"). Não aparece para o operador normal. Confirmar que não aparece no painel (P-08).
- Logs e saídas de autoteste (`App.xaml.cs:173`, `:188`, `:209`, `:212`, `:223`, `:247`): são linhas de console da captura e do autoteste, não de tela. Não entram na auditoria.

---

## 7. Ocorrências de "Gêmeo" e "Gmail" (busca de texto)

### 7.1 Resumo

| Termo | Onde buscamos | Total de ocorrências | Texto de tela | Texto de runtime (outro) | Identificador, arquivo ou comentário |
|---|---|---|---|---|---|
| Gêmeo (com e sem acento, em maiúsculas e minúsculas) | `src/`, `tests/`, `installer/`, `tools/`, `marca/`, `web/rayzer-ui/src/`, `README.md`, `.github/`, arquivos de solução | 281 | 0 | 2 (`CapturaDeTela.cs:216` e `:392`, ferramenta de captura) | 279 (identificadores, nomes de arquivo, namespaces e comentários) |
| Gmail | Os mesmos locais, mais `bin/` e `obj/` | 0 em código-fonte e instalador | 0 | 0 | 0 (há 4 coincidências de bytes em `System.IO.Compression.Native.dll`, em `bin/`; são falso positivo do runtime .NET) |

Observações:

- Em `installer/` (a pasta citada no pedido como `instalador/`) não há nenhuma ocorrência de "Gêmeo" ou "Gmail". Os arquivos de idioma `installer/wix/tema-pt-BR.wxl` e `installer/wix/Setup.wxs` estão limpos.
- Em `bin/` e `obj/` (build local), a string "gêmeo" aparece duas vezes em UTF-16 em `Desktop.App.dll` (Debug e Release). Vêm das mesmas duas literais de `CapturaDeTela.cs`. Nenhuma string de tela.
- `TestResults/baseline/*.trx` (ignorado pelo git) tem nomes de teste com "Gemeo". Não aparece para o operador. Ao renomear, esses nomes mudam também.
- `.github/workflows/ci.yml:255` tem "gêmeo" só num comentário YAML.

### 7.2 Por que o "Gêmeo" visível na instalação (docs/46 §3) não se confirma no código

O docs/46 §3 diz que o nome antigo "Gêmeo" aparece na instalação, no lugar de "Configuração da catraca". Pela leitura do código-fonte, o item de menu já é "Configuração da catraca" (`GemeoDigitalViewModel.cs:161`, o `Titulo` da tela), e o cabeçalho da tela também (`Gemeo.xaml:28`). Não encontrei nenhuma literal de tela com "Gêmeo" em `src/`.

Três hipóteses para verificar no Windows (P-01):

1. O build instalado é mais antigo do que este código.
2. O texto vem da ferramenta de captura (`CapturaDeTela.cs:216`, nome da janela, que aparece na barra de tarefas ao gerar as capturas).
3. O texto está numa tela que lê o nome de outra fonte (por exemplo, `Rayzer.Design` ou um recurso de idioma não coberto por esta busca).

Não consegui executar o build instalado neste ambiente. Esta parte fica PENDENTE.

### 7.3 Lista completa de ocorrências (código, testes e ferramentas)

Formato: `arquivo | total de ocorrências no arquivo | linha:categoria`. Categorias: **com** = comentário ou documentação; **ide** = identificador, nome de arquivo ou namespace; **tex** = literal de texto pela heurística. Só `CapturaDeTela.cs:216` e `:392` são literais de runtime de fato; `Gemeo.xaml:1`, `GerenciarCatraca.xaml:40` e `Parametrizacao.xaml:31` estão marcados como **tex** por engano (são `x:Class` e nomes de comando de binding).

```
.github/workflows/ci.yml | 1 | 255:com
src/Access.Application/Ingressos/DecisorDeIngresso.cs | 1 | 30:com
src/Access.Infrastructure.SQLite/MapasDeGiro.cs | 1 | 23:com
src/Access.Infrastructure.SQLite/Migrations/010_origem_da_leitura.sql | 1 | 6:com
src/Access.Infrastructure.SQLite/Migrations/017_mapa_de_giro.sql | 1 | 109:com
src/Contracts/Protos/edge_control.proto | 1 | 825:com
src/Desktop.App/App.xaml | 4 | 197:ide, 197:ide, 206:com, 208:com
src/Desktop.App/CapturaDeTela.cs | 44 | 104:ide, 104:ide, 106:ide, 106:ide, 127:com, 183:com, 208:ide, 208:ide, 208:ide, 216:tex, 238:ide, 240:ide, 240:ide, 246:ide, 246:ide, 247:ide, 248:ide, 249:ide, 255:ide, 256:ide, 256:ide, 257:ide, 258:ide, 259:ide, 261:ide, 267:com, 269:ide, 270:ide, 270:ide, 271:ide, 277:ide, 278:ide, 278:ide, 279:ide, 285:ide, 285:ide, 286:ide, 292:ide, 294:ide, 300:ide, 303:ide, 304:ide, 392:tex, 404:com
src/Desktop.App/Conversores.cs | 1 | 118:ide
src/Desktop.App/Telas/CampoDaCatraca.xaml | 2 | 7:ide, 21:com
src/Desktop.App/Telas/CentralDaCatraca.xaml | 3 | 8:ide, 12:ide, 39:com
src/Desktop.App/Telas/CentralDaCatraca.xaml.cs | 1 | 5:com
src/Desktop.App/Telas/Gemeo.xaml | 3 | 1:tex, 5:ide, 9:ide
src/Desktop.App/Telas/Gemeo.xaml.cs | 11 | 10:ide, 15:com, 28:ide, 48:ide, 97:ide, 102:ide, 114:ide, 165:ide, 168:ide, 505:ide, 541:ide
src/Desktop.App/Telas/GerenciarCatraca.xaml | 2 | 36:com, 40:tex
src/Desktop.App/Telas/MapaDeGiro.xaml | 5 | 8:ide, 9:ide, 11:ide, 32:com, 38:com
src/Desktop.App/Telas/MapaDeGiro.xaml.cs | 1 | 6:com
src/Desktop.App/Telas/Parametrizacao.xaml | 4 | 16:com, 29:com, 31:tex, 96:com
src/Desktop.ViewModels/CentralDaCatraca.cs | 6 | 2:ide, 7:com, 30:com, 156:com, 270:com, 319:com
src/Desktop.ViewModels/Desktop.ViewModels.csproj | 3 | 14:com, 14:com, 15:ide
src/Desktop.ViewModels/GemeoDigital/CenaDaCatraca.cs | 1 | 1:ide
src/Desktop.ViewModels/GemeoDigital/ConfiguracaoPorPeca.cs | 2 | 3:ide, 69:com
src/Desktop.ViewModels/GemeoDigital/Display2x16.cs | 1 | 1:ide
src/Desktop.ViewModels/GemeoDigital/Especificacao.cs | 4 | 3:ide, 159:com, 160:com, 328:ide
src/Desktop.ViewModels/GemeoDigital/GemeoDigitalViewModel.cs | 5 | 4:ide, 10:com, 32:ide, 77:ide, 185:ide
src/Desktop.ViewModels/GemeoDigital/GeometriaFit4.cs | 2 | 1:ide, 207:com
src/Desktop.ViewModels/GemeoDigital/Malha.cs | 1 | 1:ide
src/Desktop.ViewModels/GemeoDigital/Pecas.cs | 7 | 1:ide, 3:com, 7:com, 51:com, 56:com, 106:com, 120:com
src/Desktop.ViewModels/GemeoDigital/Roteiros.cs | 2 | 1:ide, 35:com
src/Desktop.ViewModels/GemeoDigital/TraducaoAoVivo.cs | 2 | 3:ide, 14:com
src/Desktop.ViewModels/GemeoDigital/Vistas.cs | 1 | 1:ide
src/Desktop.ViewModels/MapaDeGiro.cs | 13 | 3:ide, 15:com, 130:com, 143:com, 247:com, 252:com, 323:com, 326:com, 326:com, 345:com, 377:com, 382:com, 499:com
src/Desktop.ViewModels/Parametrizacao.cs | 2 | 551:com, 793:com
src/Desktop.ViewModels/Telas.cs | 22 | 1321:ide, 1351:ide, 1399:com, 1401:ide, 1401:ide, 1404:ide, 1404:ide, 1420:com, 1421:com, 1421:com, 1422:ide, 1429:ide, 1429:ide, 1430:ide, 1433:ide, 1436:ide, 1438:ide, 1440:ide, 1444:ide, 1498:com, 1499:ide, 1562:ide
src/Edge.Supervisor/AcompanhamentoDaOperacao.cs | 1 | 66:com
src/Rayzer.Design/Controles.xaml | 2 | 494:ide, 695:ide
tests/Integration/LigacoesDasTelasTests.cs | 8 | 34:ide, 36:ide, 36:ide, 37:ide, 37:ide, 38:ide, 38:ide, 51:ide
tests/Integration/RayzerDesignTests.cs | 1 | 154:com
tests/Integration/TelasTests.cs | 97 | 619:com, 624:ide, 627:ide, 639:ide, 643:ide, 649:ide, 653:ide, 1331:ide, 1333:ide, 1338:ide, 1343:ide, 1346:ide, 1354:ide, 1390:ide, 1392:ide, 1394:ide, 1412:com, 1435:com, 1443:ide, 1443:ide, 1445:ide, 1453:com, 1457:ide, 1459:ide, 1468:ide, 1476:ide, 1478:ide, 1483:ide, 1493:ide, 1501:ide, 1507:ide, 1514:ide, 1526:ide, 1526:ide, 1527:ide, 1527:ide, 1539:ide, 1542:ide, 1544:ide, 1549:ide, 1549:ide, 1550:ide, 1562:ide, 1564:ide, 1569:ide, 1575:ide, 1584:ide, 1592:ide, 1594:ide, 1596:ide, 1596:ide, 1597:ide, 1600:ide, 1632:ide, 1633:ide, 1634:ide, 1635:ide, 1636:ide, 1639:ide, 1655:ide, 1666:ide, 1670:ide, 1725:ide, 1727:ide, 1729:ide, 1749:ide, 1760:ide, 1762:ide, 1783:com, 1788:ide, 1798:ide, 1804:ide, 1808:ide, 1836:com, 1850:com, 1850:com, 1854:ide, 1860:ide, 1861:ide, 1861:ide, 1862:ide, 1863:ide, 1863:ide, 1863:ide, 1865:ide, 1880:ide, 1882:ide, 1886:ide, 1892:ide, 1897:ide, 1914:com, 1918:ide, 1920:ide, 1932:ide, 1933:ide, 1935:com, 1936:ide
tests/Unit/GemeoDigital/CenaDaCatracaTests.cs | 2 | 1:ide, 3:ide
tests/Unit/GemeoDigital/GemeoDigitalTests.cs | 6 | 3:ide, 4:ide, 6:ide, 9:ide, 115:com, 167:com
tests/Unit/GemeoDigital/GeometriaFit4Tests.cs | 2 | 1:ide, 3:ide
tests/Unit/Ingressos/DecisorDeIngressoTests.cs | 1 | 129:com
```

---

## 8. Decisões que o dono precisa tomar (BLOQUEADO)

Cada decisão tem o identificador usado nas linhas acima. Quando o docs/48 já tem uma pergunta equivalente, o número dela está indicado.

| ID | Decisão | Opções | Ocorrências afetadas (principais) | Recomendação do A3 |
|---|---|---|---|---|
| D-01 | Nome do ato de enviar a configuração salva à catraca. O docs/33 §9.1 e o docs/48 C3 já fixam "um Salvar e um Aplicar" | (a) manter "Aplicar"; (b) "Enviar para a catraca"; (c) "Salvar e enviar" | T-025, T-032, T-033, T-069, T-073, T-110, T-136, T-139, T-142 | (b). "Aplicar" não diz o que acontece com a catraca |
| D-02 | Nome único da configuração de uma catraca e do modo lista | (a) "Configuração da catraca" com modo lista; (b) "Configuração" com modo lista; (c) manter dois nomes | Seção 5; T-101, T-138, T-142, T-095 | (a). Um nome e um modo. docs/48 §9.2 |
| D-03 | Nome do "giro" para o operador (aba, título e rótulos) | (a) "Sentido e contagem" (sugerido); (b) "Contagem de entrada e saída" (plano §5); (c) "Passagem" | T-044, T-074, T-075, T-076, T-096, T-103, T-109 | (a). Corta a palavra "giro" do texto do operador. docs/48 §9.3 |
| D-04 | Modo técnico e o que sai da tela do operador (códigos EI, nomes de método, IDs de ensaio, versões em hash, "grupo", "porta") | (a) fica tudo no modo técnico; (b) sai do app e vai para o suporte; (c) manter como está | T-003, T-030, T-062, T-063, T-065, T-079, T-037 | (a) para os nomes de método e códigos; (b) para IDs de ensaio e referências a documentos |
| D-05 | Política para itens "Aguardando confirmação" e "Ainda não disponível": mostrar, esconder ou mostrar com quem libera | (a) esconder de quem não tem permissão (P9); (b) mostrar em uma frase "Indisponível nesta versão" e quem libera; (c) mostrar como está | T-037, T-140; Textos de `Contas.xaml:120`; `GerenciarCatraca.xaml:149`–`157` | (b). Não usar a palavra "bancada" nem IDs |
| D-06 | "Perfil" (de pessoa), "Papel" (de usuário) e "Função" (de peça ou de giro) | (a) Perfil só para pessoa, Papel só para usuário, "Função" sai do texto; (b) outra divisão | T-116, T-132, T-133, T-115, T-100 | (a) |
| D-07 | Palavra para cartão, código ou credencial | (a) "Cartão ou código de acesso"; (b) "Credencial"; (c) "Cartão" | T-100, T-114; Seção 3 ("Credencial / cartão / código"); docs/48 §9.7 (lotes) | (a) |
| D-08 | Texto no visor sem acento ("Acesso nao autorizado") e acentos no visor (firmware) | Depende de teste na bancada | T-082, T-089, T-099 | Não é decisão de front end. Confirmar na bancada. Até lá, manter a mensagem e não prometer acento |
| D-09 | "Origem" (de ingresso, de código ou de leitura) | (a) "Vendido por"; (b) "Canal"; (c) "Origem" | T-053, T-056; Seção 3 | (a) para ingresso; (b) para leitura |
| D-10 | "Categoria" (de ingresso ou de pessoa) | (a) "Tipo de ingresso"; (b) "Tipo de pessoa"; (c) "Categoria" | T-047 | Dar nomes separados para ingresso e para pessoa |
| D-11 | "Serviço local" e "serviço" em textos | (a) "Programa das catracas"; (b) "Central das catracas"; (c) manter | T-001, T-011, T-014, T-018, T-019, T-021, T-029, T-132 | (a) |
| D-12 | "Usos sem passagem" e "estornar" | (a) "Ingressos usados sem entrada confirmada" e "desfazer"; (b) outra formulação | T-045, T-046 | (a) |
| D-13 | "Instalação" (como em "nesta instalação") | (a) "neste computador"; (b) "neste evento"; (c) manter | T-009, T-033, T-058, T-087, T-092, T-109 | Depende do sentido; (a) para o software, (b) para o evento |
| D-14 | Nome do modo de teste e do simulador (plano: "Modo de teste"; hoje: "Simulação", "Modo simulação", "Simulador", "Simulador de catraca") | (a) "Modo de teste" (plano); (b) "Simulação" | T-131, T-141; `Telas.cs:1229`; `JanelaPrincipal.xaml:60` | Escolher um só. Hoje são três nomes |
| D-15 | Nome do produto: "XAcess" ou "Rayzer XAcess" | (a) "XAcess" no app e "Rayzer" só no rodapé; (b) "Rayzer XAcess" em todo lugar | Seção 5 (produto); `JanelaPrincipal.xaml:5`; `Bandeja.cs:32`; `BandejaDoSistema.cs:54` | Decisão de marca. Não é do A3 |
| D-16 | Nome do cadastro (empresas, salas, horários, feriados, perfis) | (a) "Cadastro" (plano e docs/48 C7); (b) "Perfis, horários e empresas" | Seção 5 | (a), com a lista do que tem dentro, como o docs/48 C7 propõe |

Outras decisões do docs/48 que o A3 não repete aqui: §9.1 (aprovar a árvore), §9.4 (pedidos imediatos só em Gerenciar), §9.5 (leituras recusadas), §9.8 (operador vem do login: afeta "Seu nome" em `Configuracoes.xaml:33`, `GerenciarCatraca.xaml:24`, `MapaDeGiro.xaml:34`, `Parametrizacao.xaml:49`).

---

## 9. Pendências de execução visual (PENDENTE)

| ID | Pendência | Como verificar | Ref. |
|---|---|---|---|
| P-01 | Texto "Gêmeo" no build instalado (docs/46 §3) | Instalar o pacote gerado por `installer/publicar.ps1` no Windows e percorrer o menu. Conferir a janela da captura de telas (`CapturaDeTela.cs:216`) | Seção 7.2 |
| P-02 | Quebra e corte dos textos longos (descrições de Pessoas, Contas, Parametrização) | A4 na resolução 1366×768 com DPI 100 e 125 | Seção 4 |
| P-03 | Texto da "Relé 2" (`CentralDaCatraca.cs:41`) aparece na tela da peça? | Abrir a peça do relé 2 no Windows | T-071 |
| P-04 | Campo `Detalhe` de `EstadoDoPainel` ("outbox") é exibido? | Buscar o binding no XAML (nenhum encontrado hoje) e checar em execução | T-010 |
| P-05 | `GerenciamentoDeCatracasViewModel` não é usado por nenhuma tela | Confirmar com A1 e remover se for código morto. Se for usado, auditar os textos de saúde e os emojis | Seção 6 |
| P-06 | Mensagens de `Login.cs:234` e `PorQue.cs:84` vêm do serviço | Ver o texto real no serviço (idioma e termos) | T-016 |
| P-07 | Mensagens de desenvolvedor da especificação 3D (`Especificacao.cs`) | Quebrar o arquivo de forma controlada e ver o que aparece | T-102 |
| P-08 | Nome "Automação do CI" (`LoginDeAutomacao.cs:56`) não aparece para o operador | Confirmar no painel com login de automação | Seção 6 |
| P-09 | Símbolos ✓ × e a fonte do Windows | Renderização no CI visual | `PainelAoVivo.xaml:22`, `:28`; `Contas.xaml:52`, `:56` |
| P-10 | Tooltip da bandeja (texto ao passar o mouse) | Passar o mouse no ícone da bandeja no Windows | T-017, T-018 |
| P-11 | Textos da demonstração do Gêmeo (Roteiros) | Abrir o modo Demonstração e ver cada cenário | T-094 |

---

## 10. Divergências com o docs/46 e outras observações

- **docs/46 §3, "Cartões não reconhecidos":** não existe no código. O que existe é "Usos sem passagem" (`Acessos.xaml:50`) e "Acessos negados" (`PainelAoVivo.xaml:27`). Atualizar o inventário (A1).
- **docs/46 §3, "Parâmetros do cadastro":** o menu mostra "Perfis e horários" (`ParametrosDoCadastro.cs:99`) e o cabeçalho diz "Empresas, horários e perfis" (`ParametrosDoCadastro.xaml:7`). Não existe o texto "Parâmetros do cadastro" no código.
- **docs/46 §3, "15 telas":** o menu tem 14 itens (`Telas.cs:1344`–`1357`, mais o Painel ao vivo). A Parametrização (`Parametrizacao.cs`) e a Central da catraca (`CentralDaCatraca.cs`) não estão no menu.
- **docs/46 §3, "Gêmeo digital":** o item de menu se chama "Configuração da catraca" (`GemeoDigitalViewModel.cs:161`). Não existe item "Gêmeo digital".
- **docs/46 §3, "Configuração da catraca aparece em dois lugares":** são pelo menos oito nomes em três telas (seção 5).
- **docs/46 §5, "Lote de cartões":** não existe na tela. Há "Importar planilha" e "Importações feitas" (`Pessoas.xaml:231`, `:243`).
- **docs/46 §5, "Sessão (não aparece para o operador)":** aparece em "Sessão encerrada" (`Login.cs:183`; `MensagemDeFalha.cs:42`). A premissa do glossário está errada.
- **docs/46 §5, "Tentativa / passagem":** "tentativa" aparece em pelo menos sete textos e "passagem" em mais de dez. Não é só "Acesso".
- **docs/46 §5, "Relógio":** o termo está em pelo menos 12 textos (T-003, T-004, T-024, T-068, T-134).
- **Pasta "instalador/" (citada no pedido de A3, não no docs/46):** o nome real no repositório é `installer/`. Não há ocorrência de "Gêmeo" nem "Gmail" nela.
- **docs/46 §3, Simulador e U07:** confirmado no código. O item só entra no menu com simulação ligada (`Telas.cs:1373`–`1377` (comentário); `Telas.cs:1522`–`1523`, filtro `TelasDoMenu`).
- **docs/48 (A1):** os nomes de grupo `DISPOSITIVOS` (C1) e "Cadastro" (C7) dependem deste documento. Estão em D-16 e na seção 5.
- **Rótulo da barra superior:** a janela principal usa "XAcess" (`JanelaPrincipal.xaml:43`), a bandeja usa "Rayzer XAcess" (`Bandeja.cs:32`). Ver D-15.
- **Erro de grafia:** "Saida liberada" (`MapaDeGiro.cs:141`, T-106) e "Acesso nao autorizado" (display, D-08, sem acento por limitação do visor, a confirmar).
- **Texto com erro de conteúdo:** `Configuracoes.xaml:9` diz "A espera pelo giro da nuvem só muda ao reiniciar o serviço", mas a espera é da catraca, não da nuvem (T-066).
- **Grupo errado:** "Espera pelo giro" está sob o título "Nuvem" (`Configuracoes.xaml:27`–`30`), embora seja um ajuste da catraca (T-066, docs/48 C5).
- **Trecho de tela com número do documento de projeto:** `Gemeo.xaml`, `CentralDaCatraca.cs`, `GerenciarCatraca.cs` e `Pecas.cs` mostram ao operador referências como "docs/21 §8", "D5", "INT-UX-03" e "HIL-DIR-07" (T-037, T-071, T-087). Isso está fora da portaria e deve sair.

---

## 11. Textos verificados e aprovados (amostra)

Estes textos atendem ao P2 e foram lidos literalmente na fonte. Não entram nas tabelas acima.

- Títulos e menu: "Painel ao vivo", "Catracas", "Acessos", "Consultar código", "Sincronização", "Prestação de contas", "Pessoas", "Diagnóstico", "Gerenciar catraca" ("Configurações" e "Perfis e horários" ficam fora por causa da seção 5).
- Ações: "Salvar", "Liberação manual", "Mensagem padrão", "Mensagem temporária" (sem "pedido imediato"), "Por quê?", "Gerenciar", "Ver acessos", "Abrir assistente de configuração", "Sair", "Entrar", "Buscar", "Gravar", "Cancelar", "Fechar".
- Explicação de negativa: "O que aconteceu", "O que dizer à pessoa", "O que fazer" (`App.xaml:80`–`81`; `Acessos.xaml:105`–`111`).
- Relógio de Brasília: "Horário de Brasília" (`JanelaPrincipal.xaml:149`).
- Simulação: "MODO SIMULAÇÃO — sem catraca física" e "Catraca simulada: nenhuma catraca física é acionada." (`App.xaml:118`, `JanelaPrincipal.xaml:60`). O texto está certo; o nome da tela não (D-14).
- Ingresso: "Ingresso" e "Código" (`Consulta.xaml:8`).
- Estados de catraca: "Atendendo", "Desligada", "Parada", "Conectando…", "Configurando…", "Aguardando a catraca conectar", "Operando sozinha, sem o PC", "Com problema — veja o diagnóstico" (`Textos.cs:18`–`41`).
- Pessoas: "Ativa", "Bloqueada", "Inativa", "Atendimento prioritário", "Exige quem recebe (visitante)", "Anfitrião" (`Pessoas.cs:467`–`469`; `ParametrosDoCadastro.xaml:56`; `Pessoas.xaml:134`, `:173`).
- Usuários: "Troque a senha", "Primeiro acesso ou senha redefinida…", "Login (letras sem acento, números, ponto, hífen)" (`JanelaPrincipal.xaml:197`–`199`; `Usuarios.xaml:46`).

---
