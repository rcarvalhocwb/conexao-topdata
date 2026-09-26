# ADR-0022 — Relé de webhook na nuvem, e a borda continua puxando

**Status:** Aceito · **Data:** 2026-09-25
**Complementa:** ADR-0002 (local-first), ADR-0003 (outbox)
**Fecha parcialmente:** B10

## Contexto

A tela de cadastro do Zet, no painel do produtor, resolveu metade de uma pergunta que
estava em aberto e abriu outras:

```
Deseja adicionar Webhook ao evento "Rua Iluminada Família Moletta 2025"?

Link    [ https://apizet.ruailuminada.com/webhook ]
Termos  [ NÃO ▾ ]

                                   Cancelar   Salvar
```

**O que a tela responde:**

1. O Zet **tem** integração — e é **webhook por evento**, não por conta.
2. A configuração é **um campo de link**. Só isso.

**O que a tela não oferece, e isso é o que importa:**

1. **Nenhum campo de segredo, assinatura ou chave.** Não há HMAC configurável. Logo, a
   **URL é a credencial** — quem descobrir o endereço pode injetar o que quiser na base de
   ingressos do evento.
2. **Nenhuma menção a consulta.** Webhook é empurrar. Se uma entrega se perder, não há,
   até onde se sabe, como pedir "me manda de novo o que houve entre 20h10 e 20h15".
3. **Nenhum endpoint de retorno.** Isto cobre a entrada; a volta — avisar que o ingresso
   foi usado — continua sem resposta.
4. **O campo `Termos`, hoje em `NÃO`, não tem significado conhecido.**
   `A_CONFIRMAR_COM_ZET`.

E a restrição do nosso lado não mudou: **a máquina local fica na mesma rede das catracas e
não pode ter porta aberta para a internet.** Ela não pode ser o destino do webhook.

## Decisão

**Um relé na nuvem, que guarda bytes e não interpreta nada.**

```
   Zet  ──POST──►  Relé (nuvem)  ◄──GET por cursor──  Edge (rede das catracas)
                   guarda bruto                        vem buscar, nunca recebe
                   somente inserção
```

Quatro decisões dentro dessa:

### 1. O relé não sabe o que é um ingresso

Guarda o corpo bruto, os cabeçalhos e uma sequência. Não valida, não interpreta, não
rejeita por formato. Uma mudança no payload — que vai acontecer, e sem aviso — **não pode
derrubar o recebimento**. Quem interpreta é a borda, e se ela errar, os bytes continuam
lá.

### 2. Somente inserção, com gatilhos

A tabela de entregas proíbe `UPDATE` e `DELETE`. É a prova de que o provedor entregou, com
hora — e é isso que a torna utilizável numa discussão de prestação de contas.

### 3. O webhook vira consulta por cursor

O relé transforma *push* em *pull*. Isso devolve ao sistema a única coisa que o webhook
não dá: **a capacidade de reler**. Uma queda da borda de duas horas deixa de ser uma
perda; vira um cursor atrasado.

### 4. Dois segredos distintos, ambos de 32 caracteres no mínimo

- **Entrada** — vai no caminho da URL, porque é o que o campo do painel permite. Fica
  cadastrado no provedor e sai do nosso controle.
- **Leitura** — vive só na máquina da borda.

Vazar um não entrega o outro. A comparação é em tempo constante, e o relé **recusa subir**
com segredo curto: subir com segredo fraco é pior que não subir, porque dá a impressão de
proteção.

## A regra oposta, que também é decisão

Duas situações parecidas recebem tratamento contrário, de propósito:

| Situação | Comportamento | Por quê |
|---|---|---|
| **Tradutor ausente** (formato do Zet desconhecido) | **Trava.** Não consome entrega nenhuma, cursor não anda | Se lesse sem saber traduzir, o cursor avançaria e os ingressos sumiriam sem ninguém perceber |
| **Uma entrega ilegível** | **Pula, conta e informa.** Cursor segue | Parar no primeiro payload estranho significa que nenhum ingresso posterior entra — no meio de um evento |

Nos dois casos os bytes continuam no relé. Nada é destruído; o que muda é se a fila anda.

## Consequências

- **Um componente novo para hospedar e monitorar.** O relé fora do ar não barra ninguém na
  catraca — os ingressos já ingeridos estão na base local — mas para a entrada de ingressos
  novos. Ele precisa de alerta próprio.
- **A URL do webhook precisa ser tratada como segredo.** Ela aparece em log de servidor,
  em histórico de navegador e em captura de tela. Precisa ser rotacionável, e rotacionar
  significa reeditar o cadastro no painel do Zet.
- **O relé acumula indefinidamente.** Somente inserção não tem expurgo. Para um evento é
  irrelevante; para operação contínua vai exigir política de arquivamento.
- **Sem assinatura, qualquer um que descubra a URL injeta ingressos.** O token de 32
  caracteres no caminho é a mitigação disponível, não a ideal. A pergunta a fazer ao Zet é
  se existe assinatura de payload não exposta nessa tela.
- **Se o Zet oferecer consulta por cursor**, o relé deixa de ser necessário para a
  entrada — e a implementação de `IFonteDeIngressos` passa a apontar direto para eles, sem
  nada mais mudar. O relé é uma peça substituível, não um alicerce.

## Alternativas descartadas

**Expor a borda diretamente.** Abrir porta da internet pública até a VLAN das catracas.
Descartada sem discussão.

**Túnel reverso até a borda.** Resolve a porta aberta, mas não resolve a releitura: uma
queda da borda continua perdendo entregas, porque o Zet entrega uma vez.

**O relé interpretando o payload e gravando ingressos.** Mais direto, e errado: colocaria
regra de negócio num componente exposto à internet, e uma mudança de formato derrubaria o
recebimento em vez de apenas atrasar a tradução.
