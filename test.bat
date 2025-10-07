@echo off
setlocal
pushd nightreign-mapgen

echo ==== Running fixed set ====
dotnet run -- "..\data\pattern\pattern_000.json"
dotnet run -- "..\data\pattern\pattern_003.json"
dotnet run -- "..\data\pattern\pattern_016.json"
dotnet run -- "..\data\pattern\pattern_060.json"
dotnet run -- "..\data\pattern\pattern_090.json"
dotnet run -- "..\data\pattern\pattern_101.json"
dotnet run -- "..\data\pattern\pattern_145.json"
dotnet run -- "..\data\pattern\pattern_177.json"
dotnet run -- "..\data\pattern\pattern_179.json"
dotnet run -- "..\data\pattern\pattern_231.json"
dotnet run -- "..\data\pattern\pattern_259.json"
dotnet run -- "..\data\pattern\pattern_319.json"

popd
echo Done.
endlocal
