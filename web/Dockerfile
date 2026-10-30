# The SPA served by Caddy, which also proxies the API (deploy/Caddyfile, ADR-023). Built from the repository root:
# docker build -f web/Dockerfile .   (web/Dockerfile.dockerignore selects the files)
FROM node:22-alpine AS build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM caddy:2.8.4-alpine
# Caddy runs as an unprivileged user and may still bind ports 80 and 443.
RUN apk add --no-cache libcap \
 && setcap cap_net_bind_service=+ep /usr/bin/caddy \
 && adduser -D -H -u 10001 cesio \
 && mkdir -p /data /config && chown -R cesio:cesio /data /config
COPY deploy/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /web/dist /srv
USER cesio
