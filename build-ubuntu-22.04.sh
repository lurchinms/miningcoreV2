#!/bin/bash

# dotnet 6 or higher is included in Ubuntu 22.04 and up

# install dev-dependencies
sudo add-apt-repository ppa:dotnet/backports
sudo apt install software-properties-common
sudo apt-get update; \
sudo apt-get -y install dotnet-sdk-9.0 git cmake clang ninja-build build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5 libgmp-dev libc++-dev zlib1g-dev

(cd src \

dotnet build)
