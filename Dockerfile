# One image for both process roles; the entrypoint argument selects "api" (default) or "worker".
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN dotnet restore src/Platform.Api/Platform.Api.csproj \
 && dotnet restore src/Platform.Worker/Platform.Worker.csproj
RUN dotnet publish src/Platform.Api/Platform.Api.csproj -c Release --no-restore -o /app/api \
 && dotnet publish src/Platform.Worker/Platform.Worker.csproj -c Release --no-restore -o /app/worker

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/api ./api
COPY --from=build /app/worker ./worker
COPY --chmod=755 docker/entrypoint.sh ./entrypoint.sh
USER app
EXPOSE 8080
ENTRYPOINT ["/app/entrypoint.sh"]
CMD ["api"]
