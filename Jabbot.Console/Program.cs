﻿using System;
using System.Configuration;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BookSleeve;
using Jabbot.Core.Jabbr;
using Jabbot.Core.Sprockets;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Jabbot.Console
{
    class Program
    {
        private static string BotName { get { return ConfigurationManager.AppSettings["Bot.Name"]; } }
        private static string BotPassword { get { return ConfigurationManager.AppSettings["Bot.Password"]; } }
        private static string BotGravatarEmail { get { return ConfigurationManager.AppSettings["Bot.GravatarEmail"]; } }
        private static string BotServer { get { return ConfigurationManager.AppSettings["Bot.Server"]; } }
        private static string Version { get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); } }
        private static Logger Logger { get { return LogManager.GetCurrentClassLogger(); } }
        private static IJabbrClient JabbRClient { get; set; }
        private static Timer HeartbeatTimer { get; set; }
        private static Timer DefibrillatorTimer { get; set; }
        private static bool ShouldExit { get; set; }

        static int Main(string[] args)
        {
            TaskScheduler.UnobservedTaskException += new EventHandler<UnobservedTaskExceptionEventArgs>(TaskScheduler_UnobservedTaskException);
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
            
            try
            {
                System.Console.WriteLine(String.Format("Jabbot v{0}", Version));
                Initialize();
                while (!ShouldExit) { }
            }
            catch (Exception ex)
            {
                var exception = ex.GetBaseException();
                Logger.ErrorException("An error occured while starting.", exception);
            }
            finally
            {
                Logger.Info("Exiting");
            }

            return -1;
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;

            if (e.IsTerminating)
            {
                Logger.FatalException("An unhandled exception is causing the worker to terminate.", exception);
                ShouldExit = true;
            }
            else
            {
                Logger.ErrorException("An unhandled exception occurred in the worker process.", exception);
            }
        }

        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.ErrorException("An unobserved task exception occurred.", e.Exception.GetBaseException());
            e.SetObserved();
        }

        private static void Initialize()
        {
            InitializeLogger();
            InitializeJabbRClient();
            InitializeHeartbeatTimer();
            InitializeDefibrillatorTimer();
        }

        private static void InitializeLogger()
        {
            string key = ConfigurationManager.AppSettings["LOGENTRIES_ACCOUNT_KEY"];
            string location = ConfigurationManager.AppSettings["LOGENTRIES_LOCATION"];

            LoggingConfiguration loggingConfiguration = new LoggingConfiguration();

            ColoredConsoleTarget consoleTarget = new ColoredConsoleTarget();
            consoleTarget.Layout = "${date:format=u} ${logger} ${level} ${message}";
            loggingConfiguration.AddTarget("console", consoleTarget);

            LoggingRule loggingRule1 = new LoggingRule("*", LogLevel.Debug, consoleTarget);
            loggingConfiguration.LoggingRules.Add(loggingRule1);

            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(location))
            {
                var logEntiresTarget = new LogentriesTarget();
                logEntiresTarget.Key = key;
                logEntiresTarget.Location = location;
                logEntiresTarget.Debug = true;
                logEntiresTarget.Layout = "${date:format=u} ${logger} ${level} ${message} ${exception:format=tostring}";
                loggingConfiguration.AddTarget("logentries", logEntiresTarget);

                LoggingRule loggingRule2 = new LoggingRule("*", LogLevel.Debug, logEntiresTarget);
                loggingConfiguration.LoggingRules.Add(loggingRule2);
            }

            LogManager.Configuration = loggingConfiguration;
        }

        private static void InitializeJabbRClient()
        {
            try
            {
                Logger.Info("Initializing JabbR client started");
                JabbRClient = new JabbrClient(BotServer);
                JabbRClient.OnReceivePrivateMessage += ProcessPrivateMessage;
                JabbRClient.OnReceiveRoomMessage += ProcessRoomMessage;
                if (JabbRClient.Connect()) 
                { 
                    Logger.Info(string.Format("Connection to '{0}' established.", BotServer));
                    
                    if (JabbRClient.Login(BotName, BotPassword, BotGravatarEmail))
                    {
                        Logger.Info(string.Format("Login to '{0}' successful.", BotServer));
                    }
                    else
                    {
                        Logger.Info(string.Format("Login to '{0}' not successful.", BotServer));
                    }
                }
                else 
                { 
                    Logger.Info(string.Format("Connection to '{0}' not established.", BotServer)); 
                }
                Logger.Info("Initializing JabbR client completed");
            }
            catch (Exception ex)
            {
                Logger.ErrorException("An exception occurred while initializing JabbR client.", ex);
            }
        }

        private static void InitializeHeartbeatTimer()
        {
            Logger.Info("Initializing Heartbeat Timer Started");
            var callback = new TimerCallback((object o) =>
            {
                try
                {
                    if (JabbRClient != null && JabbRClient.IsConnected)
                    {
                        using (var connection = GetRedisConnection())
                        {
                            connection.Strings.Set(0, "Jabbot:LastSeen", DateTimeOffset.UtcNow.ToString("u")).Wait();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.ErrorException("There was an error while checking the connection status.", ex);
                }
            });
            HeartbeatTimer = new Timer(callback, null, new TimeSpan(0, 0, 10), new TimeSpan(0, 5, 0));
            Logger.Info("Initializing Heartbeat Timer Completed");
        }

        private static void InitializeDefibrillatorTimer()
        {
            Logger.Info("Initializing Defibrillator Timer Started");
            var callback = new TimerCallback((object o) =>
            {
                ShouldExit = true;
            });
            var dueTime = (long)new TimeSpan(1, 0, 0).TotalMilliseconds;
            DefibrillatorTimer = new Timer(callback, null, dueTime, Timeout.Infinite);
            Logger.Info("Initializing Defibrillator Timer Completed");
        }

        private static RedisConnection GetRedisConnection()
        {
            try
            {
                var uri = new Uri(ConfigurationManager.AppSettings["REDISTOGO_URL"]);
                var host = uri.Host;
                var port = uri.Port;
                var password = uri.UserInfo.Split(':')[1];
                var redisConnection = new RedisConnection(host, port, -1, password);
                redisConnection.Open().Wait();
                return redisConnection;
            }
            catch (Exception ex)
            {
                Logger.ErrorException("An error occured initializing a RedisConnection.", ex);
                throw;
            }
        }

        private static void ProcessPrivateMessage(string from, string to, string content)
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    if (from.Equals(BotName, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    var privateMessage = new PrivateMessage(from, WebUtility.HtmlDecode(content));
                    var handled = false;

                    foreach (var sprocket in Container.Sprockets)
                    {
                        if (sprocket.CanHandle(privateMessage))
                        {
                            Logger.Info(string.Format("Message received from: '{0}' > '{1}'", from, content));
                            IncrementSprocketUsage(sprocket.Name);
                            sprocket.Handle(privateMessage, JabbRClient);
                            handled = true;
                            break;
                        }
                    }

                    if (!handled)
                    {
                        JabbRClient.PrivateReply(privateMessage.From, "I don't understand that command.");
                    }
                }
                catch (Exception ex)
                {
                    var exception = ex.GetBaseException();
                    Logger.ErrorException("An error occured while processing a private message.", ex);
                }
            });
        }

        private static void ProcessRoomMessage(dynamic message, string room)
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    var from = message.User.Name.Value;
                    var content = message.Content.Value;

                    if (from.Equals(BotName, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    var roomMessage = new RoomMessage(room, from, WebUtility.HtmlDecode(content));

                    foreach (var sprocket in Container.Sprockets)
                    {
                        if (sprocket.CanHandle(roomMessage))
                        {
                            Logger.Info(string.Format("Message received from: '{0}' > '{1}'", from, content));
                            IncrementSprocketUsage(sprocket.Name);
                            sprocket.Handle(roomMessage, JabbRClient);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    var exception = ex.GetBaseException();
                    Logger.ErrorException("An error occured while processing a room message.", exception);
                }
            });
        }

        private static void IncrementSprocketUsage(string sprocket)
        {
            Task.Factory.StartNew(() =>
                {
                    try
                    {
                        using (var connection = GetRedisConnection())
                        {
                            var utcNow = DateTimeOffset.UtcNow;

                            string allTimeHashId = "Jabbot:Statistics:Sprockets:Usage:AllTime";
                            connection.Hashes.SetIfNotExists(0, allTimeHashId, sprocket, "0");
                            var allTimeHashTask = connection.Hashes.Increment(0, allTimeHashId, sprocket);

                            string yearHashId = String.Format("Jabbot:Statistics:Sprockets:Usage:{0:yyyy}", utcNow);
                            connection.Hashes.SetIfNotExists(0, yearHashId, sprocket, "0");
                            var yearHashTask = connection.Hashes.Increment(0, yearHashId, sprocket);

                            string monthHashId = String.Format("Jabbot:Statistics:Sprockets:Usage:{0:yyyyMM}", utcNow);
                            connection.Hashes.SetIfNotExists(0, monthHashId, sprocket, "0");
                            var monthHashTask = connection.Hashes.Increment(0, monthHashId, sprocket);

                            string dayHashId = String.Format("Jabbot:Statistics:Sprockets:Usage:{0:yyyyMMdd}", utcNow);
                            connection.Hashes.SetIfNotExists(0, dayHashId, sprocket, "0");
                            var dayHashTask = connection.Hashes.Increment(0, dayHashId, sprocket);

                            connection.WaitAll(allTimeHashTask, yearHashTask, monthHashTask, dayHashTask);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.ErrorException("An error occured incrementing Sprocket usage statistics.", ex);
                    }
                });
        }
    }
}
