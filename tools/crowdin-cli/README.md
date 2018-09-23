Crowdin CLI
===================

**[Info](https://support.crowdin.com/cli-tool)**

**[Source](https://github.com/crowdin/crowdin-cli-2)**

---

Scripts included in this directory are used by ASF developers for synchronization of strings/translations between GitHub and **[Crowdin](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/Localization)**. If you're not ASF developer that has access to our localization platform, then you won't find anything interesting here.

---

## Before you begin

- Make sure that your `crowdin_identity.yml` file exists - this is the file with login credentials that is not being committed to GitHub. If it doesn't exist yet (e.g. because you've just cloned the repo), create it from `crowdin_identity_example.yml` and fill `api_key` that can be found **[here](https://crowdin.com/project/archisteamfarm/settings#api)**.

---

## Installation

Follow **[instructions](https://support.crowdin.com/cli-tool/#installation)** and ensure that `crowdin` command is recognized by your shell.

---

## Usage

- `archi_upload` for pushing strings to Crowdin.

- `archi_download` for downloading translations from Crowdin.

- `archi_sync` for upload + download.
