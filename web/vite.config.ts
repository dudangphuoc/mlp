import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// Dev: chuyển gRPC-Web (/mlplatform.v1.*) và proxy notebook (/nb/, có WebSocket) tới api (endpoint HTTP/1.1). Production: Nginx làm việc này (20/09).
const api = process.env.MLP_API_WEB ?? "http://127.0.0.1:5081";
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: { port: 5173, proxy: { "/mlplatform.v1.": { target: api, changeOrigin: false }, "/nb/": { target: api, changeOrigin: false, ws: true }, "/install/": { target: api }, "/health/": { target: api } } },
});
