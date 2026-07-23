FROM ubuntu:22.04

RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        ca-certificates libgcc-s1 libstdc++6 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /dualmon
COPY build/ ./
COPY start.sh ./start.sh
RUN chmod +x ./DualMonServer.x86_64 ./start.sh

ENTRYPOINT ["./start.sh"]
