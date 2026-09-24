# ADR-0012 — WPF como camada de apresentação

**Status:** Aceito · **Data:** 2026-09-22

## Contexto

O briefing sugere WPF ou WinUI 3. O aplicativo é de operação crítica: precisa abrir
sempre, em máquina modesta, com acessibilidade real (WCAG, leitor de tela, teclado, alto
contraste), em 1366×768 e em telas touch.

## Decisão

**WPF**, com MVVM, camada de View fina e ViewModels sem dependência de framework gráfico
(testáveis sem UI).

## Consequências

- Acessibilidade via UI Automation é madura e bem suportada por leitores de tela.
- Sem dependência do Windows App SDK como runtime externo — menos peças para falhar numa
  instalação de campo, onde ninguém vai depurar instalador às 19h de um sábado.
- Visual moderno exige investimento em estilos; é trabalho conhecido e controlado.
- ViewModels testáveis mantêm a porta aberta para uma eventual migração de View.

## Alternativas recusadas

- **WinUI 3.** Visual mais atual, porém runtime externo, histórico de arestas em
  acessibilidade e em cenários de serviço/kiosk. Risco desnecessário para o benefício.
- **Avalonia.** Multiplataforma é irrelevante aqui (a DLL é Windows), e o ferramental de
  acessibilidade é menos maduro no Windows.
- **Web local + WebView2.** Adiciona um runtime de navegador e uma superfície de
  segurança inteira para resolver um problema que não temos.
