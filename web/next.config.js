/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  // Las llamadas al backend pasan por NEXT_PUBLIC_API_URL.
  // El backend ya tiene CORS habilitado para localhost:3000 en Development.
  experimental: {
    // typedRoutes: true,  // habilitar cuando todas las rutas estén estables
  },
};

module.exports = nextConfig;
