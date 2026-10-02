import react from "@vitejs/plugin-react";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";

// Demo do Rayzer UI: a prancha da marca e o painel ao vivo, nos dois temas.
export default defineConfig({
  root: fileURLToPath(new URL(".", import.meta.url)),
  base: "./",
  plugins: [react()],
  css: { postcss: fileURLToPath(new URL("./postcss.config.cjs", import.meta.url)) },
  build: { outDir: "dist", emptyOutDir: true },
});
