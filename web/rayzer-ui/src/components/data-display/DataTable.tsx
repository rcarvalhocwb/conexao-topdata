import { cx } from "../../lib/cx";

export interface DataColumn<T> {
  key: string;
  header: string;
  /** Largura CSS ("84px", "1fr" não se aplica a tabela; use width ou deixe flexível). */
  width?: string;
  align?: "left" | "right" | "center";
  /** Horas, códigos: dígitos de largura fixa. */
  mono?: boolean;
  render?: (row: T) => React.ReactNode;
}

/**
 * DataTable — cabeçalho em versalete discreto, linhas com divisória, hover, célula com
 * recuo de 12 px; `compact` reduz a altura da linha. Vazia, mostra `empty`.
 */
export interface DataTableProps<T> {
  columns: DataColumn<T>[];
  rows: T[];
  rowKey: (row: T, index: number) => string;
  compact?: boolean;
  empty?: React.ReactNode;
  caption?: string;
  className?: string;
}

export function DataTable<T>({ columns, rows, rowKey, compact = false, empty, caption, className }: DataTableProps<T>) {
  if (rows.length === 0 && empty) {
    return <div className="grid min-h-[240px] place-items-center">{empty}</div>;
  }

  const alinhamento = (a?: string) => (a === "right" ? "text-right" : a === "center" ? "text-center" : "text-left");

  return (
    <table className={cx("w-full border-collapse text-rayzer-small", className)}>
      {caption ? <caption className="sr-only">{caption}</caption> : null}
      <thead>
        <tr className="border-y border-rayzer-border bg-rayzer-sunken">
          {columns.map((c) => (
            <th
              key={c.key}
              scope="col"
              style={c.width ? { width: c.width } : undefined}
              className={cx("h-9 px-3 text-rayzer-caption font-semibold uppercase tracking-wide text-rayzer-text-secondary", alinhamento(c.align))}
            >
              {c.header}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {rows.map((row, i) => (
          <tr key={rowKey(row, i)} className="border-b border-rayzer-divider transition-colors duration-rayzer-fast hover:bg-rayzer-hover">
            {columns.map((c) => (
              <td
                key={c.key}
                className={cx("px-3 text-rayzer-text", compact ? "h-8" : "h-9", c.mono && "font-rayzer-mono tabular-nums", alinhamento(c.align))}
              >
                {c.render ? c.render(row) : String((row as Record<string, unknown>)[c.key] ?? "—")}
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
