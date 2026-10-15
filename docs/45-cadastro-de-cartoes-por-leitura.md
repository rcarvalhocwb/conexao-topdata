# Cadastro de cartões por leitura na urna e cartão recusado

Status: **APROVADO** nos testes automatizados (Linux). **PENDENTE** a validação visual no Windows (a view WPF
é compilada pelo CI Windows). **BLOQUEADO** o teste com catraca e urna física.

## O que o operador faz

1. **Lote por leitura (ex.: 300 inteiras):** abre uma sessão (tipo, nome do lote, usos por cartão). Coloca os
   cartões na urna. Cada cartão desconhecido lido na urna entra no lote com o tipo da sessão. **Ninguém passa
   durante a sessão**, e nenhum uso é consumido. Fecha a sessão e vê quantos entraram.
2. **Cartão recusado:** a tela "Cartões não reconhecidos" lista as leituras recusadas como desconhecidas, pela
   máscara. Escolhe uma, informa tipo e lote e cadastra. Depois disso o cartão passa normalmente.

## Decisões (respostas do dono do produto)

| Pergunta | Decisão |
|---|---|
| Bancada conectada à nuvem? | **Sem conexão.** Cadastro local liberado, como manda a ADR-0025. |
| Leitura de lote libera a pessoa? | **Não.** Só registra. |
| Quem cadastra? | **Papel com a permissão `cartoes.cadastrar`** (administrador e supervisor por padrão; portaria não). |

## O que foi feito

| Área | Mudança |
|---|---|
| Base (migração **026**) | `enrollment_session`: uma sessão aberta por vez (índice único). Papéis prontos ganham `cartoes.cadastrar`. A 024 e a 025 seguem reservadas, sem efeito nesta entrega. |
| Domínio | Motivo `CadastradoNoLote` (não liberado), código `CADASTRADO_NO_LOTE`, explicação própria e rótulo "Negado · …". |
| Decisão na urna (`RepositorioDeIngressos.TentarUsar`) | Na urna, com sessão aberta: um cartão **do lote** só registra e não consome; um **desconhecido** vira `ticket` do lote (`kind=cartao_bilheteria`, `source=manual`, `owner_of_fields=local`, `created_by`=operador, `batch_label`=lote). A tentativa fica ligada ao cartão, então **não conta** como QR desconhecido. Frente e pessoas não são afetadas. |
| Cadastro de recusado (`RepositorioDeIngressos.Cadastro.cs`) | Lista só com máscara (`CredentialValue.Mascarar`), agrupada por código. O cadastro recebe o id da tentativa; o código é lido do banco, nunca vai ao painel. |
| Contrato (proto) | 5 RPCs (`AbrirSessaoDeCadastro`, `FecharSessaoDeCadastro`, `ObterSessaoDeCadastro`, `ListarLeiturasDesconhecidas`, `CadastrarLeituraDesconhecida`). Nenhuma mensagem tem o código: os testes checam isso no texto da resposta inteira. Nomes sem a palavra proibida pela varredura do contrato. |
| Serviço | `EdgeControlService.Cadastro.cs`. Permissão `cartoes.cadastrar` no catálogo e no mapa de RPCs (fail-secure). Sem o repositório, o RPC responde `Unimplemented`. |
| Painel | `CartoesNaoReconhecidosViewModel` (sessão, lista mascarada, cadastrar a seleção), tela WPF `CartoesNaoReconhecidos.xaml`, item no menu com a permissão, ícone. |

## Evidência (Linux)

| Suíte | Resultado | Antes desta entrega |
|---|---|---|
| Unit | 932 aprovados, 0 falhas | 927 |
| Contract | 59 aprovados, 0 falhas | 59 |
| Integration | 802 aprovados, 0 falhas, 4 ignorados (já existiam) | 797 + 4 |

Testes novos: `CadastroPorLeituraTests` (sessão: uma por vez e provedor reutilizável; lote na urna não libera;
frente não cadastra; cartão do lote lido de novo não consome; fechar libera a passagem; lista mascarada;
cadastrar pelo id da tentativa; duplicado e dados inválidos), `CadastroDeCartoesPeloServicoTests` (RPCs; nenhuma
resposta traz o código; sem repositório, `Unimplemented`) e `CartoesNaoReconhecidosViewModelTests` (a tela
falando com o serviço pelo canal real).

Ajuste por mudança intencional: `TelasTests` (15 telas no menu, eram 14).

## Riscos e lacunas

- **PENDENTE:** a view WPF só é compilada no CI Windows; nenhum XAML foi renderizado.
- **BLOQUEADO:** teste com a urna e as catracas reais (leitura de cartões de verdade, tempo de resposta sob carga).
- **LGPD (decisão em aberto):** `ticket_use_attempt.qr_normalized` já guarda o código em claro para toda tentativa,
  inclusive a desconhecida (migração 003). O cadastro depende disso. Falta decidir se a tentativa desconhecida
  deve ser mascarada depois do cadastro ou em um prazo.
- **Catálogo de tipos (B.6/B.7) ainda não existe:** `categoria` é texto livre em maiúsculas (`INTEIRA`, `MEIA`).
  Um tipo sem cadastro não dá nome, cor nem ordem no painel.
- **Provedor por código de texto:** a tela pede o código do provedor reutilizável (padrão `balcao-local`). Falta
  listar os provedores disponíveis para escolher numa lista.
- **Desfazer lote:** não implementado. A B.4 prevê desfazer enquanto nenhum cartão do lote foi usado; ainda não
  há RPC para isso.
- **Sessão sem prazo:** a sessão fica aberta até alguém fechar. Um prazo automático não foi incluído.
- **Atribuição:** o operador vem do login (`Chamador`). Sem login (ferramentas e testes), grava `painel`.
