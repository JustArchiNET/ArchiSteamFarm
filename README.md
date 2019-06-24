# ArchiSteamFarm

[![Build status (Windows)](https://img.shields.io/appveyor/ci/JustArchi/ArchiSteamFarm/master.svg?label=Windows&maxAge=600)](https://ci.appveyor.com/project/JustArchi/ArchiSteamFarm)
[![Build status (Unix)](https://img.shields.io/travis/com/JustArchiNET/ArchiSteamFarm/master.svg?label=Unix&maxAge=600)](https://travis-ci.com/JustArchiNET/ArchiSteamFarm)
[![Build status (Docker)](https://img.shields.io/docker/cloud/build/justarchi/archisteamfarm.svg?label=Docker&maxAge=600)](https://hub.docker.com/r/justarchi/archisteamfarm)
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
[![Bitcoin donate](https://img.shields.io/badge/Bitcoin-donate-yellow.svg)](https://blockstream.info/address/bc1q8archy9jneaqw6s3cs44azt6duyqdt8c6quml0)
[![Steam donate](https://img.shields.io/badge/Steam-donate-yellow.svg)](https://steamcommunity.com/tradeoffer/new/?partner=46697991&token=0ix2Ruv_)

---

## Description

ASF is a C# application with primary purpose of idling Steam cards from multiple accounts simultaneously. Unlike Idle Master which works only for one account at given time, while requiring Steam client running in the background and launching additional processes imitating "game playing" status, ASF doesn't require any Steam client running in the background, doesn't launch any additional processes and is made to handle unlimited Steam accounts at once. In addition to that, it's meant to be run on servers or other desktop-less machines, and features full cross-OS support, which makes it possible to launch on any operating system with .NET Core runtime, such as Windows, Linux and OS X. ASF is possible thanks to gigantic amount of work done in marvelous **[SteamKit2](https://github.com/SteamRE/SteamKit)** library.

Today, ASF is one of the most versatile Steam power tools, allowing you to make use of many features that were implemented over time. Apart from idling Steam cards, which remains the primary focus, ASF includes bunch of features on its own, such as a possibility to use it as Steam authenticator or chat logger. In addition to that, ASF includes plugin system, thanks to which anybody can further extend it to his/her needs.

---

### Core features

- Automatic idling of available games with card drops using any number of active accounts
- No requirement of running or even having official Steam client installed
- Guarantee of being VAC-free, focus on security and privacy
- Complex error-reporting mechanism, reliability even during Steam issues and other networking quirks
- Flexible cards idling algorithm, pushing the performance to the maximum while still allowing a lot of customization
- Offline idling, enabling you to skip in-game status and stop confusing your friends with fake playing status
- Advanced support for Steam accounts, including ability to redeem keys, redeem gifts, accept trades, send messages and more
- Support for latest Steam security features, including SteamGuard, SteamParental and 2-factor authentication
- Unique ASF 2FA mechanism allowing ASF to act as a mobile authenticator, if needed
- STM-like integration for trades, both passive (accepting) and active (sending), ASF can help you complete your sets
- Special plugin system, which allows you to extend ASF in any way you wish through your own code
- Powered by .NET Core, cross-OS compatibility, official support for Windows, Linux and OS X
- ...and many more!

For learning about even more ASF features, we recommend starting with our **[FAQ entry](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/FAQ#what-interesting-features-asf-offers-that-idle-master-does-not)**.

---

### Setting up / Help

Detailed guide regarding setting up and using ASF is available on our wiki in **[setting up](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/Setting-up)** section. It's user-friendly guide that shows basic, as well as a bit more complex ASF setup, covering all the required dependencies and other steps that are required in order to start using ASF software.

---

### Compatibility / Supported operating systems

ASF officially supports Windows, Linux and OS X operating systems, but it can work anywhere where you can obtain working .NET Core runtime. Please visit **[compatibility](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/Compatibility)** section on the wiki for more information regarding environments that ASF can work in.

---

### Want to know more?

Our **[wiki](https://github.com/JustArchiNET/ArchiSteamFarm/wiki)** includes a lot of other articles that will tell you about everything in regards to ASF, as well as show you other features that you can make use of. If you have some time to spare and you'd like to find out what else ASF can do for you, we heavily encourage you to take a look!
