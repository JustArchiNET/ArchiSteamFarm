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

---

## Installation

### Windows

- Install **[Java JRE](http://www.oracle.com/technetwork/java/javase/downloads/index.html)** (or entire JDK).
- **[Set JAVA_HOME properly](https://confluence.atlassian.com/doc/setting-the-java_home-variable-in-windows-8895.html)**.
- Launch `setup_crowdin.bat` as administrator.

### Linux
- Install **[OpenJDK JRE](http://openjdk.java.net/install)** (or entire JDK).
- **[Set JAVA_HOME properly](https://stackoverflow.com/questions/24641536/how-to-set-java-home-in-linux-for-all-users)**.
- Launch `crowdin.sh` as root.

Afterwards you should verify in shell that `crowdin help` command is recognized.

---

## Usage

- `archi_upload` for pushing strings to Crowdin.

- `archi_download` for downloading translations from Crowdin.

- `archi_sync` for upload + download.
