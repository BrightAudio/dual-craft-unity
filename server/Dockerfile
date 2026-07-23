FROM ubuntu:22.04

ARG SERVER_ARCHIVE_URL="https://github.com/BrightAudio/dual-craft-unity/releases/download/cloud-authority-v5-server-sync3/DualMonCloudServer-v5-sync3-linux-x64.tar.gz"
ARG SERVER_ARCHIVE_SHA256="c955fc54ae607c0319caaeacab4b71a98a9d473ec3961c6903b08c6de3dbce22"

RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        ca-certificates curl libgcc-s1 libstdc++6 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /dualmon
RUN curl --fail --location --silent --show-error "$SERVER_ARCHIVE_URL" -o /tmp/dualmon-server.tar.gz \
    && echo "$SERVER_ARCHIVE_SHA256  /tmp/dualmon-server.tar.gz" | sha256sum --check - \
    && tar -xzf /tmp/dualmon-server.tar.gz \
    && rm /tmp/dualmon-server.tar.gz
COPY start.sh ./start.sh
RUN chmod +x ./DualMonServer.x86_64 ./start.sh

ENTRYPOINT ["./start.sh"]
