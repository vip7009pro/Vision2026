FROM node:22-alpine AS builder

WORKDIR /app
COPY package*.json ./
RUN npm install

COPY . .
RUN npm run build

FROM node:22-alpine AS runner
WORKDIR /app

ENV NODE_ENV=production
ENV PORT=4000

COPY package*.json ./
RUN npm install --omit=dev

COPY --from=builder /app/dist ./dist
COPY --from=builder /app/src/public ./src/public
COPY --from=builder /app/.env.example ./.env

RUN mkdir -p /app/data /app/keys

EXPOSE 4000
VOLUME ["/app/data", "/app/keys"]

CMD ["node", "dist/server.js"]
