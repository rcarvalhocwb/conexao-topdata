# ADR-0024 — Worker e serviço conversam pela base local

**Status:** Aceito · **Data:** 2026-09-25

## Contexto

Até aqui só o modo bancada decidia acesso, numa janela de console. O serviço instalado
supervisionava processos, mas não sabia nada das catracas: o painel mostrava "worker
vivo", não "catraca atendendo". Para operar um evento, o serviço precisa saber, de cada
catraca, se ela está em operação, o que acabou de decidir e quanto falta enviar à nuvem.

O ADR-0004 escolheu gRPC sobre named pipes entre o painel e o serviço. Faltava decidir
como o worker x86 conta ao serviço o que está acontecendo.

## Decisão

**O worker grava na base local, e o serviço lê.** A mesma base SQLite (WAL, ADR-0003),
em `%ProgramData%\ConexaoTopdata\acesso.db`, passada pelo serviço ao worker com
`--banco`:

- cada tentativa já era gravada pelo `RepositorioDeIngressos`, na mesma transação da
  decisão; o serviço lê as novas a cada 500 ms e difunde aos painéis abertos;
- a situação de cada catraca vai para `device_status` a cada mudança e, sem mudança, a
  cada 2 s — é o batimento: sem notícia há mais de 15 s, a catraca deixa de contar como
  em operação, mesmo que a última linha diga o contrário;
- a configuração do evento fica em `edge_setting`, gravada pelo painel e lida pelo worker
  ao subir.

O serviço aplica as migrações antes de subir qualquer worker.

## Consequências

- **A catraca não depende do serviço.** Com o serviço fora, o worker segue decidindo e
  gravando; o painel só vê depois. É o mesmo princípio de "a catraca não depende da
  internet" (ADR-0023), um nível abaixo.
- Nada se perde entre o worker e o serviço: o que o painel mostra é o que está gravado.
- Latência de até meio segundo no painel ao vivo. Irrelevante para operador.
- A base passa a ter escritores em processos diferentes. WAL e `busy_timeout` cobrem;
  publicação que falhar por base ocupada é tentada na volta seguinte e **nunca** para a
  catraca (teste `Base_indisponivel_para_a_publicacao_nao_para_a_catraca`).
- A saída do worker precisa ser lida continuamente pelo serviço. Redirecionada e não
  lida, ela enchia o buffer do pipe e travava o worker — reproduzido em teste antes da
  correção.

## Alternativas recusadas

- **gRPC do worker para o serviço.** Um segundo servidor dentro do processo x86, e o
  evento se perde se o serviço estiver fora na hora. A base já é a fonte da verdade.
- **Saída padrão do worker como canal.** Sem garantia de entrega, sem estrutura, e é
  exatamente o canal que travava.
