# SPDX-License-Identifier: EUPL-1.2
# Test corpus for .aetheus/security-rules/opengrep/aetheus-dockerfile.yml (opengrep scan --test).
# Never built: it only exists to prove each rule fires where it must and stays silent elsewhere.

# ruleid: aetheus.dockerfile.latest-base-image
FROM mcr.microsoft.com/dotnet/aspnet:latest AS runtime
# ok: aetheus.dockerfile.latest-base-image
FROM mcr.microsoft.com/dotnet/sdk:10.0.202@sha256:0a91cab78ba1057c0d72291e3b3abf9524282514542e29a8eff4daab6cc16a15 AS build

# ruleid: aetheus.dockerfile.add-remote-url
ADD https://example.org/tool.tar.gz /opt/tool.tar.gz
# ok: aetheus.dockerfile.add-remote-url
ADD --checksum=sha256:45bcd58440e397ed52c50e953ccf5948909ea77087c9186fc7d277216f62e319 https://example.org/tool.tar.gz /opt/
# ok: aetheus.dockerfile.add-remote-url
ADD ./local.tar.gz /opt/

# ruleid: aetheus.dockerfile.apt-without-no-install-recommends
RUN apt-get update && apt-get install -y curl
# ok: aetheus.dockerfile.apt-without-no-install-recommends
RUN apt-get update && apt-get install -y --no-install-recommends curl
# ok: aetheus.dockerfile.apt-without-no-install-recommends
RUN apt-get update && apt-get install -y \
      --no-install-recommends ca-certificates

# ruleid: aetheus.dockerfile.secret-in-arg-or-env
ARG NUGET_TOKEN=abc123
# ruleid: aetheus.dockerfile.secret-in-arg-or-env
ENV DB_PASSWORD=hunter2
# ok: aetheus.dockerfile.secret-in-arg-or-env
ARG NUGET_TOKEN
# ok: aetheus.dockerfile.secret-in-arg-or-env
ENV TOKEN_FILE_PATH_HINT=$HOME

# ok: aetheus.dockerfile.final-user-root
USER root
RUN chown app /app
USER app

RUN echo later
# ruleid: aetheus.dockerfile.final-user-root
USER root
CMD ["./start"]
