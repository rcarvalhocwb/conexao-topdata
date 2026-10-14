# 57 — Configuração do evento: reorganização, carga inicial e o que falta

> Pedido do dono do produto em 14/10/2026: "os cartões já validados devem constar na instalação
> inicial; quanto ao que falta construir, pode executar e já faça a reorganização proposta".
> Este documento é o plano e o registro do que foi feito. Situação de cada item na §7.

## 1. O problema

A tela "Configurações do evento" tinha cinco campos, quase todos padrões da catraca (mensagem,
tempo de liberação, leitor da urna, tipo de leitor em número cru, espera da nuvem). Nada do
**evento** em si: nome, dias, horário, regras dos cartões da bilheteria, tipos de entrada. E
o que existia em outro lugar (configuração por catraca, provedores, chaves técnicas) não dizia
onde estava. Para quem precisa achar tudo sempre no mesmo lugar, isso é o pior caso.

## 2. A tela nova: seções fixas, sempre na mesma ordem

| # | Seção | O que tem | Salva |
|---|---|---|---|
| 1 | **Evento** | Nome, início e fim (horário de Brasília), hora de corte do dia de operação, "recusar ingressos fora do período" | Botão próprio |
| 2 | **Tipos de entrada** | INTEIRA, MEIA, SOCIAL… com nome, ordem, ativo e quantos cartões cada um tem | Linha a linha |
| 3 | **Cartões da bilheteria** | Intervalo entre usos do mesmo cartão, "só na urna", quantos cartões há e de onde vieram | Botão próprio |
| 4 | **Padrão das catracas** | Os campos de antes, com o tipo de leitor em lista com nome; "Aplicar agora nas catracas" | Botão próprio |
| 5 | **Contingência** | Desabilitada, com o motivo (decisões D3, D4, D8 e bancada) | — |
| 6 | **Alertas** | Desabilitada, com o motivo (limites ainda não definidos) | — |
| 7 | **Segurança da catraca** | Desabilitada, com o motivo (cartão master e WebServer: T13, T32) | — |
| 8 | **Avançado** | As chaves técnicas, só leitura, com o ensaio que libera cada uma | — |

Regras que valem para todas:

- **Cada seção salva sozinha.** Salvar o nome do evento não aplica nada às catracas.
- **O autor é o usuário do login** (ADR-0026). As seções novas não pedem "Seu nome".
- **Bloqueado aparece desabilitado, com o motivo**, como na Configuração da catraca. Nada some.
- O que é **por catraca** (tipo de leitor, urna, tempo, mensagem, sentido) continua na
  Configuração da catraca, que vale mais que o padrão do evento; a seção 4 diz isso.

## 3. Carga inicial dos cartões já validados

Os três arquivos exportados do sistema anterior (cartões, cartões autorizados, autorizações da
catraca) são o mesmo conjunto: 2.243 cartões, INTEIRA 1.038, MEIA 603, SOCIAL 602, 1 inativo.

**Os números não entram no repositório nem no instalador.** Os dois são públicos, e o número
de um cartão abre a catraca. Em vez disso:

1. O instalador leva o script `carregar-cartoes.ps1`.
2. Quem instala roda, como administrador:
   `& "C:\Program Files\Rayzer\XAcess\carregar-cartoes.ps1" -Arquivo "<pasta com os .csv>"`
3. O script copia os arquivos para `%ProgramData%\ConexaoTopdata\carga-inicial` (só
   administradores e o serviço enxergam) e reinicia o serviço.
4. O serviço carrega ao subir (`CargaInicialDeCartoes`), move os arquivos para
   `carga-inicial\importados` e grava o resumo no registro, sem número completo.

Regras da carga (as mesmas da sincronização com o painel na nuvem, `FonteDeCartoesDoPainel`):

| Regra | Por quê |
|---|---|
| Provedor `bilheteria-local`, reutilizável, 4 min entre usos, só na urna | Os mesmos padrões da nuvem; se a nuvem for ligada depois, é o mesmo provedor |
| Sem limite de usos | O controle do cartão da bilheteria é físico: a urna recolhe |
| Número como veio, com os zeros (ADR-0008) | 31 números só diferem nos zeros à esquerda; tirar os zeros juntaria cartões diferentes |
| Tipo do ingresso (`tipo_ingresso`) vale mais que o do plástico (`tipo_cartao`) | 5 cartões são "AUTO" no arquivo de cartões e INTEIRA nos autorizados |
| Inativo em qualquer arquivo entra desativado | O lado seguro |
| O titular não é lido | A base de ingressos não guarda dado pessoal |
| Mesmo conjunto de arquivos não carrega duas vezes | O lote fica em `import_batch`, com a impressão SHA-256 dos arquivos |

Ensaio com os arquivos reais, numa base descartável fora do repositório: 2.243 cartões, 0
problemas, um cartão válido liberou na urna, a segunda carga não mudou nada.

**A confirmar na bancada:** se a urna entrega os números com 12 e 14 dígitos, como estão nos
arquivos. Se entregar diferente, muda o perfil de leitura do provedor e a carga se repete.

## 4. Evento: período e hora de corte

- Nome, início, fim e hora de corte ficam em `edge_setting` (`evento.*`), com quem mudou.
- **Recusar fora do período** (desligado por padrão): com ele ligado, **ingresso e cartão da
  bilheteria** lidos antes do início ou depois do fim são negados com o motivo próprio
  "fora do período do evento" (`EVENTO_FORA_DO_PERIODO`). Pessoas credenciadas não são
  afetadas: têm os horários delas (docs/43), e a equipe trabalha antes e depois.
- A hora de corte define o "dia de operação" (E9): a Prestação de contas abre no dia de
  operação de hoje, do corte de hoje ao corte de amanhã.

## 5. Ingressos: tipos e cartões da bilheteria

- **Tipos de entrada:** a tabela já existia (migração 011, B.1) e o tipo desativado já negava
  (B.2); faltavam a tela e o caminho que grava (B.6/B.7). O código não muda depois de criado:
  é ele que está gravado em cada cartão.
- **Cartões da bilheteria sem nuvem:** intervalo entre usos e "só na urna" passam a ser
  ajustáveis no painel. **Com a nuvem ligada, a nuvem manda** (ADR-0025): a seção mostra os
  valores e diz que vêm da nuvem, porque a sincronização os regrava a cada partida.

## 6. O que fica para depois, e de quem depende

| Item | Depende de |
|---|---|
| Setor por catraca (qual catraca aceita qual setor) | Decisão: o evento setoriza? Os arquivos de hoje não têm setor |
| Lotação máxima e ocupação | Contagem de saída: a catraca hoje só conta entrada |
| Contingência (lista na catraca, troca automática para off-line) | D3, D4, D8 e bancada (docs/34 §9) |
| Mensagens de apresentação, off-line e de negação | Topdata (NOVO-INT-MSG-03/04/05) |
| Cartão master e WebServer desligado | Bancada (T13, T32) |
| Limites dos alertas e forma de aviso | Definição com a operação (Fase 5, docs/29) |
| Venda no balcão e bloqueio de cartão pela tela | Próxima entrega (a operação existe, falta a tela) |
| Desfazer uma carga de cartões | Próxima entrega (o lote já fica registrado) |

## 7. Situação

| Item | Situação |
|---|---|
| Carga inicial dos cartões (§3) | Feito: `CargaInicialDeCartoes`, `carregar-cartoes.ps1`, testes (`CargaInicialDeCartoesTests`) e ensaio com os arquivos reais fora do repositório |
| Plano (este documento) | Feito |
| Evento (§4): nome, período, hora de corte, recusa fora do período | Feito: `edge_setting` `evento.*`, motivo `EVENTO_FORA_DO_PERIODO` na decisão, Prestação de contas no dia de operação (`ConfiguracoesDoEventoTests`) |
| Tipos de entrada (§5) | Feito: criar, renomear, ordenar e desativar pela tela |
| Cartões da bilheteria (§5) | Feito: intervalo e "só na urna" pela tela sem nuvem; com nuvem, só leitura |
| Tela reorganizada (§2) | Feito: seis seções numeradas, cada uma com o próprio salvar; bloqueadas com o motivo; avançado só leitura |
| "Seu nome" | Preenchido com o usuário do login nas Configurações e na Configuração da catraca; as seções novas usam o login direto |
| Teste com a catraca | Pendente: depende da bancada (formato do número lido na urna) |
| §6 inteiro | Pendente, com o dono de cada item na tabela |
