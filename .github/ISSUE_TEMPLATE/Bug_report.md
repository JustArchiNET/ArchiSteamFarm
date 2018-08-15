---

name: Bug report
about: Unexpected program behaviour that needs code correction

---

<!--
I fully read and understood contributing guidelines of ASF available under https://github.com/JustArchi/ArchiSteamFarm/blob/master/.github/CONTRIBUTING.md and I believe that my issue is valid - it requires a response from ASF development team, and not ASF support.

I understand that if my issue is not meeting contributing guidelines specified above, especially if it's a question or technical issue that is not related to ASF development in any way, then it will be closed and left unanswered.

Feel free to remove our notice and fill the template below with your details.
-->

## Bug report

### Description

<!-- Short explanation of what you were going to do, what did you want to accomplish? -->

### Expected behavior

<!-- What did you expect to happen? -->

### Current behavior

<!-- What happened instead? -->

### Possible solution

<!-- Not mandatory, but you can suggest a fix/reason for the bug, if known to you. -->

### Steps to reproduce

<!-- Every command or action done after launching ASF that leads to the bug. -->
<!-- This is very important, you want to make us run into your bug as much as possible. -->

### Full log.txt recorded during reproducing the problem

```
Paste here, in-between triple backtick tags

Ensure that your log was NOT recorded in Debug mode, as it might contain sensitive information that should not be shared publicly, as stated on the wiki.
```

### Global ASF.json config

```json
Paste here, in-between triple backtick tags

Ensure that config has redacted (but NOT removed) potentially-sensitive properties, such as:
- IPCPassword (recommended)
- IPCPrefixes (optionally, if exposing public IPs)
- SteamOwnerID (optionally)
- WebProxy (optionally, if exposing private details)
- WebProxyPassword (optionally, if exposing private details)
- WebProxyUsername (optionally, if exposing private details)

Redacting involves replacing sensitive details, for example with stars (***). You should refrain from removing config lines entirely, as their pure existance might be relevant and should be preserved.
```

### BotName.json config of all affected bot instances (if more than one)

```json
Paste here, in-between triple backtick tags

Ensure that config has redacted (but NOT removed) potentially-sensitive properties, such as:
- SteamLogin (mandatory)
- SteamPassword (mandatory)
- SteamMasterClanID (optionally)
- SteamParentalPIN (optionally)
- SteamTradeToken (optionally)
- SteamUserPermissions (optionally, only SteamIDs)

Redacting involves replacing sensitive details, for example with stars (***). You should refrain from removing config lines entirely, as their pure existance might be relevant and should be preserved.
```

### Additional info

<!-- Everything else you consider worthy that we didn't ask for. -->
