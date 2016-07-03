/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security;
using SteamKit2.Internal;

namespace ArchiSteamFarm
{


    internal interface ILogger
    {
        void Log(LogEntry entry);
    }

    internal class ConsoleLogger : ILogger
    {
        public void Log(LogEntry entry)
        {
            // Write on console only when not awaiting response from user
            if (!Program.ConsoleIsBusy)
            {
                try
                {
                    Console.Write(entry.ToPlainTextString());
                }
                catch
                {
                    // Ignored
                }
            }
        }
    }

    internal class FileLogger : ILogger
    {

        private readonly object FileLock = new object();
        private Boolean Enabled = true;

        public FileLogger()
        {
            lock (FileLock)
            {
                try
                {
                    File.Delete(Program.LogFile);
                }
                catch (Exception e)
                {
                    Enabled = false;
                    Logging.Log(e);
                }
            }
        }

        public void Log(LogEntry entry)
        {
            if (!Enabled)
            {
                return;
            }

            lock (FileLock)
            {
                try
                {
                    File.AppendAllText(Program.LogFile, entry.ToPlainTextString());
                }
                catch (Exception e)
                {
                    Enabled = false;
                    Logging.Log(e);
                }
            }
        }

    }

    internal class EventLogLogger : ILogger
    {
        private Boolean Enabled = true;
        private EventLog log;
        public EventLogLogger()
        {
            log = new EventLog(SharedInfo.LogName) {Source = SharedInfo.LogSource};
        }

        public void Log(LogEntry entry)
        {
            if (Enabled)
            {
                try
                {
                    log.WriteEntry(entry.ToEventLogString(),ToEventLogSeverity(entry.Severity));
                }
                catch (Exception ex)
                {
                    Enabled = false;
                    Logging.Log("Event log could not be written!", LogSeverity.Error);

                    //The log or source does not exist and can't be created because we are not running as admin
                    if (ex is SecurityException)
                    {
                        Logging.Log("Relauncing as admin to create event log", LogSeverity.Info);
                        Program.Restart(true);
                    }
                }
            }
        }

        private EventLogEntryType ToEventLogSeverity(LogSeverity severity)
        {
            switch (severity)
            {
                case LogSeverity.WTF:
                    return EventLogEntryType.Error;
                case LogSeverity.Error:
                    return EventLogEntryType.Error;
                case LogSeverity.Exception:
                    return EventLogEntryType.Error;
                case LogSeverity.Warning:
                    return EventLogEntryType.Warning;
                case LogSeverity.Info:
                    return EventLogEntryType.Information;
                case LogSeverity.Debug:
                    return EventLogEntryType.Information;
                default:
                    return EventLogEntryType.Error;
            }
        }
    }

    internal class LogEntry
    {
        public readonly string Message, BotName, PreviousMethodName;
        public readonly LogSeverity Severity;

        public LogEntry(string message, LogSeverity severity, string botName, string previousMethodName)
        {
            this.Message = message;
            this.Severity = severity;
            this.BotName = botName;
            this.PreviousMethodName = previousMethodName;
        }

        public string ToPlainTextString()
        {
            switch (Severity)
            {
                case LogSeverity.WTF:
                    return DateTime.Now + " [!!] WTF: " + PreviousMethodName + "() <" + BotName + "> " + Message + ", WTF?" + Environment.NewLine;
                case LogSeverity.Error:
                    return DateTime.Now + " [!!] ERROR: " + PreviousMethodName + "() <" + BotName + "> " + Message + Environment.NewLine;
                case LogSeverity.Exception:
                    return DateTime.Now + " [!] EXCEPTION: " + PreviousMethodName + "() <" + BotName + "> " + Message + Environment.NewLine;
                case LogSeverity.Warning:
                    return DateTime.Now + " [!] WARNING: " + PreviousMethodName + "() <" + BotName + "> " + Message + Environment.NewLine;
                case LogSeverity.Info:
                    return DateTime.Now + " [*] INFO: " + PreviousMethodName + "() <" + BotName + "> " + Message + Environment.NewLine;
                case LogSeverity.Debug:
                    return DateTime.Now + " [#] DEBUG: " + PreviousMethodName + "() <" + BotName + "> " + Message + Environment.NewLine;
                default:
                    return DateTime.Now + " " + PreviousMethodName + "() <" + BotName + "> " + Message + Environment.NewLine;
            }
        }

        public string ToEventLogString()
        {
            switch (Severity)
            {
                case LogSeverity.WTF:
                    return "WTF: " + PreviousMethodName + "() <" + BotName + "> " + Message + ", WTF?";
                case LogSeverity.Debug:
                    return "DEBUG: " + PreviousMethodName + "() <" + BotName + "> " + Message;
                case LogSeverity.Exception:
                    return "EXCEPTION: " + PreviousMethodName + "() <" + BotName + "> " + Message;
                case LogSeverity.Error:
                case LogSeverity.Warning:
                case LogSeverity.Info:
                    return PreviousMethodName + "() <" + BotName + "> " + Message;
                default:
                    return PreviousMethodName + "() <" + BotName + "> " + Message;
            }
        }

    }


    internal enum LogSeverity
    {
        WTF,
        Error,
        Exception,
        Warning,
        Info,
        Debug
    }

    [SuppressMessage("ReSharper", "ExplicitCallerInfoArgument")]
    internal static class Logging
    {


        private static readonly List<ILogger> Loggers = new List<ILogger>();

        internal static void Init()
        {

            Loggers.Add(new ConsoleLogger());

            if (Program.GlobalConfig.LogToFile)
            {
                Loggers.Add(new FileLogger());
            }
            
            if (Program.GlobalConfig.LogToEventLog)
            {
                Loggers.Add(new EventLogLogger());
            }

        }


        internal static void Log(string message, LogSeverity severity = LogSeverity.Error, string botName = "Main", [CallerMemberName] string previousMethodName = null)
        {
            if (string.IsNullOrEmpty(message))
            {
                LogNullError(nameof(message), botName, previousMethodName, severity);
                return;
            }

            Log(new LogEntry(message, severity, botName, previousMethodName));
        }

        private static void Log(LogEntry entry)
        {
            Loggers.ForEach((logger) => logger.Log(entry));
        }

        internal static void Log(Exception exception, string botName = "Main", [CallerMemberName] string previousMethodName = null)
        {
            if (exception == null)
            {
                LogNullError(nameof(exception), botName);
                return;
            }

            string logMessage = exception.Message + Environment.NewLine + "StackTrace:" + Environment.NewLine + exception.StackTrace;
            exception = exception.InnerException;
            while (exception != null)
            {
                logMessage += Environment.NewLine + "INNER EXCEPTION: " + exception.Message + Environment.NewLine + "StackTrace:" + Environment.NewLine + exception.StackTrace;
                exception = exception.InnerException;
            }

            Log(logMessage,LogSeverity.Exception,botName, previousMethodName);
        }

        [SuppressMessage("ReSharper", "ExplicitCallerInfoArgument")]
        internal static void LogNullError(string nullObjectName, string botName = "Main", [CallerMemberName] string previousMethodName = null, LogSeverity severity = LogSeverity.Error)
        {
            while (true)
            {
                if (string.IsNullOrEmpty(nullObjectName))
                {
                    nullObjectName = nameof(nullObjectName);
                    continue;
                }

                Log(new LogEntry(nullObjectName + " is null!", severity, botName, previousMethodName));
                break;
            }
        }


        [Obsolete]
        internal static void LogGenericWTF(string message, string botName = "Main", [CallerMemberName] string previousMethodName = null)
        {
            Log(message, LogSeverity.WTF, botName, previousMethodName);
        }

        [Obsolete]
        internal static void LogGenericError(string message, string botName = "Main", [CallerMemberName] string previousMethodName = null)
        {
            Log(message, LogSeverity.Error, botName, previousMethodName);
        }

        [Obsolete]
        internal static void LogGenericException(Exception exception, string botName = "Main", [CallerMemberName] string previousMethodName = null)
        {
            while (true)
            {
                if (exception == null)
                {
                    LogNullError(nameof(exception), botName);
                    return;
                }

                Log("[!] EXCEPTION: " + previousMethodName + "() <" + botName + "> " + exception.Message + Environment.NewLine + "StackTrace:" + Environment.NewLine + exception.StackTrace);
                if (exception.InnerException != null)
                {
                    exception = exception.InnerException;
                    continue;
                }

                break;
            }
        }

        [Obsolete]
        internal static void LogGenericWarning(string message, string botName = "Main", [CallerMemberName] string previousMethodName = null)
        {
            Log(message, LogSeverity.Warning, botName, previousMethodName);
        }

        [Obsolete]
        internal static void LogGenericInfo(string message, string botName = "Main", [CallerMemberName] string previousMethodName = null)
        {
            Log(message, LogSeverity.Info, botName, previousMethodName);
        }

        [Conditional("DEBUG")]
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        [Obsolete]
        internal static void LogGenericDebug(string message, string botName = "Main", [CallerMemberName] string previousMethodName = null)
        {
            Log(message, LogSeverity.Debug, botName, previousMethodName);
        }


    }
}
