# 42. Fase 1 do docs/41: o que foi corrigido e o que falta

**Escopo:** a Fase 1 do caminho crítico do [docs/41](41-auditoria-linha-a-linha.md) §6 trata do código, sem bancada. As regras de produto seguem o que foi decidido no docs/41 §9:

- as respostas recomendadas foram aceitas;
- D8 é fail-secure por escrito;
- sem internet, a borda retenta sem limite.

**Leitura obrigatória:** nada aqui foi validado em catraca real. Cada correção tem teste automatizado. Os testes principais foram vistos falhando sem a correção, antes de passarem com ela. Ainda assim, o comportamento com a TopFit 4 real só fica provado na Fase 3 (HIL-STACK-01 em bancada).

## Achados resolvidos, por commit

| Commit | Achados | O que mudou |
|---|---|---|
| `a86fbab` | E8-3, E6-1, E10-3 (as 3 regressões) | A restrição de permissão alcança a pasta da instalação e, fora dela, só os arquivos do banco e as pastas `copias` e `registros`. Nunca alcança a pasta onde o banco está. O segundo passo de "Aplicar agora" se habilita. `verificar-ambiente.ps1` entende o layout instalado. |
| `c583a2a` | E2-02, E10-1, E10-6 | Nenhum laço em segundo plano derruba o serviço (`LacoResiliente` e `BackgroundServiceExceptionBehavior.Ignore`). Uma cópia parcial é apagada. Avisos e erros vão para o registro em arquivo, inclusive os da partida. O MSI religa o serviço depois de uma atualização. |
| `6cdcd25` | E1-04, E1-05 | Uma falha ao gravar o giro não mata o worker: a confirmação fica numa fila em memória e é regravada a cada volta. Uma falha numa catraca não derruba o grupo. Uma liberação que a DLL recusa encerra a tentativa pendente. |
| `274ccb6` | (CI) | A limpeza das pastas de teste tolera o pool do SQLite no Windows. |
| `49140a5` | E1-01, E2-01, E1-03, E2-03 | Sonda de vida em Polling: `TestarConexao` depois de 10 s sem prova de vida. O batimento do worker é lido em `device_status`: mais de 90 s sem notícia faz o supervisor matar e reiniciar. O laço ocioso pausa até 100 ms, linhas repetidas são agregadas e o registro tem limite de 50 MB por dia. A quarentena sai sozinha depois de 15 min. |
| `8f79553` | E3-01/E5-4, E8-1, E5-1, E5-2, E5-3 | Sem internet, nada vira carta morta: a borda retenta sem limite de tentativas e com prazo de 7 dias. Sem o segredo da nuvem, a borda não sincroniza e o painel mostra a falha. A suspeita de corte faz a rodada falhar. 401/403 são temporários, com mensagem sobre a credencial. O painel ganhou "Reenviar os recusados", com o nome de quem pede. O `device_id` passou a ser o do equipamento. |
| `74c0dbf` | E9-1, E9-2, E9-8, E10-4 (parcial), E10-2 | O teste de queda passou a cobrir o caminho real (`TentarUsar`), com kill no meio. O fio da `EasyInnerReal` é conferido pelo IL. O job Windows publica o TRX. O instalador só sai depois de Linux e Windows verdes. O canal de teste tem nome e UpgradeCode próprios. |
| `a779ef4` | E6-2, E6-3, E6-4, E6-5, E6-6, E6-11, E10-7, E3-03, E10-13 | As chamadas do painel têm prazo (5 s, 15 s ou 60 s) e não se empilham. Sem resposta do serviço, os sinais ficam neutros, nunca verdes. "Refazer a conexão" pede confirmação, e o assistente pergunta antes de reiniciar o serviço. Os textos foram corrigidos. Antes de cada migração há uma cópia da base. O RB-09 proíbe restaurar durante o evento, e o RB-06 testa na direção certa. |
| `d34b9d1` | E8-2, E2-04 | O serviço (SYSTEM) só inicia o programa das catracas que está na pasta do programa. Como SYSTEM, também toma posse da pasta de dados. O que continuar com dono desconhecido não é lido, e o serviço não sobe workers. Um cofre ilegível conta como ausente: a nuvem para e as catracas sobem. |
| `6166962` | E3-04, E3-09, E4-1, E4-2, E7-1, E7-2 | Uma base travada por outro escritor segura a decisão por 5 s, não 30 s: o teste mede 31 s sem a correção e 1 s com ela. Um comando recebido e sem desfecho em 10 min vira "desfecho desconhecido". O prazo da decisão nega na própria tabela de estados. Um cartão `mifare-catraca4` lido sem os zeros casa, mas só depois da leitura exata. As 7 RPCs não implementadas respondem "desligada". A migração 018 apaga as chaves `inteligencia.*` herdadas. |
| `c0a65e0` | E1-05 | Decisão: manter consumido, com registro. A tentativa guarda a causa da falha. Na tela Acessos, "Usos sem passagem" permite estornar em dois passos, com nome e motivo, depois de 2 min. O estorno devolve o uso ao ingresso, tira a entrada dos relatórios e fica auditado (migração 019). |
| `8ff8bf3` | E10-8 | O assistente habilita o .NET 3.5 sem internet, pela pasta `sources\sxs` da mídia do Windows (`/Source` e `/LimitAccess`). O procedimento está no `installer/README.md`. |

## Desvios do docs/41, com o motivo

1. **E10-4, "pré-release só no push para main":** não foi aplicado. Isso deixaria de gerar os instaladores de teste que este PR produz para a validação. Foi aplicada só a dependência (`needs: [linux, windows]`): um teste falhando já impede a pré-release.
2. **E2-02, `FailureActionsOnNonCrashFailures`:** não foi aplicado, porque não teria efeito. O `WindowsServiceLifetime` do .NET relata código 0 numa parada limpa e ignora `Environment.ExitCode` (dotnet/runtime#67146). O que resolve é o serviço não parar mais por exceção de laço, que é o `c583a2a`.
3. **E1-05, estorno do ingresso quando a liberação falha:** decidido como "manter consumido + registro" (`c0a65e0`). O estorno vale **nesta borda**. O uso já enviado à nuvem e ao provedor fica como foi, porque o contrato do retorno (docs/18 §10) não prevê estorno. A tela avisa disso antes da confirmação. *Correção do que este documento dizia antes:* cada uso sem giro sempre teve uma linha própria em `ticket_use_attempt` (consumida, sem prova de giro). O que faltava era a causa e o estorno.
4. **Canal de teste do instalador (E10-2):** como o UpgradeCode de teste mudou, uma máquina que tem a versão de teste antiga precisa desinstalá-la antes de instalar a nova. Ver `installer/README.md`.

## O que ainda falta (não é código desta fase)

- **E8-2, parte do instalador:** o MSI ainda não cria a pasta de dados com permissão protegida. A partida do serviço resolve isso (troca dono e permissão). A hipótese de elevação só se prova ou se descarta numa VM (Fase 2).
- **E1-06:** firmware fora de {14,16} leva a um estado terminal. Os valores não têm fonte; só a Topdata ou a bancada respondem.
- **E1-07:** a chave `EnviarDigitosVariaveis`, para os dígitos de cartão e QR. Depende da bancada.
- **E5-5, E5-6, E8-4:** maquininha e cortesia, o relé e o token na URL. Dependem de decisão de produto e da hospedagem.
- **E10-8, parte do instalador:** o DISM dentro do MSI continua sem prazo, e uma instalação sem internet pode esperar alguns minutos antes de seguir (a falha não desfaz a instalação).

## Próximas fases

- **Fase 2 (VM Windows):**
  - instalar o MSI de teste, conferir permissões (E8-2), atualização e religamento (E10-1);
  - rodar o RB-01 inteiro com `verificar-ambiente.ps1` sem FALHA.
- **Fase 3 (bancada com TopFit 4):** HIL-STACK-01, sonda de vida com cabo de rede puxado, firmware real (E1-06) e padrão de dígitos (E1-07).
- **Dependências de terceiros:**
  - chaves do Supabase (tirar as permissões do `anon` e exigir o segredo);
  - Zet Z1/Z2;
  - certificado de assinatura;
  - prazo da LGPD (D7).
