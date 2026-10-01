# 32 — Gerenciar catraca (fase 4)

> O que o operador pode pedir a uma catraca pelo painel, como o pedido chega a ela, o que
> fica registrado, e o que **ainda não existe** e por quê. Nada disto foi exercitado numa
> TopFit 4 de verdade: tudo roda contra o simulador. O que a bancada precisa confirmar
> está no [docs/21](21-roteiro-da-bancada.md), passos 6A e 6B.

## 1. O que existe

| Pedido | Função da DLL | Onde aparece | Observação |
|---|---|---|---|
| Acertar o relógio agora | `EnviarRelogio` (EI-008, manual 4.6.1) | Gerenciar catraca | Horário de Brasília, ano com dois dígitos |
| Mensagem no display | `EnviarMensagemTemporariaOnLine` (EI-057, manual 4.6.3) | Gerenciar catraca | Até 32 caracteres, 1 a 60 s |
| Liberação manual | `LiberarCatracaEntrada`/`…Invertida` (EI-041/043) | Gerenciar catraca | Só no sentido de entrada, com motivo obrigatório |
| Refazer a conexão | a sequência de conexão inteira | Gerenciar catraca | Reenvia a configuração completa (ADR-0020) |
| Aplicar a configuração agora | a mesma sequência, com a configuração relida | Configurações | Todas as catracas; cada uma fica alguns segundos sem atender |

Automático, sem pedido do operador (fase 4a):
- **Relógio acertado a cada conexão.** O fluxo oficial acerta na passagem para on-line.
- **Relógio conferido** 1 min depois do acerto, e depois a cada hora (`ReceberRelogio`, EI-007).
- **Divergência acima de 30 s, ou data impossível:** aviso no cartão da catraca, que continua
  "Atendendo".
- **Acerto automático durante a operação:** existe, mas fica **desligado** (chave técnica
  `relogio.acertar_ao_divergir`). Está em `A_CONFIRMAR_COM_TOPDATA` até o passo 6A da bancada
  dizer se é seguro acertar com a catraca em uso.

## 2. Como o pedido chega à catraca

```
Painel ──gRPC (pipe local + token)──► Serviço ──grava──► operator_command (base local)
                                                              │
                                    Worker da catraca ◄──lê a cada 500 ms──┘
                                    └─ executa só com a catraca em Polling (livre)
                                    └─ grava o desfecho ──► operator_command
Painel ◄── histórico ── Serviço ◄──────────────────────────────┘
```

- **Só com a catraca livre.** O laço executa o pedido em `Polling`, o estado ocioso. Com
  alguém no meio de uma passagem (validar, liberar, esperar o giro), o pedido espera. Há
  teste que reprova quando essa regra é quebrada.
- **Um pedido, uma catraca, um worker.** Só um worker consegue "pegar" cada pedido.
- **Validade:**
  - liberação manual, 15 s: se a catraca estiver ocupada por mais tempo, quem pediu
    provavelmente já foi embora, e liberar depois solta o giro para qualquer um;
  - demais pedidos, 60 s;
  - vencido, o pedido fica "Não executado a tempo", e nada acontece na catraca.
- **Refazer a conexão e aplicar:** concluem quando a catraca volta a atender, ou falham se
  ela não voltar em 2 min.
- **Base ocupada nunca para a catraca.** O desfecho que não pôde ser gravado fica guardado
  e vai na volta seguinte.

## 3. Liberação manual e a prestação de contas

O giro de uma liberação manual **não** é passagem de ingresso. Antes de liberar, o laço
encerra, sem giro, a tentativa pendente do último ingresso lido naquela catraca. Sem isso,
se a comunicação tivesse caído entre a liberação de um ingresso e o giro, o giro do
operador confirmaria a passagem daquele ingresso: a entrada de uma pessoa ficaria no nome
de outra. Há teste de ponta a ponta que reprova quando esse descarte é removido.

As liberações manuais ficam **só** em `operator_command`, com quem pediu, o motivo e se a
catraca girou. Os relatórios da fase 6 (docs/25) precisam somá-las à parte: "liberações
manuais: N (girou: M)".

## 4. Auditoria

`operator_command` (migração 009):
- **Não se apaga:** gatilho recusa `DELETE`.
- **O pedido não muda depois de gravado:** catraca, tipo, texto, motivo, quem e quando.
- **Situação final não volta atrás:** concluído, falhou e expirado.
- **Nome e motivo** ficam só na tabela. O registro em arquivo do worker diz apenas
  "comando LiberacaoManual recebido/Concluido", porque texto livre pode conter qualquer
  coisa (até um número de cartão digitado).
- **Não há login** (docs/27 §11): o nome é o que a pessoa digitou, não uma identidade
  verificada. Login e perfis de operador continuam **PROPOSTA FUTURA**; sem eles, qualquer
  pessoa com acesso ao painel pode pedir uma liberação manual. O acesso ao painel é
  protegido pelo token local do serviço (outro processo sem o token é recusado).

## 5. O que não existe, e por quê

Aparece na tela **desabilitado**, com o selo "Aguardando confirmação" e o motivo:

| Função | Por que não | O que destrava |
|---|---|---|
| Bip curto e longo | Documentado no manual (4.6.2), não ensaiado. O serviço já executa (Etapa A.8), mas recusa enquanto as chaves `comando.bip_curto` e `comando.bip_longo` estiverem desligadas | Bancada, INT-UX-03 (docs/21 §6D) |
| Relés avulsos | O que cada relé faz na TopFit 4 não está documentado | Topdata |
| Recolher cartão na urna | A função do relé 2 não está documentada (docs/21 §8) | Topdata + bancada (HIL-URNA-01) |
| Liberar nos dois sentidos / trocar o sentido | Permite carona; depende da decisão sobre evacuação. O serviço já executa os dois sentidos (Etapa A.8, motivo e confirmação digitada), mas recusa com "Aguardando decisão D5 do dono do produto", além da chave `comando.liberar_dois_sentidos` | D5 (docs/34 §9) + bancada HIL-DIR-07 |

"Liberar saída" (Etapa A.8, chave `comando.liberar_saida`, ensaio HIL-DIR-04) também existe no
serviço, recusado com a chave desligada, e ainda não aparece na tela: entra com a A.6.

## 6. Limitações conhecidas

- **A saída do "esperando o giro" depende da origem 5** (fim do tempo de acionamento).
  Se a catraca não mandar, o pedido de liberação manual é fechado em 60 s como "a catraca
  não informou giro nem fim do tempo". Isso vale para a liberação por ingresso também.
  `A_CONFIRMAR` na bancada.
- **"Aplicar agora" reconecta todas as catracas ao mesmo tempo.** Cada uma fica alguns
  segundos sem atender. A tela avisa: prefira fora do pico. Desde a Etapa A.4 (docs/35), o
  pedido vira um comando por catraca e cada uma relê o evento **e a sua própria configuração**
  (`device_config`); se a de uma catraca for recusada, só o comando dela termina como falho,
  com o motivo no histórico, e ela segue atendendo com a configuração que tinha.
- **O relógio do PC é a referência.** Se o Windows estiver com a hora errada, a catraca é
  acertada errada. O Windows precisa sincronizar a hora pela internet ou pela rede do
  evento.
- **Mensagem com acento:** o display pode não mostrar. `A_CONFIRMAR` na bancada.

## 7. Testes

- **Laço com o simulador:**
  - `RelogioDaCatracaTests` (7): acerto, conferência, limite de 30 s, acerto automático,
    falha, reconexão, nada no meio de uma passagem;
  - `ComandosDaCatracaTests` (11): liberação com e sem giro, falha, espera pela passagem,
    expiração, relógio, reconexão, aplicar, limite de 2 min, validação.
- **Ponta a ponta** (serviço + base + worker), `GerenciarCatracaTests` (8):
  - atribuição do giro;
  - aplicar para todas as catracas;
  - configuração inválida;
  - auditoria imutável;
  - dois workers disputando o mesmo pedido.
- **Telas** (`TelasTests`): a liberação só habilita com nome e motivo; "Gerenciar" no
  cartão abre a catraca certa; "Aplicar agora" exige o nome.
- **Adapter** (`AdapterTests`): horário de Brasília e ano de dois dígitos no acerto, ida e
  volta, recusa fora de 2000–2099.

**Próximo teste de hardware:** passos 6A e 6B do docs/21.
