# Build context: src/Web. The generated API types (src/lib/api/schema.d.ts) are committed,
# so the image build does not need ../Api/openapi/Api.json.
FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build && npm prune --omit=dev

FROM node:24-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production HOST=0.0.0.0 PORT=8080
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist
COPY package.json otel.mjs ./
USER node
EXPOSE 8080
# otel.mjs: OpenTelemetry to OTEL_EXPORTER_OTLP_ENDPOINT and/or ConnectionStrings__seq; a no-op without either.
CMD ["node", "--import", "./otel.mjs", "./dist/server/entry.mjs"]
