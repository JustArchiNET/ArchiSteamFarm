NetHook2
===================

This tool is used for reverse-engineering of Steam client. It's capable of hooking and recording network traffic sent/received by the client. If you're not trying to implement missing SK2 functionality in ASF, then please do not proceed.

1. Launch Steam client
2. Execute `hook.cmd`
3. Reproduce the functionality you're trying to add
4. Execute `unhook.cmd`
5. Use `NetHookAnalyzer2.exe` for analyzing recorded log (which can be found in your Steam directory)

- Source of the `NetHook2.dll` can be found **[here](https://github.com/SteamRE/SteamKit/tree/master/Resources/NetHook2)**
- Source of the `NetHookAnalyzer2.exe` can be found **[here](https://github.com/SteamRE/SteamKit/tree/master/Resources/NetHookAnalyzer2)**

===================

There is absolutely no guarantee that this will even work for you, not to mention the consequences from hooking the external DLL into steam client. You're on your own. This build is for me so I don't need to compile it from scratch every time - I strongly recommend against using it. You have SK2 sources for a reason.
