# SPDX-License-Identifier: EUPL-1.2
# Disposable systemd host used by tests/mail-stack/run.sh to execute the real mail helpers rendered
# from deploy/agent-host-config by install-agent-linux.sh. Never used in production.
ARG BASE_IMAGE=debian:bookworm-slim
FROM ${BASE_IMAGE}
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update -qq \
 && apt-get install -y -qq --no-install-recommends systemd systemd-sysv ca-certificates procps iproute2 openssl \
    swaks libnet-ssleay-perl libio-socket-ssl-perl libauthen-sasl-perl >/dev/null \
 && rm -f /lib/systemd/system/multi-user.target.wants/getty*
# Safe image default, as for Dockerfile.vpssim: run.sh opts back into uid 0 because systemd as PID 1
# requires it, so a plain `docker run` cannot start this privileged host as root by accident.
RUN useradd --system --no-create-home --shell /usr/sbin/nologin mailstack-runtime
USER mailstack-runtime
STOPSIGNAL SIGRTMIN+3
CMD ["/sbin/init"]
