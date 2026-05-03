import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5006', changeOrigin: true },
      '/health': { target: 'http://localhost:5006', changeOrigin: true },
      '/ready': { target: 'http://localhost:5006', changeOrigin: true },
      '/swagger': { target: 'http://localhost:5006', changeOrigin: true },
    },
  },
})
