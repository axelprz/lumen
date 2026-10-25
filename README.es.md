# Lumen

**[Read in English](README.md)**

Lumen es un launcher gratuito y de código abierto para Windows que reúne todos tus juegos y apps en un solo lugar. Steam, Epic, GOG, Xbox / Microsoft Store, Ubisoft Connect, EA app y Battle.net aparecen en una única biblioteca —instalados o no— junto a las apps que elijas. Lanzás cualquier cosa en dos segundos, con mouse, atajo de teclado o joystick.

> **Estado:** desarrollo inicial. Todavía no hay versiones publicadas. Ver [el plan de la v1](docs/PLAN-v1.md).

## Qué hace (objetivos de la v1)

- **Una biblioteca para todas las tiendas.** Los juegos instalados se detectan localmente; los que tenés pero no instalaste aparecen en gris y abren el launcher original para instalarlos.
- **Tus apps, curadas.** Lumen encuentra las apps del menú Inicio y elegís cuáles entran a tu biblioteca. Sin basura.
- **Se ve bien.** Interfaz oscura con fondos y colores de acento que siguen el artwork del juego seleccionado (Steam CDN + SteamGridDB).
- **Acceso rápido.** Ícono en la bandeja y un atajo global para un buscador rápido.
- **Navegación con joystick** para el sillón.
- **Horas jugadas** registradas por Lumen, más tus horas existentes de Steam.
- **Rutinas** (opcional): un click abre o cierra un conjunto de apps, sitios y carpetas, por ejemplo "Trabajar". Son archivos JSON que podés sincronizar con una carpeta o compartir.
- **Actualizaciones dentro de la app** vía GitHub Releases. Instalador o zip portable.

## Principios de privacidad y seguridad

- Todo corre localmente. Sin servidores de Lumen, sin telemetría, sin publicidad.
- Los archivos de los launchers solo se **leen**, nunca se modifican.
- Los logins a cuentas son **opcionales**. Tokens y API keys se cifran con DPAPI de Windows y nunca salen de tu PC salvo para hablar con el servicio al que pertenecen.
- La v1 no carga plugins ni temas de terceros.
- Las rutinas importadas se muestran completas y hay que aprobarlas antes de poder ejecutarlas.

## Requisitos

- Windows 10 (22H2) o Windows 11, x64
- Opcional: una [API key de Steam](https://steamcommunity.com/dev/apikey) para ver juegos de Steam no instalados y horas
- Opcional: una [API key de SteamGridDB](https://www.steamgriddb.com/profile/preferences/api) para el artwork

## Compilar desde el código

```powershell
git clone https://github.com/axelprz/lumen
cd lumen
dotnet build
dotnet test
```

Requiere el SDK de .NET 10. Para abrir la app: `dotnet run --project src/Lumen.App` (solo Windows).

La estructura del proyecto está en [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Licencia

Copyright (C) 2026 Axel Ismael Perez (AxelPRZ).

Lumen es software libre publicado bajo la [GNU General Public License v3.0 o posterior](LICENSE). Ver [COPYRIGHT](COPYRIGHT).

Lumen no está afiliado a Valve, Epic Games, CD PROJEKT, Microsoft, Ubisoft, Electronic Arts, Blizzard ni SteamGridDB. Todas las marcas pertenecen a sus dueños.
