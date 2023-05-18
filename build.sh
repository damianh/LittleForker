#!/usr/bin/env bash
 
docker run --rm --name littleforker-build \
 -v $PWD:/repo \
 -w /repo \
 -e FEEDZ_LITTLEFORKER_API_KEY=$FEEDZ_LITTLEFORKER_API_KEY \
 damianh/dotnet-sdks:7 \
 dotnet run -p build/build.csproj -c Release -- "$@"