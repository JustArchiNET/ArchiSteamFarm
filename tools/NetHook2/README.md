# NetHook2

This tool is used for reverse-engineering of Steam client. It's capable of hooking and recording network traffic sent/received by the client. If you're not trying to implement missing SK2 functionality in ASF, then please do not proceed.

---

## Usage

1. Launch Steam client
2. Execute `hook.cmd`
3. Reproduce the functionality you're trying to add
4. Execute `unhook.cmd`
5. You can use `NetHookAnalyzer2` for analyzing recorded log (which can be found in your Steam directory)

---

## Disclaimer

There is absolutely no guarantee that this will even work for you, not to mention the consequences from hooking the external DLL into Steam Client. You're entirely on your own. This build is for me so I don't need to compile it from scratch every time - I strongly recommend against using it, as I do not offer any support regarding this.

Source of files included in this directory can be found **[here](https://github.com/SteamRE/SteamKit/tree/master/Resources/NetHook2)**. The binary itself comes directly from SteamKit2's **[CI](https://ci.appveyor.com/project/SteamRE/SteamKit)**.
