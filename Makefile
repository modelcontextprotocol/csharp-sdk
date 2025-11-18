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
		--no-progress \
		--configuration $(CONFIGURATION) \
		--filter-not-trait 'Execution=Manual' \
		--crashdump \
		--hangdump \
		--hangdump-timeout 7m \
		--coverage \
		--coverage-output-format cobertura \
		-p:_MTPResultsDirectory=$(ARTIFACT_PATH)/testresults \

pack: restore
	dotnet pack --no-restore --configuration $(CONFIGURATION)

generate-docs: restore
	dotnet build -c Release
	dotnet docfx $(DOCS_PATH)/docfx.json --warningsAsErrors true

serve-docs: generate-docs
	dotnet docfx serve $(ARTIFACT_PATH)/_site --port 8080

.DEFAULT_GOAL := build
