ArchiSteamFarm
===================

ASF is a C# application that allows you to farm steam cards using multiple steam accounts simultaneously. Unlike idle master which works only on one account at given time, requires steam client running in background, and launches additional processes imitiating "game playing" status, ASF doesn't require any steam client running in the background, doesn't launch any additional processes and is made to handle unlimited steam accounts at once. In addition to that, it's meant to be run on servers or other desktop-less machines, and features full Mono support, which makes it possible to launch on any Mono-supported operating system, such as Windows, Linux or OS X. ASF is based on, and possible, thanks to [SteamKit2](https://github.com/SteamRE/SteamKit).

ASF doesn't require and doesn't interfere in any way with Steam client. In addition to that, it no longer requires exclusive access to given account, which means that you can use your main account in Steam client, and use ASF for farming the same account at the same time. If you decide to launch a game, ASF will get disconnected, and resume farming once you finish playing your game, being as transparent as possible.

**Core features:**

- Automatically farm available games using any number of active accounts
- Automatically accept friend requests sent from master
- Automatically accept all trades coming from master
- Automatically accept all steam cd-keys sent via chat from master
- Possibility to choose the most efficient cards farming algorithm, based on given account
- SteamGuard / SteamParental / 2FA support
- Unique ASF 2FA mechanism allowing ASF to act as mobile authenticator (if needed)
- ASF update notifications
- Full Mono support, cross-OS compatibility

**Setting up:**

Detailed setting up instructions are available on **[our wiki](https://github.com/JustArchi/ArchiSteamFarm/wiki/Setting-up)**.

**Current Commands:**

Detailed documentation of all available commands is available on **[our wiki](https://github.com/JustArchi/ArchiSteamFarm/wiki/Commands)**.

> Commands can be executed via a private chat with your bot.
> Remember that bot accepts commands only from ```SteamMasterID```. That property can be configured in the config.

**Supported / Tested Operating-Systems:**

 - Windows 10 Professional/Enterprise Edition (Native)
 - Windows 8.1 Professional (Native)
 - Windows 7 Ultimate (Native)
 - Debian 9.0 Stretch (Mono)
 - Debian 8.1 Jessie (Mono)
 - OS X 10.11.1 (Mono)
 
However, any operating system [listed here](http://www.mono-project.com/docs/about-mono/supported-platforms/) should run ASF flawlessly.

**Need help or more info?**

Head over to our [wiki](https://github.com/JustArchi/ArchiSteamFarm/wiki) then.
