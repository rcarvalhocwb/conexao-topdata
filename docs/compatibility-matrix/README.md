# Matriz de compatibilidade — arquivos de dados

Separador: `;` (ponto e vírgula), codificação UTF-8, primeira linha é cabeçalho.
Escolhido para abrir direto no Excel em pt-BR sem assistente de importação.

| Arquivo | Conteúdo | Consumido por |
|---|---|---|
| `funcoes-easyinner.csv` | Uma linha por função do SDK; campos de assinatura vazios até a leitura do wrapper | `Contract.Tests.MatrizDeFuncoes` (Fase 1) |
| `origens-evento.csv` | Origens de evento declaradas + lacunas explícitas | `Contract.Tests.OrigensDeEvento` (Fase 1) |
| `modelos.csv` | Capacidades por modelo e status de homologação | `Contract.Tests.Homologacao` (Fase 1) |

## Valores especiais

- `LACUNA` — o campo existe, o valor é desconhecido. **Nunca** substituir por dedução.
- `NAO_ENSAIADO` — nenhum ensaio de bancada foi feito para este modelo.
  Enquanto estiver assim, o produto recusa aplicar configuração no equipamento fora do
  modo de manutenção.

## Regra de CI (a partir da Fase 1)

1. Toda chamada nativa no código precisa ter um `id` correspondente nesta matriz.
   Chamada sem registro → build quebra.
2. Toda origem de evento tratada em `switch` precisa existir em `origens-evento.csv`.
3. Preencher um campo `LACUNA` exige, no mesmo commit, a citação da fonte na coluna
   `fonte` e a troca do selo para `FONTE_PRIMARIA`.
4. Trocar `status_homologacao` para `HOMOLOGADO` exige o relatório de bancada assinado
   anexado em `docs/compatibility-matrix/relatorios/`.
