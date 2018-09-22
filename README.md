# ArchiSteamFarm

[![Build status (Windows)](https://img.shields.io/appveyor/ci/JustArchi/ArchiSteamFarm/master.svg?label=Windows&maxAge=600)](https://ci.appveyor.com/project/JustArchi/ArchiSteamFarm)
[![Build status (Unix)](https://img.shields.io/travis/com/JustArchiNET/ArchiSteamFarm/master.svg?label=Unix&maxAge=600)](https://travis-ci.com/JustArchiNET/ArchiSteamFarm)
[![Build status (Docker)](https://img.shields.io/docker/build/justarchi/archisteamfarm.svg?label=Docker&maxAge=600)](https://hub.docker.com/r/justarchi/archisteamfarm)
[![Github last commit date](https://img.shields.io/github/last-commit/JustArchiNET/ArchiSteamFarm.svg?label=Updated&maxAge=600)](https://github.com/JustArchiNET/ArchiSteamFarm/commits)
[![License](https://img.shields.io/github/license/JustArchiNET/ArchiSteamFarm.svg?label=License&maxAge=2592000)](https://github.com/JustArchiNET/ArchiSteamFarm/blob/master/LICENSE-2.0.txt)
[![Localization](https://d322cqt584bo4o.cloudfront.net/archisteamfarm/localized.svg)](https://crowdin.com/project/archisteamfarm)

[![ConfigGenerator status](https://img.shields.io/website-up-down-green-red/https/justarchinet.github.io/ASF-WebConfigGenerator.svg?label=ConfigGenerator&maxAge=3600)](https://justarchinet.github.io/ASF-WebConfigGenerator)
[![Statistics status](https://img.shields.io/website-up-down-green-red/https/asf.justarchi.net.svg?label=Statistics&maxAge=3600)](https://asf.justarchi.net)
[![Steam group](https://img.shields.io/badge/Steam-group-yellowgreen.svg)](https://steamcommunity.com/groups/archiasf)
[![Discord](https://img.shields.io/discord/267292556709068800.svg?label=Discord&maxAge=3600)](https://discord.gg/hSQgt8j)
[![Wiki](https://img.shields.io/badge/Read-wiki-cc5490.svg)](https://github.com/JustArchiNET/ArchiSteamFarm/wiki)

[![GitHub stable release version](https://img.shields.io/github/release/JustArchiNET/ArchiSteamFarm.svg?label=Stable&maxAge=600)](https://github.com/JustArchiNET/ArchiSteamFarm/releases/latest)
[![GitHub stable release date](https://img.shields.io/github/release-date/JustArchiNET/ArchiSteamFarm.svg?label=Released&maxAge=600)](https://github.com/JustArchiNET/ArchiSteamFarm/releases/latest)
[![Github stable release downloads](https://img.shields.io/github/downloads/JustArchiNET/ArchiSteamFarm/latest/total.svg?label=Downloads&maxAge=600)](https://github.com/JustArchiNET/ArchiSteamFarm/releases/latest)

[![GitHub experimental release version](https://img.shields.io/github/release/JustArchiNET/ArchiSteamFarm/all.svg?label=Experimental&maxAge=600)](https://github.com/JustArchiNET/ArchiSteamFarm/releases)
[![GitHub experimental release date](https://img.shields.io/github/release-date-pre/JustArchiNET/ArchiSteamFarm.svg?label=Released&maxAge=600)](https://github.com/JustArchiNET/ArchiSteamFarm/releases)
[![Github experimental release downloads](https://img.shields.io/github/downloads-pre/JustArchiNET/ArchiSteamFarm/latest/total.svg?label=Downloads&maxAge=600)](https://github.com/JustArchiNET/ArchiSteamFarm/releases)

[![Patreon support](https://img.shields.io/badge/Patreon-support-yellow.svg)](https://www.patreon.com/JustArchi)
[![Paypal.me donate](https://img.shields.io/badge/Paypal.me-donate-yellow.svg)](https://www.paypal.me/JustArchi/5eur)
[![Paypal donate](https://img.shields.io/badge/Paypal-donate-yellow.svg)](https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=HD2P2P3WGS5Y4)
[![Bitcoin donate](https://img.shields.io/badge/Bitcoin-donate-yellow.svg)](https://www.blockchain.com/btc/payment_request?address=1Archi6M1r5b41Rvn1SY2FfJAzsrEUT7aT)
[![Steam donate](https://img.shields.io/badge/Steam-donate-yellow.svg)](https://steamcommunity.com/tradeoffer/new/?partner=46697991&token=0ix2Ruv_)

---

## Description

ASF is a C# application that allows you to farm steam cards using multiple steam accounts simultaneously. Unlike Idle Master which works only for one account at given time, requires steam client running in background, and launches additional processes imitating "game playing" status, ASF doesn't require any steam client running in the background, doesn't launch any additional processes and is made to handle unlimited steam accounts at once. In addition to that, it's meant to be run on servers or other desktop-less machines, and features full cross-OS support, which makes it possible to launch on any .NET Core-supported operating system, such as Windows, Linux or OS X. ASF is possible thanks to gigantic amount of work done in marvelous **[SteamKit2](https://github.com/SteamRE/SteamKit)** library.

ASF doesn't require and doesn't interfere in any way with Steam client. In addition to that, it doesn't require exclusive access to given account, which means that you can use your main account in Steam client, and use ASF for idling the same account at the same time. If you decide to launch a game, ASF will get disconnected, and resume idling once you finish playing your game, being as transparent as possible during entire process.

---

### Core features

- Automatic idling of available games with card drops using any number of active accounts
- No requirement of running or even having official Steam client installed
- Guarantee of being VAC-free
- Complex error-reporting mechanism, allowing ASF to be smart and resume idling even in case of Steam or networking problems
- Customizable cards idling algorithm which will push performance of card drops to the maximum
- Offline idling, allowing you to skip in-game status and stop confusing your friends
- Advanced support for alt accounts, including ability to redeem keys, redeem gifts, accept trades and more through a simple Steam chat
- Support for latest Steam security features, including SteamGuard, SteamParental and two-factor authentication
- Unique ASF 2FA mechanism allowing ASF to act as a mobile authenticator (if needed)
- StreamTradeMatcher integration allowing ASF to help you in completing your steam badges by accepting dupe trades
- Rebased on .NET Core 2.0, cross-OS compatibility, official support for Windows, Linux and OS X
- ...and many more!

---

### Setting up / Help

Detailed guide regarding setting up and using ASF is available on our wiki in **[setting up](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/Setting-up)** section.

---

### Compatibility / Supported operating systems

ASF officially supports Windows, Linux and OS X operating systems. Please visit **[compatibility](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/Compatibility)** section on the wiki for more info.

---

### Want to know more?

Our **[wiki](https://github.com/JustArchiNET/ArchiSteamFarm/wiki)** includes a lot of other articles that might help you further with using ASF, as well as show you everything that you can make use of.
