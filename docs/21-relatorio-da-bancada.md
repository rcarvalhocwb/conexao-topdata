# 21 — Relatório da bancada (modelo)

**Resultado desta rodada: NÃO EXECUTADO.** Este arquivo é um modelo, sem resultado de
hardware. Copie-o para `resultado-da-bancada.md` e preencha durante o
[roteiro](21-roteiro-da-bancada.md). Use uma cópia por rodada/configuração e repita as
linhas por catraca quando necessário; não sobrescreva uma rodada anterior.

## Identificação da rodada

| Campo | Valor a preencher |
|---|---|
| Data/hora inicial e final, com fuso | — |
| Responsável pela execução | — |
| Natureza | — (hardware real ou simulação; simulação não aprova hardware) |
| Versão do programa | — |
| Commit compilado / commit da tarefa | — / — (copiar `identificacao-do-pacote.json`; podem ser diferentes) |
| URL da execução do CI | — |
| Hashes dos arquivos ensaiados | — (guardar `identificacao-do-pacote.json` junto das evidências) |
| Windows: versão/arquitetura; modo worker direto ou instalado | — |
| SDK Inner Acesso: versão, arquitetura e SHA-256 da DLL usada | — (somente metadados; não anexar o SDK) |
| Catraca principal: alias, modelo, firmware, Inner e porta | — |
| Catraca testemunha, quando exigida: alias, modelo, firmware e Inner | — |
| Leitores instalados e `--tipo-leitor` usado | — |
| Configuração inicial: versão, relés, perfil e mapa de giro | — (sem credenciais) |
| Chaves técnicas ligadas nesta rodada | — (nome e valor, antes/depois) |
| Arquivo de carga | — (só teste; não anexar se contiver códigos reais) |
| Saída/código de saída de `verificar-ambiente.ps1` | — |

Resultado de cada linha: **PASSOU**, **FALHOU**, **NÃO EXECUTADO** ou
**NÃO SE APLICA**. Os dois últimos precisam de motivo. PASSOU exige o resultado
observado e a referência à evidência, incluindo horário e catraca; não basta marcar a
coluna. Uma falha aponta para correção e teste ou para issue. Condições diferentes
(leitor, chave, sentido ou tempo) devem ter sublinhas separadas.

## Pré-requisitos e fluxo inicial (§0–§5)

| Passo | Conferência do roteiro | Resultado | Observado / catraca / horário | Evidência ou impedimento |
|---|---|---|---|---|
| 0 | PC/rede, SDK, pacote identificado, materiais de teste e configuração do Inner | NÃO EXECUTADO | — | — |
| 1 | HIL-STACK-01: worker x86 abre a porta com a DLL real | NÃO EXECUTADO | — | — |
| 2 | Conecta, lê firmware, configura e entra em operação | NÃO EXECUTADO | — | — |
| 4.1–2 | Cartão A de teste no arquivo; reinício e carga | NÃO EXECUTADO | — | — |
| 4.3 | QR válido libera e confirma o giro físico (origem 6) | NÃO EXECUTADO | — | — |
| 4.4 | Repetir o QR nega usos esgotados | NÃO EXECUTADO | — | — |
| 4.5 | Cartão A na urna libera e confirma o giro | NÃO EXECUTADO | — | — |
| 5.1 | QR desconhecido | NÃO EXECUTADO | — | — |
| 5.2 | QR autorizado sem passagem | NÃO EXECUTADO | — | — |
| 5.3 | Cartão repetido dentro do intervalo de reuso | NÃO EXECUTADO | — | — |
| 5.4 | Somente na urna: cartão no leitor da frente | NÃO EXECUTADO | — | — |
| 5.5 | Somente na urna: o mesmo cartão na fenda | NÃO EXECUTADO | — | — |
| 5.6 | Passe de dois usos: duas liberações e terceira negada | NÃO EXECUTADO | — | — |

## Leitores (§3)

Guarde os códigos completos em tabela **privada, fora do repositório**. Nesta tabela
use apenas alias/máscara, quantidade de caracteres e a comparação com o cadastro:
igual, igual sem zeros à esquerda ou diferente. Registre origem numérica e nome,
leitor utilizado, catraca e horário na evidência. Recusa ou truncamento também são
resultados; não invente uma normalização para fazê-los passar.

| Linha | Amostra | Resultado | Origem / caracteres / comparação (sem código completo) | Evidência ou impedimento |
|---|---|---|---|---|
| 3.1 | Cartão A na urna | NÃO EXECUTADO | — | — |
| 3.2 | Cartão A na frente | NÃO EXECUTADO | — | — |
| 3.3 | QR numérico de 10 caracteres | NÃO EXECUTADO | — | — |
| 3.4 | QR numérico de 16 caracteres | NÃO EXECUTADO | — | — |
| 3.5 | QR com letras | NÃO EXECUTADO | — | — |
| 3.6 | QR de 20 caracteres | NÃO EXECUTADO | — | — |
| 3.7 | Cartão A no balcão, comparado com 3.1 | NÃO EXECUTADO | — | — |
| 3.8 | INTEIRA/MEIA de 12 dígitos | NÃO EXECUTADO | — | — |
| 3.9 | SOCIAL de 14 dígitos começando em 00 | NÃO EXECUTADO | — | — |
| 3.10 | SOCIAL de 14 dígitos sem zero inicial | NÃO EXECUTADO | — | — |
| 3.11 | INTEIRA de 11 dígitos | NÃO EXECUTADO | — | — |
| 3.12 | SOCIAL começando em 0000 | NÃO EXECUTADO | — | — |

## Tempos (§6)

Anote as dez medições de cada fluxo, em segundos, do gesto à liberação e do gesto à
passagem completa. Identifique tentativa/catraca nas evidências e registre separadamente
as falhas ou passagens não concluídas.

| Fluxo | Resultado | Dez tempos até liberar (s) | Dez tempos totais (s) | Evidência ou impedimento |
|---|---|---|---|---|
| QR no celular | NÃO EXECUTADO | — | — | — |
| Cartão na urna | NÃO EXECUTADO | — | — | — |

## Relógio e comandos (§6A–§6B)

| Passo | Conferência do roteiro | Resultado | Observado / catraca / horário | Evidência ou impedimento |
|---|---|---|---|---|
| 6A.1 | Acerto na conexão | NÃO EXECUTADO | — | — |
| 6A.2 | Conferência um minuto depois (diferença em segundos) | NÃO EXECUTADO | — | — |
| 6A.3 | Hora do display comparada à do PC, com fuso | NÃO EXECUTADO | — | — |
| 6A.4 | Reconexão, novo acerto e QR logo depois | NÃO EXECUTADO | — | — |
| 6B.1 | Mensagem TESTE 123 e duração | NÃO EXECUTADO | — | — |
| 6B.2 | Mensagem com acentos | NÃO EXECUTADO | — | — |
| 6B.3 | Liberação manual com giro, sentido e histórico | NÃO EXECUTADO | — | — |
| 6B.4 | Liberação manual sem giro, prazo e histórico | NÃO EXECUTADO | — | — |
| 6B.5 | QR sem giro, queda/reconexão, liberação manual: QR continua sem giro | NÃO EXECUTADO | — | — |
| 6B.6 | Acertar relógio pelo painel | NÃO EXECUTADO | — | — |
| 6B.7 | Refazer conexão e tempo sem atender | NÃO EXECUTADO | — | — |
| 6B.8 | Salvar/aplicar mensagem sem reiniciar serviço | NÃO EXECUTADO | — | — |
| 6B.9 | INT-CFG-05: versão salva × aplicada, com/sem rede e testemunhas | NÃO EXECUTADO | — | — |

## Chaves e sequência de conexão (§6C–§6D)

| Passo | Conferência do roteiro | Resultado | Observado / catraca / horário | Evidência ou impedimento |
|---|---|---|---|---|
| 6C.1 | INT-CFG-07: data/hora no evento, dez leituras | NÃO EXECUTADO | — | — |
| 6C.2 | INT-OFF-08: negação nos valores 0, 1, 2 e 3, on-line/off-line | NÃO EXECUTADO | — | — |
| 6C.3 | INT-OFF-02: tipo de lista explícito sem mudar liberações | NÃO EXECUTADO | — | — |
| 6C.4 | HIL-CARD-05: dois leitores, comparação com §3 | NÃO EXECUTADO | — | — |
| 6C.5 | INT-SM-032: rearme em vinte leituras | NÃO EXECUTADO | — | — |
| 6D.1 | INT-SM-021: referência com chave desligada | NÃO EXECUTADO | — | — |
| 6D.2 | Chave ligada: três envios em ordem, funções/retornos | NÃO EXECUTADO | — | — |
| 6D.3 | QR e cartão na frente/urna iguais à referência | NÃO EXECUTADO | — | — |
| 6D.4 | Vinte liberações e rearmes | NÃO EXECUTADO | — | — |
| 6D.5 | Cabo fora por 30 s: sem liberação autônoma; reconexão | NÃO EXECUTADO | — | — |
| 6D.6 | Reconexão pelo painel comparada à referência | NÃO EXECUTADO | — | — |

A segunda parte de INT-SM-021 (contingência real) depende de D8 e T24. Não confunda
aprovar a sequência de envios com aprovar o modo off-line. Registre as respostas T13
(enviador) e T24 (mudança/tempo/ping) nas pendências abaixo.

## Comandos adicionais e bilhetes (§6E–§6F)

| Passo | Conferência do roteiro | Resultado | Observado / catraca / horário | Evidência ou impedimento |
|---|---|---|---|---|
| 6E.1 | INT-UX-03: bip curto, cinco repetições e leitura seguinte | NÃO EXECUTADO | — | — |
| 6E.2 | INT-UX-03: bip longo, duração e distinção do curto | NÃO EXECUTADO | — | — |
| 6E.3 | HIL-DIR-04/05/06: saída em cada perfil e prestação | NÃO EXECUTADO | — | — |
| 6E.4 | HIL-DIR-07: dois sentidos, somente após decisão D5 | NÃO EXECUTADO | — | D5 pendente; não habilitar para contornar a recusa |
| 6F.1 | HIL-BIL-01: memória vazia, retorno nativo e volta à operação | NÃO EXECUTADO | — | — |
| 6F.2 | INT-REC-03: dez passagens off-line, quantidade/tipos/datas | NÃO EXECUTADO | — | — |
| 6F.3 | Segunda coleta vazia | NÃO EXECUTADO | — | — |
| 6F.4 | CHAOS-REC-01: queda na décima gravação, total e duplicações | NÃO EXECUTADO | — | — |
| 6F.5 | NOVO-INT-REC-07: três quedas distintas, totais e tipo 128 | NÃO EXECUTADO | — | — |

Em 6F.4/5 anote o total esperado/observado por rodada, perdas, repetições e o resultado
da consulta de duplicações do roteiro. A resposta documental T35 sobre remoção da
memória precisa ficar anexada; teste verde sem essa resposta não resolve a dúvida.

## Parametrização e vida dos processos (§6G–§6H)

| Passo | Conferência do roteiro | Resultado | Observado / catraca / horário | Evidência ou impedimento |
|---|---|---|---|---|
| 6G.1 | INT-PAR-01: modo guiado e rótulos | NÃO EXECUTADO | — | — |
| 6G.2 | Mudar só mensagem: uma diferença | NÃO EXECUTADO | — | — |
| 6G.3 | Salvar sem aplicar; testemunha inalterada | NÃO EXECUTADO | — | — |
| 6G.4 | Aplicar com confirmação, display e testemunha | NÃO EXECUTADO | — | — |
| 6G.5 | Aplicar sem rede: falha nunca vira aplicada | NÃO EXECUTADO | — | — |
| 6G.6 | Reconectar após falha, depois aplicar novamente | NÃO EXECUTADO | — | — |
| 6G.7 | Modo técnico: controles pendentes e aviso de sentido | NÃO EXECUTADO | — | — |
| 6H.1 | CHAOS-SVC-01: simulação identificada, quantidade de workers | NÃO EXECUTADO | — | — |
| 6H.2 | Finalizar serviço: nenhum worker sobra em até 2 s | NÃO EXECUTADO | — | — |
| 6H.3 | Sem simulação nem catracas: nenhuma atendendo | NÃO EXECUTADO | — | — |
| 6H.4 | Faxina de órfãos da versão anterior | NÃO EXECUTADO | — | — |
| 6H.5 | Worker de bancada com processo pai vivo é poupado | NÃO EXECUTADO | — | — |

6H é um ensaio do Windows instalado em modo simulação. Identifique-o assim; esse
resultado não aprova a comunicação com a catraca física.

## Mapa e prazo do giro (§6I–§6J)

| Passo | Conferência do roteiro | Resultado | Observado / catraca / horário | Evidência ou impedimento |
|---|---|---|---|---|
| 6I.1 | NOVO-HIL-DIR-11: quatro origens em Padrão | NÃO EXECUTADO | — | — |
| 6I.2 | Urna destacada no painel 3D | NÃO EXECUTADO | — | — |
| 6I.3 | Regra EI-042 contando entrada, salva sem conferência | NÃO EXECUTADO | — | — |
| 6I.4 | Aplicação do mapa e tempo sem atender | NÃO EXECUTADO | — | — |
| 6I.5 | Urna: lado físico e contagem de entrada/saída | NÃO EXECUTADO | — | — |
| 6I.6 | Conferência registrada com responsável e hora | NÃO EXECUTADO | — | — |
| 6I.7 | Leitor da frente preserva regra padrão | NÃO EXECUTADO | — | — |
| 6I.8 | Repetição por catraca/função usada (EI-041 a EI-044) | NÃO EXECUTADO | — | — |
| 6I.9 | T14/NOVO-HIL-DIR-08: Complemento bruto por lado | NÃO EXECUTADO | — | — |
| 6I.10 | NOVO-HIL-DIR-12: texto antes do giro, impacto no tempo | NÃO EXECUTADO | — | — |
| 6J.1 | NOVO-HIL-GIRO-04: relé 5 s, braço seguro, origem 5 ou prazo 8 s | NÃO EXECUTADO | — | — |
| 6J.2 | Próximo QR é atendido | NÃO EXECUTADO | — | — |
| 6J.3 | Relé 20 s, sem desistência antes de 23 s, próximo QR | NÃO EXECUTADO | — | — |
| 6J.4 | Relé 50 s, sem desistência antes de 53 s, próximo QR | NÃO EXECUTADO | — | — |
| 6J.5 | Giro aos 4–5 s é confirmado e contado | NÃO EXECUTADO | — | — |
| 6J.6 | Liberação manual sem giro: desfecho e retorno à operação | NÃO EXECUTADO | — | — |

Acrescente aqui uma tabela por catraca com **função / lado físico visto de frente /
contagem / horário / evidência** para 6I.8. Em 6J registre os segundos medidos e qual
caminho encerrou a espera; simular a ausência da origem 5 não comprova que ela deixa
de vir no hardware.

## Encerramento (§7 e §9)

| Conferência | Resultado | Observado | Evidência ou impedimento |
|---|---|---|---|
| Resumo final: consumidos, giros, sem giro, negados e desconhecidos batem com a rodada | NÃO EXECUTADO | — | — |
| Chaves experimentais restauradas; configurações inicial/final registradas | NÃO EXECUTADO | — | — |
| Critérios de §9 conferidos individualmente | NÃO EXECUTADO | — | — |

**Conclusão da rodada:** NÃO EXECUTADO. Ao preencher, liste o que foi aprovado, o que
falhou e o que continua pendente. Aprovar um subconjunto não homologa as funções não
ensaiadas. A #10 só pode ser encerrada com os resultados/evidências do escopo e as
falhas encaminhadas; uma chave só muda de padrão após seus critérios e decisões.

## Pendências e perguntas A_CONFIRMAR

Use os IDs do [docs/34](https://github.com/rcarvalhocwb/conexao-topdata/blob/main/docs/34-estudo-modulo-catraca.md)
e do [catálogo do SDK](https://github.com/rcarvalhocwb/conexao-topdata/blob/main/docs/34-anexos/01-engenheiro-topdata.md).
Acrescente as demais perguntas que a rodada encontrou. Uma resposta documental precisa
de fonte/data; uma observação precisa de modelo/firmware, configuração e evidência.

| ID | Pergunta / impedimento | Situação inicial | Resultado / fonte / evidência | Correção+teste ou issue / próximo responsável |
|---|---|---|---|---|
| HIL-STACK-01 | DLL real carrega no worker .NET 10 x86? | PENDENTE DE HARDWARE/SDK | — | — |
| T13 | Qual enviador aplica a mudança automática? | A_CONFIRMAR | — | — |
| T14 | Complemento da origem 6 distingue o lado do giro? | A_CONFIRMAR | — | — |
| T24 / D8 | Regime de contingência, tempo e ping; confirmar a decisão e a configuração usada (docs/34 diz A.7 ligado, mas o padrão do código/roteiro é desligado) | A_CONFIRMAR; sem habilitação nesta rodada | — | — |
| T35 | Quando o bilhete é removido e o que o tipo 128 devolve? | A_CONFIRMAR | — | — |
| D5 | Decisão sobre liberação nos dois sentidos | decisão pendente | — | — |
| INT-OFF-03 | Lista: envio/substituição/exclusão e bloqueio conferidos no equipamento | NÃO EXECUTADO; pré-requisito da #12 | — | — |
| INT-OFF-04 | Horários 1–100: dias, faixas, ordem de envio e limite por dia | NÃO EXECUTADO; pré-requisito da #12 | — | — |
| P8 (§6.3 do docs/43) | Tratamento de tabela com mais de duas faixas por dia | A_CONFIRMAR; não implementar por suposição | — | — |

O roteiro atual mantém a contingência desligada e não envia listas/horários de pessoas.
INT-OFF-03/04 precisam de ensaio próprio aprovado com o SDK; anotar a lacuna aqui não
habilita funções nem fecha a #12.

## Índice de evidências publicáveis

| ID | Arquivo / intervalo do registro | Catraca e horário | O que comprova | Conferência da redação |
|---|---|---|---|---|
| — | — | — | — | — |

Use os registros do serviço e do worker indicados pelo sistema instalado e a saída do
console no modo direto. Anexe só trechos revisados, com aliases/máscaras de cartões de
teste. A tabela de números completos fica privada. Não anexe código real, token,
credencial, `bancada.db`, backup nem SDK. Guarde o original restrito para investigação;
registre o nome e o intervalo de cada cópia redigida neste índice.
