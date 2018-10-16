docker build --tag litleforker-build .
docker run --rm --name litleforker-build ^
 -v /var/run/docker.sock:/var/run/docker.sock ^
 -v %cd%/artifacts:/artifacts ^
 --network host litleforker-build ^
 dotnet run -p /build/build.csproj -- %*