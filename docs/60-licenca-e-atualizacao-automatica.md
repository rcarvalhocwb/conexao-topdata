# 60 — Licença e atualização automática

> Pedido do dono do produto em 15/10/2026: "o sistema não pode buscar as atualizações dentro da
> pasta releases do git e atualizar automaticamente? Inclusive por aí conseguimos fazer a gestão do
> vencimento das licenças."
>
> Decisões do dono, no mesmo dia:
> 1. No vencimento, **as catracas nunca param**; bloqueia só o administrativo, com **15 dias de
>    tolerância**.
> 2. A atualização **baixa sozinha e instala em silêncio** quando o sistema é finalizado ou de
>    madrugada.
> 3. **O dono aprova** qual versão vira estável.

## 1. Por que a licença não mora no GitHub

As Releases são públicas, e uma lista de clientes com vencimento ficaria à vista. E o evento roda
sem internet (ADR-0017): a licença tem de ser conferida no próprio computador. Por isso:

- **Licença = um texto assinado pela Rayzer**, com a mesma técnica da recuperação pelo suporte
  (ECDSA P-256, docs/58). O XAcess confere a assinatura com a chave pública embutida, sem rede.
- **Quem assina é a chave do suporte**, que fica só na máquina do suporte. A ferramenta
  `Rayzer.Suporte` ganhou a opção **3 — Emitir licença**.
- **Sem chave pública na versão** (`ChavesDoSuporte.Publicas` vazio), a licença não é exigida: nada
  muda para quem já usa.

### 1.1 O texto da licença

`XAL1.<dados em base64url>.<assinatura em base64url>`, onde os dados são um JSON:

| Campo | Exemplo | Regra |
|---|---|---|
| `v` | `1` | Versão do formato |
| `id` | `L-2026-0001` | Número da licença, para o suporte achar |
| `cliente` | `Rua Iluminada` | Aparece no painel |
| `instalacao` | `a1b2c3…` ou `*` | O identificador da instalação (Diagnóstico); `*` vale em qualquer computador |
| `vence_em` | `2027-01-31` | Vale até o fim desse dia, no horário de Brasília |
| `emitida_em` | `2026-10-15T18:00:00Z` | Registro |

### 1.2 Situações

| Situação | Quando | O que acontece |
|---|---|---|
| Não exigida | Versão sem chave pública | Nada |
| Válida | Faltam mais de 30 dias | Nada |
| Vence em breve | Faltam 30 dias ou menos | Aviso no painel com a data |
| Em tolerância | Venceu há até 15 dias (ou sem licença há até 15 dias) | Aviso forte; tudo funciona |
| Bloqueada | Passou da tolerância | Bloqueia o administrativo (abaixo); **as catracas seguem** |

Sem licença instalada, a tolerância de 15 dias conta da primeira partida do serviço com a exigência
ligada.

**Bloqueado com a licença vencida:** atualizações automáticas, os relatórios e a prestação de contas
(`ObterRelatorios`, `ObterPrestacaoDeContas`) e mudar o evento (`GravarEvento`).
**Nunca bloqueado:** catracas, acessos, painel ao vivo, sincronização, login, usuários, diagnóstico
e instalar a licença nova.

### 1.3 Renovar

Diagnóstico → **Licença** → colar o texto → **Instalar licença** (permissão `configuracao.editar`).
A tela mostra o identificador da instalação para mandar à Rayzer. Entregar a licença pela nuvem fica
para depois (o canal existe: a sincronização).

## 2. Atualização automática

### 2.1 O SDK da Topdata sai do instalador de produção

Hoje o instalador de produção leva a `EasyInner.dll` e só existe no repositório privado. Se a
atualização viesse de lá, cada PC de evento precisaria de um token com acesso ao repositório do
SDK. Decisão:

- O **instalador de produção estável é público e sem o SDK** (como o de teste), montado pelo CI
  público na promoção (§2.4).
- O SDK chega ao PC como já previsto no assistente: **"Localizar EasyInner.dll"** (copia para a
  pasta das catracas) ou o instalador do SDK da Topdata (`SysWOW64`).
- **O serviço guarda uma cópia do SDK** em `%ProgramData%\ConexaoTopdata\sdk\` e a **repõe** na
  pasta das catracas se ela sumir (por exemplo, numa atualização por cima de uma instalação feita
  pelo workflow privado, que é dona da DLL). A reposição acontece antes de as catracas subirem.

### 2.2 Canais

| Canal | Instalador | Atualiza sozinho? |
|---|---|---|
| Produção | "Rayzer XAcess", release estável `v0.1.N` (pública, marcada como *latest*) | **Sim** |
| Teste | "Rayzer XAcess (teste)", pré-release `teste-0.1.N` | Não: a cada commit sairia uma |

O instalador grava o canal em `HKLM\Software\Rayzer\XAcess\Canal`. O serviço só aceita uma ficha de
versão do mesmo canal.

### 2.3 O ciclo no serviço

1. **Verifica** 10 minutos depois de subir e a cada 6 horas:
   `GET https://api.github.com/repos/rcarvalhocwb/conexao-topdata/releases/latest`. A *latest* do
   GitHub nunca é pré-release: só as versões que você aprovou.
2. **Confere a ficha** `versao.json` com a assinatura `versao.json.sig` e a chave pública de
   atualização embutida (`ChavesDeAtualizacao.Publicas`). Sem assinatura válida, nada é baixado.
3. **Baixa** o MSI para `%ProgramData%\ConexaoTopdata\atualizacoes\<versão>\` e confere tamanho e
   SHA-256 contra a ficha.
4. **Instala em silêncio**, só quando:
   - **o sistema foi finalizado**: o operador saiu do painel (bandeja → Sair) e as catracas estão
     sem leitura há 5 minutos; ou
   - **de madrugada**: entre 03:00 e 05:00 de Brasília, com as catracas sem leitura há 30 minutos.

   Nunca com a licença bloqueada. A mesma versão é tentada no máximo 3 vezes.
5. **Como instala:** cópia da base (`copias\antes-da-atualizacao`), cópia do SDK, e uma tarefa
   agendada do Windows (conta SYSTEM) roda `msiexec /i … /qn /norestart`. O MSI fecha o painel,
   para o serviço, troca os arquivos e sobe o serviço de novo, como numa atualização manual. A
   tarefa é necessária porque o próprio serviço é parado no meio da instalação.
6. **Depois:** o serviço novo confere a própria versão e registra "atualizado de A para B" (ou a
   falha). O painel mostra as Novidades na próxima abertura, como já faz.

**Painel aberto de madrugada:** 2 minutos antes, o estado avisa o painel. Ele deixa um relançador
(PowerShell oculto, na conta do operador) e fecha. O relançador espera a instalação terminar e
abre o painel de novo na bandeja.

### 2.4 Aprovar uma versão estável

1. Escolha uma versão de teste já conferida (por exemplo `teste-0.1.215`).
2. GitHub → **Actions** → **Promover versão estável** → **Run workflow** → digite `0.1.215`.
3. O workflow:
   - remonta o MSI e o Setup **de produção** a partir do mesmo commit, sem o SDK;
   - gera a ficha;
   - assina a ficha com o segredo do GitHub;
   - publica a release `v0.1.215`, que vira a *latest*.

As pré-releases de teste são apagadas depois das 5 últimas; aprove dentro dessa janela. As estáveis
nunca são apagadas.

### 2.5 As chaves (uma vez)

Na `Rayzer.Suporte`, opção **4 — Gerar a chave de atualização**:

- `atualizacao-privada.pem` e a senha vão para os **segredos do repositório** no GitHub
  (Settings → Secrets → Actions):
  - `CHAVE_DE_ATUALIZACAO`: o conteúdo do `.pem`;
  - `SENHA_DA_CHAVE_DE_ATUALIZACAO`: a senha.
- `atualizacao-publica.txt` vai para quem mantém o código, que a põe em
  `ChavesDeAtualizacao.Publicas`.

Sem a chave pública, a atualização automática fica desligada e o Diagnóstico diz isso.

## 3. Onde aparece no painel

Diagnóstico ganha **Licença e atualizações**:

- **Licença:** situação, cliente, vencimento, identificador da instalação, campo para instalar.
- **Atualizações:** versão instalada, última estável, situação e quando vai instalar.

A barra do painel avisa a licença perto do vencimento ou vencida; as catracas continuam no centro.

## 4. Riscos e limites

- **Código aberto:** quem compilar o código pode tirar a conferência da licença. A licença é um
  impedimento para o uso normal; a proteção é o contrato, o suporte e as atualizações assinadas.
- **Sem certificado de assinatura de código:** o Windows não reclama numa instalação silenciosa pelo
  `msiexec`, mas a assinatura da ficha é o que garante que o MSI é nosso. Comprar um certificado
  de assinatura de código continua recomendado.
- **Teste físico pendente:** a instalação silenciosa, o relançador e a reposição do SDK só são
  provados num Windows real. Os testes automáticos cobrem a decisão, a ficha, a licença e os
  arquivos gerados; não cobrem o `msiexec` rodando.

## 5. Situação

| Parte | Situação (15/10/2026) |
|---|---|
| Licença | **Feito:** `Licencas` (formato e conferência), `ControleDeLicenca` e RPCs `ObterLicenca`/`InstalarLicenca` no serviço, bloqueio no `InterceptadorDeSessao`, aviso na barra e seção no Diagnóstico, opção 3 da `Rayzer.Suporte` |
| Atualização | **Feito:** `AtualizacaoAutomatica` (ficha, download, decisão, instalação por tarefa agendada), `GuardaDoSdk` na partida, canal no MSI, aviso de "painel finalizado", relançador do painel |
| Promoção | **Feito:** workflow `promover-estavel.yml` (confere o CI, monta o MSI de produção sem SDK, confere o canal e a chave, assina, publica) |
| Testes | `LicencasTests`, `LicencaNoPainelTests` (Unit); `AtualizacaoAutomaticaTests`, `PecasDaAtualizacaoTests`, `LicencaNoServicoTests` (Integration) |
| Chaves | **Depende do dono:** chave do suporte (opção 1, liga a licença) e chave de atualização (opção 4, liga a atualização). Enquanto não vierem, nada muda para quem usa |
| Teste físico | **Pendente:** instalação silenciosa, relançador e reposição do SDK num Windows real |
