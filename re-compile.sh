#!/bin/bash

(cd src/Miningcore && \
BUILDIR=${1:-../../build} && \
echo "Building into $BUILDIR" && \
dotnet publish -c Release --framework net9.0 -o $BUILDIR)
