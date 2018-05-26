Crowdin CLI
===================

**[Latest release](https://downloads.crowdin.com/cli/v2/crowdin-cli.zip)**

**[Source](https://github.com/crowdin/crowdin-cli-2)**

**[Help](https://support.crowdin.com/cli-tool/#cli-2)**

---

This tool is being used by ASF developers for synchronization of strings/translations between GitHub and **[Crowdin](https://github.com/JustArchi/ArchiSteamFarm/wiki/Localization)**. If you're not ASF developer that has access to our localization platform, then you won't find anything interesting here.

---

## Before you begin

- Make sure that your `crowdin_identity.yml` file exists - this is the file with login credentials that is not being committed to GitHub. If it doesn't exist yet (e.g. because you've just cloned the repo), create it from `crowdin_identity_example.yml` and fill `api_key` that can be found **[here](https://crowdin.com/project/archisteamfarm/settings#api)**.

- Ensure that `crowdin` command is recognized by your OS.

---

### Windows

- Install **[Java JDK](http://www.oracle.com/technetwork/java/javase/downloads/index.html)**.
- **[Set JAVA_HOME properly](https://confluence.atlassian.com/doc/setting-the-java_home-variable-in-windows-8895.html)**.
- Launch `setup_crowdin.bat` as administrator.
- Open new `cmd` prompt and verify that `crowdin help` indeed works.

---

## Usage

- `archi_upload.ps1` for pushing strings to Crowdin.

- `archi_download.ps1` for downloading translations from Crowdin (typically last commit before release).

- `archi_sync.ps1` for upload + download (tree sync).
