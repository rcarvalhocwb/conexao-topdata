# 24 — Análise do primeiro teste instalado e prompt de ajustes

Origem: primeiro teste do `ConexaoTopdata-Setup.exe` (commit 452276d) no PC do usuário, em
modo simulação, em 28/09/2026. Cada item abaixo foi conferido no código; a coluna "causa"
diz o que foi achado, não o que se supõe.

## 1. Diagnóstico

| # | Relato | Causa conferida no código | Gravidade |
|---|---|---|---|
| 1 | "Launch" no fim do Setup não abre nada | `installer/wix/Setup.wxs` usa `LaunchTarget` apontando para o `Edge.Configurador.exe`, que tem manifesto `requireAdministrator`. O Burn abre o alvo **sem elevação**; o Windows recusa (erro 740) e o bundle não avisa. Falta `LaunchTargetElevatedId` + `ApprovedExeForElevation`, ou apontar o botão para o Painel (que não exige administrador). | Alta |
| 2 | Com o sistema aberto, o Setup fala em inglês e não fecha os aplicativos | O bundle usa o tema padrão do WiX (`hyperlinkLicense`) sem localização pt-BR. O MSI não declara `util:CloseApplication` para `Desktop.App.exe` e `Edge.Configurador.exe`; o Restart Manager só pede para o usuário fechar. | Alta |
| 3 | Branding e alinhamento "amadores" | Controles WPF sem estilo próprio (ComboBox, DatePicker e botões no visual padrão do Windows), alturas diferentes na mesma linha de filtros (`MinHeight` solto por controle), sem logotipo, paleta ou tipografia do evento. Não existe guia visual; os prints do sistema de nota fiscal, referência pedida, ainda não chegaram. | Média |
| 4 | Seleção de data e hora disfuncional | (a) O WPF usa `en-US` por padrão em `FrameworkElement.Language`, independente do Windows: daí o "Select a date". (b) Não há hora: `DatePicker` só escolhe dia. (c) **Bug**: o filtro "Até" vira meia-noite do dia escolhido (`TelaBase.Instante`), então "até hoje" exclui o dia de hoje inteiro — em `Acessos` e em `Prestação de contas`. | Alta (c) / Média |
| 5 | Prestação de contas pouco definida | O contrato (`PrestacaoDeContas` no proto) só tem totais por categoria, catraca, hora e motivo de negativa. Faltam: por provedor, por lote/turno/dia do evento, entradas únicas × reentradas, capacidade/lotação, conciliação com a bilheteria (vendidos × usados × não usados), exportação (PDF/CSV) com assinatura/hash, e corte fechado reproduzível na tela. | Média |
| 6 | Falta "gerenciar a catraca" | Não existe RPC nem tela para comandos. O adapter já tem `EnviarMensagemPadrao` e `AcionarReleDaUrna`; a interop gerada declara `EnviarMensagemTemporariaOnLine`, `AcionarBipCurto/Longo`, `LigarBipIntermitente`, `ManterRele1Acionado`, `DesabilitarRele1/2`, `EnviarRelogio`, `ReceberRelogio`, entre outras. Várias estão sem semântica documentada — seguem a regra `A_CONFIRMAR_COM_TOPDATA`. | Alta |
| 7 | Falta cadastro/importação de cartões | Não existe RPC nem tela. Hoje o cartão entra só pela sincronização com a nuvem ou pelo arquivo de simulação. As três planilhas recebidas (cartões autorizados, RFID e offline) definem os formatos de importação (ver `docs/22`, só contagens e formatos). | Alta |
| 8 | Fuso horário | A base guarda UTC e as telas convertem com `ToLocalTime()` — segue o fuso do Windows, o que está certo. **Mas o relógio da catraca nunca é acertado**: `EnviarRelogio` existe na interop e nenhum código a chama. O visor pode mostrar hora diferente do PC, e registros offline da catraca ficariam com a hora dela. Também não há aviso quando o fuso do Windows não é o do evento (America/Sao_Paulo). | Alta |
| 9 | Alertas visuais e notificações | Só existe a faixa de estado no topo do painel. Não há: alerta sonoro, notificação do Windows, ícone na bandeja, histórico de alertas, reconhecimento ("ciente") pelo operador, nem regras para as situações que param o evento. | Alta |

### Situações que podem parar o evento (base para o item 9)

Catraca sem comunicação; programa da catraca parado/em quarentena; serviço parado; disco
quase cheio ou base local com erro; `EasyInner.dll` ausente; relógio da catraca divergente;
fila de envio à nuvem crescendo sem esvaziar; cartão/lista desatualizados (última
sincronização velha); taxa de negativas anormal numa catraca (leitor sujo, QR errado,
fraude); catraca liberando sem girar repetidamente (travada); urna cheia (se o equipamento
informar — `A_CONFIRMAR_COM_TOPDATA`); PC em bateria/sem energia (se houver no-break
monitorável).

## 2. O que depende do usuário

- **Identidade visual**: logotipo, cores e fontes do evento, e os prints do sistema de nota fiscal.
- **Relatórios**: quais números a prestação de contas precisa mostrar, e para quem (organização, patrocinador, prefeitura).
- **Importação de cartões**: confirmar que as três planilhas já enviadas são os formatos a aceitar.
- **Fuso do evento**: confirmar America/Sao_Paulo.
- **Comandos da catraca**: quais o operador pode usar durante o evento. Os que não estão documentados pela Topdata só são liberados depois do teste de bancada.

## 3. Prompt

Copie o bloco abaixo para uma sessão nova (ou peça "execute o prompt do docs/24" nesta).

```text
Você vai coordenar um time de especialistas para levar o sistema Conexão Topdata
(repositório rcarvalhocwb/conexao-topdata, branch claude/gallant-wright-pdloor, PR #1) do
estado "funciona no simulador" para "pronto para operar a Rua Iluminada Família Moletta".
Leia antes: README.md, docs/03, docs/10, docs/21, docs/22, docs/23, docs/24 (este
diagnóstico) e os ADRs 0023 e 0024. Trabalhe em português.

## O time (cada um assina a sua parte no PR e revisa a do outro)

1. Gerente de produto de eventos — prioriza pelo risco de parar a portaria; escreve os
   critérios de aceite de cada item; fala com o usuário quando algo depende dele.
2. Designer de UX/UI para operação crítica — cria o guia visual (paleta, tipografia,
   grade de 8 px, estados de cor Bom/Atenção/Problema, ícones), desenha cada tela para
   operador em pé, sob pressão, com toque ou mouse; contraste AA; nada de controle
   padrão do Windows sem estilo.
3. Desenvolvedor WPF sênior — implementa o guia em ResourceDictionary único (estilos de
   TextBox, ComboBox, DatePicker, botões, DataGrid, cartões), um seletor de data E hora
   próprio em pt-BR, cultura pt-BR no aplicativo inteiro, alinhamento por Grid com
   SharedSizeGroup; mantém MVVM (Desktop.ViewModels sem WPF).
4. Engenheiro de instalador (WiX 6/Burn) — tema próprio do bundle em pt-BR (.wxl),
   botão final que funciona (Painel sem elevação ou Assistente com
   LaunchTargetElevatedId/ApprovedExeForElevation), fechamento automático dos
   aplicativos com util:CloseApplication e parada/partida do serviço na atualização,
   tela de manutenção (Reparar/Desinstalar) em português, logotipo do evento no bundle.
5. Engenheiro de integração Topdata (EasyInner) — expõe comandos da catraca por uma
   interface do adapter, com cada função classificada: documentada e testada em bancada
   / documentada sem teste / A_CONFIRMAR_COM_TOPDATA (desabilitada por padrão, com teste
   de bancada escrito no docs/21). Acerta o relógio da catraca (EnviarRelogio) na
   conexão e periodicamente, e alerta divergência.
6. Engenheiro de back-end e dados (serviço, gRPC, SQLite) — novos RPCs: comandos da
   catraca (com fila, confirmação e auditoria de quem mandou), cadastro e importação de
   cartões (validação, prévia, relatório de erros por linha, importação atômica), relatórios
   e alertas; migrações novas, nunca editar as antigas.
7. Analista de prestação de contas (financeiro/auditoria de eventos) — define os
   relatórios: por dia/turno, provedor, categoria, catraca e hora; vendidos × usados ×
   não usados; entradas únicas × reentradas; negativas por motivo; corte fechado com
   hash; exportação PDF e CSV com cabeçalho do evento, período e horário de geração.
8. Engenheiro de confiabilidade/operação de eventos (SRE) — catálogo de alertas com
   severidade, regra, mensagem ao operador e ação sugerida (lista em docs/24 §1); faixa
   de alerta, som, notificação do Windows, ícone na bandeja, histórico com "ciente";
   alerta repetido não vira ruído (agrupar e silenciar por tempo).
9. Especialista em segurança e LGPD — revisa cada item novo contra as regras abaixo;
   importação de cartões sem expor números completos em log; auditoria dos comandos.
10. Engenheiro de QA — testes de unidade e integração para cada item, testes de
    ligação das telas (tests/Integration/LigacoesDasTelasTests), autoteste do painel no
    CI com o serviço em modo simulação (já existe: estender a cada tela nova), capturas
    de tela do CI anexadas como artefato para revisão visual, roteiro de teste manual
    para o usuário.
11. Redator técnico — textos da interface em português de portaria, docs/23 e docs/21
    atualizados, notas da versão no pré-lançamento.

## Regras que ninguém quebra

- Não invente funções, valores de enum, códigos de retorno, capacidades ou comportamentos.
  Quando a documentação for ambígua, marque A_CONFIRMAR_COM_TOPDATA, crie uma abstração
  desabilitada por padrão e escreva um teste de bancada.
- Preserve os exemplos oficiais como referência somente leitura. Não copie cegamente
  código de demonstração para produção.
- Preserve cartões como string; nunca converta para número que remova zeros à esquerda.
- Criptografe segredos e dados sensíveis em repouso usando DPAPI/Windows Credential
  Manager ou cofre configurado.
- Nunca armazene credencial em texto puro e nunca exponha um banco cloud diretamente à
  rede pública das catracas.
- Logs estruturados sem número completo de cartão, face, digital, senha ou segredo.
- Não exponha WebServer HTTP do equipamento em internet pública. Segmente catracas em
  VLAN e restrinja ACLs.
- Proteja a API local contra outro processo não autorizado.
- Dados biométricos exigem proteção forte, minimização, base legal e política de retenção.
- Nunca declare "finalizado" apenas porque compilou. Mostre testes executados,
  resultados, limitações conhecidas e o próximo teste de hardware necessário.
- O repositório é público: nada de binário/SDK/PDF da Topdata, código do Lovable, .env,
  chaves, URL do projeto Supabase, números de cartão ou as planilhas enviadas.

## Ordem de entrega (cada fase é um push com CI verde e Setup novo publicado)

Fase 1 — Defeitos que atrapalham o teste agora
- Botão final do Setup abre o sistema.
- Setup em português, fecha Painel e Assistente sozinho e retoma a instalação.
- Filtro "Até" inclui o dia inteiro (ou a hora escolhida); datas em pt-BR em todo lugar.
- Aceite: teste automatizado para o filtro; no CI, instalação silenciosa do Setup com o
  Painel aberto termina sem pedir nada; captura do Setup em pt-BR como artefato.

Fase 2 — Guia visual e seletor de data e hora
- Guia visual aplicado a todas as telas e ao Assistente; seletor de data e hora próprio;
  cultura pt-BR e fuso do Windows exibido no rodapé, com aviso se diferente do do evento.
- Aceite: capturas de todas as telas no CI (--capturar), revisadas contra o guia; teste
  de ligação sem falhas; nenhum texto em inglês nas capturas.

Fase 3 — Cartões
- Tela de cadastro (um cartão, com leitura pelo leitor do balcão se houver) e de
  importação (CSV nos três formatos recebidos): prévia, erros por linha, duplicados,
  zeros à esquerda preservados, importação atômica, desfazer a última importação.
- Aceite: testes com arquivos sintéticos (nunca os reais), incluindo zeros à esquerda,
  duplicados, linhas inválidas e arquivo grande (100 mil linhas) com tempo medido.

Fase 4 — Gerenciar catraca
- Tela por catraca: mensagem padrão e temporária no visor, acertar relógio, reiniciar a
  conexão, liberar manualmente (com motivo, registrado), bip, relés — só o que estiver
  documentado; o resto aparece desabilitado com o selo "aguardando confirmação da
  Topdata" e o teste de bancada correspondente escrito no docs/21.
- Relógio da catraca acertado ao conectar e a cada hora; alerta se divergir > 30 s.
- Aceite: testes contra o simulador para cada comando; auditoria (quem, quando, o quê);
  itens novos na tabela da bancada.

Fase 5 — Alertas e notificações
- Catálogo de alertas (docs/24 §1) implementado no serviço, com faixa, som, notificação
  do Windows, bandeja e histórico com "ciente".
- Aceite: teste que provoca cada alerta no modo simulação (derrubar worker, parar
  sincronização, encher fila, relógio divergente, taxa de negativas) e confere a
  mensagem; nenhum alerta com número de cartão completo.

Fase 6 — Prestação de contas
- Relatórios definidos pelo analista, com filtros de data e hora, corte fechado com
  hash, exportação PDF e CSV.
- Aceite: testes com base sintética conferindo cada total contra uma contagem
  independente em SQL; corte reproduzível (mesma entrada, mesmo hash).

## Como relatar cada fase

Ao fim de cada fase: o que mudou, os testes executados e os resultados (números), as
capturas de tela, as limitações conhecidas, o link do Setup novo e o próximo teste que
o usuário precisa fazer no PC ou na bancada. Pergunte ao usuário só o que for decisão
dele (listado em docs/24 §2), e siga com o resto.
```
