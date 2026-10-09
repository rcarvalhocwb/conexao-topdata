# 48. Arquitetura de informação do front end (E2)

> **PROPOSTA. Não aprovada. Depende da aprovação do dono do produto.**
>
> Nenhuma linha de código muda por causa deste documento. Pelo docs/46 §10, a árvore abaixo vai ao dono; só depois
> da aprovação (ou do ajuste) os agentes A4, A5 e A7 auditam sobre ela e o E6 vira lista de mudanças.

**Autor:** agente A1 (arquiteto de informação), plano `docs/46-prompt-analise-front-end.md`.
**Base:** o inventário `docs/47-inventario-front-end.md` (E1), commit `3ad6db3`. Os números entre colchetes, como
[E1 #13], apontam para a linha do inventário.

**Classificação** (docs/46 §7): **APROVADO** = verificado no código; **PENDENTE** = depende de execução visual ou de
outro agente; **BLOQUEADO** = depende de decisão do dono; **REPROVADO** = verificado e não atende ao princípio.
Uma proposta ainda não implementada nunca é APROVADO: o que se aprova aqui é o fato de o código atual sustentar (ou
não) a proposta.

---

## 1. O problema, em uma frase por princípio

| Princípio | Hoje | Sit. |
|---|---|---|
| P1 · Um lugar por função | A configuração de uma catraca tem duas telas (Configuração da catraca e Parametrização) e um terceiro nível (Configurações); o giro tem dois painéis com salvar diferente; mensagem temporária, relógio e reconexão estão em duas telas; o nome de quem opera é pedido em sete lugares. Ver §4. | REPROVADO |
| P3 · Organização pela tarefa | 14 itens em lista plana, na ordem em que foram construídos (`Telas.cs:1342-1358`). | REPROVADO |
| P4 · Contexto preservado | Gerenciar, Configuração da catraca e Parametrização têm cabeçalho fixo, sem o nome da catraca, e cada uma guarda a sua própria catraca escolhida. | REPROVADO |
| P9 · Permissão esconde | Gerenciar, Configuração da catraca e Configurações aparecem com `operacao.ver`; as ações pedem outra permissão [E1 §6 D11]. | REPROVADO |
| P10 · Ninguém precisa de manual | O texto de Novidades diz "Configuração da catraca num lugar só" (`Novidades.cs:39`), e não é verdade [§4 DU12]. O atalho "Modelo de planilha de cartões" promete importar no painel, e o painel não importa cartões [E1 §4]. | REPROVADO |

---

## 2. Árvore proposta

```
OPERAÇÃO
  Painel ao vivo                        (tela inicial)
  Acessos                               abas: Todos · Recusadas · Usos sem passagem
  Consultar código

CATRACAS
  Catracas                              lista; cada cartão abre a catraca
    └ [Catraca 01 · Portão A]           título sempre com número e nome da catraca
        Visão geral · Gerenciar · Configuração
                                         Configuração = desenho | lista, com o Giro dentro
  Padrão de todas as catracas           (hoje: seção "Catracas" de Configurações)

GESTÃO
  Pessoas                               ficha, credenciais e importar planilha de pessoas
  Perfis, horários e empresas           abas: Perfis · Horários e feriados · Empresas e salas
  Prestação de contas

INFRAESTRUTURA
  Sincronização com a nuvem             recebe a "espera pelo giro" de Configurações
  Diagnóstico

ADMINISTRAÇÃO
  Usuários e papéis

FERRAMENTAS                             o grupo só existe com o modo simulação ligado
  Simulador

Fora dos grupos
  Barra superior, ao lado do nome:      Trocar minha senha · Sair
  Rodapé da barra lateral:              Novidades e ajuda · Tema · Recolher menu
  Bandeja do Windows:                   Abrir o painel · Catracas · Iniciar a operação ·
                                        Encerrar a operação… · Fechar o painel
  Assistente de configuração:           programa à parte; botões de contexto em Catracas
                                        ("Incluir ou tirar catracas…") e em Sincronização
                                        ("Configurar a nuvem…")
```

**Itens de menu:** 11 (12 com simulação), contra 14 hoje e 16 na árvore do docs/46 §6.

**Os nomes são de trabalho.** O texto final de cada item é do A3 (E3, glossário). O exemplo `Catraca 01 · Portão A`
é fictício.

### 2.1 Regras da árvore

1. **Uma função, um lugar.** Cada formulário, lista ou ação existe numa só tela (tabela do §3).
2. **Atalho leva, não copia.** Um botão em outra tela pode levar ao lugar da função (o "Gerenciar" do cartão da catraca
   no Painel, o "Ver acessos" que abre Acessos filtrado). O atalho nunca traz o formulário junto. Duas telas com o mesmo
   formulário é duplicata, mesmo que chamem a mesma RPC.
3. **Tudo de uma catraca mora dentro dela.** A subtela tem um seletor de catraca no título; trocar de catraca mantém a
   aba aberta (P4). Os três seletores de hoje (Gerenciar, Configuração da catraca, Parametrização) viram um.
4. **Padrão do evento mora ao lado das catracas.** O que vale para todas as catracas fica em CATRACAS › Padrão de todas
   as catracas. Na configuração de cada catraca, "Usar o padrão do evento" leva a ele (atalho).
5. **Quem opera vem do login.** Nenhuma tela pede "Seu nome". O registro usa o usuário da sessão.
6. **O que é de todos fica fora dos grupos.** Trocar a senha, sair, novidades e ajuda não dependem de papel; ficam na
   barra superior e no rodapé, que todo papel vê.
7. **O Simulador e o grupo FERRAMENTAS só existem com simulação ligada** (regra U07 de hoje, `Telas.cs:1373-1387`,
   mantida e escrita aqui). Fora da simulação, nada de teste fica ao alcance do operador.
8. **Função que não existe não ganha item.** Até ser construída, ela não aparece no menu (sem tela vazia). Vale para
   Cartões (lotes) e Ajuda (até ter conteúdo).

---

## 3. Regra "onde fica": cada função no seu lugar

Coluna **Hoje** com a referência do E1. Coluna **Sit.**: o que o código atual permite afirmar.

| Função | Lugar único proposto | Hoje | Muda? | Sit. |
|---|---|---|---|---|
| Ver o evento num relance (catracas, acessos ao vivo, totais) | OPERAÇÃO › Painel ao vivo | Painel ao vivo [E1 #6] | não | APROVADO (existe) |
| Histórico de acessos com filtros | OPERAÇÃO › Acessos › Todos | Acessos [E1 #8] | vira aba | APROVADO (existe) |
| Leituras recusadas (negadas) | OPERAÇÃO › Acessos › Recusadas | não existe; Acessos com filtro `Negados` chega perto | nova aba | BLOQUEADO (decisão: aba ou item, §5 C2) |
| Estornar uso sem passagem | OPERAÇÃO › Acessos › Usos sem passagem | cartão no rodapé de Acessos, só quando há algum [E1 #23] | vira aba | APROVADO (existe) |
| Explicar uma negativa ("Por quê?") | painel lateral, onde a negativa aparece (Painel ao vivo e Acessos) | igual [E1 #22] | não | APROVADO. Não é duplicata: é o mesmo componente aberto a partir do dado |
| Consultar um código | OPERAÇÃO › Consultar código | igual [E1 #9] | não | APROVADO |
| Lista de catracas e situação | CATRACAS › Catracas | Catracas [E1 #7] e cartões do Painel | não (o Painel fica com o cartão resumido e atalhos) | APROVADO |
| Visão geral de uma catraca (situação, hora da catraca com alerta de diferença, último acesso, aberta/fechada, configuração aplicada ou não) | CATRACAS › catraca › Visão geral | não existe; dados espalhados [E1 §4] | nova subtela | APROVADO que os dados já existem (`ListarEquipamentos`, `ListarCatracasFechadas`, `ObterConfiguracaoDaCatraca`); desenho PENDENTE |
| Liberar um giro sem ingresso | CATRACAS › catraca › Gerenciar | Gerenciar catraca [E1 #12] | muda de endereço | APROVADO (existe, com dois passos) |
| Mensagem temporária no visor | CATRACAS › catraca › Gerenciar | Gerenciar **e** painel do display na Configuração da catraca [E1 #12, #20] | sai da Configuração | BLOQUEADO (docs/33 §9.2 pôs no desenho, §5 C4) |
| Acertar a hora da catraca | CATRACAS › catraca › Gerenciar | Gerenciar **e** painel da coluna [E1 #12, #20] | sai da Configuração | BLOQUEADO (idem) |
| Refazer a conexão | CATRACAS › catraca › Gerenciar | Gerenciar **e** painel da coluna [E1 #12, #20] | sai da Configuração | BLOQUEADO (idem) |
| Fechar e reabrir a catraca | CATRACAS › catraca › Gerenciar | Gerenciar [E1 #12] | muda de endereço | APROVADO (existe) |
| Histórico de pedidos à catraca | CATRACAS › catraca › Gerenciar | Gerenciar [E1 #12] | não | APROVADO |
| Configuração de uma catraca (leitor, urna, tempo, mensagem padrão, instalação) | CATRACAS › catraca › Configuração (vista "desenho" ou "lista", mesmas alterações pendentes, um Salvar, um Aplicar) | Configuração da catraca [E1 #13, #20] **e** Parametrização [E1 #35] | as duas viram uma | BLOQUEADO (aprovação da árvore) |
| Giro (lado que o braço libera, conta como entrada ou saída) | CATRACAS › catraca › Configuração › Giro | painel dos braços/urna [E1 #21] e aba Giro da Parametrização, com salvar diferente | um só comportamento: o salvar da configuração | BLOQUEADO (§5 C3) |
| Demonstração com cenários (só no desenho) | CATRACAS › catraca › Configuração, modo Demonstração | Configuração da catraca [E1 #13] | não | APROVADO (existe) |
| Padrão de todas as catracas (mensagem, tempo liberada, leitor da urna, tipo de leitor) e "Aplicar em todas" | CATRACAS › Padrão de todas as catracas | Configurações, seção `Catracas` [E1 #14] | muda de grupo e de nome | BLOQUEADO (§5 C5) |
| Espera pelo giro antes de subir para a nuvem | INFRAESTRUTURA › Sincronização com a nuvem | Configurações, seção `Nuvem` [E1 #14] | muda de tela | BLOQUEADO (§5 C5); ver a nota técnica no §7 |
| Incluir ou tirar catracas; ligar o modo simulação | Assistente de configuração, aberto por botão em CATRACAS › Catracas | só pelo menu Iniciar, ou no painel quando não há configuração [E1 #36-39] | ganha botão de contexto | BLOQUEADO (permissão nova, §5 C8) |
| Configurar a nuvem (endereço, segredo) | Assistente, aberto por botão em Sincronização | idem | ganha botão de contexto | BLOQUEADO (idem) |
| Fila de envio e reenvio dos recusados pela nuvem | INFRAESTRUTURA › Sincronização com a nuvem | Sincronização [E1 #10] | não | APROVADO |
| Pacote para o suporte | INFRAESTRUTURA › Diagnóstico | Diagnóstico [E1 #15] | não | APROVADO |
| Pessoas, ficha, credenciais, bloqueio | GESTÃO › Pessoas | igual [E1 #17, #24-26] | não | APROVADO |
| Importar planilha de pessoas e desfazer lote | GESTÃO › Pessoas | igual [E1 #27] | não | APROVADO |
| Cartões (lotes), inclusive pela urna | não entra até existir | não existe [E1 §4] | — | BLOQUEADO (dono e urna física) |
| Perfis, horários, feriados, empresas, salas | GESTÃO › Perfis, horários e empresas | `Perfis e horários` / `Empresas, horários e perfis` [E1 #18, #28-32] | um nome só | APROVADO que é uma tela; nome fica com o A3 |
| Prestação de contas e CSV | GESTÃO › Prestação de contas | igual [E1 #11] | não | APROVADO |
| Usuários, papéis e permissões; redefinir senha de outro | ADMINISTRAÇÃO › Usuários e papéis | `Usuários` / `Usuários e papéis` [E1 #19, #33-34] | um nome só | APROVADO |
| Trocar a própria senha | barra superior, ao lado do nome | não existe (só a troca obrigatória) [E1 #3] | nova | APROVADO que a RPC já serve (`TrocarSenha` exige só login, `InterceptadorDeSessao.cs:38`) |
| Sair | barra superior | igual | não | APROVADO |
| Novidades desta versão | rodapé da barra lateral (reabre a janela) | janela que abre uma vez [E1 #4] | ganha botão | APROVADO que a janela existe |
| Ajuda | rodapé da barra lateral, junto de Novidades | não existe | nova | BLOQUEADO (conteúdo) |
| Simular uma leitura | FERRAMENTAS › Simulador | Simulador [E1 #16] **e** botão `Na simulada` da Configuração da catraca | o botão some ou vira atalho | BLOQUEADO (§5 C10) |
| Iniciar e encerrar a operação | bandeja (como hoje) e citado na Ajuda | só bandeja [E1 #40-41] | não | APROVADO |
| Tema claro/escuro, recolher menu | rodapé da barra lateral | igual | não | APROVADO |

---

## 4. Duplicatas encontradas

| # | Função | Onde aparece hoje | Evidência | Proposta | Sit. |
|---|---|---|---|---|---|
| DU1 | Configuração de uma catraca | (a) Configuração da catraca, menu; (b) Parametrização da catraca, fora do menu; (c) no nível do evento, Configurações › Catracas | `Telas.cs:1391-1446`; `Gemeo.xaml:145` (`Ver em lista`); `GerenciarCatraca.xaml:39-46`; `Parametrizacao.xaml:30-36`; docs/33 §9.1 ("A Parametrização e a Gerenciar catraca continuam existindo") | (a) e (b) viram uma subtela com duas vistas; (c) vira "Padrão de todas as catracas", ao lado | REPROVADO (P1) |
| DU2 | Giro da catraca | painel dos braços/urna (sem salvar próprio) e aba `_Giro` da Parametrização (com salvar e aplicar próprios) | `CentralDaCatraca.cs:55` (`Giro.ControlesProprios = false`); `MapaDeGiro.xaml:32-36` | um comportamento: entra no salvar e no aplicar da configuração | REPROVADO (P1) |
| DU3 | Mensagem temporária, acertar relógio, refazer conexão | Gerenciar catraca e painéis do display e da coluna na Configuração da catraca | `GerenciarCatraca.xaml:93-125`; `CentralDaCatraca.xaml:94-131`; `CentralDaCatraca.cs:56` (`Comandos = new GerenciarCatracaViewModel`) | ficam só em Gerenciar; no desenho, o painel diz "fica em Gerenciar" com botão que troca de aba mantendo a catraca | REPROVADO (P1); resolução BLOQUEADA (decisão do dono em docs/33 §9.2) |
| DU4 | Escolher a catraca | três seletores independentes (Gerenciar, Configuração da catraca, Parametrização), e número digitado em Acessos e no Simulador | `GerenciarCatraca.xaml:20`; `Gemeo.xaml:242`; `Parametrizacao.xaml:44`; `Telas.cs:487` (Acessos), `:1236` (Simulador) | um seletor no título da subtela da catraca; Acessos e Simulador escolhem pela lista, não por número | REPROVADO (P4) |
| DU5 | Lista de catracas em cartões | Painel ao vivo e Catracas, com o mesmo modelo `Cartao.Catraca` e as mesmas ações | `App.xaml:92-187`; `PainelAoVivo.xaml:47`; `Catracas.xaml` | Catracas é o lugar; o Painel mantém o cartão como resumo com atalhos (regra 2) | APROVADO como achado; aceitável com a regra 2 |
| DU6 | Nome de quem opera | sete campos "Seu nome…" / "Quem estorna" com login ativo | E1 §6 D12; o serviço grava `request.Operador` | vem do login; os campos somem | REPROVADO (P1); depende de mudança no serviço (E6) |
| DU7 | Simular uma leitura | Simulador e botão `Na simulada` dos cenários | `Gemeo.xaml:285-290`; `GemeoDigitalViewModel.cs:140-141`, mesma RPC `SimularLeitura` | o botão vira atalho para o Simulador com o código preenchido, ou some | REPROVADO (P1); resolução BLOQUEADA |
| DU8 | Levar a configuração à catraca | `_Aplicar agora nas catracas…` (todas), `Aplicar nesta catraca…` (uma) e `Refazer a _conexão…` ("recebe a configuração completa de novo") | `Telas.cs:1098-1118`; `Gemeo.xaml:210`; `GerenciarCatraca.xaml:112-113` | aplicar em todas fica no Padrão; aplicar uma fica na Configuração; reconexão em Gerenciar diz que não é aplicar | PENDENTE (o A8 confere se reconexão e aplicar têm o mesmo efeito na catraca) |
| DU9 | Mensagem no visor (nomes) | `Mensagem no visor da catraca` (Configurações), `Mensagem no display` (Gerenciar), `Mensagem temporária` (Configuração), `Como a mensagem aparece no display` (prévia no desenho) | XAML citado no E1 | padrão (configuração) e temporária (pedido) são funções diferentes e ficam; "visor" × "display" vai ao A3 | PENDENTE (A3) |
| DU10 | Diagnóstico | menu e botão `Diagnóstico` em cada cartão de catraca | `App.xaml:175-182`; `Telas.cs:1447-1451` | atalho válido, mas abre o diagnóstico geral, sem a catraca: perde o contexto. Na Visão geral da catraca o atalho deve levar ao trecho daquela catraca | REPROVADO (P4) |
| DU11 | Acessos de uma catraca | menu Acessos e botão `Ver acessos` do cartão | `Telas.cs:1471-1489` | atalho válido (filtra a catraca); fica | APROVADO |
| DU12 | Texto de Novidades | "Configuração da catraca num lugar só" | `Novidades.cs:39-40` contra DU1 | o texto só volta a ser verdade depois de DU1 resolvida; até lá, corrigir o texto | REPROVADO (P10) |
| DU13 | "Lote" | lotes de importação de pessoas (Pessoas) e "Cartões (lotes)" do docs/46 e do atalho do instalador | `Pessoas.cs:16`; `ConexaoTopdata.wxs:290-294` | A3 dá nomes diferentes: "importação de pessoas" × "lote de cartões" | PENDENTE (A3) |
| DU14 | Entrada do Gerenciar pela bandeja | item `Gerenciar catraca` no menu da bandeja | `BandejaDoSistema.cs:40` | vira `Catracas` (abre a lista; dali, a catraca) | APROVADO como achado |

---

## 5. Comparação com a árvore do docs/46 §6

Iguais nos dois: Painel ao vivo, Acessos e Consultar código em OPERAÇÃO; Pessoas e Prestação de contas em GESTÃO;
Sincronização com a nuvem e Diagnóstico em INFRAESTRUTURA; Usuários e papéis em ADMINISTRAÇÃO; Simulador em FERRAMENTAS
só com simulação; o título da subtela com o nome da catraca; a configuração da catraca só dentro da catraca.

Diferenças, uma por uma:

| # | docs/46 §6 | Esta proposta | Por quê | Sit. |
|---|---|---|---|---|
| C1 | Grupo `DISPOSITIVOS` | Grupo `CATRACAS` | O sistema só tem catracas; "dispositivo" é palavra da engenharia (P2). O nome final é do A3. | BLOQUEADO (com A3) |
| C2 | `Leituras recusadas` como item próprio | Aba `Recusadas` dentro de Acessos | São as mesmas tentativas de Acessos com resultado negado (`ListarAcessos` já filtra `Negados`, `Telas.cs:490-493`). Item próprio seria a mesma lista em dois lugares (P1). Custo: um clique a mais (Acessos → Recusadas = 2 cliques, dentro do limite). Se o dono preferir item próprio, ele tem de ser **a** tela de recusadas, e Acessos perde o filtro `Negados`. | BLOQUEADO |
| C3 | Por catraca: `Visão geral · Configuração · Giro · Gerenciar` | `Visão geral · Gerenciar · Configuração`, com o Giro dentro da Configuração | (a) O dono já decidiu que a configuração da catraca inteira tem **um** salvar e **um** aplicar, e o mapa de giro "entra na mesma versão do salvo" (docs/33 §9.1, `CentralDaCatraca.cs:6-26`). Giro como aba irmã voltaria a ter salvar próprio, como a aba Giro da Parametrização hoje (DU2). (b) Gerenciar antes de Configuração: liberar, fechar e acertar a hora são do dia a dia; configurar é de montagem. | BLOQUEADO |
| C4 | Gerenciar inclui "fechar/abrir, liberar, reconectar" | Igual, **e** mensagem temporária e acertar a hora saem do desenho | Hoje esses pedidos estão em Gerenciar e no desenho (DU3). Isso foi pedido no docs/33 §9.2; tirar do desenho desfaz parte daquela decisão. | BLOQUEADO |
| C5 | `Configurações do evento` em INFRAESTRUTURA | `Padrão de todas as catracas` em CATRACAS; a espera pelo giro vai para Sincronização | Dos cinco campos de Configurações, quatro são de catraca (`Configuracoes.xaml:13-24`) e são o "padrão do evento" que cada catraca herda; o quinto é da nuvem (`:27-30`). Quem procura a mensagem do visor procura em catraca, não em infraestrutura. | BLOQUEADO |
| C6 | `Cartões (lotes)` em GESTÃO | Fora até existir | Não há tela, RPC nem importação de cartões no painel [E1 §4]. Um item vazio contraria P10. A importação de pessoas (que existe) fica em Pessoas. | BLOQUEADO (dono e urna física) |
| C7 | `Cadastro (empresas, salas, horários, feriados, perfis)` | `Perfis, horários e empresas` | O item de menu precisa dizer o que tem dentro; "Cadastro" confunde com o cadastro de pessoas. Hoje a tela já tem dois nomes (D3 do E1). | PENDENTE (A3 fecha o nome) |
| C8 | (não há) | Assistente de configuração por botões de contexto | Incluir catraca, ligar simulação e configurar a nuvem só existem no Assistente, que hoje some do painel depois da primeira configuração [E1 D13]. Não vira item de menu: é outro programa, pede administrador do Windows. Não há permissão para ele no catálogo (18 códigos, `Permissoes.cs`). | BLOQUEADO (permissão nova ou reuso de `configuracao.editar`, com A6) |
| C9 | `Trocar minha senha` em ADMINISTRAÇÃO | Na barra superior, ao lado do nome | ADMINISTRAÇÃO só aparece para `usuarios.gerenciar`. O papel `portaria` nunca veria "Trocar minha senha" ali (P9). Todo papel vê a barra superior. | BLOQUEADO |
| C10 | `Novidades e ajuda` em FERRAMENTAS | Rodapé da barra lateral | Se FERRAMENTAS só existe com simulação (regra do próprio §6), Novidades e ajuda sumiria numa instalação real. | BLOQUEADO |
| C11 | FERRAMENTAS sempre presente | O grupo só existe com simulação | Consequência de C10: sobra só o Simulador. | BLOQUEADO |
| C12 | `Catracas` dentro de DISPOSITIVOS, sem item irmão | `Catracas` e `Padrão de todas as catracas` | Consequência de C5. | BLOQUEADO |

Conflito com o glossário do docs/46 §5: a linha "Giro / mapa de giro → DISPOSITIVOS › catraca › Giro" passa a ser
"CATRACAS › catraca › Configuração › Giro" se C3 for aprovada. O A3 ajusta o E3.

---

## 6. Permissão por item (para o A6 conferir)

Regra proposta para P9: o item aparece para quem tem a permissão de **usar** a função; quem só pode ver recebe a tela
em leitura, com a frase de quem libera. Hoje o menu usa `operacao.ver` para telas cujas ações pedem outra coisa
(`Telas.cs:1320-1322`).

| Item | Aparece com | Agir exige | Hoje no menu | Sit. |
|---|---|---|---|---|
| Painel ao vivo, Acessos (Todos, Recusadas) | `operacao.ver` | — | `operacao.ver` | APROVADO |
| Acessos › Usos sem passagem | `operacao.ver` | `acessos.estornar` | dentro de Acessos | APROVADO |
| Consultar código | `codigos.consultar` | — | igual | APROVADO |
| Catracas, Visão geral | `operacao.ver` | — | igual | APROVADO |
| catraca › Gerenciar | `catraca.comandar` **ou** `catraca.fechar` | cada ação a sua | `operacao.ver` | REPROVADO hoje; proposta PENDENTE (A6) |
| catraca › Configuração | `operacao.ver` em leitura; edição com `catraca.configurar` | salvar `catraca.configurar`; aplicar `catraca.comandar` | `operacao.ver`, sem modo leitura | REPROVADO hoje; proposta PENDENTE (A6) |
| Padrão de todas as catracas | `configuracao.editar` | salvar `configuracao.editar`; aplicar em todas `catraca.comandar` | `operacao.ver` | REPROVADO hoje; proposta PENDENTE (A6) |
| Pessoas | `pessoas.ver` | `pessoas.editar`, `pessoas.bloquear`, `pessoas.importar` | igual | APROVADO |
| Perfis, horários e empresas | `cadastro.parametros` | igual | igual | APROVADO |
| Prestação de contas | `relatorios.ver` | — | igual | APROVADO |
| Sincronização com a nuvem | `operacao.ver` | reenviar `sincronizacao.operar`; espera pelo giro `configuracao.editar` | `operacao.ver` | PENDENTE (A6) |
| Diagnóstico | `diagnostico.ver` | — | igual | APROVADO |
| Usuários e papéis | `usuarios.gerenciar` | igual | igual | APROVADO |
| Simulador | `simulador.usar` e simulação ligada | igual | igual | APROVADO |
| Trocar minha senha, Sair, Novidades e ajuda | logado | — | só troca obrigatória e Sair | APROVADO que a RPC serve |

Com os papéis prontos (`020_usuarios_do_sistema.sql:73-98`, `022_permissoes_de_pessoas.sql`), o papel `portaria`
(`operacao.ver`, `codigos.consultar`, `catraca.comandar`, `pessoas.ver`, `pessoas.editar`, `pessoas.bloquear`) hoje vê
9 itens, inclusive Configuração da catraca e Configurações, onde não pode gravar nada. Na proposta, vê Painel ao vivo,
Acessos, Consultar código, Catracas (com Visão geral, Gerenciar e Configuração só em leitura), Pessoas e
Sincronização. Em nenhum dos dois casos vê Usuários (critério do docs/46 §8): APROVADO no mapa do código, PENDENTE na
execução (A6).

---

## 7. O que muda para quem já usa o sistema

**No menu**

- A lista plana de 14 itens vira 5 grupos com 11 itens (12 com simulação). Os nomes que somem do menu:
  `Gerenciar catraca`, `Configuração da catraca`, `Configurações`. Os que mudam de nome: `Perfis e horários` →
  `Perfis, horários e empresas`; `Usuários` → `Usuários e papéis`; `Sincronização` → `Sincronização com a nuvem`.
  Os nomes finais são do A3.
- Quem abria **Gerenciar catraca** passa a abrir **Catracas** e escolher a catraca (ou usar o botão "Gerenciar" do
  cartão no Painel ao vivo, como hoje). São dois cliques, um a mais que hoje para quem já estava com a catraca certa
  escolhida.
- Quem abria **Configuração da catraca** faz o mesmo caminho e escolhe a aba **Configuração**. O desenho continua lá.
- **Parametrização da catraca** deixa de ser uma tela: é a vista "lista" dentro da Configuração. Os botões
  `_Parametrização (em lista)`, `Ver em lista`, `Abrir a confi_guração` e `Voltar a _Gerenciar catraca` somem; no lugar
  fica uma troca "desenho | lista" na mesma tela.
- **Configurações** some: a mensagem do visor, o tempo liberada, o leitor da urna, o tipo de leitor e o botão de aplicar
  em todas vão para **Padrão de todas as catracas**; a espera pelo giro vai para **Sincronização com a nuvem**.

**Dentro das telas**

- O título de cada subtela de catraca passa a trazer o número e o nome da catraca, e trocar de catraca não volta para a
  primeira aba.
- Acessos ganha as abas **Recusadas** e **Usos sem passagem** (hoje o cartão de usos só aparece no rodapé).
- O campo "Seu nome" some de sete lugares. O registro passa a usar o usuário do login. Os registros antigos continuam com
  o nome que foi digitado na época.
- **Trocar minha senha** aparece ao lado do nome, no alto. **Novidades** pode ser reaberta pelo rodapé.
- Na Configuração, mensagem temporária, acertar a hora e refazer a conexão deixam o desenho e ficam em Gerenciar (se C4
  for aprovada).
- Na bandeja, `Gerenciar catraca` vira `Catracas`.

**Fora do painel**

- O manual de passo a passo do docs/33 §9.3 e as capturas de tela precisam ser refeitos. O texto de Novidades
  (`Novidades.cs:39`) também, e a mudança pede uma nova edição de Novidades (`Edicao` sobe de 1 para 2) para avisar quem
  atualiza.
- O atalho do menu Iniciar "Modelo de planilha de cartões" promete uma importação que não existe; precisa de decisão
  (tirar o atalho ou construir a importação).

**O que não muda**

- A regra de decisão da catraca, os RPCs de configuração e de comando, e a permissão que o serviço confere em cada RPC
  (`InterceptadorDeSessao.Exigida`). A árvore é navegação e texto.

**Nota técnica para o E6 (não é decisão de produto).** Hoje a espera pelo giro é gravada no mesmo pedido que os campos
de catraca (`GravarConfiguracaoRequest` com `ConfiguracaoDoEvento` inteira, `Telas.cs:1066-1077`). Separar os campos em
duas telas exige que cada uma leia o registro inteiro e mande de volta o que não mudou, ou uma mudança no contrato. E
tirar o "Seu nome" depende de o serviço passar a gravar o usuário da sessão (`ChamadorDoPainel`) em vez de
`request.Operador`. Os dois casos são PENDENTES até o E6 avaliar; nenhum pede migração de base, pelo que foi lido.

---

## 8. Cliques até a função (estimativa do A1; a medição é do A2 no E5)

Com o Painel ao vivo como tela inicial e os cartões de catraca com atalhos.

| Tarefa do docs/46 §4 (A2) | Caminho proposto | Cliques | Sit. |
|---|---|---|---|
| 1. Ver se está tudo funcionando | abre no Painel ao vivo | 0 | PENDENTE (A2) |
| 2. Descobrir que uma catraca parou | Painel ao vivo (cartão em alerta) → Visão geral | 1 | PENDENTE (A2) |
| 3. Liberar uma pessoa sem cartão | Painel ou Catracas → "Gerenciar" no cartão | 1 a 2 | PENDENTE (A2) |
| 4. Lote de cartões pela urna | não existe | — | BLOQUEADO |
| 5. Cadastrar um cartão recusado | Acessos → Recusadas (depois, Pessoas) | 2 | PENDENTE (A2) |
| 6. Fechar e reabrir uma catraca | Catracas → "Gerenciar" no cartão | 2 | PENDENTE (A2) |
| 7. Fechar o dia e gerar a prestação de contas | Prestação de contas | 1 | PENDENTE (A2) |
| 8. Trocar a própria senha | "Trocar minha senha" na barra superior | 1 | PENDENTE (A2) |
| 9. Criar um operador de portaria | Usuários e papéis → Novo usuário | 2 | PENDENTE (A2) |

**Altura do menu.** 11 itens de 48 px (`Rayzer.Height.Touch` 44 + margem) mais 5 títulos de grupo passam de 600 px.
A área útil da barra lateral em 1366×768 fica perto de 430-460 px (estimativa pelo XAML, E1 §6 D14). A árvore reduz
o problema da issue #9, mas **não o resolve sozinha**: item mais baixo, rolagem própria do menu ou grupos recolhíveis
são decisão do A4 com medição real. PENDENTE.

---

## 9. Decisões que dependem do dono do produto

1. **Aprovar, ajustar ou recusar a árvore do §2** (critério do docs/46 §8: nada de código antes disso).
2. **Configuração da catraca e Parametrização viram uma subtela só**, com vista "desenho" e vista "lista" (DU1).
3. **Giro dentro da Configuração** (um salvar) ou aba irmã, como no docs/46 §6 (C3).
4. **Pedidos imediatos** (mensagem temporária, acertar a hora, refazer a conexão) **só em Gerenciar**, desfazendo parte
   do docs/33 §9.2 (C4, DU3).
5. **Leituras recusadas**: aba de Acessos ou item próprio (C2).
6. **Configurações do evento** vira "Padrão de todas as catracas" em CATRACAS, e a espera pelo giro vai para
   Sincronização (C5).
7. **Cartões (lotes)**: fica fora do menu até existir? E o atalho "Modelo de planilha de cartões" do instalador: sai ou
   fica (C6)?
8. **Quem opera vem do login**, e os sete campos "Seu nome" somem (DU6). Muda o que fica gravado na auditoria.
9. **Botão "Na simulada"**: some ou vira atalho para o Simulador (DU7).
10. **Assistente de configuração** no painel: com qual permissão (C8)?
11. **Trocar minha senha** na barra superior e **Novidades e ajuda** no rodapé, fora dos grupos (C9, C10).
12. **Nome dos grupos** `CATRACAS` em vez de `DISPOSITIVOS`; `INFRAESTRUTURA` fica ou muda (C1, com o A3).
