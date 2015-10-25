# ArchiSteamFarm

Big work-in-progress

Allows you to farm steam cards using multiple accounts simultaneously.

Each account is defined by it's own XML config in "config" directory.

Current functions:
- Does not need Steam client running, or even a GUI. Fully based on SteamKit2 and reverse-engineered Steam protocol.
- Automatically farm steam cards using any number of active accounts
- Automatically accept friend requests sent from master
- Automatically accept all trades coming from master
- Automatically accept all steam cd-keys sent via chat from master
- SteamGuard / 2-factor-authentication support
- Full Mono support, tested on Debian "9.0" Stretch (testing)

TODO:
- Smart multi-games farming till every game reaches 2 hours, then one-by-one (similar to Idle Master) - Backend code is already here, just missing the logic and tests.
- Possible integration with SteamTradeMatcher, bot can detect dupes and trade them automatically. Again, backend code is already here, just missing actual implementation.
- Automatic sending of steam trades to master, after game is fully farmed.
- Probably much more

This is big WIP, so feel free to send pull requests if you wish.

I'll release some releases later, when everything is tested and code cleaned up.
