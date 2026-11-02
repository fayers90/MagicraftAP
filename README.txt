# Magicraft Archipelago

### An Archipelago Multiworld Integration for Magicraft

Connect **Magicraft** to the [Archipelago](https://archipelago.gg/) multiworld randomizer and share progression, items, and challenges with players across different games.

![Platform](https://img.shields.io/badge/Platform-Windows-blue)
![Framework](https://img.shields.io/badge/BepInEx-5.4.23.6-purple)
![Game](https://img.shields.io/badge/Game-Magicraft-orange)
![Status](https://img.shields.io/badge/Status-In%20Development-yellow)

**[Archipelago](https://archipelago.gg/) • [Magicraft on Steam](https://store.steampowered.com/app/2103140/) • [Report an Issue](../../issues) • [Releases](../../releases)**

## About

**Magicraft Archipelago** is a community-developed integration that brings Magicraft into the Archipelago multiworld randomizer ecosystem.

Archipelago allows multiple games to participate in a shared randomized experience. Items obtained in one game can unlock progression in another, creating a cooperative multiworld adventure.

This project consists of:

- A **BepInEx plugin** that integrates Archipelago with Magicraft.
- A **Magicraft `.apworld`** for Archipelago world generation.
- An in-game connection interface for joining multiworld sessions.

**Development status:** This integration is actively being developed and tested. Features and compatibility may change between releases.

## Features

### Archipelago Connectivity

- Connect to an Archipelago multiworld server.
- Authenticate using your player slot.
- Send location checks to the server.
- Receive randomized items from other worlds.
- Access the connection interface using **F8**.

### Randomized Progression

The integration is being developed around several Magicraft systems:

- **Room Clears** — progression checks awarded through gameplay.
- **Treasure Chests** — Archipelago item and location integration.
- **Shops** — randomized Archipelago purchases within Magicraft's shop systems.
- **Relics** — integration with the game's relic reward and unlock systems.
- **Spells and Talents** — integration with Magicraft's progression and unlock mechanics.

Not all systems are necessarily complete or available in every release. Refer to the release notes for currently supported functionality.

## Installation

### Requirements

- A Windows installation of **Magicraft**.
- **BepInEx 5** (the project is developed against version 5.4.23.6).
- A compatible Archipelago multiworld session.
- The Magicraft `.apworld` matching your plugin release.

### Installing the Plugin

1. Download the latest compatible package from [GitHub Releases](../../releases).
2. Locate your Magicraft installation directory through Steam:
   - Right-click **Magicraft**.
   - Select **Properties → Installed Files → Browse**.
3. Extract the distribution package into the Magicraft installation directory, preserving its folder structure.
4. If prompted to merge existing folders, check that you are not replacing a different BepInEx installation or configuration.
5. Launch Magicraft.

The distribution package is intended to provide the files required for the supported installation method. See the individual release notes for any additional steps.

### Connecting to Archipelago

1. Launch Magicraft with the plugin installed.
2. Press **F8** to open the Archipelago connection window.
3. Enter your Archipelago server address, port, and player slot details.
4. Enter a password if required by your server.
5. Connect to your multiworld session.

Once connected, the plugin can exchange supported location checks and items with your Archipelago session.

## Archipelago World Setup

The repository includes `Magicraft.apworld`, which defines the Magicraft world for Archipelago generation.

Use the `.apworld` version corresponding to the plugin release you intend to play.

Your Archipelago host will need to install the world definition and generate a multiworld using compatible player options.

## Building from Source

The complete C# plugin source is available in this repository.

### Requirements

- .NET SDK compatible with the project configuration.
- BepInEx 5 development assemblies.
- The required Unity and Magicraft assemblies for compilation.

### Build

Clone the repository and open the project directory:

git clone https://github.com/GatorBloomStudios/MagicraftAP.git
cd MagicraftAP

Then build:

dotnet restore
dotnet build MagicraftAP.csproj -c Release

Depending on your local setup, you may need to update assembly reference paths in the project file.

**Note:** Game assemblies and other third-party dependencies are not necessarily included in the source repository. Obtain these from their appropriate sources.

## Repository Structure

MagicraftAP/
├── Plugin.cs                 # Plugin entry point
├── ArchipelagoClient.cs      # AP server integration
├── ArchipelagoCheckQueue.cs  # Location check handling
├── ArchipelagoItemState.cs   # Received item state
├── ArchipelagoShop.cs        # Shop integration
├── NPCUnlockChecks.cs        # NPC-related checks
├── RelicRestriction.cs       # Relic progression integration
├── MagicraftAP.csproj        # C# project
├── Magicraft.apworld         # Archipelago world definition
└── dist/                     # Distribution package

Additional source files handle diagnostics, game patches, progression, and other integration functionality.

## Reporting Bugs

This project modifies game behaviour and may be affected by Magicraft updates.

If you encounter an issue, please [open a GitHub issue](../../issues) and include:

- Your Magicraft version.
- Your MagicraftAP release version.
- A description of the problem.
- Steps to reproduce it.
- Relevant BepInEx logs, with passwords and private server details removed.

## Contributing

Contributions, bug reports, and suggestions are welcome.

If you want to contribute code, please open an issue or pull request describing the proposed change.

The project is being developed with the aim of providing a reliable Magicraft integration for the Archipelago community.

## Credits

**Developed by GatorBloom Studios

Special thanks to:

- The [Archipelago](https://archipelago.gg/) development community.
- The [BepInEx](https://github.com/BepInEx/BepInEx) project.
- The developers of Magicraft.

This is an unofficial community project and is not affiliated with or endorsed by the developers of Magicraft or Archipelago.

**Made by GatorBloom Studios**
