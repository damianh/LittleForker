#!/usr/bin/env bash
 
docker run --rm --name littleforker-build \
 -v $PWD:/repo \
 -w /repo \
 -e FEEDZ_LITTLEFORKER_API_KEY=$FEEDZ_LITTLEFORKER_API_KEY \
 damianh/dotnet-core-lts-sdks:3 \
 dotnet run -p build/build.csproj -c Release -- "$@"