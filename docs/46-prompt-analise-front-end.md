# 46. Prompt: análise e organização do front end, pensado para o operador

**Status:** prompt de trabalho para o time de agentes. Ele **não altera código sozinho**. Produz inventário,
arquitetura de informação, glossário, auditorias e uma lista priorizada de mudanças. Essa lista passa pelo
dono do produto antes de virar código.

---

## 1. Objetivo

Quem opera o evento não é programador. Precisa encontrar qualquer função do sistema em no máximo **dois cliques**,
entender o nome sem consultar glossário e conseguir operar a portaria inteira sem chamar o suporte.

Cada função tem **um lugar só**, com nome único, e esse lugar é previsível: quem sabe onde está uma coisa sabe
onde está a seguinte. Nada aparece em dois lugares com nomes diferentes.

## 2. Princípios (todo agente aplica)

| # | Princípio | Como se verifica |
|---|---|---|
| P1 | **Um lugar por função.** Uma configuração mora num único lugar. | Nenhuma função aparece em duas telas do menu. |
| P2 | **Nome que o operador usa na portaria**, não o nome da engenharia. | Glossário (seção 5). Nenhum termo técnico no caminho principal. |
| P3 | **Organização pela tarefa do operador**, não pelo módulo técnico. | Roteiros (seção 4, A2) chegam à tarefa em ≤ 2 cliques. |
| P4 | **Contexto preservado.** Subpágina de uma catraca mostra o nome dela o tempo todo. | Título da subpágina inclui a catraca; trocar de catraca não perde a seção. |
| P5 | **Estado sempre visível**, no mesmo lugar e com a mesma cor: simulação, sem internet, catraca fechada, sem login. | Barra superior idêntica em todas as telas. |
| P6 | **Ação que libera passagem ou apaga algo exige confirmação** com motivo, e nunca é um clique só. | Lista de ações sensíveis conferida tela por tela. |
| P7 | **Cabe na tela.** Nada corta, nada exige rolagem horizontal, o principal fica acima da dobra. | Auditoria responsiva (E4). |
| P8 | **Um tema só:** apenas tokens do `Rayzer.Design`. Nenhuma cor literal em XAML. Claro, escuro e alto contraste funcionam em todas as telas. | Busca por cor literal em `Desktop.App` = zero. |
| P9 | **Permissão esconde, não quebra.** O menu mostra só o que o papel usa; quando algo está fora do papel, a tela diz quem libera. | Teste por papel (A6). |
| P10 | **Ninguém precisa de manual.** Cada tela tem uma linha dizendo para que serve; estado vazio diz o próximo passo. | Revisão de estados vazios (A5). |

## 3. Ponto de partida (verificar; não confiar)

**Telas hoje**, na ordem do menu (`src/Desktop.ViewModels/Telas.cs`, `JanelaViewModel`):

Painel ao vivo · Catracas · Acessos · Consultar código · Sincronização · Prestação de contas · Gerenciar catraca ·
Gêmeo digital · Configurações · Diagnóstico · Simulador · Pessoas · Parâmetros do cadastro · Usuários ·
Cartões não reconhecidos.

**Subtelas e telas dentro de telas:** Configuração desta catraca (título `Configuração desta catraca`), Giro desta
catraca (mapa de giro), Perfis e horários, Empresa/Sala/Horário/Feriado (dentro de Parâmetros), Papéis (dentro de
Usuários), Ficha de pessoa (dentro de Pessoas).

**Problemas já registrados** (não reabrir o diagnóstico; confirmar e medir):
- Menu cortado em janelas pequenas (issue #9).
- Nome antigo "Gêmeo" ainda visível na instalação, no lugar de "Configuração da catraca"; resíduo de "Gmail" a procurar no build instalado.
- "Configuração da catraca" aparece em dois lugares: no Gêmeo e numa tela própria. Deve restar um só.
- Vocabulário técnico na tela principal (sessão, tentativa, fila, outbox, ingresso, mapa de giro, relógio).
- Quinze telas em lista plana, sem agrupamento.
- Simulador aparece só com modo simulação ligado (`U07` em `Telas.cs`); a regra deve ficar explícita.

**Proposta já discutida em princípio** (a validar, não a recomeçar):

| Grupo | Conteúdo previsto |
|---|---|
| OPERAÇÃO | Painel ao vivo · Acessos · Leituras recusadas (cartões não reconhecidos) · Consultar código |
| DISPOSITIVOS | Lista de catracas → por catraca: Visão geral · Configuração · Giro · Gerenciar (fechar/abrir, liberar, reconectar) |
| GESTÃO | Pessoas · Cartões (lotes) · Cadastro (empresas, salas, horários, feriados, perfis) · Prestação de contas |
| INFRAESTRUTURA | Sincronização com a nuvem · Configurações do evento · Diagnóstico |
| ADMINISTRAÇÃO | Usuários e papéis · Trocar minha senha |
| FERRAMENTAS | Simulador (só com simulação ligada) · Novidades e ajuda |

## 4. Agentes e funções

Cada agente entrega arquivos em `docs/` e não edita código de tela.

- **Orquestrador.** Abre tarefas, consolida as saídas, decide a ordem e monta a lista de decisões para o dono.
  Não escreve tela.
- **A1 · Arquiteto de informação.** Mapeia cada função a um lugar. Propõe a árvore final, aplica a regra
  "uma função, um lugar", aponta duplicatas e diz onde cada subtela pertence. Entrega E1 e E2.
- **A2 · Analista de tarefas do operador.** Escreve os roteiros reais, do início ao fim, com cliques e tempo:
  1. abrir o evento e ver se está tudo funcionando;
  2. descobrir que uma catraca parou;
  3. liberar uma pessoa que não tem cartão (com motivo);
  4. cadastrar um lote de cartões inteiros pela urna;
  5. cadastrar um cartão recusado;
  6. fechar uma catraca e reabri-la;
  7. fechar o dia e gerar a prestação de contas;
  8. trocar a própria senha;
  9. criar um operador de portaria que não vê o restante.
  Entrega E5.
- **A3 · Nomenclatura e textos.** Audita todo texto visível: títulos, botões, cabeçalhos de coluna, mensagens de
  erro e de estado vazio. Mantém o glossário. Nenhum termo fora do glossário no caminho principal. Entrega E3.
- **A4 · Responsividade e layout.** Testa cada tela em 1366×768, 1920×1080, 2560×1440 e 3840×2160, com DPI de
  100, 125, 150, 175 e 200%. Procura cortes em DataGrid, botões que somem, barra lateral que não cabe, rolagem
  horizontal e textos que quebram mal. Entrega E4 (parte layout).
- **A5 · Tema e estados.** Verifica uso exclusivo de tokens do `Rayzer.Design`; procura cores e fontes literais;
  confere claro, escuro e alto contraste; confere os estados de cada tela (carregando, vazio, erro, sem permissão,
  simulação, sem internet). Entrega E4 (parte tema).
- **A6 · Permissões e papéis.** Para cada item do menu, registra a permissão que o libera (`CodigosDePermissao`
  e `Permissoes`). Simula quatro papéis: administrador, supervisor, portaria e somente leitura. Confirma que o menu
  mostra só o permitido e que nenhuma tela quebra. Entrega E4 (parte permissões).
- **A7 · Acessibilidade e teclado.** Confere `AutomationProperties.Name` em todo controle, ordem de tabulação,
  atalhos para as ações frequentes e contraste mínimo.
- **A8 · Revisor adversarial.** Tenta perder um operador de propósito: procura o caminho em que ele não sabe o que
  fazer. Cada achado vem com passo a passo reproduzível, e a classificação da seção 7.

## 5. Glossário inicial (os agentes completam)

| Termo técnico | Como o operador lê | Onde fica |
|---|---|---|
| Gêmeo digital / Configuração da catraca | Configuração da catraca | DISPOSITIVOS › catraca |
| Giro / mapa de giro | Contagem de entrada e saída | DISPOSITIVOS › catraca › Giro |
| Sessão | (não aparece para o operador) | — |
| Tentativa / passagem | Acesso | OPERAÇÃO › Acessos |
| Cartões não reconhecidos | Leituras recusadas | OPERAÇÃO › Leituras recusadas |
| Lote de cartões | Lote de cartões | GESTÃO › Cartões |
| Fila de envio / outbox | Fila de envio | INFRAESTRUTURA › Sincronização |
| Ingresso | Ingresso | OPERAÇÃO › Consultar código |
| Relógio da catraca | Hora da catraca | DISPOSITIVOS › catraca › Visão geral (com alerta de diferença) |
| Simulador | Modo de teste | FERRAMENTAS (só com simulação ligada) |

## 6. Árvore-alvo (rascunho para validação)

```
OPERAÇÃO
  Painel ao vivo
  Acessos
  Leituras recusadas
  Consultar código
DISPOSITIVOS
  Catracas
    └ [nome da catraca]
        Visão geral · Configuração · Giro · Gerenciar
GESTÃO
  Pessoas
  Cartões (lotes)
  Cadastro (empresas, salas, horários, feriados, perfis)
  Prestação de contas
INFRAESTRUTURA
  Sincronização com a nuvem
  Configurações do evento
  Diagnóstico
ADMINISTRAÇÃO
  Usuários e papéis
  Trocar minha senha
FERRAMENTAS
  Simulador            (só com simulação ligada)
  Novidades e ajuda
```

Regras da árvore: a configuração de uma catraca mora só em DISPOSITIVOS › catraca; o título da subtela traz o nome
da catraca; o Simulador some fora do modo de teste.

## 7. Evidência e classificação

Cada item auditado recebe uma destas situações:

- **APROVADO:** foi executado e verificado, com a evidência anexada (captura, saída de teste ou caminho reproduzível).
- **PENDENTE:** depende de um passo ainda não feito (por exemplo, a compilação da tela WPF no CI do Windows).
- **BLOQUEADO:** depende de hardware ou de uma decisão do dono que ainda não existe (por exemplo, a urna física).
- **REPROVADO:** foi executado e não atende ao princípio.

Regras:
- Não declarar aprovado o que não foi executado.
- Não inventar captura nem resultado de teste.
- Visual no Windows fica PENDENTE até o CI ou o dono validar.
- Nenhuma captura pode conter dado pessoal real: usar só exemplos fictícios.

## 8. Entregáveis e critérios de aceite

| Entregável | Arquivo | Conteúdo |
|---|---|---|
| E1 | `docs/47-inventario-front-end.md` | Cada tela: título, caminho de menu hoje, permissão, dado que mostra |
| E2 | `docs/48-arquitetura-de-informacao.md` | Árvore final e a regra "onde fica" |
| E3 | `docs/49-glossario-e-textos.md` | Glossário completo e lista de textos a trocar |
| E4 | `docs/50-auditoria-responsiva-tema-e-papeis.md` | Tabela tela × resolução × DPI × tema × papel, com a classificação |
| E5 | `docs/51-roteiros-do-operador.md` | Os nove roteiros, com cliques e tempo medidos |
| E6 | `docs/52-mudancas-priorizadas.md` | Lista ordenada de mudanças de código, sem código ainda |

**Critérios de aceite** (o trabalho só termina quando todos forem verdadeiros):
- Cada roteiro do operador chega à função principal em até dois cliques, ou tem justificativa escrita.
- Nenhum termo técnico no caminho principal.
- Nenhuma função duplicada; "Configuração da catraca" aparece em um lugar só.
- Nenhuma cor literal em `Desktop.App`.
- Em 1366×768 com 100% e 125%, o menu e as telas principais cabem sem corte nem rolagem horizontal.
- Com o papel de portaria, "Usuários" não aparece, e a tela de Pessoas não quebra.
- Dono aprovou a árvore (E2) antes de qualquer alteração de código.

## 9. Restrições do projeto

- Continua em WPF. Não migrar para outra tecnologia.
- Nenhum SDK da Topdata no repositório (`EasyInner.dll`, exemplos, manuais).
- Nenhum dado pessoal real em código, captura ou planilha.
- Sem usuário oculto nem login paralelo (ADR-0026).
- A interface só mostra: a regra de decisão da catraca não muda.
- Commits sem nome de modelo nem trailer com nome de modelo; PR em rascunho.

## 10. Ordem de execução

1. A2 (roteiros) e A1 (inventário) em paralelo.
2. A3 (glossário) e A6 (papéis) em paralelo, usando o inventário.
3. A1 propõe a árvore → **o dono aprova ou ajusta** → só então segue.
4. A4, A5 e A7 auditam sobre a árvore aprovada.
5. A8 faz a revisão adversarial sobre o que foi produzido.
6. O orquestrador consolida E1–E6 e entrega ao dono a lista de decisões pendentes.

## 11. Como usar

Cole este documento como instrução do orquestrador. Os agentes leem `docs/` e o código de `src/Desktop.ViewModels`
e `src/Desktop.App`. Este prompt é o plano de análise: ainda não é o sistema pronto para o operador.
