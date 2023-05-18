@ECHO OFF

docker run --rm -it --name littleforker-build ^
  -v %cd%:/repo ^
  -w /repo ^
  -e FEEDZ_LITTLEFORKER_API_KEY=%FEEDZ_LITTLEFORKER_API_KEY% ^
  damianh/dotnet-sdks:7 ^
  dotnet run -p build/build.csproj -c Release -- %*

if errorlevel 1 (
   echo Docker build failed: Exit code is %errorlevel%
   exit /b %errorlevel%
)