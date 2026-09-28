# 23 — Modo simulação: o sistema inteiro sem catraca física

Para treinar a equipe e testar o sistema antes de ter catraca instalada.

## Como ligar

1. Instale com o `RayzerXAcess-Setup.exe`.
2. No **Assistente de configuração**, passo 2, marque **Modo simulação** e informe as
   catracas (número e nome), como se fossem reais.
3. **Gravar e iniciar o serviço.**

A EasyInner.dll e o SDK da Topdata **não** são necessários nesse modo.

## Como usar

No **Painel do evento**, tela **Simulador**:

1. número da catraca;
2. o código "lido" — há uma lista de códigos de teste ao lado;
3. **Na fenda da urna** para simular o cartão da bilheteria entrando pela urna;
4. **A pessoa gira se liberar** para simular a passagem (sem isso, fica "liberado sem
   giro", como quem desiste na catraca);
5. **Passar**.

O resultado aparece na hora na própria tela, no **Painel ao vivo**, em **Acessos** e na
**Prestação de contas** — e, com a nuvem configurada, sobe para o painel na nuvem como um
acesso real. O cabeçalho do painel diz **MODO SIMULAÇÃO** o tempo todo.

## O que é simulado e o que é de verdade

| Parte | No modo simulação |
|---|---|
| Catraca, leitor, giro | **Simulados** (o simulador dos testes, `src/Simulator`) |
| Decisão de acesso, base local, regras (reuso, urna, usos) | **De verdade** |
| Painel, relatórios, envio à nuvem | **De verdade** |

## Códigos de teste

Carregados pelo serviço a cada partida, de `simulacao.exemplo.json`:

| Código | O que é |
|---|---|
| `1000000001` | QR da venda online, inteira, 1 uso |
| `1000000002` | QR da venda online, meia, 1 uso |
| `1000000003` | QR da venda online, solidária, 1 uso |
| `2000000004` | QR da venda online, inteira, 2 usos |
| `0000000101` | Cartão da bilheteria, inteira — só na urna, não volta antes de 4 min |
| `0000000102` | Cartão da bilheteria, meia — idem |
| `0000000103` | Cartão da bilheteria, social — idem |

## Segurança

Fora do modo simulação, o serviço **recusa** o pedido de "passar código": ninguém libera
uma catraca física pelo painel. Voltar para catraca física é desmarcar a caixa no
assistente e gravar de novo.
