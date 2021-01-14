using HollowTwitch.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace HollowTwitch.Utils
{
    /// <summary>
    /// Enable user setting their own translation
    /// usage:
    /// create a file name "localization.txt" with UTF-8 encoding
    /// type for each line in the file: local_language:english
    /// </summary>
    class Localization
    {
        public static string DATA_DIR
        {
            get
            {
                if (SystemInfo.operatingSystemFamily != OperatingSystemFamily.MacOSX)
                {
                    return Path.GetFullPath(Application.dataPath + "/Managed/Mods");
                }
                else
                {
                    return Path.GetFullPath(Application.dataPath + "/Resources/Data/Managed/Mods");
                }
            }
        }
        public Dictionary<string, string> translations;
        private static Localization _instance;
        public static Localization Instance { get
            {
                if (_instance == null)
                    _instance = new Localization();
                return _instance;
            } }
        private Localization()
        {
            var path = Path.Combine(DATA_DIR, "localization.txt");
            if(!File.Exists(path)) // generate default translation,each line is english_cmd:english_cmd
            {
                using (FileStream f = File.Create(path))
                {
                    using (StreamWriter sw = new StreamWriter(f, Encoding.GetEncoding("UTF-8")))
                    {
                        foreach (Command command in TwitchMod.Instance.Processor.Commands)
                        {
                            sw.WriteLine($"{command.Name}:{command.Name}");
                        }
                    }
                }

            }
            
            translations = new Dictionary<string, string>();
            
            using (FileStream fileStream = File.OpenRead(path))
            {
                using (StreamReader sr = new StreamReader(fileStream,Encoding.GetEncoding("UTF-8")))
                {
                    string line;
                    while(!string.IsNullOrEmpty((line = sr.ReadLine()))) //read each line and add to dictionary
                    {
                        var pair = line.Split(':');
                        try
                        {
                            translations.Add(pair[0], pair[1]);
                        }
                        catch
                        {

                        }
                    }
                }
            }
        }
    }
}
