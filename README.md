ArchiSteamFarm
===================

Big work-in-progress. This bot allows you to farm steam cards using multiple accounts simultaneously. Each account is defined by it's own XML config in `config` directory and you don´t need any steam-client running in the background.

**Current functions:**

 - Automatically farm steam cards using any number of active accounts
 - Automatically accept friend requests sent from master
 - Automatically accept all trades coming from master
 - Automatically accept all steam cd-keys sent via chat from master
 - SteamGuard / 2-factor-authentication support
 - Full Mono support, tested on Debian "9.0" Stretch (testing)

**Current Commands:**

 - `!exit` Stops whole ASF
 - `!farm` Restarts the bot and starts card-farming (again)
 - `!start <BOT>` Starts given bot instance
 - `!status` Prints current status of ASF
 - `!stop <BOT>` Stops given bot instance

> You can use chat-commands in group-chat or private-chat with your bot.
> The MasterID has to be set for this specific bot / config-file.

**Supported / Tested Operating-Systems:**

 - Windows 10 Enterprise Edition
 - Debian 9.0 Stretch (Mono)
 - Debian 8.1 Jessie (Mono)

**TODO**:

- Smart Multi-Game-Farming
- Possible integration with SteamTradeMatcher, bot can detect dupes and trade them automatically. Backend-code is already here, just missing actual implementation.
- Automatic sending of steamtrades to master(after game is fully farmed)
- Probably much more


> This is big WIP, so feel free to send pull requests if you wish. I'll
> release some releases later, when everything is tested and code
> cleaned up.
