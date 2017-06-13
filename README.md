# ArchiSteamFarm

[![Build status (Windows)](https://img.shields.io/appveyor/ci/JustArchi/ArchiSteamFarm/master.svg?label=Windows&maxAge=60)](https://ci.appveyor.com/project/JustArchi/ArchiSteamFarm)
[![Build status (Mono)](https://img.shields.io/travis/JustArchi/ArchiSteamFarm/master.svg?label=Mono&maxAge=60)](https://travis-ci.org/JustArchi/ArchiSteamFarm)
[![License](https://img.shields.io/github/license/JustArchi/ArchiSteamFarm.svg?label=License&maxAge=86400)](./LICENSE-2.0.txt)
[![GitHub release](https://img.shields.io/github/release/JustArchi/ArchiSteamFarm.svg?label=Latest&maxAge=60)](https://github.com/JustArchi/ArchiSteamFarm/releases/latest)
[![Github downloads](https://img.shields.io/github/downloads/JustArchi/ArchiSteamFarm/latest/total.svg?label=Downloads&maxAge=60)](https://github.com/JustArchi/ArchiSteamFarm/releases/latest)
[![Crowdin](https://d322cqt584bo4o.cloudfront.net/archisteamfarm/localized.svg)](https://github.com/JustArchi/ArchiSteamFarm/wiki/Localization)

[![Patreon support](https://img.shields.io/badge/Patreon-support-yellow.svg)](https://www.patreon.com/JustArchi)
[![Paypal.me donate](https://img.shields.io/badge/Paypal.me-donate-yellow.svg)](https://www.paypal.me/JustArchi/1usd)
[![Paypal donate](https://img.shields.io/badge/Paypal-donate-yellow.svg)](https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=HD2P2P3WGS5Y4)
[![Bitcoin donate](https://img.shields.io/badge/Bitcoin-donate-yellow.svg)](https://blockchain.info/payment_request?address=1Archi6M1r5b41Rvn1SY2FfJAzsrEUT7aT)
[![Steam donate](https://img.shields.io/badge/Steam-donate-yellow.svg)](https://steamcommunity.com/tradeoffer/new/?partner=46697991&token=0ix2Ruv_)

[![Gitter](https://img.shields.io/gitter/room/JustArchi/ArchiSteamFarm.svg?label=Chat&maxAge=86400)](https://gitter.im/JustArchi/ArchiSteamFarm)
[![Steam group](https://img.shields.io/badge/Steam-group-yellowgreen.svg)](https://steamcommunity.com/groups/ascfarm)
[![Discord](https://img.shields.io/badge/Discord-join-blue.svg)](https://discord.gg/HStsVSB)

---

## Description

ASF is a C# application that allows you to farm steam cards using multiple steam accounts simultaneously. Unlike Idle Master which works only for one account at given time, requires steam client running in background, and launches additional processes imitiating "game playing" status, ASF doesn't require any steam client running in the background, doesn't launch any additional processes and is made to handle unlimited steam accounts at once. In addition to that, it's meant to be run on servers or other desktop-less machines, and features full Mono support, which makes it possible to launch on any Mono-supported operating system, such as Windows, Linux or OS X. ASF is based on, and possible, thanks to [SteamKit2](https://github.com/SteamRE/SteamKit).

ASF doesn't require and doesn't interfere in any way with Steam client. In addition to that, it no longer requires exclusive access to given account, which means that you can use your main account in Steam client, and use ASF for farming the same account at the same time. If you decide to launch a game, ASF will get disconnected, and resume farming once you finish playing your game, being as transparent as possible.

---

### Core features

- Automatic farming of available games with card drops using any number of active accounts
- No requirement of running or even having official Steam client installed
- Guarantee of being VAC-free
- Complex error-reporting mechanism, allowing ASF to be smart and resume farming even in case of Steam or networking problems
- Customizable cards farming algorithm which will push performance of cards farming to the maximum
- Offline farming, allowing you to skip in-game status and not confuse your friends anymore
- Advanced support for alt accounts, including ability to redeem keys, redeem gifts, accept trades and more through a simple Steam chat
- Support for latest Steam security features, including SteamGuard, SteamParental and Two-Factor authentication
- Unique ASF 2FA mechanism allowing ASF to act as a mobile authenticator (if needed)
- StreamTradeMatcher integration allowing ASF to help you in completing your steam badges by accepting dupe trades
- Full Mono support, cross-OS compatibility, official support for Windows, Linux and OS X
- ...and many more!

---

### Setting up / Help

Detailed guide regarding setting up and using ASF is available on our wiki in **[setting up](https://github.com/JustArchi/ArchiSteamFarm/wiki/Setting-up)** section.

---

### Compatibility / Supported operating systems

ASF officially supports Windows, Linux and OS X operating systems. Please visit **[compatibility](https://github.com/JustArchi/ArchiSteamFarm/wiki/Compatibility)** section on the wiki for more info.

---

### Want to know more?

Our **[wiki](https://github.com/JustArchi/ArchiSteamFarm/wiki)** includes a lot of other articles that might help you further with using ASF, as well as show you everything that you can make use of.
