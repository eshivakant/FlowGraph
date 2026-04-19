import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/repos': { target: 'http://127.0.0.1:5250', changeOrigin: true },
      '/jobs': { target: 'http://127.0.0.1:5250', changeOrigin: true },
      '/graph': { target: 'http://127.0.0.1:5250', changeOrigin: true },
      '/healthz': { target: 'http://127.0.0.1:5250', changeOrigin: true },
    },
  },
})
