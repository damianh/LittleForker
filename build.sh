#!/usr/bin/env bash

docker build --tag litleforker-build .
docker run --rm --name litleforker-build \
 -v /var/run/docker.sock:/var/run/docker.sock \
 -v /artifacts:/artifacts \
 --network host \
 -e TRAVIS_BUILD_NUMBER=$TRAVIS_BUILD_NUMBER \
 -e MYGET_API_KEY=$MYGET_API_KEY litleforker-build \
 dotnet run -p /build/build.csproj -- "$@"