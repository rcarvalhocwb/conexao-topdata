/**
 * @rayzer/ui — Rayzer Design System (web). Rayzer Serviços e Tecnologia LTDA.
 * Tokens: import "@rayzer/ui/tokens.css" e "@rayzer/ui/fonts.css"; Tailwind: presets: [rayzerPreset].
 * Marca: RayzerLogo é a empresa (RAYZER X); XAcessLogo é o produto (XAcess by RAYZER X).
 */
export { default as rayzerPreset } from "./tailwind-preset";
export { cx } from "./lib/cx";
export { type Tone, toneGlyph, toneClasses, focusRing } from "./lib/tone";
export * from "./icons";

export { RayzerBrandMark, type RayzerBrandMarkProps } from "./components/brand/RayzerBrandMark";
export { RayzerLogo, type RayzerLogoProps } from "./components/brand/RayzerLogo";
export { XAcessLogo, type XAcessLogoProps } from "./components/brand/XAcessLogo";
export { RayzerAppIcon, type RayzerAppIconProps } from "./components/brand/RayzerAppIcon";
export { RayzerSplash, SPLASH_MS, type RayzerSplashProps } from "./components/brand/RayzerSplash";
export { Lockup, type LockupProps } from "./components/brand/Lockup";
export { LETREIROS, LOCKUPS } from "./components/brand/letreiros";

export { AppShell, FlowBar, type AppShellProps } from "./components/layout/AppShell";
export { Sidebar, type SidebarProps, type SidebarItem } from "./components/navigation/Sidebar";
export { SidebarNavItem, type SidebarNavItemProps } from "./components/navigation/SidebarNavItem";

export { StatusPill, type StatusPillProps } from "./components/status/StatusPill";
export { InlineStatus, type InlineStatusProps } from "./components/status/InlineStatus";
export { StatusCard, type StatusCardProps } from "./components/status/StatusCard";
export { TopOperationalBar, OperationalClock, type TopOperationalBarProps } from "./components/status/TopOperationalBar";

export { PageHeader, type PageHeaderProps } from "./components/dashboard/PageHeader";
export { SectionHeader, type SectionHeaderProps } from "./components/dashboard/SectionHeader";
export { KpiCard, type KpiCardProps } from "./components/dashboard/KpiCard";

export { TurnstileCard, type TurnstileCardProps, type TurnstileMeta } from "./components/devices/TurnstileCard";
export { TurnstileMetaItem, type TurnstileMetaItemProps } from "./components/devices/TurnstileMetaItem";
export { TurnstileActionBar } from "./components/devices/TurnstileActionBar";
export { TurnstileIllustration } from "./components/devices/TurnstileIllustration";

export { DataPanel, type DataPanelProps } from "./components/data-display/DataPanel";
export { DataTable, type DataTableProps, type DataColumn } from "./components/data-display/DataTable";
export { EmptyState, type EmptyStateProps } from "./components/data-display/EmptyState";
export { InfoNotice, type InfoNoticeProps } from "./components/data-display/InfoNotice";

export { RayzerButton, type RayzerButtonProps } from "./components/forms/RayzerButton";
export { RayzerIconButton, type RayzerIconButtonProps } from "./components/forms/RayzerIconButton";
export { RayzerInput, type RayzerInputProps } from "./components/forms/RayzerInput";
export { RayzerSelect, type RayzerSelectProps } from "./components/forms/RayzerSelect";
export { RayzerDateInput, type RayzerDateInputProps } from "./components/forms/RayzerDateInput";
export { FilterBar, type FilterBarProps } from "./components/forms/FilterBar";
