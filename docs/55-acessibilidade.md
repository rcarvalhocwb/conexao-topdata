# 55. Acessibilidade e teclado (A7)

**Status:** auditoria estática do XAML e do código por trás das telas. **Nenhum teste com leitor de tela, nenhuma execução no Windows e nenhuma captura foram feitos.** Tudo o que depende disso está marcado como PENDENTE.
**Base:** branch `tarefa/prompt-analise-front-end`, commit `41d2e65`, em 2026-10-09.
**Escopo:** `src/Desktop.App`: `JanelaPrincipal.xaml` (barra lateral, barra superior e login), `JanelaDeNovidades.xaml`, o template do cartão de catraca em `App.xaml` e as 18 telas em `Telas/*.xaml`. `CampoDaCatraca.xaml` é template; `CentralDaCatraca.xaml` e `MapaDeGiro.xaml` são subtelas. A árvore de referência é a do docs/46 §6.
**Fora do escopo:** `Edge.Configurador` (assistente de configuração, aberto pelo botão da barra superior). Os estilos de `Rayzer.Design` entram apenas onde afetam foco e teclado.

Classificação segundo docs/46 §7: APROVADO, PENDENTE, BLOQUEADO, REPROVADO.

---

## 1. Resumo

| Critério | Resultado | Situação |
|---|---|---|
| 1. Controles sem `AutomationProperties.Name` | 0 de 126 campos de entrada (TextBox 71, ComboBox 19, DatePicker 7, PasswordBox 4, DataGrid 22, ListBox 3). Itens do menu recolhido: todos, sem nome. | REPROVADO (menu) |
| 2. Ações importantes sem atalho de teclado | 13 botões (estorno 2, desfazer 2, reconectar 3, aplicar 4, cadastrar 2). | REPROVADO |
| 3. Ordem de tabulação | Nenhum `TabIndex`, nenhum `TabNavigation`: vale a ordem de declaração do XAML. Seis divergências em relação à ordem visual. | REPROVADO |
| 4. Atalho para ações frequentes | Nenhum atalho global. Há atalhos locais para liberar, fechar, cadastrar e reconectar, mas "Refazer a conexão" usa a mesma letra de "Configurar a catraca" na mesma tela. | BLOQUEADO (atalho global) e REPROVADO (chave duplicada) |
| 5. Contraste | Nenhuma cor literal em `Desktop.App`; os pares são tokens `Rayzer.*`. Um par literal está fora do XAML: o texto do display apagado do gêmeo, com razão calculada de 1,1:1. | PENDENTE (tokens) e BLOQUEADO (display apagado) |

Também foram encontrados sete rótulos visíveis que não estão contidos no nome acessível (REPROVADO, WCAG 2.5.3), sete chaves de acesso repetidas numa mesma tela (REPROVADO), e nomes repetidos que não identificam a linha ou a catraca (PENDENTE, depende de leitor de tela).

---

## 2. Matriz por tela

| Tela (arquivo) | 1. Nome | 2. Ação sem atalho | 3. Ordem de Tab | 4. Atalhos | Situação |
|---|---|---|---|---|---|
| Janela principal e barra lateral (`JanelaPrincipal.xaml`) | Menu recolhido: todos os itens sem nome (L87-98) | 0 | Tema e Recolher antes do menu (L63-72) | Nenhum para abrir o painel | REPROVADO |
| Painel ao vivo (`Telas/PainelAoVivo.xaml`) | 0 | 0 | "Por quê?" antes da lista (L62 antes de L88) | Nenhum | REPROVADO (ordem) |
| Catracas e cartão de catraca (`Telas/Catracas.xaml`, `App.xaml` L130-180) | 0 falta; nomes repetidos nos botões do cartão | 0 | Ordem linear | Nenhum para "Gerenciar" | PENDENTE |
| Acessos (`Telas/Acessos.xaml`) | 0 | 2 (estorno) | "Usos sem passagem" e "Por quê?" antes da grade (L50, L94 antes de L120) | Alt+B em Buscar, e Enter pelo IsDefault | REPROVADO |
| Consultar código (`Telas/Consulta.xaml`) | 0 | 0 | Linear | Enter (L15), Alt+C | PENDENTE |
| Sincronização (`Telas/Sincronizacao.xaml`) | 0 | 0 (1 fora da lista) | Linear | Nenhum; "Reenviar" sem atalho | PENDENTE |
| Prestação de contas (`Telas/Contas.xaml`) | 0 | 0 (1 fora da lista) | Linear | Alt+G em Gerar | PENDENTE |
| Diagnóstico (`Telas/Diagnostico.xaml`) | 0 | 0 | Linear | Alt+E | PENDENTE |
| Configuração da catraca, gêmeo (`Telas/Gemeo.xaml`) | 0 | 3 (desfazer 1, aplicar 2) | Coluna esquerda inteira antes da direita (L38-230 antes de L233) | Setas, + e - no desenho (`Gemeo.xaml.cs` L994-1021) | REPROVADO |
| Painel da peça (`Telas/CentralDaCatraca.xaml`) | 0 | 2 (reconectar) | Linear | Nenhum | REPROVADO |
| Campo da catraca (`Telas/CampoDaCatraca.xaml`, template) | "Usar o padrão do evento" repetido em todo campo | 0 | n/a | n/a | PENDENTE |
| Giro desta catraca (`Telas/MapaDeGiro.xaml`) | "Usar o padrão" e outros repetidos por origem | 3 (desfazer 1, aplicar 2) | Linear | Nenhum | REPROVADO |
| Gerenciar catraca (`Telas/GerenciarCatraca.xaml`) | 0 | 1 (confirmar reconexão) | Linear | L, F, A, P, R; C repetido (L39 e L112) | REPROVADO |
| Parametrização (`Telas/Parametrizacao.xaml`) | 0 | 0 | Linear | D e G repetidos na mesma tela | REPROVADO |
| Empresas, horários e perfis (`Telas/ParametrosDoCadastro.xaml`) | 0 | 2 (cadastrar) | Linear | Teclas nas ações de cadastro, exceto "Gravar empresa" e "Gravar sala" | REPROVADO |
| Pessoas (`Telas/Pessoas.xaml`) | 0 | 0 | "Buscar" e "Adicionar" antes do campo de texto (L30, L222 antes de L31, L223) | Alt+N, Alt+B; B, D e C repetidos | REPROVADO |
| Usuários e papéis (`Telas/Usuarios.xaml`) | 0 | 0 | Linear | Alt+N, Alt+G, Alt+R | PENDENTE |
| Simulador (`Telas/Simulador.xaml`) | 0 | 0 | Linear | Enter (L21), Alt+P | PENDENTE |
| Configurações do evento (`Telas/Configuracoes.xaml`) | 0 | 0 | Linear | Alt+S, Alt+A; C repetido (L50 e L51) | REPROVADO |
| Novidades desta versão (`JanelaDeNovidades.xaml`) | 0 | 0 | Linear | Sem IsDefault nem IsCancel (Enter e Esc não fecham) | REPROVADO (nome e teclas) |

---

## 3. Detalhes por critério

### 3.1 Controles sem nome (critério 1)

**Campos de entrada.** Varredura de todo o XAML de `Desktop.App`: todos os TextBox, ComboBox, DatePicker, PasswordBox, DataGrid e ListBox têm `AutomationProperties.Name`. Resultado: **APROVADO** (verificado no código).

**Botões.** Há 103. A varredura não achou nenhum sem nome e sem texto: os 82 sem `AutomationProperties.Name` têm texto em `Content` ou em filhos de texto, e o nome acessível sai dele. Nenhum botão só com ícone foi encontrado nas telas. Resultado: **APROVADO**.

**Itens do menu (REPROVADO).** `JanelaPrincipal.xaml` L87-98: a `ListBox` `Menu` tem um `ListBoxItem` por tela permitida ao papel (`Desktop.ViewModels/Telas.cs` L1522-1524, `TelasDoMenu`: até 15 telas; o Simulador aparece só com simulação ligada, e Usuários depende da permissão). Nenhum item tem `AutomationProperties.Name`. Com a barra aberta, o nome sai do texto do título. Ao recolher, `Menu.Tag` vira vazio (`JanelaPrincipal.xaml.cs` L203) e o título fica `Collapsed` (`JanelaPrincipal.xaml` L93-94). Sobra só o glifo do ícone. O `ToolTip` está no `StackPanel` e não vira nome. Falta o item.

**Contêineres sem nome (não contados).** São listas ou grupos que só agrupam controles que já têm nome: `CampoDaCatraca.xaml:44`, `Gemeo.xaml:346`, `MapaDeGiro.xaml:59`, `Parametrizacao.xaml:85, 88, 91, 94`, `ParametrosDoCadastro.xaml:13` (TabControl), `:44` e `:136`, `Usuarios.xaml:54` e `:104`, `JanelaDeNovidades.xaml:36`.

**Nomes repetidos que não identificam a linha (PENDENTE, leitor de tela).** O nome existe, mas é igual em todas as ocorrências:
- `CampoDaCatraca.xaml:20`: "Usar o padrão do evento", igual em todo campo da Parametrização e do painel da peça.
- `MapaDeGiro.xaml:47, 50, 53, 56`: "Usar o padrão", "Função que libera o giro", "Como o giro conta" e "Texto do giro", repetidos em cada origem.
- `App.xaml:81, 158, 167, 176`: "Por que esta leitura foi negada", "Gerenciar esta catraca", "Ver acessos desta catraca" e "Abrir diagnóstico", repetidos em cada cartão ou linha. O nome não diz qual catraca nem qual linha.

**Rótulo visível fora do nome (REPROVADO, WCAG 2.5.3).** Quem usa comando de voz fala o texto que vê. Quando o texto visível não está contido no nome, o comando não funciona.

| # | Arquivo:linha | Texto visível | Nome acessível |
|---|---|---|---|
| 1 | `JanelaPrincipal.xaml:63-64` | "Tema escuro" ou "Tema claro" (L67) | "Alternar tema claro e escuro" |
| 2 | `JanelaPrincipal.xaml:71-72` | "Recolher menu" ou "Expandir menu" (L75) | "Recolher ou expandir o menu" |
| 3 | `Telas/GerenciarCatraca.xaml:39-41` | "Configurar a catraca" | "Abrir a configuração desta catraca" |
| 4 | `Telas/GerenciarCatraca.xaml:43-45` | "Parametrização (em lista)" | "Parametrização desta catraca" |
| 5 | `Telas/Gemeo.xaml:145-147` | "Ver em lista" | "Ver esta configuração em lista (Parametrização)" |
| 6 | `JanelaDeNovidades.xaml:54-57` | "Entendi" | "Fechar as novidades desta versão" |
| 7 | `App.xaml:75-81` | "Por quê?" | "Por que esta leitura foi negada" |

Não entram: `Parametrizacao.xaml:30`, cujo texto "Abrir a confi_guração" está contido no nome "Abrir a configuração desta catraca".

### 3.2 Ações importantes sem atalho de teclado (critério 2)

Todas as ações abaixo são botões sem `_` no `Content` e sem outra forma de atalho. **REPROVADO** (verificado no código).

| Ação | Arquivo:linha | Texto |
|---|---|---|
| Estornar | `Telas/Acessos.xaml:76` | "Estornar o selecionado…" |
| Estornar | `Telas/Acessos.xaml:83` | "Estornar" |
| Desfazer | `Telas/MapaDeGiro.xaml:126` | "Desfazer" |
| Desfazer | `Telas/Gemeo.xaml:208` | "Desfazer" |
| Reconectar a catraca | `Telas/CentralDaCatraca.xaml:115` | "Refazer a conexão…" |
| Reconectar a catraca | `Telas/CentralDaCatraca.xaml:125` | "Confirmar e refazer" |
| Reconectar a catraca | `Telas/GerenciarCatraca.xaml:121` | "Confirmar e refazer" |
| Aplicar na catraca (reconecta, como avisa `Configuracoes.xaml:9`) | `Telas/Gemeo.xaml:210` | "Aplicar nesta catraca…" |
| Aplicar na catraca | `Telas/Gemeo.xaml:220` | "Aplicar agora" |
| Aplicar na catraca | `Telas/MapaDeGiro.xaml:136` | "Aplicar nesta catraca…" |
| Aplicar na catraca | `Telas/MapaDeGiro.xaml:143` | "Aplicar agora" |
| Cadastrar | `Telas/ParametrosDoCadastro.xaml:190` | "Gravar empresa" |
| Cadastrar | `Telas/ParametrosDoCadastro.xaml:232` | "Gravar sala" |

Total: **13**. Liberar, fechar e excluir têm chave em todos os botões encontrados (`GerenciarCatraca.xaml:70, 80, 140`; `ParametrosDoCadastro.xaml:122, 148`; `Pessoas.xaml:189, 253`).

Outras ações sem chave, fora da lista do docs/46: "Salvar" (`Gemeo.xaml:206`), "Salvar o giro" (`MapaDeGiro.xaml:125`), "Mostrar no display" (`CentralDaCatraca.xaml:109`), "Acertar o relógio agora" (`CentralDaCatraca.xaml:114`), "Reenviar os recusados" (`Sincronizacao.xaml:39`), "Exportar para Excel" (`Contas.xaml:30-32`) e os "Cancelar" de `Acessos.xaml:84`, `Gemeo.xaml:221`, `MapaDeGiro.xaml:144` e `CentralDaCatraca.xaml:126`.

### 3.3 Ordem de tabulação (critério 3)

- `TabIndex`: nenhuma ocorrência em `Desktop.App`. `TabNavigation` e `IsTabStop`: nenhuma. A única configuração direcional é `KeyboardNavigation.DirectionalNavigation` nas listas (`Rayzer.Design/Controles.xaml` L700-712 e L774-780). Logo, a ordem de Tab é a ordem de declaração no XAML.
- Divergências entre a ordem de declaração e a ordem visual (**REPROVADO**, por código; confirmar com teste de teclado):
  - a. `JanelaPrincipal.xaml` L63 e L71 (Tema e Recolher, no rodapé visual da barra) vêm antes do menu (L87).
  - b. `Telas/PainelAoVivo.xaml` L62 ("Por quê?", à direita) vem antes da lista (L88).
  - c. `Telas/Acessos.xaml` L50 ("Usos sem passagem", embaixo) e L94 ("Por quê?", à direita) vêm antes da grade principal (L120).
  - d. `Telas/Gemeo.xaml` L38-230 (coluna esquerda: desenho e configuração) vem inteira antes de `ColunaDireita` (L233). O seletor "Catraca" (L243) e a lista "Peças da catraca" (L271) só são alcançados depois de toda a coluna da esquerda.
  - e. `Telas/Pessoas.xaml` L30 ("Buscar") vem antes do campo "Texto da busca" (L31). O foco chega ao botão antes de o operador chegar ao campo.
  - f. `Telas/Pessoas.xaml` L222 ("Adicionar") vem antes do campo "Código da nova credencial" (L223).
- Duas listas com formulário ao lado (`Usuarios.xaml`, `Pessoas.xaml`) percorrem a coluna da esquerda antes da direita. Isso é a leitura esperada de esquerda para direita, e não foi contado como divergência.
- Demais telas: a ordem de declaração coincide com a ordem visual. **PENDENTE**: confirmar com teclado.
- **Foco inicial e login (PENDENTE).** `JanelaPrincipal.xaml.cs` L96 põe o foco no menu ao carregar, e L105 leva o foco ao campo de login quando o sistema pede login. A grade de login (`JanelaPrincipal.xaml` L181) é o último filho da janela, então, pela ordem de Tab, os controles que ficam atrás dela vêm antes dos campos de login. Só um teste mostra o que acontece.

### 3.4 Atalhos para as ações frequentes do operador (critério 4)

| Ação frequente | Onde está | Atalho hoje | Situação |
|---|---|---|---|
| Abrir o evento (ver o painel ao vivo) | Menu, `JanelaPrincipal.xaml` L87 | Nenhum. Não há `KeyBinding` na janela e não há `KeyDown` em `JanelaPrincipal.xaml.cs`. O menu anda só por Tab e setas. | REPROVADO |
| Liberar um giro | `GerenciarCatraca.xaml` L70 | Alt+L, mas só dentro da tela Gerenciar catraca | BLOQUEADO (atalho global, decisão do dono). Chave local: APROVADO |
| Cadastrar pessoa | `Pessoas.xaml` L25 | Alt+N, só dentro da tela Pessoas | BLOQUEADO (atalho global). Chave local: APROVADO |
| Fechar catraca | `GerenciarCatraca.xaml` L140 | Alt+F, só dentro da tela | BLOQUEADO (atalho global). Chave local: APROVADO |
| Reconectar catraca | `GerenciarCatraca.xaml` L112 | Alt+C, mas a letra C também está em "Configurar a catraca" (L39), visível ao mesmo tempo | REPROVADO (chave duplicada) |
| Abrir o assistente | `JanelaPrincipal.xaml` L143 | Alt+A, quando o botão aparece | APROVADO (código) |
| Passar código de teste | `Telas/Simulador.xaml` L21 e L30 | Enter e Alt+P | APROVADO (código) |
| Consultar código | `Telas/Consulta.xaml` L15 e L18 | Enter e Alt+C | APROVADO (código) |

**Atalhos globais: nenhum.** Os únicos `KeyBinding` do projeto são Enter em `Consulta.xaml` L15 e `Simulador.xaml` L21.

**Chaves repetidas numa mesma tela (REPROVADO).** O WPF não tem como saber qual dos dois botões a tecla deve acionar, e o comportamento real depende do Windows.
- `GerenciarCatraca.xaml`: C em L39 ("Configurar a catraca") e L112 ("Refazer a conexão"), visíveis juntos. Também em L80 e L82, no bloco de confirmação.
- `Parametrizacao.xaml`: D em L90 (aba "Display") e L129 ("Desfazer"). G em L30 ("Abrir a configuração") e L97 (aba "Giro").
- `Pessoas.xaml`: B em L30 ("Buscar") e L187 ("Bloquear"). D em L188 ("Desbloquear") e L253 ("Desfazer o lote"). C em L235 ("Escolher arquivo e conferir") e L236 ("Aplicar importação").
- `Configuracoes.xaml`: C em L50 ("Confirmar a aplicação") e L51 ("Cancelar"), no mesmo bloco.
- Não contado: `ParametrosDoCadastro.xaml` usa G em duas abas ("Gravar perfil" e "Gravar tabela"). Só uma aba fica visível por vez.

**Atalhos que já existem (APROVADO, por código):**
- Enter: `Consulta.xaml` L15 e `Simulador.xaml` L21.
- Botão padrão (IsDefault, Enter aciona): `Acessos.xaml` L42, `Pessoas.xaml` L30, `Contas.xaml` L29, `Simulador.xaml` L30, `Configuracoes.xaml` L37, `JanelaPrincipal.xaml` L193 e L214.
- Desenho 3D, com foco no palco (`Gemeo.xaml` L88-90): setas giram, + e - aproximam e afastam (`Gemeo.xaml.cs` L994-1021). O texto de ajuda aponta a lista de peças como alternativa.

**Novidades (REPROVADO, baixo impacto).** `JanelaDeNovidades.xaml` L54-57 não tem `IsDefault` nem `IsCancel`. Enter e Esc não fecham a janela.

### 3.5 Contraste (critério 5)

- **Cores literais em `Desktop.App`: zero.** Busca por `#` com seis dígitos e por nomes de cor em `.xaml` e `.cs`. Os `Brush` de `Gemeo.xaml.cs` servem ao modelo 3D e ao display, e vêm de `Desktop.ViewModels/GemeoDigital/fit4.json`.
- **Pares de tokens (PENDENTE, tema renderizado).** Os pares dependem dos temas claro, escuro e alto contraste, e só se medem com a tela renderizada. Os de maior risco:
  - `Rayzer.Nav.Text.Muted` sobre `Rayzer.Nav.Background` (`JanelaPrincipal.xaml` L44 e L80).
  - `Rayzer.Text.Primary` (padrão do texto de `Rayzer.Type.Body`) sobre `Rayzer.Warning.Subtle` (`GerenciarCatraca.xaml` L74-78; `Configuracoes.xaml` L44-46).
  - `Rayzer.Text.OnBrand` sobre `Rayzer.Brand.Primary` (`Gemeo.xaml` L339-343, prévia do display).
  - Texto de `Rayzer.Type.Mono` sobre `Rayzer.Surface.Sunken` (`Diagnostico.xaml` L71-76, últimas linhas dos programas).
- **Display do gêmeo (texto literal sobre fundo literal, em `fit4.json` L121-124):**
  - Aceso: texto `#E6F2FF` sobre `#104EA8`, razão calculada de **6,9:1**. Passa de 4,5:1. A renderização 3D pode mudar o valor: **PENDENTE**.
  - Apagado: texto `#101824` sobre `#060C16`, razão calculada de **1,1:1**. O código desenha as letras com a cor de texto apagado quando a luz do display está apagada (`Gemeo.xaml.cs` L590-591). É **BLOQUEADO**: depende de decisão do dono sobre se o texto de display apagado precisa ser legível.

### 3.6 Foco visível (apoio ao critério 3)

Os estilos de `Rayzer.Design/Controles.xaml` tiram o foco padrão (`FocusVisualStyle` nulo) e põem um indicador próprio com `IsKeyboardFocused` ou `IsKeyboardFocusWithin`, usando `Rayzer.Focus`. Verificado para botões, campos, caixas de marcar, rádios, abas e itens de lista e do menu.

- **DataGridCell (PENDENTE):** `Controles.xaml` L679-682 põe `FocusVisualStyle` nulo e não tem gatilho de foco. Se a seleção de linha não mostrar a célula com foco, o operador não vê onde está. Testar nas grades de Pessoas, Acessos e Usuários.

---

## 4. Pendências e decisões

**BLOQUEADO (decisão do dono):**
1. Esquema de atalhos globais: abrir o painel, liberar, cadastrar pessoa, fechar e reconectar catraca. Qual combinação e se vale um atalho por tela.
2. Legibilidade do texto do display apagado do gêmeo (1,1:1).

**PENDENTE (teste no Windows, não executado):**
- Leitor de tela (Narrator ou NVDA) em cada tela: nome de cada controle, rótulo contido no nome, nomes repetidos listados em 3.1.
- Teste de teclado por tela: ordem de Tab (3.3), foco inicial e login, sobreposição do login.
- Comportamento real das chaves repetidas (3.4): qual botão recebe o foco ao pressionar Alt+C, Alt+B, Alt+D e Alt+G.
- Contraste renderizado nos três temas (3.5).
- Indicador de foco nas células das grades (3.6).

**Fora do escopo, encaminhado:**
- `Usuarios.xaml` L50 ("Senha inicial") e L67 ("Senha provisória") são `TextBox`, então a senha aparece em texto claro na tela. Encaminhar ao A8 (revisão adversarial) e à segurança.
- `Edge.Configurador` (assistente de configuração) não foi auditado.
- `CentralDaCatraca.xaml` e `MapaDeGiro.xaml` repetem o botão "Aplicar" de `Gemeo.xaml`. A duplicação é do A1 (P1).

## 5. Mudanças de código sugeridas (para o docs/52)

Nada foi alterado em código. Os itens REPROVADOS que pedem mudança entram no docs/52 como lista priorizada: nome dos itens do menu recolhido (1 problema, em todos os itens), rótulos fora do nome (7), chaves repetidas (7 conflitos em 4 telas), ações sem atalho (13), ordem de tabulação (6 divergências) e a janela de novidades sem IsDefault e IsCancel.
