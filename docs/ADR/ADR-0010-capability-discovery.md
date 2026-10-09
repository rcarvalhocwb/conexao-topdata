# ADR-0010 — Descoberta de capacidade antes de habilitar recurso

**Status:** Aceito · **Data:** 2026-09-22

## Contexto

Capacidades variam por linha, placa, firmware, leitor e opcionais. Enviar uma configuração
que o equipamento não suporta pode falhar com código genérico, ser ignorada em silêncio,
ou — o pior caso — ser parcialmente aplicada.

## Decisão

Antes de qualquer configuração, o equipamento passa por `ReadingIdentity` e
`CheckingCompatibility`: modelo e firmware são lidos, comparados com
`docs/compatibility-matrix/modelos.csv` e registrados. Um recurso só é habilitado se
estiver marcado como suportado **e** o modelo estiver `HOMOLOGADO` naquela matriz.

Tudo que não estiver confirmado nasce **desabilitado**, atrás de uma feature flag, com um
teste de bancada associado. Equipamento `NAO_ENSAIADO` só aceita configuração em modo de
manutenção, com confirmação explícita do técnico.

## Consequências

- O produto é honesto por construção: não promete o que não foi ensaiado.
- Exige disciplina de atualizar a matriz a cada ensaio — por isso a matriz é código
  (CSV versionado, lido por teste), não uma planilha perdida em e-mail.
- A tela "Adicionar equipamento" mostra, ao final, a lista de recursos disponíveis
  **naquele** equipamento e o porquê de cada indisponibilidade.

## Alternativas recusadas

- **Tentar e tratar o erro.** Configuração parcialmente aplicada não é detectável por
  código de retorno, e o efeito é físico.
- **Matriz estática em código.** Muda a cada firmware; precisa ser dado, não build.
