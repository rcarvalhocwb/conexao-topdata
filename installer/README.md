# Instalação de desenvolvimento

Sobe o ambiente completo numa máquina Windows de bancada. **Não é o instalador de
produção** — esse vem na Fase 5, com pacote assinado, rollback e verificação de
integridade das DLLs.

## O que é instalado

| Componente | Arquitetura | Papel |
|---|---|---|
| `Edge.Worker.X86` | **x86** | Único que carrega a `EasyInner.dll` |
| `Edge.Supervisor` | x64 | Supervisiona os workers e atende o IPC |
| `Desktop.App` | x64 | Painel do operador |

## Passos

```powershell
# 1. Publica os três componentes em artifacts\
.\installer\publicar.ps1

# 2. Confere os pré-requisitos da máquina
.\installer\verificar-ambiente.ps1

# 3. Gera o token de sessão
.\installer\instalar-dev.ps1

# 4. Descreve os grupos
copy installer\workers.exemplo.json artifacts\Edge.Supervisor\workers.json
# edite: nome, porta e equipamentos de cada grupo
```

O passo 3 gera um **token de sessão** e o grava em
`%LOCALAPPDATA%\ConexaoTopdata\token`, com permissão só para o usuário atual. O serviço
recusa a conexão sem ele, e recusa **subir** sem ele — a ACL do named pipe protege por
identidade de usuário, não por processo, então qualquer coisa rodando na mesma conta
alcançaria o serviço.

O serviço também recusa subir com configuração inválida: porta repetida entre grupos,
equipamento em dois grupos ou executável inexistente. Reclamar na partida é muito melhor
que descobrir com a fila formada.

## O que roda hoje, e o que não roda

Sendo direto, porque a diferença decide se vale agendar bancada:

| Componente | Sobe? | O que acontece |
|---|---|---|
| `Edge.Supervisor` | **Sim** | Lê `workers.json`, sobe os workers com a base local, supervisiona com reinício e quarentena, sincroniza com a nuvem e atende o painel pelo IPC (ADR-0024) |
| `Desktop.App` | **Sim** | Painel do evento, lendo o serviço |
| `Edge.Worker.X86` | **Tenta** | Confere os pré-requisitos e chama a DLL de verdade. É aqui que o ensaio **HIL-STACK-01** responde: código 0 se a porta abriu, 2 se a DLL não carregou ou devolveu GPF |

O caminho painel → serviço → supervisão funciona ponta a ponta, e desde 24/09 o worker tem
adapter real: as assinaturas vieram do SDK 6.0.2.0. O que falta é a primeira conversa com
hardware.

Rodar o worker sozinho **é** o ensaio HIL-STACK-01:

```powershell
$env:EDGE_TOKEN = Get-Content "$env:LOCALAPPDATA\ConexaoTopdata\token"
.\artifacts\Edge.Worker.X86\Edge.Worker.X86.exe --porta 3570
```

| Saída | O que significa |
|---|---|
| código 0, "Porta aberta" | A DLL carregou num processo .NET 10 de 32 bits. O plano B some |
| código 2, retorno 8 (GPF) | Ambiente: rode `verificar-ambiente.ps1` |
| código 2, DLL não encontrada | Registre as DLLs, ou mova só este hospedeiro para .NET Framework 4.8 (docs/12) |

**Nenhuma catraca foi acionada por este sistema até hoje.**

## Setup.exe — o caminho de produção

Um arquivo só: **`RayzerXAcess-Setup.exe`**, com o .NET embutido (uns 180 MB). Cada
commit publica uma pré-release própria, **"Instalador de teste 0.1.N"** (ficam as 5
últimas), na página de versões:

https://github.com/rcarvalhocwb/conexao-topdata/releases

**Baixe o `RayzerXAcess-Setup-0.1.N.zip`**, extraia e execute o `.exe` como administrador.
O `.exe` solto também está lá, mas Chrome e Edge costumam segurar download de `.exe`
grande sem assinatura digital ("não é baixado com frequência") e às vezes não terminam; o
`.zip` passa. Ao abrir, o Windows pode mostrar "O Windows protegeu o computador": clique em
**Mais informações → Executar assim mesmo**. As duas coisas somem com o certificado de
assinatura de código (ainda não existe).

`RayzerXAcess-MSI-0.1.N.zip` traz o mesmo sistema como MSI, sem a tela de boas-vindas.

### O que ele faz

Execute como administrador. Ele:

- instala o serviço, o painel, o programa das catracas e o assistente em
  `C:\Program Files\Rayzer\XAcess`, cada um com o seu .NET — nada para instalar antes;
- registra o serviço **ConexaoTopdataEdge** com partida **automática** (não o inicia
  durante a instalação: quem inicia é o assistente, depois de gravar a configuração). Se o
  serviço cair, o Windows o sobe de novo em 5 s (recuperação configurada pelo instalador);
- cria no **Firewall do Windows** a regra de entrada **"Rayzer XAcess — catracas (entrada
  TCP)"** para o programa das catracas: vale para qualquer porta que o assistente escolher,
  mas **só para a sub-rede local**, nunca para a internet. Catracas numa VLAN roteada, em
  outra sub-rede, pedem ampliar o endereço remoto da regra (RB-02). A regra sai na
  desinstalação;
- põe o **painel perto do relógio** (bandeja do sistema) quando alguém entra no Windows —
  `INICIAR_NA_BANDEJA=0` na linha de comando do MSI desliga;
- enquanto o serviço roda, o Windows **não suspende** o computador por inatividade (a tela
  pode apagar; o plano de energia não é alterado);
- cria no menu Iniciar o **Painel do evento**, o **Assistente de configuração** e o
  **Modelo de planilha de cartões** (`docs/26`);
- ao terminar, o botão **Abrir o painel** abre o painel. Enquanto a instalação não estiver
  configurada, o painel mostra o botão **Abrir assistente de configuração** (que pede a
  permissão de administrador);
- fala português em todas as telas, inclusive "Programas abertos";
- **atualiza** rodando o Setup novo por cima, e **desinstala** por "Aplicativos instalados"
  do Windows. Nos dois casos, fecha sozinho o painel e o assistente se estiverem abertos, e
  o serviço, ao parar, encerra o programa das catracas. Os dados em
  `C:\ProgramData\ConexaoTopdata` ficam. Na atualização de uma instalação já configurada, o
  serviço é iniciado de novo ao fim; se não subir, a atualização não é desfeita e o painel
  mostra o serviço fora (RB-01). Esse caminho ainda não foi ensaiado numa VM (docs/41, Fase 2).
- o instalador de **teste** (pré-release deste repositório) se chama **Rayzer XAcess (teste)** e tem
  identidade própria, separada do instalador de **produção** (o do repositório privado, com a
  EasyInner.dll). Um nunca atualiza o outro (docs/41, achado E10-2). Quem tem instalada uma versão de
  teste anterior a esta mudança precisa desinstalá-la por "Aplicativos instalados" antes de instalar
  a nova; os dados em `C:\ProgramData\ConexaoTopdata` ficam.

### O que fica de fora, e como o assistente resolve

| Item | Por que não vem | Como resolver |
|---|---|---|
| SDK da Topdata (`EasyInner.dll`) | É da Topdata, e **este repositório é público** — o SDK proprietário nunca é versionado aqui | **No instalador de produção** o SDK já vem embarcado (ver abaixo). **No instalador de teste** (CI público, sem SDK): instale o SDK Inner Acesso, **ou** no assistente clique em **Localizar EasyInner.dll…**. Ele recusa a DLL de 64 bits |
| .NET Framework 3.5 | É recurso do Windows, não arquivo | O Setup **habilita sozinho** durante a instalação (DISM; pode precisar de internet). Se falhar, a instalação segue e o assistente tem o botão **Habilitar .NET Framework 3.5**. **Sem internet:** botão **Habilitar sem internet (mídia do Windows)…**, escolhendo a pasta `sources\sxs` do ISO montado ou do pendrive de instalação **da mesma versão do Windows do PC** (ver abaixo) |

**.NET Framework 3.5 sem internet** (docs/41, achado E10-8). Com a mídia do Windows à mão (ISO
montado ou pendrive, da mesma versão e edição do Windows do PC), use o botão do assistente, ou rode
num prompt de administrador, trocando `D:` pela letra da mídia:

```
dism /Online /Enable-Feature /FeatureName:NetFx3 /All /NoRestart /Source:D:\sources\sxs /LimitAccess
```

`/LimitAccess` impede o DISM de ir ao Windows Update. A pasta precisa ter o arquivo
`microsoft-windows-netfx3-ondemand-package…cab`; sem ele, o assistente recusa a pasta e diz qual escolher.
Ainda não ensaiado numa VM sem rede (docs/41, Fase 2).

**Sem catraca física?** Marque **Modo simulação** no passo 2 do assistente: o SDK não é
necessário e o painel ganha a tela **Simulador**. Ver [docs/23](../docs/23-modo-simulacao.md).

### Instalador de produção com o SDK da Topdata embarcado

Com autorização da Topdata, o instalador de produção sai com a `EasyInner.dll` (e as DLLs
que ela usa) **embarcadas** ao lado do `Edge.Worker.X86.exe`. Assim o operador só instala e
configura — nada de baixar ou registrar o SDK à parte; o assistente detecta a DLL sozinha.

O SDK é proprietário e **nunca entra neste repositório público**. Ele chega ao build de uma
destas duas formas:

**A) Na bancada (uma máquina Windows com o SDK instalado):**

```powershell
# Aponte para a pasta do SDK Inner Acesso (onde está a EasyInner.dll)
.\installer\publicar.ps1 -SdkDir "C:\Topdata\SDK Inner Acesso"
```

A `EasyInner.dll` é copiada para `artifacts\Edge.Worker.X86`, e o build do MSI a embarca.

**B) No repositório privado, pelo workflow de modelo (recomendado):**

O SDK fica num repositório **privado** próprio. O código público nunca o recebe: o CI
público reprova qualquer MSI que contenha a `EasyInner.dll`.

1. Crie um repositório **privado** (ex.: `rcarvalhocwb/rayzer-xacess-sdk`) com a pasta
   `inner/` na raiz, contendo a `EasyInner.dll` e as DLLs que ela usa.
2. Copie [`installer/privado/publicar-com-sdk.yml`](privado/publicar-com-sdk.yml) para
   `.github/workflows/publicar-com-sdk.yml` desse repositório privado.
3. Em **Actions → Publicar o instalador com o SDK da Topdata → Run workflow**, informe o SHA
   completo de um commit deste repositório público que **já passou no CI**.

O workflow só empacota commit com todas as verificações em sucesso, monta o MSI e o Setup com
a `EasyInner.dll` e publica como pré-release **do repositório privado**. Não há token nem
segredo para configurar. Quem for instalar a versão com o SDK precisa de acesso ao repositório
privado, ou receber o `.zip` por fora.

Antes de liberar a uma catraca de verdade, falta só o ensaio de bancada **HIL-STACK-01**
(a `EasyInner.dll` carrega num processo .NET 10 de 32 bits?) e a primeira conversa com o
equipamento.

### Operadores: quem pode abrir o painel

O painel roda com a conta de cada operador, e o serviço só aceita quem está no grupo local
**`ConexaoTopdata Operadores`** (ou for administrador). Sem isso, o painel dos operadores não
conecta, e nenhum usuário comum da máquina consegue comandar as catracas.

Depois de instalar, como administrador, na pasta de instalação:

```powershell
.\configurar-operador.ps1 -Operador 'PORTARIA\ana','PORTARIA\bruno'
```

O script cria o grupo (se faltar), põe as contas nele e reinicia o serviço. Cada operador
precisa **sair e entrar de novo no Windows** para a permissão valer. O script não lê nem imprime
o token do serviço. Ver `docs/runbooks/RB-01` se o painel continuar sem conectar.

### Configurar — Assistente de configuração

Abra pelo menu Iniciar (ele pede permissão de administrador):

1. **Ambiente** — o que o computador tem e o que falta.
2. **Catracas** — número do Inner e nome de cada uma, e a porta (3570). Em cada catraca,
   aponte o servidor para o IP deste computador e essa porta.
3. **Nuvem** — opcional: endereço, identificador deste computador, formato do cartão,
   intervalo de reuso, só na urna, e o segredo.
4. **Gravar** — confere tudo, grava e reinicia o serviço.

Onde fica cada coisa, em `%ProgramData%\ConexaoTopdata`:

| Arquivo | O que é |
|---|---|
| `workers.json` | a configuração que o assistente gravou |
| `acesso.db` | a base local: ingressos, cartões, tentativas, fila de envio |
| `token` | senha interna entre o painel e o serviço, gerada pelo serviço na primeira subida; só administradores escrevem, usuários leem |
| `segredos\nuvem.segredo` | o segredo da nuvem, cifrado pelo Windows (DPAPI) para esta máquina; só SYSTEM e administradores leem |
| `registros\` | registro diário do serviço e de cada programa de catraca, sem número de cartão |

Reabrir o assistente traz a configuração atual preenchida; o segredo nunca é mostrado, e
deixar o campo em branco mantém o que está no cofre.

### Segundo plano e o ícone perto do relógio

As catracas são atendidas pelo **serviço**, não pelo painel. O serviço sobe com o Windows,
mesmo sem ninguém entrar, e continua com o painel fechado.

- **Fechar o painel** (o X) só o esconde: ele fica no ícone perto do relógio. Na primeira
  vez, um aviso explica que as catracas continuam atendendo.
- **Ícone:** passar o mouse mostra "catracas 3/4 · serviço operacional". Um aviso do Windows
  aparece quando uma catraca para de atender, e quando o serviço some ou volta. Clique
  duas vezes para abrir o painel.
- **Menu do ícone (botão direito):**
  - **Abrir o painel**;
  - **Gerenciar catraca**;
  - **Iniciar a operação**;
  - **Encerrar a operação (parar as catracas)…**: pede confirmação e a permissão de
    administrador, e para o serviço — as catracas deixam de ser atendidas;
  - **Fechar o painel (as catracas continuam)**.
- Abrir o painel pelo menu Iniciar com ele já na bandeja só traz a janela: um painel por
  sessão do Windows.

### Usar — Painel do evento

Abre pelo menu Iniciar e pede usuário e senha (ver abaixo); o token da instalação é lido de `%ProgramData%`. Recém-instalado e
sem configuração, o cabeçalho diz "Instalação ainda não configurada — abra o Assistente de
configuração".


### Login: usuário e senha (ADR-0026)

O painel pede usuário e senha. Na instalação nova, existe um único usuário:

| Usuário | Senha |
|---|---|
| `admin` | `xacess` |

- **Primeiro acesso:** o painel pede, na hora, o **nome e o login de quem vai administrar** e uma **senha nova** (mínimo de 8 caracteres). Até isso, nada mais funciona, e a senha padrão deixa de valer depois da troca.
- **Usuários e papéis:** o administrador cria os demais usuários em **Usuários** e escolhe o papel de cada um. Os papéis prontos são:
  - Administrador;
  - Supervisor;
  - Portaria;
  - Somente leitura.

  O administrador também pode criar papéis e marcar o que cada um pode fazer. O serviço confere a permissão em cada ação, não só a tela.
- **Senha esquecida:** o administrador define uma senha provisória em **Usuários**, e a pessoa troca no próximo acesso.
- **Bloqueio:** 5 senhas erradas seguidas bloqueiam o usuário por 5 minutos.
- **Antes de entrar:** o ícone perto do relógio continua mostrando a situação das catracas. As telas só abrem depois do login.

## Imagens das telas

Numa máquina Windows, com o serviço rodando:

```powershell
& "C:\Program Files\Rayzer\XAcess\Painel\Desktop.App.exe" --capturar capturas
```

Fotografa todas as telas do painel com os dados reais do serviço e grava
`capturas\relatorio.txt`. Sem serviço, as telas saem com "sem resposta do serviço local".
Renderiza o **conteúdo** da janela, sem a barra de título. O CI roda o mesmo modo no job
do instalador e fotografa também a primeira tela do Setup (`00-setup.png`).

## A arte do Setup

O Setup tem tema próprio (`installer/wix/tema-rayzer.xml`): a arte da marca
(`marca/arte-rayzer-x.jpg`) de fundo, com os textos e botões numa faixa escura à direita.
O fundo é gerado por `python tools/gerar-fundo-do-setup.py`: troque a arte e rode de novo.
O fundo é pintado pela própria janela (`Theme/@ImageFile` + `Window/@SourceX/SourceY`),
por baixo dos textos e botões, e precisa ser maior que a janela (822 x 463 para 820 x 461).
Não use `ImageControl` como fundo: ele fica por cima dos botões.

## Antes de encostar em hardware

Antes de conectar uma catraca de verdade falta um ensaio:

**`HIL-STACK-01`** — um processo .NET moderno compilado `win-x86` consegue carregar a
`EasyInner.dll`? A DLL exige .NET Framework 3.5, o que sugere assembly *mixed-mode*.
Se falhar, só o worker vira .NET Framework 4.8, isolado atrás do IPC
(ver `docs/12-decisao-de-stack.md`).

As assinaturas P/Invoke **já são reais** (escritas à mão em
`src/Topdata.EasyInner.Interop/EasyInnerNative.cs` e geradas do SDK 6.0.2.0 em
`EasyInnerGerada.cs`); o adapter real existe. O que resta é a DLL na máquina (embarcada no
instalador de produção, acima) e o ensaio de carga acima.

Enquanto isso, o worker roda contra o simulador e toda a lógica já é exercitada.

## Portas

Cada worker escuta numa **porta TCP própria** (padrão 3570, depois 3571, 3572…), e a
catraca precisa apontar para a porta do worker que a gerencia. Realocar um equipamento
entre workers exige reconfigurar a catraca — não é operação de software.
Ver `docs/ADR/ADR-0021-porta-por-worker-e-protocolo-nda.md`.

O IPC entre painel e serviço **não usa porta TCP**: é named pipe.
