@echo off
cd src\Miningcore
dotnet publish -c Release --framework net9.0 -o ../../build
