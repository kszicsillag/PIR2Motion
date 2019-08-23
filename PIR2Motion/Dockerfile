FROM mcr.microsoft.com/dotnet/core/runtime:2.2-stretch-slim-arm32v7 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:2.2-stretch AS build
WORKDIR /src
COPY ["PIR2Motion/PIR2Motion.csproj", "PIR2Motion/"]
RUN dotnet restore "PIR2Motion/PIR2Motion.csproj"
COPY . .
WORKDIR "/src/PIR2Motion"
RUN dotnet build "PIR2Motion.csproj" -c Release -o /app

FROM build AS publish
RUN dotnet publish "PIR2Motion.csproj" -c Release -o /app

FROM base AS final
RUN apt-get update && apt-get install -y gnupg wget unzip
RUN URL=http://downloads.rclone.org/v1.48.0/rclone-v1.48.0-linux-arm.zip ; \
  cd /tmp \
  && wget -q $URL \
  && unzip /tmp/rclone-v1.48.0-linux-arm.zip \
  && mv /tmp/rclone-*-linux-arm/rclone /usr/bin \
  && rm -r /tmp/rclone*

RUN echo "deb http://archive.raspbian.org/raspbian stretch main contrib non-free rpi firmware" >>  /etc/apt/sources.list \
	&& apt-key adv --batch --keyserver keyserver.ubuntu.com --recv-key 0x9165938D90FDDD2E \
	&& echo "deb http://archive.raspberrypi.org/debian stretch main ui" >>  /etc/apt/sources.list.d/raspi.list \
	&& apt-key adv --batch --keyserver keyserver.ubuntu.com --recv-key 0x82B129927FA3303E
RUN apt-get update && apt-get install -y --no-install-recommends \
		libraspberrypi-bin \		
	&& rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "PIR2Motion.dll"]