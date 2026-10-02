import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import type { Config } from "tailwindcss";
import rayzerPreset from "../src/tailwind-preset";

const aqui = dirname(fileURLToPath(import.meta.url));

export default {
  presets: [rayzerPreset],
  content: [join(aqui, "index.html"), join(aqui, "*.tsx"), join(aqui, "../src/**/*.{ts,tsx}")],
} satisfies Config;
