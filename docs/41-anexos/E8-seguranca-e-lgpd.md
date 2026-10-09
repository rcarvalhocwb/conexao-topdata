> Relatório bruto de um especialista da auditoria linha a linha (docs/40), commit auditado 29df501.
> Os estados aqui são os do especialista, antes da revisão cruzada; o consolidado é o docs/41.

RELATÓRIO E8 — SEGURANÇA E LGPD (commit 29df501, somente leitura; nada editado, sem build/test, sem rede externa)

Comandos usados: Read, Grep, `git log`, `git ls-files`, `git grep` (procurei segredos commitados e padrão de CPF: **nenhum segredo real encontrado**; só o marcador `SEU-PROJETO.supabase.co` em installer/workers.exemplo.json:10 e um CPF fictício em docs/34-anexos/03:825).

---

## A) Achados

### E8-1 | CRÍTICO | NÃO VERIFICÁVEL AQUI (lado Supabase)
- **Onde:**
  - src/Edge.Supervisor/CofreDeSegredos.cs:133-137 e 160-177
  - src/Edge.Supervisor/Program.cs:208-211
  - docs/22-sistema-supabase.md:249-259, 408-417, 444-455
- **O que o código faz:** `CabecalhoDeSegredo` manda a requisição sem credencial nenhuma quando o cofre está vazio. O serviço só grava uma linha no registro ("as requisições saem sem credencial") e continua a sincronização: baixa cartões e envia tentativas.
- **Por que é problema:** o docs/22 diz que as funções `middleware-sync-*` estão com JWT desligado e que a chave `anon` lê `authorizations` (nome do titular e possivelmente CPF) e insere em `access_events`. Se for assim, qualquer pessoa com o endereço do projeto baixa a lista de cartões com nome e grava acessos falsos, que podem consumir ingresso de quem pagou. A borda não impede nem acusa esse estado de forma visível.
- **Como provar:** com o projeto Supabase real, chamar as funções sem cabeçalho e com a chave anon. Do lado da borda, o teste que deveria existir é: serviço com `nuvem` configurada e cofre vazio → sincronização não sobe.
- **Correção mínima:** com `nuvem` configurada e sem segredo, recusar sincronizar e mostrar alerta no painel. No Supabase, exigir o segredo nas funções e tirar as permissões do `anon` (decisão D1 na seção D).

### E8-2 | ALTO | HIPÓTESE (precisa de VM Windows)
- **Onde:**
  - src/Edge.Supervisor/ConfiguracaoDoSupervisor.cs:35-41 e 112
  - src/Edge.Supervisor/Program.cs:40-43, 62, 87 e 103
  - src/Edge.Supervisor/SegurancaLocal.cs:48-55 e 138-170
  - src/Edge.Supervisor/Instalacao/AssistenteDeConfiguracao.cs:159-164
  - installer/wix/ConexaoTopdata.wxs (nenhum CreateFolder nem Permission para %ProgramData%)
- **O que o código faz:**
  - `workers.json` diz qual executável o serviço (SYSTEM) inicia. `ResolverExecutavel` aceita qualquer caminho absoluto, e `Validar` só confere se o arquivo existe.
  - O serviço lê `workers.json` (linha 62) e o token (linha 87) **antes** de restringir a pasta (linha 103).
  - `GarantirToken` reaproveita um `token` que já exista.
  - `RestringirPastaDeDados` troca só a DACL; o dono do arquivo não muda.
  - Quem cria `%ProgramData%\ConexaoTopdata` é o assistente ou o serviço, sem ACL explícita, ou seja, com a herança padrão do ProgramData. Nessa herança, Users podem criar arquivos em subpastas e CREATOR OWNER fica com controle total.
- **Por que é problema:** na janela entre a instalação e a primeira partida do serviço (ou antes da instalação), um usuário local comum pode:
  - criar a pasta ou os arquivos `workers.json`, `workers.json.novo` ou `token` e ficar como dono deles;
  - fazer o serviço executar um binário dele como SYSTEM: elevação local, com acesso ao `acesso.db` (cartões em claro) e ao cofre;
  - ou fixar o token que quiser.
  
  Como continua dono, ele consegue reabrir a ACL depois que o serviço restringe.
- **Como provar:** VM com usuário comum → criar `C:\ProgramData\ConexaoTopdata\workers.json` antes do primeiro start → `Executavel` apontando para um binário do usuário → iniciar o serviço. Não há nenhum teste de ACL no repositório: tests/Integration/InstalacaoRealTests.cs:28-60 só testa o `CurrentUserOnly` e o reaproveitamento do token.
- **Correção mínima:**
  - o MSI cria a pasta com ACL protegida (SYSTEM e Administradores) e dono Administradores;
  - o serviço confere dono e ACL de `workers.json` e da pasta antes de ler;
  - `Executavel` fica restrito à pasta do programa.

### E8-3 | MÉDIO | CONFIRMADO por leitura (efeito real só numa VM)
- **Onde:**
  - src/Edge.Supervisor/Program.cs:103
  - src/Edge.Supervisor/ConfiguracaoDoSupervisor.cs:44 e 50-55
  - src/Edge.Supervisor/SegurancaLocal.cs:127-171
  - src/Contracts/InstalacaoLocal.cs:14-25
- **O que o código faz:** a pasta restringida é a pasta **do banco** (`configuracao.PastaDeDados`), não `InstalacaoLocal.PastaDeDados`, que é onde estão token, `workers.json` e segredos.
- **Por que é problema:** com `"banco"` personalizado no `workers.json`:
  - token, `workers.json` e a pasta `segredos` ficam com a ACL herdada do ProgramData;
  - a restrição é aplicada recursivamente, como SYSTEM, numa pasta qualquer. Exemplo: com banco `D:\acesso.db`, ela troca a ACL de todo o disco D:.
  
  O assistente grava `Banco` nulo (AssistenteDeConfiguracao.cs:132), então o caso nasce de edição manual ou do exemplo.
- **Como provar:** VM com banco em `D:\x\acesso.db` → conferir `icacls` antes e depois da partida.
- **Correção mínima:** restringir sempre `InstalacaoLocal.PastaDeDados` e recusar banco fora de uma pasta dedicada.

### E8-4 | ALTO | HIPÓTESE (depende da hospedagem do relé)
- **Onde:**
  - src/Relay.Ingressos/Program.cs:18 e 45-55
  - src/Relay.Ingressos/Segredos.cs:9-14 e 33-36
  - não existe appsettings*.json em src/Relay.Ingressos/
- **O que o código faz:** o token de entrada vai no caminho (`/webhook/{token}`). `WebApplication.CreateBuilder` sem configuração registra em nível Information, e a linha de início de requisição do ASP.NET Core inclui o caminho.
- **Por que é problema:**
  - o segredo vai para o stdout e os logs do provedor de hospedagem;
  - não há assinatura (ADR-0022), então quem tem o token injeta "ingressos" válidos que a borda ingere: pode liberar quem não deveria;
  - não há limite de taxa.
- **Como provar:** subir o relé localmente e fazer um POST: o token aparece no console.
- **Correção mínima:**
  - filtrar `Microsoft.AspNetCore.Hosting.Diagnostics` (ou `Microsoft.AspNetCore`) para Warning, ou redigir o caminho;
  - trocar o token;
  - pedir HMAC à Zet.

### E8-5 | MÉDIO | CONFIRMADO por leitura
- **Onde:**
  - src/Relay.Ingressos/ArmazenamentoDeEntregas.cs:58-77 e 82-105
  - src/Relay.Ingressos/Cabecalhos.cs:13-23
  - src/Relay.Ingressos/Program.cs:97-100
- **O que o código faz:**
  - guarda o corpo bruto, sem cifrar e sem prazo;
  - um gatilho proíbe DELETE;
  - os cabeçalhos guardados incluem `X-Forwarded-For` e `User-Agent` (o IP do comprador é dado pessoal);
  - `/entregas?desde=0` devolve tudo a quem tiver o token de leitura.
- **Por que é problema:** o payload da Zet tem nome, e-mail, telefone e CPF do comprador. TradutorDaZet.cs:18 afirma que a borda não lê esses campos, o que confirma que eles vêm no payload. No relé esses dados ficam para sempre e o expurgo LGPD é impossível sem mudar o esquema.
- **Correção mínima:**
  - prazo de retenção, com expurgo controlado (gatilho que permita apagar só depois de N dias, ou banco por evento);
  - cifragem em repouso;
  - não guardar `X-Forwarded-For`.

### E8-6 | MÉDIO | CONFIRMADO por leitura — bloqueado pela decisão D7 (jurídico)
- **Onde:**
  - Migrations/003_ingressos_e_provedores.sql:18-19 e 58
  - Migrations/001_esquema_inicial.sql:31
  - src/Access.Infrastructure.SQLite/TrilhaDeCredenciais.cs:21-22 e 310-311
  - src/Edge.Supervisor/ChaveDaImpressao.cs:24-27
  - Migrations/011:260
- **O que o código faz:** o código do ingresso e o número do cartão ficam em claro, por decisão do ADR-0014, em `ticket.qr_raw`/`qr_normalized`, em `ticket_use_attempt.qr_normalized` e em `access_decision.credential_value`. Isso se repete nas cópias de segurança.
  - Não existe rotina de retenção ou expurgo desses dados.
  - `Expurgado` e `TitularRevelado` existem só como enum; nenhum código os produz.
  - Não existe relatório ao titular.
  - Base legal aparece só nos documentos (docs/34-anexos/03 e outros), não no código.
- **Por que é problema:** sem prazo nem expurgo, o dado do evento vive indefinidamente no PC do evento e nas cópias de segurança.
- **Correção mínima:** decidir a D7 e implementar a Etapa B.9: expurgo por destruição de chave, mais a purga de `qr_normalized`/`credential_value` e das cópias depois do prazo.

### E8-7 | MÉDIO | CONFIRMADO o desenho; exploração é HIPÓTESE
- **Onde:**
  - src/Edge.Supervisor/ProcessoDeWorker.cs:101-124
  - installer/wix/ConexaoTopdata.wxs:187-193 (`Profile="all"`)
  - src/Edge.Supervisor/Instalacao/FirewallDasCatracas.cs (`profile=any`)
- **O que o código faz:** o worker x86 herda a conta SYSTEM do serviço, escuta TCP para a sub-rede local em qualquer perfil de rede (inclusive Público) e carrega uma DLL nativa de terceiro que interpreta bytes vindos da rede.
- **Por que é problema:** o protocolo da catraca não autentica, então qualquer máquina da sub-rede se passa por catraca. Uma falha na DLL vira execução como SYSTEM.
- **Correção mínima:** rodar o worker numa conta de serviço sem privilégio e limitar o firewall aos perfis domínio e privado.

### E8-8 | MÉDIO | CONFIRMADO
- **Onde:**
  - .github/workflows/ci.yml:705-726
  - exemplo do que passa: src/Edge.Worker/Bancada/SessaoDeBancada.cs:140-141
- **O que o código faz:** a varredura "SEC-LOG-01" do CI é um único grep: `(Log|log)[A-Za-z]*\(.*\.(Raw|Normalized)`.
- **O que ela pega:** só chamadas cujo nome começa com Log/log e que citam `.Raw…` ou `.Normalized` na mesma linha.
- **O que deixa passar:**
  - `Registrar(`, `_registrar(`, `_escrever(`, `Console.WriteLine`, `Escrever(` e exceções. Exemplo: SessaoDeBancada.cs:141 grava `RawCardData` inteiro via `_escrever` e passa;
  - chamada em várias linhas;
  - segredos (não há gitleaks nem equivalente);
  - installer/ e docs/.
- **Atenuante:** o teste de execução tests/Integration/SecLog01Tests.cs:62 cobre o laço de operação.
- **Correção mínima:** varredura de segredos, mais grep de `RawCardData`/`Codigo` em qualquer escrita de texto, mais `ParecemHaverDadosSensiveis` aplicado aos arquivos de registro gerados nos testes.

### E8-9 | MÉDIO | CONFIRMADO
- **Onde:** .github/workflows/ci.yml:117-119, 557-597 e 590
- **O que o código faz:**
  - o Setup.exe e o MSI saem sem assinatura de código;
  - as notas da release ensinam a contornar o SmartScreen;
  - a pré-release pública é publicada a partir de qualquer PR do mesmo repositório, com `contents: write`;
  - não é publicado hash para conferência.
- **Por que é problema:** o operador é treinado a executar binário não assinado como administrador, sem forma de conferir a integridade.
- **Correção mínima:** certificado de assinatura (Authenticode), publicar SHA-256 e publicar só a partir de `main`.

### Achados BAIXO e INFORMATIVO

- **E8-10 | BAIXO:** o redator só pega 6 ou mais dígitos isolados (src/Shared.Observability/RedatorDeDadoSensivel.cs:110). Deixa passar cartão de 4 ou 5 dígitos, QR alfanumérico, UID em hex e CPF com pontuação. Atenuado pela máscara na origem (src/Edge.Worker/Operacao/SessaoDeOperacao.cs:552).
- **E8-11 | BAIXO:** no zip de diagnóstico, só os registros passam pelo redator (MontadorDoPacoteDeDiagnostico.cs:57). O `diagnostico.txt` (linhas 39-44), que leva o stderr do worker (ProcessoDeWorker.cs:201-202; EdgeControlService.cs:574-587), não passa.
- **E8-12 | BAIXO:** `RegistroDeFalhas` grava `exception.ToString()` sem prazo de retenção (src/Contracts/RegistroDeFalhas.cs:28-29). O comentário das linhas 8-10 ("o contrato não tem esse campo") é falso: existem `ConsultarCodigoRequest.codigo` (edge_control.proto:365-367) e `SimularLeituraRequest.codigo` (proto:429).
- **E8-13 | BAIXO:** SegurancaLocal.cs:71-72 diz que o grupo é criado pelo instalador; o MSI só copia `configurar-operador.ps1`, que é manual (ConexaoTopdata.wxs:227-234). Além disso, o `NTAccount` sem prefixo de máquina (SegurancaLocal.cs:110) pode resolver um grupo de domínio de mesmo nome (hipótese).
- **E8-14 | BAIXO:** o token é escrito (SegurancaLocal.cs:54) antes de ter a ACL aplicada (linha 59). Há uma janela com a ACL herdada; criar o arquivo já com a ACL fecha a janela.
- **E8-15 | BAIXO (hipótese):** o cliente do pipe não verifica a identidade do servidor (TransporteLocal.cs:156-167). Um processo que crie o pipe antes do serviço recebe o token do painel. O impacto é limitado pela ACL do pipe real.
- **E8-16 | INFORMATIVO:** o DPAPI está no escopo da máquina, com entropia constante e pública (CofreDeSegredos.cs:57, 68 e 88). A única barreira real é a ACL do arquivo.
- **E8-17 | BAIXO:** os modos de teste mostram o código inteiro: bancada (SessaoDeBancada.cs:140-141, com aviso em Edge.Worker.X86/Program.cs:168) e ArquivoDeBancada.cs:96 e 107.
- **E8-18 | INFORMATIVO:** sem injeção de SQL. Todo SQL interpolado só usa constantes ou marcadores de parâmetro (ConsultasDaOperacao.cs:132 e 266; FilaDeComandosSqlite.cs:76; RepositorioDeIngressos.cs:1093-1110; LeiturasSimuladas.cs:65; ConfiguracoesDasCatracas.cs:80 e 98). O `VACUUM INTO` escapa aspas e o caminho é interno (CopiaDeSeguranca.cs:59).
- **E8-19 | INFORMATIVO:**
  - Deserialização: só System.Text.Json em tipos fechados; nenhum BinaryFormatter, TypeNameHandling ou Newtonsoft.
  - Caminhos de arquivo: nenhum vem do IPC (o proto não tem campo de caminho). O relé não usa caminho vindo da entrada. `CopiarEasyInner` valida nome e cabeçalho PE (AssistenteDeConfiguracao.cs:287-296) e roda em processo administrador.
- **E8-20 | INFORMATIVO:** `InterceptadorDeToken` compara em tempo constante (InterceptadorDeToken.cs:73) e cobre unary e server streaming. Não tem override para client ou duplex streaming; hoje não existe RPC desse tipo (proto:26 é o único stream), mas um RPC assim no futuro entraria sem token.
- **E8-21 | INFORMATIVO:** o segredo da nuvem entra pela entrada padrão (Program.cs:18-35), e a chave HMAC vai ao worker pela entrada padrão, não pela linha de comando (ProcessoDeWorker.cs:107-110; Program.cs:151-152). Correto.
- **E8-22 | INFORMATIVO:** src/Topdata.EasyInner.Interop/EasyInnerGerada.cs (655 linhas) foi gerado do SDK Inner Acesso 6.0.2.0 (linhas 2-3) e está no repositório público. As DLLs do SDK não estão versionadas (vendor/topdata só tem scripts, README e .gitignore); o MSI com SDK é montado no repositório privado (installer/privado/publicar-com-sdk.yml).

**Ordem de inicialização e ACL do token (a pergunta do coordenador):** `GarantirToken` (Program.cs:87) aplica ao token uma DACL protegida: SYSTEM e Administradores com controle total, grupo dos operadores com leitura (SegurancaLocal.cs:179-193). Depois, `RestringirPastaDeDados` (linha 103) pula arquivos chamados `token` (linhas 159-162), e a DACL protegida não é sobrescrita pela herança. Portanto o token **continua legível para o grupo**, desde que o Windows mantenha o padrão "Bypass traverse checking" para Users, porque a pasta passa a negar listagem ao grupo. Isso é NÃO VERIFICÁVEL AQUI.

Subpastas recriadas depois (registros, copias, segredos, telemetria) herdam SYSTEM e Administradores pela regra OI|CI da raiz (linhas 137-143): correto. A ressalva é o E8-3, quando a pasta do banco não é a pasta da instalação.

---

## B) Arquivos lidos

**Lidos inteiros:**
- docs/40-prompt-auditoria-linha-a-linha.md
- src/Edge.Supervisor: SegurancaLocal.cs, Program.cs, CofreDeSegredos.cs, ConfiguracaoDoSupervisor.cs, MontadorDoPacoteDeDiagnostico.cs, Instalacao/AssistenteDeConfiguracao.cs
- src/Contracts: InterceptadorDeToken.cs, RegistroDeFalhas.cs, InstalacaoLocal.cs, TransporteLocal.cs
- src/Edge.Configurador: JanelaDoAssistente.xaml.cs, App.xaml.cs, ControleDaOperacao.cs, app.manifest
- src/Relay.Ingressos: Program.cs, ArmazenamentoDeEntregas.cs, Segredos.cs, Cabecalhos.cs
- src/Shared.Observability: RedatorDeDadoSensivel.cs, LogEstruturado.cs
- src/Edge.Worker/Operacao/RegistroEmArquivo.cs
- src/Desktop.App/Telas: Contas.xaml.cs, Diagnostico.xaml.cs
- installer: configurar-operador.ps1, verificar-ambiente.ps1, wix/ConexaoTopdata.wxs, wix/Setup.wxs

**Consultados por busca ou trecho:**
- src/Edge.Supervisor: ChaveDaImpressao.cs (1-90), ProcessoDeWorker.cs (95-130), Instalacao/FirewallDasCatracas.cs, SincronizacaoComANuvem.cs, EdgeControlService.cs (565-620)
- src/Edge.Worker.X86/Program.cs
- src/Edge.Worker: Operacao/SessaoDeOperacao.cs:552, Bancada/SessaoDeBancada.cs:140
- src/Access.Domain/Credentials/CredentialValue.cs (55-90)
- src/Access.Infrastructure.SQLite: ConsultasDaOperacao.cs, RepositorioDeIngressos.cs, FilaDeComandosSqlite.cs, LeiturasSimuladas.cs, ConfiguracoesDasCatracas.cs, CopiaDeSeguranca.cs, ArquivoDeBancada.cs, TrilhaDeCredenciais.cs
- migrações 001, 002, 003, 004 e 011
- src/Sync.Connectors.Rest: Painel/ConectorDeTentativasDoPainel.cs (140-185), Painel/FonteDeCartoesDoPainel.cs, TradutorDaZet.cs:18
- src/Desktop.ViewModels/Telas.cs (746-847)
- src/Desktop.App/App.xaml.cs
- src/Contracts/Protos/edge_control.proto (355-440)
- src/Topdata.EasyInner.Interop/EasyInnerGerada.cs (cabeçalho)
- .github/workflows/ci.yml (1-30, 110-125, 540-600, 705-726)
- installer/privado/publicar-com-sdk.yml
- docs/22 §8.1 e §9.2
- tests: Integration/InstalacaoRealTests.cs e os nomes dos testes em IpcLocalTests.cs, ReleDeWebhookTests.cs, PacoteDeDiagnosticoTests.cs e SecLog01Tests.cs

---

## C) Placar (docs/40 §7)

**16. Segurança local e da nuvem: PARCIAL.**
- Canal e token: **PROVADO EM CI** só a recusa sem token e com token errado (tests/Integration/IpcLocalTests.cs:119 e 132) e o token com 32 bytes reaproveitado (InstalacaoRealTests.cs:51-60).
- ACL do pipe, do arquivo do token e da pasta de dados: **IMPLEMENTADO SEM PROVA** (nenhum teste confere ACL; precisa de VM).
- Restrição da pasta: falha nos casos dos achados E8-2 e E8-3.
- Nuvem: o segredo é opcional na borda, e o lado Supabase é **NÃO VERIFICÁVEL AQUI** e está **BLOQUEADO POR DECISÃO OU TERCEIRO** (quem mantém o Supabase precisa exigir o segredo e tirar as permissões do `anon`).
- Relé: token comparado em tempo constante e **PROVADO EM CI** (ReleDeWebhookTests.cs:140 e 152), mas o token pode ir para log (E8-4) e não há assinatura.
- Instalador sem assinatura de código (E8-9).

**17. Proteção de dado pessoal (LGPD): PARCIAL.**
- Existe:
  - minimização (TradutorDaZet.cs:18);
  - máscara (CredentialValue.cs:73-88);
  - redator e SEC-LOG-01 (**PROVADO EM CI**: SecLog01Tests.cs:62, PacoteDeDiagnosticoTests.cs:82);
  - bilhetes coletados sem código em claro (migração 015);
  - trilha com HMAC (ChaveDaImpressao.cs).
- Ausente:
  - retenção e expurgo de `ticket`, `ticket_use_attempt`, `access_decision` e das cópias (o enum `Expurgado` não tem produtor);
  - relatório ou atendimento ao titular;
  - base legal no produto;
  - retenção no relé (o DELETE é proibido).
- Expurgo **BLOQUEADO POR DECISÃO** D7 (jurídico, prazos). O código em claro no `acesso.db` é escolha do ADR-0014.

---

## D) Perguntas de decisão

**D1. Nuvem configurada sem segredo no cofre:**
- (a) o serviço recusa sincronizar e o painel mostra alerta — **recomendada**
- (b) manter como está (só uma linha no registro)
- (c) desligar a nuvem até o Supabase exigir segredo

Motivo: hoje a borda opera em silêncio contra funções que, segundo o docs/22, estão abertas e expõem nome e CPF. A opção (a) força a correção antes do evento sem parar as catracas.

**D2. Proteção de %ProgramData%\ConexaoTopdata e do `workers.json`:**
- (a) o MSI cria a pasta com ACL protegida e dono Administradores; o serviço recusa `workers.json` ou `token` com dono ou ACL inesperados, e `Executavel` fica restrito à pasta do programa — **recomendada**
- (b) só documentar no runbook
- (c) assinar ou cifrar o `workers.json`

Motivo: fecha a elevação a SYSTEM (E8-2) e o E8-3 com mudança pequena, testável numa VM.

**D3. Retenção LGPD (D7):**
- (a) definir agora o prazo (ex.: N dias após o evento) e implementar o expurgo por destruição de chave, a purga dos códigos em claro e das cópias, e a purga do relé — **recomendada**
- (b) adiar para depois do primeiro evento
- (c) deixar de guardar código em claro (SQLCipher ou só hash), revertendo o ADR-0014

Motivo: a opção (a) é a única que dá prazo e expurgo demonstráveis sem mexer no caminho da decisão de acesso. A (c) muda a operação e a prestação de contas às vésperas do evento.
