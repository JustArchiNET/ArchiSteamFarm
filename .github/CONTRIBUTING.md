# Contributing

Before making an issue or pull request, you should carefully read **[ASF wiki](https://github.com/JustArchiNET/ArchiSteamFarm/wiki)** first. At least reading **[FAQ](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/FAQ)** is mandatory.

## Issues

GitHub **[issues](https://github.com/JustArchiNET/ArchiSteamFarm/issues)** page is being used for ASF "todo" list, regarding both features and bugs. It has **strict policy** that applies to everybody - GitHub is **NOT** technical support, it's a place dedicated **only** to ASF bugs and suggestions. It's **not** proper place for technical issues, general discussion or questions that are unrelated to development. In short, GitHub is for the **development** part of the ASF, and all issues should be **development-oriented**. You have **[ASF chat](https://discord.gg/hSQgt8j)** and **[Steam group](https://steamcommunity.com/groups/archiasf/discussions/1)** for general discussion, questions, technical issues and everything else that is not related to ASF development. If you decide to use GitHub issues, please make sure that you're in fact dealing with a bug, or your suggestion makes sense, preferably by asking on chat/steam group first. Invalid issues will be closed immediately and won't be answered - if you're not sure if your issue is valid, then most likely it's not, and you shouldn't post it here. Valid bugs/suggestions will be forwarded and added as GitHub issues by us, if needed.

Examples of **invalid** issues:
- Asking how to install the program or use one of its functions
- Having technical difficulties running the program in some environment, encountering expected issues caused by the user's neglect
- Reporting problems that are not caused by ASF, such as ASF-ui issues or Steam not allowing you to log in
- Other activities that are not related to ASF development in any way and do not require any development action from us

Examples of **valid** issues:
- Reporting incorrect program behaviour that requires a code correction from us
- Posting a valid development suggestion on improving the project
- Correcting mistakes on our wiki, in the documentation or in likewise places that we're in charge of
- Other activities that directly benefit ASF development and require a development action

---

### Bugs

Before reporting a bug you should carefully check if the "bug" you're encountering is in fact ASF bug and not technical issue that is answered in the **[FAQ](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/FAQ#issues)** or in other place on the wiki. Typically technical issue is **intentional ASF behaviour which might not match your expectations**, e.g. failing to send or accept Steam trades - logic for accepting and sending Steam trades is outside of the ASF, as stated in the FAQ, and there is no bug related to that because it's up to Steam to accept such request, or not. If you're not sure if you're encountering ASF bug or technical issue, please avoid using GitHub issues.

When reporting ASF bug, posting a log is **mandatory**, regardless if it contains information that is relevant or not. You're allowed to make small modifications such as changing bot names to something more generic, but you should not be doing anything else. You want us to fix the bug you've encountered, then help us instead of making it harder - we're not being paid for that, and we're not forced to fix the bug you've encountered. Include as much relevant info as possible - if bug is reproducible, when it happens, if it's a result of a command - which one, does it happen always or only sometimes, with one account or all of them - everything you consider appropriate, that could help us reproduce the bug and fix it. The more information you include, the higher the chance of bug getting fixed. **If nobody is able to reproduce your bug, there is also no way of blindly fixing it**, so it's in your best interest to **make us run into your bug**.

It would also be cool if you could reproduce your issue on latest **[pre-release](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/Release-cycle)** (and not stable) version, as this is the most recent codebase that might include not-yet-released fix for your issue already. Of course, that is not mandatory, as ASF offers support for both latest pre-release as well as latest stable versions, but it's entirely possible that your bug is already fixed, just not released yet.

---

### Suggestions

While everybody is able to create suggestions how to improve ASF, GitHub issues is not the proper place to discuss if your enhancement makes sense - by posting it you already **believe** that it makes sense, and you're **ready to convince us how**. If you have some idea but you're not sure if it's possible, makes sense, or fits ASF purpose - you have **[Steam group](https://steamcommunity.com/groups/archiasf/discussions/1)** discussions where we'll be happy to discuss given enhancement in calm atmosphere, evaluating possibilities and pros/cons. This is what we suggest to do in the first place, as in GitHub issue you're switching from **having an idea** into **having a valid enhancement with general concept, given purpose and fixed details - you're ready to defend your idea and convince us how it can be useful for ASF**. This is the general reason why many issues are rejected - because you're lacking details that would prove your suggestion being worthy.

ASF has a strict scope - idling Steam cards from Steam games + basic bots management. ASF scope is very subjective and evaluated on practical/moral basis - how much this feature fits ASF, how much actual coding effort is required to make it happen, how useful/wanted this feature is by the community and likewise. In general we don't mind further enhancements to the program, as there is always a room for improvement, but at the same time we consider ASF to be feature-complete and vast majority of things that are suggested today are simply out of the scope of ASF as a program. This is why we've rejected **[a lot](https://github.com/JustArchiNET/ArchiSteamFarm/issues?q=label%3AEnhancement+label%3A%22Not+going+to+happen%22)** of general enhancements, for various different reasons, mainly regarding the scope of the program. Some people might find it hard to understand why we're rather sceptical towards suggestions, while the answer for that isn't obvious at first.

> In the lifetime of an Open Source project, only 10 percent of the time spent adding a feature will be spent coding it. The other 90 percent will be spent in support of that feature.

This includes especially maintenance of that feature, such as documenting it, keeping that documentation up-to-date, ensuring this feature won't break between one version and another, actively supporting that feature (including answering people how it works, why it doesn't work the way they want, and why it works like that), on top of all usual code maintenance, refactoring and writing it in the first place. We code ASF in our free time, and our free time is not infinite. Moreover, even if it was infinite then we'd still not be able to please everybody, as it's simply not possible to create a program that satisfies everyone. Developing any kind of software project is always about compromise - you can't possibly have everything. Using ASF for checking weather or reminding you to feed your cat isn't necessarily within the program's scope. Posting comments on profiles, sorting your games library or managing your Steam inventory isn't within that scope either. You don't have to agree with us, but you have to respect our decision as project maintainers when you make a suggestion that simply isn't what ASF was designed to do. ASF is open-source project, if you can't live without some feature then you can always code it **yourself**, and if that's too much for you, then like it or not, but you have to respect our decision whether **we** will decide to code it.

After ASF scope, we come to Steam ToS. There is no place for discussion here - if we think that something goes too much from the gray zone and is directly or indirectly against Steam ToS, we'll **always** reject it. Some good examples in this category include any automation related to the Steam store or Steam market. Both services are directly claimed to be subscription marketplaces, and automating any subscription marketplace is against Steam ToS. We're not in position to state if we agree with Steam ToS or not - we're doing our best to follow it. Interpretation of Steam ToS and what in fact is allowed or not, is also very subjective, as ToS itself is very vague and unclear in almost every possible aspect. However, the fact remains the same - we do not code, neither accept features that are in direct or indirect conflict with Steam ToS when there is no strong reasoning that we're wrong about making that call. You should check Steam **[ToS](https://store.steampowered.com/subscriber_agreement/english)** and **[online conduct](https://store.steampowered.com/online_conduct?l=english)** at the minimum.

Lastly, ASF code strikes to be perfect, so we generally won't reinvent the wheel by coding something that is already possible, either with ASF, or some other third-party tool that achieves that much better. This does not mean that you can't suggest another way to deal with a particular problem, as long as you follow everything stated above, and you're able to defend your suggestion and prove how it's useful/better than what is already possible - why current solution doesn't work for you, and new one would. Once again - reasoning is what mainly matters when dealing with ASF suggestions. Lack of good reasoning is the main reason why it's hard to convince us into doing something, as we know our code better than anybody else, including everything that is possible to do with it, so if you can't tell us why your solution works better than the old one, then there is not enough reasoning to change/improve it in the first place.

In any case, you should be able to explain to us in the issue why you consider your enhancement as useful, why do you think that adding it to ASF is beneficial for **all users**, not just yourself. You have to convince us to follow your logic. If your suggestion indeed makes sense, or can be considered practical, most likely we won't have anything against that, but **you** should be the one pointing out advantages, not us. Creating an issue means that you're asking us to spend that very limited free time explained above on your case specifically, so you must have some valid arguments that would defend our main question "why". If you can't answer it, we won't answer it for you.

---

## Pull requests

Pull requests are a bit different compared to issues, as in PR you're asking us to review the code and accept it, unless **we** have a reason against it. Very often we won't have enough arguments to accept given suggestion and code something, but we also won't have enough arguments **against** given suggestion, which makes it possible for you to code it yourself, then send a PR for review, and hopefully include your feature in ASF, even if we wouldn't code it otherwise. Such issues are appropriately tagged with **[PR-ok](https://github.com/JustArchiNET/ArchiSteamFarm/issues?q=is%3Aissue+is%3Aclosed+label%3APR-ok)** so you can easily take a look at those features that we wouldn't mind, but neither code ourselves. All of that is possible thanks to the fact that when dealing with PR, **we** are in position to find reasoning against it, and not necessarily you defending your own code. This is how **[a lot](https://github.com/JustArchiNET/ArchiSteamFarm/pulls?q=is%3Apr+is%3Amerged)** of ASF features were actually made possible, but at the same time there are still **[cases](https://github.com/JustArchiNET/ArchiSteamFarm/pulls?q=is%3Apr+is%3Aclosed+label%3A%22Not+going+to+happen%22)** of PRs that we decided to reject.

In general any pull request is welcome and should be accepted, unless there is a strong reason against it. A strong reason includes mainly only things we directly do not agree with, such as features that are against Steam ToS (like explained above), greatly against ASF scope (to the point it'd hurt overall maintenance), or likewise. If there is nothing severe enough to justify rejecting PR, we'll tell you how to fix it (if needed), so we can allow it in ASF. If you're improving existing code, rewriting it to be more efficient, clean, better commented - there is absolutely no reason to reject such PR, as long as it's in fact correct. If you want to add a missing feature, and you're not sure if it should be included in ASF, for example because you're not sure if it fits ASF scope - it won't hurt to ask before spending your own time, preferably in **[Steam group](https://steamcommunity.com/groups/archiasf/discussions/1)** discussions, so we can evaluate the idea and give feedback instead of accepting/rejecting the concept which is usually happening with GitHub issues - after all you want to code it yourself, so you shouldn't use GitHub issues that are being used for expecting us to add things. Still, as stated above, our entire GitHub repo is dedicated to development part of ASF, so feel free to post an issue in which you'll ask if given feature would be accepted in PR, if you prefer that way instead of using the Steam group.

Every pull request is carefully examined by our continuous integration system - it won't be accepted if it doesn't compile properly or causes any test to fail. We also expect that you at least barely tested the modification you're trying to add, so we can be sure that it works. Consider the fact that you're not coding it only for yourself, but for thousands of users.

ASF is open-source project, developed mainly by **[JustArchi](https://github.com/JustArchi)**, but also by **[many other contributors](https://github.com/JustArchiNET/ArchiSteamFarm/graphs/contributors)**. It's not our purpose or objective to make you problems or forbid you from improving our project, especially if you have a decent idea how to do so, therefore don't be afraid of making a suggestion first, then implementing it, after your suggestion is confirmed to make sense. Even if you're not completely sure how you should achieve a particular goal, you can still send a PR with your own solution, and get a feedback in code review regarding it. If you're sending a PR, we expect that you did your best in it code-wise, so make sure that you're proud of your code, same like we are proud of ASF. Also don't be afraid or in a hurry to fix your code based on our review - it's **for you** so you can make your PR better while learning about exact reasons why your previous solution wasn't the best one.

> Always code as if the guy who ends up maintaining your code will be a violent psychopath who knows where you live.

~John Woods

---

### License

ASF is using **[Apache License 2.0](https://github.com/JustArchiNET/ArchiSteamFarm/blob/master/LICENSE-2.0.txt)**.

> Unless You explicitly state otherwise, any Contribution intentionally submitted for inclusion in the Work by You to the Licensor shall be under the terms and conditions of this License, without any additional terms or conditions.

For more info about the license, please check out **[license](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/License)** wiki section.

---

### Code style

Please stick with ASF code style when submitting PRs. In repo you can find several different files dedicated to making it easier for you:

- **[EditorConfig](https://github.com/JustArchiNET/ArchiSteamFarm/blob/master/.editorconfig)** file which is supported by all major IDEs and requires no further setup. It's a good starting point, although it doesn't include all the rules that we'd like to see.
- **[VS settings](https://github.com/JustArchiNET/ArchiSteamFarm/blob/master/CodeStyle.vssettings)** file that you can use in Visual Studio for import. This one includes far more options than EditorConfig alone, and it's a very good choice if you're using bare VS.
- **[DotSettings](https://github.com/JustArchiNET/ArchiSteamFarm/blob/master/ArchiSteamFarm.sln.DotSettings)** file that is being used by **[ReSharper](https://www.jetbrains.com/resharper)** and **[Rider](https://www.jetbrains.com/rider)**. This one is the most complete config file that is also being loaded automatically when you're using ReSharper/Rider with our code.

Personally we're using Visual Studio + ReSharper, so no other action is needed after opening `ArchiSteamFarm.sln` solution. If you're using VS alone, it's probably a good idea to import our code style settings, although even editor config should be enough for majority of cases. If you can save us those few extra seconds cleaning up your code after accepting it, it would be great and surely improve overall code history.
