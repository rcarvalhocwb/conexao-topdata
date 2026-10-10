# 21 — Roteiro da bancada: primeira TopFit 4 girando com um ingresso nosso

> **O que este ensaio responde:** um ingresso da nossa base faz uma TopFit 4 girar? E o
> que exatamente cada leitor da catraca entrega?
>
> **O que ele usa:** o mesmo laço, a mesma máquina de estados e a mesma base da operação.
> Não é simulação do fluxo — é o fluxo, com a tela ligada. Antes de chegar à bancada, ele
> foi exercitado inteiro contra o simulador (`tests/Integration/BancadaTests.cs`).

---

## 0. O que você precisa ter

| Item | Detalhe |
|---|---|
| Uma TopFit 4 | Com o leitor de QR externo e o leitor Mifare da urna instalados |
| Um PC Windows | Na **mesma rede** da catraca |
| SDK Inner Acesso instalado | É o que traz a `EasyInner.dll` |
| Os arquivos publicados | O **pacote da bancada** da CI (abaixo), ou `installer\publicar.ps1`, que gera `artifacts\Edge.Worker.X86\` |
| Cartões Mifare **de teste** | Cinco ou dez, nunca de cliente |
| Um celular | Para mostrar QR Codes de teste |
| Um gerador de QR | Qualquer site ou aplicativo que gere QR a partir de um texto |

**Como baixar o pacote da bancada.** No GitHub, entre logado, abra a aba **Actions**, clique
na execução mais recente da CI da branch com sucesso e, no fim da página, em **Artifacts**,
baixe `pacote-da-bancada`. É um zip com a pasta `Edge.Worker.X86` (autocontida: não
precisa instalar o .NET), este roteiro, o [modelo de relatório](21-relatorio-da-bancada.md),
o `identificacao-do-pacote.json` e o `verificar-ambiente.ps1`. Descompacte, por
exemplo, em `C:\Bancada`, e use `C:\Bancada\Edge.Worker.X86` onde este roteiro diz
`artifacts\Edge.Worker.X86`. A `EasyInner.dll` **não** vem no pacote: ela é da Topdata e
vem do SDK instalado na máquina.

Mantenha a estrutura do ZIP: o verificador e os dois documentos ficam **ao lado** da pasta
`Edge.Worker.X86`, não dentro dela. Antes do passo 1, no PowerShell:

```powershell
cd C:\Bancada
.\verificar-ambiente.ps1 -Porta 3570
Copy-Item .\21-relatorio-da-bancada.md .\resultado-da-bancada.md
```

O verificador reconhece essa estrutura, a instalação (`Worker`) e a publicação do
repositório (`artifacts\Edge.Worker.X86`). Registre sua saída e o código de saída no
relatório. Ele confere os pré-requisitos; **porta aberta pelo worker**, no passo 1, é
que prova o carregamento da DLL. Se a porta estiver livre, a linha `conferir` é esperada
antes de iniciar o worker.

Copie os dados de `identificacao-do-pacote.json` para o relatório e mantenha o arquivo
junto das evidências: ele identifica a versão, o commit efetivamente compilado, o commit
da tarefa, a execução do CI e os hashes SHA-256. Cada rodada usa uma cópia do modelo,
com data, modelo/firmware, SDK e todas as linhas inicialmente **NÃO EXECUTADO**.

O MSI (`instalador-msi`, mesma página) instala o serviço e o painel do operador. Os
pacotes do CI levam o .NET embutido. Os passos 1–6A e 7 usam o worker sozinho; os passos
6B–6J indicam quando precisam do sistema instalado. Quem publicar manualmente com
`installer\publicar.ps1` precisa dos runtimes .NET 10 x86, x64 e Desktop x64, pois esse
script usa `--self-contained false`. O SDK e os requisitos nativos continuam necessários.

**A catraca precisa ser configurada para se conectar a este PC.** Na integração pela
`EasyInner.dll`, quem inicia a conexão é a catraca, e o PC escuta. No Gerenciador de
Inners ou no WebServer da catraca, aponte-a para o **IP deste PC, porta 3570**, e anote o
**número do Inner** dela — ele é o `--inner` dos comandos abaixo.

> ⚠ **Se hoje a catraca está configurada para falar com o sistema do Supabase**, ela vai
> precisar ser reapontada para este PC durante o ensaio. Ver
> [`22`](22-sistema-supabase.md), pergunta 1.

---

## 1. A DLL carrega? (ensaio `HIL-STACK-01`)

```
cd artifacts\Edge.Worker.X86
Edge.Worker.X86.exe --porta 3570
```

| O que aparece | O que significa |
|---|---|
| `Porta aberta.` | **Passou.** Siga para o passo 2 |
| `Retorno 8 (GPF)` | Ambiente: volte à raiz do ZIP, rode `.\verificar-ambiente.ps1` e corrija o que ele apontar (no repositório: `installer\verificar-ambiente.ps1`) |
| `A EasyInner.dll não foi encontrada` | SDK não instalado, ou fora do caminho |
| `arquitetura incompatível` | O executável não saiu em 32 bits — publique de novo |

**Anote o resultado, qualquer que seja.** Se falhar mesmo com o ambiente certo, é a
resposta de `HIL-STACK-01`: o worker volta para .NET Framework 4.8, e nada mais muda
([`12`](12-decisao-de-stack.md)).

---

## 2. A catraca conecta e entra em operação

```
Edge.Worker.X86.exe --porta 3570 --bancada bancada.exemplo.json --inner 1
```

O arquivo de exemplo já vem na pasta, com cinco ingressos de teste. A tela mostra a
catraca conectando, lendo o firmware, recebendo a configuração e entrando em operação. O
display da catraca deve passar a mostrar **"Aproxime o ingresso"**.

Se ela ficar presa em `Conectar`, o problema é rede: IP, porta, firewall do Windows, ou
número do Inner diferente do `--inner`.

---

## 3. Descobrir o que cada leitor entrega — **o passo mais importante**

Com o sistema rodando, faça cada leitura e **anote exatamente o que aparece na tela**:

```
20:14:03.221 inner-1: origem Leitor2 (3) código=[0078234567] (10 caracteres)
```

| # | Leitura | Origem que apareceu | Código que apareceu | Caracteres |
|---|---|---|---|---|
| 1 | Cartão Mifare A **na fenda da urna** | | | |
| 2 | Cartão Mifare A **no leitor da frente**, se ele ler | | | |
| 3 | QR `1000000001` na tela do celular | | | |
| 4 | QR `1234567890123456` (16) | | | |
| 5 | QR `ABC1234567` (com letras) | | | |
| 6 | QR com **20 caracteres** | | | |
| 7 | Cartão Mifare A no **leitor do balcão** | — | | |
| 8 | Cartão do estoque, **INTEIRA ou MEIA**, cujo número no painel tem **12 dígitos** | | | |
| 9 | Cartão **SOCIAL** cujo número no painel tem **14 dígitos começando com `00`** | | | |
| 10 | Cartão **SOCIAL** cujo número no painel tem **14 dígitos sem zero no começo** | | | |
| 11 | Um dos cartões **INTEIRA** cujo número no painel tem **11 dígitos** | | | |
| 12 | Cartão **SOCIAL** cujo número no painel começa com **`0000`** | | | |

Cada linha responde uma pergunta que nenhum documento respondeu:

- **1 e 2** — de qual leitor o Mifare chega. Se for `Leitor2 (3)`, a regra "cartão só na urna"
  pode ser ligada.
- **3 e 4** — se o QR chega inteiro, e por qual origem.
- **5** — se letras passam. Se não passarem, o QR do Zet precisa ser só numérico.
- **6** — o que a catraca faz com QR longo demais: corta, recusa, ou entrega outra coisa.
- **7 comparado com 1** — **se o leitor do balcão e a catraca entregam o mesmo número.** Se
  não entregarem, toda venda do balcão será recusada na porta
  ([`20`](20-leitores-qr-e-cartao-mifare.md), seção 4.2).
- **8 a 12** — **se o número que a catraca lê é o mesmo cadastrado no painel.** O cadastro
  exportado em 25/09 tem números de 12, 14, 11 e 6 dígitos, e 31 cartões SOCIAL
  aparecem duas vezes (com e sem `00` na frente). Para cada linha, anote também o número
  que o painel mostra para aquele cartão e diga se são iguais, iguais a menos de zeros à
  esquerda, ou diferentes. É isso que decide a regra de normalização dos cartões
  ([`22`](22-sistema-supabase.md), seção 8.5). **Não escreva o número completo em
  nenhum lugar que vá para o repositório**; a tabela preenchida fica com você.

**Se nenhum QR for lido**, encerre (Ctrl+C) e rode de novo com `--tipo-leitor 5`. A
Topdata fala em "serial barcode", que no SDK pode ser o 5 ou o 8.

---

## 4. A primeira catraca girando

1. Abra `bancada.exemplo.json` e ponha, em `cartoes`, o **código que a catraca entregou**
   para o cartão A no passo 3 (linha 1).
2. Rode de novo com o mesmo comando do passo 2.
3. Mostre o QR `1000000001`.

A tela deve mostrar, em sequência:

```
20:31:07.105 inner-1: origem Leitor1 (2) código=[1000000001] (10 caracteres)
20:31:07.129 inner-1: LIBERADO AUTORIZADO (zet/inteira) em 20 ms
20:31:07.132 inner 1: giro liberado (Entrada)
20:31:09.480 inner-1: origem GiroConfirmado (6)
```

(Esta é a saída real do sistema, capturada contra o simulador. Na catraca, os tempos
serão outros.)

**Passe pela catraca.** A última linha — `GiroConfirmado (6)` — é a prova física: o sensor
óptico viu alguém passar.

4. Mostre o mesmo QR de novo: `NEGADO USOS_ESGOTADOS`.
5. Passe o cartão A na urna: `LIBERADO … bilheteria-local/inteira`.

---

## 5. As regras, uma a uma

| # | Faça | Deve aparecer |
|---|---|---|
| 1 | Mostre um QR que não está no arquivo | `NEGADO CREDENCIAL_DESCONHECIDA` |
| 2 | Libere com QR e **não passe** | depois de ~5 s, a tentativa fica sem giro — confira no resumo final |
| 3 | Passe o cartão A de novo, logo depois de usar | `NEGADO INTERVALO_DE_REUSO` |
| 4 | Troque `somenteNaUrna` para `true`, reinicie, passe o cartão **no leitor da frente** | `NEGADO FORA_DA_URNA` |
| 5 | O mesmo cartão, na **fenda da urna** | `LIBERADO` |
| 6 | QR `2000000005` duas vezes (passe de 2 usos) | `LIBERADO` nas duas; `NEGADO` na terceira |

---

## 6. Cronometrar

Com um cronômetro, dez passagens de cada:

| Fluxo | Do gesto até o giro liberado | Tempo total de passagem |
|---|---|---|
| QR na tela do celular | | |
| Cartão na fenda da urna | | |

Se a urna for muito mais lenta que o QR, vale uma catraca "QR expresso" por placa no dia do
evento ([`19`](19-bilheteria-local-e-divisao-das-catracas.md), seção 2.5).

---

## 6A. Relógio da catraca (`INT-CLK-01`, `INT-CLK-02`)

O sistema acerta o relógio da catraca **toda vez que ela conecta** (`EnviarRelogio`, no
horário de Brasília) e confere **um minuto depois**, e então a cada hora
(`ReceberRelogio`). O manual descreve as duas funções, mas não diz se a catraca guarda o
fuso, nem se é seguro acertar com ela atendendo. Por isso o acerto automático durante a
operação está **desligado** até esta tabela voltar preenchida.

1. Assim que a catraca entrar em operação (passo 2), a tela mostra `relógio acertado`.
2. Um minuto depois deve aparecer `relógio conferido (0 s)`, ou ±1 s.
3. Se o display da catraca mostra a hora, confira contra o relógio do PC.
4. Tire o cabo de rede por 30 s e ponha de volta. Depois de reconectar, deve aparecer
   `relógio acertado` de novo. **Logo em seguida**, mostre um QR: ele precisa liberar
   normalmente.

| # | O que aparece | O que significa |
|---|---|---|
| 1 | `relógio conferido (0 s)` ou ±1 s | Acerto e leitura funcionam, no horário de Brasília |
| 2 | `relógio divergente (+10800 s)` ou `(-10800 s)` | A catraca devolve em outro fuso (3 h de diferença): anote e não opere até corrigir |
| 3 | `relógio divergente (data inválida)` | Dia e mês trocados, ou ano em outro formato: anote o que o display mostra |
| 4 | `falha ao acertar o relógio (…)` | Anote o retorno. A catraca deve continuar atendendo mesmo assim |
| 5 | O QR logo depois do reacerto não libera, ou demora | Acertar com a catraca em uso **não** é seguro: o acerto automático fica desligado de vez |

| # | Resultado | Hora no display | Hora no PC |
|---|---|---|---|
| Conferência 1 min depois | | | |
| Depois de reconectar | | | |
| QR logo após o reacerto | | — | — |

---

## 6B. Gerenciar catraca pelo painel (sistema instalado)

Este passo precisa do **sistema instalado** (serviço + painel), não do modo `--bancada`: os
pedidos passam pelo serviço ([`32`](32-gerenciar-catraca.md)). Abra **Gerenciar catraca**,
escolha a catraca e digite seu nome.

| # | Faça | Deve acontecer | Anote |
|---|---|---|---|
| 1 | Mensagem "TESTE 123", 10 s | Aparece no display e some depois de ~10 s; histórico "Feito" | Tempo real que ficou |
| 2 | Mensagem "AÇÃO É ÓTIMA" | Veja como os acentos aparecem | Aparece certo? |
| 3 | Liberação manual, motivo "teste de bancada", e **gire** | Libera no sentido de entrada; histórico "liberada; girou" | Sentido certo? |
| 4 | Liberação manual, e **não gire** | Depois do tempo de acionamento: "liberada; ninguém girou" | Veio em quantos s? |
| 5 | Mostre um QR, **não gire**, tire o cabo antes do fim do tempo; ponha de volta; libere à mão e gire | A prestação de contas mostra o QR **sem giro** | Confere? |
| 6 | Acertar o relógio agora | "Feito"; passo 6A de novo dá 0 s | — |
| 7 | Refazer a conexão | A catraca some e volta a "Atendendo"; "reconectada" | Quantos s sem atender? |
| 8 | Configurações → mude a mensagem → Salvar → Aplicar agora | A mensagem nova aparece no display sem reiniciar o serviço | Quantos s sem atender? |
| 9 | **Salva × aplicada (`INT-CFG-05`, Etapa A.5).** Na base: `SELECT inner_number, config_version, config_applied_at FROM device_status;` e anote. Mude a mensagem, Salvar, **tire o cabo da catraca** e Aplicar agora; espere 1 min e consulte de novo. Ponha o cabo de volta, espere "Atendendo" e consulte de novo | Com o cabo fora: versão e momento **iguais** aos anotados (a catraca não recebeu). Com o cabo de volta: versão **nova** e momento novo, e a mensagem nova no display. As outras catracas não mudam | Algum retorno de `EnviarConfiguracoes` ≠ 0 no registro do worker com o cabo no lugar? Qual? |

---

## 6C. Chaves técnicas da configuração (Etapa A.2)

Parâmetros que hoje vão para a catraca com o **padrão da DLL**, que ninguém conhece
([`34`](34-estudo-modulo-catraca.md) §4.1). Cada um só é enviado com a sua chave técnica em
`edge_setting`, **desligada**; nenhuma tem tela. Precisa do **sistema instalado**, como o 6B (o
modo `--bancada` não lê `edge_setting`).

**Como ligar uma chave:** com o serviço parado, na base local:

```sql
INSERT INTO edge_setting (key, value, updated_at, updated_by)
VALUES ('<chave>', '1', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'), 'bancada')
ON CONFLICT (key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at,
    updated_by = excluded.updated_by;
```

Para `catraca.registrar_acesso_negado`, o valor é `0`, `1`, `2` ou `3` (vazio = desligada). Suba o serviço ou use **Configurações → Aplicar agora**. **Uma chave por vez**, e volte a
`0` (ou vazio) se o resultado for ruim. O registro do worker mostra, a cada subida, os alertas
da configuração.

| # | Chave | Ensaio | O que muda na catraca | Faça e anote | Liga de vez quando |
|---|---|---|---|---|---|
| 1 | `catraca.enviar_data_hora_no_evento` | INT-CFG-07 | `ReceberDataHoraDadosOnLine(1)` (EI-027) | Com a chave desligada e depois ligada, mostre um QR: a tentativa tem a data e a hora certas? Alguma leitura chegou com data zerada ou inválida? | ligada dá data certa em 10 leituras seguidas |
| 2 | `catraca.registrar_acesso_negado` = `0`, `1`, `2`, `3` | INT-OFF-08 | `RegistrarAcessoNegado(n)` (EI-021) | Para cada valor: mostre um QR desconhecido; depois tire o cabo (off-line) e mostre de novo. O que aparece no WebServer em Registros para cada valor? A liberação de quem é válido continua igual? | o valor que registra a negação sem mudar a liberação está anotado — e a Topdata confirmou o significado (T13) |
| 3 | `catraca.enviar_tipo_de_lista` | INT-OFF-02 | `DefinirTipoListaAcesso(0)` (EI-033): "não usar lista", explícito | Ligue, aplique e confira que QR e cartão continuam liberando como antes; anote no WebServer o que ele diz sobre a lista | nada muda na liberação (a lista na catraca é a Etapa D) |
| 4 | `catraca.enviar_wiegand_dois_leitores` | HIL-CARD-05 | `ConfigurarWiegandDoisLeitores(0, 0)` (EI-024) | Ligue e repita a tabela do passo 3 (linhas 1–7): QR na frente, cartão na frente e na urna | a tabela do passo 3 sai igual à de antes |
| 5 | `catraca.enviar_formas_de_entrada` | INT-SM-032 | o rearme do leitor passa a usar o modelo; com os valores de hoje, manda os mesmos (0, 0, 7, 0, 0) | Ligue e passe 20 leituras seguidas (QR e cartão): o leitor rearma todas as vezes? | 20 de 20 rearmam; mudar `FormaEntrada` só depois da tabela de T26 |

Não têm chave, de propósito: o **cartão master** (o número não pode morar na base em claro;
gerar e custodiar é PROPOSTA FUTURA, SEC-MASTER-01), o **WebServer** e as **mensagens de
apresentação e off-line** (a função só tem assinatura do SDK: NOVO-SEC-WEB-01, NOVO-INT-MSG-03 e
NOVO-INT-MSG-04 respondem antes). Enquanto isso, **anote o que o display mostra na 1ª linha**
quando alguém passa: se aparecer o número do cartão, é o padrão da DLL (LGPD, docs/34 §6).

---

## 6D. Sequência oficial de conexão (`INT-SM-021`, Etapa A.7)

Hoje, a cada conexão, a catraca recebe **a mesma configuração três vezes** (regime on-line, mudança
automática 0) e `EnviarConfiguracoesMudancaAutomaticaOnLineOffLine` (EI-029) nunca é chamada
([`34`](34-estudo-modulo-catraca.md) §2, F3). A sequência do manual — **cfg off-line → mudança
automática → cfg on-line** — está no código atrás da chave técnica `catraca.sequencia_oficial`,
**desligada**. Ela muda **só a ordem e a forma** do envio:

| Envio | Chamadas à DLL (docs/34 §4.3) |
|---|---|
| cfg off-line | `ConfigurarInnerOffLine` + campos comuns → `EnviarConfiguracoes` |
| mudança | `HabilitarMudancaOnLineOffLine(0, 10)` → `EnviarConfiguracoesMudancaAutomaticaOnLineOffLine` |
| cfg on-line | `ConfigurarInnerOnLine` + **os mesmos** campos comuns → `EnviarConfiguracoes` |

Os campos comuns são os de hoje (cartão, leitor, leitores 1 e 2, relés 1 e 2, teclado e as chaves
do 6C que estiverem ligadas). A mudança vai com **0: a contingência continua desligada** (decisão
D8 do docs/34 §9); a chave não faz a catraca operar sozinha sem o PC. Ficam de fora, por não terem
linha na matriz: mensagens off-line e `EnviarMensagensOffLine` (NOVO-INT-MSG-04), entradas e
mensagens da mudança (NOVO-INT-SM-022, NOVO-INT-MSG-05) e o `PingOnLine` periódico (T24).

Precisa do **sistema instalado**, como o 6C. Ligue com o mesmo SQL do 6C, chave
`catraca.sequencia_oficial`, valor `1`, e **reinicie o serviço**: a chave é do laço e vale no
próximo início do worker, não no "Aplicar agora". O registro do worker mostra `sequência oficial de
conexão ligada` e, por catraca, `cfg offline enviada`, `cfg mudança automática enviada` e
`cfg online enviada`.

| # | Faça | Deve acontecer | Anote |
|---|---|---|---|
| 1 | Suba com a chave **desligada**; repita o passo 4 (QR e cartão) | Libera e gira como sempre | — (é a referência) |
| 2 | Ligue a chave e suba de novo | Os três envios aparecem no registro, nessa ordem, e a catraca chega a "Atendendo" | Algum `falha em cfg …`? Com qual função e retorno? |
| 3 | Repita o passo 4 (QR, cartão na frente e na urna) | Igual à linha 1: o segundo `EnviarConfiguracoes` não pode ter desfeito leitor, relé ou urna | Igual? Se não, o que mudou |
| 4 | Passe 20 leituras seguidas | 20 de 20 liberam; o leitor rearma todas as vezes | Quantas? |
| 5 | Tire o cabo por 30 s e ponha de volta | A catraca **não** passa a liberar sozinha sem o PC (mudança 0); ao voltar, a sequência se repete e ela volta a atender | Liberou sem o PC? Quanto tempo sem atender? |
| 6 | Com a catraca em operação, refaça a conexão pelo painel (6B, linha 7) | Volta a "Atendendo" | Quantos s sem atender, comparado com a chave desligada |

**Liga de vez quando:** as linhas 2 a 6 saem iguais à referência, nenhum envio volta com erro, e a
Topdata respondeu T13 (qual enviador aplica `HabilitarMudancaOnLineOffLine`: se for o
`EnviarConfiguracoes`, o segundo envio a devolve ao padrão da DLL). A **contingência de verdade**
— mudança 2, tempo e `PingOnLine` — é a segunda parte do INT-SM-021 (T24: significado de 0/1/2,
unidade do tempo, período do ping; modo 2, parar o ping, cronometrar a queda, voltar), e só depois
da D8.

## 6E. Comandos novos do painel (Etapa A.8)

Comandos que o serviço já sabe executar, mas **recusa** enquanto a chave técnica de cada um
estiver desligada ([`34`](34-estudo-modulo-catraca.md) §11). Precisa do **sistema instalado**,
como o 6B. Ligue a chave como no 6C (valor `1`; **só `1` liga**), uma por vez, e volte a `0`
depois do ensaio. **Ainda não há botão:** a tela **Gerenciar catraca** mostra esses comandos
desabilitados, com o selo "Aguardando confirmação", e habilitá-los é da A.6. Até lá, o pedido
precisa de um cliente do serviço local (`EnviarComando`, com o `TipoDeComando` da coluna 3, pelo
pipe com token) — ou o ensaio espera a A.6. Não grave o pedido direto em `operator_command`: a
recusa pela chave e pela D5 é do serviço. Todo pedido fica no histórico
(`operator_command`), executa **só com a catraca livre** (Polling) e expira se ela não ficar
livre a tempo (bip 60 s; liberações 15 s).

| # | Chave | Comando | Ensaio | Função (matriz) | Faça e anote | Liga de vez quando |
|---|---|---|---|---|---|---|
| 1 | `comando.bip_curto` | `BIP_CURTO` (6) | INT-UX-03 | `AcionarBipCurto(Inner)` (EI-048) | Peça o bip com a catraca parada e depois logo após uma passagem: soa? Quanto dura? O display muda? O próximo QR é lido normalmente? | soa 5 de 5 vezes e a leitura seguinte não é afetada |
| 2 | `comando.bip_longo` | `BIP_LONGO` (7) | INT-UX-03 | `AcionarBipLongo(Inner)` (EI-049) | Igual à linha 1; anote a diferença de duração para o curto | igual à linha 1, e dá para distinguir do curto |
| 3 | `comando.liberar_saida` | `LIBERAR_SAIDA` (8), motivo obrigatório | HIL-DIR-04 (direta); HIL-DIR-05/06 (invertida) | a outra função do par do perfil: `LiberarCatracaSaida` (EI-042) com o perfil `Entrada`; `LiberarCatracaSaidaInvertida` (EI-044) com `EntradaInvertida`; `LiberarCatracaEntrada`/`EntradaInvertida` com `Saida`/`SaidaInvertida` | Peça e **gire no sentido de quem sai**; depois tente girar no de quem entra. Histórico: "liberada na saída; girou". A prestação de contas **não** conta passagem de ingresso | gira só no sentido de saída, em cada perfil usado no evento |
| 4 | `comando.liberar_dois_sentidos` | `LIBERAR_DOIS_SENTIDOS` (9), motivo e confirmação digitada `EVACUAR n` | HIL-DIR-07 | `LiberarCatracaDoisSentidos(Inner)` (EI-045) | **Não roda hoje:** o serviço recusa com "Aguardando decisão D5 do dono do produto" mesmo com a chave ligada. Quando a D5 for tomada: peça e gire nos dois sentidos; anote por quanto tempo fica livre e quantos giros passam (carona) | **D5 decidida** pelo dono do produto (docs/34 §9) **e** este ensaio aprovado |

Não está aqui, de propósito: **entrar e sair de manutenção**. A matriz não tem função da DLL
para isso (o estado existe só no programa da catraca, no PC); o que a catraca faz quando o PC
para de atendê-la é `A_CONFIRMAR_COM_TOPDATA`.

---

## 6F. Coleta de bilhetes (`HIL-BIL-01`, `INT-REC-03`, `CHAOS-REC-01`, `NOVO-INT-REC-07`, Etapa A.9)

A catraca guarda as marcações que faz sozinha (off-line) numa memória **circular de 30.000**: a mais
nova apaga a mais antiga em silêncio ([`34`](34-estudo-modulo-catraca.md) §3). `ColetarBilhete`
(EI-039) devolve uma marcação e **a tira da memória** (FUN:40). O worker grava cada uma na base
(`collected_ticket`, migração 015, só a máscara e a impressão do código) **antes** de pedir a
próxima (R-68). A coleta é só **por pedido** (comando `ColetarBilhetes`), atrás da chave técnica
`catraca.coletar_bilhetes`, **desligada**; nenhuma tela tem botão para ela.

Precisa do **sistema instalado** (o serviço entrega ao worker a chave da impressão; sem ela o worker
recusa a coleta). Ligue com o SQL do 6C, chave `catraca.coletar_bilhetes`, valor `1` — vale no
próximo pedido, sem reiniciar. Sem botão, o pedido vai direto na base (o worker confere a chave ao
receber):

```sql
INSERT INTO operator_command (id, inner_number, kind, requested_by, requested_at, expires_at)
VALUES (lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-7' ||
        substr(lower(hex(randomblob(2))), 2) || '-8' || substr(lower(hex(randomblob(2))), 2) || '-' ||
        lower(hex(randomblob(6))),
        1, 'ColetarBilhetes', 'bancada',
        strftime('%Y-%m-%dT%H:%M:%fZ', 'now'), strftime('%Y-%m-%dT%H:%M:%fZ', 'now', '+60 seconds'));
```

Confira depois com `SELECT status, result FROM operator_command ORDER BY requested_at DESC LIMIT 1;`
e `SELECT inner_number, raw_type, marked_at, code_mask, collection_seq FROM collected_ticket ORDER BY
collected_at, collection_seq;`. **Durante a coleta, aquela catraca não atende leitura** — faça fora
de qualquer passagem.

Para haver marcações, a catraca precisa ter operado **sem o PC**: com a contingência desligada (D8),
isso só acontece pela segunda parte do INT-SM-021 (6D) ou pelo que o WebServer permitir. Até lá, só
a linha 1 é possível.

| # | Ensaio | Faça | Deve acontecer | Anote |
|---|---|---|---|---|
| 1 | HIL-BIL-01 (T3) | Chave ligada, memória vazia: peça a coleta | Uma chamada; comando "Feito" com "0 bilhete(s) coletado(s)"; a catraca volta a "Atendendo" | O registro mostra `sem bilhetes` ou `erro ao coletar bilhete (…)`? Com qual retorno? (é o `RET_SEM_BILHETES`) |
| 2 | INT-REC-03 | Faça 10 passagens off-line conhecidas (anote hora e cartão de teste de cada uma); volte ao on-line; peça a coleta | 10 linhas em `collected_ticket` (ou o múltiplo de T22), tipos 10/11/12…, hora certa **ao minuto** | Quantas marcações por passagem (T22)? Tipos? Alguma data inválida (`marked_at` vazio)? |
| 3 | INT-REC-03 | Peça a coleta de novo | "0 bilhete(s) coletado(s)": a memória ficou vazia | Voltou algum? Com qual tipo? |
| 4 | CHAOS-REC-01 | Faça 30 passagens off-line; peça a coleta e, quando o registro do worker mostrar o 10º `bilhete … gravado`, **mate o worker** (Gerenciador de Tarefas → `Edge.Worker.X86` → Finalizar) ; espere o serviço subir de novo e peça a coleta | Ao final, 30 marcações na base, **nenhuma repetida** (`SELECT code_hmac, marked_at, raw_type, COUNT(*) FROM collected_ticket GROUP BY 1, 2, 3 HAVING COUNT(*) > 1;` vazio) | Total? Apareceu algum `raw_type = 128`? Quantas? |
| 5 | NOVO-INT-REC-07 (T35) | Repita a linha 4 três vezes, matando em momentos diferentes | Se aparecer `raw_type = 128` logo depois da queda, a catraca devolve o não confirmado (o que o código espera) | Total por rodada; houve rodada com 29 (bilhete perdido)? |

**Liga de vez quando:** a linha 1 diz o que volta com a memória vazia (T3); as linhas 2 e 3 contam
certo; nas linhas 4 e 5 **nenhuma** rodada perdeu ou duplicou marcação — e a Topdata respondeu T35
(quando a memória apaga: ao devolver, ou só no próximo pedido; o 128 traz a data e o código do
original?). Se a catraca apagar ao devolver, a queda exatamente entre a coleta e a gravação perde
aquela marcação (uma, nunca mais: o laço não pede a próxima antes de gravar) — e a chave só liga
com essa limitação aceita pelo dono do produto. A coleta **automática** na volta do off-line depende
da D8 e fica para depois.

## 6G. Parametrização da catraca pelo painel (`INT-PAR-01`, Etapa A.6)

Precisa do **sistema instalado**, como o 6B. Abra **Gerenciar catraca**, escolha a catraca,
digite seu nome e clique em **Parametrização desta catraca**. Faça com duas catracas ligadas: a
que você muda e uma testemunha.

| # | Faça | Deve acontecer | Anote |
|---|---|---|---|
| 1 | Abra a tela no modo guiado | Só leitor de ingressos, leitor da urna, tempo e mensagem; o leitor com o selo "Aguardando confirmação"; nenhum termo técnico | Algum texto que o operador não entenderia? |
| 2 | Mude só a mensagem ("BANCADA 1") | "O que muda" lista **uma** linha, do padrão do evento para a nova; Salvar habilita só com o nome | — |
| 3 | Salvar | "Salva, não aplicada"; a testemunha, aberta na mesma tela, não mudou | — |
| 4 | Aplicar nesta catraca… → Aplicar agora | Pede confirmação; "Aplicando" até o pedido terminar; depois "Aplicada" e a mensagem nova no display; a testemunha continua com a do evento | Quantos s sem atender? Quanto tempo até "Aplicada"? |
| 5 | Mude o tempo para 7 s, Salvar, **tire o cabo** e Aplicar | Fica "Aplicando"; depois de ~2 min o pedido falha e a tela diz "Salva, não aplicada: o último pedido falhou" — **nunca** "Aplicada" | O texto do resultado no histórico |
| 6 | Ponha o cabo de volta e espere "Atendendo" | O worker já tinha trocado a configuração no pedido que falhou: ao reconectar, envia o salvo. A tela diz "A catraca está com a configuração salva, mas o último pedido não foi concluído" (versões iguais, pedido falho); Aplicar de novo leva a "Aplicada" | Veio sozinha em quantos s? |
| 7 | Modo técnico, aba Instalação | Wiegand e rearme do leitor **desabilitados**, com o selo e o ensaio que os libera (HIL-CARD-05, INT-SM-032). Na aba Liberação as quatro funções podem ser escolhidas desde a decisão D9 (docs/34 §9), com o aviso de conferir o sentido na aba Giro (§6I) | — |

Ensaio aprovado: os passos 4 e 5 batem (aplicada só depois de concluído e com a versão igual;
falha nunca vira aplicada), e a testemunha não muda em nenhum passo.

## 6H. O programa das catracas morre com o serviço (`CHAOS-SVC-01`)

Defeito relatado pelo dono do produto em 01/10 ([docs/29](29-o-que-falta.md) §1A): depois de
desligar o simulador, o painel mostrava três catracas "Atendendo", firmware 4.2.0, sem nenhuma
catraca na rede. Eram workers simulados órfãos de uma partida anterior do serviço. Precisa do
**sistema instalado** (é o serviço do Windows que importa) e do Gerenciador de Tarefas aberto na
aba **Detalhes**. Não precisa de catraca: o ensaio é feito primeiro em modo simulação.

| # | Faça | Deve acontecer | Anote |
|---|---|---|---|
| 1 | Com o Assistente, ligue o **modo simulação** e inicie o serviço | Um `Edge.Worker.X86.exe` por grupo na aba Detalhes; o painel mostra as catracas "Atendendo", **cada cartão com o selo "Simulação"** e o aviso geral "Modo simulação" | Quantos `Edge.Worker.X86.exe`? |
| 2 | No Gerenciador de Tarefas, **finalize** o `Edge.Supervisor.exe` (Finalizar tarefa — não use "Parar o serviço") | Em até 2 s **nenhum** `Edge.Worker.X86.exe` sobra (Job Object); o painel diz "Sem resposta" no serviço | Sobrou algum? Quanto tempo levou? |
| 3 | Com o Assistente, **desligue** o modo simulação (nenhuma catraca ligada na rede) e inicie o serviço | Nenhuma catraca "Atendendo": cada uma "Aguardando a catraca conectar" ou "Conectando…", sem firmware e sem "Relógio conferido"; nenhum selo "Simulação"; "0/N online" | O texto de cada cartão |
| 4 | (Órfão da versão anterior) Com a versão **anterior** instalada, repita o passo 2 e confira que sobram workers; então instale esta versão e inicie o serviço | O registro do serviço (`registros/servico-*.log`) tem "faxina: worker órfão N encerrado"; os workers antigos somem; o painel como no passo 3 | As linhas "faxina:" |
| 5 | Abra um terminal e rode `Edge.Worker.X86.exe --bancada` da pasta instalada (sem o serviço), depois inicie o serviço | A faxina **poupa** o worker da bancada ("poupado: o processo que o criou continua de pé"); ele só para quando você fechar o terminal | — |

Ensaio aprovado: no passo 2 nenhum worker sobrevive ao serviço, e no passo 3 nenhuma catraca
aparece como "Atendendo" sem catraca na rede.

---

## 6I. Mapa de giro: conferência de sentido no comissionamento (`NOVO-HIL-DIR-11`, `NOVO-HIL-DIR-12`, D9)

Decisão D9 do dono do produto (docs/34 §9): **o que conta como entrada ou saída é do sistema**.
"Entrada" e "saída" na DLL (EI-041 a EI-044) são só o nome do lado em que o braço gira, do ponto
de vista da catraca. O mapa de giro diz, para cada origem — leitor 1 (frente e QR), leitor 2
(urna), teclado, liberação manual — qual função chamar e como o giro conta. O que continua do
mundo físico é **para que lado o braço gira com cada função nesta instalação**: isso não espera
a Topdata, é conferido aqui, uma vez por catraca e por função usada.

Precisa do sistema instalado. Use cartões de teste sintéticos (`0000000101`…). Gêmeo digital →
clique nos braços (ou na urna) → painel **Giro desta catraca**, ou Parametrização → aba **Giro**.

| # | Faça | Deve acontecer | Anote |
|---|---|---|---|
| 1 | Abra o painel sem mapa | As quatro origens em "Padrão", seta "sentido de entrada", nenhum selo de conferência na catraca | — |
| 2 | Clique na urna no 3D | O painel abre com o **leitor 2 (urna)** em destaque | — |
| 3 | Urna: desmarque Padrão, escolha **Liberar saída (EI-042)**, conte como **Entrada**, Salvar com o nome | A seta no 3D vira para o outro lado; "o que muda" com uma linha; depois de salvar, o selo **"Sentido ainda não conferido nesta instalação"** | — |
| 4 | Aplicar nesta catraca… → Aplicar agora | "Aplicando" e depois "Aplicada" (a versão do salvo inclui o mapa) | Segundos sem atender |
| 5 | Passe um cartão de teste **na urna** e gire | A catraca libera pela função escolhida; o braço gira para um lado. No painel ao vivo: **"Entrada liberada"**; o total de entradas sobe 1, o de saídas não | **Para que lado o braço girou** (olhando de frente para o display) |
| 6 | Se girou para o lado da seta: **Girou para o lado da seta**. Se não: **Girou ao contrário** | Fica registrado com o nome e a hora; o selo some (ou vira "girou ao contrário: troque a função") | — |
| 7 | Passe o mesmo tipo de cartão **na frente** (leitor 1, sem regra) | Libera por `LiberarCatracaEntrada`, como sempre; conta como entrada | Lado do braço |
| 8 | Repita 3–6 para cada função que for usar (EI-041 a EI-044), em cada catraca | Uma conferência por (catraca, função) | Tabela função × lado, por catraca |
| 9 | Na base: `SELECT counted_as, release_function, turn_complement FROM ticket_use_attempt ORDER BY rowid DESC LIMIT 2;` | `entrada`, `Saida`, e o **Complemento bruto** da origem 6 | **O Complemento mudou com o lado do giro?** (é a T14, NOVO-HIL-DIR-08) |
| 10 | (`NOVO-HIL-DIR-12`) Ligue `catraca.exibir_texto_do_giro` = `1`, reinicie o serviço, repita o passo 5 | O display mostra o texto do giro ("Entrada liberada" ou o personalizado) **antes** de liberar | O giro continua normal? Quanto tempo a mais até liberar? O texto some sozinho? |

Ensaio aprovado: em cada catraca, toda função usada no mapa tem a conferência registrada, o lado
anotado bate com a seta (ou a função foi trocada), e entradas e saídas no painel batem com o que
se passou. A chave `catraca.exibir_texto_do_giro` só é ligada se o passo 10 não atrapalhar o giro
nem a vazão (cada chamada a mais no caminho da passagem custa tempo, docs/34 §8).

## 6J. Prazo do giro (`NOVO-HIL-GIRO-04`, Etapa I.1b)

Defeito F9 ([docs/34](34-estudo-modulo-catraca.md) §2, achado do [docs/36](36-inovacao-sobre-a-catraca.md)):
depois de liberar, o laço esperava a origem 5 (fim do tempo do relé) para rearmar o leitor; se ela
não viesse, a catraca parava de atender sem erro. Agora o laço desiste sozinho depois do **tempo do
relé 1 + 3 s** (no mínimo 8 s) e rearma o leitor. **Tirar a origem 5 não é possível na bancada**:
o que se ensaia é "liberar e segurar o braço" e conferir que a pista volta a atender — e por qual
caminho. Use o modo bancada (passo 2) ou o sistema instalado com o registro do worker aberto.

| # | Faça | Deve acontecer | Anote |
|---|---|---|---|
| 1 | Tempo do relé 1 = **5 s**. Mostre um QR válido e **segure o braço** (não gire) | Em ~5 s, a linha `evento FimTempoAcionamento (5)` (caminho normal) **ou**, se ela não vier, aos 8 s `giro não confirmado: prazo de 8 s, sem origem 5 nem 6 — rearmando o leitor`; depois `leitor reabilitado` | Qual das duas linhas? Segundos entre `giro liberado` e ela |
| 2 | Logo depois, mostre outro QR válido | `LIBERADO` e `giro liberado`: a pista voltou a atender | Atendeu? |
| 3 | Repita 1 e 2 com o tempo do relé 1 = **20 s** | Nenhuma desistência antes de 23 s; a origem 5 perto dos 20 s, ou a desistência aos 23 s | Segundos; atendeu depois? |
| 4 | Repita 1 e 2 com o tempo do relé 1 = **50 s** | Nenhuma desistência antes de 53 s | Segundos; atendeu depois? |
| 5 | Tempo do relé 1 = 5 s. Libere e **gire perto do fim** do tempo (aos 4–5 s) | `GiroConfirmado (6)`, nunca `giro não confirmado`: o giro que chega dentro do prazo não é cortado | O giro foi contado? |
| 6 | Liberação manual pelo painel, sem girar | Desfecho "liberada; ninguém girou" (com a origem 5) ou "liberada; giro não confirmado: prazo de 8 s" (sem ela) | Qual desfecho |

Ensaio aprovado: em todas as linhas a catraca volta a atender a leitura seguinte, sem reiniciar
nada, e nenhum giro feito dentro do tempo do relé é cortado. Se a linha 1 mostrar a desistência
(sem origem 5), anote: é a resposta de HIL-EVT-01 para a origem 5 e muda o docs/34 §2 (F9 deixa
de ser dormente). A margem de 3 s (`DeviceStateMachine.MargemDoGiro`) se confirma aqui e em
NOVO-LOAD-LOOP-01; se o giro da linha 5 chegar depois do fim do relé + 3 s, a margem precisa crescer.

---

## 7. Encerrar e conferir

**Ctrl+C.** O sistema mostra a prestação de contas do ensaio:

```
giros confirmados: 4 · autorizados sem giro: 1
zet: consumidos 4 · com giro 3 · sem giro 1 · negados 3
bilheteria-local: consumidos 2 · com giro 2 · sem giro 0 · negados 2
códigos desconhecidos: 1 tentativa(s), 1 código(s) distinto(s)
```

**Os números precisam bater com o que você fez.** Se você passou quatro vezes e ele diz
três, alguma coisa está errada — e é exatamente isso que o ensaio existe para descobrir.

A base fica em `bancada.db`, na mesma pasta. Apague para recomeçar do zero.

---

## 8. O que este ensaio NÃO testa

Dito antes, para ninguém descobrir depois:

1. **A urna recolhendo o cartão.** O leitor da urna lê e o sistema decide, mas o comando
   que faz a urna engolir o cartão (relé 2) **não é dado**: a função certa do relé 2 não
   está documentada, e não vou adivinhar. Ele é o fluxo da Fase 3
   ([`04`](04-workflow-collect-card-then-enter.md)). **Na bancada, o cartão volta para a
   mão de quem passou.**
2. **O Zet e o sistema do Supabase.** Os ingressos vêm do arquivo, e nada sai do PC — os
   provedores do ensaio não têm conector, de propósito.
3. **Operação sem o sistema.** A mudança automática para off-line está desligada: se o
   programa parar, a catraca para de liberar.
4. **O parque inteiro sob carga com supervisor.** Os passos do worker direto aceitam
   `--inner 1,2,3`; os passos 6B–6J que usam o sistema instalado ensaiam os casos descritos,
   incluindo a testemunha de 6G, sem homologar capacidade ou estabilidade de todo o parque.

## 9. Critério de aprovação

- [ ] Passo 1: porta aberta
- [ ] Passo 3: tabela preenchida, **linha 7 igual à linha 1**
- [ ] Passo 4: QR libera, gira, e `GiroConfirmado` aparece
- [ ] Passo 4: cartão libera
- [ ] Passo 5: as seis regras se comportam como a tabela diz
- [ ] Passo 6A: relógio conferido com no máximo ±1 s, e o QR logo após o reacerto libera
- [ ] Passo 6B: as nove linhas se comportam como a tabela diz; linha 5 com o QR sem giro
- [ ] Passo 6C: cada chave ensaiada tem a linha preenchida (ou "não ensaiada") e voltou desligada se o resultado foi ruim
- [ ] Passo 6D: com a sequência oficial ligada, as linhas 2–6 iguais à referência (ou "não ensaiada"); a chave voltou desligada se o resultado foi ruim
- [ ] Passo 6E: cada comando ensaiado tem a linha preenchida (ou "não ensaiado"); a linha 4 fica "não ensaiada" até a D5; toda chave voltou a `0`
- [ ] Passo 6F: coleta de bilhetes ensaiada (ou "não ensaiada"); nenhum bilhete perdido nem duplicado depois da queda; a chave voltou a `0`
- [ ] Passo 6G: parametrização pelo painel ensaiada (INT-PAR-01): salva × aplicada bate com o display; nada que aguarda confirmação pôde ser escolhido
- [ ] Passo 6H: o serviço finalizado não deixa nenhum Edge.Worker.X86 vivo, e sem catraca nem simulação o painel nunca mostra "Atendendo" (CHAOS-SVC-01)
- [ ] Passo 6I: sentido de cada função conferido por catraca (NOVO-HIL-DIR-11); o Complemento da origem 6 anotado (T14)
- [ ] Passo 6J: prazo do giro (NOVO-HIL-GIRO-04): com o braço seguro, a pista volta a atender em todas as linhas; nenhum giro dentro do tempo do relé foi cortado
- [ ] Passo 7: o resumo bate com o que foi feito

**Preencha e envie a cópia do [relatório](21-relatorio-da-bancada.md)**, com o resultado de
cada passo, as tabelas de leitura e de tempo, o resumo do passo 7 e trechos do registro do
serviço/worker. Identifique a catraca em cada resultado; se o passo exige duas, registre
também a testemunha. Uma falha precisa apontar para a correção/teste ou para uma issue.
Passo não executado continua pendente, mesmo com CI verde. As perguntas `A_CONFIRMAR`
do docs/34 e os ensaios off-line `INT-OFF-03/04` precisam de evidência própria antes da #12.

Os números completos da tabela privada do passo 3 **não são enviados ao repositório**.
Use aliases (cartão A, QR A), máscara e quantidade de caracteres; remova códigos reais e
tokens dos trechos anexados. Não anexe `bancada.db`, backups, arquivos com credenciais nem
as DLLs proprietárias do SDK. O relatório traz o índice de evidências redigidas e as
pendências que ainda precisam de decisão ou ensaio.
