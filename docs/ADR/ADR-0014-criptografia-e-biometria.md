# ADR-0014 — Criptografia em repouso e segregação de biometria

**Status:** Aceito · **Data:** 2026-09-22

## Contexto

O sistema guarda credenciais, dados pessoais e, se o módulo estiver no escopo, templates
biométricos — dado pessoal sensível sob a LGPD. Uma máquina de evento é fisicamente
acessível, às vezes fica em sala improvisada, e pode ser levada embora.

## Decisão

- Segredos (tokens, chaves de conector, certificados) em **DPAPI escopo máquina** ou
  Windows Credential Manager. Nunca em arquivo de configuração, nunca em texto puro.
- Dados pessoais sensíveis cifrados em repouso.
- **Biometria em armazenamento segregado**, com chave distinta, acesso por permissão
  específica, e exportação sujeita a aprovação em duas pessoas.
- Backups cifrados. Logs com redação aplicada no serializador (ADR-0008).
- Retenção com prazo declarado e rotina de expurgo verificável.

## Consequências

- Perda física da máquina não entrega a base biométrica.
- Restauração de backup exige a chave — o runbook precisa cobrir isso, ou o backup é
  inútil no pior momento.
- DPAPI escopo máquina amarra o segredo àquela máquina: migração de host é procedimento
  documentado, não cópia de pasta.

## Alternativas recusadas

- **Cifrar o banco inteiro (SQLCipher).** Protege menos do que parece (a chave precisa
  estar disponível ao processo) e degrada desempenho no caminho crítico.
- **Sem cifra, confiando em BitLocker.** Depende de configuração do cliente, que não
  controlamos e que frequentemente não existe.
