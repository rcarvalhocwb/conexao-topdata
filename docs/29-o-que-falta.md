# 29 — O que falta (varredura de 29/09/2026)

> Cruzamento de todo o histórico do PR #1 (67 commits) com os docs 00–28, os ADRs, os
> runbooks e o código. Onde um doc antigo diz "não existe" e o código já tem, vale o código
> (lista na §6). Nada foi testado em catraca real até hoje.

## 1. Onde estamos

**Pronto e verificado no CI (sem hardware):**
- serviço Windows com workers x86 isolados, fila local SQLite, reenvio sem duplicar (testado com `kill -9`);
- adapter real da `EasyInner.dll`, montado a partir do manual 6.0.2.0 e nunca executado contra uma catraca;
- decisão de ingresso, com urna, intervalo de reuso e vários provedores;
- sincronização de cartões com o painel na nuvem;
- modo simulação;
- painel e assistente com o Rayzer Design System;
- gerenciar catraca (fase 4): relógio acertado e conferido, liberação manual com motivo, mensagem no display, refazer a conexão, aplicar agora — tudo auditado ([docs/32](32-gerenciar-catraca.md));
- gêmeo digital da TopFit 4: a catraca em 3D, fichas das peças, cenários de demonstração e espelho ao vivo — ainda não visto numa tela Windows ([docs/33](33-gemeo-digital.md));
- **mapa de giro** (decisão D9, 01/10): por catraca e por origem (leitor 1, urna, teclado, liberação manual), qual função libera o braço e se o giro conta como entrada ou saída, no gêmeo (clique nos braços ou na urna) e na aba Giro da Parametrização; totais e prestação de contas pelo rótulo; falta a conferência de sentido na bancada ([docs/21](21-roteiro-da-bancada.md) §6I, [docs/32](32-gerenciar-catraca.md) §5A);
- MSI e Setup em português;
- prestação de contas atual: resumo do período por categoria, catraca, hora e motivo, e o CSV dele (os relatórios R1–R8 ainda não existem; ver §1A);
- pacote web `rayzer-ui`.

642 testes .NET e 24 da web (29/09, depois da fase 4).

**Pronto, mas nunca exercitado:** o caminho completo da catraca (leitura → decisão → liberação → giro), o recolhimento pela urna e a liberação por sentido.

**Estudo do módulo catraca (30/09):** o [docs/34](34-estudo-modulo-catraca.md) cataloga as 265
funções da DLL, propõe o modelo completo de parametrização, o cadastro e a importação de
cartões e o gêmeo acompanhando cada função. Ele também acha **oito defeitos no código atual**,
quatro deles já ativos: dígitos variáveis nunca enviados, sequência oficial de conexão não
seguida, retorno de erro tratado como "sem eventos" e mensagem enviada no meio da montagem. O
prompt de implementação, em etapas, está no [docs/35](35-prompt-modulo-catraca.md). As decisões
do dono (D1–D8) e as 35 perguntas para a Topdata (T1–T35) estão no docs/34 §9 e §10.
A situação de cada etapa fica no docs/34 §11: a Etapa 0 está concluída no código, e da Etapa A
a A.1 (montador único da configuração da catraca, com o teste da ADR-0020 item 3) e a A.2 (modelo
completo da configuração, com cinco parâmetros novos atrás de chaves técnicas desligadas — ensaios
no docs/21 §6C) e a A.3 (configuração por catraca na base: migração 012, com histórico só-INSERT,
e a camada da catraca no montador) e a A.4 (o worker aplica por catraca: cada uma sobe com a sua
configuração, o "Aplicar agora" relê só a catraca do comando, e configuração recusada numa catraca
cai no padrão dela na subida ou faz o comando falhar, sem derrubar as outras) e a A.5 (salva ×
aplicada: cada catraca publica a versão — SHA-256 canônico, sem o número do cartão master — e o
momento da configuração que ela **aceitou**, só depois do envio com retorno 0; migração 013 e
dois campos novos no `Equipamento` do contrato) também; falta o ensaio INT-CFG-05 (docs/21 §6B,
passo 9). A A.7 (a sequência oficial de conexão — cfg off-line → mudança automática, EI-029 →
cfg on-line) está no código atrás da chave técnica `catraca.sequencia_oficial`, **desligada** até
INT-SM-021 (docs/21 §6D): desligada, a catraca recebe os três envios iguais de sempre; ligada, não
liga a contingência (mudança automática 0; D8). A A.6 (tela **Parametrização da catraca**, aberta
pela Gerenciar catraca: abas, modo guiado × técnico, "o que muda (atual → novo)", salvar e aplicar com
nome e confirmação, "aplicada" só quando o pedido terminou e a versão aceita é a do salvo; o que aguarda
confirmação aparece desabilitado com o selo) também está no código; falta o ensaio INT-PAR-01
(docs/21 §6G). A A.8 também está no código: bip curto e longo, liberar saída e liberar nos dois
sentidos, cada um com a sua chave técnica desligada (o serviço recusa antes de enfileirar; ensaios
no docs/21 §6E). Os dois sentidos são recusados também pela **decisão D5, ainda não tomada** —
ligar exige D5 + bancada (HIL-DIR-07). Entrar e sair de manutenção ficaram de fora (sem função na
matriz; `A_CONFIRMAR_COM_TOPDATA`). A A.9 (coleta de bilhetes real: cada marcação da
memória da catraca gravada na base — migração 015, só máscara e impressão HMAC — **antes** de pedir a
próxima, R-68; deduplicação pelo conteúdo e pelo tipo 128) está no código, só por comando manual e
atrás da chave técnica `catraca.coletar_bilhetes`, **desligada** até INT-REC-03, CHAOS-REC-01 e a
resposta de T35 (docs/21 §6F); a coleta automática na volta do off-line depende da D8. Da Etapa B, a D1 foi decidida (ADR-0025) e B.1–B.3 estão no código: migração 011
(tipos, trilha do cadastro, lotes de importação), tipo desativado negando na catraca e a prévia da
importação (CSV e .xlsx, sem gravar).

**Inovação sobre a catraca (02/10):** o [docs/36](36-inovacao-sobre-a-catraca.md) achou um defeito
dormente, o **F9** (docs/34 §2): o prazo de cada estado da máquina estava na tabela e nada o
aplicava — se a catraca não mandasse a origem 5 depois de uma liberação, a pista parava de atender
sem erro (no modo simulação, uma leitura liberada sem giro já fazia isso). A **Etapa I.1b** (C1) o
corrigiu no código, sem chave: o laço desiste do giro depois do tempo do relé 1 + 3 s (no mínimo os
8 s da tabela), só depois de uma espera sem evento, e volta a atender pelo rearme do leitor, como a
origem 5; a tentativa termina sem giro e a liberação manual diz "giro não confirmado: prazo de N s".
A coleta de bilhetes para em 10 min; os estados de configuração só caem pelo prazo quando o passo
está impedido; a validação do acesso nunca (o destino da tabela seria liberar). Falta a bancada:
NOVO-HIL-GIRO-04 (docs/21 §6J).

## 1A. Defeitos relatados pelo dono do produto (01/10) e o que foi feito

### "Desliguei o simulador e o sistema ainda reconhecia como atendendo"

**Causa confirmada.** O `Kill(entireProcessTree)` dos workers só rodava na parada limpa do
serviço (`WorkerSupervisor.Encerrar`, em `ApplicationStopping`). Com o serviço morto de outro
jeito (Gerenciador de Tarefas, queda, terminal fechado), os workers x86 — filhos, mas não
presos ao serviço — ficavam vivos. Os simulados seguiam gravando `device_status` com
`Polling`, `online = 1`, firmware 4.2.0 (o `FirmwareInfo(16,1,4,2,0)` do `SimulatedDevice`) e
relógio conferido de hora em hora. O serviço seguinte, já em modo real, acreditava em qualquer
linha com menos de 15 s (`NoticiaVelha`), sem saber quem a gravou: três catracas "Atendendo",
sem o chip "Modo simulação" (que é do serviço, e o serviço estava em modo real). Os mesmos 15 s
de crença também apareciam ao alternar o modo simulação com reinício limpo. O worker órfão não
segura a porta TCP no modo simulação (o `InnerSimulator` não abre socket); um órfão **real**
seguraria a porta 3570 e o worker novo não subiria.

**O que mudou (defesa em profundidade, sem tocar no caminho do giro):**
- **Job Object** (`ContencaoDosWorkers`, kernel32): o kernel mata os workers quando o serviço
  some, de qualquer jeito. Recusado pelo Windows, o motivo vai ao diagnóstico do worker.
- **Vigia do pai** no worker (`--pai`, `VigiaDoProcessoPai`): sem o serviço, encerra limpo; à
  força em 10 s se a DLL o prender. À prova de PID reaproveitado.
- **Faxina na partida** (`FaxinaDeOrfaos`): encerra só workers desta instalação (caminho
  completo) cujo pai morreu; pai vivo (outra instância, bancada) é poupado. Encerra também os
  órfãos da versão anterior, que não têm a vigia.
- **Situação só vale da partida atual** (migração 016, `--sessao`): o serviço ignora linha de
  outra partida ou sem partida; a catraca aparece "Aguardando a catraca conectar".
- **Selo "Simulação" por catraca** (`Equipamento.simulacao`), coerente com o aviso geral.
- Só catracas cadastradas contam em "N/M online".

Provado por teste em qualquer sistema: `SessaoDoServicoTests`, `WorkerMorreComOServicoTests`
(contenção e regra da faxina por abstração) e `VigiaDoProcessoPaiTests`. **Só no Windows**
(rodam no CI Windows, voltam sem afirmar nada no Linux): o Job Object matando o worker e a
faxina achando um órfão de verdade. Ensaio para o dono: docs/21 §6H (`CHAOS-SVC-01`).

**Limitação conhecida:** a versão anterior do worker não conhece as colunas da 016 e, ao
regravar a linha, deixa a partida que estava lá. Se um worker órfão da versão anterior
sobreviver à faxina (Windows recusou encerrá-lo) **e** o worker novo gravar a mesma catraca, a
linha alterna entre os dois a cada 2 s com a partida do novo. A faxina registra a recusa
("não pôde ser encerrado") no `registros/servico-*.log`; nesse caso, reinicie o PC antes do evento.

### "Os relatórios não estão funcionais"

**O que existe:** a tela **Prestação de contas**, com o resumo do período (autorizados, com giro,
negados; por categoria, por catraca, por hora e por motivo de negativa) e o CSV dele.

**O que estava quebrado e foi corrigido (com teste que falhava antes):**
- "De" vazio não mandava o início e o serviço contava **só as últimas 24 h**, sem dizer. Agora
  vazio = desde o começo, como em Acessos, e o período vai sempre explícito.
- Nem a tela nem o CSV diziam **que período** os números cobriam; mudar o filtro sem gerar de
  novo exportava outro período sem aviso. O serviço devolve o período aplicado
  (`periodo_desde`/`periodo_ate`), mostrado na tela e na linha "Período" do CSV.
- Exportar para um arquivo **aberto no Excel** (ou pasta que sumiu) estourava como "erro
  inesperado"; agora vira mensagem. "Exportar" fica desabilitado antes de gerar.
- O nome do arquivo usava o fuso do Windows; agora a hora de Brasília.

**O que nunca existiu** (fase 6, abaixo): R1–R8 do docs/25, PDF, corte fechado com código de
conferência, dia de operação com hora de corte (E9), pessoas distintas, liberações manuais nos
números. A tela agora os lista em "Ainda não disponível", desabilitados, com o selo e o motivo.

## 2. Depende de outras pessoas (bloqueia ou muda o rumo)

| # | Pergunta | Quem responde | O que destrava |
|---|---|---|---|
| E1 | Redistribuir a `EasyInner.dll` no Setup | Topdata | Setup que já funciona com catraca real, sem instalar o SDK à parte |
| E2 | B2 — parque real: quantas TopFit 4, firmware, se todas têm urna **e** leitor de QR | cliente | divisão das catracas (docs/19) e perfil do cartão |
| E3 | B4 / D5 — fail-safe × fail-secure (o que a catraca faz sem PC ou sem energia) e evacuação | responsável pela segurança do evento | configuração de contingência, RB-10 (evacuação) e o comando "Liberar nos dois sentidos", que já existe no serviço e é recusado até a decisão (A.8) |
| E4 | B7 — público, janela de pico e número de portões | cliente | dimensionamento (docs/14): pode faltar catraca |
| E5 | B8 — cartão de proximidade ou Mifare; 10 ou 14 dígitos | cliente + bancada | normalização do código (zeros à esquerda) |
| E6 | B9 — biometria/facial no escopo? | negócio + jurídico | fase 5 (só com base legal) |
| E7 | Como o comprador on-line entra: caminho A, B ou C do [docs/30](30-integracao-zet-analise.md) §4 | você + Zet | ligar a bilheteria on-line à lista local |
| E8 | ~~Payload do Zet~~ **respondido em 29/09** — `TradutorDaZet` pronto (docs/30). Falta: ingressos da maquininha e cortesias, que nunca chegam por webhook (Z1) | Zet | 5,8% dos pedidos hoje ficariam de fora da catraca |
| E9 | Hora de corte do dia de operação (proposta: 06:00) | organização | relatórios por dia (fase 6) |
| E10 | Meia-entrada: o evento está sujeito à cota? Que número entregar? | contador/jurídico | conteúdo do R1 (docs/25 §6) |
| E11 | Renomear o repositório `conexao-topdata` | você | só organização |
| E12 | Prints do sistema de nota fiscal (referência visual pedida no início) | você | já superado pelo brand board, confirmar se ainda vale |

## 2A. Urgente na nuvem (segurança e LGPD) — docs/22 §9.2

Pela descrição recebida em 29/09, `authorizations` (com o nome do titular e possivelmente
o CPF) pode ser lida, e `access_events` pode ser gravada, com a chave pública `anon`.
Qualquer pessoa leria os titulares e forjaria acessos. A correção é do dono do painel:
1. tirar as políticas de `anon`;
2. servir a catraca só pelas funções, com segredo por equipamento;
3. se precisar de leitura direta, usar uma view sem dado pessoal.

O documento para enviar à equipe do painel é o [docs/31](31-contrato-da-nuvem.md).

## 3. Depende da bancada (uma TopFit 4 + PC + SDK)

Roteiro pronto em [`21`](21-roteiro-da-bancada.md); pacote da bancada publicado pelo CI.

1. **HIL-STACK-01 — antes de tudo:** o worker .NET 10 x86 carrega a `EasyInner.dll`? Se não carregar, o worker volta para .NET Framework 4.8 atrás do mesmo IPC (ADR-0016).
2. **O maior risco físico:** o painel guarda cartões de 12 e 14 dígitos, e um leitor Mifare "ABA 10 dígitos" entrega no máximo 10 (docs/22 §9.4). Tabela de leituras 1–12 do docs/21: origem, código e número de caracteres que a catraca entrega. As linhas 8–12 decidem os zeros à esquerda dos cartões reais.
3. Qual `TipoLeitor` o QR aceita (5 ou 8) e se passam letras e 20 caracteres.
4. Recolhimento pela urna: relé 2 → origem 7 → liberar → origem 6; qual variante de `LiberarCatracaEntrada*`; capacidade da urna.
5. A TopFit 4 distingue o sentido do giro? Sem isso não há lotação, só entradas (docs/25 §6). Desde a D9 o sistema **classifica** cada giro pela função de liberação usada e pelo mapa de giro (entrada ou saída); o que falta na bancada é a **conferência de sentido** (NOVO-HIL-DIR-11, docs/21 §6I: para que lado o braço gira com cada função, por catraca), o `Complemento` da origem 6 (T14, gravado bruto em `turn_complement`) e o texto do giro no display (NOVO-HIL-DIR-12, chave `catraca.exibir_texto_do_giro`, desligada).
6. Tempos medidos: urna × QR por pessoa, latência p95.
7. Relógio: `EnviarRelogio`/`ReceberRelogio`, semântica e formato. É pré-requisito da fase 4.
8. Mensagens no visor, bip e relés: o que cada função faz de fato (`A_CONFIRMAR_COM_TOPDATA`).
9. Leitor externo de QR: tensão, velocidade, terminador e alimentação (docs/20).
10. Ensaios de caos com hardware: CHAOS-DEV-01 (isolamento entre grupos), CHAOS-REC-01 (bilhetes sem duplicar; roteiro no docs/21 §6F).
11. **INT-SM-021 — sequência oficial de conexão (Etapa A.7):** com a chave `catraca.sequencia_oficial` ligada, a catraca entra em operação e libera igual a hoje? `EnviarConfiguracoesMudancaAutomaticaOnLineOffLine` devolve 0? O segundo `EnviarConfiguracoes` desfaz o que a mudança mandou (T13)? Roteiro no docs/21 §6D. A contingência de verdade (mudança 2, `PingOnLine` periódico, T24) só depois da D8.
12. **Coleta de bilhetes (Etapa A.9): HIL-BIL-01, INT-REC-03, CHAOS-REC-01 e NOVO-INT-REC-07:** o que `ColetarBilhete` devolve com a memória vazia (T3); 10 marcações off-line coletadas e contadas; matar o worker no meio da coleta e conferir que nenhuma se perde nem duplica; e quando a memória apaga o bilhete — ao devolver ou só no próximo pedido (T35). Roteiro no docs/21 §6F; só então a chave `catraca.coletar_bilhetes` liga.
13. **NOVO-HIL-GIRO-04 — prazo do giro (Etapa I.1b, F9):** liberar e segurar o braço com o relé 1 em 5, 20 e 50 s; a pista volta a atender pela origem 5 ou, sem ela, pelo prazo (relé + 3 s); nenhum giro dentro do tempo do relé é cortado. Diz também se a origem 5 chega de fato (HIL-EVT-01) e confirma a margem de 3 s. Roteiro no docs/21 §6J.

## 4. Desenvolvimento que dá para fazer já (sem hardware)

As fases do docs/24 §3, em ordem de impacto para o evento.

> Esta seção está detalhada, com modelo, testes e ordem, no [docs/34](34-estudo-modulo-catraca.md) §5
> e no [docs/35](35-prompt-modulo-catraca.md), Etapa B.

### Fase 3 — Cartões (alta)
- Cadastro de **tipos** (nome, ordem, cor, ativo): ~~tabela~~ **feita (B.1, migração 011)**, e ~~tipo inativo negando~~ **feito (B.2)**. Falta a tela e o caminho que grava tipos (B.6/B.7).
- Tela de cadastro (um cartão, com leitura pelo leitor do balcão se houver).
- Importação do modelo do docs/26 (.xlsx/.csv):
  - ~~prévia, erros por linha e duplicados~~ **feito (B.3, `Access.Importacao`)**;
  - ~~zeros à esquerda preservados~~ **feito (B.3)**;
  - importação atômica e desfazer a última importação (B.4);
  - ~~teste com 100 mil linhas~~ **feito para a prévia (B.3)**; o LOAD-IMPORT-01 da aplicação com catracas lendo é da B.4.
- ~~Relação com a nuvem~~ **decidida (ADR-0025):** com conexão, a nuvem manda; sem conexão, o cadastro local substitui e sobe ao reconectar. Falta a fila de subida, a lista de conflitos e o contrato da função da nuvem que recebe a fila (PROPOSTA FUTURA).

### Fase 4 — Gerenciar catraca (alta)
- ~~Acertar o relógio da catraca ao conectar e a cada hora, com alerta se divergir mais de 30 s.~~ **Feito (fase 4a):** acerto a cada conexão, conferência 1 min depois e a cada hora, aviso no cartão da catraca. O acerto automático durante a operação fica desligado até o passo 6A do docs/21.
- ~~Tela por catraca~~ **Feito (fase 4b/4c, [docs/32](32-gerenciar-catraca.md)):** mensagem temporária, refazer a conexão, acertar o relógio, liberação manual com motivo e auditoria, e "Aplicar agora" em Configurações. Bip, relés, urna e sentido aparecem desabilitados com o selo "Aguardando confirmação"; bancada no passo 6B do docs/21.
- Comandos da Etapa A.8 (bip curto e longo, liberar saída, liberar nos dois sentidos) no serviço, cada um com a sua chave técnica **desligada**; falta a bancada (docs/21 §6E), a D5 para os dois sentidos e os botões na tela (A.6).
- Falta: somar as liberações manuais nos relatórios da fase 6 (e as de saída, quando ligadas).

### Fase 5 — Alertas e notificações (alta)
Catálogo do docs/24 §1:
- catraca sem comunicação;
- worker em quarentena;
- serviço parado;
- disco ou base com problema;
- DLL ausente;
- relógio divergente;
- fila da nuvem sem esvaziar;
- lista de cartões velha;
- taxa anormal de negativas;
- libera sem girar;
- urna cheia (`A_CONFIRMAR`).

Formas de aviso: faixa, som, notificação do Windows, ícone na bandeja, histórico com "ciente". Nenhum alerta pode trazer o número de cartão completo.

### Fase 6 — Prestação de contas (média, obrigatória antes do fechamento)
- Relatórios R1–R8 do docs/25:
  - boletim por tipo;
  - fluxo por hora;
  - por catraca;
  - por origem;
  - conciliação;
  - ocorrências;
  - consolidado;
  - saúde da operação.
- Dia de operação com hora de corte (E9) e contagem de pessoas distintas.
- Validado × liberado em todo relatório.
- Corte fechado com hash na tela; PDF e CSV com a autoria da Rayzer.

### Itens menores, mas que faltam
- **Aviso de fuso:** o docs/24 pede avisar quando o Windows não estiver em Brasília. Hoje o sistema só ignora o fuso do Windows (o que está certo), sem avisar.
- **Autoria nos executáveis:** `Directory.Build.props` ainda diz `<Product>Conexão Topdata</Product>`; deveria ser "Rayzer XAcess". A tela "Sobre" também não existe (decisão de 28/09).
- ~~**CSV da prestação:** o nome do arquivo usa `DateTime.Now` (fuso do Windows)~~ **corrigido em 01/10 (§1A):** hora de Brasília.
- **Runbooks:** nenhum escrito. Já dá para escrever:
  - RB-01 (worker não inicia);
  - RB-02 (porta e firewall);
  - RB-06 (catraca sem comunicação);
  - RB-12 (pacote de diagnóstico);
  - RB-11 (backlog de sincronização).
  - RB-10 (evacuação) depende de E3.
- **Log em arquivo diário no serviço:** existe no worker e no painel, não no serviço (docs/22 §6.5).
- **Reprocessamento manual de cartas mortas:** a fila existe, falta a tela e o comando.
- **Backup e restauração da base local:** não existe (fase 5 do docs/07, RB-09).

## 5. Endurecimento e entrega (antes de produção)

| Item | Estado |
|---|---|
| Assinatura de código do Setup e dos executáveis | não existe (sem certificado) |
| SBOM, verificação de hash das DLLs, atualização assinada | não existe |
| LGPD — cartões atrelados a pessoas são dado pessoal: base legal, retenção, expurgo, relatório ao titular | não existe; passa a ser obrigatório com a fase 3 |
| Login e perfis de operador | PROPOSTA FUTURA (docs/27 §11) |
| Heartbeat e comandos remotos pela nuvem | não existe; só depois de desenhar a auditoria (ADR-0023) |
| Observabilidade OpenTelemetry | não existe (log estruturado com redação existe) |
| SOAK-72H, CHAOS-WAN-01 (8 h), LOAD-SYNC-01 | não executados (o soak curto roda no CI) |
| Instalação limpa e atualização em máquina virgem, manual do operador, UX-01 com 5 operadores | não feitos |
| Relé de webhook (Relay.Ingressos) hospedado na nuvem | código existe, não está implantado |

## 6. Docs desatualizados (dizem "não existe" para o que já existe)

| Doc | O que corrigir |
|---|---|
| 07 — backlog | fases 2–4: o motor de decisão, a operação no serviço, a sincronização com o painel e o MSI/Setup existem; renumerar para a nomenclatura de fases do docs/24 |
| 15 — integração | tabela da §1: motor de decisão e cadastro local "não existe" (já existem em parte) |
| 16 — provedores | §7: "nada hospedado" e "caminho da catraca não ligado" (o serviço hospeda; o worker chama o decisor) |
| 19 — bilheteria local | §8: "a leitura da catraca chegando aqui" já existe |
| 22 — Supabase | §6.5: "painel desktop parcial" (hoje completo, 9 telas); "cache de cartões sem fonte" (a fonte do painel existe) |
| README | contagem de testes e estado geral |

## 7. Ordem recomendada

1. **Agora, em paralelo:**
   - pedir E1 à Topdata e E2/E4/E5 ao cliente;
   - marcar a bancada.
2. **Sem esperar ninguém:**
   - Fase 4 só o relógio + Fase 5 alertas: é o que evita parar o evento;
   - aviso de fuso, autoria e runbooks RB-01/02/06/12.
3. **Fase 3 cartões:** a planilha do docs/26 já está pronta.
4. **Bancada:** HIL-STACK-01, depois o docs/21 inteiro, com os resultados voltando para os `A_CONFIRMAR`.
5. **Fase 4 completa** com o que a bancada confirmou.
6. **Fase 6 relatórios**, com E9 e E10 respondidos.
7. **Endurecimento (§5):** LGPD antes de cadastrar pessoas reais; assinatura e backup antes do evento.
