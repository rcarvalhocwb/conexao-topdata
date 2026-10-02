# 19 — Bilheteria local, venda online, e como dividir as catracas

> **A pergunta:** cinco catracas, dois fluxos. Cartão físico RFID/NFC da bilheteria local,
> reutilizado, nunca antes de 4 a 5 minutos, em categorias — meia, inteira, solidária, e
> outras que aparecerem. E ingresso online do Zet, validado aqui e devolvido para baixa lá.
> Três catracas para a bilheteria e duas para o online, separadas? Ou todas validando tudo?
>
> **Contexto que muda a resposta:** é um evento de teste. O desenho pode mudar depois.

---

> ## ⚠ Correção de 25/09 — a conclusão da primeira versão estava errada
>
> A primeira versão deste documento afirmava: *"uma catraca lê QR ou lê cartão, não os
> dois"*, e por isso recomendava separar 3 + 2. Tirei isso só da assinatura do SDK —
> `ConfigurarTipoLeitor(byte Tipo)` recebe um tipo só — e a própria versão anterior
> avisava que *"o SDK diz como se configura, não o que o leitor físico faz"*.
>
> O leitor físico faz. Com o modelo informado — **TopFit 4** — a Topdata documenta, para a
> linha de catracas 4:
>
> > *"é possível utilizar dois leitores de tecnologias diferentes configurados juntos, como
> > código de barras e proximidade, código de barras e smart, **QR Code e proximidade, QR
> > Code e smart**"* — e isso *"no mesmo produto, com a possibilidade de biometria e
> > urna"*.
>
> **A recomendação muda.** A seção 2 é a nova.
>
> *Fonte: páginas "Leitor QR Code nas catracas" e "Especificações técnicas catraca Fit 4"
> do suporte Topdata. O domínio está bloqueado nesta sessão; o texto acima veio de trechos
> de busca, não da página lida inteira. Confirmar na bancada.*

---

## 1. O que o hardware permite, agora com o modelo certo

**TopFit 4 com urna e leitor de QR atende os dois fluxos na mesma catraca:**

| Fluxo | Onde é lido | Tecnologia | Configuração, segundo a Topdata |
|---|---|---|---|
| Cartão da bilheteria | **Fenda da urna** (leitor 2, origem 3) | Proximidade ou Mifare | Automática: *ABA Track 14 dígitos* (proximidade) ou *10 dígitos* (Mifare) |
| Ingresso do Zet | **Leitor de QR** na frente | QR Code | *"Serial Barcode"*, ligado em `SERIAL_1_TTL` |

Isso explica a aparente contradição com o SDK: o tipo de leitor configurável é o da porta
serial (onde fica o QR), e os leitores de proximidade e Mifare da linha 4 se configuram
sozinhos.

**Duas coisas que ainda não sei, e que a bancada precisa responder:**

1. **Qual valor de `TipoLeitor` o QR da TopFit 4 usa.** A Topdata diz *"Serial Barcode"*;
   o SDK tem o **5** (*barras serial*) e o **8** (*QR Code por letras*). Se for o 5 e o QR
   do Zet tiver letras, as letras podem não passar. **Isso liga direto com a pergunta 9 do
   questionário ao Zet** (o que tem dentro do QR). Ensaio `HIL-CARD-03`.
2. **Se as cinco TopFit 4 têm urna e leitor de QR.** Urna é opcional por unidade. Se só
   algumas tiverem, a divisão volta a ser decidida pelo hardware.

---

## 2. A recomendação, corrigida: **todas leem os dois; a divisão é por placa, não por hardware**

Se as cinco catracas tiverem urna e leitor de QR, **configure todas para ler os dois**, e
comece com as cinco atendendo tudo.

### 2.1 Porque o maior risco era a proporção errada

A versão anterior identificou o risco principal da separação: **não sabemos quantas pessoas
virão pelo online** (pergunta B7). Com 3 + 2 fixos em hardware, errar a proporção põe uma
fila travada ao lado de catracas ociosas. **Com todas lendo tudo, esse risco some**:
cada pessoa vai para a catraca livre, venha de onde vier.

### 2.2 Porque separar continua possível, e passa a ser reversível

Com todas lendo os dois, **separar vira questão de sinalização**: uma placa "QR Code" em
duas catracas e "Cartão" nas outras três. Se a fila do online crescer às 20h, muda-se a
placa — em segundos, sem reconfigurar nada, sem técnico.

Com cada catraca lendo uma coisa só, essa decisão fica presa ao que foi montado de manhã.

> **Ler os dois mantém todas as opções abertas. Ler um só fecha as outras.**

### 2.3 Porque o isolamento de falha se mantém

A objeção natural é: se a integração com o Zet cair, as cinco catracas são afetadas. **Não
são.** Uma falha no Zet deixa os ingressos online desconhecidos na base; o cartão da
bilheteria é validado por outro caminho, na fenda da urna, e continua passando nas cinco.
As duas coisas estão separadas no **dado**, não na catraca.

### 2.4 Porque a urna enche mais devagar

Com o cartão entrando pelas cinco urnas em vez de três, cada urna enche proporcionalmente
mais devagar. **Menos esvaziamento no pico** — e esvaziar urna no pico é exatamente o que
tira uma catraca de operação na pior hora (origem 20, urna cheia).

### 2.5 O custo real, e como medir

Há um custo, e é honesto registrá-lo: **o fluxo da urna é mais lento que o do QR.**
Inserir o cartão, a urna ler, o relé abrir a fenda, o cartão cair, a catraca liberar — é
mais demorado que mostrar o celular. Numa fila mista, quem tem QR espera atrás de quem
tem cartão.

Se na bancada o tempo da urna sair muito maior que o do QR, a solução é **dedicar uma
catraca a "QR expresso" por placa** — sem mudar nada no sistema. Medir os dois tempos é
parte do ensaio `B-08`.

### 2.6 Se as cinco não tiverem os dois

Aí vale a análise da versão anterior: a divisão é a que o hardware permitir, e a
proporção deve seguir a venda online acumulada até a véspera.

---

## 3. Dimensionar os cartões: o ciclo de 20 minutos manda

Dois números sobre o ciclo, e cada um serve para uma coisa diferente:

| Número | Vale para | Por quê |
|---|---|---|
| **Mínimo** — 4 a 5 min | **Intervalo de reuso** | Tem de ser ≤ o ciclo honesto mais rápido, ou barra cliente honesto |
| **Médio** — 20 min | **Estoque de cartões** | É quanto tempo cada cartão fica fora do balcão |

### O estoque sai de uma conta só

Cartões em circulação = **vendas por hora no pico** × **ciclo médio em horas**.

É a lei de Little, e ela não depende de nenhuma suposição sobre o evento:

| Vendas de balcão por hora, no pico | Ciclo médio | Cartões em circulação |
|---|---|---|
| 150 | 20 min (⅓ h) | **50** |
| 300 | 20 min | **100** |
| 600 | 20 min | **200** |

Isso é o **mínimo em regime**, com cartão chegando ao balcão na mesma velocidade em que
sai. O estoque real precisa de folga acima disso — para o começo do evento, quando nenhum
cartão voltou ainda, e para a variação do esvaziamento.

**A alavanca é o ciclo, não o estoque.** O ciclo de 20 minutos é quase todo tempo de
cartão parado dentro da urna esperando esvaziamento. Esvaziar com mais frequência encurta
o ciclo — e cada minuto a menos é estoque a menos.

> A capacidade física da urna (quantos cartões cabem) **não foi encontrada** na
> documentação acessível. Um trecho de busca citava *"15.000 usuários"*, mas esse é o
> tamanho da **lista de acesso** do equipamento, não da urna. Medir na bancada (`B-08`).

---

## 4. O que o software já faz: separa por dado, não por catraca

**A gestão individual e separada que vocês querem não precisa de catraca separada.** Ela
já sai do banco:

- A bilheteria local é **um provedor** no sistema, do mesmo jeito que o Zet é outro.
- Cada tentativa de passagem grava **de qual provedor** era o ingresso.
- A prestação de contas é **por provedor** e **por categoria**, independente de qual
  catraca atendeu.

Há um teste que prova isso: a **mesma catraca** valida um cartão da bilheteria e um
ingresso do Zet, e as duas contas saem separadas e corretas.

Consequência para o ano que vem: **juntar as catracas é mudança de hardware e de
configuração, não de código.** O teste deste ano não trava o desenho do próximo.

---

## 5. O cartão da bilheteria: recipiente, não ingresso

Esta é a decisão de modelo mais importante do documento.

**O cartão físico é um recipiente. O que se vende é o uso.**

```
  cartão 04A1B2C3  ── venda #1 (inteira, 18:02) ── passa 18:05 ──┐
                                                                  │ volta ao balcão
                   ── venda #2 (meia,    18:14) ── passa 18:19 ──┤
                                                                  │
                   ── venda #3 (solidária, ...) ...              ─┘
```

Uma linha por cartão, uma linha por venda. É o que permite contar **vendas** — e não
cartões — na prestação de contas: um cartão vendido três vezes são três entradas vendidas.

### 5.1 Com urna, o cartão fica — desde que só valha na urna

A urna muda o golpe clássico. O cartão **é retido na entrada**: não sai do lado de dentro
para ser jogado por cima da grade. Isso resolve a fraude principal na origem.

Mas só resolve se o cartão **só for aceito na fenda da urna**. Na TopFit 4 com os dois
leitores, o cartão de proximidade também seria lido pelo leitor da frente — e aí a pessoa
passaria **com o cartão na mão**, e a urna viraria enfeite.

Por isso o provedor da bilheteria tem a regra `SomenteNaUrna`:

| Onde o cartão foi lido | Resultado |
|---|---|
| Fenda da urna (leitor 2, origem 3) | Validado normalmente |
| Leitor da frente (leitor 1, origem 2) | **Recusado — `ForaDaUrna`.** A venda continua intacta |
| Não se sabe | **Recusado.** Não saber onde foi lido é recusar |

A recusa não queima a entrada: a mesma pessoa, na mesma catraca, põe o cartão na fenda e
passa. Há teste para isso, e a trava foi violada de propósito para confirmar que o teste
pega — sem ela, o cartão lido na frente é aceito.

### 5.2 O intervalo de reuso vira segunda linha de defesa

Com a urna retendo o cartão, o intervalo deixa de ser a trava principal e passa a cobrir o
que a urna não cobre:

- **cartão clonado** — a cópia aparece enquanto o original está na urna;
- **urna que devolve o cartão** — cartão preso, fenda que não fecha;
- **cartão vindo de outro lugar** que não o balcão.

O golpe da revenda imediata continua recusado: **a revenda não zera o relógio.** O relógio
conta a partir do último giro, e só o tempo real o libera.

**Configurar pelo mínimo, não pela média.** O ciclo médio é 20 minutos, mas o intervalo
precisa ser ≤ o ciclo honesto **mais rápido** — 4 minutos. Configurar 20 barraria todo
cartão que fizesse o ciclo em menos que a média, isto é, metade dos clientes honestos.

### 5.3 Não se revende cartão com venda paga e não usada

Se alguém tentar revender um cartão que ainda tem uma venda não usada, o sistema recusa
(`VendaAnteriorNaoUsada`). Sobrescrever apagaria o dinheiro de quem pagou — e a entrada
dessa pessoa.

### 5.4 Categorias são texto aberto

Meia, inteira, solidária hoje; cortesia, idoso, estudante amanhã. **Tipo novo de entrada
criado na véspera não exige versão nova do sistema.** Há teste com uma categoria que
nenhum código conhece.

### 5.5 A categoria é fotografada em cada uso

Como o cartão é revendido, a categoria dele muda. Se o relatório lesse a categoria **atual**
do cartão, a meia-entrada vendida às 18h viraria a inteira vendida às 18h10, e a prestação
de contas por tipo mentiria. Cada passagem guarda a categoria **daquela** venda.

---

## 6. A prestação de contas que sai daqui

Por provedor, e dentro de cada um, por categoria:

```
BILHETERIA LOCAL                 vendidas   entraram   saldo
  inteira                           412        405        7
  meia                              288        286        2
  solidaria                          96         96        0
  ─────────────────────────────────────────────────────────
                                    796        787        9

ZET (online)                     vendidas   entraram   saldo
  inteira                           ...
```

O **saldo** é quem pagou e não entrou, ou cartão que ainda está na mão de alguém. Somado
às recusas por motivo — inclusive quantas vezes o intervalo de reuso pegou um cartão cedo
demais —, é a prova de que o sistema funcionou, com o giro da catraca como testemunha.

> Os números acima são ilustrativos da forma do relatório, não uma previsão.

---

## 7. O que está construído

- Bilheteria local como provedor, com intervalo de reuso e cartão reutilizável
- Venda de balcão: primeira venda cria o cartão, revenda recarrega, venda paga não usada
  é protegida, cartão bloqueado não é revendido
- A catraca recusando o cartão que volta cedo demais, **mesmo depois de revendido**
- Categoria aberta, fotografada em cada uso
- Prestação de contas por categoria e, na bilheteria, contando vendas e não cartões
- Bilheteria local sem aviso de saída — não há ninguém do outro lado para dar baixa
- Contrato do webhook com categoria ([`18`](18-contrato-do-webhook.md))
- **Cartão da bilheteria só na fenda da urna** (`SomenteNaUrna`, migração 005), com a
  recusa no leitor da frente sem queimar a entrada, e leitor desconhecido tratado como
  recusa
- A mesma catraca atendendo cartão pela urna e QR pela frente, com as contas separadas

**Um bug que quase entrou, e foi pego:** a chave de idempotência do aviso de uso era
`uso:{cartão}:{número do uso}`. Num cartão revendido o contador volta a zero, então o
primeiro uso da segunda venda repetia a chave da primeira — e o aviso seria descartado em
silêncio. Corrigido antes de existir; há teste que reprova se voltar.

## 8. O que não existe

1. **Tela de venda de balcão.** `VenderNoBalcao` é uma operação do sistema, testada; não
   há tela para o operador usar.
2. **Tela do relatório.** O relatório é dado, não PDF nem painel.
3. **Bloquear cartão.** O sistema respeita cartão bloqueado, mas não há operação para
   bloquear — hoje, só direto no banco.
4. **A leitura da catraca chegando aqui.** Falta o motor de decisão da Fase 2 — e com ele
   o fluxo físico da urna (abrir a fenda com o relé 2, confirmar o recolhimento pela
   origem 7, liberar o giro), desenhado em [`04`](04-workflow-collect-card-then-enter.md)
   e ainda não construído.
5. **Nada disso encostou em hardware.**

## 9. O que já foi respondido, e o que falta

**Respondido em 25/09:**

- **Modelo:** TopFit 4 — fecha parte de B2
- **Cartão:** padrão para urna coletora
- **Ciclo médio:** 20 minutos
- **Payload do Zet:** solicitado

**O que falta, em ordem de impacto:**

1. **As cinco TopFit 4 têm urna e leitor de QR?** Decide se "todas leem os dois" é
   possível. É a única pergunta que muda a recomendação.
2. **O cartão é de proximidade ou Mifare?** Muda o tamanho do código que a catraca entrega
   — 14 ou 10 dígitos, segundo a Topdata — e o perfil de normalização do cartão.
3. **Na bancada:** qual `TipoLeitor` o QR da TopFit 4 aceita (5 ou 8), e se letras passam.
4. **Na bancada:** o tempo de passagem pela urna e pelo QR, medidos — para saber se vale
   uma catraca de "QR expresso".
5. **Na bancada:** quantos cartões cabem na urna.
6. **Vendas de balcão por hora no pico** — para dimensionar o estoque de cartões pela
   tabela da seção 3.
