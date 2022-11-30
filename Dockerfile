FROM mcr.microsoft.com/dotnet/sdk as build

WORKDIR /app

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV POWERSHELL_TELEMETRY_OPTOUT=1

RUN apt-get update && \
    apt-get -y upgrade

COPY . .

RUN dotnet publish -c Release -r linux-x64 -p:PublishSingleFile=true --self-contained


FROM mcr.microsoft.com/dotnet/runtime as runtime

WORKDIR /app

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV POWERSHELL_TELEMETRY_OPTOUT=1

COPY --from=build /app/bin/Release/*/*/publish/* /app/

ENTRYPOINT ["/app/alerter"]
