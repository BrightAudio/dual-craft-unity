FROM ubuntu:22.04

ARG SERVER_ARCHIVE_URL="https://github.com/BrightAudio/dual-craft-unity/releases/download/cloud-authority-v5-server-sync1/DualMonCloudServer-v5-sync1-linux-x64.tar.gz"
ARG SERVER_ARCHIVE_SHA256="84c9ef70e5e7422f8bb405b09af702476b5c395d7ea404e6720126cc84298178"

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
