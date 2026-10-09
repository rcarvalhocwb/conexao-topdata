# 25 — Relatórios da prestação de contas: o que não pode faltar

> Pedido do usuário (28/09): "a prestação de contas do que é feito com as passagens deve
> ser feito um estudo para saber quais os relatórios não podem faltar". Exemplo dado:
> "acesso hoje 300 meias entradas, 100 inteiras, 50 solidários, conforme os tipos
> cadastrados pelo usuário".
>
> Evento no fuso de Brasília (`America/Sao_Paulo`). Sistema desenvolvido sob demanda por
> **Rayzer Serviços e Tecnologia**.

## 1. Para quem a prestação de contas serve

| Quem lê | O que precisa provar ou decidir |
|---|---|
| Organização do evento | Quantas pessoas entraram, de que tipo, em que dia e hora; se a operação funcionou |
| Patrocinadores e apoiadores | Público atingido por dia e no total; pico de público |
| Poder público (prefeitura, cessão de rua, segurança) | Público por dia; horários de pico; ocorrências |
| Parceiros de ingresso (bilheteria, venda online) | Quantos ingressos de cada um foram usados, e quais não |
| Programas sociais (entrada social/solidária) | Quantas entradas sociais e solidárias foram atendidas |
| Contador e jurídico | Números que batem com a venda, meia-entrada separada, rastro de auditoria |

A catraca mede **uso**, não **venda**. Tudo o que é financeiro (valor arrecadado, repasse,
imposto) vem do sistema de venda. O que este sistema entrega é a prova de que o uso
aconteceu, de que tipo e quando, e a conciliação com o que foi vendido ou cadastrado.

## 2. Três definições que precisam estar escritas em todo relatório

1. **Acesso validado** = a catraca **liberou e girou** (passagem confirmada). Liberou e a
   pessoa não passou (tempo esgotado) conta à parte, como "liberado sem passagem". É a
   diferença entre contar gente e contar leituras.
2. **Tipo** = o tipo que o cartão tinha **no momento do uso**. A migração 004 já grava o
   tipo em cada tentativa: se o cartão for recadastrado de meia para inteira à noite, as
   meias da tarde continuam meias.
3. **Dia de operação** = de uma hora de corte até a mesma hora do dia seguinte, em horário
   de Brasília (padrão **06:00**, configurável). Evento noturno que passa da meia-noite não
   pode dividir uma noite em dois dias.

## 3. Os relatórios que não podem faltar

### R1 — Boletim do dia (o do exemplo)

É o relatório principal. Um por dia de operação.

- Acessos validados **por tipo**, na ordem dos tipos cadastrados: "MEIA 300 · INTEIRA 100 ·
  SOLIDÁRIO 50 · SOCIAL …", mais o total e a participação de cada tipo em %.
- Liberados sem passagem, por tipo.
- Negados por motivo: cartão desconhecido, bloqueado, fora da validade, esperando o
  intervalo de reuso, usos esgotados, fora da urna.
- Pessoas distintas (cartões distintos validados) × passagens: mostra as reentradas.
- Primeiro e último acesso do dia, e a hora de pico.
- Mostrado ao vivo na tela durante o dia e fechado no corte.

### R2 — Fluxo por hora

- Validados por hora (ou a cada 15 min), por tipo e no total, com a curva do dia.
- Serve para escalar equipe e para o poder público (horários de pico).

### R3 — Por catraca (portão)

- Validados, liberados sem passagem e negados, por catraca e por hora.
- Tempo em que cada catraca ficou fora do ar no dia (disponibilidade).
- Mostra a catraca sobrecarregada, o leitor com problema e a catraca parada.

### R4 — Por origem do ingresso

- Validados por provedor: bilheteria local (cartão), venda online (QR), cortesias, e o que
  mais houver.
- É o número que cada parceiro confere.

### R5 — Conciliação (cadastrado ou vendido × usado)

- Por tipo: cartões cadastrados e ativos, validados pelo menos uma vez, nunca usados.
- Online: ingressos recebidos, usados, não usados e cancelados; usos ainda não avisados ao
  provedor.
- Cartões usados mais vezes do que o normal no dia (ver R6).

### R6 — Ocorrências e segurança

- Tentativas negadas repetidas do mesmo código em pouco tempo, com o código **mascarado**:
  suspeita de cartão emprestado, cópia ou leitor com defeito.
- Códigos desconhecidos mais tentados (mascarados): pode ser um lote que não foi cadastrado.
- Liberações manuais: quem liberou, quando, em que catraca e por quê.
- Comandos enviados às catracas, cadastros e importações de cartões, e mudanças de
  configuração: quem fez, quando e o quê.

### R7 — Consolidado do evento

- R1 de todos os dias lado a lado: dia × tipo, total por tipo, total geral, média por dia,
  maior dia e maior hora.
- É o relatório final para patrocinadores e poder público.

### R8 — Saúde da operação

- Quedas de catraca, do programa das catracas e do serviço, com duração.
- Tempo sem sincronizar com a nuvem e tamanho máximo da fila de envio.
- Alertas do dia e quando o operador os marcou como "ciente".

## 4. O que todo relatório precisa ter

- **Cabeçalho**:
  - nome do evento;
  - período (dia de operação ou intervalo de data e hora);
  - fuso "Horário de Brasília";
  - gerado em;
  - quem gerou;
  - versão do sistema;
  - "Rayzer Serviços e Tecnologia".
- **Corte fechado.** Um relatório "fechado" congela os números e recebe um código de
  conferência (SHA-256 dos dados). Gerar de novo com o mesmo período tem que dar o mesmo
  código. É isso que torna o número defensável depois.
- **Exportação.** PDF para entregar, e CSV (separador `;`, UTF-8) para o contador conferir.
- **Nunca** o número completo de cartão nem o nome do titular. Os relatórios são
  agregados, e o código aparece sempre mascarado.
- Página numerada ("1 de 3") e campo para assinatura do responsável no PDF do R1 e do R7.

## 5. O que isso muda no sistema

| Item | Hoje | Precisa |
|---|---|---|
| Tipos | Texto livre em cada cartão | Cadastro de tipos (nome, ordem, cor, ativo), e o cartão aponta para um tipo — ver [`26`](26-modelo-de-planilha-de-cartoes.md) |
| Dia de operação | Não existe | Hora de corte na configuração; todos os relatórios por dia a usam |
| Fuso | O do Windows | O do evento (`America/Sao_Paulo`) na configuração; a tela avisa se o Windows estiver em outro |
| Validado × liberado | Já distinguidos na base (giro confirmado) | Mostrar os dois em todo relatório |
| Pessoas distintas | Não calculado | Contagem de cartões distintos validados |
| Liberação manual e comandos | Não existem | Auditoria obrigatória (R6) |
| Corte fechado com hash | Existe corte reproduzível no domínio | Levar à tela, ao PDF e ao CSV |
| Exportação PDF/CSV | Não existe | Implementar (fase 6 do prompt do docs/24) |

## 6. A confirmar

- **Meia-entrada.** A Lei 12.933/2013 e o Decreto 8.537/2015 tratam da meia-entrada e da
  cota destinada a ela. O R1 separa as meias validadas, mas a cota se mede sobre a
  **venda**, que é do sistema de venda. Se o evento está sujeito à regra e que número
  precisa ser entregue são perguntas para o contador ou o jurídico do evento.
  `A_CONFIRMAR`.
- **Público simultâneo (lotação).** Só é possível se houver contagem de saída. A TopFit 4
  distingue o sentido do giro? `A_CONFIRMAR_COM_TOPDATA`, a verificar em teste de bancada.
  Sem isso, o sistema informa entradas, não lotação.
- **Hora de corte** do dia de operação: o padrão proposto é 06:00, e a organização
  confirma.
