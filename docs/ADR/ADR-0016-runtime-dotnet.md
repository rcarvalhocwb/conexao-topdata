# ADR-0016 — Runtime .NET alvo

**Status:** Proposto — decisão do cliente (N1) · **Data:** 2026-09-22

## Contexto

O briefing recomenda **.NET 8**. O suporte oficial do .NET 8 (LTS) **termina em novembro
de 2026** — cerca de dois meses após a data deste documento. Um produto que está apenas
começando nasceria fora de suporte antes do primeiro evento grande, o que tem
consequências em correção de segurança e em auditoria de cliente corporativo.

## Decisão proposta

Alvo **.NET 10 (LTS)**, com suporte até novembro de 2028. Os recursos que a arquitetura
usa (named pipes no Kestrel, RID `win-x86`, WPF) estão disponíveis.

Voltar para .NET 8 é uma alteração de uma linha de `TargetFramework` por projeto,
enquanto não houver uso de API introduzida depois — restrição que a Fase 1 respeita
deliberadamente, para manter a porta aberta.

## Consequências

- Suporte de segurança por toda a vida útil prevista do produto.
- Requer .NET 10 Runtime nas máquinas de campo — o instalador cuida disso.
- Se o cliente tiver padrão corporativo fixado em .NET 8, a troca é barata **até** a Fase 2;
  depois disso, cresce.

## Alternativas

- **.NET 8**, conforme pedido no briefing: aceitável se houver exigência corporativa, com
  o custo de suporte a vencer em nov/2026 registrado como risco aceito.
- **.NET Framework 4.8**: recusado. O briefing menciona .NET Framework 3.5+ como requisito
  **da DLL**, não do nosso processo; adotá-lo abriria mão de todo o ferramental moderno.
