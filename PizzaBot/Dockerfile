FROM mcr.microsoft.com/dotnet/aspnet:6.0-focal-amd64 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

ENV DELETION_PASSPHRASE=""
ENV DATABASE_URL=""
ENV DATABASE_USERNAME=""
ENV DATABASE_PASSWD=""
ENV DATABASE_NAME=""
ENV ADMIN_PATH=""

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY PizzaBot.csproj .
RUN dotnet restore "PizzaBot.csproj"
# Copy everything
COPY . .
# Build and publish a release
RUN dotnet build "Pizzabot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Pizzabot.csproj" -c Release -o /app/publish


# Build runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "Pizzabot.dll"]
