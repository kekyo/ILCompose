@echo off

rem ILCompose - Compose partially implementation both .NET language and IL assembler.
rem Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
rem
rem Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0

echo.
echo "==========================================================="
echo "Build ILCompose"
echo.

rem git clean -xfd

dotnet build -p:Configuration=Release -p:Platform="Any CPU" -p:RestoreNoCache=True ILCompose.sln
dotnet pack -p:Configuration=Release -p:Platform="Any CPU" -o artifacts ILCompose.csproj
