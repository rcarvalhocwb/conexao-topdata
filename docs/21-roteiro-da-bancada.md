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
| Os arquivos publicados | `installer\publicar.ps1` gera `artifacts\Edge.Worker.X86\` |
| Cartões Mifare **de teste** | Cinco ou dez, nunca de cliente |
| Um celular | Para mostrar QR Codes de teste |
| Um gerador de QR | Qualquer site ou aplicativo que gere QR a partir de um texto |

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
| `Retorno 8 (GPF)` | Ambiente: rode `installer\verificar-ambiente.ps1` e corrija o que ele apontar |
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
4. **Várias catracas com supervisor.** O ensaio roda um worker direto. Aceita
   `--inner 1,2,3`, mas o supervisor, que reinicia worker morto, não está no caminho.

## 9. Critério de aprovação

- [ ] Passo 1: porta aberta
- [ ] Passo 3: tabela preenchida, **linha 7 igual à linha 1**
- [ ] Passo 4: QR libera, gira, e `GiroConfirmado` aparece
- [ ] Passo 4: cartão libera
- [ ] Passo 5: as seis regras se comportam como a tabela diz
- [ ] Passo 7: o resumo bate com o que foi feito

**Mande a tabela do passo 3 e o resumo do passo 7.** São eles que fecham as perguntas
em aberto sobre leitor, QR e cartão.
