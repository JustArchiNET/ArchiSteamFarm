Crowdin CLI
===================

**[Info](https://support.crowdin.com/cli-tool)**

**[Source](https://github.com/crowdin/crowdin-cli-2)**

**[Latest version](https://downloads.crowdin.com/cli/v2/crowdin-cli.zip)**

---

Scripts included in this directory are used by ASF developers for synchronization of strings/translations between GitHub and **[Crowdin](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/Localization)**. If you're not ASF developer that has access to our localization platform, then you won't find anything interesting here.

---

## Before you begin

Make sure that your `crowdin_identity.yml` file exists. This is the file with login credentials that is not being committed to GitHub. If it doesn't exist yet (e.g. because you've just cloned the repo), create it from `crowdin_identity_example.yml` and fill `api_key` that can be found in our **[project settings](https://crowdin.com/project/archisteamfarm/settings#api)**.

---

## Installation

Follow **[crowdin instructions](https://support.crowdin.com/cli-tool/#installation)** and ensure that `crowdin` command is recognized by your shell. This is recommended setup.

Alternatively, at the bare minimum install latest **[Java JRE](https://www.oracle.com/technetwork/java/javase/downloads)**, ensure that `java` command is recognized by your shell and that your java version is able to execute bundled `crowdin-cli.jar`.

---

## Usage

`archi_upload` for pushing source strings to Crowdin (if not done automatically by CI).

`archi_download` for downloading translated strings from Crowdin (if not done automatically by CI).

`archi_sync` for upload + download.

`archi_core` for custom workflows and integration.
