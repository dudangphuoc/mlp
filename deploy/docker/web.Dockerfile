# mlp-web — static SPA + gRPC-Web/HTTP proxy to the api (P0-10).
FROM node:24-alpine AS build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM nginx:1.29-alpine
COPY deploy/docker/web.nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist /usr/share/nginx/html
# Client SDKs (one file each), downloadable from the web app at /sdk/.
COPY sdk/python/mlplatform.py sdk/dotnet/MLPlatform.Client/MLPlatformClient.cs /usr/share/nginx/html/sdk/
