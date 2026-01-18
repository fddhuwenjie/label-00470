# 构建阶段
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src

# 复制 csproj 并还原依赖
COPY ["backend.csproj", "./"]
RUN dotnet restore "backend.csproj"

# 复制其余源码
COPY . .
WORKDIR "/src/."
RUN dotnet build "backend.csproj" -c Release -o /app/build

# 发布阶段
FROM build AS publish
RUN dotnet publish "backend.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 最终运行阶段
FROM mcr.microsoft.com/dotnet/runtime:6.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 暴露端口 (MQTT默认端口)
EXPOSE 1883

ENTRYPOINT ["dotnet", "backend.dll"]
