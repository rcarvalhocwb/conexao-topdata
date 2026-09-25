# 19 — Bilheteria local, venda online, e como dividir as catracas

> **A pergunta:** cinco catracas, dois fluxos. Cartão físico RFID/NFC da bilheteria local,
> reutilizado, nunca antes de 4 a 5 minutos, em categorias — meia, inteira, solidária, e
> outras que aparecerem. E ingresso online do Zet, validado aqui e devolvido para baixa lá.
> Três catracas para a bilheteria e duas para o online, separadas? Ou todas validando tudo?
>
> **Contexto que muda a resposta:** é um evento de teste. O desenho pode mudar depois.

---

## 1. O que decide a pergunta não é preferência — é o hardware

Em teoria de filas a resposta é conhecida: **cinco atendentes numa fila só atendem mais
rápido que três numa fila e dois na outra.** Fila separada tem sempre um lado ocioso
enquanto o outro trava. Se fosse só isso, a resposta seria "todas validam tudo".

Mas há um fato de fonte primária que vem antes:

> **Na EasyInner, o tipo de leitor é configurado por equipamento, não por leitor.**

```
ConfigurarTipoLeitor(byte Tipo)          ← um tipo só. Não recebe qual leitor.
ConfigurarLeitor1(byte Operacao)         ← só o SENTIDO: entrada, saída, os dois
ConfigurarLeitor2(byte Operacao)         ← idem
```

Procurei nas 265 funções reais da DLL uma forma de pôr o leitor 1 em RFID e o leitor 2 em
QR na mesma catraca. **Não existe.** A única função de "dois leitores" é
`ConfigurarWiegandDoisLeitores`, que são dois leitores da **mesma** tecnologia.

Ou seja, pelo que o SDK expõe: **uma catraca configurada em QR Code (tipo 8) lê QR; uma
configurada em proximidade lê cartão. Não os dois.**

Isso transforma a pergunta. "Todas validam tudo" deixa de ser uma escolha de gestão e
passa a depender de uma de duas coisas que **ainda não sabemos**:

1. **O leitor físico de vocês lê as duas coisas e entrega as duas no mesmo formato.**
   Existem leitores combinados que leem QR e cartão NFC e mandam os dois como texto. Se o
   de vocês for assim, uma catraca em tipo 8 aceita os dois. `A_CONFIRMAR` — depende do
   modelo exato do leitor, que é a pergunta **B2**.
2. **O cartão da bilheteria também tem um QR impresso.** Aí todas as catracas ficam em QR
   e leem os dois — o QR do cartão da bilheteria e o do ingresso do Zet.

> Limite desta análise: o SDK diz como se configura, não o que o leitor físico faz. Pode
> haver uma combinação de hardware que resolva isso e que não aparece na DLL. A pergunta
> vai para a Topdata junto com o modelo das catracas.

---

## 2. A recomendação para este teste: **separar, 3 + 2**

Não porque separar é melhor em geral. Porque, **para um primeiro teste**, separar tem três
vantagens que valem mais que a eficiência da fila única:

### 2.1 É o que o hardware permite sem suposição

Três catracas em proximidade, duas em QR. Funciona com o que o SDK garante. Não depende de
leitor combinado nem de reimprimir cartão.

### 2.2 Isola a falha — e é isso que um teste precisa

A integração com o Zet é a parte nova e a parte com mais dependência externa: webhook,
relé, internet, formato que eles ainda não confirmaram. **Se ela falhar, as três catracas
da bilheteria continuam funcionando exatamente como antes**, sem nem perceber.

Com todas as catracas validando tudo, um problema no online aparece nas cinco — e numa
fila de evento, "a catraca não está aceitando" não diz de onde vem o problema.

### 2.3 O teste fica legível

Com os fluxos separados, cada número do relatório tem uma causa só. Tempo médio de
passagem, taxa de recusa, falha de leitura — tudo sai por fluxo, sem precisar desembaraçar
qual catraca atendeu o quê. **É isso que vai decidir o desenho do ano que vem.**

---

## 3. O risco real da separação, e como dimensionar

O custo de separar é **a fila desbalanceada**. Três catracas para a bilheteria e duas
para o online só estão certas se o público vier nessa proporção.

A conta é aritmética, não estimativa. Com 2 das 5 catracas no online, elas têm 40% da
capacidade:

| Público que vem pelo online | Cada catraca online trabalha | Cada catraca da bilheteria trabalha |
|---|---|---|
| 40% | 1× — **equilibrado** | 1× |
| 60% | **1,5×** o equilíbrio | 0,67× |
| 80% | **2×** o equilíbrio | 0,33× — quase ociosa |

**Isso é carga, não tamanho de fila.** Fila não cresce na mesma proporção que a carga:
longe do limite, 1,5× de carga quase não se nota; perto do limite, é a diferença entre
fila nenhuma e uma fila que não para de crescer enquanto a bilheteria olha para o lado.
Qual dos dois vai acontecer depende do ritmo de chegada, que é a pergunta **B7**.

**A proporção das catracas tem de seguir a proporção de vendas.** Três e dois só está
certo se ~40% vier pelo online. Se a pré-venda do Zet estiver indo bem, o certo pode ser
2 + 3, ou 1 + 4.

Duas proteções práticas:

1. **Decidir a divisão na véspera, olhando a venda online acumulada**, não agora.
2. **Ter uma catraca que troca de lado.** Se o leitor físico permitir, reconfigurar o tipo
   de leitor de uma catraca é operação de software (`EnviarConfiguracaoCompleta`) — segundos.
   Se não permitir, trocar o leitor é operação de bancada, e aí a divisão fica fixa no dia.

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

### 5.1 O intervalo de 4 a 5 minutos é uma trava antifraude

O ciclo físico honesto — passar na catraca, o cartão voltar à bilheteria, ser revendido —
leva minutos. **O mesmo cartão aparecendo de novo antes disso não é um cliente.** É o
cartão jogado por cima da grade para quem está do lado de fora.

O golpe clássico, e como o sistema o recusa:

1. A pessoa entra com o cartão.
2. Joga o cartão para fora da grade.
3. O comparsa o entrega no balcão, que o revende — **o balcão aceita**, não tem como saber.
4. O comparsa tenta entrar. **A catraca recusa:** `EmIntervaloDeReuso`.

O detalhe que faz isso funcionar: **a revenda não zera o relógio.** Se zerasse, o passo 4
passaria. O relógio só conta a partir do último giro, e só a passagem do tempo real o
libera.

### 5.2 Configurar 4 minutos, não 5

O intervalo precisa ser **menor ou igual ao ciclo honesto mais rápido.** Se vocês
configurarem 5 minutos e um cartão fizer o ciclo real em 4min30, **um cliente honesto é
barrado** — e ninguém na porta vai entender por quê.

Se o ciclo mais rápido observado é 4 minutos, configurar 4 (ou até um pouco menos). O
ensaio de bancada deve medir o ciclo real antes do evento.

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
4. **A leitura da catraca chegando aqui.** Falta o motor de decisão da Fase 2.
5. **Nada disso encostou em hardware.**

## 9. O que preciso de vocês

1. **B2 — modelo exato das catracas e dos leitores.** Decide se "todas validam tudo" é
   possível.
2. **O cartão da bilheteria tem QR impresso, ou pode ter?** É o caminho mais simples para
   juntar as catracas no ano que vem.
3. **O ciclo real mais rápido de um cartão**, medido na bancada — para configurar o
   intervalo sem barrar cliente honesto.
4. **A proporção esperada de venda online × bilheteria**, para dividir as catracas.
