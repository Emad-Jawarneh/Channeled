#!/usr/bin/env bash
set -euo pipefail

# Native MinIO only — never Docker (AGENTS non-negotiable).

echo "=== MinIO setup (native binary) ==="

BIN_DIR="${CHANNELED_MINIO_BIN_DIR:-$HOME/.local/bin}"
DATA_DIR="${CHANNELED_MINIO_DATA:-$HOME/.local/share/channeled/minio}"
MINIO_BIN="${BIN_DIR}/minio"
ADDRESS="${CHANNELED_MINIO_ADDRESS:-127.0.0.1:9000}"
CONSOLE="${CHANNELED_MINIO_CONSOLE:-127.0.0.1:9001}"

mkdir -p "${BIN_DIR}" "${DATA_DIR}"

if [[ ! -x "${MINIO_BIN}" ]]; then
  echo "Downloading MinIO server binary..."
  curl -fsSL -o "${MINIO_BIN}" \
    "https://dl.min.io/server/minio/release/linux-amd64/minio"
  chmod +x "${MINIO_BIN}"
fi

if [[ -z "${MINIO_ROOT_USER:-}" || -z "${MINIO_ROOT_PASSWORD:-}" ]]; then
  echo "Set MINIO_ROOT_USER and MINIO_ROOT_PASSWORD (do not commit secrets)." >&2
  exit 1
fi

if curl -fsS "http://${ADDRESS}/minio/health/live" >/dev/null 2>&1; then
  echo "MinIO already healthy at http://${ADDRESS}"
  exit 0
fi

echo "Starting MinIO at ${ADDRESS} (console ${CONSOLE})..."
nohup env \
  MINIO_ROOT_USER="${MINIO_ROOT_USER}" \
  MINIO_ROOT_PASSWORD="${MINIO_ROOT_PASSWORD}" \
  "${MINIO_BIN}" server "${DATA_DIR}" \
    --address "${ADDRESS}" \
    --console-address "${CONSOLE}" \
  >"${DATA_DIR}/minio.log" 2>&1 &

for _ in $(seq 1 30); do
  if curl -fsS "http://${ADDRESS}/minio/health/live" >/dev/null 2>&1; then
    echo "MinIO is up."
    echo "Export for S3 integration tests:"
    echo "  export CHANNELED_S3_ENDPOINT=http://${ADDRESS}"
    echo "  export CHANNELED_S3_ACCESS_KEY=\$MINIO_ROOT_USER"
    echo "  export CHANNELED_S3_SECRET_KEY=\$MINIO_ROOT_PASSWORD"
    echo "  export CHANNELED_S3_BUCKET=channeled"
    exit 0
  fi
  sleep 1
done

echo "MinIO failed to become healthy. See ${DATA_DIR}/minio.log" >&2
exit 1
