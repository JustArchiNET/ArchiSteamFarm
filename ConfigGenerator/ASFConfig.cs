using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace ConfigGenerator {
	internal class ASFConfig {
		internal static List<ASFConfig> ASFConfigs = new List<ASFConfig>();

		internal string FilePath { get; set; }

		protected ASFConfig() {
			ASFConfigs.Add(this);
		}

		protected ASFConfig(string filePath) {
			FilePath = filePath;
			ASFConfigs.Add(this);
		}

		internal virtual void Save() {
			lock (FilePath) {
				try {
					File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
				} catch (Exception e) {
					Logging.LogGenericException(e);
				}
			}
		}

		internal virtual void Remove() {
			string queryPath = Path.GetFileNameWithoutExtension(FilePath);
			lock (FilePath) {
				foreach (var configFile in Directory.EnumerateFiles(Program.ConfigDirectory, queryPath + ".*")) {
					try {
						File.Delete(configFile);
					} catch (Exception e) {
						Logging.LogGenericException(e);
					}
				}
			}
			ASFConfigs.Remove(this);
		}

		internal virtual void Rename(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				return;
			}

			string queryPath = Path.GetFileNameWithoutExtension(FilePath);
			lock (FilePath) {
				foreach (var file in Directory.EnumerateFiles(Program.ConfigDirectory, queryPath + ".*")) {
					try {
						File.Move(file, Path.Combine(Program.ConfigDirectory, botName + Path.GetExtension(file)));
					} catch (Exception e) {
						Logging.LogGenericException(e);
					}
				}
				FilePath = Path.Combine(Program.ConfigDirectory, botName + ".json");
			}
		}
	}
}
