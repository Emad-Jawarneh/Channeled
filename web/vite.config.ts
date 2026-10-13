import { defineConfig, type ProxyOptions } from "vite";
import react from "@vitejs/plugin-react";

/** Large multipart uploads need no proxy idle timeout (default can drop mid-part). */
const apiProxy: ProxyOptions = {
  target: "http://127.0.0.1:5048",
  changeOrigin: true,
  timeout: 0,
  proxyTimeout: 0,
};

const proxy: Record<string, string | ProxyOptions> = {
  "/api": apiProxy,
  "/hubs": { target: "http://127.0.0.1:5048", ws: true, timeout: 0, proxyTimeout: 0 },
  "/health": "http://127.0.0.1:5048",
  "/openapi": "http://127.0.0.1:5048",
  "/metrics": "http://127.0.0.1:5048",
};

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy,
  },
  preview: {
    port: 5173,
    proxy,
  },
});
