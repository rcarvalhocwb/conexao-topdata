# 50. Auditoria de responsividade, tema e papéis (E4)

**Entregável:** E4 do `docs/46-prompt-analise-front-end.md`. Este arquivo reúne três partes, cada uma de um agente.

| Parte | Agente | Situação |
|---|---|---|
| Responsividade e layout (tela × resolução × DPI) | A4 | **pendente** (outro agente) |
| Tema e estados (tokens, claro, escuro, alto contraste) | A5 | **pendente** (outro agente) |
| **Permissões e papéis** | A6 | **escrita neste arquivo (seção abaixo)** |

Classificação usada em toda a parte de papéis (docs/46 §7), com o sentido que ela tem aqui:

- **APROVADO:** verificado lendo o código; o trecho está citado.
- **PENDENTE:** depende de executar a tela no Windows ou de um teste com login real.
- **BLOQUEADO:** depende de decisão do dono do produto.
- **REPROVADO:** verificado no código e não atende ao princípio P9 ("Permissão esconde, não quebra. O menu mostra só o que o papel usa; quando algo está fora do papel, a tela diz quem libera").

Nada aqui foi executado: não há captura, não rodei a interface nem os testes. Tudo vem de leitura de código, da leitura das migrações e de uma comparação mecânica entre o contrato e o mapa de permissões do serviço. Os nomes de usuário e as pessoas que aparecem nos exemplos são fictícios.

---

## Parte A6. Permissões e papéis

### 1. Resumo

| # | Conclusão | Situação |
|---|---|---|
| 1 | O menu mostra só as telas que o papel pode abrir (filtro por permissão em `JanelaViewModel.TelasDoMenu`). | APROVADO |
| 2 | Com o papel Portaria, "Usuários" não aparece. | APROVADO (por código) |
| 3 | Todos os 60 RPCs do contrato têm entrada no mapa do serviço; nenhum fica sem permissão mapeada. | APROVADO |
| 4 | Recusa por permissão não derruba a tela: vira mensagem na própria tela. | APROVADO |
| 5 | Dentro das telas, os botões de ação não são escondidos nem desabilitados por permissão; o operador clica e recebe a recusa. | REPROVADO (P9) |
| 6 | A mensagem de recusa diz o que faltou ("Seu papel não permite: …"), mas não diz quem libera. | REPROVADO (P9) |
| 7 | Configurações e Configuração da catraca são liberadas só por `operacao.ver`, mas o uso principal delas é editar. Portaria e Somente leitura as veem e não conseguem salvar. | REPROVADO (P9) |
| 8 | A tela de Pessoas abre para Portaria e avisa que documento e contato estão mascarados. | APROVADO (código); visual PENDENTE |
| 9 | O papel Somente leitura existe e vem da migração 020. | APROVADO |
| 10 | A migração 026 citada no pedido não existe no repositório. | Divergência registrada (seção 3) |
| 11 | Se o Portaria deve ou não ver Configurações, Configuração da catraca, Sincronização e poder fechar catraca. | BLOQUEADO (decisão do dono) |
| 12 | Teste de ponta a ponta por papel, com login real, na interface. | PENDENTE |

### 2. Telas do menu e a permissão que libera cada uma

Fonte: `src/Desktop.ViewModels/Telas.cs`, lista `Telas` e mapa `PermissaoDaTela` em `JanelaViewModel` (linhas 1312 a 1358); filtro em `TelasDoMenu` (linhas 1522 a 1528). Os códigos de `CodigosDePermissao` (`src/Desktop.ViewModels/Login.cs`) são idênticos aos de `Permissoes` (`src/Access.Domain/Usuarios/Permissoes.cs`): 18 códigos nos dois lados, mesmos nomes e mesmas constantes.

São **14 telas no menu do código**, e não 15 como diz o docs/46 §3: "Cartões não reconhecidos" não existe em `Telas.cs` nem em nenhum `.cs` ou `.xaml` do `src` (busca por "não reconhecid" e "NaoReconhecid" sem resultado). O que o operador lê no menu é a propriedade `Titulo` de cada tela (`JanelaPrincipal.xaml`, `Text="{Binding Titulo}"`), e dois títulos diferem do docs/46:

| Ordem | Título no menu (propriedade `Titulo`) | Classe | Permissão que libera (`CodigosDePermissao`) | Código gravado na base | Observação |
|---|---|---|---|---|---|
| 1 | Painel ao vivo | `PainelAoVivoViewModel` | `OperacaoVer` | `operacao.ver` | |
| 2 | Catracas | `CatracasViewModel` | `OperacaoVer` | `operacao.ver` | |
| 3 | Acessos | `AcessosViewModel` | `OperacaoVer` | `operacao.ver` | |
| 4 | Consultar código | `ConsultaViewModel` | `CodigosConsultar` | `codigos.consultar` | |
| 5 | Sincronização | `SincronizacaoViewModel` | `OperacaoVer` | `operacao.ver` | |
| 6 | Prestação de contas | `ContasViewModel` | `RelatoriosVer` | `relatorios.ver` | |
| 7 | Gerenciar catraca | `GerenciarCatracaViewModel` | `OperacaoVer` | `operacao.ver` | |
| 8 | **Configuração da catraca** | `GemeoDigitalViewModel` | `OperacaoVer` | `operacao.ver` | O docs/46 chama de "Gêmeo digital"; no código o título já é "Configuração da catraca" (`GemeoDigitalViewModel.cs:161`). Se a instalação ainda mostra "Gêmeo", é build antigo: PENDENTE confirmar no instalado. |
| 9 | Configurações | `ConfiguracoesViewModel` | `OperacaoVer` | `operacao.ver` | |
| 10 | Diagnóstico | `DiagnosticoViewModel` | `DiagnosticoVer` | `diagnostico.ver` | |
| 11 | Simulador | `SimuladorViewModel` | `SimuladorUsar` e modo simulação ligado | `simulador.usar` | Duas condições: `t is not SimuladorViewModel \|\| Painel.Estado.Simulacao` (U07) e a permissão. |
| 12 | Pessoas | `PessoasViewModel` | `PessoasVer` | `pessoas.ver` | |
| 13 | **Perfis e horários** | `ParametrosDoCadastroViewModel` | `CadastroParametros` | `cadastro.parametros` | O docs/46 chama de "Parâmetros do cadastro"; o título no código é "Perfis e horários" (`ParametrosDoCadastro.cs:99`). Dentro dela ficam empresas, salas, horários, feriados e perfis. |
| 14 | Usuários | `UsuariosViewModel` | `UsuariosGerenciar` | `usuarios.gerenciar` | Os papéis são geridos dentro desta tela; não há item de menu separado. |

**Telas fora do menu** (abertas por botão em outra tela, sem passar pelo filtro do menu):

| Tela | Título | Entra por | Está em `PermissaoDaTela`? | Consequência |
|---|---|---|---|---|
| `ParametrizacaoViewModel` | "Parametrização" | Botão "Parametrizar" em Gerenciar catraca e na Configuração da catraca (`JanelaViewModel.Parametrizar`, linha 1392) | Não | Abre para quem chegou ao botão, isto é, qualquer papel com `operacao.ver`. O `TelaAtual` não passa por `Permitida`. A leitura funciona (`ObterConfiguracaoDaCatraca` exige `operacao.ver`); salvar exige `catraca.configurar`. |
| `MapaDeGiroViewModel` | "Giro desta catraca" | Dentro da Configuração da catraca (`CentralDaCatracaViewModel`) | Não | Mesma regra: ler com `operacao.ver`, gravar com `catraca.configurar`. |
| "Trocar minha senha" | (sem item de menu) | Só aparece como etapa do login quando `must_change` está ligado | Não se aplica | O RPC `TrocarSenha` é de qualquer usuário logado (valor nulo no mapa), mas o menu não oferece a troca voluntária. Isso é assunto de A1/A2, registrado aqui só como fato. |

**Comportamento especial verificado:**
- Sem sessão de login (`Sessao is null`: testes e ferramentas), o menu mostra tudo menos Usuários (`Permitida`, linha 1526). Em produção `Sessao` nunca é nulo (`JanelaPrincipal.xaml.cs:258`).
- Antes do login (`Sessao.Logado == false`), `TelasDoMenu` fica vazio.
- Quando o papel muda no meio da sessão, `SessaoMudou` recalcula o menu e, se a tela atual saiu, troca para a primeira disponível (`Telas.cs:1363` a `1370`).

Situação: **APROVADO** (verificado no código). Visual do menu por papel: **PENDENTE**.

### 3. Papéis prontos nas migrações

Fonte: `src/Access.Infrastructure.SQLite/Migrations/`. Os números 020, 021, 022 e 023 existem. **A 026 não existe**: o diretório vai de `001` a `023`, e faltam também `014`, `024` e `025`. Não há outra migração que mexa em `app_role_permission` (busca por `app_role_permission` em todo o repositório só acha `020`, `021`, `022`, `023` e `UsuariosDoSistema.cs`). O código C# também não semeia permissão de papel pronto: `UsuariosDoSistema.cs` só insere papéis novos criados pelo administrador (`builtin = 0`, linha 609). Se a 026 existe em outro ramo ou foi planejada, ela não está neste.

O que cada migração faz com os papéis:

| Migração | O que faz |
|---|---|
| `020_usuarios_do_sistema.sql` | Cria `app_user`, `app_role`, `app_role_permission`, `app_user_role`, `app_user_event`. Semeia **quatro papéis do sistema** (`builtin = 1`): `administrador`, `supervisor`, `portaria`, `somente_leitura`, e as permissões iniciais de cada um. Protege o papel Administrador de perder `usuarios.gerenciar` e impede apagar papel do sistema. |
| `021_pessoas.sql` | Cria as tabelas de pessoas. **Não toca em papéis nem permissões** (sem `app_role` no arquivo). |
| `022_permissoes_de_pessoas.sql` | Acrescenta permissões de pessoas, cadastro e fechar catraca a Administrador, Supervisor e Portaria. **Não altera Somente leitura.** |
| `023_importacao_de_pessoas.sql` | Acrescenta `pessoas.importar` a Administrador e Supervisor. |

**Existe papel de somente leitura:** sim, `somente_leitura` ("Somente leitura", descrição "Só acompanha e vê relatórios"), do sistema, criado em `020`. APROVADO.

Permissões efetivas de cada papel pronto, depois de aplicar 020, 022 e 023 (18 permissões no catálogo `Permissoes.Todas`):

| Permissão | Administrador | Supervisor | Portaria | Somente leitura | Vem de |
|---|:-:|:-:|:-:|:-:|---|
| `operacao.ver` | sim | sim | sim | sim | 020 |
| `codigos.consultar` | sim | sim | sim | não | 020 |
| `relatorios.ver` | sim | sim | não | sim | 020 |
| `diagnostico.ver` | sim | sim | não | não | 020 |
| `catraca.comandar` | sim | sim | sim | não | 020 |
| `catraca.configurar` | sim | sim | não | não | 020 |
| `configuracao.editar` | sim | sim | não | não | 020 |
| `sincronizacao.operar` | sim | sim | não | não | 020 |
| `acessos.estornar` | sim | sim | não | não | 020 |
| `simulador.usar` | sim | sim | não | não | 020 |
| `usuarios.gerenciar` | sim | não | não | não | 020 |
| `pessoas.ver` | sim | sim | sim | não | 022 |
| `pessoas.ver_dados` | sim | sim | não | não | 022 |
| `pessoas.editar` | sim | sim | sim | não | 022 |
| `pessoas.bloquear` | sim | sim | sim | não | 022 |
| `cadastro.parametros` | sim | sim | não | não | 022 |
| `catraca.fechar` | sim | sim | não | não | 022 |
| `pessoas.importar` | sim | sim | não | não | 023 |
| **Total** | **18** | **17** | **6** | **2** | |

Conferência: Administrador tem as 18 do catálogo; Supervisor tem as 18 menos `usuarios.gerenciar`; Portaria tem `operacao.ver`, `codigos.consultar`, `catraca.comandar`, `pessoas.ver`, `pessoas.editar`, `pessoas.bloquear`; Somente leitura tem `operacao.ver` e `relatorios.ver`. O comentário da 022 confirma a intenção: "A portaria cadastra e bloqueia, mas vê documento e contato mascarados." O administrador pode mudar tudo isso depois, pela tela de Usuários; estes são só os pontos de partida (comentário da 022).

Observação: o papel Somente leitura **não ganhou `pessoas.ver`**, então não vê a tela de Pessoas. É coerente com "só acompanha e vê relatórios", mas é uma escolha que o dono pode querer rever (item 11 do resumo).

### 4. Matriz tela × papel

Cada célula diz se a tela **aparece** no menu e a permissão usada. Regra aplicada: aparece quando o papel tem a permissão da coluna "Permissão" da tabela da seção 2 (`Sessao.Pode(permissao)`). Calculada a partir das duas tabelas anteriores; nenhuma célula foi observada na interface.

| Tela | Permissão | Administrador | Supervisor | Portaria | Somente leitura |
|---|---|:-:|:-:|:-:|:-:|
| Painel ao vivo | `operacao.ver` | aparece | aparece | aparece | aparece |
| Catracas | `operacao.ver` | aparece | aparece | aparece | aparece |
| Acessos | `operacao.ver` | aparece | aparece | aparece | aparece |
| Consultar código | `codigos.consultar` | aparece | aparece | aparece | não aparece |
| Sincronização | `operacao.ver` | aparece | aparece | aparece | aparece |
| Prestação de contas | `relatorios.ver` | aparece | aparece | não aparece | aparece |
| Gerenciar catraca | `operacao.ver` | aparece | aparece | aparece | aparece |
| Configuração da catraca (Gêmeo) | `operacao.ver` | aparece | aparece | aparece | aparece |
| Configurações | `operacao.ver` | aparece | aparece | aparece | aparece |
| Diagnóstico | `diagnostico.ver` | aparece | aparece | não aparece | não aparece |
| Simulador | `simulador.usar` + simulação ligada | aparece só com simulação | aparece só com simulação | não aparece | não aparece |
| Pessoas | `pessoas.ver` | aparece | aparece | aparece | não aparece |
| Perfis e horários | `cadastro.parametros` | aparece | aparece | não aparece | não aparece |
| Usuários | `usuarios.gerenciar` | aparece | não aparece | não aparece | não aparece |
| **Telas visíveis (de 14)** | | **14** (13 sem simulação) | **13** (12 sem simulação) | **9** | **8** |

Contagem: Administrador 14 com simulação ligada (13 sem). Supervisor 13 com simulação (12 sem), pois não vê Usuários. Portaria 9. Somente leitura 8: `operacao.ver` abre sete telas (Painel ao vivo, Catracas, Acessos, Sincronização, Gerenciar catraca, Configuração da catraca, Configurações) e `relatorios.ver` abre a Prestação de contas.

Situação da matriz: **APROVADO** como cálculo sobre o código e as migrações. **PENDENTE** como observação na interface por papel (precisa de execução no Windows com logins de teste de cada papel).

#### 4.1 O Portaria vê alguma tela que não deveria?

A resposta depende de quais telas o dono considera parte do trabalho da portaria. O que o código mostra:

- A descrição do papel na migração 020 é "Acompanha, consulta códigos e comanda catracas". As permissões do papel batem com isso (mais pessoas, da 022).
- O Portaria vê **9 telas**. Seis delas são coerentes com a descrição: Painel ao vivo, Catracas, Acessos, Consultar código, Gerenciar catraca e Pessoas.
- Três aparecem no menu sem que o papel consiga usar o principal delas:
  - **Configurações** (`operacao.ver`): o formulário é de edição do evento; salvar exige `configuracao.editar` e o Portaria não tem. REPROVADO (P9).
  - **Configuração da catraca** (`operacao.ver`): é a tela de editar parâmetros, giro e aplicar; salvar exige `catraca.configurar`, que o Portaria não tem. REPROVADO (P9).
  - **Sincronização** (`operacao.ver`): ler o estado é útil; o botão de reenviar os recusados exige `sincronizacao.operar`. Aqui só a ação falha, a leitura serve. REPROVADO só na ação (item 5 do resumo); a presença da tela é decisão do dono.
- O Portaria **não** vê Usuários, Diagnóstico, Prestação de contas, Perfis e horários nem Simulador. APROVADO.

Então: o papel de portaria vê, sim, telas que provavelmente não deveria ver como telas de trabalho (Configurações e Configuração da catraca), e vê uma tela cuja ação principal ele não tem (Sincronização). O motivo é um só: três telas de edição estão presas à permissão de leitura `operacao.ver`. A decisão do que a portaria deve ver é do dono: **BLOQUEADO**.

### 5. Mapa de permissões do serviço (`InterceptadorDeSessao`)

Fonte: `src/Edge.Supervisor/InterceptadorDeSessao.cs`, dicionário `Exigida` (linhas 33 a 104), e o contrato `src/Contracts/Protos/edge_control.proto`.

**Método:** extraí os nomes de `rpc` do serviço `EdgeControl` (único `service` do `.proto`) e os nomes das chaves do dicionário `Exigida` e comparei os dois conjuntos.

| Medida | Resultado |
|---|---|
| RPCs no contrato | 60 |
| Chaves no mapa `Exigida` | 60 |
| RPCs do contrato **sem entrada** no mapa | **0** |
| Entradas no mapa **sem RPC** correspondente (sobra) | **0** |
| RPCs com permissão específica | 53 |
| RPCs mapeados com valor nulo ("basta estar logado" ou sem sessão) | 7 |

**RPCs sem permissão mapeada: nenhum.** APROVADO (comparação mecânica).

Os 7 mapeados como nulos não são esquecimento, são decisão escrita no código:

| RPC | Regra | Observação |
|---|---|---|
| `Entrar` | Anônimo (`Anonimas`) | Precisa ser, é o login. |
| `ObterEstado` | Anônimo | Alimenta o ícone da bandeja antes do login: contagens e situação, sem dado pessoal (comentário nas linhas 106 a 110). |
| `ListarEquipamentos` | Anônimo | Mesmo motivo: nomes e situação das catracas. |
| `TrocarSenha` | Basta estar logado; passa mesmo com troca de senha pendente (`DuranteATroca`) | |
| `Sair` | Basta estar logado; passa na troca pendente | |
| `ObterSessao` | Basta estar logado; passa na troca pendente | |
| `ObterParametrosDoCadastro` | Basta estar logado | Os parâmetros não têm dado pessoal (comentário na linha 75). A tela de Pessoas precisa deles mesmo sem `cadastro.parametros`. |

**O que acontece com um RPC novo sem entrada no mapa (fail-secure):** `Conferir` busca o nome no dicionário e, se não achar, lança `RpcException(PermissionDenied, "Função sem permissão definida: {rpc}.")` (linhas 154 a 157). Isso vem **antes** da checagem de sessão. Ou seja: nega para todo mundo, inclusive administrador, e até para quem não está logado. Isso é o desejado do ponto de vista de segurança. APROVADO.

**Isso gera erro para o operador?** Hoje, não, porque o conjunto está completo. Se um RPC novo fosse acrescentado ao contrato sem entrada no mapa, o operador veria um erro: a tela que o chamasse mostraria, em `Mensagem`, o texto "Função sem permissão definida: NomeDoRpc.", porque `MensagemDeFalha.Para` devolve o `Detail` quando o status é `PermissionDenied` (`MensagemDeFalha.cs:41`). O texto cita o nome técnico do RPC, que o operador não entende (princípio P2). Há um teste que impede o contrato de chegar assim: `Toda_rpc_do_contrato_tem_permissao_definida` (`tests/Integration/LoginEUsuariosTests.cs:195` a `209`), que também confere que toda permissão do mapa existe no catálogo. **Eu não executei esse teste**; a conclusão "0 sem mapa" é da minha comparação mecânica, e o teste é a garantia de que ela continua valendo no CI. Se o teste rodar no CI, a conclusão fica coberta; PENDENTE ver o resultado do CI.

**Mapa por permissão** (RPCs que cada permissão libera):

| Permissão | RPCs |
|---|---|
| `operacao.ver` | AcompanharEventos, ListarAcessos, ObterConfiguracao, ObterSincronizacao, ListarUsosSemPassagem, ListarComandos, ObterConfiguracaoDaCatraca, ObterMapaDeGiro, ExplicarNegativa, ObterSugestoes, ObterSaudeDasCatracas, ObterRitmo, ListarAlertas, MarcarAlertaComoCiente, ListarCatracasFechadas |
| `codigos.consultar` | ConsultarCodigo |
| `relatorios.ver` | ObterRelatorioPosEvento, ObterPrestacaoDeContas |
| `diagnostico.ver` | ObterDiagnostico, ObterPacoteDeDiagnostico |
| `catraca.comandar` | EnviarComando |
| `catraca.configurar` | RegistrarDestinoDaSugestao, GravarConfiguracaoDaCatraca, GravarMapaDeGiro, RegistrarConferenciaDoGiro |
| `configuracao.editar` | GravarConfiguracao |
| `sincronizacao.operar` | ReenviarCartasMortas |
| `acessos.estornar` | EstornarUso |
| `simulador.usar` | SimularLeitura |
| `pessoas.ver` | BuscarPessoas, ObterPessoa |
| `pessoas.editar` | GravarPessoa, AdicionarCredencial |
| `pessoas.bloquear` | MudarSituacaoDaPessoa, MudarSituacaoDaCredencial |
| `pessoas.importar` | PreverImportacaoDePessoas, ImportarPessoas, ListarImportacoesDePessoas, DesfazerImportacaoDePessoas |
| `cadastro.parametros` | GravarEmpresa, GravarSala, GravarHorario, ExcluirHorario, GravarFeriado, ExcluirFeriado, GravarPerfil |
| `catraca.fechar` | FecharCatraca, AbrirCatraca |
| `usuarios.gerenciar` | ListarUsuarios, GravarUsuario, RedefinirSenha, ListarPapeis, GravarPapel |

(`pessoas.ver_dados` não libera RPC: muda o que `ObterPessoa` e `BuscarPessoas` devolvem. Sem ela, documento, contato, nascimento e veículo voltam mascarados, `EdgeControlService.Pessoas.cs:66` a `80` e `371`.)

**Ponto de atenção no mapa:** `MarcarAlertaComoCiente` (uma ação que grava) está sob `operacao.ver`, uma permissão de leitura. Não é lacuna de segurança grave (só marca ciência de alerta), mas quebra a regra "ver não grava". Registro para o orquestrador; sem classificação própria porque não afeta P9.

### 6. O que acontece hoje quando o papel não tem a permissão

São quatro situações diferentes, e só a primeira cumpre o P9.

**6.1 Tela inteira fora do papel: a tela some do menu. APROVADO.**
`TelasDoMenu` filtra por `Permitida(t)` (`Telas.cs:1522`), e a lista do menu é ligada a `TelasDoMenu` (`JanelaPrincipal.xaml:88`). Não aparece, não dá erro. Se o papel perder a permissão com a tela aberta, o menu é recalculado e a tela atual troca para a primeira disponível (`Telas.cs:1363` a `1370`). O servidor repete a regra em cada chamada (`InterceptadorDeSessao.Conferir`), então esconder o item não é a única barreira.

**6.2 Ação dentro de tela liberada: o botão aparece e dá erro. REPROVADO (P9).**
A tela mostra a ação, o operador clica, o serviço nega, a tela escreve a recusa em `Mensagem`. O painel não consulta permissão em nenhum botão: o único uso de `Sessao.Pode` em todo o `src/Desktop.*` é o filtro do menu (`Telas.cs:1528`), e os `.xaml` não ligam nenhum controle a permissão (a busca por "Pode" e "Permiss" nos XAML só acha `PodeExportar` e `PodeExplicar`, que não são de papel). Efeito por ação e por papel:

| Tela | Ação | RPC | Permissão | Papéis que clicam e recebem recusa |
|---|---|---|---|---|
| Acessos | Estornar um uso sem passagem | `EstornarUso` | `acessos.estornar` | Portaria, Somente leitura |
| Sincronização | Reenviar o que a nuvem recusou | `ReenviarCartasMortas` | `sincronizacao.operar` | Portaria, Somente leitura |
| Gerenciar catraca | Comandos (liberar, mensagem, relógio, reconectar) | `EnviarComando` | `catraca.comandar` | Somente leitura |
| Gerenciar catraca | Fechar e reabrir catraca | `FecharCatraca`, `AbrirCatraca` | `catraca.fechar` | **Portaria**, Somente leitura |
| Configuração da catraca, Parametrização e Giro | Salvar parâmetros, salvar mapa de giro, registrar conferência | `GravarConfiguracaoDaCatraca`, `GravarMapaDeGiro`, `RegistrarConferenciaDoGiro` | `catraca.configurar` | Portaria, Somente leitura |
| Configuração da catraca, Parametrização e Giro | Aplicar na catraca | `EnviarComando` | `catraca.comandar` | Somente leitura |
| Configuração da catraca | Passar leitura simulada | `SimularLeitura` | `simulador.usar` | Portaria, Somente leitura |
| Configurações | Salvar a configuração do evento | `GravarConfiguracao` | `configuracao.editar` | Portaria, Somente leitura |
| Configurações | Aplicar agora nas catracas | `EnviarComando` | `catraca.comandar` | Somente leitura |
| Pessoas | Importar planilha e desfazer lote | `ImportarPessoas`, `DesfazerImportacaoDePessoas` | `pessoas.importar` | Portaria |

Como o erro aparece: `TelaBase.Tentar` captura `RpcException` e grava `Mensagem = MensagemDeFalha.Para(erro)` (`Telas.cs:56` a `59`). Para `PermissionDenied` com texto, a mensagem é o texto do serviço: `"Seu papel não permite: {Nome da permissão}."` (`InterceptadorDeSessao.cs:189` e `190`), por exemplo "Seu papel não permite: Mudar a configuração do evento." Não quebra a tela nem derruba o aplicativo. **Não quebra: APROVADO.** Não cumpre "esconde": **REPROVADO**.

Limite desta conclusão: vi que as chamadas de `Telas.cs`, `GerenciarCatraca.cs` e `Pessoas.cs` que citei ficam dentro de `Tentar` ou de `try/catch (RpcException)`, mas não percorri cada comando de `Parametrizacao.cs`, `MapaDeGiro.cs` e `Usuarios.cs`. Marco PENDENTE a conferência de que nenhuma chamada fica fora de `Tentar`.

**6.3 Carga de dados que o papel não pode ler: a seção fica vazia, sem aviso. Tem tratamento, mas calado.**
- Pessoas, "Importações feitas": `LerLotesAsync` engole `RpcException` e deixa `Lotes = []` (`Pessoas.cs:237` a `247`). Para o Portaria, a seção "Importar planilha" aparece inteira (`Pessoas.xaml:228` a `253`, sem condição de papel) com a lista vazia; o botão "Aplicar importação" negaria. O operador não sabe que a seção não é dele.
- Gerenciar catraca, catracas fechadas: `LerFechadasAsync` engole `Unimplemented` e `PermissionDenied` e mostra "Aberta: a catraca decide normalmente." (`GerenciarCatraca.cs:187` a `192`). **Risco:** se a leitura for negada, a tela afirma que a catraca está aberta mesmo que esteja fechada. Hoje só `operacao.ver` é exigida para ler, e todo papel que chega nessa tela a tem, então o caso não acontece com os quatro papéis prontos; pode acontecer com um papel personalizado cuja tela chegou por outro caminho. Registro como risco, sem classificar como falha atual.
- Painel ao vivo e acompanhamento ao vivo: para um papel personalizado sem `operacao.ver`, `AcompanharAsync` falha com `PermissionDenied`, o `catch (RpcException)` engole e tenta de novo a cada 3 segundos para sempre (`Telas.cs:302` a `313`). Sem erro visível, mas com tráfego repetido e uma lista ao vivo que nunca enche. Não ocorre com os quatro papéis prontos (todos têm `operacao.ver`). Papel personalizado: PENDENTE testar.

**6.4 Quando a recusa é de mascaramento, a tela avisa. APROVADO (código).**
Em Pessoas, o serviço devolve documento, contato, nascimento e veículo mascarados a quem não tem `pessoas.ver_dados`; `DadosCompletos` vem da ficha e `DadosMascarados` liga um aviso no formulário (`Pessoas.cs:476` e `488`; `Pessoas.xaml:60`). Ao gravar, o campo que volta igual à máscara não apaga o dado guardado (`EdgeControlService.Pessoas.cs`, comentário das linhas 19 a 20 e verificação nas linhas 187 e 380). Este é o único ponto do painel que já faz o que o P9 pede: explicar a limitação na própria tela. O texto exato do aviso e se ele diz **quem** libera o dado não foi lido nesta auditoria: PENDENTE.

**6.5 Quem libera: nenhuma tela diz. REPROVADO (P9).**
O P9 pede que "a tela diga quem libera". Em nenhum ponto lido a recusa cita um responsável ("peça ao supervisor", "peça ao administrador"). A mensagem do serviço cita só a função faltante. O texto "peça ao administrador" que existe em `MensagemDeFalha.SemPermissao` (linha 24) é de outro caso (conta do Windows fora do grupo "ConexaoTopdata Operadores"), não de papel.

### 7. Pessoas para Portaria (critério de aceite do docs/46 §8)

O critério: "Com o papel de portaria, 'Usuários' não aparece, e a tela de Pessoas não quebra."

| Parte do critério | Evidência | Situação |
|---|---|---|
| "Usuários" não aparece para Portaria | `PermissaoDaTela[UsuariosViewModel] = usuarios.gerenciar` (`Telas.cs:1327`); Portaria não tem essa permissão (`020`: só Administrador; `022` e `023` não a dão a ninguém) | APROVADO (por código) |
| Pessoas aparece para Portaria | `PessoasViewModel` pede `pessoas.ver`; Portaria recebe na `022` | APROVADO (por código) |
| Pessoas carrega sem erro para Portaria | A carga chama `ObterParametrosDoCadastro` (sem permissão exigida), `BuscarPessoas` (`pessoas.ver`, o Portaria tem) e `ListarImportacoesDePessoas` (`pessoas.importar`, o Portaria não tem; erro engolido, `Pessoas.cs:237`) | APROVADO (por código): nenhuma chamada da carga produz erro visível para o Portaria |
| Cadastrar, abrir ficha e bloquear funcionam para Portaria | `GravarPessoa`/`AdicionarCredencial` (`pessoas.editar`) e `MudarSituacao*` (`pessoas.bloquear`); Portaria os tem. Existe teste de integração `Portaria_cadastra_ve_mascarado_e_nao_apaga_o_que_nao_ve` (`tests/Integration/PessoasPeloServicoTests.cs:98`); **não o executei** | APROVADO (por código); resultado do teste PENDENTE |
| Seção "Importar planilha" aparece para Portaria sem poder usá-la | `Pessoas.xaml:228` a `253` sem condição de papel | REPROVADO (P9), item 6.2 |
| A tela renderiza sem corte nem quebra no Windows para Portaria | Não observado | PENDENTE |

### 8. Decisões para o dono

| # | Decisão | Por que importa | Situação |
|---|---|---|---|
| D1 | O Portaria deve ver **Configurações** (evento) e **Configuração da catraca**? Se não, estas telas precisam de permissão própria no mapa de telas (`configuracao.editar` e `catraca.configurar`, ou uma permissão de "ver" separada de "editar"). | Hoje elas aparecem para quem só tem `operacao.ver` e o salvar falha. | BLOQUEADO |
| D2 | O Portaria pode **fechar e reabrir** catraca? Hoje tem `catraca.comandar` mas não `catraca.fechar`. O roteiro 6 do docs/46 (fechar e reabrir) é tarefa de portaria? | O botão aparece em Gerenciar catraca e o clique é negado. | BLOQUEADO |
| D3 | O Somente leitura deve ver **Pessoas** (hoje não tem `pessoas.ver`)? E deve ver Gerenciar catraca, Configuração da catraca e Configurações, onde nenhuma ação funciona? | Esse papel hoje abre 8 telas; em quatro delas (Sincronização, Gerenciar catraca, Configuração da catraca, Configurações) toda ação negaria. | BLOQUEADO |
| D4 | Esconder ou desabilitar, com motivo, as ações fora do papel (com o texto "Peça ao Supervisor"), em vez de deixar o clique negar. Se sim, a `SessaoDoUsuarioViewModel` passa a ser exposta às telas. | É o que o P9 pede. | BLOQUEADO (vira item de E6; muda código) |
| D5 | Trocar a mensagem do serviço "Seu papel não permite: …" por uma que diga quem libera. | P9 e P2. | BLOQUEADO |
| D6 | `MarcarAlertaComoCiente` sob `operacao.ver` (grava com permissão de leitura): aceitar ou criar permissão própria. | Consistência do mapa. | BLOQUEADO |
| D7 | A migração 026 citada pelo plano não existe. Confirmar se é um número reservado, de outro ramo, ou erro de referência. | Evita auditar contra um papel que não está no repositório. | BLOQUEADO |

### 9. O que falta para fechar a parte de papéis

1. **PENDENTE:** teste por papel com login real no Windows: entrar com um usuário fictício de cada papel (por exemplo `ana.supervisao`, `bruno.portaria`, `carla.leitura`), anotar o menu visto e comparar com a matriz da seção 4. Também abrir cada ação da tabela 6.2 e registrar a mensagem exata.
2. **PENDENTE:** confirmar no instalado se ainda aparece "Gêmeo digital" ou "Parâmetros do cadastro" (o código já diz "Configuração da catraca" e "Perfis e horários").
3. **PENDENTE:** ler o texto do aviso de dados mascarados em Pessoas e dizer se cita quem libera.
4. **PENDENTE:** executar `Toda_rpc_do_contrato_tem_permissao_definida` e `Portaria_cadastra_ve_mascarado_e_nao_apaga_o_que_nao_ve` (ou ver o resultado deles no CI).
5. **PENDENTE:** percorrer `Parametrizacao.cs`, `MapaDeGiro.cs` e `Usuarios.cs` para confirmar que nenhuma chamada ao serviço fica fora de `Tentar` ou de `try/catch`.
6. **PENDENTE:** testar um papel personalizado sem `operacao.ver` (o caso do laço de reconexão de 3 segundos e do menu quase vazio).

---

## Parte A4. Responsividade e layout

**Pendente** (outro agente). Tabela tela × resolução × DPI a preencher pelo A4.

## Parte A5. Tema e estados

**Pendente** (outro agente). Tabela tela × tema × estado a preencher pelo A5.
