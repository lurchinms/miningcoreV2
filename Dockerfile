FROM mcr.microsoft.com/dotnet/sdk:9.0-noble as BUILDER
WORKDIR /app
RUN apt-get update && \
    apt-get -y install cmake clang ninja-build build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5 libzmq3-dev golang-go libgmp-dev libc++-dev zlib1g-dev
COPY . .
WORKDIR /app/src/Miningcore
RUN dotnet publish -c Release --framework net9.0 -o ../../build

FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble
WORKDIR /app
RUN apt-get update && \
    apt-get install -y libzmq5 libzmq3-dev libsodium-dev curl && \
    apt-get clean
EXPOSE  4000-4090
COPY --from=BUILDER /app/build ./
CMD ["./Miningcore", "-c", "config.json" ]
