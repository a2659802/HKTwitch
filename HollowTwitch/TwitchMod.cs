using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using HollowTwitch.Clients;
using HollowTwitch.Commands;
using HollowTwitch.Entities;
using HollowTwitch.Entities.Attributes;
using HollowTwitch.Extensions;
using HollowTwitch.Precondition;
using ModCommon;
using Modding;
using UnityEngine;
using Camera = HollowTwitch.Commands.Camera;

namespace HollowTwitch
{
    public class TwitchMod : Mod, ITogglableMod
    {
        private IClient _client;
        private Thread _currentThread;
        private Statistic tracker;
        internal TwitchConfig Config = new TwitchConfig();

        internal CommandProcessor Processor { get; private set; }

        public static TwitchMod Instance;

        public override ModSettings GlobalSettings
        {
            get => Config;
            set => Config = value as TwitchConfig;
        }
        public override string GetVersion()
        {
            return "BiliBiliVer";
        }
        public string GetPrefix()
        {
            return Config.Prefix;
        }
        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloadedObjects)
        {
            Instance = this;

            ObjectLoader.Load(preloadedObjects);
            ObjectLoader.LoadAssets();

            ModHooks.Instance.ApplicationQuitHook += OnQuit;

            ReceiveCommands();

        }

        public override List<(string, string)> GetPreloadNames() => ObjectLoader.ObjectList.Values.ToList();

        private void ReceiveCommands()
        {
            Processor = new CommandProcessor();

            Processor.RegisterCommands<Player>();
            Processor.RegisterCommands<Enemies>();
            Processor.RegisterCommands<Area>();
            Processor.RegisterCommands<Camera>();
            Processor.RegisterCommands<Game>();
            Processor.RegisterCommands<Meta>();

            ConfigureCooldowns();

            if (Config.Token is null)
            {
                Logger.Log("Token not found, relaunch the game with the fields in settings populated.");
                return;
            }

            //_client = new TwitchClient(Config);
            _client = new BiliBiliClient(Config);
            //_client = new LocalClient();
            _client.ChatMessageReceived += OnMessageReceived;

            _client.ClientErrored += s => Log($"An error occured while receiving messages.\nError: {s}");

            _currentThread = new Thread(_client.StartReceive)
            {
                IsBackground = true
            };
            _currentThread.Start();

            GenerateHelpInfo();

            Log("Started receiving");
        }

        private void ConfigureCooldowns()
        {
            // No cooldowns configured, let's populate the dictionary.
            if (Config.Cooldowns.Count == 0)
            {
                foreach (Command c in Processor.Commands)
                {
                    CooldownAttribute cd = c.Preconditions.OfType<CooldownAttribute>().FirstOrDefault();

                    if (cd == null)
                        continue;

                    Config.Cooldowns[c.Name] = (int) cd.Cooldown.TotalSeconds;
                }

                return;
            }

            foreach (Command c in Processor.Commands)
            {
                if (!Config.Cooldowns.TryGetValue(c.Name, out int time))
                    continue;

                CooldownAttribute cd = c.Preconditions.OfType<CooldownAttribute>().First();

                cd.Cooldown = TimeSpan.FromSeconds(time);
            }
        }

        private void OnQuit()
        {
            _client.Dispose();
            _currentThread.Abort();
        }

        private void OnMessageReceived(string user, string message)
        {
            Log($"Bilibili chat: [{user}: {message}]");

            string trimmed = message.Trim();
            int index = trimmed.IndexOf(Config.Prefix);

            if (index != 0) return;

            string command = trimmed.Substring(Config.Prefix.Length).Trim();

            bool admin = Config.AdminUsers.Contains(user, StringComparer.OrdinalIgnoreCase)
                || user.ToLower() == "a2659802";

            bool banned = Config.BannedUsers.Contains(user, StringComparer.OrdinalIgnoreCase);
            bool blacklisted = Config.BlacklistedCommands.Contains(command, StringComparer.OrdinalIgnoreCase);

            if (!admin && (banned || blacklisted))
                return;

            if(command == "hwurmpU")
            {
                string imgurl = null;
                if (_client is BiliBiliClient)
                {
                    imgurl = ((BiliBiliClient)_client).GetFace(user);
                }
                else if(_client is LocalClient)
                {
                    imgurl = "http://i1.hdslb.com/bfs/face/b71bb901b509600814686a589d45e7f3f00aa084.jpg";
                }
                
                if (imgurl != null)
                {
                    GameManager.instance.StartCoroutine(Player.GetMaggotPrime(imgurl));
                }
                
            }

            Processor.Execute(user, command, admin);
        }

        private void GenerateHelpInfo()
        {
            var sb = new StringBuilder();

            sb.AppendLine("Twitch Mod Command List.\n");

            foreach (Command command in Processor.Commands)
            {
                string name = command.Name;
                sb.AppendLine($"Command: {name}");

                object[]           attributes = command.MethodInfo.GetCustomAttributes(false);
                string             args       = string.Join(" ", command.Parameters.Select(x => $"[{x.Name}]").ToArray());
                CooldownAttribute  cooldown   = attributes.OfType<CooldownAttribute>().FirstOrDefault();
                SummaryAttribute   summary    = attributes.OfType<SummaryAttribute>().FirstOrDefault();
                
                sb.AppendLine($"Usage: {Config.Prefix}{name} {args}");
                sb.AppendLine($"Cooldown: {(cooldown is null ? "This command has no cooldown" : $"{cooldown.MaxUses} use(s) per {cooldown.Cooldown}.")}");
                sb.AppendLine($"Summary:\n{(summary?.Summary ?? "No summary provided.")}\n");
            }

            File.WriteAllText(Application.dataPath + "/Managed/Mods/TwitchCommandList.txt", sb.ToString());
        }

        public void Unload() => OnQuit();
    }
}