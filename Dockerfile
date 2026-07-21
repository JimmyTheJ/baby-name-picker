FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY BabyNamePicker.sln ./
COPY src/BabyNamePicker/BabyNamePicker.csproj src/BabyNamePicker/
COPY data/nicknames.json data/
COPY data/ssa-sample/ data/ssa-sample/
RUN dotnet restore src/BabyNamePicker/BabyNamePicker.csproj
COPY src/BabyNamePicker/ src/BabyNamePicker/
RUN dotnet publish src/BabyNamePicker/BabyNamePicker.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
RUN mkdir -p /data
VOLUME ["/data"]
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "BabyNamePicker.dll"]
