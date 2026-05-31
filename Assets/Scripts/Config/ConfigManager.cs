using System;
using System.IO;
using UnityEngine;

namespace Assets.Game_Manager
{
    public class ConfigManager
    {
        [Serializable]
        public class Config
        {
            public int timerLength;
            public string chatbotFallbackUrl;
            public string playerName;
        }

        [Serializable]
        public class CharacterDescriptions
        {
            public Description[] descriptions;
        }

        [Serializable]
        public class Description
        {
            public string name;
            public string core;
            public string thoughts;
            // "Name: what this NPC knows about them" lines, one per person
            public string social;
        }

        private static readonly Lazy<ConfigManager> _instance =
            new Lazy<ConfigManager>(() => new ConfigManager());

        private Config config;
        private CharacterDescriptions desc;

        private ConfigManager()
        {
            var c_path = "Assets/Config Files/config.json";
            var c_json = File.ReadAllText(c_path);
            config = JsonUtility.FromJson<Config>(c_json);

            var d_path = "Assets/Config Files/character_descriptions.json";
            var d_json = File.ReadAllText(d_path);
            desc = JsonUtility.FromJson<CharacterDescriptions>(d_json);
        }

        public static ConfigManager Instance => _instance.Value;
        public int GetTimerLength() => config.timerLength;
        public string GetChatbotUrl() => config.chatbotFallbackUrl;
        public string GetPlayerName() => string.IsNullOrWhiteSpace(config.playerName) ? "Stranger" : config.playerName;

        public string GetCharacterDescription(int idx) => desc.descriptions[idx].core;
        public Description GetFullCharacterDescription(int idx) => desc.descriptions[idx];
        public string GetCharacterName(int idx) => desc.descriptions[idx].name;
    }
}