@ECHO OFF

docker build ^
 -f build.Dockerfile ^
 --tag littleforker-build .

if errorlevel 1 (
   echo Docker build failed: Exit code is %errorlevel%
   exit /b %errorlevel%
)

docker run --rm -it --name littleforker-build ^
  -v %cd%:/repo ^
  -w /repo ^
  -e FEEDZ_LITTLEFORKER_API_KEY=%FEEDZ_LITTLEFORKER_API_KEY% ^
  littleforker-build ^
  dotnet run -p build/build.csproj -c Release -- %*

if errorlevel 1 (
   echo Docker build failed: Exit code is %errorlevel%
   exit /b %errorlevel%
)