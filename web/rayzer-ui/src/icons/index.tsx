import type { SVGProps } from "react";

/**
 * Ícones Rayzer: traço de 1,75 px em grade 24, cantos arredondados — a mesma família dos
 * ícones do Windows usados no desktop. Decorativos por padrão (aria-hidden): o texto ao
 * lado é quem informa.
 */
export type IconProps = SVGProps<SVGSVGElement> & { size?: number };

function base({ size = 20, children, ...rest }: IconProps & { children: React.ReactNode }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      {...rest}
    >
      {children}
    </svg>
  );
}

export const IconHome = (p: IconProps) => base({ ...p, children: <><path d="M3 11l9-7 9 7" /><path d="M5 10v10h14V10" /></> });
export const IconTurnstile = (p: IconProps) =>
  base({ ...p, children: <><rect x="4" y="7" width="7" height="14" rx="1.5" /><rect x="3.5" y="3" width="8" height="4" rx="1" /><path d="M11 11l9-2M11 11l7 7M11 11l2-7" /></> });
export const IconList = (p: IconProps) => base({ ...p, children: <><path d="M9 6h12M9 12h12M9 18h12" /><path d="M4 6h.01M4 12h.01M4 18h.01" /></> });
export const IconSearch = (p: IconProps) => base({ ...p, children: <><circle cx="11" cy="11" r="7" /><path d="M20 20l-4-4" /></> });
export const IconSync = (p: IconProps) => base({ ...p, children: <><path d="M20 11a8 8 0 00-14.3-4.9L4 8" /><path d="M4 4v4h4" /><path d="M4 13a8 8 0 0014.3 4.9L20 16" /><path d="M20 20v-4h-4" /></> });
export const IconReport = (p: IconProps) => base({ ...p, children: <><path d="M14 3H6v18h12V7z" /><path d="M14 3v4h4M9 13h6M9 17h6" /></> });
export const IconSettings = (p: IconProps) =>
  base({ ...p, children: <><circle cx="12" cy="12" r="3" /><path d="M12 2v3M12 19v3M4.2 4.2l2.1 2.1M17.7 17.7l2.1 2.1M2 12h3M19 12h3M4.2 19.8l2.1-2.1M17.7 6.3l2.1-2.1" /></> });
export const IconActivity = (p: IconProps) => base({ ...p, children: <path d="M3 12h4l3-8 4 16 3-8h4" /> });
export const IconPlay = (p: IconProps) => base({ ...p, children: <path d="M7 4l13 8-13 8z" /> });
export const IconCloud = (p: IconProps) => base({ ...p, children: <path d="M7 18h10a4 4 0 00.5-8A6 6 0 006 9.5 4.3 4.3 0 007 18z" /> });
export const IconServer = (p: IconProps) => base({ ...p, children: <><rect x="4" y="4" width="16" height="6" rx="1.5" /><rect x="4" y="14" width="16" height="6" rx="1.5" /><path d="M8 7h.01M8 17h.01" /></> });
export const IconClock = (p: IconProps) => base({ ...p, children: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></> });
export const IconCheck = (p: IconProps) => base({ ...p, children: <path d="M5 12l5 5 9-10" /> });
export const IconClose = (p: IconProps) => base({ ...p, children: <path d="M6 6l12 12M18 6L6 18" /> });
export const IconUsers = (p: IconProps) => base({ ...p, children: <><circle cx="9" cy="8" r="3.5" /><path d="M3 20a6 6 0 0112 0" /><path d="M16 4.5a3.5 3.5 0 010 7M21 20a6 6 0 00-4-5.7" /></> });
export const IconHistory = (p: IconProps) => base({ ...p, children: <><path d="M3 12a9 9 0 103-6.7L3 8" /><path d="M3 3v5h5M12 7v5l3 2" /></> });
export const IconChevronDown = (p: IconProps) => base({ ...p, children: <path d="M6 9l6 6 6-6" /> });
export const IconCalendar = (p: IconProps) => base({ ...p, children: <><rect x="3.5" y="5" width="17" height="15" rx="2" /><path d="M3.5 10h17M8 3v4M16 3v4" /></> });
export const IconDownload = (p: IconProps) => base({ ...p, children: <><path d="M12 4v11M7 10l5 5 5-5" /><path d="M5 20h14" /></> });
export const IconInfo = (p: IconProps) => base({ ...p, children: <><circle cx="12" cy="12" r="9" /><path d="M12 11v6M12 7.5h.01" /></> });
export const IconWarning = (p: IconProps) => base({ ...p, children: <><path d="M12 3l10 18H2z" /><path d="M12 10v5M12 18h.01" /></> });
export const IconMenu = (p: IconProps) => base({ ...p, children: <path d="M4 7h16M4 12h16M4 17h16" /> });
export const IconSun = (p: IconProps) =>
  base({ ...p, children: <><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" /></> });
export const IconMoon = (p: IconProps) => base({ ...p, children: <path d="M20 14.5A8 8 0 019.5 4 8 8 0 1020 14.5z" /> });
export const IconFolder = (p: IconProps) => base({ ...p, children: <path d="M3 6h6l2 2h10v11H3z" /> });
export const IconEthernet = (p: IconProps) => base({ ...p, children: <><rect x="4" y="6" width="16" height="12" rx="1.5" /><path d="M8 18v-4h8v4M10 14v-2M14 14v-2" /></> });
export const IconChip = (p: IconProps) => base({ ...p, children: <><rect x="6" y="6" width="12" height="12" rx="1.5" /><path d="M9 2v4M15 2v4M9 18v4M15 18v4M2 9h4M2 15h4M18 9h4M18 15h4" /></> });
export const IconMore = (p: IconProps) => base({ ...p, children: <path d="M12 6h.01M12 12h.01M12 18h.01" /> });
