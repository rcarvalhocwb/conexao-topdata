# 09 — Plano de bancada

> Nenhum modelo vai a produção sem ensaio próprio. Documentação demonstra capacidade;
> só o ensaio comprova comportamento naquele firmware.

## Entrada necessária (hoje ausente — B1)

- `EasyInner.dll` e todas as DLLs auxiliares, na versão de distribuição oficial;
- wrapper `EasyInner.cs`, enumerações e máquina de estados;
- Manual de Integração SDK Inner Acesso;
- exemplos oficiais C# (on-line e completo) — **preservados somente leitura**, nunca
  copiados para produção;
- exemplos de biometria; SDK do Leitor Facial, se facial estiver no escopo;
- manuais do modelo e firmware de cada equipamento do parque.

## Bancada mínima

1 equipamento por modelo a homologar · switch gerenciável com VLAN e espelhamento de porta ·
host Windows x64 com o Edge instalado · fonte com corte controlado (ensaios de energia) ·
conjunto de cartões reais, incluindo **casos difíceis**: zeros à esquerda, comprimento
máximo, Facility Code, cartão danificado · analisador de rede (captura para diagnóstico).

## Protocolo de ensaio

Cada item registra: comando, parâmetros, retorno nativo, tempo, efeito físico observado,
evento recebido e evidência (captura de tela ou de rede).

| Bloco | Ensaios |
|---|---|
| **B-01 Ambiente** | DLL registrada; dependências presentes; processo x86 sobe; hash conferido |
| **B-02 Comunicação** | Abrir/fechar porta; testar conexão; reconexão após cabo removido; porta ocupada; firewall |
| **B-03 Identidade** | Firmware, modelo, versão biométrica; leitura de relógio e drift |
| **B-04 Configuração** | Envio atômico; leitura de volta e diff; rollback; **drift provocado pelo WebServer** |
| **B-05 Leitura** | Cada tipo de leitor; dígitos variáveis; zeros à esquerda; FC; cartão danificado |
| **B-06 Liberação** | Sentido A e B; invertido; dois sentidos; tempo de acionamento |
| **B-07 Confirmação** | Origem 5; **origem 6 presente ou ausente**; giro reverso; sem giro |
| **B-08 Urna** | Recolhimento; origem 7; origem 20; capacidade real; cartão preso; desistência |
| **B-09 Off-line** | Lista no limite; horários; queda para off-line; bilhetes; reconciliação |
| **B-10 Biometria** | Cadastro, identificação, ajustes, exclusão (se no escopo) |
| **B-11 Falhas** | Retorno 8 provocado; timeout; desconexão durante comando; energia cortada |
| **B-12 Desempenho** | **Tempo p95 por passagem** (insumo do dimensionamento) e latência por etapa |

## Relatório assinado

Um por modelo **e** firmware, arquivado em `docs/compatibility-matrix/relatorios/`:

```
Modelo · Firmware · Placa · Leitores · Opcionais
Data · Responsável técnico · Local
Resultados por bloco (B-01..B-12): OK | FALHA | NÃO APLICÁVEL | NÃO ENSAIADO
Funções confirmadas → atualizam funcoes-easyinner.csv (selo FONTE_PRIMARIA)
Capacidades confirmadas → atualizam modelos.csv
Origens de evento observadas, inclusive desconhecidas
Tempo p95 por passagem medido
Limitações encontradas
Assinatura
```

Só com esse relatório o modelo passa de `NAO_ENSAIADO` para `HOMOLOGADO` na matriz —
e só então o produto aceita configurá-lo fora do modo de manutenção
([ADR-0010](ADR/ADR-0010-capability-discovery.md)).

## Gravação para regressão

Toda sessão de bancada é **gravada** pelo adapter gravador e reproduzida em CI. Um defeito
observado uma vez em hardware vira teste automatizado permanente — a única forma de não
repetir o mesmo erro no evento seguinte.
