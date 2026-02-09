SOURCE_DIRECTORY := $(dir $(realpath $(lastword $(MAKEFILE_LIST))))
ARTIFACT_PATH := $(SOURCE_DIRECTORY)artifacts
DOCS_PATH := $(SOURCE_DIRECTORY)docs
CONFIGURATION ?= Release

clean:
	dotnet clean
	rm -rf $(ARTIFACT_PATH)/*
	rm -rf $(DOCS_PATH)/api

restore:
	dotnet tool restore
	dotnet restore

build: restore
	dotnet build --no-restore --configuration $(CONFIGURATION)

test: build
	dotnet test \
		--no-build \
		--configuration $(CONFIGURATION) \
		--filter '(Execution!=Manual)' \
		--blame \
		--blame-crash \
		--blame-hang-timeout 7m \
		--diag "$(ARTIFACT_PATH)/diag.txt" \
		--logger "trx" \
		--collect "XPlat Code Coverage" \
		--results-directory $(ARTIFACT_PATH)/testresults \
		-- \
		RunConfiguration.CollectSourceInformation=true

test-aot:
	dotnet publish tests/ModelContextProtocol.AotCompatibility.TestApp/ModelContextProtocol.AotCompatibility.TestApp.csproj --configuration $(CONFIGURATION) -o $(ARTIFACT_PATH)/aot-publish
	$(ARTIFACT_PATH)/aot-publish/ModelContextProtocol.AotCompatibility.TestApp

pack: restore
	dotnet pack --no-restore --configuration $(CONFIGURATION)

generate-docs: restore
	dotnet build -c Release
	dotnet docfx $(DOCS_PATH)/docfx.json --warningsAsErrors true

serve-docs: generate-docs
	dotnet docfx serve $(ARTIFACT_PATH)/_site --port 8080

.DEFAULT_GOAL := build
