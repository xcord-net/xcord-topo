#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
IMAGE_NAME="xcord-topo"
CONTAINER_NAME="xcord-topo"
PORT="${PORT:-8090}"
BUILD_DIR="$SCRIPT_DIR/.build"

# ── Helpers ──────────────────────────────────────────────────────────

log()  { printf '\033[1;34m==> %s\033[0m\n' "$*"; }
ok()   { printf '\033[1;32m==> %s\033[0m\n' "$*"; }

cleanup() {
  log "Cleaning up..."
  docker rm -f "$CONTAINER_NAME" 2>/dev/null || true
  docker rmi -f "$IMAGE_NAME" 2>/dev/null || true
  rm -rf "$BUILD_DIR"
  ok "Done"
}

# ── Build ────────────────────────────────────────────────────────────

build_frontend() {
  log "Building frontend..."
  (cd "$SCRIPT_DIR/src/frontend" && npm ci --silent && npm run build -- --outDir "$BUILD_DIR/wwwroot")
}

build_backend() {
  log "Publishing backend..."
  dotnet publish "$SCRIPT_DIR/src/backend/src/XcordTopo.Api/XcordTopo.Api.csproj" \
    -c Release \
    -o "$BUILD_DIR/publish" \
    --verbosity quiet
}

build_image() {
  log "Building Docker image..."
  # Kill any container using our port (including leftover compose containers)
  docker ps -q --filter "publish=$PORT" | xargs -r docker rm -f 2>/dev/null || true
  docker rm -f "$CONTAINER_NAME" 2>/dev/null || true
  docker rmi -f "$IMAGE_NAME" 2>/dev/null || true
  docker build -t "$IMAGE_NAME" -f "$SCRIPT_DIR/docker/Dockerfile" "$SCRIPT_DIR"
}

build() {
  rm -rf "$BUILD_DIR"
  mkdir -p "$BUILD_DIR"
  build_frontend
  build_backend
  build_image
  ok "Build complete"
}

# ── Run (production mode) ────────────────────────────────────────────

run() {
  build

  docker rm -f "$CONTAINER_NAME" 2>/dev/null || true
  trap cleanup EXIT INT TERM

  ok "xcord-topo running at http://localhost:$PORT"

  docker run \
    --name "$CONTAINER_NAME" \
    -p "$PORT:80" \
    -e ASPNETCORE_ENVIRONMENT=Production \
    -e Data__BasePath=/data \
    "$IMAGE_NAME"
}

# ── Dev mode ─────────────────────────────────────────────────────────
#
# Backend in Docker container, frontend via Vite dev server with HMR.
# Frontend hot-reloads instantly. Backend changes: re-run ./run.sh dev.
#

dev() {
  rm -rf "$BUILD_DIR"
  mkdir -p "$BUILD_DIR/wwwroot"
  build_backend
  build_image

  docker rm -f "$CONTAINER_NAME" 2>/dev/null || true
  trap cleanup EXIT INT TERM

  docker run -d \
    --name "$CONTAINER_NAME" \
    -p "$PORT:80" \
    -e ASPNETCORE_ENVIRONMENT=Development \
    -e Data__BasePath=/data \
    "$IMAGE_NAME"

  ok "Backend running at http://localhost:$PORT"
  ok "Starting Vite dev server at http://localhost:3000 (proxying /api → :$PORT)"

  cd "$SCRIPT_DIR/src/frontend"
  VITE_API_TARGET="http://localhost:$PORT" npx vite --port 3000
}

# ── Entry point ──────────────────────────────────────────────────────

case "${1:-}" in
  dev)
    dev
    ;;
  *)
    run
    ;;
esac
