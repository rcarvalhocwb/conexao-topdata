# ADR-0020 — A configuração enviada é sempre completa e explícita

**Status:** Aceito · **Data:** 2026-09-24 · **Substitui a mitigação do risco R-20**

## Contexto

A Topdata é explícita (`FONTE_PRIMARIA`, FAQ "Por que a catraca perde as configurações
feitas via WebServer?"):

> *"o software é sempre a fonte da verdade das configurações"* — e
> *"a função `EnviarConfiguracoes()` **sempre envia um conjunto de parâmetros ao
> equipamento**, mesmo que o desenvolvedor não tenha explicitamente setado esses valores.
> A DLL possui valores padrão, e caso o software não monte o buffer com os parâmetros
> desejados, **esses valores padrão serão enviados**, sobrescrevendo a configuração
> anterior."*

Consequência: **montar meia configuração não preserva o resto — sobrescreve com os
defaults da DLL.** O equipamento aceita, retorna 0, e a instalação fica errada. É um modo
de falha silencioso, em produção, sem erro nenhum.

Não há como manter permanentemente uma configuração feita via WebServer: assim que o
equipamento entra em on-line via SDK, ele passa a seguir **somente** o que o software
envia.

## Decisão

1. Existe **um** tipo `DeviceConfiguration` que descreve **todos** os parâmetros
   suportados. Não há configuração parcial no domínio.
2. A cada conexão ou reconexão, o worker monta e envia a **configuração completa**, com
   todos os campos explicitamente setados — nenhum campo é deixado ao default da DLL.
3. Um teste de contrato falha se um campo suportado pela matriz não for coberto pelo
   montador de configuração.
4. O WebServer do equipamento é declarado, na documentação e na interface, como
   **ferramenta de diagnóstico, não de configuração**. O assistente de comissionamento
   diz isso ao instalador com todas as letras.
5. A leitura de volta continua existindo, mas para **conferência e auditoria** — não como
   estratégia de preservação.

## Consequências

- Acaba a categoria inteira de defeitos "a catraca voltou ao padrão sozinha".
- Alterar um parâmetro exige reenviar tudo — combina naturalmente com o buffer global da
  DLL, que é limpo a cada `EnviarConfiguracoes` (ADR-0006).
- A janela de configuração fica maior, o que reforça configurar **antes** do evento, com
  progresso visível.
- O diff de configuração muda de significado: compara a configuração **desejada** com a
  **lida do equipamento**, para detectar intervenção manual — e não para decidir o que
  enviar (envia-se sempre tudo).

## Alternativas recusadas

- **Enviar só o que mudou.** Impossível: a DLL preenche o resto com defaults.
- **Deixar o WebServer como fonte para alguns campos.** A própria Topdata responde
  "**Não**" à pergunta se dá para manter permanentemente a configuração do WebServer.
