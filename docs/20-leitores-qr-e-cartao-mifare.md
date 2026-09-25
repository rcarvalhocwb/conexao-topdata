# 20 — Qual leitor de QR comprar, e o que exigir do cartão Mifare

> **A decisão:** um leitor de QR externo em cada uma das cinco TopFit 4, e cartão Mifare
> na bilheteria. **A pergunta:** qual leitor a catraca aceita.
>
> **Sobre as fontes:** tudo o que vem da Topdata aqui foi lido em **trechos de busca** do
> portal de suporte — o domínio está bloqueado para leitura direta nesta sessão. Os números
> são da própria Topdata, mas precisam ser conferidos na bancada antes da compra em
> quantidade.

---

## 1. Antes do leitor: o limite que nenhum leitor resolve

A Topdata documenta, para a Catraca 4, a leitura de QR **"de 4 até 16 dígitos"**, com
número de dígitos variável habilitado.

**Isso vale para qualquer leitor.** O limite é da placa da catraca, não do leitor. Um QR
de 36 caracteres — um identificador UUID, que é o formato mais comum em sistema web — ou
uma URL **não passa, com o leitor mais caro do mercado.**

Por isso a primeira pergunta não é "qual leitor", é **"o que tem dentro do QR do Zet"**.
Ela foi para o topo do contrato do webhook ([`18`](18-contrato-do-webhook.md), seção 5.0),
e o sistema agora **recusa na entrada** ingresso com QR fora de 4 a 16 caracteres — dias
antes do evento, e não na porta.

---

## 2. A recomendação: o leitor da própria Topdata

**Compre o leitor de QR que a Topdata vende para a TopFit 4**, e não um leitor genérico.

O que a Topdata documenta sobre o leitor dela para a linha 4:

| Característica | O que a Topdata diz |
|---|---|
| Montagem | **Integrado na tampa** da catraca |
| Leitura | Por aproximação — QR impresso em cartão **ou na tela do celular** |
| Simbologias | QR Code Modelo 1, Modelo 2, Micro QR; **numérico e alfanumérico** |
| Código de barras | Code 128, UPC-A, EAN-13, 2 de 5 Intercalado, 3 de 9 |
| Ligação | Porta **`SERIAL_1_TTL`** da placa |
| Configuração | Tipo de leitor "QR Code" / "código de barras serial" no Gerenciador de Inners |

**Por que o da Topdata, mesmo custando mais:**

1. **É o único que se sabe que funciona.** Um leitor genérico pode falar serial TTL e ainda
   assim não funcionar: velocidade diferente, terminação de linha diferente, prefixo que a
   placa não espera, tensão diferente. Cada uma dessas diferenças é "não lê" — ou pior,
   "lê errado".
2. **Encaixa na tampa.** Leitor genérico precisa de suporte, furação e passagem de cabo.
3. **A Topdata continua dando suporte.** Com leitor de terceiro na porta serial, o primeiro
   problema de leitura vira uma discussão sobre de quem é a culpa — na semana do evento.

A diferença de preço de cinco leitores é pequena perto do custo de uma catraca que não lê
no dia.

---

## 3. Se, mesmo assim, for um leitor genérico: o que ele precisa ter

Cada item abaixo que não bater significa leitor que não lê. Os valores exatos da placa da
Topdata **não estão na documentação que consegui acessar** — por isso a lista diz o que
perguntar, e não o que responder.

| Requisito | Por quê | Valor esperado |
|---|---|---|
| **Saída serial TTL** (UART) | É o que a porta `SERIAL_1_TTL` recebe | TTL. **Não** RS-232 — a tensão de RS-232 pode danificar a placa. **Não** USB, **não** Wiegand |
| **Nível de tensão** | 3,3 V e 5 V não são intercambiáveis | `A_CONFIRMAR_COM_TOPDATA` |
| **Velocidade, bits, paridade** | Precisa ser igual à da placa | `A_CONFIRMAR_COM_TOPDATA` |
| **Terminador** (CR, LF, nenhum) | A placa precisa saber onde o código acaba | `A_CONFIRMAR_COM_TOPDATA` |
| **Sem prefixo nem identificador de simbologia** | Um prefixo vira parte do código lido — e o código deixa de bater | Prefixos desligados |
| **Leitura de tela de celular** | Muito leitor barato lê papel e falha em tela | Declarado pelo fabricante |
| **Modo de leitura automática** (por presença) | Ninguém aperta gatilho numa catraca | Modo contínuo ou por detecção |
| **Alimentação** | A placa fornece uma corrente limitada | `A_CONFIRMAR_COM_TOPDATA` |
| **Luz do sol** | Um evento na rua tem tela de celular sob sol forte | Testar ao ar livre, não na mesa |

**A última linha merece atenção separada.** Leitura de QR em tela de celular é a parte
mais frágil do fluxo online — tela com brilho baixo, tela trincada, película, sol direto.
Qualquer leitor, inclusive o da Topdata, precisa ser testado **no local e no horário** do
evento.

---

## 4. O cartão Mifare: o que exigir na compra

A Topdata documenta que, na linha 4, o leitor Mifare se configura sozinho em
**"ABA Track, 10 dígitos"**.

### 4.1 Compre cartão com identificador de 4 bytes

Esta parte é aritmética:

- Dez dígitos decimais é **exatamente** o que cabe num identificador de **4 bytes** — o
  maior valor possível é 4.294.967.295, que tem 10 dígitos.
- Há cartões Mifare com identificador de **7 bytes**. Esse número tem até 17 dígitos e
  **não cabe em 10.**

Como a TopFit 4 trata um cartão de 7 bytes — trunca, recusa, ou lê outra coisa — eu não sei
(`A_CONFIRMAR_COM_TOPDATA`). O que se sabe é que 10 dígitos não comportam o número inteiro.
Se ele for truncado, **dois cartões diferentes podem virar o mesmo número**.

**Na compra: pedir explicitamente "Mifare Classic 1K com UID de 4 bytes".** Fornecedor
costuma ter os dois.

**E o sistema não tem como perceber.** Dois cartões físicos que entregam o mesmo número
são, para o software, **o mesmo cartão**. O balcão só recusa a segunda venda se a primeira
ainda não tiver sido usada (`VendaAnteriorNaoUsada`). Se já tiver sido usada, a segunda
venda é tratada como **revenda do mesmo cartão**, e aceita — o que faz dois cartões
circularem com um número só, e o intervalo de reuso barrar um deles na catraca sem motivo
aparente.

**A única defesa é a bancada:** testar um lote de amostra do fornecedor antes de comprar
o estoque (ensaio 2 da seção 7).

### 4.2 O risco maior: o leitor do balcão e a catraca lerem números diferentes

**Este é, na minha avaliação, o risco mais caro do fluxo da bilheteria**, e ele não aparece
em nenhum teste de software.

O balcão registra a venda com o número que **o leitor do balcão** entrega. A catraca valida
com o número que **o leitor da catraca** entrega. **Se os dois não forem idênticos, toda
venda do balcão é recusada na porta como "ingresso desconhecido".**

E eles facilmente não são idênticos. Um leitor de mesa USB costuma entregar o identificador:

- em **hexadecimal** (`04A1B2C3`) em vez de decimal (`0078234567`);
- com os **bytes em outra ordem** — o mesmo cartão vira outro número;
- **sem os zeros à esquerda**.

O sistema já cobre a última: o perfil `MifareCatraca4` completa zeros até 10 dígitos. As
duas primeiras **não têm como ser corrigidas por software sem saber o que cada leitor faz.**

**O teste que resolve, e que precisa acontecer antes de comprar o leitor do balcão:**

> Pegue **um** cartão. Passe na catraca e anote o número que ela entrega. Passe no leitor
> do balcão e anote o número. **Têm de ser iguais, caractere por caractere.** Repita com
> dez cartões.

Se não forem iguais, há duas saídas: trocar o leitor do balcão por um configurável, ou
**cadastrar o estoque de cartões passando cada um na própria catraca** antes do evento, e
vender pelo número impresso no cartão.

---

## 5. O que mudou no sistema

- **Perfis de leitura com os números da Topdata** — `QrCatraca4` (4 a 16 caracteres, sem
  alterar o conteúdo) e `MifareCatraca4` (sempre 10 dígitos, completando zeros).
- **Ingresso com QR fora de 4 a 16 caracteres é recusado na entrada.** Um identificador
  UUID, que tem 36, tem teste próprio.
- **O contrato do webhook** ganhou a regra dos 4 a 16 caracteres como a primeira das regras
  não negociáveis, e o exemplo do contrato passou a ter só números — é o exemplo que o Zet
  vai copiar.

---

## 6. O que perguntar à Topdata, numa mensagem só

1. Qual é o **código de peça do leitor de QR** da TopFit 4, para compra avulsa e
   instalação nas cinco catracas que já temos?
2. Leitor de QR **de terceiro** na porta `SERIAL_1_TTL` é suportado? Se sim: **tensão,
   velocidade, formato e terminador** que a placa espera.
3. O limite de **4 a 16 dígitos** do QR aceita **letras**? Com qual tipo de leitor?
4. Como a TopFit 4 trata um cartão **Mifare de 7 bytes**?
5. Qual é a **capacidade física da urna**, em número de cartões?
6. Qual leitor de mesa USB entrega **o mesmo número** que a catraca, para o balcão?

## 7. O ensaio de bancada, antes de comprar em quantidade

| # | O que fazer | O que decide |
|---|---|---|
| 1 | Um cartão na catraca e no leitor do balcão | Se os números batem (seção 4.2) |
| 2 | Dez cartões do lote do fornecedor | Se são de 4 bytes, e se algum repete número |
| 3 | QR numérico de 16 caracteres na tela do celular | Se o limite de fato é 16 |
| 4 | QR com letras | Se letras passam |
| 5 | QR em celular ao sol, com brilho baixo | Se o leitor serve para o evento |
| 6 | Cartão na urna e QR na frente, cronometrados | Se vale uma catraca "QR expresso" ([`19`](19-bilheteria-local-e-divisao-das-catracas.md)) |
