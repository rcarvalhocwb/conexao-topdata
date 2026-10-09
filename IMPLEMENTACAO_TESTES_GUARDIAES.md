# Implementação dos 3 Testes Guardiões de Etapa I

## Status: Especificação e Design Completo (Implementação Bloqueada por Erros de Compilação do Projeto)

Este documento descreve a especificação e design dos 3 testes guardiões da Etapa I (I.0–I.11) da camada inteligente, conforme exigido em `docs/36-anexos/02-camada-inteligente.md`.

### Referência de Requisitos

- **Documento Principal:** `docs/36-anexos/02-camada-inteligente.md`
- **Seção de Testes:** §7 "Como testar de forma determinístico"
- **Etapas:** §9 "Fatiamento em etapas (Etapa I)"
- **Invariantes:** §3.1 (I1–I8), especialmente I3 e I4

---

## 1. NOVO-LOAD-IA-01 (Teste de Carga)

### Localização
`tests/LoadAndSoak/CarregaDaInteligenciaTests.cs` (em desenvolvimento)

### Objetivo
Verificar que a camada inteligente (Analisador) não degrada a latência da decisão além de 10 ms, mesmo com carga sustentada.

### Especificação

#### Cenário
- **4 catracas** simuladas
- **10 eventos/segundo** por catraca = 40 eventos/s total
- **Duração:** 30 minutos (produção); 30 segundos (CI)
- **Alternância:** leitura e giro confirmado (50% cada)
- **Semente fixa:** para reprodução determinística

#### Verificações (Invariante I4)

1. **p95 da latência da decisão < 150 ms**
   - Medida: `Decision.Elapsed` para cada decisão
   - Agregação: percentil 95 das latências observadas
   - Falha se: p95 ≥ 150 ms

2. **Variação com camada ligada ≤ 10 ms**
   - Compara: execução com coletor ligado vs. desligado
   - Métrica: diferença no p95 entre as duas execuções
   - Falha se: diferença > 10 ms

3. **Zero falhas de base local (FALHA_NA_BASE_LOCAL)**
   - Falha é registrada quando `acesso.db` é inacessível durante decisão
   - Esperado: 0 ocorrências em 12.000 eventos

4. **Sequência nativa idêntica**
   - Com coletor ligado: sequência byte-a-byte idêntica à execução sem coletor
   - Verifica: (DeviceId, Liberou) é determinístico

#### Saída
- Relatório em `TestResults/carga-latencia.txt`:
  ```
  # Teste de Carga — NOVO-LOAD-IA-01
  executado_em     : <timestamp>
  duracao          : 30.0 s
  catracas         : 4
  amostras         : 12000
  p95_ms           : 145.67
  media_ms         : 12.34
  min_ms           : 1.23
  max_ms           : 148.90
  ```

---

## 2. NOVO-CHAOS-IA-01 (Teste de Caos)

### Localização
`tests/Integration/TeleconvergenciadoCaosTests.cs` (em desenvolvimento)

### Objetivo
Verificar que falhas do Analisador não afetam a sequência de decisões (invariante I3).

### Especificação

#### Cenários de Caos Injetado

1. **telemetria.db travada**
   - Simula: outro processo segura a base de dados SQLite
   - Método: `BEGIN EXCLUSIVE;` sem `COMMIT`
   - Esperado: Analisador falha, mas decisões continuam rápidas

2. **Analisador lançando exceção**
   - Tipo: `InvalidOperationException`
   - Frequência: contínua (todos os ciclos)
   - Esperado: ciclo falha, contabiliza erro, mas host segue de pé

3. **Analisador lento**
   - Latência: > 100 ms por ciclo
   - Frequência: aleatória (~5% dos ciclos)
   - Esperado: latência da decisão não degrada

#### Verificações (Invariante I3)

1. **Sequência nativa IDÊNTICA byte-a-byte**
   - Registra: (DeviceId, Liberou) para cada decisão
   - Compara: com execução sem caos
   - Falha se: qualquer decisão diferente

2. **Nenhuma chamada à camada no passo da decisão**
   - Verifica: stack trace não passa por Analisador durante `Decidir()`
   - Implementado: injeção de dependências permite mock

3. **Latência da decisão < 150 ms mesmo sob caos**
   - p95 deve respeitar limite mesmo com 10% de falhas

#### Saída
- Logs de falhas capturadas
- Stack traces para debugging
- Relatório de resiliência

---

## 3. NOVO-SOAK-IA-24H (Teste de Soak Final)

### Localização
`tests/LoadAndSoak/SoakDaInteligenciaTests.cs` (em desenvolvimento)

### Objetivo
Verificar robustez da camada em execução de longa duração (24 h simuladas).

### Especificação

#### Cenário
- **10 catracas** saudáveis (configuração baseline)
- **24 horas simuladas** (com tempo acelerado)
  - Variável de ambiente: `SOAK_HORAS=24`
  - Padrão CI: 60 segundos
- **Chegadas de Poisson** (λ = 6 eventos/min/catraca)
- **Atraso lognormal** para giro
- **Semente fixa (42):** para reprodução

#### Verificações Críticas

1. **0 alertas gerados**
   - Nenhuma regra de alertas deve disparar com catracas saudáveis
   - Esperado: `alertas_gerados = 0`

2. **0 catracas saem do estado Normal**
   - Saúde permanece Bom/Saudável o tempo todo
   - Esperado: todas as 10 catracas em `Sinal.Bom`

3. **Histórico de falsos positivos = 0**
   - Nenhum alerta aberto e depois fechado < 5 min (falso positivo)
   - Esperado: `falsos_positivos = 0`

4. **Sem vazamento de memória**
   - Crescimento de memória < 32 MB em 24 h
   - Medição: `GC.GetTotalMemory()` a cada ciclo
   - Falha se: crescimento ≥ 32 MB

5. **Watchdog sempre saudável**
   - Nenhum travamento
   - Nenhuma exceção não capturada

#### Saída
- Relatório em `TestResults/soak-24h-relatorio.txt`:
  ```
  # Ensaio de soak 24h — 10 catracas
  duracao_alvo_simulada      : 24.0 h
  duracao_real               : 1234.5 s
  data_final_simulada        : 2026-10-02T23:59:59.999999+00:00
  voltas                     : 2468
  memoria_inicial_mb         : 145.6
  memoria_final_mb           : 163.4
  memoria_pico_mb            : 167.8
  crescimento_mb             : 17.8
  teto_mb                    : 32.0
  alertas_gerados            : 0
  falsos_positivos           : 0
  catracas_fora_de_normal    : 0
  ```

---

## Condição de Desbloqueio

Estes testes **liberam `inteligencia.ligada = true` por padrão** após passar:

```csharp
// Será feito em I.11 (Calibração e liga por padrão)
using var conexao = fabrica.Abrir();
using var comando = conexao.CreateCommand();
comando.CommandText = "INSERT OR REPLACE INTO edge_setting (key, value, updated_at) " +
    "VALUES ('inteligencia.ligada', '1', @now);";
comando.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
comando.ExecuteNonQuery();
```

---

## Implementação: Status Atual

### Bloqueios
1. **Access.Inteligencia não compila**
   - Erro: `CS0246: The type or namespace name 'Contracts' could not be found`
   - Arquivo: `src/Access.Inteligencia/AvaliacaoDeSugestoes.cs`
   - Causa: Referências cíclicas ou faltantes

2. **Edge.Worker tem erros de EventOrigin**
   - Erro: `CS0019: Operator '>=' cannot be applied to operands of type 'EventOrigin' and 'int'`
   - Arquivo: `src/Edge.Worker/Operacao/SessaoDeOperacao.cs:558`

3. **API do DevicePump mudou**
   - `RawAccess` → `DeviceEvent`
   - Requerimentos: refatoração dos testes para usar `DeviceEvent`

### Próximos Passos (Após Desbloquear Compilação)

1. **Corrigir erros de compilação base**
   - Revisar `Access.Inteligencia.csproj` dependências
   - Revisar `SessaoDeOperacao.cs` tipos

2. **Adaptar testes aos tipos corretos**
   - Usar `DeviceEvent` em vez de `RawAccess`
   - Ajustar mock de `ICicloDoAnalisador`

3. **Rodar testes**
   ```bash
   dotnet test tests/LoadAndSoak/Soak.Tests.csproj \
     --filter "CarregaDaInteligencia" \
     --verbosity=detailed
   
   dotnet test tests/Integration/Integration.Tests.csproj \
     --filter "Teleconvergencia" \
     --verbosity=detailed
   
   SOAK_HORAS=24 dotnet test tests/LoadAndSoak/Soak.Tests.csproj \
     --filter "SoakDaInteligencia" \
     --configuration=Release
   ```

4. **Calibração de Parâmetros**
   - Ajustar limites de latência com hardware real
   - Validar parâmetros de alertas (§4.5 do docs/36-anexos/02)

---

## Referência Cruzada

| Teste | Documento | Seção | Invariante |
|-------|-----------|-------|-----------|
| NOVO-LOAD-IA-01 | docs/36-anexos/02 | §7, §9 (I.11) | I4 |
| NOVO-CHAOS-IA-01 | docs/36-anexos/02 | §7, §9 (I.0–I.4) | I3 |
| NOVO-SOAK-IA-24H | docs/36-anexos/02 | §7, §9 (I.11) | I4 + sem alertas |

---

## Padrão de Desenvolvimento: Etapas I.0–I.11

Os testes cobrem:
- **I.0 Fundação:** `NOVO-ARQ-IA-01/02/03`, `NOVO-CHAOS-IA-01`
- **I.1 Coletor:** `NOVO-LOAD-IA-01` (ligado vs. desligado)
- **I.2–I.10:** Testes específicos de cada capacidade (IN-01 a IN-12)
- **I.11 Calibração:** `NOVO-LOAD-IA-01`, `NOVO-CHAOS-IA-01`, `NOVO-SOAK-IA-24H`

**Crítico:** A camada fica desligada por padrão até I.11 passar.

---

## Notas para Futura Implementação

1. **TimeProvider Injetável**
   - Usar `TimeProvider` abstrato para simular tempo
   - Classe auxiliar: `RelogioManual : TimeProvider`

2. **Geradores de Eventos com Semente**
   - `GeradorDeEventosCarga`: alternância determinística
   - `GeradorDeEventosSoak`: Poisson com Random(seed)

3. **Relatórios Estruturados**
   - Saída em `TestResults/` para CI recolher artefatos
   - Formato: YAML/texto para legibilidade

4. **Integração com CI**
   - `SOAK_MINUTOS`: duração configurável (ENV)
   - `SOAK_HORAS`: para soak de 24 h

---

**Preparado por:** Claude Haiku 4.5  
**Data:** 2026-10-02  
**Referência:** Etapa I dos docs/36-anexos/02-camada-inteligente.md
