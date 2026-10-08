> Relatório bruto de um especialista da auditoria linha a linha (docs/40), commit auditado 29df501.
> Os estados aqui são os do especialista, antes da revisão cruzada; o consolidado é o docs/41.

# E10 — Engenheiro de entrega: relatório (commit 29df501, somente leitura)

**O que eu rodei.** Read, Grep, git log e `gh api` só de leitura: releases, workflow runs, jobs do run 157 e check-runs de 29df501. Não rodei build, test, WiX nem PowerShell. O comportamento de WiX, MSI, SCM e PowerShell é NÃO VERIFICÁVEL AQUI, salvo o que o job "Instalador MSI (Windows)" já executou. No run 161 (29df501) os seis check-runs estão verdes.

**Estado dos achados.** Nenhum achado tem revisão cruzada ainda (o par do E10 é o E8). "CONFIRMADO" abaixo quer dizer confirmado na leitura do código, pendente da revisão do E8.

## A) Achados

### ALTO

**E10-1 | ALTO | installer/wix/ConexaoTopdata.wxs:152-156, :31, :74-79; tema-pt-BR.wxl:48; installer/README.md:112-115**
- **O que faz:** o `ServiceControl` só tem `Stop="both" Remove="uninstall"`, sem `Start`. O `MajorUpgrade` (wxs:31) desinstala o produto antigo, o que para e apaga o serviço, e depois instala o novo sem iniciá-lo. O `CloseApplication` fecha o painel e a bandeja, e a bandeja só volta no próximo logon (chave Run, wxs:207).
- **Por que é problema:** depois de uma atualização N→N+1, todas as catracas ficam paradas até alguém iniciar o serviço ou reiniciar o PC. A tela final do Setup ainda diz "O serviço das catracas inicia com o Windows…" (wxl:48), e o README (112-115) não avisa.
- **Como provar:** instalar 0.1.N, configurar, rodar o Setup 0.1.N+1 e conferir `Get-Service ConexaoTopdataEdge`, que deve ficar `Stopped`. O CI não tem teste de atualização: ci.yml:339-411 só faz `/i` e `/x`.
- **Estado:** CONFIRMADO no código. A execução é NÃO VERIFICÁVEL AQUI.
- **Correção mínima:** uma ação adiada `sc.exe start ConexaoTopdataEdge` com `Return="ignore"`, condicionada a `WIX_UPGRADE_DETECTED` e à existência do workers.json. Mais um teste de atualização no CI.

**E10-2 | ALTO | installer/privado/publicar-com-sdk.yml:95,129,167; .github/workflows/ci.yml:143,290; ConexaoTopdata.wxs:20,31**
- **O que faz:** o MSI público e o MSI privado (com SDK) usam o mesmo `UpgradeCode` e a versão `0.1.$GITHUB_RUN_NUMBER`. O contador de runs de cada repositório é independente: o público já está em 0.1.161 (release teste-0.1.161), e o privado começa em 0.1.1.
- **Por que é problema:**
  - **(a)** O MSI de produção com SDK não instala por cima de um instalador de teste: o MajorUpgrade recusa com "Já existe uma versão mais nova" (wxs:31).
  - **(b)** Qualquer instalador de teste público instalado depois da produção é tratado como upgrade e remove a EasyInner.dll, que é arquivo do MSI antigo. Todos os grupos vão para quarentena.
- **Como provar:** comparar as linhas citadas com as releases `teste-0.1.161` (público) e com o run 1 do privado.
- **Estado:** CONFIRMADO no código. O efeito do MSI é NÃO VERIFICÁVEL AQUI.
- **Correção mínima:** um esquema de versão separado para produção (ex.: 1.0.N), ou um `UpgradeCode` distinto para o instalador de teste.

**E10-3 | ALTO | installer/verificar-ambiente.ps1:18,34,53-56,72-75,79-86; docs/runbooks/RB-01-worker-nao-inicia.md:18-22**
- **O que faz:** o script entrou no MSI no commit 39a876a, mas continua escrito para o layout de desenvolvimento.
  - Instalado em `C:\Program Files\Rayzer\XAcess`, `$raiz` aponta para `C:\Program Files\Rayzer` e procura `artifacts\Edge.Worker.X86\…`. Sempre dá "conferir" e manda rodar `publicar.ps1`, que não vai no MSI (wxs:225-226).
  - A DLL só é procurada em SysWOW64/System32 (linhas 72-75). O assistente procura também na pasta do worker (AssistenteDeConfiguracao.cs:226-231), onde a DLL fica no MSI de produção e onde o botão "Localizar" a copia. Resultado: FALHA e uma citação a `vendor/topdata/README.md`, que não existe na máquina.
  - Se o worker está escutando na porta 3570, que é o normal, o script dá FALHA e diz "Feche-o" (linhas 81-82).
- **Por que é problema:** o RB-01, passo 2, exige "todas as linhas dizem ok", o que é impossível. Em pleno incidente o técnico é mandado matar o worker, reinstalar o SDK e "repetir este passo", em laço.
- **Como provar:** ler as linhas citadas. O teste InstaladorTests.cs:47-57 compara só os IDs, não a lógica.
- **Estado:** CONFIRMADO no código. A execução é NÃO VERIFICÁVEL AQUI.
- **Correção mínima:**
  - Usar `$PSScriptRoot\Worker\Edge.Worker.X86.exe` e incluir a pasta do worker na busca da DLL.
  - Tratar como ok a porta ocupada pelo próprio `Edge.Worker.X86`.
  - Ajustar o critério do RB-01.

### MÉDIO

**E10-4 | MÉDIO | .github/workflows/ci.yml:113-121, 562-563; installer/README.md:71-79**
- **O que faz:** o job `instalador` não tem `needs:` e publica uma pré-release pública mesmo quando os testes falham.
- **Como provar:** no run 157 (02b2fed), "Build e testes (Windows)" teve conclusão failure e mesmo assim saiu a release `teste-0.1.157`. Isso também vale para commits de PR ainda não mesclados. O README chama esse caminho de "o caminho de produção".
- **Estado:** CONFIRMADO pela API do GitHub.
- **Correção mínima:** `needs: [linux, windows]` e publicar só em push para main.

**E10-5 | MÉDIO | ci.yml:166, 339-411, 347, 90-108**
- **O que o CI não prova:**
  - O teste instala e desinstala, mas nunca inicia o serviço instalado pelo SCM.
  - O autoteste roda o `Edge.Supervisor.exe` como console, com o usuário do runner (ci.yml:166). Ficam sem prova: `UseWindowsService` (Program.cs:223), a conta LocalSystem, a ACL da pasta de dados aplicada como SYSTEM (Program.cs:101-104) e o erro 1053.
  - O DISM é pulado (`PULAR_NETFX35=1`).
  - Os scripts do operador só passam pela análise de sintaxe.
- **Estado:** CONFIRMADO.
- **Correção mínima:** gravar um workers.json em modo simulação em `%ProgramData%`, `Start-Service`, esperar `Running`, conferir `registros\servico-*.log`, `Stop-Service`. E testar a atualização com dois MSIs de versões diferentes.

**E10-6 | MÉDIO | src/Edge.Supervisor/Program.cs:54-56, 66-67, 74-81, 106, 173; ConexaoTopdata.wxs:145-149**
- **O que faz:** erro de configuração ou falha de migração antes da linha 173 vai para `Console.Error` com `return 1`, ou vira exceção não tratada. Rodando como serviço não existe console, e o arquivo `registros\servico-AAAA-MM-DD.log` ainda nem foi aberto. A recuperação do serviço o reinicia a cada 5 s, para sempre.
- **Por que é problema:** o RB-01:30-31 manda o técnico ao log, que não tem a causa. O gatilho do RB-09:3 ("não sobe por causa do banco") não é observável. Nenhum runbook cita o Visualizador de Eventos.
- **Estado:** CONFIRMADO no código. Sobrepõe-se ao E2.
- **Correção mínima:** abrir o `RegistroEmArquivo` antes de ler a configuração, e/ou gravar no Event Log.

**E10-7 | MÉDIO | Program.cs:106; src/Edge.Supervisor/AgendadorDeCopias.cs:24; ConexaoTopdata.wxs:31**
- **O que faz:** a migração roda antes de qualquer cópia. A primeira cópia sai 1 min depois, já do banco migrado. O downgrade é bloqueado e não existe procedimento documentado de volta.
- **Por que é problema:** voltar para N exige restaurar a última cópia de 6 em 6 h, perdendo os acessos desde então.
- **Estado:** CONFIRMADO.
- **Correção mínima:** `CopiaDeSeguranca.Criar` antes de `Migrator.Aplicar` quando houver migração pendente, e documentar o rollback (desinstalar N+1, instalar N, restaurar a cópia pré-migração).

**E10-8 | MÉDIO | ConexaoTopdata.wxs:50-58; src/Edge.Configurador/JanelaDoAssistente.xaml.cs:171-172; installer/README.md:122**
- **O que faz:** o DISM roda sem `/Source … /LimitAccess`, tanto no MSI quanto no botão do assistente. Não há procedimento offline em lugar nenhum: o grep por `sxs`/`LimitAccess` em docs não encontra nada.
- **Por que é problema:** num PC de evento sem internet, o .NET 3.5 não é habilitado, e a EasyInner.dll falha com retorno 8 (wxs:34). O `WixQuietExec` não tem prazo e pode segurar a instalação por vários minutos.
- **Estado:** NÃO VERIFICÁVEL AQUI, e o CI pula esse passo.
- **Correção mínima:** documentar `dism /Online /Enable-Feature /FeatureName:NetFx3 /All /Source:<mídia>\sources\sxs /LimitAccess` e deixar o assistente aceitar uma pasta de origem.

**E10-9 | MÉDIO | installer/configurar-operador.ps1:54-57, 46-51, 64**
- **O que faz:** sempre executa `Restart-Service`, sem confirmação, o que derruba todas as catracas. A ACL do canal usa o SID do grupo (SegurancaLocal.cs:85-87, 106-110), então o reinício só é necessário quando o grupo acabou de ser criado; incluir um membro só exige logoff/logon.
- **Outros problemas:**
  - Termina com código 0 mesmo se todas as inclusões falharem (46-51).
  - A linha 64 roda `Get-LocalGroupMember` sob `ErrorAction Stop`, e a chamada pode lançar exceção com SID órfão no grupo.
- **Estado:** CONFIRMADO no código.
- **Correção mínima:** reiniciar só se o grupo foi criado nesta execução e com confirmação; código de saída diferente de 0 em falha.

**E10-10 | MÉDIO | installer/README.md:175; RB-01:19**
- **O que faz:** os documentos mandam rodar `.\script.ps1`. No Windows cliente a ExecutionPolicy padrão é Restricted, então o script não roda. Nenhum documento orienta sobre isso (grep `ExecutionPolicy` não encontra nada).
- **Estado:** NÃO VERIFICÁVEL AQUI.
- **Correção mínima:** `powershell -ExecutionPolicy Bypass -File …`, ou atalhos com esse comando, ou scripts assinados.

**E10-11 | MÉDIO | installer/README.md:4-5, 79-84; ci.yml:590; publicar-com-sdk.yml:101-106**
- **O que faz:** Setup, MSI, executáveis e .ps1 saem sem assinatura. O README promete "pacote assinado… verificação de integridade das DLLs", e nada disso existe. O workflow privado embarca o que estiver em `inner/` sem conferir hash.
- **Por que é problema:** operadores aprendem a ignorar o SmartScreen. AppLocker/WDAC bloqueiam a instalação. Não há como conferir um zip repassado por terceiros.
- **Estado:** BLOQUEADO POR TERCEIRO (certificado; docs/38:106 classifica A13 como EXTERNO).
- **Correção mínima:** certificado, `signtool` no CI e conferência com `Get-AuthenticodeSignature`. Hash fixo da EasyInner.dll no workflow privado.

**E10-12 | MÉDIO | docs/runbooks/RB-02-porta-ou-firewall.md:28-29; ConexaoTopdata.wxs:185-196; src/Edge.Configurador/ControleDaOperacao.cs:66,77**
- **O que faz:** quando a regra de firewall é ampliada à mão para uma VLAN, ela é recriada como `localSubnet` também a cada atualização: o MajorUpgrade remove o produto antigo, e o CI prova que a remoção apaga a regra (ci.yml:405-407). O RB-02 diz que ela some só "se reinstalar". O assistente confere apenas se existe uma regra com aquele nome.
- **Por que é problema:** depois de atualizar, as catracas da VLAN param em silêncio.
- **Estado:** HIPÓTESE forte (a remoção na atualização é inferência; o CI só prova a desinstalação).
- **Correção mínima:** propriedade MSI para o endereço remoto, ou uma segunda regra que não pertença ao MSI. Corrigir o RB-02.

**E10-13 | MÉDIO | docs/runbooks/RB-06-catraca-sem-comunicacao.md:25-26**
- **O que faz:** o critério é um `Test-NetConnection` até o IP da catraca na porta do grupo. Mas a catraca é o cliente, e quem escuta é o PC (wxs:175-177; FirewallDasCatracas.cs:8-9). O próprio RB-02:30-31 testa até o PC.
- **Por que é problema:** gera um falso negativo e um chamado errado para a equipe de rede.
- **Estado:** CONFIRMADO.
- **Correção mínima:** testar o IP do PC a partir de outro computador; ping até a catraca.

**E10-14 | MÉDIO | docs/runbooks/RB-09-restauracao-do-banco.md:3-4**
- **O que faz:** "o Diagnóstico acusa falha de integridade" não existe. `SqliteConnectionFactory.VerificarIntegridade` (SqliteConnectionFactory.cs:62) só é chamado por testes.
- **O que falta:** o RB-09 não cita o `telemetria.db` (Program.cs:243). A restauração nunca foi ensaiada: CopiaDeSegurancaTests prova a criação, a integridade e a retenção da cópia, não o procedimento de restaurar.
- **O que está certo:** nome do arquivo (CopiaDeSeguranca.cs:47), intervalo de 6 h e retenção de 14 (AgendadorDeCopias.cs:22-25).
- **Estado:** CONFIRMADO.

**E10-15 | MÉDIO | installer/privado/publicar-com-sdk.yml:59-76, 38-39; ci.yml:6**
- **O que faz:** o workflow aceita qualquer SHA "verde" sem conferir se ele está em main. Commits de PR de fork também rodam o CI. Esse código é compilado no repositório privado, com o SDK e um token `contents: write`.
- **Por que é problema:** colar o SHA errado pode vazar o SDK.
- **O que mais falta:** o workflow não testa instalar e desinstalar o MSI com SDK e não exige os .ps1 na lista de arquivos obrigatórios (156-158).
- **Estado:** HIPÓTESE (depende de erro humano); revisar com o E8.
- **Correção mínima:** conferir `gh api compare/main...$sha` antes de compilar.

**E10-16 | MÉDIO | ConexaoTopdata.wxs:132-139**
- **O que faz:** o `ServiceInstall` não define `Account`, então o serviço roda como LocalSystem. O worker, filho do serviço, presumivelmente herda SYSTEM e escuta TCP na sub-rede com uma DLL de terceiros. Não li o ProcessoDeWorker.
- **Estado:** HIPÓTESE; revisar com o E8.
- **Correção mínima:** conta virtual `NT SERVICE\ConexaoTopdataEdge`.

**E10-17 | MÉDIO | installer/README.md:114-115; ConexaoTopdata.wxs (não há remoção de `%ProgramData%`)**
- **O que faz:** a desinstalação deixa `acesso.db`, 14 cópias, registros, segredo e token, sem nenhuma opção de expurgo.
- **Por que é problema:** retenção de dado pessoal (LGPD).
- **Estado:** CONFIRMADO; revisar com o E8.

### BAIXO

- **E10-18:** a página Opções do Setup oferece uma pasta de instalação que é ignorada: não há `MsiProperty INSTALLFOLDER` e o `LaunchTarget` é fixo (tema-rayzer.xml:70-73, 80-96; Setup.wxs:50-55).
- **E10-19:** `INICIAR_NA_BANDEJA=0` não persiste; a atualização liga a bandeja de novo (wxs:204-206).
- **E10-20:** o componente de atalhos é per-machine, mas o KeyPath está em HKCU (wxs:236-266; ICE57).
- **E10-21:** `<Files Include>` não é recursivo, então subpastas do publish ficam fora do MSI (satélites de cultura e `Licencas\Fontes\*-OFL.txt`, licença exigida pela OFL). O Configurador vai sem .pdb (wxs:125-127, 161-171, 213-217). HIPÓTESE quanto ao conteúdo das subpastas.
- **E10-22:** a EasyInner.dll copiada pelo assistente (AssistenteDeConfiguracao.cs:299-300) fica em Program Files depois da desinstalação.
- **E10-23:** o MSI não cria o grupo "ConexaoTopdata Operadores" e não o remove. Sem rodar o script, nenhum não-administrador conecta (documentado em README:166-180).
- **E10-24:** o README de desenvolvimento está desatualizado. O token em LOCALAPPDATA e o "recusa subir sem ele" (README:32-36; instalar-dev.ps1:16-34) não batem com Program.cs:87 (`GarantirToken` em ProgramData). README:3-5 diz "não é instalador de produção", e README:71 chama o Setup de "caminho de produção".
- **E10-25:** publicar.ps1 (26-41) não publica o Edge.Configurador, usa `--self-contained false` e não passa `-p:Version`; diverge do CI.
- **E10-26:** docs/runbooks/README.md:20 diz que o RB-12 está pendente por falta do botão, mas o botão existe (Diagnostico.xaml:14, commit 6a9062e). O RB-01, passo 5, não manda exportar o pacote.
- **E10-27:** RB-01:17 diz "Se for 'quarentena'", mas o painel mostra "Programa da catraca parou várias vezes" (Textos.cs:39). Os passos 3 e 4 do RB-01 reiniciam o serviço inteiro sem o aviso que o RB-06:27-28 dá.
- **E10-28:** RB-02:23 pede "linha com o processo Edge.Worker.X86", mas `Get-NetTCPConnection` mostra só o PID.
- **E10-29:** workers.exemplo.json:26,32 aponta para `..\Edge.Worker.X86\`, que só vale no layout `artifacts`; na instalação a pasta é `Worker` (wxs:92).
- **E10-30:** o comentário de verificar-ambiente.ps1:5-7 promete mais do que o teste prova (InstaladorTests.cs:47-57 compara só IDs).
- **E10-31:** um re-run reusa o `GITHUB_RUN_NUMBER`, então `gh release create` falha (ci.yml:569-592). MSIs de mesma versão sem `AllowSameVersionUpgrades` se instalam lado a lado (wxs:31).

### INFORMATIVO

- **E10-32:** worker x86 PROVADO EM CI: `PlatformTarget x86` (Edge.Worker.X86.csproj:17), `win-x86` (ci.yml:146) e cabeçalho PE 0x014C (ci.yml:266-281). Nenhum .csproj fixa RuntimeIdentifier ou SelfContained; isso vem da linha de comando.
- **E10-33:** a proibição da EasyInner.dll no MSI público e no pacote da bancada é PROVADA EM CI (ci.yml:330-332, 613). A conferência é só pelo nome; as DLLs companheiras do SDK não são conferidas.
- **E10-34:** PROVADOS EM CI (ci.yml:360-377): regra de firewall por programa, TCP, LocalSubnet, Allow; recuperação RESTARTx3; partida Automatic; fechamento do painel na desinstalação (ci.yml:398-410). Hipótese: a recuperação só age em queda, não em parada "limpa" com erro (falta FailureActionsOnNonCrashFailures).
- **E10-35:** o Setup abre e mostra o botão Instalar: PROVADO EM CI (ci.yml:436-537).
- **E10-36:** global.json 10.0.100 com `latestFeature`: o CI pode usar uma feature band mais nova que a da máquina de desenvolvimento.

## B) Cobertura

| Arquivo | Linhas | Lido inteiro | Achados |
|---|---|---|---|
| installer/README.md | 277 | sim | 4 (E10-1, 4, 11, 24) |
| installer/publicar.ps1 | 70 | sim | 1 (E10-25) |
| installer/verificar-ambiente.ps1 | 94 | sim | 2 (E10-3, 30) |
| installer/instalar-dev.ps1 | 50 | sim | 1 (E10-24) |
| installer/configurar-operador.ps1 | 66 | sim | 2 (E10-9, 10) |
| installer/workers.exemplo.json | 35 | sim | 1 (E10-29) |
| installer/bancada.exemplo.json | 29 | sim | 0 |
| installer/simulacao.exemplo.json | 25 | sim | 0 |
| installer/modelos/cartoes-modelo.csv | 5 | sim | 0 (dados fictícios) |
| installer/modelos/tipos-modelo.csv | 5 | sim | 0 |
| installer/modelos/modelo-cadastro-de-cartoes.xlsx | binário | não (binário) | — |
| installer/wix/ConexaoTopdata.wxs | 270 | sim | 12 |
| installer/wix/Setup.wxs | 57 | sim | 1 (E10-18) |
| installer/wix/tema-rayzer.xml | 158 | sim | 1 (E10-18) |
| installer/wix/tema-pt-BR.wxl | 74 | sim | 1 (E10-1) |
| installer/wix/fundo-setup.png | binário | não (imagem 822x463) | — |
| installer/privado/publicar-com-sdk.yml | 185 | sim | 3 (E10-2, 11, 15) |
| .github/workflows/ci.yml | 726 | sim | 6 (E10-4, 5, 31, 32-35) |
| Directory.Build.props | 43 | sim | 0 |
| global.json | 6 | sim | 1 (E10-36) |
| src/**/*.csproj (21 arquivos) | 503 | sim, todos | 1 (E10-32) |
| docs/runbooks/README.md | 24 | sim | 1 (E10-26) |
| docs/runbooks/TEMPLATE.md | 26 | sim | 0 |
| docs/runbooks/RB-01-worker-nao-inicia.md | 41 | sim | 4 (E10-3, 6, 10, 27) |
| docs/runbooks/RB-02-porta-ou-firewall.md | 43 | sim | 2 (E10-12, 28) |
| docs/runbooks/RB-06-catraca-sem-comunicacao.md | 38 | sim | 1 (E10-13) |
| docs/runbooks/RB-09-restauracao-do-banco.md | 44 | sim | 2 (E10-6, 14) |

Arquivos lidos para conferir as afirmações:
- **Inteiros:** src/Edge.Supervisor/Program.cs (295), tests/Integration/InstaladorTests.cs (193), src/Edge.Supervisor/Instalacao/FirewallDasCatracas.cs (40).
- **Em parte, por grep:** AgendadorDeCopias.cs, SegurancaLocal.cs, CopiaDeSeguranca.cs, RegistroEmArquivo.cs, AssistenteDeConfiguracao.cs, JanelaDoAssistente.xaml.cs, Textos.cs, WorkerSupervisor.cs, Migrator.cs.

## C) Veredito das capacidades (docs/40 §7)

**18. Instalação, atualização, desinstalação, cópia de segurança e restauração: PARCIAL.**
- **Provado em CI:** instalar e desinstalar o MSI, arquivos no lugar, regra de firewall, recuperação do serviço, partida automática, bandeja, Setup que abre, ausência da EasyInner.dll (ci.yml:339-411, 436-537; run 161 verde). A criação, verificação e retenção da cópia também (tests/Integration/CopiaDeSegurancaTests.cs).
- **O que falta:**
  - O serviço instalado nunca foi iniciado pelo SCM (E10-5).
  - O DISM é pulado no CI e não há caminho offline (E10-8).
  - A atualização N→N+1 não tem teste e deixa o serviço parado (E10-1).
  - As versões colidem entre o instalador público e o privado (E10-2).
  - A migração roda sem cópia antes e sem volta (E10-7).
  - A restauração nunca foi ensaiada (E10-14).
  - A assinatura de código está ausente (E10-11, BLOQUEADO POR TERCEIRO: certificado).

**19. Diagnóstico e runbooks, parte entrega: PARCIAL.**
- **O que existe:** 4 dos 12 runbooks escritos (README.md:7-20). O pacote de diagnóstico existe e tem teste (tests/Integration/PacoteDeDiagnosticoTests.cs; commit 6a9062e).
- **O que falta:**
  - O RB-01, passo 2, não é executável como está escrito (E10-3, E10-10).
  - O RB-06, passo 4, testa na direção errada (E10-13).
  - O gatilho do RB-09 não é observável (E10-14).
  - Falhas na partida do serviço não deixam registro (E10-6).
  - O índice diz que o RB-12 está pendente, mas o botão já existe (E10-26).
- **Ensaio:** nenhum runbook foi ensaiado numa máquina instalada.

## D) Perguntas de decisão

1. **Depois de uma atualização, o serviço deve subir sozinho?**
   - (a) Sim, se já houver workers.json, com falha que não desfaz a instalação. **[RECOMENDADA]** Fecha o E10-1 sem perder o motivo de não iniciar na instalação limpa.
   - (b) Não; documentar que é preciso iniciar à mão.
   - (c) Iniciar sempre.

2. **Como separar o instalador de produção (com SDK) do de teste?**
   - (a) Versão de produção 1.0.N e teste 0.1.N, mesmo UpgradeCode, com teste de atualização no CI.
   - (b) UpgradeCode e nome de produto distintos para o teste, de modo que não se instalem um por cima do outro. **[RECOMENDADA]** Impede que um teste remova a EasyInner.dll de uma máquina de evento (E10-2).
   - (c) Manter como está.

3. **A pré-release pública por commit continua existindo?**
   - (a) Só em push para main, com `needs` dos jobs de teste. **[RECOMENDADA]** O run 157 publicou com teste falhando (E10-4).
   - (b) Manter por commit de PR, mas com `needs`.
   - (c) Manter como está.
