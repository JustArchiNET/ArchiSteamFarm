# Contributing

Before making an issue or pull request, you should carefully read **[ASF wiki](https://github.com/JustArchi/ArchiSteamFarm/wiki)** first. At least reading **[FAQ](https://github.com/JustArchi/ArchiSteamFarm/wiki/FAQ)** is mandatory.

## Issues

GitHub **[issues](https://github.com/JustArchi/ArchiSteamFarm/issues)** page is being used for ASF "Todo" list, regarding both features and bugs. It has rather **strict policy** that applies to everybody - GitHub is **NOT** technical support - it's a place **only** for ASF bugs and suggestions. It's **not** proper place for technical issues, general discussion or questions (unless related to development). In short, GitHub is for **development** part of the ASF, and all issues should be **development-oriented**. You have **[ASF chat](https://discord.gg/hSQgt8j)** and **[Steam group](http://steamcommunity.com/groups/ascfarm/discussions/1/)** for general discussion, questions, technical issues and everything else that is not related to ASF development. If you decide to use GitHub issues, please make sure that you're in fact dealing with a bug, or your suggestion makes sense, preferably by asking on chat/steam group first. Invalid issues will be closed immediately and won't be answered - if you're not sure if your issue is valid, then most likely it's not, and it should be posted on **[ASF chat](https://discord.gg/hSQgt8j)** or **[Steam group](http://steamcommunity.com/groups/ascfarm/discussions/1/)**, instead, like pointed out above. Valid bugs/suggestions will be forwarded and added as GitHub issues by us, if needed.

All issues related to wiki, especially correcting mistakes, getting rid of outdated stuff or posting suggestions, are welcome.

---

### Bugs

Before reporting a bug you should carefully check if the "bug" you're encountering is in fact ASF bug and not technical issue that is answered in the **[FAQ](https://github.com/JustArchi/ArchiSteamFarm/wiki/FAQ#issues)** or in other place on the wiki. Typically technical issue is **intentional ASF behaviour which might not match your expectations**, e.g. failing to send or accept steam trades - logic for accepting and sending steam trades is outside of the ASF, as stated in the FAQ, and there is no bug related to that because it's up to Steam to accept such request, or not. If you're not sure if you're encountering ASF bug or technical issue, please use **[ASF chat](https://discord.gg/hSQgt8j)** or **[Steam group](http://steamcommunity.com/groups/ascfarm/discussions/1/)** and avoid GitHub issues.

Regarding ASF bugs - Posting a log is **mandatory**, regardless if it contains information that is relevant or not. You're allowed to make small modifications such as changing bot names to something more generic, but you should not be doing anything else. You want us to fix the bug you've encountered, then help us instead of making it harder - we're not being paid for that, and we're not forced to fix the bug you've encountered. Include as much relevant info as possible - if bug is reproducible, when it happens, if it's a result of a command - which one, does it happen always or only sometimes, with one account or all of them - everything you consider appropriate, that could help us reproduce the bug and fix it. The more information you include, the higher the chance of bug getting fixed. **If nobody is able to reproduce your bug, there is also no way of blindly fixing it**, so it's in your best interest to **make us run into your bug**.

It would also be cool if you could reproduce your issue on latest pre-release (and not stable) version, as this is most recent codebase that might include not-yet-released fix for your issue already. Of course, that is not mandatory, as ASF offers support for both latest pre-release as well as latest stable versions, but it's entirely possible that your bug is already fixed, just not released yet.

---

### Suggestions

While everybody is able to create suggestions how to improve ASF, GitHub issues is not the best place to discuss if your enhancement makes sense - by posting it you already **believe** that it makes sense, and you're **ready to convince us how**. If you have some idea but you're not sure if it's possible, makes sense, or fits ASF purpose - you have **[Steam group](http://steamcommunity.com/groups/ascfarm/discussions/1/)** discussions where we'll be happy to discuss given enhancement in calm atmosphere, evaluating possibilities and pros/cons. This is what we suggest to do in the first place, as in GitHub issue you're switching from **having an idea** into **having a valid enhancement with general concept, given purpose and fixed details - you're ready to defend your idea and convince us how it can be useful for ASF**. This is the general reason why many issues are rejected - because you're lacking details that would prove your suggestion being worthy.

ASF has rather strict scope - idling Steam cards from Steam games + basic bots management. ASF scope is very subjective and evaluated on practical/moral basis - how much this feature fits ASF, how much actual coding effort is required to make it happen, how useful/wanted this feature is by the community and likewise. Some good examples include chat logging feature that we coded in #354 (out of the scope, but ASF already had everything that is needed, so very little coding effort, why not) but also Steam discovery queue (we were in general strongly against that, but considered it due to the fact that many people would appreciate it).

After ASF scope, we come to Steam ToS. There is no place for discussion here - if we think that something goes too much from the gray zone and is directly or indirectly against Steam ToS, we'll **always** reject it. Some good examples in this category include any automation related to the Steam store or Steam market. Both services are directly claimed to be subscription marketplaces, and automating any subscription marketplace is against Steam ToS. We're not in position to state if we agree with Steam ToS or not - we're doing our best to follow it. Interpretation of Steam ToS and what in fact is against it, is also very subjective, as ToS itself is very vague and unclear, but fact is - we do not code, neither accept features that are in conflict with Steam ToS (if in our opinion something is against the ToS, and there is no strong reasoning that we're wrong about it).

Lastly, ASF code strikes to be perfect, so we generally won't reinvent the wheel by coding something that is already possible, either with ASF, or some other third-party tool that does that much better. This does not mean that you can't suggest another way to deal with particular thing, as long as you follow everything stated above, and you're able to defend your suggestion and prove how it's useful/better than what is already possible - why current solution doesn't work for you, and new one would. Once again - reasoning is what mainly matters when dealing with ASF suggestions. Lack of good reasoning is the main reason why it's hard to convince us into doing something, as we know our code better than anybody else, including everything that is possible to do with it, so if you can't tell us why your solution works better than the old one, then there is not enough reasoning to change/improve it in the first place.

In any case, if you're posting a suggestion, then explain to us in the issue why you consider it useful, why do you think that adding it to ASF is beneficial for **all users**, not yourself. Why we should spend our time coding it, convince us. If suggestion indeed makes sense, or can be considered practical, most likely we won't have anything against that, but **you** should be the one pointing out advantages, not us. Creating an issue means that you're asking us to do given thing, so you must have some valid arguments that would defend our main question "why?" - if you can't answer it, we won't answer it for you.

---

## Pull requests

Pull requests are a bit different compared to issues, as in PR you're asking us to review the code and accept it, unless **we** have a reason against it. Very often we won't have enough arguments to accept given suggestion and code something, but we also won't have enough arguments **against** given suggestion, which makes it possible for you to code it yourself, then send a PR for review, and hopefully include your feature in ASF, even if we wouldn't code it otherwise. All of that is thanks to the fact that when dealing with PR, **we** are in position to find reasoning against it, and not necessarily you defending your own code. Still, it'd be a great thing to explain what is the purpose of the PR, expected usage and pros/cons - if you omit that info, we'll most likely ask anyway if we won't be able to read it clearly from the code itself. After all you're asking us to include your own code in ASF, so most likely you already have a reason why you coded it - share it with us!

In general any pull request is welcome and should be accepted, unless there is a strong reason against it. A strong reason includes mainly only things we directly do not agree with, such as features that are against Steam ToS (like explained above), greatly against ASF scope, or likewise. If there is nothing severe enough to justify rejecting PR, we'll tell you how to fix it (if needed), so we can allow it in ASF. If you're improving existing codebase, rewriting code to be more efficient, clean, better commented - there is absolutely no reason to reject such PR, as long as it's correct. If you want to add a missing feature, and you're not sure if it should be included in ASF, for example because you're not sure if it fits ASF scope - it won't hurt to ask before spending your own time, preferably in **[Steam group](http://steamcommunity.com/groups/ascfarm/discussions/1/)** discussions, so we can evaluate the idea and give feedback instead of accepting/rejecting the concept which is usually happening with GitHub issues - after all you want to code it yourself, so you shouldn't use GitHub issues that are being used for expecting us to add things. Still, GitHub issues are for development part of ASF like stated above, so feel free to post an issue in which you'll ask if given feature would be accepted in PR, if you prefer that way instead of using the Steam group.

Every pull request is carefully examined by our continuous integration system - it won't be accepted if it doesn't compile properly or causes any test to fail. We also expect that you at least barely tested the modification you're trying to add, so we can be sure that it works. Consider the fact that you're not coding it only for yourself, but for thousands of users.

At the same time ASF is open-source project, developed mainly by **[JustArchi](https://github.com/JustArchi)**, but also by **[many other contributors](https://github.com/JustArchi/ArchiSteamFarm/graphs/contributors)**. It's not our purpose to make you problems or forbid you from improving it, especially if you have a decent idea how to do so, therefore don't be afraid of making a suggestion first, then implementing it, after your suggestion is accepted (valid). Even if it's not a perfect solution, as long as it works it can be merged and improved in the future, as improving existing code is much easier than writing it from scratch. Still, if you're sending a PR, we expect that you did your best in it code-wise, so make sure that you're proud of your code, same like we are proud of ASF.

*"Always code as if the guy who ends up maintaining your code will be a violent psychopath who knows where you live"*

~John Woods

---

### License

ASF is using **[Apache License 2.0](https://github.com/JustArchi/ArchiSteamFarm/blob/master/LICENSE-2.0.txt)**.

> Unless You explicitly state otherwise, any Contribution intentionally submitted for inclusion in the Work by You to the Licensor shall be under the terms and conditions of this License, without any additional terms or conditions.

For more info about the license, please check out **[license](https://github.com/JustArchi/ArchiSteamFarm/wiki/License)** wiki section.

---

### Code style

Please stick with ASF code style when submitting PRs. In repo you can find standard **[VS settings](https://github.com/JustArchi/ArchiSteamFarm/blob/master/CodeStyle.vssettings)** file that you can use in Visual Studio for import. In addition to that, there is also **[DotSettings](https://github.com/JustArchi/ArchiSteamFarm/blob/master/ArchiSteamFarm.sln.DotSettings)** file for **[ReSharper](https://www.jetbrains.com/resharper/)** (optional). If you can save us those few extra seconds cleaning up your code after accepting it, it would be great and improve overall code history.
