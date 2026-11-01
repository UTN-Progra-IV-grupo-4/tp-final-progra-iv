#!/bin/bash

# Script para importar el realm de Keycloak
# Este script espera a que Keycloak esté listo y luego importa el realm desde realm-export.json

set -e

KEYCLOAK_URL="${KEYCLOAK_URL:-http://localhost:8080}"
KEYCLOAK_ADMIN="${KEYCLOAK_ADMIN:-admin}"
KEYCLOAK_ADMIN_PASSWORD="${KEYCLOAK_ADMIN_PASSWORD:-admin}"
REALM_FILE="/opt/keycloak/data/import/realm-export.json"
REALM_NAME="seat-reservation"

echo "⏳ Esperando a que Keycloak esté listo..."

# Esperar a que Keycloak esté respondiendo
until curl -s -f "${KEYCLOAK_URL}/health/ready" > /dev/null 2>&1; do
    echo "   Keycloak no está listo aún, esperando..."
    sleep 5
done

echo "✅ Keycloak está listo"

# Esperar un poco más para asegurar que el admin API esté disponible
sleep 10

echo "🔐 Obteniendo token de administrador..."

# Obtener token de admin
ADMIN_TOKEN=$(curl -s -X POST "${KEYCLOAK_URL}/realms/master/protocol/openid-connect/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "username=${KEYCLOAK_ADMIN}" \
  -d "password=${KEYCLOAK_ADMIN_PASSWORD}" \
  -d "grant_type=password" \
  -d "client_id=admin" | jq -r '.access_token')

if [ -z "$ADMIN_TOKEN" ] || [ "$ADMIN_TOKEN" == "null" ]; then
    echo "❌ Error: No se pudo obtener el token de administrador"
    exit 1
fi

echo "✅ Token de administrador obtenido"

# Verificar si el realm ya existe
echo "🔍 Verificando si el realm '${REALM_NAME}' ya existe..."
REALM_EXISTS=$(curl -s -X GET "${KEYCLOAK_URL}/admin/realms/${REALM_NAME}" \
  -H "Authorization: Bearer ${ADMIN_TOKEN}" \
  -w "%{http_code}" -o /dev/null)

if [ "$REALM_EXISTS" == "200" ]; then
    echo "⚠️  El realm '${REALM_NAME}' ya existe. Eliminándolo para recrear..."
    curl -s -X DELETE "${KEYCLOAK_URL}/admin/realms/${REALM_NAME}" \
      -H "Authorization: Bearer ${ADMIN_TOKEN}"
    sleep 5
fi

echo "📥 Importando realm desde ${REALM_FILE}..."

# Importar el realm
IMPORT_RESULT=$(curl -s -X POST "${KEYCLOAK_URL}/admin/realms" \
  -H "Authorization: Bearer ${ADMIN_TOKEN}" \
  -H "Content-Type: application/json" \
  -d @"${REALM_FILE}" \
  -w "%{http_code}" -o /dev/null)

if [ "$IMPORT_RESULT" == "201" ]; then
    echo "✅ Realm '${REALM_NAME}' importado exitosamente"
else
    echo "❌ Error al importar realm. HTTP Status: ${IMPORT_RESULT}"
    exit 1
fi

echo "🎉 Configuración de Keycloak completada"
echo ""
echo "📋 Información del realm:"
echo "   - Realm: ${REALM_NAME}"
echo "   - URL: ${KEYCLOAK_URL}/realms/${REALM_NAME}"
echo "   - Console: ${KEYCLOAK_URL}/admin"
echo ""
echo "👤 Usuarios de prueba:"
echo "   - user1 / User123!"
echo "   - user2 / User123!"
echo "   - admin  / Admin123!"
echo ""
echo "🔑 Clients:"
echo "   - availability-web (public)"
echo "   - booking-service (confidential)"
echo "   - payment-service (confidential)"
