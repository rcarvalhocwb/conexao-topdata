# Implementação I.9 — Sugestões de Parametrização

Etapa I.9 do docs/36 (IN-07): análise e sugestão de mudanças em campos da catraca com base em evidência estatística.

## O que foi criado

### 1. Infraestrutura SQL

**`src/Access.Infrastructure.SQLite/MigracoesDaTelemetria/T008_suggestion.sql`**

Tabela `suggestion` em `telemetria.db`, com colunas:
- `id`: UUIDv7 único
- `session_id`: partida do serviço (nullable)
- `inner_number`: número da catraca (1–99)
- `field`: campo sugerido (TempoDoAcionamento1, TipoDeLeitor, MensagemPadrao)
- `current_value`: valor atual
- `suggested_value`: valor sugerido
- `evidence`: JSON com motivo, valor medido, amostra, referência
- `created_at`: ISO-8601 UTC
- `situation`: "aberta", "usada", "descartada"
- `used_by`: operador que usou/descartou
- `used_at`: quando foi usado/descartado

Índices para consulta rápida por catraca e situação.

### 2. Lógica de Análise

**`src/Access.Inteligencia/AvaliacaoDeSugestoes.cs`**

Três métodos de sugestão (funções puras, determinísticas):

#### P1: Tempo do Relé (`SugerirTempoRele`)
- Entrada: histórico de Δ (liberação → giro), tempo configurado
- Mínimo: 200 liberações
- Lógica:
  - Calcula p95(Δ)
  - Se p95 + 1 < tempo configurado E ≤ 2% giros tardios → sugere reduzir
  - Se ≥ 2% giros tardios → sugere aumentar 1 s
  - Garante faixa 1–50 s
- Saída: `SugestaoDeParametrizacao` ou nula

#### P2: Limpeza do Leitor (`SugerirLimpezaLeitor`)
- Entrada: concentração de problemas por leitor (leituras vazias + desconhecidos fora do perfil)
- Mínimo: 50 problemas total
- Lógica:
  - Detecta se > 50% dos problemas estão num só leitor
  - Recomenda limpeza/reposicionamento/confirmação de tipo
- Saída: `SugestaoDeParametrizacao` ou nula

#### P3: Display (`SugerirDisplay`)
- Entrada: negações por motivo, mensagem atual
- Mínimo: nenhum fixo (taxa 30% do total)
- Lógica:
  - Detecta se um motivo domina (> 30% por padrão)
  - Mapeia motivo para mensagem sugerida
  - Não sugere se já está configurada
- Saída: `SugestaoDeParametrizacao` ou nula

Serialização: `SerializarEvidencia()` / `DesserializarEvidencia()` para JSON.

### 3. Persistência

**`src/Access.Infrastructure.SQLite/CadernoDesugestoes.cs`**

Gerencia leitura/escrita de sugestões em `telemetria.db`:
- `Gravar(catraca, sugestao, sessao, agora)`: insere sugestão nova
- `ListarAbertas(catraca)`: lista sugestões abertas (RPC `ObterSugestoes`)
- `RegistrarDestino(id, situacao, operador, agora)`: marca como usada/descartada (RPC `RegistrarDestinoDaSugestao`)

### 4. Interface gRPC

**`src/Contracts/Protos/edge_control.proto`**

Duas RPCs novas:

```proto
rpc ObterSugestoes(ObterSugestoesRequest) returns (SugestoesDaCatraca);
rpc RegistrarDestinoDaSugestao(RegistrarDestinoDaSugestaoRequest) returns (RegistrarDestinoDaSugestaoResponse);
```

Mensagens:
- `ObterSugestoesRequest`: `inner` (número da catraca)
- `SugestoesDaCatraca`: list de `SugestaoDeParametrizacao`
- `SugestaoDeParametrizacao`: id, campo, valor_atual, valor_sugerido, evidencia_json, criada_em
- `RegistrarDestinoDaSugestaoRequest`: sugestao_id, situacao ("usada"|"descartada"), operador
- `RegistrarDestinoDaSugestaoResponse`: registrada (bool), problemas (list)

### 5. UI — ViewModel

**`src/Desktop.ViewModels/Parametrizacao.cs`**

Integração:
- Nova propriedade: `Sugestoes` (IReadOnlyList<SugestaoDeParametrizacao>)
- Novo comando: `CarregarSugestoes` (ComandoAssincrono)
- Método público: `UsarSugestao(sugestao)` — preenche o campo, não salva
- Método público: `DescartarSugestaoAsync(id)` — marca como descartada

**Fluxo:**
1. Abrir tela de Parametrização → carrega sugestões abertas (I.9: carrega sugestões abertas)
2. Chip "Sugestão" aparece para cada sugestão aberta
3. "Usar sugestão" → preenche campo (Invariante I7: só preenche, operador salva)
4. Operador revisa e clica "Salvar" (com nome)
5. Aplicar em dois passos (como sempre)

**Registrar uso:**
- `UsarSugestao()`: chama `RegistrarDestinoDaSugestaoAsync(..., "usada")` em background
- `DescartarSugestaoAsync()`: chama `RegistrarDestinoDaSugestaoAsync(..., "descartada")`

## Como integrar no Analisador

O Analisador (Etapa I.0–I.3) já existe. Quando C3 (ou outra etapa) quiser gerar sugestões, o fluxo é:

```csharp
// A cada 15 minutos:
var caderno = new CadernoDesugestoes(_fabrica);

// P1: tempo do relé
if (AvaliacaoDeSugestoes.SugerirTempoRele(...) is { } p1)
{
    caderno.Gravar(catraca, p1, sessao, agora);
}

// P2: leitor
if (AvaliacaoDeSugestoes.SugerirLimpezaLeitor(...) is { } p2)
{
    caderno.Gravar(catraca, p2, sessao, agora);
}

// P3: display
if (AvaliacaoDeSugestoes.SugerirDisplay(...) is { } p3)
{
    caderno.Gravar(catraca, p3, sessao, agora);
}
```

As sugestões são **novas** sempre (nunca regeneradas), e **abertas** (situation='aberta') até o operador decidir.

## Testes

**`tests/Unit/AvaliacaoDeSugestoesTests.cs`**

Cobre:
- P1 com lognormal: 200 deltas com mediana 2 s, relé 5 s → sugere 4 s (NOVO-SIM-SUG-01)
- P1 com giros tardios: ≥ 2% → aumenta 1 s
- P1 com poucos dados: < 200 → nula
- P2 com concentração: > 50% num leitor → sugere ação
- P2 distribuído: ≤ 50% → nula
- P2 com poucos dados: < 50 → nula
- P3 com motivo dominante: > 30% → texto sugerido
- P3 com motivo fraco: ≤ 30% → nula
- P3 com mensagem já configurada: → nula
- Serialização round-trip: JSON preserva dados

## Invariantes

- **I5 (determinístico):** mesmo input sempre dá mesmo output
- **I6 (sem código):** `evidence` em JSON não tem código, máscara nem nome de titular
- **I7 (ato com nome):** "usar sugestão" só preenche; salvar faz o ato com nome
- **T008 STRICT:** coluna `evidence` é TEXT; sem campo para código, máscara ou nome

## Pontos críticos

1. **Mínimos de amostra:** P1 ≥ 200, P2 ≥ 50, P3 nenhum (taxa)
2. **Faixa de tempo:** 1–50 s, regra 11 (< 8 s)
3. **"Usar sugestão" não salva:** só preenche; operador revisa e salva com nome
4. **Registrar uso é assíncrono:** não bloqueia a UI
5. **Sugestões abertas não se regeneram:** novos cálculos criam novas sugestões

## Próximos passos

- I.1: Coletor do worker (device_signal, health_minute, G-01 a G-14)
- I.3+: Analisador que gera agg_minute, alert
- C3+: Etapas que calculam dados (ritmo, ocupação, Δ, etc.)
- Teste NOVO-SIM-SUG-01: simulador com lognormal, Δ, giros tardios
- Teste NOVO-ARQ-IA-03: "usar sugestão" não grava sem "Salvar"

## Arquivos alterados/criados

Criados:
- `T008_suggestion.sql` — tabela de sugestões
- `AvaliacaoDeSugestoes.cs` — análise de 3 tipos de sugestão
- `CadernoDesugestoes.cs` — persistência em telemetria.db
- `AvaliacaoDeSugestoesTests.cs` — 10 testes unitários
- `IMPLEMENTACAO_I9.md` — este arquivo

Alterados:
- `edge_control.proto` — 2 RPCs novas + 4 mensagens
- `Parametrizacao.cs` — chip "Sugestão", método UsarSugestao(), carregamento ao abrir
