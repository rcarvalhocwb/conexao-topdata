# 11 — O que dá para fazer com o SDK: inventário completo de capacidades

> **Fonte:** Manual de Integração SDK Inner Acesso (PDF oficial, obtido via portal do
> integrador) + FAQs oficiais do portal. Salvo indicação contrária, tudo aqui é
> `FONTE_PRIMARIA`. As lacunas remanescentes estão marcadas.

## 1. O que o SDK é — e o que ele não é

A EasyInner.dll **não é uma API REST**. É uma biblioteca Win32 que fala um **protocolo
binário proprietário sobre socket TCP/IP**, e o seu software é o **servidor**: as catracas é
que abrem a conexão, apontando para o IP e a porta que você escuta (padrão **3570**).

Quatro características definem toda a arquitetura possível:

| Característica | Consequência |
|---|---|
| **32 bits (x86)** | O processo que carrega a DLL precisa ser x86, mesmo em Windows 64 |
| **Bloqueante** | Cada chamada pausa a thread até concluir, falhar ou dar timeout |
| **Não thread-safe** | **Uma única thread** pode tocar a DLL. Acesso serializado, sem exceção |
| **~30 equipamentos por instância** | Acima disso, múltiplos processos — **cada um em sua própria porta TCP** |

Exige **.NET Framework 3.5+** instalado no Windows (dependência da DLL, não do nosso
código) e instalador oficial que registra as DLLs em `System32`/`SysWOW64`.

## 2. Inventário funcional: 58 funções mapeadas

Tudo em [`compatibility-matrix/funcoes-easyinner.csv`](compatibility-matrix/funcoes-easyinner.csv),
com assinatura, parâmetros, retornos, riscos e teste associado.

### 2.1 Comunicação e diagnóstico — 7 funções

`DefinirTipoConexao` · `AbrirPortaComunicacao(Porta)` · `FecharPortaComunicacao()` ·
`TestarConexaoInner(Inner)` · `PingOnline(Inner)` · `ReceberVersaoFirmware(...)` ·
`ReceberRelogio(...)`

**O que dá para construir:** descoberta e identificação automática de equipamento
(modelo, linha, firmware, se tem biometria), teste de conectividade, medição de drift de
relógio, health check por equipamento.

`ReceberVersaoFirmware` devolve `Linha`, `Variacao`, `VersaoAlta`, `VersaoBaixa`,
`VersaoSufixo` e `InnerAcessoBio` — é a **base programática do capability discovery**
([ADR-0010](ADR/ADR-0010-capability-discovery.md)).

### 2.2 Configuração do equipamento — 22 funções

Padrão e dígitos do cartão, tipo de leitor, operação dos leitores 1 e 2, relés, teclado,
cartão master, Wiegand duplo, biometria variável, registro de acesso negado, mudança
automática on-line/off-line, envio atômico.

**Valores concretos que o produto pode oferecer como escolha ao instalador:**

- **Tipo de leitor (0–7):** código de barras · magnético · proximidade Abatrack II ·
  Wiegand · Smart Card serial · barras serial · Wiegand FC sem separador ·
  **TTL/Serial ASCII** (o mais flexível, cobre QR Code serial).
- **Operação de cada leitor (0–4):** desabilitado · somente entrada · somente saída ·
  entrada e saída · **entrada e saída invertidas**.
- **Função de cada relé (0–5):** não utilizado · registro entrada ou saída · registro
  entrada · registro saída · sirene · revista. Tempo de 0 a 50 s.
- **Dígitos:** fixo (`DefinirQuantidadeDigitosCartao`) ou **variável**
  (`InserirQuantidadeDigitoVariavel`, uma chamada por tamanho aceito).

> ⚠️ **Armadilha documentada:** `ConfigurarAcionamento1/2` **não** deve ser usado para
> acionar o giro. Giro é `LiberarCatraca...()`. Relé é para urna, sirene, registro.

### 2.3 Operação em tempo real (on-line) — 18 funções

`ReceberDadosOnLine` é o centro: devolve `Origem`, `Complemento`, `Cartao` e data/hora
completa (com segundos).

**Liberação de giro — cinco variantes:** `LiberarCatracaEntrada` · `LiberarCatracaSaida` ·
`LiberarCatracaEntradaInvertida` · `LiberarCatracaSaidaInvertida` ·
`LiberarCatracaDoisSentidos`.

**Isso resolve o item B6 do nosso backlog:** existe função de liberação de **entrada** e
existe a variante **invertida**. O workflow `CollectCardThenEnter` é implementável — o que
resta é o comissionamento decidir qual das quatro usar naquele portão.

**Sinalização:** bip curto/longo, LED verde, LED vermelho, backlight, mensagem padrão e
mensagem temporária no display (32 caracteres, ou 16 com data/hora).

> LEDs verde e vermelho são **somente Inner Acesso (Linha 3)**. Na Linha 4 o produto
> precisa sinalizar por display e bip.

**Urna:** `AcionarRele2(Inner, Tempo)` abre a fenda.

### 2.4 Operação off-line — 7 funções

Lista branca/negra, inserção de usuário com horário, envio e limpeza de lista, horários de
acesso, coleta de bilhetes.

**Semântica do horário por usuário:** `1–100` = horário cadastrado · **`101` = sempre
liberado** · **`102` = sempre negado**. Isso dá, sem tabela de horário nenhuma, uma
lista branca com exceções — útil para o subconjunto priorizado do nível T2.

> ⚠️ **Alterar um único usuário exige reenviar a lista inteira.** A catraca sobrescreve.
> Em evento grande isso é uma operação cara e precisa ser planejada, não feita a cada
> mudança.

### 2.5 Coleta de bilhetes — reconciliação

`ColetarBilhete` devolve `Tipo`, data/hora (**sem segundos**) e `Cartao`, e **remove o
bilhete da memória do equipamento** a cada coleta bem-sucedida.

> ⚠️ **Risco crítico confirmado:** se o processo morrer entre "a DLL removeu o bilhete" e
> "gravamos no SQLite", o evento **desaparece**. Por isso a gravação local é commitada
> antes de pedir o próximo bilhete — teste `CHAOS-REC-01`.

Tipos de bilhete em [`compatibility-matrix/tipos-bilhete.csv`](compatibility-matrix/tipos-bilhete.csv).
Destaques: 10/11 entrada/saída por cartão · 12/13 negadas · 110–113 equivalentes por
teclado · **128 = bilhete repetido** (o próprio equipamento sinaliza duplicata).

### 2.6 Biometria — parcialmente documentado

`EnviarDigitalUsuarioBio`, `SetarBioVariavel`, `ConfigurarBioVariavel`, mais as origens
12, 13 e 18. **O manual cita sem detalhar assinaturas** — o detalhamento está nos
exemplos da SDK (pacote separado). Lacuna aberta, ver seção 5.

### 2.7 Facial — SDK à parte, e um ponto que muda o produto

Confirmado na fonte oficial:

- Facial fala **WebSocket/JSON**, porta padrão **7792**. Não passa pela EasyInner.
- No arranjo **facial + catraca Inner**: o leitor facial reconhece o rosto e manda o
  **número de cartão** para a catraca por ligação física; a catraca repassa via EasyInner;
  o software decide; o software libera o giro via EasyInner.
  → **O fluxo de acesso é 100% EasyInner.** O SDK facial serve para cadastrar pessoas e
  faces.
- Na **Catraca Easy** (linha facial), a Topdata afirma textualmente: **não existe evento de
  confirmação de giro físico via WebSocket**. O `sendlog` é o único retorno e confirma
  apenas autorização e comando elétrico.

Isso confirma [ADR-0007](ADR/ADR-0007-autorizacao-versus-passagem.md) com fonte primária:
em linha Easy, "passou" é indeterminável pelo software. Em linha Inner com sensor óptico,
a origem 6 dá a prova.

## 3. O que dá para construir — mapa de funcionalidades

| Módulo do produto | Funções que o sustentam | Viável? |
|---|---|---|
| Descoberta e inventário automático de parque | `TestarConexaoInner`, `ReceberVersaoFirmware` | ✅ total |
| Capability discovery por firmware | `ReceberVersaoFirmware` (campo `Linha`) | ⚠️ tabela de linhas incompleta (ver 5) |
| Configuração com diff, aprovação e rollback | todas as `Configurar*`/`Definir*` + `EnviarConfiguracoes` | ✅ total |
| Monitor ao vivo de eventos | `ReceberDadosOnLine` + tabela de origens | ✅ total |
| Decisão de acesso local | lógica própria (a DLL não decide nada no on-line) | ✅ total |
| Liberação por sentido, com perfil comissionado | 5 variantes de `LiberarCatraca*` | ✅ total |
| Confirmação de passagem física | origem 6 (Inner com sensor óptico) | ✅ na linha Inner · ❌ na linha Easy |
| `CollectCardThenEnter` (urna liberando entrada) | `AcionarRele2` + origem 7 + `LiberarCatracaEntrada[Invertida]` + origem 6 | ✅ viável |
| Alerta de urna cheia | origem 20 | ✅ total |
| Autonomia off-line no equipamento | lista branca/negra + horários + mudança automática | ✅ limitada ao tamanho da lista |
| Reconciliação de bilhetes | `ColetarBilhete` + tipo 128 | ✅ total |
| Sincronização de relógio e horário de verão | `EnviarRelogio`, `ReceberRelogio`, `EnviarHorarioVerao` | ✅ total |
| Feedback ao usuário no equipamento | bips, LEDs, backlight, mensagens | ✅ (LEDs só Linha 3) |
| Biometria digital | `EnviarDigitalUsuarioBio` + origens 12/13/18 | ⚠️ assinaturas nos exemplos |
| Facial | SDK separado (WebSocket) | ✅ com o outro SDK |
| Catraca expedidora de cartões | `ColetarBilhete` + tipos 003/004/005 | ✅ (opera off-line) |

**Resposta curta à pergunta "dá para controlar totalmente a catraca?":**
Sim, no modo on-line o software decide tudo — a catraca não valida nada sozinha, só
executa. O limite não é de funcionalidade: é de **arquitetura** (x86, thread única,
30 equipamentos por processo) e de **confirmação física** (origem 6 só na linha Inner).

## 4. Descobertas que mudam decisões já tomadas

### 4.1 O WebServer **sempre** perde para o SDK — e os defaults da DLL vazam

A Topdata é explícita: *"o software é sempre a fonte da verdade das configurações"*.
E, o que é mais perigoso: **`EnviarConfiguracoes()` envia um conjunto completo de
parâmetros, incluindo os valores padrão da DLL para tudo que você não setou
explicitamente.**

Ou seja: montar meia configuração não deixa o resto como estava — **sobrescreve com os
defaults da DLL**. Esse é um modo de falha silencioso, de campo, que derruba uma
instalação inteira.

→ Novo [ADR-0020](ADR/ADR-0020-configuracao-sempre-completa.md). Muda o risco R-20: não
basta detectar drift, é obrigatório **enviar sempre a configuração completa e explícita**,
a cada conexão.

### 4.2 Cada processo precisa da sua própria porta TCP

O manual recomenda múltiplas instâncias e diz: *"cada instância escute em uma porta TCP
diferente (ex.: Instância 1 na 3570, Instância 2 na 3571)"*.

→ Isso tem consequência **operacional**, não só de código: cada catraca precisa ser
configurada apontando para a porta do worker que a gerencia. Mover uma catraca de worker
é reconfigurar a catraca. O produto precisa tratar o **mapa porta↔worker↔equipamento**
como configuração de primeira classe, e o assistente de comissionamento precisa dizer ao
instalador qual porta usar.

### 4.3 Existe uma saída para o limite de 30 equipamentos

O manual registra, em 6.7: *"a integração direta via protocolo TCP/IP (sem a DLL,
conforme documentação de baixo nível e **solicitação de NDA**) pode ser uma alternativa.
Essa abordagem permite maior controle sobre o gerenciamento de conexões e threads"*.

**É o achado mais estratégico desta análise.** Com o protocolo sob NDA, some a restrição
x86, some o Windows obrigatório, some a thread única e some o teto de 30 equipamentos.
→ Tratado em [`12-decisao-de-stack.md`](12-decisao-de-stack.md) e
[ADR-0019](ADR/ADR-0019-stack-e-linguagem.md).

### 4.4 "Coletor Urna 4" ≠ "Catraca com Urna Coletora"

A especificação oficial do **Coletor Urna 4** mostra um produto que **não é uma catraca**:
é um pedestal que recolhe o cartão e aciona **cancela ou porta por contato seco** (relé
3 A). **Não tem mecanismo de giro, logo não emite origem 6.**

Em compensação, ele já implementa em firmware:

- **"Liberação de acesso somente após recolhimento dos cartões"** — a ordem do nosso
  workflow é garantida pelo equipamento;
- **detecção de desistência** de cartão não recolhido;
- **detecção de urna cheia**.

Também resolve a ambiguidade de dígitos: a lista guarda **15.000 usuários com cartões de
4 a 16 dígitos**. E confirma Wiegand **26, 34 e 37 bits**, Mifare 13,56 MHz e proximidade
125 kHz.

Consequência de projeto em [`04`](04-workflow-collect-card-then-enter.md): num Coletor
Urna 4, "recolheu" é o evento mais forte disponível, e consumir o ingresso ali é decisão
de negócio a ser aprovada, não default técnico.

### 4.5 A máquina de estados oficial

O manual publica a FSM recomendada, e ela é **mais específica** que a que eu havia
proposto — vale adotar os nomes oficiais para facilitar suporte:

```
ESTADO_CONECTAR → ESTADO_ENVIAR_CFG_OFFLINE → ESTADO_ENVIAR_CONFIGMUD_ONLINE_OFFLINE
→ ESTADO_ENVIAR_CFG_ONLINE → ESTADO_CONFIGURAR_ENTRADAS_ONLINE → ESTADO_ENVIAR_MSG_PADRAO
→ ESTADO_POLLING ⇄ ESTADO_VALIDAR_ACESSO → ESTADO_LIBERAR_CATRACA
→ ESTADO_MONITORA_GIRO_CATRACA → (volta a ESTADO_CONFIGURAR_ENTRADAS_ONLINE)
```
mais `ESTADO_RECONECTAR`, `ESTADO_ENVIAR_MSG_ACESSO_NEGADO` e `ESTADO_COLETAR_BILHETES`.

Dois detalhes que só aparecem aqui:

1. `AbrirPortaComunicacao` é chamada **uma única vez**, antes do laço — confirma o
   singleton por worker.
2. Após o giro (ou o timeout), o fluxo volta para **`ESTADO_CONFIGURAR_ENTRADAS_ONLINE`**,
   não direto para o polling. É assim que o leitor é reabilitado para a próxima leitura.
   Pular isso é a causa documentada de *"catraca/leitor trava após passar o cartão"*.

## 5. Lacunas que restam

| Lacuna | Onde fecha |
|---|---|
| Assinaturas de `DefinirTipoConexao`, `LiberarLeitor`, `InserirHorarioAcesso`, `EnviarHorariosAcesso`, `EnviarConfiguracoesFuncoes`, `EnviarDigitalUsuarioBio` | Pacote de **exemplos** da SDK (download separado) |
| Código de retorno exato para "sem eventos" e "sem bilhetes" | Exemplos da SDK (`RET_SEM_BILHETES` é citado sem valor) |
| Tabela de códigos de `Linha` do firmware: 8 códigos (1, 2, 3, 6, 7, 14, 16, 18) para 7 descrições — o PDF saiu desalinhado | Exemplos da SDK ou suporte |
| `ApagarListaAcesso`: tem parâmetro `Inner`? Envia automaticamente? O manual se contradiz | Exemplos da SDK ou suporte |
| Tipos de bilhete `003/004/005/010/012/013` colidem com `3/4/5/10/12/13` | Suporte Topdata |
| `DefinirQuantidadeDigitosCartao`: seção 4.1.2 diz 4–16, tabela 4.1.9 diz 1–16 | Suporte Topdata |
| Capacidade real de lista e de bilhetes **por modelo** | Ensaio de bancada |
| Origens 11, 14–17, 19 | Ausentes da tabela oficial — tratadas como desconhecidas ([ADR-0018](ADR/ADR-0018-eventos-desconhecidos.md)) |

**O próximo download que destrava tudo:** o pacote de **exemplos em C#** do portal do
integrador. Ele contém o `EasyInner.cs` com as assinaturas P/Invoke reais e o enum
`Enumeradores.Retorno` com os valores que faltam.

## Fontes

- [Manual de Integração SDK Inner Acesso (PDF oficial)](https://www.dropbox.com/scl/fi/wwx4r7mu5nwjk7hoqckjf/Manual-de-Integra-o-SDK-Inner-Acesso.pdf?rlkey=s5laub5eqd3sr5a0h4nnubh01&dl=0)
- [Funcionamento do SDK para Catracas e Coletores – Linha Inner](https://integrador.topdata.com.br/suporte/funcionamento-sdk-easyinner/)
- [Quantos equipamentos a EasyInner.dll pode gerenciar simultaneamente?](https://integrador.topdata.com.br/suporte/quantos-equipamentos-a-easyinner-dll-pode-gerenciar-simultaneamente/)
- [Por que a catraca perde as configurações feitas via WebServer?](https://integrador.topdata.com.br/suporte/por-que-a-catraca-perde-as-configuracoes-feitas-via-webserver/)
- [Como funciona o fluxo de comunicação entre Leitor Facial + Catraca Linha Inner?](https://integrador.topdata.com.br/suporte/como-funciona-o-fluxo-de-comunicacao-entre-leitor-facial-catraca-linha-inner/)
- [Catraca Easy – Existe confirmação de giro físico via WebSocket?](https://integrador.topdata.com.br/suporte/catraca-easy-existe-confirmacao-de-giro-fisico-via-websocket/)
- [Download do SDK para Catracas e Coletores – Linha Inner](https://integrador.topdata.com.br/suporte/download-sdk-inner-acesso/)
- [Especificações técnicas do Coletor Urna 4](https://suporte.topdata.com.br/suporte/especificacoes-tecnicas-coletor-urna-4/)
