FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/BabyNamePicker.slnx ./
COPY src/BabyNamePicker/BabyNamePicker.csproj BabyNamePicker/
COPY data/ /data/
RUN dotnet restore BabyNamePicker.slnx
COPY src/BabyNamePicker/ BabyNamePicker/
RUN dotnet publish BabyNamePicker/BabyNamePicker.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
RUN mkdir -p /data
VOLUME ["/data"]
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "BabyNamePicker.dll"]
