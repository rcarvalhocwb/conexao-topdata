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
- MSI e Setup em português;
- prestação de contas atual (totais e CSV);
- pacote web `rayzer-ui`.

580 testes .NET e 24 da web.

**Pronto, mas nunca exercitado:** o caminho completo da catraca (leitura → decisão → liberação → giro), o recolhimento pela urna e a liberação por sentido.

## 2. Depende de outras pessoas (bloqueia ou muda o rumo)

| # | Pergunta | Quem responde | O que destrava |
|---|---|---|---|
| E1 | Redistribuir a `EasyInner.dll` no Setup | Topdata | Setup que já funciona com catraca real, sem instalar o SDK à parte |
| E2 | B2 — parque real: quantas TopFit 4, firmware, se todas têm urna **e** leitor de QR | cliente | divisão das catracas (docs/19) e perfil do cartão |
| E3 | B4 — fail-safe × fail-secure (o que a catraca faz sem PC ou sem energia) | responsável pela segurança do evento | configuração de contingência, RB-10 (evacuação) |
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
5. A TopFit 4 distingue o sentido do giro? Sem isso não há lotação, só entradas (docs/25 §6).
6. Tempos medidos: urna × QR por pessoa, latência p95.
7. Relógio: `EnviarRelogio`/`ReceberRelogio`, semântica e formato. É pré-requisito da fase 4.
8. Mensagens no visor, bip e relés: o que cada função faz de fato (`A_CONFIRMAR_COM_TOPDATA`).
9. Leitor externo de QR: tensão, velocidade, terminador e alimentação (docs/20).
10. Ensaios de caos com hardware: CHAOS-DEV-01 (isolamento entre grupos), CHAOS-REC-01 (bilhetes sem duplicar).

## 4. Desenvolvimento que dá para fazer já (sem hardware)

As fases do docs/24 §3, em ordem de impacto para o evento.

### Fase 3 — Cartões (alta)
- Cadastro de **tipos** (nome, ordem, cor, ativo); cada cartão atrelado a um tipo ou a uma pessoa.
- Tela de cadastro (um cartão, com leitura pelo leitor do balcão se houver).
- Importação do modelo do docs/26 (.xlsx/.csv):
  - prévia, erros por linha e duplicados;
  - zeros à esquerda preservados;
  - importação atômica e desfazer a última importação;
  - teste com 100 mil linhas.
- Relação com a nuvem: o cadastro local e o `sync-cards` do painel não podem se sobrescrever. É preciso definir quem é a fonte da verdade de cada campo.

### Fase 4 — Gerenciar catraca (alta)
- ~~Acertar o relógio da catraca ao conectar e a cada hora, com alerta se divergir mais de 30 s.~~ **Feito (fase 4a):** acerto a cada conexão, conferência 1 min depois e a cada hora, aviso no cartão da catraca. O acerto automático durante a operação fica desligado até o passo 6A do docs/21.
- Tela por catraca:
  - mensagem padrão e temporária no visor;
  - reiniciar a conexão;
  - liberação manual com motivo e auditoria;
  - bip e relés.
- Só o documentado; o resto desabilitado com o selo "aguardando Topdata" e o item correspondente escrito no docs/21.

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
- **CSV da prestação:** o nome do arquivo usa `DateTime.Now` (fuso do Windows), não o horário de Brasília.
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
