FROM quay.io/wildfly/wildfly:40.0.1.Final-jdk17

ARG PGJDBC_VERSION=42.7.13
ENV PGJDBC_VERSION=${PGJDBC_VERSION}

USER root

RUN mkdir -p "${JBOSS_HOME}/modules/org/postgresql/main" \
    && mkdir -p /opt/jboss/container

COPY --chown=jboss:root artifacts/postgresql-${PGJDBC_VERSION}.jar \
    ${JBOSS_HOME}/modules/org/postgresql/main/postgresql-${PGJDBC_VERSION}.jar
COPY --chown=jboss:root wildfly/module.xml \
    ${JBOSS_HOME}/modules/org/postgresql/main/module.xml
COPY --chown=jboss:root wildfly/entrypoint.sh /opt/jboss/container/entrypoint.sh
COPY --chown=jboss:root wildfly/healthcheck.sh /opt/jboss/container/healthcheck.sh

RUN chmod 0644 \
      "${JBOSS_HOME}/modules/org/postgresql/main/postgresql-${PGJDBC_VERSION}.jar" \
      "${JBOSS_HOME}/modules/org/postgresql/main/module.xml" \
    && chmod 0755 \
      /opt/jboss/container/entrypoint.sh \
      /opt/jboss/container/healthcheck.sh

USER jboss

COPY --chown=jboss:root wildfly/configure-wildfly.cli /tmp/configure-wildfly.cli
RUN "${JBOSS_HOME}/bin/jboss-cli.sh" --file=/tmp/configure-wildfly.cli \
    && rm -f /tmp/configure-wildfly.cli \
    && rm -rf "${JBOSS_HOME}/standalone/configuration/standalone_xml_history"

COPY --chown=jboss:root artifacts/api.war \
    ${JBOSS_HOME}/standalone/deployments/api.war
COPY --chown=jboss:root artifacts/servicio.war \
    ${JBOSS_HOME}/standalone/deployments/servicio.war

EXPOSE 8080 9990

ENTRYPOINT ["/opt/jboss/container/entrypoint.sh"]
