#!/bin/bash

(cd src && \
BUILDIR=${1:-../build} && \
echo "Building into $BUILDIR" && \
dotnet build -o $BUILDIR)
