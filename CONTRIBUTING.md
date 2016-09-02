# Contributing

Before making an issue or pull request, you should carefully read **[ASF wiki](https://github.com/JustArchi/ArchiSteamFarm/wiki)** first. At least reading **[FAQ](https://github.com/JustArchi/ArchiSteamFarm/wiki/FAQ)** is mandatory.

## Issues

GitHub **[issues](https://github.com/JustArchi/ArchiSteamFarm/issues)** page is being used for ASF TODO list, regarding both features and bugs. It has rather strict policy - GitHub is NOT technical support and all cases that are not suggestions neither bug reports should NOT be posted there. You have **[ASF chat](https://gitter.im/JustArchi/ArchiSteamFarm)** and **[Steam group](http://steamcommunity.com/groups/ascfarm/discussions/1/)** for general discussion, questions or technical issues. Please avoid using GitHub issues, unless you indeed want to report a bug or suggest an enhancement. Even prior to doing that, please make sure that you're indeed dealing with a bug, or your suggestion makes sense, preferably by asking on chat/steam group first. Invalid issues will be closed immediately and won't be answered.

---

### Bugs

Before reporting a bug you should carefully check if the "bug" you're encountering is in fact ASF bug and not technical issue that is answered in the **[FAQ](https://github.com/JustArchi/ArchiSteamFarm/wiki/FAQ#issues)** or in other place on the wiki. Typically technical issue is intentional ASF behaviour which might not match your expectations, e.g. failing to send or accept steam trades - logic for accepting and sending steam trades is outside of the ASF, as stated in the FAQ, and there is no bug related to that because it's up to Steam to accept such request, or not. If you're not sure if you're encountering ASF bug or technical issue, please use **[ASF chat](https://gitter.im/JustArchi/ArchiSteamFarm)** or **[Steam group](http://steamcommunity.com/groups/ascfarm/discussions/1/)** and avoid GitHub issues.

Regarding ASF bugs - Posting a log is **mandatory**, regardless if it contains information that is relevant or not. You're allowed to make small modifications such as changing bot names to something more generic, but you should not be doing anything else. You want us to fix the bug you've encountered, then help us instead of making it harder - we're not being paid for that, and we're not forced to fix the bug you've encountered. Include as much relevant info as possible - if bug is reproducable, when it happens, if it's a result of a command - which one, does it happen always or only sometimes, with one account or all of them - everything you consider appropriate, that would help us reproduce the bug and fix it. The more information you include, the higher the chance of bug getting fixed. If nobody is able to reproduce your bug, there is also no way of blindly fixing it.

It would also be cool if you could reproduce your issue on latest pre-release (and not stable) version, as this is most recent codebase that might include not-yet-released fix for your issue already. Of course, that is not mandatory, as ASF offers support for both latest pre-release as well as latest stable versions.

---

### Suggestions

ASF has rather strict scope - farming Steam cards from Steam games, which means that anything going greatly out of the scope will not be accepted, even if it's considered useful. A good example of that is Steam discovery queue, that provides extra cards during Steam sales - this is out of the scope of ASF as a program, ASF focuses on one task and is doing it efficiently, if you want to create your own bot that does exactly what you want - pay somebody for creating it.

If your suggestion doesn't go out of the scope of ASF, then explain to us in the issue why you consider it useful, why do you think that adding it to ASF is beneficial for **all users**, not yourself. Why we should spend our time coding it, convince us. If suggestion indeed makes sense, or can be considered practical, most likely we won't have anything against that, but **you** should be the one pointing out advantages, not us.

---

## Pull requests

In general any pull request is welcome and should be accepted, unless there is a strong reason against it. A strong reason includes e.g. a feature going potentially out of the scope of ASF. If you're improving existing codebase, rewriting code to be more efficient, clean, better commented - there is absolutely no reason to reject it. If you want to add missing feature, and you're not sure if it should be included in ASF, it won't hurt to ask before spending your own time.

Every pull request is carefully examined by our continuous integration system - it won't be accepted if it doesn't compile properly or causes any test to fail. We also expect that you at least barely tested the modification you're trying to add, and not blindly editing the file without even checking if it compiles. Consider the fact that you're not coding it only for yourself, but for thousands of users.

---

### License

ASF is using **[Apache License 2.0](https://github.com/JustArchi/ArchiSteamFarm/blob/master/LICENSE-2.0.txt)**.

> Unless You explicitly state otherwise, any Contribution intentionally submitted for inclusion in the Work by You to the Licensor shall be under the terms and conditions of this License, without any additional terms or conditions.

The license also permits you to:

> You may add Your own copyright statement to Your modifications(...)

Adding your own copyright statement is totally fine and it should be in format of **[copyright and contact](https://github.com/JustArchi/ArchiSteamFarm/blob/master/ArchiSteamFarm/Program.cs#L8-L9)**, specified below all currently existing statements of the file you're modifying. Adding such statement is not mandatory when sending PRs, and it's up to you to decide if you want to put one, or not.

---

### Code style

Please stick with ASF code style when submitting PRs. In repo you can find standard **[VS settings](https://github.com/JustArchi/ArchiSteamFarm/blob/master/CodeStyle.vssettings)** file that you can use in Visual Studio for import. In addition to that, there is also **[DotSettings](https://github.com/JustArchi/ArchiSteamFarm/blob/master/ArchiSteamFarm.sln.DotSettings)** file for **[ReSharper](https://www.jetbrains.com/resharper/)** (optional). Consistency is the key.
