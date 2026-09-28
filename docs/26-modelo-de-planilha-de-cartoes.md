# 26 — Modelo de planilha para cadastro e importação de cartões

> Decisão do usuário (28/09): "o cartão é atrelado sempre a uma pessoa ou a um tipo —
> cartão um tipo social, cartão dois tipo inteira, cartão três tipo meia —, o que facilita
> no final saber quantos foram validados por tipo, conforme os tipos cadastrados pelo
> usuário."

Arquivos do modelo, em `installer/modelos/`, instalados junto com o sistema:

| Arquivo | Para quê |
|---|---|
| `modelo-cadastro-de-cartoes.xlsx` | O modelo principal, para preencher no Excel. Tem as abas **Instruções**, **Tipos**, **Cartões** e **Exemplo** |
| `tipos-modelo.csv` | Os mesmos tipos, em CSV, para quem gera a planilha por outro sistema |
| `cartoes-modelo.csv` | Os mesmos cartões, em CSV |

A importação aceita `.xlsx` e `.csv`. O CSV usa separador `;` e UTF-8, que é o padrão do
Excel em português.

## 1. Aba "Tipos" — os tipos de entrada do evento

O usuário cadastra quantos tipos quiser. Os relatórios contam por eles, na ordem daqui.

| Coluna | Obrigatória | Regra |
|---|---|---|
| `tipo` | sim | Código curto em MAIÚSCULAS, sem espaço e sem acento: `INTEIRA`, `MEIA`, `SOCIAL`, `SOLIDARIO`. É o que vai na coluna `tipo` dos cartões |
| `nome_exibido` | sim | Como aparece na tela e no relatório: "Meia-entrada", "Solidário" |
| `ordem` | sim | Número. A posição do tipo nos relatórios |
| `ativo` | sim | `SIM` ou `NAO`. Tipo inativo não aceita cartão novo e nega o uso, mas continua nos relatórios antigos |

## 2. Aba "Cartões" — um cartão por linha

| Coluna | Obrigatória | Regra |
|---|---|---|
| `codigo` | sim | O número do cartão **exatamente como a catraca lê**. A coluna é **Texto**: zeros à esquerda fazem parte do número (`00123456789012` ≠ `123456789012`). Só letras e dígitos, sem espaço, ponto ou traço |
| `tipo` | sim | Um dos tipos da aba Tipos |
| `titular` | não | Nome da pessoa, quando o cartão é pessoal. Deixe vazio quando o cartão é só do tipo (cartão de bilheteria reutilizável) |
| `situacao` | sim | `ATIVO` ou `BLOQUEADO` |
| `validade_inicio` | não | `dd/mm/aaaa hh:mm`, horário de Brasília. Vazio = já vale |
| `validade_fim` | não | `dd/mm/aaaa hh:mm`, horário de Brasília. Vazio = vale até o fim do evento |
| `usos_maximos` | não | Número inteiro ≥ 1. Vazio = sem limite de usos (o controle fica com o intervalo de reuso e a urna) |
| `observacao` | não | Texto livre, até 200 caracteres. Não coloque CPF nem telefone |

**Não coletamos CPF, telefone nem e-mail.** O sistema de catraca não precisa desses dados
para decidir a entrada, e dado pessoal que não é coletado não vaza (LGPD, princípio da
necessidade). O nome do titular também é opcional, e só aparece na tela de consulta,
nunca em relatório nem em log.

## 3. Como a importação se comporta

1. **Prévia antes de gravar.** A tela mostra quantos cartões são novos, quantos mudam,
   quantos ficam iguais e quantas linhas têm erro, com o número da linha e o motivo. Nada
   é gravado até o operador confirmar.
2. **Tudo ou nada.** A importação confirmada grava todas as linhas válidas numa transação
   só. As linhas com erro vão para um arquivo de devolução, para corrigir e reimportar.
3. **O mesmo código outra vez = atualização**, não duplicata. Serve para trocar o tipo, o
   titular ou a situação. A troca vale dali em diante: os usos anteriores continuam com o
   tipo antigo nos relatórios (ver [`25`](25-relatorios-da-prestacao-de-contas.md), §2).
4. **Código repetido dentro do mesmo arquivo** é erro nas duas linhas: o sistema não
   adivinha qual está certa.
5. **Tipo que não existe** é erro na linha. Tipo novo se cria na aba Tipos, ou na tela de
   tipos, antes.
6. **A importação nunca apaga cartão.** Um cartão que não está na planilha continua como
   estava. Para tirar um cartão de uso, marque `BLOQUEADO`.
7. **Desfazer a última importação** volta os cartões ao estado anterior, desde que eles
   não tenham sido usados depois dela.

### Erros que a importação recusa, de propósito

| Situação | Por que recusa |
|---|---|
| Célula de `codigo` formatada como **número** no .xlsx | O Excel já pode ter tirado os zeros à esquerda, e não há como saber. Formate a coluna como Texto e cole de novo |
| `codigo` em notação científica (`1,23457E+11`) | O Excel converteu o número e perdeu dígitos |
| `codigo` com espaço, ponto, traço ou vírgula | A catraca não lê esses caracteres; provavelmente é erro de digitação |
| `codigo` com menos de 4 ou mais de 16 caracteres | É o limite documentado da catraca 4 (mesma regra do QR) |
| Data fora do formato `dd/mm/aaaa hh:mm`, ou `validade_fim` antes de `validade_inicio` | Validade ambígua não entra |

## 4. Quanto ao cadastro do ano passado

As três planilhas exportadas do sistema na nuvem (ver [`22`](22-sistema-supabase.md),
§8.5) cabem neste modelo assim:

- `codigo` ← o número do cartão, **como texto**;
- `tipo` ← a categoria (INTEIRA, MEIA, SOCIAL), que no sistema antigo estava no campo de
  nome do cliente;
- `titular` ← vazio;
- `situacao` ← ATIVO/BLOQUEADO conforme o campo de ativo.

**Atenção às duas grafias.** 31 cartões SOCIAL existem com 12 e com 14 dígitos (`00` na
frente). Até a bancada dizer quantos dígitos a catraca entrega (docs/21, linhas 8 a 12),
a importação avisa quando um código é igual a outro já cadastrado com zeros a mais ou a
menos na frente, e não junta os dois sozinha.

## 5. Os cartões de exemplo

A aba **Exemplo** e os CSVs de modelo usam números fictícios, que começam com `9999`. A
aba **Cartões** do modelo vem vazia, só com o cabeçalho, para que um exemplo nunca seja
importado por engano.
