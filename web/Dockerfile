# The SPA served by Caddy, which also proxies the API (deploy/Caddyfile, ADR-023). Built from the repository root:
# docker build -f web/Dockerfile .   (web/Dockerfile.dockerignore selects the files)
FROM node:22-alpine AS build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# Caddy rebuilt with a current Go toolchain and patched libraries: the official 2.11.7 binary (Go 1.26.8, x/net
# 0.59, go-jose 4.1.5) fails the deploy's image scan. Same Caddy version, no plugins.
FROM golang:1.27.3-alpine AS caddy
RUN go install github.com/caddyserver/xcaddy/cmd/xcaddy@v0.4.7 \
 && xcaddy build v2.11.7 --output /caddy \
      --replace golang.org/x/net=golang.org/x/net@v0.60.0 \
      --replace github.com/go-jose/go-jose/v4=github.com/go-jose/go-jose/v4@v4.1.6

FROM caddy:2.11.7-alpine
COPY --from=caddy /caddy /usr/bin/caddy
# Caddy runs as an unprivileged user and may still bind ports 80 and 443. apk upgrade picks up Alpine fixes released
# after the base image, which the deploy's image scan would otherwise fail on.
RUN apk upgrade --no-cache \
 && apk add --no-cache libcap \
 && setcap cap_net_bind_service=+ep /usr/bin/caddy \
 && adduser -D -H -u 10001 cesio \
 && mkdir -p /data /config && chown -R cesio:cesio /data /config
COPY deploy/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /web/dist /srv
USER cesio
