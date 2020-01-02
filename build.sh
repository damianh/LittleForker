#!/usr/bin/env bash

docker build \
 -f build.dockerfile \
 --tag littleforker-build .

docker run --rm --name littleforker-build \
 -v $PWD:/repo \
 -w /repo \
 -e FEEDZ_LITTLEFORKER_API_KEY=$FEEDZ_LITTLEFORKER_API_KEY \
 littleforker-build \
 dotnet run -p build/build.csproj -c Release -- "$@"