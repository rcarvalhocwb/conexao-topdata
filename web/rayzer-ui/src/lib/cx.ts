/** Junta classes, ignorando falsos. Sem dependência: o pacote não arrasta clsx. */
export function cx(...classes: Array<string | false | null | undefined>): string {
  return classes.filter(Boolean).join(" ");
}
