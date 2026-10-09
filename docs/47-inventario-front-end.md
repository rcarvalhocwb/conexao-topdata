# 47. Inventário do front end (E1)

**Status:** entregável E1 do plano `docs/46-prompt-analise-front-end.md`, feito pelo agente A1 (arquiteto de
informação). Só leitura de código: nenhum `.cs`, `.xaml` ou `.proto` foi alterado.

**Base:** branch `tarefa/prompt-analise-front-end`, commit `3ad6db3`. Arquivos lidos: `src/Desktop.ViewModels/*.cs`
(inclusive `GemeoDigital/`), `src/Desktop.App/*.xaml`, `src/Desktop.App/Telas/*.xaml`, `src/Desktop.App/Conversores.cs`,
`src/Desktop.App/BandejaDoSistema.cs`, `src/Edge.Configurador/JanelaDoAssistente.xaml`,
`src/Access.Domain/Usuarios/Permissoes.cs`, `src/Edge.Supervisor/InterceptadorDeSessao.cs`,
`src/Access.Infrastructure.SQLite/Migrations/020_usuarios_do_sistema.sql` e `022_permissoes_de_pessoas.sql`,
`installer/wix/ConexaoTopdata.wxs`.

**Classificação usada** (docs/46 §7):

- **APROVADO:** verificado no código (arquivo e linha citados).
- **PENDENTE:** depende de execução visual (WPF no Windows, build instalado).
- **BLOQUEADO:** depende de decisão do dono do produto.
- **REPROVADO:** verificado e não atende ao princípio citado.

Neste inventário, APROVADO quer dizer "a tela existe, o título é este e a regra de menu é esta, conferido no código".
**Nada aqui foi visto rodando.** A aparência de cada tela é PENDENTE até a execução no Windows (A4, A5).

---

## 1. Resumo em números

| Item | Quantidade | Situação |
|---|---|---|
| Itens no menu lateral (lista `Telas` da `JanelaViewModel`) | **14** (o docs/46 §3 diz 15) | APROVADO — `Telas.cs:1342-1358` |
| Telas abertas fora do menu, dentro da janela principal | 1 (Parametrização da catraca) | APROVADO — `Telas.cs:1391-1419`, `App.xaml:205` |
| Painéis reutilizáveis dentro de telas (Configuração desta catraca, Giro desta catraca, Por quê?, Usos sem passagem) | 4 | APROVADO |
| Subtelas por aba ou cartão (Ficha de pessoa, Ficha de usuário, Papéis, Perfil, Tabela de horário, Feriado, Empresa, Sala…) | 11 | APROVADO |
| Telas e janelas de entrada (janela principal, login, troca de senha, novidades, bandeja) | 5 | APROVADO |
| Assistente de configuração (programa à parte, 4 passos) | 4 | APROVADO |
| Funções que só existem na bandeja (iniciar, encerrar a operação, fechar o painel) | 3 | APROVADO |
| **Total de linhas no inventário (seção 3: 5 + 14 + 15 + 1 + 4 + 3)** | **42** | — |
| Funções citadas no docs/46 que **não existem** no código | 6 | APROVADO (verificada a ausência) |
| ViewModels sem tela (código órfão) | 2 | APROVADO |

---

## 2. O menu de hoje, na ordem exata

Fonte: `src/Desktop.ViewModels/Telas.cs`. A ordem vem da lista `Telas` (linhas 1342-1358). O texto do item é a
propriedade `Titulo` de cada ViewModel (o `DataTemplate` do menu liga `Text="{Binding Titulo}"`,
`JanelaPrincipal.xaml:93`). A permissão vem do dicionário `PermissaoDaTela` (linhas 1312-1328).

Regra de visibilidade (`TelasDoMenu`, linhas 1522-1528): o item aparece se (a) o usuário está logado **e** tem a
permissão da tela, e (b) para o Simulador, o serviço está em modo simulação (regra U07, linhas 1373-1387). Sem sessão
(testes e ferramentas), aparecem todas menos Usuários.

| Ordem | Texto no menu (`Titulo`) | ViewModel | Permissão no menu (`CodigosDePermissao` → código) | Regra extra |
|---|---|---|---|---|
| 1 | `Painel ao vivo` | `PainelAoVivoViewModel` | `OperacaoVer` → `operacao.ver` | tela inicial (`_telaAtual = Painel`, linha 1359) |
| 2 | `Catracas` | `CatracasViewModel` | `OperacaoVer` → `operacao.ver` | — |
| 3 | `Acessos` | `AcessosViewModel` | `OperacaoVer` → `operacao.ver` | — |
| 4 | `Consultar código` | `ConsultaViewModel` | `CodigosConsultar` → `codigos.consultar` | — |
| 5 | `Sincronização` | `SincronizacaoViewModel` | `OperacaoVer` → `operacao.ver` | — |
| 6 | `Prestação de contas` | `ContasViewModel` | `RelatoriosVer` → `relatorios.ver` | — |
| 7 | `Gerenciar catraca` | `GerenciarCatracaViewModel` | `OperacaoVer` → `operacao.ver` | — |
| 8 | `Configuração da catraca` | `GemeoDigitalViewModel` | `OperacaoVer` → `operacao.ver` | — |
| 9 | `Configurações` | `ConfiguracoesViewModel` | `OperacaoVer` → `operacao.ver` | — |
| 10 | `Diagnóstico` | `DiagnosticoViewModel` | `DiagnosticoVer` → `diagnostico.ver` | — |
| 11 | `Simulador` | `SimuladorViewModel` | `SimuladorUsar` → `simulador.usar` | só com `Painel.Estado.Simulacao` (U07) |
| 12 | `Pessoas` | `PessoasViewModel` | `PessoasVer` → `pessoas.ver` | — |
| 13 | `Perfis e horários` | `ParametrosDoCadastroViewModel` | `CadastroParametros` → `cadastro.parametros` | — |
| 14 | `Usuários` | `UsuariosViewModel` | `UsuariosGerenciar` → `usuarios.gerenciar` | sem sessão, nunca aparece |

Os códigos são os mesmos em `CodigosDePermissao` (`src/Desktop.ViewModels/Login.cs:11-31`) e em `Permissoes`
(`src/Access.Domain/Usuarios/Permissoes.cs:18-35`): 18 códigos, todos iguais. APROVADO.

O menu é uma lista plana de 14 itens, sem grupo nem separador (`JanelaPrincipal.xaml:87-98`). APROVADO.

---

## 3. Inventário

Colunas: **Tela** (nome de trabalho) · **Título exibido** (texto exato do código; "menu" é o `Titulo` da ViewModel,
"cabeçalho" é o `RayzerPageHeader`/`RayzerSectionHeader` da tela) · **Menu hoje** · **Permissão** (para ver; e, entre
parênteses, a que cada ação exige no serviço, conforme `InterceptadorDeSessao.Exigida`) · **Dado principal** ·
**ViewModel** · **XAML** · **Sit.** (classificação).

Caminhos: `VM/` = `src/Desktop.ViewModels/`; `XA/` = `src/Desktop.App/`; `TE/` = `src/Desktop.App/Telas/`.

### 3.1 Janela, entrada e saída

| # | Tela | Título exibido | Menu hoje | Permissão | Dado principal | ViewModel | XAML | Sit. |
|---|---|---|---|---|---|---|---|---|
| 1 | Janela principal (moldura) | Janela: `XAcess — Painel do evento · Rayzer`. Barra superior: cartões `Serviço local`, `Catracas`, `Nuvem`, `Modo simulação`; relógio `Horário de Brasília`; nome do usuário e `_Sair` | — (contém o menu) | logado | estado do serviço, catracas online, nuvem, simulação, hora do evento, frase da situação | `VM/Telas.cs:1307` `JanelaViewModel` + `PainelAoVivoViewModel` (cabeçalho) | `XA/JanelaPrincipal.xaml` | APROVADO |
| 2 | Login — Entrar | `Entrar` (campos `Usuário`, `Senha`; botão `_Entrar`) | cobre a janela antes do login | nenhuma (`Entrar` é anônima) | usuário e senha | `VM/Login.cs:53` `SessaoDoUsuarioViewModel` | `XA/JanelaPrincipal.xaml:181-194` | APROVADO |
| 3 | Login — Troca de senha obrigatória | `Troque a senha`; no primeiro acesso do `admin` pede também `Nome do administrador` e `Login do administrador`; botão `_Trocar a senha e entrar` | cobre a janela | logado com senha padrão ou redefinida (só `TrocarSenha`, `ObterSessao`, `Sair`) | senha atual, nova, confirmação | `VM/Login.cs:214` (`TrocarSenhaAsync`) | `XA/JanelaPrincipal.xaml:196-215` | APROVADO |
| 4 | Novidades | Janela: `Novidades desta versão · Rayzer XAcess`; título `Novidades desta versão`; botão `Entendi` | janela modal, abre **uma vez** depois de atualização (`MostrarNovidadesUmaVez`, `JanelaPrincipal.xaml.cs:160`) | nenhuma | 6 itens do que mudou para o operador | `VM/Novidades.cs`, `VM/NovidadesViewModel.cs` | `XA/JanelaDeNovidades.xaml` | APROVADO |
| 5 | Bandeja do Windows | Dica `Rayzer XAcess · catracas …`; menu: `Abrir o painel`, `Gerenciar catraca`, `Iniciar a operação`, `Encerrar a operação (parar as catracas)…`, `Fechar o painel (as catracas continuam)` | ícone perto do relógio | `ObterEstado` e `ListarEquipamentos` são anônimas | catracas atendendo, avisos de queda | `VM/Bandeja.cs` `ResumoDaBandeja` | `XA/BandejaDoSistema.cs:38-49` (WinForms, sem XAML) | APROVADO |

### 3.2 Telas do menu

| # | Tela | Título exibido | Menu hoje | Permissão | Dado principal | ViewModel | XAML | Sit. |
|---|---|---|---|---|---|---|---|---|
| 6 | Painel ao vivo | menu `Painel ao vivo`; cabeçalho `Painel ao vivo`; seções `Catracas`, `Acessos em tempo real` | 1º | `operacao.ver` | cartões das catracas (situação, último evento, firmware, grupo, porta, reconexões, relógio) e os 100 últimos acessos ao vivo | `VM/Telas.cs:101` `PainelAoVivoViewModel` | `TE/PainelAoVivo.xaml` | APROVADO |
| 7 | Catracas | menu `Catracas`; cabeçalho `Catracas` | 2º | `operacao.ver` (`ListarEquipamentos` não exige) | os mesmos cartões de catraca do Painel (`Cartao.Catraca`, `App.xaml:92`), com ações `Gerenciar`, `Ver acessos`, `Diagnóstico` | `VM/Telas.cs:405` `CatracasViewModel` | `TE/Catracas.xaml` | APROVADO |
| 8 | Acessos | menu `Acessos`; cabeçalho `Acessos` | 3º | `operacao.ver` (estorno: `acessos.estornar`) | histórico filtrável (catraca, `Todos`/`Liberados`/`Negados`, categoria, período), até 500 linhas | `VM/Telas.cs:456` `AcessosViewModel` | `TE/Acessos.xaml` | APROVADO |
| 9 | Consultar código | menu `Consultar código`; cabeçalho `Consultar código`; seção `Últimas tentativas com este código` | 4º | `codigos.consultar` | situação de um código (origem, categoria, situação, usos, último uso) e histórico dele | `VM/Telas.cs:560` `ConsultaViewModel` | `TE/Consulta.xaml` | APROVADO |
| 10 | Sincronização | menu `Sincronização`; cabeçalho `Sincronização`; seção `Origens dos códigos`; botão `Reenviar os recusados` | 5º | `operacao.ver` (reenviar: `sincronizacao.operar`) | nuvem configurada, última sincronização, aguardando envio, recusados pela nuvem, origens dos códigos | `VM/Telas.cs:645` `SincronizacaoViewModel` | `TE/Sincronizacao.xaml` | APROVADO |
| 11 | Prestação de contas | menu `Prestação de contas`; cabeçalho `Prestação de contas`; seções `Por categoria`, `Por catraca`, `Por hora`, `Principais motivos de negação`, `Ainda não disponível` | 6º | `relatorios.ver` | liberados, com giro, negados, entradas e saídas do período; CSV | `VM/Telas.cs:742` `ContasViewModel` | `TE/Contas.xaml` | APROVADO |
| 12 | Gerenciar catraca | menu `Gerenciar catraca`; cabeçalho `Gerenciar catraca`; seções `Liberação manual`, `Mensagem no display`, `Manutenção`, `Fechar a catraca`, `Ainda não disponível`, `Histórico de pedidos desta catraca` | 7º | `operacao.ver` (liberar, mensagem, relógio, reconectar: `catraca.comandar`; fechar/reabrir: `catraca.fechar`) | uma catraca escolhida numa lista; pedidos e o histórico deles | `VM/GerenciarCatraca.cs:48` `GerenciarCatracaViewModel` | `TE/GerenciarCatraca.xaml` | APROVADO |
| 13 | Configuração da catraca (antigo "Gêmeo digital") | menu `Configuração da catraca`; cabeçalho `Configuração da catraca`; seções `Configuração desta catraca`, `Peças`, `Cenários`, `Como a mensagem aparece no display`; modos `_Demonstração` / `_Ao vivo` | 8º | `operacao.ver` (salvar: `catraca.configurar`; aplicar e pedidos imediatos: `catraca.comandar`; "Na simulada": `simulador.usar`) | desenho 3D da catraca, peças, cenários de demonstração e a configuração real da catraca escolhida | `VM/GemeoDigital/GemeoDigitalViewModel.cs:32` `GemeoDigitalViewModel` | `TE/Gemeo.xaml` | APROVADO |
| 14 | Configurações | menu `Configurações`; cabeçalho `Configurações do evento`; seções `Catracas`, `Nuvem`; botões `_Salvar`, `_Aplicar agora nas catracas…` | 9º | `operacao.ver` (salvar: `configuracao.editar`; aplicar: `catraca.comandar`) | padrão do evento para todas as catracas (mensagem, tempo liberada, leitor da urna, tipo de leitor) e espera pelo giro da nuvem | `VM/Telas.cs:955` `ConfiguracoesViewModel` | `TE/Configuracoes.xaml` | APROVADO |
| 15 | Diagnóstico | menu `Diagnóstico`; cabeçalho `Diagnóstico`; seção `Camada inteligente (Analisador)`; botão `_Exportar pacote de diagnóstico` | 10º | `diagnostico.ver` | versão, pasta de dados, últimas linhas dos programas das catracas, saúde do Analisador; pacote zip | `VM/Telas.cs:1122` `DiagnosticoViewModel` | `TE/Diagnostico.xaml` | APROVADO |
| 16 | Simulador | menu `Simulador`; cabeçalho `Simulador de catraca`; seções `Últimas tentativas nesta catraca`, `Códigos de teste` | 11º, **só com modo simulação** | `simulador.usar` | passar um código fictício numa catraca simulada e ver o resultado | `VM/Telas.cs:1214` `SimuladorViewModel` | `TE/Simulador.xaml` | APROVADO |
| 17 | Pessoas | menu `Pessoas`; cabeçalho `Pessoas`; seções `Buscar`, ficha, `Situação`, `Credenciais`, `Importar planilha` | 12º | `pessoas.ver` (gravar e credencial: `pessoas.editar`; bloquear: `pessoas.bloquear`; importar: `pessoas.importar`; dado completo: `pessoas.ver_dados`) | busca de pessoas (nome, perfil, empresa, situação, vale até) e a ficha da escolhida | `VM/Pessoas.cs:30` `PessoasViewModel` | `TE/Pessoas.xaml` | APROVADO |
| 18 | Perfis e horários | menu `Perfis e horários`; cabeçalho **`Empresas, horários e perfis`**; abas `Perfis`, `Horários e feriados`, `Empresas e salas` | 13º | `cadastro.parametros` (`ObterParametrosDoCadastro` só exige login) | perfis, tabelas de horário, feriados, empresas e salas | `VM/ParametrosDoCadastro.cs:38` `ParametrosDoCadastroViewModel` | `TE/ParametrosDoCadastro.xaml` | APROVADO |
| 19 | Usuários | menu `Usuários`; cabeçalho **`Usuários e papéis`**; seções `Usuários`, `Papéis` | 14º | `usuarios.gerenciar` | usuários (nome, login, papéis, situação) e papéis (origem, permissões) | `VM/Usuarios.cs:42` `UsuariosViewModel` | `TE/Usuarios.xaml` | APROVADO |

### 3.3 Painéis e subtelas dentro de telas

| # | Tela | Título exibido | Onde aparece hoje | Permissão | Dado principal | ViewModel | XAML | Sit. |
|---|---|---|---|---|---|---|---|---|
| 20 | Configuração desta catraca | cartão `Configuração desta catraca` (`Gemeo.xaml:149`) + painel da peça (`TituloDoPainel`: nome da peça ou `Escolha uma peça`); aviso `Configuração real desta catraca — vale depois de Aplicar.`; botões `Salvar`, `Desfazer`, `Aplicar nesta catraca…`, `Ver em lista` | dentro de **Configuração da catraca** (nº 13). **Não é item de menu.** O `Titulo` da VM (`Configuração desta catraca`) não aparece em menu | como nº 13 | campos da peça clicada, "o que muda", firmware e relógio (coluna), mensagem temporária (display), acertar relógio e refazer conexão | `VM/CentralDaCatraca.cs:34` `CentralDaCatracaViewModel` (junta `ParametrizacaoViewModel`, `MapaDeGiroViewModel` e `GerenciarCatracaViewModel`) | `TE/CentralDaCatraca.xaml`, `TE/CampoDaCatraca.xaml` | APROVADO |
| 21 | Giro desta catraca (mapa de giro) | sem cabeçalho próprio; `AutomationProperties.Name="Giro desta catraca"`; seção `O que muda (atual → novo)` | (a) painel dos braços ou da urna em Configuração da catraca, **sem** salvar próprio; (b) aba `_Giro` da Parametrização, **com** salvar e aplicar próprios | `operacao.ver` (gravar e conferir: `catraca.configurar`) | para cada origem: lado que o braço libera, conta como entrada/saída, texto no display; conferência depois de aplicar | `VM/MapaDeGiro.cs:268` `MapaDeGiroViewModel` | `TE/MapaDeGiro.xaml` | APROVADO |
| 22 | Por quê? | `Por quê?` (painel lateral de 360 px) | dentro de Painel ao vivo e de Acessos, botão em cada acesso negado | `operacao.ver` (`ExplicarNegativa`) | o que aconteceu, o que dizer à pessoa, o que fazer | `VM/PorQue.cs:21` `PainelPorQue` | `TE/PainelAoVivo.xaml`, `TE/Acessos.xaml:92-110` | APROVADO |
| 23 | Usos sem passagem | `Usos sem passagem (N): consumidos sem giro confirmado`; botões `Estornar o selecionado…`, `Estornar`, `Cancelar` | dentro de Acessos, só quando há algum | `operacao.ver` (estornar: `acessos.estornar`) | ingressos consumidos sem giro; estorno com motivo e confirmação | `VM/UsosSemPassagem.cs:36` `PainelDeUsosSemPassagem` | `TE/Acessos.xaml:48-90` | APROVADO |
| 24 | Ficha de pessoa | `Nova pessoa` ou `Ficha: <nome>`; botão `_Gravar a ficha` | cartão dentro de Pessoas (não é tela separada) | `pessoas.ver`; gravar: `pessoas.editar` | perfil, nome, documento (mascarado sem `pessoas.ver_dados`), contato, empresa, sala, validade, horário, observação | `VM/Pessoas.cs:349` (`TituloDaFicha`) | `TE/Pessoas.xaml:57-175` | APROVADO |
| 25 | Situação da pessoa | `Situação`; botões `_Bloquear`, `_Desbloquear`, `_Inativar` | cartão dentro de Pessoas | `pessoas.bloquear` | situação e motivo | `VM/Pessoas.cs` | `TE/Pessoas.xaml:182-190` | APROVADO |
| 26 | Credenciais da pessoa | `Credenciais`; botões `_Perdida`, `Bl_oquear`, `De_volvida`, `_Reativar`, `_Adicionar` | cartão dentro de Pessoas | `pessoas.editar` / `pessoas.bloquear` | cartão, QR ou senha, só mascarado | `VM/Pessoas.cs` | `TE/Pessoas.xaml:198-225` | APROVADO |
| 27 | Importar planilha de pessoas (lotes) | `Importar planilha`; `Importações feitas`; botões `Escolher arquivo e _conferir…`, `Apli_car importação`, `_Desfazer o lote escolhido` | cartão dentro de Pessoas | `pessoas.importar` | prévia da planilha e lotes aplicados (são lotes **de pessoas**, não de cartões) | `VM/Pessoas.cs:153-247` | `TE/Pessoas.xaml:231-255` | APROVADO |
| 28 | Perfil | aba `Perfis`; `Novo perfil` ou `Perfil: <nome>`; botão `_Gravar perfil` | aba dentro de Perfis e horários | `cadastro.parametros` | campos obrigatórios, validade, entradas por dia, tabela de horário, catracas permitidas | `VM/ParametrosDoCadastro.cs:261` | `TE/ParametrosDoCadastro.xaml:15-83` | APROVADO |
| 29 | Tabela de horário | aba `Horários e feriados`; `Tabelas de horário (até 100)`; `Nova tabela de horário` ou `Tabela <n>: <nome>` | aba dentro de Perfis e horários | `cadastro.parametros` | faixas de cada dia (até 4) | `VM/ParametrosDoCadastro.cs:212` | `TE/ParametrosDoCadastro.xaml:85-155` | APROVADO |
| 30 | Feriado | `Feriados (usam a linha Feriado da tabela)`; botões `_Marcar feriado`, `_Desmarcar o selecionado` | mesma aba de Horários | `cadastro.parametros` | dia e nome | `VM/ParametrosDoCadastro.cs` | `TE/ParametrosDoCadastro.xaml:111-123` | APROVADO |
| 31 | Empresa | aba `Empresas e salas`; `Nova empresa` ou `Empresa: <nome>` | aba dentro de Perfis e horários | `cadastro.parametros` | nome, CNPJ, ativa | `VM/ParametrosDoCadastro.cs:151` | `TE/ParametrosDoCadastro.xaml:158-192` | APROVADO |
| 32 | Sala | `Salas e unidades`; `Nova sala ou unidade` ou `Sala: <nome>` | mesma aba de Empresas | `cadastro.parametros` | nome, empresa, andar, bloco | `VM/ParametrosDoCadastro.cs:173` | `TE/ParametrosDoCadastro.xaml:195-235` | APROVADO |
| 33 | Ficha de usuário | `Novo usuário` ou `Usuário: <nome>`; botões `_Gravar usuário`, `_Redefinir a senha` | coluna esquerda de Usuários | `usuarios.gerenciar` | nome, login, senha inicial, ativo, papéis; senha provisória | `VM/Usuarios.cs:140` | `TE/Usuarios.xaml:41-72` | APROVADO |
| 34 | Papéis | `Papéis`; `Novo papel` ou `Papel: <nome>`; `O que este papel pode fazer`; botão `Gravar p_apel` | coluna direita de Usuários | `usuarios.gerenciar` | nome, descrição e as 18 permissões, agrupadas | `VM/Usuarios.cs:168` | `TE/Usuarios.xaml:76-118` | APROVADO |

### 3.4 Tela aberta de dentro de outra (fora do menu)

| # | Tela | Título exibido | Como se chega | Permissão | Dado principal | ViewModel | XAML | Sit. |
|---|---|---|---|---|---|---|---|---|
| 35 | Parametrização da catraca | `Titulo` da VM: `Parametrização` (não aparece em menu); cabeçalho `Parametrização da catraca`; abas `_Leitura`, `Li_beração`, `_Display`, `_Instalação` (só `Modo técnico`), `_Giro`; botões `Abrir a confi_guração`, `Voltar a _Gerenciar catraca` | botão `_Parametrização (em lista)` em Gerenciar catraca; botão `Ver em lista` em Configuração da catraca (`Telas.cs:1391-1419`) | **nenhuma checagem no menu** (fica fora de `PermissaoDaTela`); ler exige `operacao.ver`; salvar `catraca.configurar`; aplicar `catraca.comandar` | a mesma configuração da catraca da Configuração da catraca, em abas | `VM/Parametrizacao.cs:494` `ParametrizacaoViewModel` | `TE/Parametrizacao.xaml` | APROVADO |

### 3.5 Assistente de configuração (programa à parte)

`src/Edge.Configurador/JanelaDoAssistente.xaml`, janela `XAcess — Assistente de configuração · Rayzer`. Abre com
elevação de administrador do Windows (`Verb = "runas"`). Não usa o login do painel.

| # | Passo | Título exibido | Como se chega | Dado principal | Sit. |
|---|---|---|---|---|---|
| 36 | 1 | aba `1  ·  Ambiente`; `Este computador tem o que precisa?` | atalho do menu Iniciar `Rayzer XAcess — Assistente de configuração` (`ConexaoTopdata.wxs:283`); no painel, botão `Abrir _assistente de configuração` **só quando o serviço está sem configuração** (`EstadoDoPainel.cs:128,165`) | pré-requisitos do computador | APROVADO |
| 37 | 2 | aba `2  ·  Catracas`; `Quais catracas este computador atende?`; caixa `Modo simulação: catracas simuladas, sem catraca física (para treinar e testar)` | idem | número da catraca (`NÚMERO DA CATRACA (INNER)`), `NOME NO PAINEL`, modo simulação | APROVADO |
| 38 | 3 | aba `3  ·  Nuvem`; `Painel na nuvem` | idem | endereço e segredo da nuvem | APROVADO |
| 39 | 4 | aba `4  ·  Gravar`; `Conferir e gravar`; aviso `Primeiro acesso ao painel` | idem | resumo e gravação | APROVADO |

Linhas 40 a 42 (funções de bandeja que não existem dentro do painel):

| # | Função | Onde existe | Observação | Sit. |
|---|---|---|---|---|
| 40 | Iniciar a operação | só no menu da bandeja (`BandejaDoSistema.cs:46`) e no assistente (`--iniciar-operacao`) | não há item no painel | APROVADO |
| 41 | Encerrar a operação (parar as catracas) | só no menu da bandeja (`BandejaDoSistema.cs:47`), com confirmação e elevação | não há item no painel | APROVADO |
| 42 | Fechar o painel (as catracas continuam) | só no menu da bandeja (`BandejaDoSistema.cs:49`); o "X" da janela manda para a bandeja | — | APROVADO |

---

## 4. Funções citadas no docs/46 que não existem no código

| Função citada | Onde o docs/46 cita | O que existe de mais próximo | Sit. |
|---|---|---|---|
| Cartões não reconhecidos / Leituras recusadas | §3 (15º item do menu), §5, §6 | Nada com esse nome em `src/` (busca por "reconhecid" = zero). O mais próximo: Acessos com filtro `Negados`; Consultar código responde `Código não cadastrado. A catraca negaria este código.` | APROVADO (ausência verificada) |
| Cartões (lotes) | §3, §5, §6 | Só lotes **de importação de pessoas** (nº 27). Não há importação de cartões no painel, mas o instalador cria o atalho `Rayzer XAcess — Modelo de planilha de cartões` com a descrição "Planilha para cadastrar tipos e cartões e importar no painel" (`ConexaoTopdata.wxs:290-294`). O docs/34-anexos/02 já registrava "Não há nenhum código de importação". | REPROVADO (P10: o atalho promete o que o painel não faz) |
| Trocar minha senha | §3, §6 | Só a troca **obrigatória** (nº 3). A RPC `TrocarSenha` exige só estar logado (`InterceptadorDeSessao.cs:38`) e aceita a senha atual, mas nenhuma tela a oferece a quem já entrou. | APROVADO (ausência verificada) |
| Ajuda | §3 (Novidades e ajuda), §6 | Nada. Não há tela, botão, F1 nem link de ajuda. | APROVADO (ausência verificada) |
| Novidades (reabrir) | §6 | A janela existe (nº 4), mas abre só uma vez por edição; não há como reabrir. | APROVADO (ausência verificada) |
| Visão geral de uma catraca | §3, §6 | Não há subtela. Os dados estão espalhados: cartão da catraca (Painel e Catracas), linha de situação em Gerenciar, `Equipamento` (firmware e relógio) no painel da coluna em Configuração da catraca. | APROVADO (ausência verificada) |

## 5. Código sem tela (órfão)

| ViewModel | O que faz | Onde é usada | Sit. |
|---|---|---|---|
| `VM/GerenciamentoDeCatracasViewModel.cs:30` | saúde das catracas pela camada inteligente (`ObterSaudeDasCatracas`, a cada 10 s) | nenhum XAML, nenhum `new` fora dela | APROVADO |
| `VM/PainelViewModel.cs:22` | painel do evento, versão anterior à `PainelAoVivoViewModel` | nenhum XAML, nenhum `new` fora dela | APROVADO |

Também há RPCs com permissão definida e sem tela no painel: `ObterRitmo`, `ListarAlertas`, `MarcarAlertaComoCiente`,
`ObterRelatorioPosEvento` (`InterceptadorDeSessao.cs:55-59`). `ObterSugestoes` é chamada pela `ParametrizacaoViewModel`,
mas `Parametrizacao.xaml` não mostra as sugestões. APROVADO (verificado por busca). Fica para o A6 e o E6 decidir se
somem ou ganham lugar.

---

## 6. Divergências entre o docs/46 §3 e o código

| # | O docs/46 §3 diz | O código diz | Sit. |
|---|---|---|---|
| D1 | 15 telas no menu, a última `Cartões não reconhecidos` | **14** itens; `Cartões não reconhecidos` não existe em lugar nenhum | APROVADO |
| D2 | Item de menu `Gêmeo digital` | O item se chama `Configuração da catraca` desde o commit `7568a17` ("UI: renomeia o módulo Gêmeo digital para Configuração da catraca"). Em texto visível do código não sobrou "Gêmeo": só em comentários, nomes de classe e arquivo (`GemeoDigitalViewModel`, `Gemeo.xaml`, `AbrirNoGemeo`) e no título da janela de captura de tela (`CapturaDeTela.cs:216`, ferramenta interna). "Gmail": zero ocorrências em `src/` e `installer/`. | APROVADO no código; **PENDENTE** no build instalado (se a instalação mostra "Gêmeo", ela é anterior a `7568a17`) |
| D3 | Item de menu `Parâmetros do cadastro` | O item se chama `Perfis e horários`; o cabeçalho da mesma tela diz `Empresas, horários e perfis`. Dois nomes para a mesma tela, e nenhum é o do docs/46. | REPROVADO (P1/P2: nome não é único) |
| D4 | "Configuração desta catraca" é subtela com esse título | É um **cartão** dentro de Configuração da catraca (`Gemeo.xaml:149`) e o `Titulo` de uma VM que nunca vai ao menu. Não tem o nome da catraca no título (P4). | REPROVADO (P4) |
| D5 | "Configuração da catraca" aparece em dois lugares: no Gêmeo e numa tela própria | Confirmado, com detalhe: os dois lugares são **Configuração da catraca** (menu) e **Parametrização da catraca** (fora do menu, aberta por `_Parametrização (em lista)` e `Ver em lista`). As duas leem e gravam pelos mesmos RPCs. Há ainda uma terceira porta para os mesmos campos no nível do evento: **Configurações** (`Configurações do evento`, seção `Catracas`). | REPROVADO (P1) |
| D6 | "Giro desta catraca" é uma subtela | É um painel usado em dois lugares, com comportamento diferente: sem salvar próprio no painel dos braços/urna, com salvar próprio na aba `_Giro` da Parametrização. | REPROVADO (P1) |
| D7 | Empresa/Sala/Horário/Feriado "dentro de Parâmetros" | Correto, mas são abas `Perfis`, `Horários e feriados`, `Empresas e salas` da tela `Perfis e horários`. | APROVADO |
| D8 | Papéis dentro de Usuários | Correto: coluna direita da tela `Usuários` (cabeçalho `Usuários e papéis`). | APROVADO |
| D9 | Ficha de pessoa dentro de Pessoas | Correto: é cartão, não tela. O título traz o nome (`Ficha: <nome>`). | APROVADO |
| D10 | Simulador só com simulação ligada (U07) | Correto (`Telas.cs:1373-1387`, `1522-1523`), **e** também exige `simulador.usar`. Fora do Simulador, o botão `Na simulada` na Configuração da catraca usa a mesma RPC `SimularLeitura` e aparece com o serviço em simulação (`Gemeo.xaml:285-290`). | APROVADO |
| D11 | (não citado) | Gerenciar catraca, Configuração da catraca e Configurações aparecem no menu com `operacao.ver`, mas as ações pedem `catraca.comandar`, `catraca.configurar`, `catraca.fechar` ou `configuracao.editar`. Os papéis prontos `portaria` e `somente_leitura` veem essas telas e recebem recusa ao agir. | REPROVADO (P9) |
| D12 | (não citado) | Mesmo com login (ADR-0026), sete lugares pedem o nome à mão: `Seu nome (fica registrado em cada pedido)` (Gerenciar), `Seu nome (fica registrado)` (Configuração da catraca, Parametrização, Giro com controles próprios), `Seu nome (fica registrado junto da mudança)` (Configurações), operador em Sincronização, `Quem estorna` (Acessos). O serviço grava o texto digitado (`request.Operador`: estorno `EdgeControlService.cs:200`, reenvio `:210`, configuração do evento `:479`, comandos `:875`; giro `EdgeControlService.MapaDeGiro.cs:172`; parametrização `EdgeControlService.Parametrizacao.cs:122`), não o usuário da sessão. | REPROVADO (P1: o mesmo dado pedido em vários lugares) |
| D13 | (não citado) | O Assistente de configuração (incluir e tirar catracas, ligar simulação) só tem botão no painel quando não há configuração. Depois, só pelo menu Iniciar. A tela Catracas manda "use o Assistente de configuração (passo 2)" sem dar o caminho. | REPROVADO (P10) |
| D14 | (não citado) | O menu tem 14 itens de 48 px (`Rayzer.Height.Touch` = 44 + margem 2+2) = 672 px, sem rolagem própria declarada. Em 1366×768 a área útil da barra lateral fica perto de 430-460 px (estimativa pelo cabeçalho e rodapé do XAML). Coerente com a issue #9. | **PENDENTE** (medição real é do A4) |

## 7. Notas para os outros agentes (achados de passagem, não são escopo do A1)

- **P6 (A8, E6):** `_Desfazer o lote escolhido` (Pessoas) apaga as pessoas do lote com um clique, sem confirmação
  nem motivo (`Pessoas.cs:201-217`). `_Fechar: ninguém passa` (Gerenciar) exige motivo, mas não tem segundo passo.
  Liberar um giro, Refazer a conexão, Aplicar e Estornar já são de dois passos. Classificação: REPROVADO para o
  desfazer do lote; fechar a catraca fica para o A8 decidir se "fechar" entra na regra (não libera nem apaga).
- **P2 (A3):** a Parametrização tem `Modo técnico`, que mostra "os nomes do SDK" (`Parametrizacao.xaml:7-13`); a
  Configurações tem o rótulo `Tipo de leitor (técnico — 8 na bancada; 5 se o QR não for lido)`. Termos no caminho
  principal: `Inner` (assistente), `firmware`, `Reconexões`, `Grupo`, `Porta` (cartão da catraca).
- **Nomes duplos de uma mesma tela (A3):** `Usuários` × `Usuários e papéis`; `Configurações` × `Configurações do
  evento`; `Simulador` × `Simulador de catraca`; `Parametrização` × `Parametrização da catraca`; `Perfis e horários` ×
  `Empresas, horários e perfis`.
- **docs/33 §9.3 desatualizado:** o passo a passo ainda manda abrir "Gêmeo digital no menu" e usar "Configurar no
  gêmeo" / "Abrir no gêmeo"; o código diz `Configuração da catraca`, `_Configurar a catraca`, `Abrir a confi_guração`.
