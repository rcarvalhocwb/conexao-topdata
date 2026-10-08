# Prompt: fechar o Rayzer XAcess para operação real, sem invenção

Você é o engenheiro responsável por fechar o Rayzer XAcess para operação real num evento com
catracas TopFit 4. Repositório `rcarvalhocwb/conexao-topdata`, branch `claude/gallant-wright-pdloor`,
PR #1. Trabalhe no código, nos testes, no instalador, na documentação e no CI. Tudo que depender de
hardware, de uma máquina Windows limpa, de uma decisão de negócio ou de um terceiro deve ser
**declarado como bloqueado, com dono e critério**, e não resolvido por simulação.

## 1. Regras absolutas

1. **Não invente.** Nenhuma funcionalidade, texto, número, tela, ensaio ou resultado que não exista
   de verdade no código ou não tenha sido executado. Se você não rodou, diga que não rodou.
2. **Não simule e não rotule como prova o que não é.** Simulador, costura falsa (`HardwareInLoop`)
   e teste de texto provam o código, não a catraca nem o Windows. Nunca escreva que algo "funciona
   com a catraca" ou "está validado no Windows" sem essa execução.
3. **Não enfraqueça verificação.** Não altere asserção para fazer teste passar, não marque como
   ignorado, não remova etapa do CI, não troque um teste de comportamento por um de texto para
   ficar verde. Corrija a causa.
4. **Texto para o operador só afirma o que o sistema faz hoje.** Se o comportamento depende de
   uma decisão pendente (fail-safe, contingência, urna, hora de corte), o texto diz que é pendente.
5. **Não ligue chave técnica desligada** (`catraca.sequencia_oficial`, `catraca.coletar_bilhetes`,
   `inteligencia.ligada`, liberação em dois sentidos, `catraca.enviar_digitos_variaveis`) sem o
   ensaio ou a decisão que a libera. Registre o motivo no relatório.
6. **Não decida por negócio.** D3, D4, D5, D8, E3, E5, E9, E10, meia-entrada, caminho da venda
   on-line e quem opera são decisões das pessoas responsáveis. Implemente a mecânica quando a
   regra já estiver escrita nos documentos; caso contrário, pare nesse ponto e registre.
7. **Nunca publique material da Topdata** (DLLs, exemplos, `EasyInner` gerado, manuais, `CHECKSUMS`
   de DLL) no repositório. O SDK fica no repositório privado descrito em `installer/privado/`.
8. **Commits**: mensagem em português, descrevendo o que mudou e o que foi verificado. Termine com
   a linha `Claude-Session: https://claude.ai/code/session_01QKU6H2xGWvy1cxMQ5yybk3`. Não coloque
   nome de modelo em commit, PR, comentário, documento ou artefato. Não reescreva histórico. Não
   faça push para outra branch sem autorização.

## 2. Estado de partida (08/10/2026)

- Último commit: `39a876a`. Rodada anterior: `3a6cc94`, `aa6faed`.
- Testes locais, rodados por projeto, todos passando: Unit 857, Integration 673, Contract 58,
  HardwareInLoop 154 (simulados).
- CI `39a876a`: Linux, Windows, Rayzer UI, soak e varredura de dado sensível passaram. O job
  **Instalador MSI (Windows)** estava em execução: confirme o resultado antes de qualquer outro passo.
- A catraca real **nunca foi acionada** pelo sistema. Nenhum ensaio de bancada foi feito.
- Já implementado, com teste, mas **não verificado numa VM Windows**:
  - canal local restrito ao grupo `ConexaoTopdata Operadores` (`src/Edge.Supervisor/SegurancaLocal.cs`);
    script `installer/configurar-operador.ps1`, incluído no MSI;
  - cópia de segurança de `acesso.db` com verificação e retenção
    (`src/Access.Infrastructure.SQLite/CopiaDeSeguranca.cs`, `src/Edge.Supervisor/AgendadorDeCopias.cs`);
  - mensagem de falha centralizada (`src/Desktop.ViewModels/MensagemDeFalha.cs`), com a causa
    "falta o grupo" afirmada só quando o token existe e não pode ser lido.
- Runbooks escritos: `docs/runbooks/RB-01`, `RB-02`, `RB-06`, `RB-09`. Os demais estão pendentes.

## 3. Passo 0: confirmar o que está de fato verificado

Antes de codar, confirme em `gh api repos/rcarvalhocwb/conexao-topdata/commits/<sha>/check-runs`
que todos os jobs do último commit estão `success`. Se algum falhar, corrija-o primeiro: nada novo
entra sobre CI vermelho.

Gates que todo commit deve passar:

```
dotnet build src/Desktop.App/Desktop.App.csproj
dotnet test tests/Unit/Unit.Tests.csproj
dotnet test tests/Contract/Contract.Tests.csproj
dotnet test tests/HardwareInLoop/HardwareInLoop.Tests.csproj
dotnet test tests/Integration/Integration.Tests.csproj
```

Valide YAML dos workflows e o XML do `.wxs`. Os blocos PowerShell só são validados de verdade no
runner Windows: se você mexer neles, confirme o job correspondente.

## 4. Inventário: o que falta, com critério de aceite

Tipo: **CÓDIGO** (fazer e testar aqui) · **WINDOWS** (só no CI Windows ou VM) · **BANCADA**
(catraca real) · **DECISÃO** (pessoa responsável) · **EXTERNO** (terceiro) · **PESSOAS** (operador real).

### 4.1 Segurança e dados

| ID | O que falta | Critério de aceite | Tipo |
|---|---|---|---|
| S03 | Confirmar o canal e o token em Windows | Conta comum fora do grupo não lê o token nem conecta; conta no grupo conecta; `configurar-operador.ps1` cria o grupo e reinicia o serviço | WINDOWS |
| S04 | ACL da pasta de dados do serviço | Só SYSTEM e Administradores acessam `acesso.db`, o cofre e a cópia de segurança; teste em VM | CÓDIGO + WINDOWS |
| S05 | SBOM e verificação de hash das DLLs | Serviço recusa subir se a DLL da catraca não bate com a referência; SBOM gerado no CI | CÓDIGO (hash de referência vem da Topdata) |
| S07 | Redação de logs | Coberta para os nomes de campo do banco e do contrato; ampliar só com teste que injeta cada campo | CÓDIGO (feito, falta revisão) |
| S08 | Trilha de auditoria de cartões | `TrilhaDeCredenciais` gravada em toda edição manual de cartão; teste de que edição sem trilha falha | CÓDIGO (depende de B.6/B.7) |
| S09 | Identidade de quem opera | Decisão sobre login ou conta Windows; até lá, o campo "Operador" continua texto livre e a tela diz isso | DECISÃO |
| S10 | Contrato de equipamento | Decidir um segredo por máquina com lista de `device_id` ou um por catraca; alinhar `docs/31` e o código; teste de integração | DECISÃO + CÓDIGO |
| S11 | Relé de webhook da Zet | Token sai da URL e vai para cabeçalho; corpo com assinatura verificada; teste que recusa assinatura errada | CÓDIGO |
| S12 | Varredura de dado sensível no CI | Cobre `Console.WriteLine`, `erro.ToString()` gravado em arquivo e CSV; reprova dado plantado de propósito | CÓDIGO |
| S13 | Documentação de segurança | `docs/03 §11` só descreve controles que existem | CÓDIGO (texto) |
| S01 | Políticas `anon` da nuvem (Supabase) | Consultas com a chave pública retornam zero linhas de dado pessoal | EXTERNO (dono do painel) |
| S02 | Funções de sincronização da nuvem sem segredo | Sem segredo ou com segredo errado, resposta 401 | EXTERNO (dono do painel) |
| S06 | LGPD: base legal, retenção, expurgo e relatório ao titular | RIPD aprovado; expurgo testado; conflito com o corpo bruto da Zet resolvido | DECISÃO + PESSOAS (jurídico) |

### 4.2 Instalação, atualização e operação do serviço

| ID | O que falta | Critério de aceite | Tipo |
|---|---|---|---|
| A01 | Instalação limpa sem internet | Setup concluído numa VM sem internet, com .NET 3.5 pré-habilitado ou fonte offline documentada; worker responde | WINDOWS |
| A02 | Atualização N para N+1 | Base populada sobrevive; cópia feita antes da migração; procedimento de volta executado | WINDOWS |
| A03 | Atualização com catracas em operação | Regra escrita no instalador e aviso no Setup; serviço religa ao fim; worker órfão não contamina a base | CÓDIGO + WINDOWS |
| A04 | Desinstalação e dados de eventos anteriores | Arquivamento e limpeza com confirmação; teste em VM | CÓDIGO + WINDOWS |
| A06 | Retenção de logs do serviço | Limpeza por idade e tamanho; teste; alerta de disco ligado a A10 | CÓDIGO |
| A07 | Pacote de diagnóstico (RB-12) | Botão exporta zip sanitizado (log, versão, pré-requisitos, `workers.json` sem segredo); teste que procura segredo e número de cartão no zip | CÓDIGO |
| A10 | Alertas da Fase 5 | Para cada situação de `docs/24 §1` que o código consegue detectar: aviso e botão "ciente", com teste que provoca a situação; situações sem sinal real ficam listadas como pendentes | CÓDIGO |
| A11 | Firewall e porta errada | Detecta porta de outro grupo e catraca fora da sub-rede; RB-02 cobre o que fazer | CÓDIGO + WINDOWS |
| A12 | Fuso do Windows | Aviso quando o Windows não está em Brasília; nomes de arquivo de log pela hora de Brasília | CÓDIGO |
| A13 | Assinatura de código | Setup, MSI e executáveis assinados com carimbo de tempo; CI verifica `Get-AuthenticodeSignature` | EXTERNO (certificado) |
| A14 | Cadastro de cartões B.4 a B.6 | Importação em lotes curtos, desfazer a última importação, carga com a catraca lendo sem travar | CÓDIGO + WINDOWS |
| A15 | Aviso de novidades | Teste no CI que confere se a edição foi aumentada quando a versão traz mudança operacional | CÓDIGO |
| A08 | Runbooks restantes | RB-03, RB-04, RB-05, RB-07, RB-08, RB-10, RB-11, RB-12 escritos **só** quando o comportamento que descrevem existir; RB-10 depende de T07 | CÓDIGO (parcial) |

### 4.3 Experiência do operador

| ID | O que falta | Critério de aceite | Tipo |
|---|---|---|---|
| U04 | Urna, cartão preso, catraca travada | Textos descrevem só o que o sistema faz hoje; estado "catraca travada" existe se o worker o detecta | CÓDIGO |
| U05 | Roteiro do dia e manual do operador | Checklist de abertura e encerramento baseado nas telas reais; validar com operador | CÓDIGO + PESSOAS |
| U06 | Teste com cinco operadores (UX-01) | Cinco tarefas cronometradas com registro | PESSOAS |
| U07 | Simulação na prestação | Linhas e CSV marcam simulação; Simulador oculto fora do modo simulação; autoteste de instalação real falha se a simulação estiver ligada | CÓDIGO |
| U08 | Termos técnicos visíveis | Varredura automática de textos de tela contra um glossário; zero ocorrências | CÓDIGO |
| U10 | Botões desabilitados e erros sem próximo passo | Cada botão desabilitado diz o que falta; erro de campo aparece no campo | CÓDIGO |
| U11 | Acessibilidade | Palco 3D responsivo; modo técnico global; teste de navegação por teclado; Narrador ou NVDA validado em 1366×768 e 200% | CÓDIGO + PESSOAS |
| U12 | Autoria e tela Sobre | `Directory.Build.props` com Product "Rayzer XAcess"; tela Sobre com versão (contato só com dado aprovado) | CÓDIGO |
| U13 | Bandeja | Rótulos do menu correspondem ao que cada item faz de verdade | CÓDIGO |
| U14 | Nome das catracas | Um padrão de nome em todas as telas, com teste | CÓDIGO |
| U15 | Gêmeo digital | Selo de demonstração e pergunta ao operador validados | PESSOAS |
| U16 | Carregamento | Indicador com rótulo | CÓDIGO |

### 4.4 Prestação de contas e camada inteligente

| ID | O que falta | Critério de aceite | Tipo |
|---|---|---|---|
| P01 | Relatórios R1–R8 | Cada um definido em `docs/25` e implementado com teste; os que não puderem ser definidos ficam como "Ainda não disponível" | CÓDIGO + DECISÃO |
| P02 | PDF com autoria | Cabeçalho, "página N de M", campo de assinatura; conferido contra o CSV | CÓDIGO |
| P03 | Hora de corte (E9) | Configurável; o valor padrão só entra com decisão registrada | DECISÃO + CÓDIGO |
| P04 | Pessoas distintas | Contagem pela regra de ingresso escrita; teste com ingresso reutilizado | CÓDIGO |
| P05 | Validado × liberado | Renomear "AUTORIZADOS"; três contagens (liberado, com giro, sem passagem) | CÓDIGO |
| P06 | Corte fechado | Snapshot, hash SHA-256 e código de conferência; reproduzível | CÓDIGO |
| P07 | Maquininha e cortesia (Zet) | Fonte definida pela Zet; conciliação com teste | EXTERNO + CÓDIGO |
| P08 | Caminho da venda on-line | Decisão A, B ou C; implementação do caminho escolhido | DECISÃO + CÓDIGO |
| P09 | Meia-entrada (E10) | Campo de cota e número no R1 quando houver regra | DECISÃO |
| P10 | Liberações manuais nos números | Linha própria no relatório, com teste | CÓDIGO |
| P11 | Autoria do relatório | Evento, fornecedor, versão e responsável em todo relatório | CÓDIGO |
| P12 | Disponibilidade e pico | Histórico de disponibilidade gravado; métricas testadas | CÓDIGO |
| P13 | Camada inteligente desligada | Já resolvido e testado; manter | verificado |
| P14 | RPCs de inteligência `Unimplemented` | Implementar o que `docs/36` define, ou remover do contrato com teste de contrato | CÓDIGO |
| P15 | Ensaios da camada (LOAD-IA-01, SOAK-IA-24H) | Só entram com a camada ligada; enquanto isso, documentados como pendentes | BANCADA |

### 4.5 Qualidade, CI e documentação

| ID | O que falta | Critério de aceite | Tipo |
|---|---|---|---|
| Q07 | Testes que leem texto | Substituir os de texto de documentação por comportamento; manter os que comparam CSV com código | CÓDIGO |
| Q08 | Falha de captura de tela | Captura que falha reprova o CI; `Assert` sobre 1366×768 real | CÓDIGO |
| Q09 | Demonstração para a Topdata | Parte A (software) só com o que existe; parte B depende de bancada | CÓDIGO |
| T19 | Documentos que contradizem o código | `docs/07`, `ADR-0016`, contagem de exports, referência à `docs/36`: corrigidos | CÓDIGO (texto) |
| — | Repositório público com material derivado do SDK | Decisão jurídica sobre `EasyInnerGerada.cs`; até lá, nada é removido sem autorização | DECISÃO + EXTERNO |

### 4.6 Bancada, ambiente e decisões que não se resolvem por código

| ID | O que falta | Tipo |
|---|---|---|
| T01 | HIL-STACK-01: o worker .NET 10 x86 carrega a `EasyInner.dll` numa catraca real; registrar em `docs/12` | BANCADA |
| T02 | Plano B em .NET Framework 4.8 existe só no papel; decidir e criar, se T01 falhar | DECISÃO + CÓDIGO |
| T03 | Dígitos variáveis não enviados por padrão (`ConfiguracoesDaBorda.cs:95`) | BANCADA |
| T04 | Tipo de leitor e formato de QR e cartão (`docs/21 §3`) | BANCADA + DECISÃO |
| T05 | Retorno diferente de zero tratado como "sem evento"; queda de socket sem reconexão | BANCADA |
| T06 | Sentido do giro e liberação por função em campo | BANCADA |
| T07 | Fail-safe × fail-secure (D5, ADR-0013) | DECISÃO |
| T08 | O que a catraca faz sem o PC (E3, D8) | DECISÃO |
| T09 | Autorização escrita da Topdata para redistribuir a DLL e as de apoio | EXTERNO |
| T10 | Firmware homologado {14, 16} sem fonte | BANCADA + EXTERNO |
| T11 | Urna e recolhimento de cartão (relé 2) | BANCADA + EXTERNO, se houver urna |
| T12 | Coleta de bilhetes (`catraca.coletar_bilhetes` desligada) | BANCADA |
| T13 | Respostas da Topdata às perguntas T1–T35 | EXTERNO |
| T14 | Ensaios pendentes do `docs/21` | BANCADA |
| T15 | Queda de worker e quarentena sem ação do operador | CÓDIGO + BANCADA |
| T16 | Defaults da DLL em itens de segurança (cartão master, WebServer) | BANCADA + EXTERNO |
| T17 | Número de cartão no display | BANCADA |
| T18 | Lista de acesso na catraca (Etapa D) | DECISÃO + BANCADA |
| T20 | Capacidade por worker e NDA (T1) | EXTERNO |
| Q01 | Ensaios com hardware no CI, que hoje não existem | BANCADA |
| Q02 | Ensaios de caos, carga e desempenho: CHAOS-WAN-01, CHAOS-PWR-01, LOAD-SYNC-01, HIL-PERF-01 | BANCADA + VM (tempo corrido) |
| Q03 | SOAK de 72 h em runner próprio com DLL real | VM (72 h corridas) |
| Q04 | Aceite CA-01 a CA-13 com situação e assinatura | DECISÃO + PESSOAS |
| Q05 | Homologação por modelo e firmware (`docs/compatibility-matrix`) | BANCADA |
| Q06 | Upgrade, reversão e restauração (REL-01 a REL-05) | VM |
| A13 | Assinatura de código | EXTERNO |

## 5. Ordem de trabalho

1. Passo 0 (seção 3). Não avance com CI vermelho.
2. Os itens **CÓDIGO** sem decisão pendente, em ordem de risco operacional: A07, A10, A06, A11, A12,
   U10, U12, U13, U14, U07, U08, A03 (texto e regra), P05, P10, P11, P12, P04, P02, P01 nos relatórios
   já definidos em `docs/25`, S11, S12, S04 (código), Q07, Q08, T19, S13. Um commit por item.
3. Os itens **WINDOWS**: rodar no CI Windows; quando não for possível reproduzir no CI, deixar o
   roteiro escrito e marcar como pendente de VM.
4. Os itens **DECISÃO** e **EXTERNO**: não implementar a regra. Escrever no relatório a pergunta
   exata, quem responde e o que cada resposta muda no código.
5. Os itens **BANCADA**, **VM**, **PESSOAS**: deixar o roteiro e o critério prontos, sem marcar como feitos.

## 6. Relatório final obrigatório

Criar `docs/39-relatorio-de-prontidao.md` com uma linha por item da seção 4 e um dos estados:

- **VERIFICADO**: com o commit, o nome do teste ou o job do CI que comprova. Sem essa evidência,
  o estado não é VERIFICADO.
- **IMPLEMENTADO, NÃO VERIFICADO**: código pronto, sem a execução que o confirma (por exemplo, Windows).
- **BLOQUEADO**: com o tipo, o dono e o que destrava.
- **PENDENTE-DECISÃO**: com a pergunta exata e quem responde.

Não escreva "100% operacional" no relatório. Escreva quantos itens estão VERIFICADOS, quantos
IMPLEMENTADOS SEM VERIFICAÇÃO e quantos BLOQUEADOS, com a lista de bloqueios. A produção só deve
acontecer depois de decisão humana sobre esse relatório.

## 7. Critério de parada

Pare e relate quando: (a) um item exigir decisão de negócio ou terceiro; (b) um teste falhar e a
causa não estiver no seu alcance; (c) um passo exigir hardware ou uma máquina Windows que você não
tem. Nessas situações, não contorne com simulação. Escreva o que foi tentado, o que falhou e o que
você precisa.
