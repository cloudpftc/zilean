# Zilean Build & Deploy Makefile
# Builds .NET locally, creates Docker image, runs docker-compose

DOTNET := $(HOME)/.dotnet/dotnet
ARCH := x64
CONFIG := Release
OUT_DIR := out
IMAGE_NAME := zilean-local
IMAGE_TAG := latest
COMPOSE_FILE := docker-compose-test.yaml

.PHONY: all build docker-build up down restart logs clean help

## Default: build + docker-build + up
all: build docker-build up

## Build .NET projects and publish to ./out/
build:
	@echo "==> Cleaning previous build output..."
	@rm -rf $(OUT_DIR)
	@mkdir -p $(OUT_DIR)
	@echo "==> Restoring dependencies..."
	@$(DOTNET) restore -a $(ARCH)
	@echo "==> Publishing Zilean.ApiService..."
	@$(DOTNET) publish src/Zilean.ApiService/Zilean.ApiService.csproj \
		-c $(CONFIG) --no-restore -a $(ARCH) \
		-o $(OUT_DIR)
	@echo "==> Publishing Zilean.Scraper..."
	@$(DOTNET) publish src/Zilean.Scraper/Zilean.Scraper.csproj \
		-c $(CONFIG) --no-restore -a $(ARCH) \
		-o $(OUT_DIR)
	@echo "==> Build complete. Output in $(OUT_DIR)/"

## Build Docker image using pre-built output
docker-build:
	@echo "==> Building Docker image $(IMAGE_NAME):$(IMAGE_TAG)..."
	@docker build -f Dockerfile.runtime -t $(IMAGE_NAME):$(IMAGE_TAG) .

## Start docker-compose stack
up:
	@echo "==> Starting docker-compose stack..."
	@docker compose -f $(COMPOSE_FILE) up -d --build

## Stop docker-compose stack
down:
	@echo "==> Stopping docker-compose stack..."
	@docker compose -f $(COMPOSE_FILE) down

## Restart docker-compose stack
restart: down up

## Follow docker-compose logs
logs:
	@docker compose -f $(COMPOSE_FILE) logs -f

## Clean build output and Docker image
clean:
	@echo "==> Cleaning build output..."
	@rm -rf $(OUT_DIR)
	@echo "==> Removing Docker image..."
	@docker rmi $(IMAGE_NAME):$(IMAGE_TAG) 2>/dev/null || true
	@echo "==> Clean complete."

## Show this help message
help:
	@echo "Zilean Build & Deploy"
	@echo ""
	@echo "Usage:"
	@echo "  make all          Build + Docker + Up (default)"
	@echo "  make build        Build .NET projects to ./out/"
	@echo "  make docker-build Build Docker image from ./out/"
	@echo "  make up           Start docker-compose stack"
	@echo "  make down         Stop docker-compose stack"
	@echo "  make restart      Restart docker-compose stack"
	@echo "  make logs         Follow docker-compose logs"
	@echo "  make clean        Remove build output and Docker image"
	@echo "  make help         Show this help message"
