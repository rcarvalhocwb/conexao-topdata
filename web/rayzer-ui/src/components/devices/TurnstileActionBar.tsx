/** TurnstileActionBar — ações do cartão (Ver acessos, Diagnóstico…), separadas por uma divisória. */
export function TurnstileActionBar({ children }: { children: React.ReactNode }) {
  return <div className="mt-2.5 flex justify-end gap-1 border-t border-rayzer-divider pt-1.5">{children}</div>;
}
