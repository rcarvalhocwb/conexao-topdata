# Prompt: auditoria linha a linha do Rayzer XAcess, com especialistas

Você coordena uma auditoria técnica completa do repositório `rcarvalhocwb/conexao-topdata`, branch
`claude/gallant-wright-pdloor` (PR #1, último commit verificado `479d697`, CI todo verde). O
objetivo da auditoria é responder, com evidência, a duas perguntas:

1. **Em que condição real está cada função do sistema?**
2. **A que distância estamos do objetivo?**

**O objetivo:** integração total com as catracas Topdata TopFit 4 e o sistema operacional e
confiável num evento real. Isso significa que a credencial é lida, a decisão é tomada, a catraca
libera, o giro é confirmado, tudo é registrado e prestado contas, e o sistema continua de pé quando
algo falha.

A auditoria é **somente leitura**. Ninguém edita código, commita, faz push, abre PR nem comenta no
GitHub. A saída é um relatório.

## 1. Regras absolutas

1. **Toda afirmação tem evidência com `caminho:linha`.** Sem evidência, não é achado, é hipótese,
   e vai rotulada como hipótese.
2. **Não invente.** Não presuma comportamento de API, da DLL da Topdata, do Windows ou da nuvem.
   O que não está no código, nos documentos ou num teste executado é "não verificável aqui".
3. **Separe o que foi provado do que foi lido.** Ler o código prova o que o código diz, não o que a
   catraca faz. Os testes de `tests/HardwareInLoop` usam uma costura falsa no lugar da DLL. Nada
   aqui prova o hardware.
4. **Leia de verdade.** "Linha por linha" quer dizer que cada arquivo da seção 3 é lido inteiro, não
   amostrado nem resumido a partir do nome. Cada especialista registra quais arquivos leu (seção 6).
5. **Rode o que puder rodar e diga o que rodou.** Build e testes podem ser executados; registre o
   comando e o resultado. Não rode nada que altere o repositório ou fale com serviço externo.
6. **Não infle nem esvazie a severidade.** Use a escala da seção 5. Um achado que não se reproduz
   na revisão cruzada cai para hipótese.
7. **Não repita o levantamento antigo sem conferir.** `docs/29`, `docs/34`, `docs/36` e o
   levantamento de 07/10 são pontos de partida, não verdade: cada item citado deles é conferido no
   código atual, e o que já foi corrigido é marcado como corrigido, com o commit.
8. **Não exponha segredo nem dado pessoal** no relatório, mesmo que apareça em algum arquivo.

## 2. Contexto que todos leem antes de começar

- `README.md`, `docs/00-entendimento-e-escopo.md` (critérios CA-01 a CA-13), `docs/03-arquitetura.md`,
  `docs/29-o-que-falta.md`, `docs/34-estudo-modulo-catraca.md`, `docs/38-prompt-de-producao.md`.
- `docs/ADR/` (26 decisões), o contrato `src/Contracts/Protos/edge_control.proto`, o
  `.github/workflows/ci.yml` e as 16 migrações em `src/Access.Infrastructure.SQLite/Migrations/`.
- Fato de partida: **a catraca real nunca foi acionada por este sistema.** Ninguém executou o
  HIL-STACK-01.

## 3. Os especialistas e o que cada um lê

Cada especialista é um agente independente, de alto conhecimento na sua área, que lê **todos** os
arquivos `.cs` e `.xaml` (fora de `bin/` e `obj/`) dos projetos atribuídos. Tamanhos medidos no commit
`479d697`.

| # | Especialista | Projetos (linhas de C# / XAML) | Foco |
|---|---|---|---|
| E1 | **Engenheiro de integração Topdata** | `Topdata.EasyInner.Interop` (1.342), `Topdata.EasyInner.Adapter` (589), `Edge.Worker` (2.906), `Edge.Worker.X86` (491), `Simulator` (816) | Cada P/Invoke contra a convenção da DLL (StdCall, tipos, tamanho de buffer, x86); cada código de retorno tratado; máquina de estados da catraca; o laço do worker; prazos, reconexão e watchdog; o que o simulador faz de diferente da DLL real |
| E2 | **Arquiteto de serviço e concorrência** | `Edge.Supervisor` (5.657), `Contracts` (447), `Shared.Observability` (344) | Ciclo de vida dos processos, supervisão e quarentena, threads e cancelamento, condições de corrida, IPC e token, permissões do Windows, serviços em segundo plano, o que acontece em cada falha |
| E3 | **Engenheiro de dados e persistência** | `Access.Infrastructure.SQLite` (6.247), as 16 migrações, `Sync.Core` (585) | Transações, atomicidade, WAL e `busy_timeout`, migrações e reversão, índices, idempotência, deduplicação, fila de saída, cópia de segurança e restauração |
| E4 | **Especialista em regras de acesso e domínio** | `Access.Domain` (1.177), `Access.Application` (3.440), `Access.Importacao` (1.690) | A decisão de liberar ou negar: cada caminho, cada regra (urna, reuso, janela, categoria, provedor), valores-limite, normalização de código (zeros à esquerda, dígitos), o que nega por falha e o que libera por engano |
| E5 | **Engenheiro de integrações externas** | `Sync.Connectors.Rest` (1.584), `Sync.Ingestao` (317), `Relay.Ingressos` (370) | Contrato com a nuvem e com a Zet, retentativas, cartas mortas, tempo esgotado, TLS, segredos, assinaturas, o que entra sem validação |
| E6 | **Especialista em interface e operação** | `Desktop.ViewModels` (8.981), `Desktop.App` (2.817 / 2.199 XAML), `Edge.Configurador` (566 / 185), `Rayzer.Design` (797 / 2.239) | Cada tela e cada comando: o que o operador vê, se o texto é verdadeiro, estados de erro, confirmação em ação perigosa, ligação entre XAML e ViewModel, acessibilidade, o que acontece quando o serviço cai |
| E7 | **Especialista em camada inteligente** | `Access.Inteligencia` (2.479) e o `AnalisadorDaOperacao` | Isolamento do caminho de decisão (nada pode bloquear ou mudar a liberação), chaves desligadas, RPCs `Unimplemented`, custo por ciclo |
| E8 | **Especialista em segurança e LGPD** | Todos os projetos, transversal, mais `installer/` e `ci.yml` | Dado pessoal em log, tela, CSV e zip; segredos; permissões; superfície de ataque local e remota; base legal, retenção e expurgo |
| E9 | **Engenheiro de qualidade e testes** | `tests/` inteiro (30.209 linhas: Unit 8.865, Integration 16.429, HardwareInLoop 3.045, Contract 1.373, LoadAndSoak 248, CrashProbe 249) e `ci.yml` | O que cada teste prova de fato; testes que só leem texto; asserções fracas ou impossíveis de falhar; o que o CI roda no Windows e no Linux; lacunas entre função e teste |
| E10 | **Engenheiro de entrega** | `installer/` (WiX, PowerShell), `ci.yml`, `Directory.Build.props` | Instalação limpa, atualização, desinstalação, serviço, firewall, scripts do operador, o que vai e o que não vai no MSI, assinatura |

**Revisão cruzada:** todo achado de severidade CRÍTICO ou ALTO é relido por um segundo especialista
(regra: E1↔E2, E3↔E4, E5↔E8, E6↔E9, E7↔E2, E10↔E8). Se o revisor não reproduzir o achado no código,
ele cai para hipótese e o desacordo vai para o relatório.

## 4. Como cada especialista lê

Para cada arquivo:

1. Ler o arquivo inteiro, de cima a baixo.
2. Para **cada função pública e cada função privada com lógica de decisão, I/O, concorrência ou
   P/Invoke**, registrar:
   - o que ela promete (nome, comentário, documento que a cita);
   - o que ela faz de fato, linha por linha;
   - entradas fora do esperado: nulo, vazio, limite, negativo, muito grande, texto com acento,
     fuso, horário de verão, arquivo travado, disco cheio, sem permissão, cancelamento no meio;
   - o que acontece quando o que ela chama falha;
   - quem a chama, e se alguém a chama (código morto conta);
   - qual teste a exercita (nome do teste) e se o teste falharia caso a função estivesse errada.
3. Comparar comentário e documentação com o código. Divergência é achado.
4. Registrar cada achado no formato da seção 5.

Comandos permitidos para apoiar a leitura (registre os que usar):
`dotnet build`, `dotnet test` por projeto, `git log`, `git blame`, `grep`/`rg`, `gh api` só de
leitura no repositório. Nada que grave no repositório ou em serviço externo.

## 5. Formato do achado

```
ID: E<n>-<sequência>          Severidade: CRÍTICO | ALTO | MÉDIO | BAIXO | INFORMATIVO
Arquivo: caminho:linha(s)
Função: Classe.Metodo
O que o código faz: (fato, citando a linha)
Por que é problema: (consequência concreta na operação do evento)
Como reproduzir ou provar: (teste existente, teste que deveria existir, entrada que dispara)
Estado: CONFIRMADO (com revisão cruzada) | HIPÓTESE | NÃO VERIFICÁVEL AQUI (precisa de bancada, VM ou nuvem)
Correção sugerida: (o mínimo necessário; sem implementar)
```

Escala de severidade:

- **CRÍTICO**: pode liberar quem não deveria, deixar de liberar quem deveria sem aviso, perder acesso
  registrado, expor dado pessoal ou segredo, ou parar todas as catracas.
- **ALTO**: falha que derruba uma catraca, um grupo ou a prestação de contas, ou que o operador não
  consegue perceber nem resolver.
- **MÉDIO**: comportamento errado com contorno conhecido, ou robustez abaixo do que o documento promete.
- **BAIXO**: clareza, manutenção, texto, código morto sem risco.
- **INFORMATIVO**: observação sem defeito.

## 6. Cobertura obrigatória

Cada especialista entrega uma tabela com **todos** os arquivos atribuídos:

| Arquivo | Linhas | Lido inteiro (sim/não) | Funções analisadas | Achados |
|---|---|---|---|---|

A auditoria só termina quando 100% dos arquivos `.cs` e `.xaml` de `src/` estiverem marcados como
lidos inteiros por pelo menos um especialista. Se faltar algum, o relatório diz quais e por quê.

## 7. A distância até o objetivo

Depois dos achados, o coordenador monta o placar das capacidades do objetivo. Para cada capacidade,
um único estado, com evidência:

- **PROVADO EM HARDWARE**: executado com uma TopFit 4 real, com registro. (Hoje esperado: nenhuma.)
- **PROVADO EM CI**: teste automatizado que falharia se a capacidade quebrasse, verde no último commit.
- **IMPLEMENTADO SEM PROVA**: o código existe, mas nenhum teste ou ensaio o prova.
- **PARCIAL**: falta parte da capacidade (dizer qual).
- **AUSENTE**: não existe.
- **BLOQUEADO POR DECISÃO OU TERCEIRO**: dizer quem decide e o quê.

Capacidades do objetivo:

1. Carregar a `EasyInner.dll` no worker de 32 bits e conectar à catraca.
2. Configurar a catraca (leitor, dígitos, relés, display, sequência oficial de conexão).
3. Ler QR e cartão de proximidade e normalizar o código.
4. Decidir: ingresso válido, reuso, janela, categoria, provedor, urna.
5. Liberar o braço no sentido certo, por função e por catraca.
6. Confirmar o giro (origem 6) e encerrar a tentativa sem giro no prazo.
7. Registrar cada acesso sem perder e sem duplicar, inclusive com queda de processo.
8. Operar sem o PC e recuperar o que a catraca guardou (coleta de bilhetes).
9. Detectar catraca sem comunicação, socket morto e worker travado, e se recuperar.
10. Sincronizar cartões e acessos com a nuvem, com fila, retentativa e cartas mortas.
11. Receber ingressos da Zet e conciliar com a bilheteria.
12. Liberação manual, mensagem no display, relógio, aplicar configuração, com auditoria.
13. Prestação de contas: resumo, relatórios R1–R8, PDF, corte fechado.
14. Painel do operador: telas, textos verdadeiros, erros com próximo passo, confirmação em ação perigosa.
15. Alertas ao operador.
16. Segurança local (canal, token, pasta de dados) e da nuvem.
17. Proteção de dado pessoal (LGPD).
18. Instalação, atualização, desinstalação, cópia de segurança e restauração.
19. Diagnóstico e runbooks para o suporte.
20. Ensaios de longa duração, caos e carga.

Depois do placar:

- **Percentual por estado**, sem ponderação inventada: quantas capacidades estão em cada estado, de 20.
- **Caminho crítico até o objetivo:** a sequência mínima de itens que leva cada capacidade a
  PROVADO EM HARDWARE, com o tipo de cada um (código, bancada, VM, decisão, terceiro) e quem resolve.
- **Estimativa de esforço**, rotulada como estimativa, em dias de desenvolvimento e de bancada,
  separando o que depende de terceiros (sem prazo controlável).
- **Resposta direta:** "Estamos muito longe do objetivo?" em uma frase, seguida da justificativa
  com os números do placar.

## 8. Entregáveis

1. `docs/41-auditoria-linha-a-linha.md`, com:
   - resumo executivo (uma página): estado geral, os 10 achados mais graves e a resposta da seção 7;
   - os achados por especialista, ordenados por severidade;
   - os desacordos da revisão cruzada;
   - as tabelas de cobertura (seção 6);
   - o placar e o caminho crítico (seção 7);
   - a lista do que não foi possível verificar e o que seria preciso para verificar;
   - os comandos executados e os resultados.
2. Uma lista curta de **perguntas de decisão** para o responsável pelo projeto, em formato de
   múltipla escolha, cada uma com a opção recomendada marcada e o motivo.

Não escreva "pronto para produção" nem "100%" no relatório se o placar não sustentar isso com
evidência. Não crie código, não corrija nada e não abra PR: a correção é uma etapa seguinte,
decidida a partir deste relatório.

## 9. Critério de parada

Pare e relate se: (a) não conseguir ler algum arquivo; (b) o build ou os testes falharem antes da
auditoria (registre a falha e audite mesmo assim, marcando o que ficou sem prova); (c) uma verificação
exigir hardware, VM Windows, acesso à nuvem ou credencial que você não tem. Nesses casos, registre o
que faltou e siga com o resto.
