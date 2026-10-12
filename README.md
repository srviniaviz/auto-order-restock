# Auto Order Restock

Automatically order stock for Supermarket Simulator when a product has an incomplete sales shelf or a labeled storage rack slot with space, and no reserve stock. The default is **one box per eligible product**, checked every **10 seconds**. Eligible products are grouped into a market cart order; if the cart reaches its limit, the rest can be ordered on later checks.

The mod also checks physical boxes in the scene, including delivered boxes on the street, before ordering again. A pending order is released after stock arrives or a two-minute retry window expires, so products can be ordered again after their last box is consumed. It uses the game's normal purchasing and delivery flow. Restockers or the player still move products to shelves.

## Install

1. Install [Tobey's BepInEx Pack for Supermarket Simulator](https://www.nexusmods.com/supermarketsimulator/mods/9) (BepInEx 6 IL2CPP).
2. Extract the release ZIP into the game's folder. The file should end up at `BepInEx/plugins/AutoOrderRestock/AutoOrderRestock.dll`.
3. Start the game and load a single-player save.

## Controls and settings

- **Ctrl+Alt+R:** enable or pause automatic orders. A short notice appears on screen.
- **Ctrl+Alt+L:** reload the config while the game is running.

The config file is created at `BepInEx/config/vinicius.autoorderrestock.cfg`. Defaults: `Enabled=true`, `BoxesPerProduct=1`, `CheckIntervalSeconds=10`, `DryRun=false`, `MaxOrdersPerSession=0` (no session limit). `BoxesPerProduct` accepts 1-10. Orders require an empty market cart, enough money for the whole order, an active product license, and an open ordering window. If funds are insufficient, the mod clears the cart it filled and leaves the player's cart alone.

The mod runs only in single-player. It does not edit save files. It was tested locally on Supermarket Simulator v1.7.1. Other mods that alter the cart or inventory can interfere.

## Build

Build with .NET 6 SDK and the game plus BepInEx interop assemblies installed:

    dotnet build AutoOrderRestock.csproj -c Release -p:GameDir="C:\path\to\Supermarket Simulator"

Source code is licensed under MIT. Game and BepInEx binaries are not included in this repository. Created with Codex assistance and validated locally by the player.

Portuguese instructions: [README.pt-BR.md](README.pt-BR.md).
